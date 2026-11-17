using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 构造 H 的 §0.4 七步核对 + §19.4 的 Q1 子配方审计。
/// </summary>
internal static class ConstructedHPaperTests
{
    private static readonly char[] Alphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    /// <summary>
    /// docs §0.8 的 <c>.T..T..c</c>（8 层）：走「构造 H 一次顶针」，
    /// 前态逐列等于 <c>A=.TcccTc.</c>、<c>B=cTTTcT..</c>、<c>E=cccccccc</c>、
    /// <c>N=TPTTTPT.</c>，一次 Pin 后目标列逐格等于目标。
    ///
    /// <para>
    /// <b>期望更新的原因（2026-11-17 第三轮改动 3）</b>：策略优先级改成
    /// ① 构造 H 一次顶针 → ② 构造 G/D 碎晶 → ③ 算法 6 四步 → ④ 多刀 之后，
    /// <c>.T..T..c</c> 由 ① 的 §0.11 九步构造 H 直接完成（A 是 §13.6 七步的
    /// 深间隙抬升形态 <c>.TcccTc.</c>），不再落到 ③ 的 §0.4 论文四步。
    /// 于是不必先执行「提前弱落」再顶针：一次 Pin 就得到目标。
    /// </para>
    /// </summary>
    public static void GeneralSolutionMatchesDocs()
    {
        const string text = ".T..T..c";
        const int cap = 8;

        ShapeDocument target = FromColumns(4, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
        Assert.Equal(AngleConstruction.H, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? ".T..T..c 应可制作");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有 Note");

        AngleWorkStep preStateStep = order.Steps.First(
            step => step.Kind == AngleWorkStepKind.PreState && step.Shape is not null);

        ShapeDocument preState = preStateStep.Shape!;

        // 一次顶针前态：四列逐列一致（A | B | E | N）。
        Assert.Equal(".TcccTc.", ShapeSignature.ColumnText(preState, 0));
        Assert.Equal("cTTTcT..", ShapeSignature.ColumnText(preState, 1));
        Assert.Equal("cccccccc", ShapeSignature.ColumnText(preState, 2));
        Assert.Equal("TPTTTPT.", ShapeSignature.ColumnText(preState, 3));

        // 稳定、四列合法、前态目标列 ≠ 目标。
        Assert.True(ShapeOps.IsStable(preState), "§0.4 的前态应稳定");
        Assert.True(
            ShapeSignature.AllColumnsLegal(preState, out int badColumn),
            "前态第 " + (badColumn + 1) + " 象限不过五项条件");
        Assert.NotEqual(text, ShapeSignature.ColumnText(preState, 0));

        // 一次顶针的中间值仍要在 Limitations 里如实写出（可回查）。
        string limitations = string.Join("\n", order.Limitations);

        Assert.True(
            limitations.Contains("构造 H（§13.6 七步", StringComparison.Ordinal),
            "Limitations 应写明 §0.11 九步 / §13.6 七步的构造 H：" + limitations);

        // 提前弱落 → 一次真实顶针，目标列逐格等于目标。
        AngleWorkStep pinStep = order.Steps.First(
            step => step.Kind == AngleWorkStepKind.Pin);

        ShapeDocument pinInput = order.Steps
            .TakeWhile(step => step.Kind != AngleWorkStepKind.Pin)
            .Last(step => step.Shape is not null)
            .Shape!;

        Assert.True(
            ShapeOps.Same(
                ShapePhysics.Pin(pinInput).Document,
                pinStep.Shape),
            "Pin 步快照应等于真实 ShapePhysics.Pin");
        Assert.Equal(text, ShapeSignature.ColumnText(pinStep.Shape!, 0));

        // 带洞前态要么有整份交换链子配方、要么有 Q1 一层子配方。
        Assert.True(
            order.SubRecipe is not null || order.Q1SubRecipe is not null,
            "带洞前态必须带子配方");
        Assert.True(order.Q1SubRecipe is not null, "A 带洞时必须挂 Q1 子配方");
        Assert.Equal(RecipeKind.Cut, order.Q1SubRecipe!.Kind);
        Assert.Equal(0, order.Q1SubRecipe.Rank);

        ShapeDocument q1Start = order.Q1SubRecipe.Steps[0].Shape!;

        Assert.True(
            AllColumnsTight(q1Start),
            "Q1 子配方的起点必须全是紧列（§5.1 / §6 第 7 条）");
        Assert.Equal(
            ".TcccTc.",
            ShapeSignature.ColumnText(order.Q1SubRecipe.Shape, 0),
            "半破后的 Q1 应等于前态 A");
        Assert.True(
            Replay.Verify(
                order.Q1SubRecipe,
                order.Q1SubRecipe.Shape,
                out IReadOnlyList<string> errors),
            "Q1 子配方应能用真实 Cut 回放：" + string.Join(",", errors));
    }

    /// <summary>
    /// §19.4 审计：≤7 层的所有 S 列里，凡是顶针路线（构造 H）且前态 A 带洞的工序单，
    /// 都必须挂上可回放的 Q1 子配方；实在做不出来的（例如洞与原生晶体相邻，
    /// 一层紧列半破会误伤目标晶体）必须在 <see cref="AngleWorkOrder.Limitations"/>
    /// 里写明「Q1 子配方未找到（一层紧列不可行）」，不许伪造。
    ///
    /// <para>
    /// ≤6 层里正好有两例一层紧列不可行：<c>.T...c</c> 与 <c>.T.T.c</c>——
    /// 前态 A 的洞紧贴在原生晶体下方，把洞填成临时晶体会与原生晶体连成一块，
    /// 半破时会把目标晶体一起炸掉（与 §19.3 对 <c>.T.T..c</c> 的解释同源）。
    /// 7 层再多 10 例同族（<c>.T...c.</c>、<c>.T.T.c.</c> 等），全部如实标注。
    /// </para>
    /// <para>
    /// 同时对 §19.1 的晶下间隙判据做检查：≤6 层里只有 <c>.T...c</c> 的 A 存在
    /// 「目标本身要求」的晶下间隙（目标对应位置没有材料），其余案例都不带这条说明。
    /// </para>
    /// </summary>
    public static void PinOrdersCarryQ1SubRecipe()
    {
        int hOrders = 0;
        int rule17Orders = 0;
        int holed = 0;
        int found = 0;
        int missing = 0;
        int missingSix = 0;
        int invalid = 0;
        int requiredGapNotes = 0;
        int requiredGapNotesSix = 0;
        int avoidableGapNotes = 0;
        var missingCases = new List<string>();
        var invalidCases = new List<string>();
        var requiredGapCases = new List<string>();
        var requiredGapCasesSix = new List<string>();

        for (int layers = 3; layers <= 7; layers++)
        {
            int combinations = (int)Math.Pow(Alphabet.Length, layers);
            var buffer = new char[layers];

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = Alphabet[remaining % Alphabet.Length];
                    remaining /= Alphabet.Length;
                }

                string text = new(buffer);
                ShapeDocument target = BuildColumn(text);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(target, 0))
                {
                    continue;
                }

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (!order.IsComplete ||
                    order.Construction != AngleConstruction.H)
                {
                    continue;
                }

                hOrders++;

                string limitations = string.Join("\n", order.Limitations);

                if (limitations.Contains("§17.1 R1", StringComparison.Ordinal) ||
                    limitations.Contains("结构性降表", StringComparison.Ordinal))
                {
                    rule17Orders++;
                }

                if (limitations.Contains(
                        "晶下间隙是目标本身要求的",
                        StringComparison.Ordinal))
                {
                    requiredGapNotes++;
                    requiredGapCases.Add(text);

                    if (layers <= 6)
                    {
                        requiredGapNotesSix++;
                        requiredGapCasesSix.Add(text);
                    }
                }

                if (limitations.Contains(
                        "目标对应位置有材料",
                        StringComparison.Ordinal))
                {
                    avoidableGapNotes++;
                }

                ShapeDocument preState = order.Steps
                    .FirstOrDefault(step =>
                        step.Kind == AngleWorkStepKind.PreState &&
                        step.Shape is not null)
                    ?.Shape!;

                if (ShapeSignature.IsTightColumn(preState, 0))
                {
                    continue;
                }

                holed++;

                if (order.Q1SubRecipe is null)
                {
                    missing++;

                    if (layers <= 6)
                    {
                        missingSix++;
                    }

                    missingCases.Add(text);

                    if (!limitations.Contains(
                            AngleWorkOrderBuilder.Q1SubRecipeMissingText,
                            StringComparison.Ordinal))
                    {
                        invalid++;
                        invalidCases.Add(text + "：缺 Q1 但 Limitations 没有写明");
                    }

                    continue;
                }

                found++;

                Recipe q1 = order.Q1SubRecipe;

                if (q1.Kind != RecipeKind.Cut ||
                    q1.Rank != 0 ||
                    q1.Steps.Count < 2 ||
                    !AllColumnsTight(q1.Steps[0].Shape!) ||
                    ShapeSignature.ColumnText(q1.Shape, 0) !=
                        ShapeSignature.ColumnText(preState, 0) ||
                    !Replay.Verify(q1, q1.Shape, out _))
                {
                    invalid++;
                    invalidCases.Add(text + "：Q1 子配方不合法或回放失败");
                }
            }
        }

        Console.WriteLine(
            $"  Q1AUDIT hOrders={hOrders} r1={rule17Orders} holed={holed} " +
            $"found={found} missing={missing}（≤6 层 {missingSix}）invalid={invalid}");

        if (missingCases.Count > 0)
        {
            Console.WriteLine("  Q1MISSING " + string.Join(" ", missingCases));
        }

        Console.WriteLine(
            "  Q1GAP required=" + requiredGapNotes +
            "：" + string.Join(" ", requiredGapCases) +
            " avoidable=" + avoidableGapNotes);

        Assert.True(hOrders > 0, "≤7 层应有构造 H 的工序单");
        Assert.True(holed > 0, "构造 H 的前态 A 应至少有一个带洞的样本");
        Assert.Equal(0, invalid, "Q1 子配方审计失败：" + string.Join(" | ", invalidCases));

        // 断言更新的原因（§13.6 / §R1）：通用构造 H 现在保证前态 A 里每颗原生
        // 晶体下方都不是空（P1），因此不再出现「目标本身要求」的晶下间隙，
        // 也不再有 ≤6 层的两例 Q1 缺失（`.T...c` / `.T.T.c` 都改由 P1 前态完成）。
        Assert.Equal(
            0,
            missingSix,
            "≤6 层 Q1 子配方缺失应为 0：" + string.Join(" ", missingCases));
        Assert.True(
            missingCases.Count == 0,
            "≤6 层不应有缺少 Q1 子配方的案例，实际：" +
            string.Join(" ", missingCases));

        // §19.1：P1 硬约束生效后，「晶下间隙」不再出现在构造 H 的前态里。
        Assert.Equal(
            0,
            requiredGapNotes,
            "P1 生效后不应再有「目标本身要求的晶下间隙」：" +
            string.Join(" ", requiredGapCases));
        Assert.Equal(0, requiredGapNotesSix, "≤6 层不应再有晶下间隙说明");
        Assert.Equal(0, avoidableGapNotes, "可避免的晶下间隙必须优先贴住");
    }

    private static bool AllColumnsTight(
        ShapeDocument document)
    {
        for (int part = 0; part < document.PartCount; part++)
        {
            if (!ShapeSignature.IsTightColumn(document, part))
            {
                return false;
            }
        }

        return true;
    }

    private static ShapeDocument BuildColumn(
        string text)
    {
        var document = new ShapeDocument(text.Length, 4);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                'c' => ShapeCell.Crystal('r'),
                'P' => ShapeCell.Pin(),
                'T' => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }

    private static ShapeDocument FromColumns(
        int parts,
        int layers,
        params (int Column, string Text)[] columns)
    {
        var document = new ShapeDocument(layers, parts);

        foreach ((int column, string text) in columns)
        {
            for (int layer = 0; layer < layers; layer++)
            {
                char code = layer < text.Length ? text[layer] : '.';

                document[layer, column] = code switch
                {
                    'c' => ShapeCell.Crystal('r'),
                    'P' => ShapeCell.Pin(),
                    'T' => ShapeCell.Shape('C', 'u'),
                    _ => ShapeCell.Empty()
                };
            }
        }

        return document;
    }
}
