using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 工序卡片层级（docs/work-order-card-hierarchy-plan.md §3 / §3.5）的验收用例。
///
/// <para><b>口径</b></para>
/// <list type="number">
/// <item>角卡数量 = 非空角数量（n = 4 最多 4 张，n = 6 最多 6 张）；</item>
/// <item>半边卡 2 张、交换卡 1 张；</item>
/// <item>角卡产物固定 Q1（目标列 Q1、右柱 Q2、左柱 Qn），柱子连续接地；</item>
/// <item>所有 <c>Verified = true</c> 的卡，<c>Inputs → Steps → Output</c> 真实回放逐格通过；</item>
/// <item>校验不过的卡不允许带输入链（<c>Inputs</c> 必须为空，<c>Note</c> 说明卡在哪一步）。</item>
/// </list>
/// </summary>
internal static class CardHierarchyTests
{
    /// <summary>用户给的第一个图形：只有第 1–2 象限有材料（8 层、n = 4）。</summary>
    private const string UserExampleHalfOnly =
        "crCu----:CuCu----:--Cu----:CuCu----:--------:crcr----:P-cr----:Cucr----";

    /// <summary>用户给的第二个图形：两半都填（第 1–4 象限都有材料）。</summary>
    private const string UserExampleBothHalves =
        "crCucrcr:CuCu----:--Cu----:CuCu----:--------:crcr----:P-cr----:Cucr----";

    /// <summary>n = 6 的稠密样例（语料 8 的交换链目标，每列都有材料）。</summary>
    private const string DenseSix = "CrCgCbCcCmCy";

    /// <summary>
    /// 用户给的角1产物短代码（2026-11-20 裁定）：
    /// Q1 = <c>cTccTcPT</c> + 右柱 Q2 / 左柱 Q4，Q3 空。
    /// </summary>
    private const string UserCornerOneOutput =
        "crCu--Cu:CuCu--Cu:crCu--Cu:crCu--Cu:CuCu--Cu:crCu--Cu:P-Cu--Cu:CuCu--Cu";

    /// <summary>
    /// 用户给的角2产物短代码（2026-11-20 裁定）：
    /// Q1 = <c>TTTcTccc</c> + 右柱 Q2 / 左柱 Q4，Q3 空。
    /// </summary>
    private const string UserCornerTwoOutput =
        "CuCu--Cu:CuCu--Cu:CuCu--Cu:crCu--Cu:CuCu--Cu:crCu--Cu:crCu--Cu:crCu--Cu";

    #region NormalizeToQ1

    /// <summary>n = 4：目标列在 Q3 / Q2 时都旋回 Q1，右柱 Q2、左柱 Q4。</summary>
    public static void NormalizeQ1FourQuadrants()
    {
        /* 例 1：目标列在 Q3（column = 2），右柱 Q4、左柱 Q2。 */
        CheckNormalize(
            parts: 4,
            column: 2,
            columnText: "cT.c");

        /* 例 2：目标列在 Q2（column = 1），右柱 Q3、左柱 Q1。 */
        CheckNormalize(
            parts: 4,
            column: 1,
            columnText: "T.cT");
    }

    /// <summary>n = 6：目标列在 Q3 / Q6 时都旋回 Q1，右柱 Q2、左柱 Q6。</summary>
    public static void NormalizeQ1SixQuadrants()
    {
        CheckNormalize(
            parts: 6,
            column: 2,
            columnText: "cT.cT.");

        CheckNormalize(
            parts: 6,
            column: 5,
            columnText: ".T.cTT");
    }

