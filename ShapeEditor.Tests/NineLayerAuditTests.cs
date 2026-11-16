using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 9 层穷举审计与 docs §21.3 两个缺口的回归（<c>T..cT.T.c</c> / <c>T.T.cccTc</c>）。
///
/// <para>
/// 口径与 <see cref="AngleAuditTests.ExhaustiveSevenEightLayerAudit"/> 一致：
/// 字母表 <c>. T P c</c>、单角、<see cref="AngleWorkOrderBuilder.Build"/> 默认参数，
/// 统计 S 列数、生成失败数、假阳性、<see cref="AngleConstruction.FillBreak"/> 例数。
/// </para>
/// <para>
/// 9 层共 4^9 = 262144 列，全枚举比 7 / 8 层多一个数量级；本测试仍然跑全量，
/// 但只对「五项 ✔ 且属于 S」的列调用生成器（36508 个），把耗时压在 20 s 量级。
/// </para>
/// </summary>
internal static class NineLayerAuditTests
{
    private const int Parts = 4;

    private static readonly char[] Alphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    /// <summary>
    /// docs §21.3 的两个缺口：9 层、D ✔、底层是普通格。
    ///
    /// <para>
    /// 修复口径（算法 6）：<b>先做底块提升</b>（最下方连续普通块抬一格 → A 第 1 层为空，
    /// 顶针不会补针），再按 §20.3 ⑤ 生成 A | B | E | N，一次真实
    /// <see cref="ShapePhysics.Pin"/> 逐格验收。本测试独立重放：
    /// </para>
    /// <list type="number">
    /// <item>前态四列：目标列 A 的第 1 层为空（底块已抬起），B / E / N 都是紧列、过五项条件；</item>
    /// <item>真实 Pin：前态的目标列逐格等于目标（提升后的批量下落与底块恢复都由真实物理完成）；</item>
    /// <item>Pin 后桥柱晶体必须全部消失；</item>
    /// <item>前态目标列带洞时必须挂子配方（§6 第 7 条），且不得退回
    /// <see cref="AngleConstruction.FillBreak"/>。</item>
    /// </list>
    /// </summary>
    public static void TwoNineLayerGapsUseLiftedConstructedH()
    {
        var cases = new (string Text, bool ExpectHoles)[]
        {
            ("T..cT.T.c", true),
            ("T.T.cccTc", true)
        };

        foreach ((string text, bool expectHoles) in cases)
        {
            ShapeDocument target = BuildColumn(text);

            ColumnCheck check = ShapeSignature.CheckColumn(text);

            Assert.True(check.IsLegal, text + " 应是合法列");
            Assert.True(
                PillarSCriterion.BottomResourceD(text),
                text + " 的 D 应成立（算法 6）");
            Assert.True(
                PillarSCriterion.IsPillarColumn(text),
                text + " 应属于 9 层的 S");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);

            Assert.True(
                order.IsComplete,
                text + " 应完成，实际：" + (order.Note ?? "<null>"));
            Assert.Equal(
                AngleAlgorithm.Algorithm6,
                order.Algorithm,
                text + " 应走算法 6");
            Assert.Equal(
                AngleConstruction.H,
                order.Construction,
                text + " 应走构造 H（底块提升）");
            Assert.False(
                order.Construction == AngleConstruction.FillBreak,
                text + " 不得退回 FillBreak");
            Assert.Equal(1, order.Rank, text + " 应只有一次顶针");
            Assert.True(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                text + " 必须有真实 Pin 步");

            AngleWorkStep? preStateStep = order.Steps.FirstOrDefault(
                step => step.Kind == AngleWorkStepKind.PreState &&
                        step.Shape is not null);

            Assert.True(preStateStep?.Shape is not null, text + " 应给出前态");

            ShapeDocument preState = preStateStep!.Shape!;

            // ① 底块提升：A 的第 1 层为空，Pin 不会补针。
            Assert.Equal(
                CellType.Empty,
                preState[0, order.Column].Type,
                text + " 的前态目标列第 1 层必须为空（底块已抬起）");

            // ② 前态四列：每列过五项条件；B / E / N 必须是紧列。
            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out int badColumn),
                text + $" 的前态第 {badColumn + 1} 象限不合法");

            for (int part = 0; part < preState.PartCount; part++)
            {
                if (part != order.Column)
                {
                    Assert.True(
                        ShapeSignature.IsTightColumn(preState, part),
                        text + $" 的前态辅助列 Q{part + 1} 应是紧列");
                }
            }

            // ③ 真实 Pin 后再按论文四步的弱落恢复底块，目标列逐格等于目标。
            PhysicsResult pin = ShapePhysics.Pin(preState);

            Assert.True(pin.Success, text + " 的真实 Pin 失败：" + pin.Error);
            Assert.True(pin.Document is not null, text + " 的真实 Pin 没有产物");

            ShapeDocument pinned = pin.Document!;

            AngleWorkStep pinStep = order.Steps.First(
                step => step.Kind == AngleWorkStepKind.Pin);

            Assert.True(
                ShapeOps.Same(pinned, pinStep.Shape),
                text + " 的 Pin 步快照应等于真实 Pin");

            AngleWorkStep? finalRestore = order.Steps.LastOrDefault(
                step => step.Kind == AngleWorkStepKind.WeakDrop);

            ShapeDocument restored = finalRestore?.Shape ?? pinned;

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(restored, order.Column),
                text + " 的弱落恢复后目标列应逐格等于目标");

            Assert.True(
                ShapeOps.Same(
                    ShapeOps.Project(restored, order.Column, 1),
                    ShapeOps.Project(target, order.Column, 1)),
                text + " 的弱落恢复结果不是目标角");

            // ④ 顶针后晶体桥必须全部删除。
            foreach (int auxiliary in order.AuxiliaryColumns)
            {
                for (int layer = 0; layer < pinned.LayerCount; layer++)
                {
                    Assert.False(
                        pinned[layer, auxiliary].Type == CellType.Crystal,
                        text + $" 顶针后 Q{auxiliary + 1} 第 {layer + 1} 层仍有晶体桥");
                }
            }

            // ⑤ 目标列带洞 ⇒ 必须挂子配方（§6 第 7 条 / §19.4）。
            bool holes = !ShapeSignature.IsTightColumn(preState, order.Column);

            Assert.Equal(expectHoles, holes, text + " 的前态目标列应带洞");

            if (holes)
            {
                Assert.True(
                    order.TargetColumnHasSubRecipe || order.SubRecipe is not null,
                    text + " 的前态目标列带洞却没有子配方证明可造");
            }

            if (order.Q1SubRecipe is not null)
            {
                Assert.True(
                    Replay.Verify(order.Q1SubRecipe, null, out IReadOnlyList<string> q1Errors),
                    text + " 的 Q1 子配方回放失败：" + string.Join("；", q1Errors));

                // Q1 子配方只覆盖目标列：半破后的 QC 列应逐格等于前态的 A。
                Assert.Equal(
                    ShapeSignature.ColumnText(preState, order.Column),
                    ShapeSignature.ColumnText(order.Q1SubRecipe.Shape, order.Column),
                    text + " 的 Q1 子配方没有做出前态的 A 列");
            }

            // ⑥ 末步逐格等于目标。
            AngleWorkStep? targetStep = order.Steps
                .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target);

            Assert.True(
                targetStep?.Shape is not null &&
                ShapeOps.Same(targetStep.Shape, target),
                text + " 的末步没有逐格等于目标");

            Console.WriteLine(
                $"  NINEGAP {text} ok algorithm={order.Algorithm} " +
                $"construction={order.Construction} rank={order.Rank} " +
                $"A={ShapeSignature.ColumnText(preState, order.Column)} " +
                $"B={ShapeSignature.ColumnText(preState, (order.Column + 1) % Parts)} " +
                $"E={ShapeSignature.ColumnText(preState, (order.Column + 2) % Parts)} " +
                $"N={ShapeSignature.ColumnText(preState, (order.Column + 3) % Parts)} " +
                $"holes={holes} q1={order.Q1SubRecipe is not null} " +
                $"sub={order.SubRecipe is not null} " +
                $"segments={DescribeCrystalSegments(text)}");
        }
    }

    /// <summary>
    /// 9 层全枚举审计：S 列数 36508、失败 0、假阳性 0、FillBreak 0。
    ///
    /// <para>
    /// 修复前 docs §21.3 记录 2 个失败（<c>T..cT.T.c</c>、<c>T.T.cccTc</c>）。
    /// 修复后两个角都走「底块提升 + 构造 H」，本审计要求失败数回到 0，且
    /// 不得有任何 S 列退回 <see cref="AngleConstruction.FillBreak"/>。
    /// </para>
    /// </summary>
    public static void ExhaustiveNineLayerAudit()
    {
        const int layers = 9;

        var failures = new List<string>();
        var falsePositives = new List<string>();
        var fillBreak = new List<string>();
        var constructions = new SortedDictionary<string, int>(StringComparer.Ordinal);

        int combinations = 1;

        for (int index = 0; index < layers; index++)
        {
            combinations *= Alphabet.Length;
        }

        var buffer = new char[layers];
        int legal = 0;
        int s = 0;
        int success = 0;

        var watch = Stopwatch.StartNew();

        for (int code = 0; code < combinations; code++)
        {
            int remaining = code;

            for (int layer = 0; layer < layers; layer++)
            {
                buffer[layer] = Alphabet[remaining % Alphabet.Length];
                remaining /= Alphabet.Length;
            }

            string text = new(buffer);

            if (!ShapeSignature.CheckColumn(text).IsLegal)
            {
                continue;
            }

            legal++;

            if (!PillarSCriterion.IsPillarColumn(text))
            {
                continue;
            }

            s++;

            ShapeDocument target = BuildColumn(text);

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

            if (!order.IsComplete)
            {
                failures.Add(text + "：" + (order.Note ?? "未给出原因"));
                continue;
            }

            success++;

            string key = order.Construction.ToString();

            constructions[key] = constructions.TryGetValue(key, out int count)
                ? count + 1
                : 1;

            if (order.Construction == AngleConstruction.FillBreak)
            {
                fillBreak.Add(text);
            }

            if (order.HasIllegalPreState(out int preStateIndex, out int badColumn))
            {
                falsePositives.Add(
                    text + $"：第 {preStateIndex + 1} 条前态第 {badColumn + 1} 象限不合法");
                continue;
            }

            AngleWorkStep? targetStep = order.Steps
                .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target);

            if (order.Kind == AngleBuildKind.NeedsCrystalBreak &&
                (targetStep?.Shape is null ||
                 !ShapeOps.Same(targetStep.Shape, target)))
            {
                falsePositives.Add(text + "：末步没有逐格等于目标");
            }
        }

        watch.Stop();

        Console.WriteLine(
            $"  NINEAUDIT layers={layers} total={combinations} legal={legal} " +
            $"s={s} success={success} failures={failures.Count} " +
            $"falsePositives={falsePositives.Count} fillBreak={fillBreak.Count} " +
            "constr=" + string.Join(
                " ",
                constructions.Select(pair => pair.Key + "=" + pair.Value)) +
            $" auditMs={watch.ElapsedMilliseconds}");

        if (failures.Count > 0)
        {
            Console.WriteLine(
                "  NINEFAILCOLS " + string.Join(
                    " ",
                    failures.Select(line => line.Split('：')[0])));
            Console.WriteLine("  NINEFAILNOTES " + string.Join(" | ", failures));
        }

        if (falsePositives.Count > 0)
        {
            Console.WriteLine(
                "  NINE假阳性：" + string.Join(" | ", falsePositives));
        }

        Assert.Equal(
            262144,
            combinations,
            "9 层枚举列数应为 4^9 = 262144");
        Assert.Equal(36508, s, "9 层 S 列数应为 36508");
        Assert.Equal(36508, success, "9 层 S 列生成成功数应仍为 36508");
        Assert.True(
            failures.Count == 0,
            $"9 层 S 列生成失败 {failures.Count} 个（docs §21.3 修复前 2 个）：" +
            string.Join(" | ", failures));
        Assert.True(
            falsePositives.Count == 0,
            $"9 层出现 {falsePositives.Count} 个假阳性：" +
            string.Join(" | ", falsePositives));
        Assert.True(
            fillBreak.Count == 0,
            "9 层 S 列不得退回 FillBreak：" + string.Join(" ", fillBreak));
    }

    /// <summary>把列文本里的连续晶体段写成 <c>L5–L7(3)</c>（层号 1 起）。</summary>
    private static string DescribeCrystalSegments(string text)
    {
        var segments = new List<string>();

        for (int layer = 0; layer < text.Length; layer++)
        {
            if (text[layer] != ShapeSignature.CrystalCode)
            {
                continue;
            }

            int bottom = layer;

            while (layer + 1 < text.Length &&
                   text[layer + 1] == ShapeSignature.CrystalCode)
            {
                layer++;
            }

            segments.Add(
                bottom == layer
                    ? $"L{bottom + 1}(1)"
                    : $"L{bottom + 1}–L{layer + 1}({layer - bottom + 1})");
        }

        return segments.Count == 0 ? "无" : string.Join("、", segments);
    }

    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, Parts);

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
}