    /// <summary>旋转后逐格正确、支撑柱连续接地，且旋转量是最小的朝向归一化量。</summary>
    private static void CheckNormalize(
        int parts,
        int column,
        string columnText)
    {
        ShapeDocument shape = BuildTriple(parts, column, columnText);

        int rotation = ShapeNormalization.Q1Rotation(parts, column);

        int expectedHeight = 0;

        for (int layer = 0; layer < columnText.Length; layer++)
        {
            if (columnText[layer] != ShapeSignature.EmptyCode)
            {
                expectedHeight = layer + 1;
            }
        }

        Assert.Equal(
            (parts - column) % parts,
            rotation,
            "Q1 归一化的旋转量必须是 (−column) mod n");

        ShapeDocument normalized = ShapeNormalization.NormalizeToQ1(shape, column);

        Assert.Equal(shape.LayerCount, normalized.LayerCount, "层数不能变");
        Assert.Equal(parts, normalized.PartCount, "象限数不能变");

        /*
         * 逐格核对：归一化形态的第 p 个象限必须等于原形态的第 (p − rotation) 个象限，
         * 第 0 位（Q1）就是原目标列。
         */
        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                ShapeCell expected = shape[layer, ShapeOps.Wrap(part - rotation, parts)];
                ShapeCell actual = normalized[layer, part];

                Assert.True(
                    ShapeOps.SameCell(expected, actual),
                    $"归一化第 {layer + 1} 层第 {part + 1} 象限与旋转量 {rotation} 对不上");
            }
        }

        Assert.Equal(
            columnText,
            ShapeSignature.ColumnText(normalized, 0),
            "归一化后 Q1 必须是目标列");

        Assert.True(
            ShapeNormalization.PillarsAreGrounded(
                normalized,
                out int height,
                out string? error),
            "归一化后左右柱必须连续接地：" + error);

        Assert.Equal(expectedHeight, height, "目标列高度应等于列文本长度");

        Assert.Equal(
            ShapeSignature.SolidCode,
            ShapeSignature.ColumnText(normalized, 1)[0],
            "右柱必须落在 Q2");

        Assert.Equal(
            ShapeSignature.SolidCode,
            ShapeSignature.ColumnText(normalized, parts - 1)[0],
            "左柱必须落在 Qn");
    }

    /// <summary>造「左柱 + 目标列 + 右柱」三列形态（原朝向，柱高 = 目标列文本去掉尾部空层）。</summary>
    private static ShapeDocument BuildTriple(
        int parts,
        int column,
        string columnText)
    {
        var shape = new ShapeDocument(columnText.Length, parts);

        int height = 0;

        for (int layer = 0; layer < columnText.Length; layer++)
        {
            shape[layer, column] = columnText[layer] switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };

            if (columnText[layer] != ShapeSignature.EmptyCode)
            {
                height = layer + 1;
            }
        }

        int left = ShapeOps.Wrap(column - 1, parts);
        int right = ShapeOps.Wrap(column + 1, parts);

        for (int layer = 0; layer < height; layer++)
        {
            shape[layer, left] = ShapeCell.Shape('C', 'u');
            shape[layer, right] = ShapeCell.Shape('C', 'u');
        }

        return shape;
    }

    #endregion

    #region 卡片层级

    /// <summary>
    /// 两个用户样例：角卡数量 = 非空角数量，半边卡 2 张，交换卡 1 张，
    /// 而且每张卡都通过真实回放。
    /// </summary>
    public static void CardCountsAndReplayForUserExamples()
    {
        CheckExample(UserExampleHalfOnly, expectedCorners: 2);
        CheckExample(UserExampleBothHalves, expectedCorners: 4);
    }

    private static void CheckExample(
        string code,
        int expectedCorners)
    {
        ShapeDocument target = Parse(code);

        WorkOrderCardSet set = CardBuilder.Build(target);

        Assert.Equal(
            expectedCorners,
            set.CornerCards.Count,
            "角卡数量必须等于非空角数量");

        Assert.Equal(
            NonEmptyColumns(target),
            set.CornerCards.Count,
            "角卡只给非空角画卡");

        Assert.Equal(2, set.HalfCards.Count, "半边卡必须恒为 2 张");
        Assert.True(set.ExchangeCard is not null, "交换卡必须存在");

        Assert.Equal(
            expectedCorners + 3,
            set.Cards.Count,
            "卡片总数 = 角卡 + 2 张半边卡 + 1 张交换卡");

        foreach (WorkOrderCardView card in set.Cards)
        {
            Assert.True(
                card.Verified,
                card.Title + " 应当通过真实回放：" + card.Note);

            Assert.True(
                CardReplay.Verify(card, out IReadOnlyList<string> errors),
                card.Title + " 的 Inputs→Steps→Output 回放失败：" +
                string.Join("；", errors));
        }

        Assert.Equal(set.Cards.Count, set.VerifiedCount, "全部卡片都应通过回放");

        /* 交换卡：两张半边卡的真实 Exchange 必须逐格等于目标。 */
        WorkOrderCardView exchange = set.ExchangeCard!;

        Assert.Equal(2, exchange.Inputs.Count, "交换卡必须有两张输入");
        Assert.Equal("半边1", exchange.InputTitles[0]);
        Assert.Equal("半边2", exchange.InputTitles[1]);
        Assert.True(
            ShapeOps.Same(exchange.Output, target),
            "交换卡的输出必须逐格等于目标");

        (ShapeDocument? first, ShapeDocument? second, _) =
            ShapeExchange.Exchange(exchange.Inputs[0], exchange.Inputs[1]);

        Assert.True(
            ShapeOps.Same(first, target) || ShapeOps.Same(second, target),
            "两张半边输入真实交换后必须得到目标");
    }

    /// <summary>角卡产物固定 Q1：目标列 Q1、右柱 Q2、左柱 Qn，且两柱连续接地。</summary>
    public static void CornerCardOutputsAreFixedAtQ1()
    {
        foreach (string code in new[] { UserExampleHalfOnly, UserExampleBothHalves })
        {
            ShapeDocument target = Parse(code);

            WorkOrderCardSet set = CardBuilder.Build(target);

            Assert.True(set.CornerCards.Count > 0, "样例必须有角卡");

            foreach (WorkOrderCardView card in set.CornerCards)
            {
                int parts = card.Output.PartCount;

                Assert.Equal(parts, target.PartCount, "角卡象限数必须与目标一致");

                if (card.TightColumnProduct)
                {
                    /*
                     * 属于需要碎晶的半边：产物 = 该列在半边前态里的【紧列】+ 双柱，
                     * 不再是目标列本身（用户裁定 2026-11-20）。
                     */
                    Assert.True(
                        ShapeSignature.IsTightColumn(card.Output, 0),
                        card.Title + " 的 Q1 必须是半边前态里的紧列");
                }
                else if (card.LiftCornerProduct)
                {
                    /* v4 半边里的角：Q1 是逆提升后的目标列 + 左右支撑柱。 */
                    Assert.True(
                        card.Verified,
                        card.Title + " 的 v4 角卡应通过真实回放：" + card.Note);

                    HalfWorkOrder half = AngleWorkOrderBuilder.BuildHalf(
                        target,
                        card.Column < target.PartCount / 2
                            ? 0
                            : target.PartCount / 2,
                        8)!;

                    int index = half.Target.Columns
                        .Select((part, at) => (part, at))
                        .First(pair => pair.part == card.Column)
                        .at;

                    Assert.True(
                        ShapeOps.Same(half.CornerProducts[index], card.Output),
                        card.Title + " 的 Q1 必须是 v4 角产物");
                }
                else
                {
                    Assert.Equal(
                        ShapeSignature.ColumnText(target, card.Column),
                        ShapeSignature.ColumnText(card.Output, 0),
                        "独立角卡的 Q1 必须是该角的目标列（" + card.Title + "）");
                }

                Assert.True(
                    ShapeNormalization.PillarsAreGrounded(
                        card.Output,
                        out int height,
                        out string? error),
                    card.Title + " 的左右柱必须连续接地：" + error);

                Assert.True(height > 0, "目标列高度必须大于 0");

                /* 其余象限（Q3）不属于产物。 */
                for (int part = 2; part < parts - 1; part++)
                {
                    Assert.Equal(
                        new string(ShapeSignature.EmptyCode, card.Output.LayerCount),
                        ShapeSignature.ColumnText(card.Output, part),
                        card.Title + " 的第 " + (part + 1) + " 象限不属于产物");
                }
            }
        }
    }

    /// <summary>
    /// 半边卡：真实回放必须能重算到半边目标。
    /// v4 是可见「交换 / 拆柱 / 跌落」，n ≥ 6 的整半边是隐藏「下落」。
    /// </summary>
    public static void HalfCardsReplayWithRealPhysics()
    {
        foreach (string code in new[] { UserExampleHalfOnly, UserExampleBothHalves })
        {
            ShapeDocument target = Parse(code);

            WorkOrderCardSet set = CardBuilder.Build(target);

            foreach (WorkOrderCardView card in set.HalfCards)
            {
                Assert.True(card.Verified, card.Title + " 应通过校验");

                if (card.PreState is not null)
                {
                    Assert.True(
                        CardReplay.VerifyPreState(card, out string preStateReason),
                        card.Title + " 的前态字段不可回放：" + preStateReason);
                }

                if (card.Mode == HalfMode.FromCorners)
                {
                    Assert.Equal(2, card.Inputs.Count, "A 类半边卡必须有两张角卡输入");
                    Assert.False(
                        card.VisibleSteps.Any(step => step.Operation == "碎晶"),
                        "v4 半边卡里不能再有碎晶步（碎晶归角卡）");
                    Assert.True(
                        card.VisibleSteps.Any(step => step.Operation == "交换") &&
                        card.VisibleSteps.Any(step => step.Operation == "拆柱") &&
                        card.VisibleSteps.Any(step => step.Operation == "跌落"),
                        "v4 半边卡的可见步骤应为 交换 / 拆柱 / 跌落");
                }
                else
                {
                    Assert.Equal(
                        0,
                        card.Inputs.Count,
                        "整半边卡（候选 1 / 2）不给输入（前态是卡内私有的）");

                    Assert.True(
                        card.PreState is not null,
                        "整半边卡（候选 1 / 2）必须把前态放进卡片字段");

                    Assert.Equal(
                        "前态",
                        card.PreStateTitle,
                        "整半边卡的前态标题");
                }

                ShapeDocument? previous = null;

                foreach (CardStep step in card.Steps)
                {
                    switch (step.Operation)
                    {
                        case "前态":
                        case "并半":
                            previous = step.Output;
                            break;

                        case "旋转":
                        {
                            ShapeDocument? rotationInput = step.InputIndex >= 0
                                ? card.Inputs[step.InputIndex]
                                : previous;

                            Assert.True(
                                rotationInput is not null,
                                "旋转步必须有输入");

                            Assert.True(
                                ShapeOps.Same(
                                    ShapeOps.Rotate(rotationInput!, step.Rotation),
                                    step.Output),
                                card.Title + " 的旋转步用真实 Rotate 重算不出来");
                            previous = step.Output;
                            break;
                        }

                        case "碎晶":
                            Assert.True(previous is not null, "碎晶步必须有输入");
                            Assert.True(
                                ReplayCutInTest(previous!, step.Output),
                                card.Title + " 的碎晶步用真实 Cut 重算不出来");
                            previous = step.Output;
                            break;

                        case "交换":
                            previous = step.Output;
                            break;

                        case "拆柱":
                            Assert.True(previous is not null, "拆柱步必须有输入");
                            Assert.True(
                                CardReplay.RemovesSupportPillars(
                                    previous!,
                                    step.Output,
                                    out string pillarReason),
                                card.Title + " 的拆柱步结构不对：" + pillarReason);
                            previous = step.Output;
                            break;

                        case "下落":
                        case "跌落":
                            Assert.True(previous is not null, "跌落步必须有输入");
                            Assert.True(
                                ShapeOps.Same(
                                    ShapePhysics.Collapse(previous!).Document,
                                    step.Output),
                                card.Title + " 的跌落步用真实 Collapse 重算不出来");
                            previous = step.Output;
                            break;

                        default:
                            previous = step.Output;
                            break;
                    }
                }

                Assert.True(previous is not null, card.Title + " 必须至少一步");
                Assert.True(
                    ShapeOps.Same(previous, card.Output),
                    card.Title + " 的最后一步必须就是半边产物");
            }
        }
    }

    /// <summary>
    /// v4 半边里的两张角卡：产物 = <b>逆提升后</b>的目标列 + 左右支撑柱（Q1），
    /// 角卡保留自己的碎晶 / 顶针可见步骤；没有 TightColumnProduct 标记。
    /// </summary>
    public static void HalfOwnedCornersUseV4CornerProducts()
    {
        ShapeDocument target = Parse(UserExampleHalfOnly);

        WorkOrderCardSet set = CardBuilder.Build(target);

        WorkOrderCardView corner1 = set.CornerCards.Single(card => card.Column == 0);
        WorkOrderCardView corner2 = set.CornerCards.Single(card => card.Column == 1);

        foreach (WorkOrderCardView corner in new[] { corner1, corner2 })
        {
            Assert.False(
                corner.TightColumnProduct,
                corner.Title + " 属于 v4 半边，不再是「紧列 + 双柱」的碎晶输入");
            Assert.True(corner.Verified, corner.Title + " 应通过真实回放：" + corner.Note);
            Assert.Equal(
                ShapeNormalization.ColumnHeight(corner.Output, 0),
                ShapeNormalization.ColumnHeight(corner.Output, 1),
                corner.Title + " 的右柱应覆盖目标列高度");
        }

        Assert.Equal(
            "cT..TcPT",
            ShapeSignature.ColumnText(corner1.Output, 0),
            "角1 的 Q1 必须是逆提升后的 u");
        Assert.Equal(
            "TTT.Tccc",
            ShapeSignature.ColumnText(corner2.Output, 0),
            "角2 的 Q1 必须是逆提升后的 v");

        /* 两张角卡产物必须就是半边工序里真实用到的 CornerProducts。 */
        HalfWorkOrder half = AngleWorkOrderBuilder.BuildHalf(target, 0, 8)!;

        Assert.True(
            ShapeOps.Same(half.CornerProducts[0], corner1.Output),
            "角1 的产物必须就是半边工序里的 CornerProducts[0]");
        Assert.True(
            ShapeOps.Same(half.CornerProducts[1], corner2.Output),
            "角2 的产物必须就是半边工序里的 CornerProducts[1]");
    }

    /// <summary>
    /// v4 半边1：Inputs = 两张角卡产物；可见步骤 = 交换 → 拆柱 → 跌落（没有碎晶）；
    /// 前态字段 = 拆柱后、跌落前的形状；真实 Collapse 得到输出。
    /// </summary>
    public static void HalfCardUsesCornerInputsAndDropsPillars()
    {
        ShapeDocument target = Parse(UserExampleHalfOnly);

        WorkOrderCardSet set = CardBuilder.Build(target);

        WorkOrderCardView half1 = set.HalfCards[0];

        Assert.Equal(HalfMode.FromCorners, half1.Mode, "半边1 应走 A 类（角产物交换）");
        Assert.True(half1.Verified, "半边1 应通过真实回放：" + half1.Note);

        Assert.Equal(2, half1.Inputs.Count, "半边1 应有两张角卡输入");
        Assert.Equal("角1", half1.InputTitles[0]);
        Assert.Equal("角2", half1.InputTitles[1]);

        WorkOrderCardView corner1 = set.CornerCards.Single(card => card.Column == 0);
        WorkOrderCardView corner2 = set.CornerCards.Single(card => card.Column == 1);

        Assert.True(
            ShapeOps.Same(half1.Inputs[0], corner1.Output),
            "输入[0] 必须就是角1 的产物");
        Assert.True(
            ShapeOps.Same(half1.Inputs[1], corner2.Output),
            "输入[1] 必须就是角2 的产物");

        Assert.False(
            half1.VisibleSteps.Any(step => step.Operation == "碎晶"),
            "半边1 的可见步骤里不能再有碎晶");
        Assert.Equal(
            "交换 → 拆柱 → 跌落",
            string.Join(" → ", half1.VisibleSteps.Select(step => step.Operation)),
            "半边1 的可见步骤必须是 交换 / 拆柱 / 跌落");

        Assert.True(half1.PreState is not null, "半边1 必须把「拆柱后」放进前态字段");
        Assert.Equal(
            "拆柱后（跌落前）",
            half1.PreStateTitle,
            "半边1 的前态标题");

        Assert.True(
            CardReplay.VerifyPreState(half1, out string preStateReason),
            "半边1 的前态字段不可回放：" + preStateReason);

        ShapeDocument settled = ShapePhysics.Collapse(half1.PreState!).Document!;

        Assert.True(
            ShapeOps.Same(settled, half1.Output),
            "半边1 的前态经一次真实 Collapse 必须得到输出");

        ShapeDocument expected = ShapeOps.Project(target, 0, target.PartCount / 2);

        Assert.True(
            ShapeOps.Same(half1.Output, expected),
            "半边1 输出必须等于目标的前 k 列");
    }

    /// <summary>
    /// 独立角（不参与任何需要碎晶的半边）保持原样：
    /// 角卡 = 紧列 → 碎晶 → 目标角 + 双柱，可见步骤里仍有真实碎晶。
    ///
    /// <para>
    /// 注意：<c>.T.c</c> 这个单列本身是构造 H（可见步骤是「顶破」）——它没有碎晶，
    /// 也不属于任何需要碎晶的半边，所以卡片原样保留。真正验证「独立角保留碎晶」
    /// 用的是同样单列、但真实工序是碎晶的 <c>cT.T.ccc</c>。
    /// </para>
    /// </summary>
    public static void IndependentCornersKeepCrystalBreak()
    {
        ShapeDocument target = BuildColumn("cT.T.ccc", parts: 4);

        WorkOrderCardSet set = CardBuilder.Build(target);

        WorkOrderCardView corner = set.CornerCards.Single();

        Assert.False(corner.TightColumnProduct, "独立角不应被改成紧列产物");
        Assert.True(corner.Verified, "独立角应通过真实回放：" + corner.Note);

        CardStep[] cuts = corner.VisibleSteps
            .Where(step => step.Operation == "碎晶")
            .ToArray();

        Assert.True(cuts.Length >= 1, "独立角必须保留碎晶步");

        ShapeDocument? previous = null;

        foreach (CardStep step in corner.Steps)
        {
            if (step.Operation == "碎晶")
            {
                Assert.True(previous is not null, "碎晶步必须有输入");
                Assert.True(
                    ReplayCutInTest(previous!, step.Output),
                    "独立角的碎晶步必须用真实 Cut 重算出来");
            }

            previous = step.Output;
        }

        /* `.T.c`：独立角、构造 H（顶破），不属于任何需要碎晶的半边，卡片保持不变。 */
        WorkOrderCardSet dotTc = CardBuilder.Build(BuildColumn(".T.c", parts: 4));

        WorkOrderCardView dotCorner = dotTc.CornerCards.Single();

        Assert.False(dotCorner.TightColumnProduct, ".T.c 不应被改成紧列产物");
        Assert.True(dotCorner.Verified, ".T.c 角卡应通过真实回放：" + dotCorner.Note);
        Assert.True(
            dotCorner.VisibleSteps.Any(step => step.Operation == "顶破"),
            ".T.c 的真实工序是构造 H（顶破），卡片应原样保留");
    }

    /// <summary>
    /// 只有 v4 半边里的角才用 CornerProducts（逆提升后 + 支撑柱）；
    /// 两侧都能做 v4 时两张半边卡都是 FromCorners，角卡都走角产物路径。
    /// </summary>
    public static void OnlyV4HalfCornersUseCornerProducts()
    {
        ShapeDocument target = Parse(UserExampleBothHalves);

        WorkOrderCardSet set = CardBuilder.Build(target);

        Assert.Equal(HalfMode.FromCorners, set.HalfCards[0].Mode, "半边1 走 v4");
        Assert.Equal(HalfMode.FromCorners, set.HalfCards[1].Mode, "半边2 也走 v4");

        Assert.True(
            set.CornerCards.All(card => !card.TightColumnProduct),
            "v4 角卡不再使用「紧列 + 双柱」的碎晶输入");

        Assert.True(
            set.CornerCards.All(card => card.Verified),
            "所有角卡都应通过真实回放");

        foreach (WorkOrderCardView card in set.Cards)
        {
            Assert.True(card.Verified, card.Title + " 应通过真实回放：" + card.Note);
        }
    }

    /// <summary>n = 6：角卡 6 张、半边卡 2 张、交换卡 1 张，全部通过真实回放。</summary>
    public static void SixQuadrantCardsAreComplete()
    {
        ShapeDocument target = Parse(DenseSix);

        WorkOrderCardSet set = CardBuilder.Build(target);

        Assert.Equal(6, set.CornerCards.Count, "n = 6 应有 6 张角卡");
        Assert.Equal(2, set.HalfCards.Count, "半边卡必须恒为 2 张");
        Assert.True(set.ExchangeCard is not null, "交换卡必须存在");

        foreach (WorkOrderCardView card in set.Cards)
        {
            Assert.True(
                card.Verified,
                card.Title + " 应当通过真实回放：" + card.Note);

            Assert.True(
                CardReplay.Verify(card, out IReadOnlyList<string> errors),
                card.Title + " 回放失败：" + string.Join("；", errors));
        }

        Assert.True(
            ShapeOps.Same(set.ExchangeCard!.Output, target),
            "交换卡输出必须等于目标");
    }

    /// <summary>
    /// 校验不过的卡必须降级：<c>Verified = false</c>、<c>Inputs</c> 清空、
    /// <c>Note</c> 写清卡在哪一步。
    /// </summary>
    public static void FailedCardsDropInputsAndExplain()
    {
        /* 顶针下方是空位：角不合法 ⇒ 角卡 / 半边卡 / 交换卡都必须降级。 */
        ShapeDocument target = Parse("--------:P-------:Cu------");

        WorkOrderCardSet set = CardBuilder.Build(target);

        var cards = set.Cards;

        Assert.True(cards.Count > 0, "即使在不可制作的图形上也要给出卡片（并标红）");

        bool anyFailed = false;

        foreach (WorkOrderCardView card in cards)
        {
            if (card.Verified)
            {
                continue;
            }

            anyFailed = true;

            Assert.Equal(0, card.Inputs.Count, card.Title + " 校验不过时不能画输入链");
            Assert.True(
                !string.IsNullOrEmpty(card.Note) &&
                card.Note!.StartsWith("未通过：", StringComparison.Ordinal),
                card.Title + " 的 Note 必须写「未通过：…」");
        }

        Assert.True(anyFailed, "悬空晶体的卡片不应该全部通过校验");
    }

    #endregion

    #region 工具

    /// <summary>测试侧独立写一遍真实切割回放（不调用 <see cref="CardReplay"/>）。</summary>
    private static bool ReplayCutInTest(
        ShapeDocument from,
        ShapeDocument to)
    {
        for (int rotation = 0; rotation < from.PartCount; rotation++)
        {
            CutResult cut = ShapePhysics.Cut(
                ShapeOps.Rotate(from, rotation));

            if (!cut.Success)
            {
                continue;
            }

            if (cut.West is not null &&
                ShapeOps.Same(ShapeOps.Rotate(cut.West, -rotation), to))
            {
                return true;
            }

            if (cut.East is not null &&
                ShapeOps.Same(ShapeOps.Rotate(cut.East, -rotation), to))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按「一列文本 + 象限数」造一个只有第 1 象限有材料的图形。</summary>
    private static ShapeDocument BuildColumn(
        string text,
        int parts)
    {
        var document = new ShapeDocument(text.Length, parts);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }

    private static int NonEmptyColumns(
        ShapeDocument shape)
    {
        int count = 0;

        for (int part = 0; part < shape.PartCount; part++)
        {
            for (int layer = 0; layer < shape.LayerCount; layer++)
            {
                if (shape[layer, part].Type != CellType.Empty)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static ShapeDocument Parse(
        string code)
    {
        Assert.True(
            ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError),
            "短代码尺寸推断失败：" + inferError);

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        Assert.True(parsed.Success, parsed.Error ?? "短代码解析失败");

        return parsed.Document!;
    }

    #endregion
}
