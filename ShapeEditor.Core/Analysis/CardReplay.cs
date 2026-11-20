using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 卡片回放校验：<b>不相信卡片里的任何快照</b>，从输入（或卡内前态）出发，
/// 用真实 <see cref="ShapePhysics"/> 把每一步重算一遍，再逐格比较。
///
/// <para><b>覆盖的步骤</b></para>
/// <list type="bullet">
/// <item><c>碎晶</c>：真实 <see cref="ShapePhysics.Cut"/>（顺转某个象限 → 切 → 转回，
/// 取最小可行旋转量，先西侧后东侧）；</item>
/// <item><c>顶破</c>：真实 <see cref="ShapePhysics.Pin"/>；</item>
/// <item><c>交换</c>：真实 <see cref="ShapeExchange.Exchange"/>，两路输出任意一路
/// 逐格等于该步产物即通过；</item>
/// <item><c>堆叠</c>：真实 <see cref="ShapePhysics.Stack"/>（块的形状随步骤一起保存）；</item>
/// <item><c>下落 / 弱落</c>：真实 <see cref="ShapePhysics.Collapse"/>；</item>
/// <item><c>旋转</c>：真实 <see cref="ShapeOps.Rotate"/>；</item>
/// <item><c>前态</c>：卡内私有起点，只要求它是链的第一步；</item>
/// <item><c>构造 C / 纯支撑交接 / 收尾</c>：结构校验——目标列逐格保持、
/// 产物只剩「左柱 + 目标列 + 右柱」、两根柱子连续接地；</item>
/// <item><c>结晶</c>：结构校验——只允许把空位 / 顶针变成晶体，其余格子一格不动；</item>
/// <item><c>废料</c>：必须是上一次真实 Exchange 的另一路输出。</item>
/// </list>
///
/// <para>
/// 失败时返回逐条原因（第几步、哪种操作、期望与实际差在哪），由
/// <see cref="CardBuilder"/> 决定降级：<c>Verified = false</c> + 清空
/// <see cref="WorkOrderCardView.Inputs"/>。
/// </para>
/// </summary>
public static class CardReplay
{
    /// <summary>可见步骤的操作名（这几种必须真实回放通过）。</summary>
    public static readonly IReadOnlyList<string> VisibleOperations = new[]
    {
        "碎晶",
        "交换",
        "顶破",
        "堆叠"
    };

    /// <summary>卡内步骤里允许出现的全部操作名。</summary>
    public static readonly IReadOnlyList<string> KnownOperations = new[]
    {
        "前态",
        "并半",
        "构造 A",
        "旋转",
        "碎晶",
        "交换",
        "顶破",
        "堆叠",
        "堆叠尾",
        "下落",
        "弱落",
        "结晶",
        "构造 C",
        "纯支撑交接",
        "收尾",
        "废料"
    };

    /// <summary>
    /// 校验一张卡：<c>Inputs → Steps → Output</c> 必须真实可回放且逐格相等。
    /// </summary>
    public static bool Verify(
        WorkOrderCardView card,
        out IReadOnlyList<string> errors)
    {
        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        var collected = new List<string>();
        var recent = new List<ShapeDocument>();

        (ShapeDocument? First, ShapeDocument? Second) lastExchange = (null, null);

        int index = 0;

        foreach (CardStep step in card.Steps)
        {
            index++;

            string label = $"第 {index} 步（{step.Operation}）";

            if (!KnownOperations.Contains(step.Operation))
            {
                collected.Add(label + "未知的操作名。");
                recent.Add(step.Output);
                continue;
            }

            ShapeDocument? input;

            if (step.InputIndex >= 0)
            {
                if (step.InputIndex >= card.Inputs.Count)
                {
                    collected.Add(
                        label + $"要取第 {step.InputIndex + 1} 张输入，但卡片只有 {card.Inputs.Count} 张输入。");
                    input = null;
                }
                else
                {
                    input = card.Inputs[step.InputIndex];
                }
            }
            else
            {
                input = recent.Count > 0 ? recent[^1] : null;
            }

            switch (step.Operation)
            {
                case "前态":
                    if (step.InputIndex >= 0 || index != 1)
                    {
                        collected.Add(label + "前态只能是卡片的第一步，且不取输入。");
                    }

                    break;

                case "并半":
                {
                    /*
                     * 半边卡的起点：把各列的「紧列」（角卡产物）拼进半边前态。
                     * 真实核对：每张输入的 Q1 必须逐格等于本步产物在 InputColumns[i]
                     * 上的那一列——证明半边前态真的用了这些角卡产物，而不是另画一份。
                     */
                    if (index != 1)
                    {
                        collected.Add(label + "并半只能是卡片的第一步。");
                        break;
                    }

                    if (step.InputIndex >= 0)
                    {
                        collected.Add(label + "并半不取单张输入（它要读完整个输入链）。");
                        break;
                    }

                    if (card.Inputs.Count != card.InputColumns.Count)
                    {
                        collected.Add(
                            label + "输入与输入列映射数量不一致（" +
                            card.Inputs.Count + " vs " + card.InputColumns.Count + "）。");
                        break;
                    }

                    bool assemblyOk = true;

                    for (int inputIndex = 0; inputIndex < card.Inputs.Count; inputIndex++)
                    {
                        int column = card.InputColumns[inputIndex];

                        if (column < 0 || column >= step.Output.PartCount)
                        {
                            collected.Add(
                                label + $"第 {inputIndex + 1} 张输入的列号 {column} 越界。");
                            assemblyOk = false;
                            continue;
                        }

                        if (!SameColumnShape(card.Inputs[inputIndex], 0, step.Output, column))
                        {
                            collected.Add(
                                label + $"第 {inputIndex + 1} 张输入的紧列没有逐格进入第 " +
                                (column + 1) + " 象限的前态。");
                            assemblyOk = false;
                        }
                    }

                    if (assemblyOk && card.Inputs.Count == 0)
                    {
                        collected.Add(label + "并半没有任何输入。");
                    }

                    break;
                }

                case "构造 A":
                    if (step.InputIndex >= 0 || index != 1)
                    {
                        collected.Add(label + "构造 A 只能是卡片的第一步，且不取输入。");
                        break;
                    }

                    if (card.Column < 0)
                    {
                        collected.Add(label + "卡片没有记录目标列，无法做结构校验。");
                    }
                    else
                    {
                        if (!ShapeSignature.IsTightColumn(step.Output, card.Column))
                        {
                            collected.Add(label + "目标列不是紧列（构造 A 要求连续接地、没有空洞）。");
                        }

                        if (!IsThreeColumnProduct(step.Output, card.Column, out string tightReason))
                        {
                            collected.Add(label + tightReason);
                        }
                    }

                    break;

                case "旋转":
                    if (input is null)
                    {
                        collected.Add(label + "没有可旋转的输入。");
                    }
                    else if (!ShapeOps.Same(
                                 ShapeOps.Rotate(input, step.Rotation),
                                 step.Output))
                    {
                        collected.Add(
                            label + $"顺转 {step.Rotation} 个象限后与产物不逐格相等。");
                    }

                    break;

                case "碎晶":
                    if (input is null)
                    {
                        collected.Add(label + "没有可切割的输入。");
                    }
                    else if (!TryCut(input, step.Output, out string cutReason))
                    {
                        collected.Add(label + cutReason);
                    }

                    break;

                case "顶破":
                    if (input is null)
                    {
                        collected.Add(label + "没有可顶针的输入。");
                    }
                    else if (!ShapeOps.Same(
                                 ShapePhysics.Pin(input).Document,
                                 step.Output))
                    {
                        collected.Add(
                            label + "真实 Pin 的结果与该步产物不逐格相等" +
                            Diff(input, step.Output));
                    }

                    break;

                case "下落":
                case "弱落":
                    if (input is null)
                    {
                        collected.Add(label + "没有可下落的输入。");
                    }
                    else if (!ShapeOps.Same(
                                 ShapePhysics.Collapse(input).Document,
                                 step.Output))
                    {
                        collected.Add(
                            label + "真实 Collapse 的结果与该步产物不逐格相等");
                    }

                    break;

                case "交换":
                {
                    (ShapeDocument? left, ShapeDocument? right) = ResolveExchangeInputs(
                        card,
                        recent,
                        out string inputError);

                    if (left is null || right is null)
                    {
                        collected.Add(label + inputError);
                        break;
                    }

                    (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
                        ShapeExchange.Exchange(left, right);

                    if (first is null || second is null)
                    {
                        collected.Add(
                            label + "真实交换失败（" + (exchangeError ?? "未知原因") + "）。");
                        break;
                    }

                    lastExchange = (first, second);

                    if (!ShapeOps.Same(first, step.Output) &&
                        !ShapeOps.Same(second, step.Output))
                    {
                        collected.Add(
                            label + "真实 Exchange 的两路输出都不等于该步产物");
                    }

                    break;
                }

                case "堆叠":
                    if (input is null)
                    {
                        collected.Add(label + "没有可堆叠的输入。");
                    }
                    else if (step.Block is null)
                    {
                        collected.Add(label + "缺少堆叠块的形状，无法回放。");
                    }
                    else if (!ShapeOps.Same(
                                 ShapePhysics.Stack(
                                     input,
                                     step.Block,
                                     step.Output.LayerCount).Document,
                                 step.Output))
                    {
                        collected.Add(label + "真实 Stack 的结果与该步产物不逐格相等。");
                    }

                    break;

                case "结晶":
                    if (input is null)
                    {
                        collected.Add(label + "没有可结晶的输入。");
                    }
                    else if (!OnlyFillsEmptyWithCrystal(input, step.Output, out string crystalReason))
                    {
                        collected.Add(label + crystalReason);
                    }

                    break;

                case "构造 C":
                case "纯支撑交接":
                case "收尾":
                {
                    if (card.Column < 0)
                    {
                        collected.Add(label + "卡片没有记录目标列，无法做结构校验。");
                        break;
                    }

                    if (input is null)
                    {
                        /* 允许作为链的第一步（「没有前态的简单角」只有收尾形态）。 */
                        if (index != 1)
                        {
                            collected.Add(label + "没有可交接的输入。");
                            break;
                        }
                    }
                    else if (!SameColumn(input, step.Output, card.Column))
                    {
                        collected.Add(
                            label + $"目标列（第 {card.Column + 1} 象限）被这一步改动了");
                        break;
                    }

                    if (step.Operation == "构造 C" &&
                        !IsThreeColumnProduct(step.Output, card.Column, out string shapeReason))
                    {
                        collected.Add(label + shapeReason);
                    }
                }

                    break;

                case "堆叠尾":
                {
                    if (card.Column < 0)
                    {
                        collected.Add(label + "卡片没有记录目标列，无法做结构校验。");
                        break;
                    }

                    if (input is null)
                    {
                        collected.Add(label + "没有可追加的输入。");
                        break;
                    }

                    if (!IsTopExtension(input, step.Output, card.Column, out string tailReason))
                    {
                        collected.Add(label + tailReason);
                    }
                }

                    break;

                case "废料":
                {
                    if (lastExchange.First is null || lastExchange.Second is null)
                    {
                        collected.Add(label + "前面没有真实交换，无法核对废料。");
                        break;
                    }

                    bool wasteIsFirst = ShapeOps.Same(lastExchange.First, step.Output);
                    bool wasteIsSecond = ShapeOps.Same(lastExchange.Second, step.Output);

                    if (!wasteIsFirst && !wasteIsSecond)
                    {
                        collected.Add(label + "不是上一次真实 Exchange 的任何一路输出。");
                        break;
                    }

                    ShapeDocument? product = recent.Count > 0 ? recent[^1] : null;

                    ShapeDocument? other = wasteIsFirst
                        ? lastExchange.Second
                        : lastExchange.First;

                    if (product is null || !ShapeOps.Same(product, other))
                    {
                        collected.Add(label + "与产物不是同一次真实 Exchange 的两路输出。");
                    }

                    /* 废料是旁支，不占链上的位置。 */
                    continue;
                }
            }

            recent.Add(step.Output);
        }

        if (recent.Count == 0)
        {
            collected.Add("卡片没有任何步骤。");
        }
        else if (!ShapeOps.Same(recent[^1], card.Output))
        {
            collected.Add("最后一步的产物与卡片输出不逐格相等");
        }

        if (card.Inputs.Count > 0)
        {
            bool used = card.Steps.Any(step => step.InputIndex >= 0) ||
                        card.Steps.Count > 0 &&
                        card.Steps[0].Operation is "交换" or "并半";

            if (!used)
            {
                collected.Add("卡片有输入，但没有任何一步用到输入链。");
            }
        }

        errors = collected;
        return collected.Count == 0;
    }

    /// <summary>
    /// 真实切割回放：在 0 … n−1 里找<b>最小</b>的旋转量，使
    /// <c>Rotate(Cut(Rotate(from, r)).West, −r)</c>（再试东侧）逐格等于
    /// <paramref name="to"/>。
    /// </summary>
    public static bool TryCut(
        ShapeDocument from,
        ShapeDocument to,
        out string reason)
    {
        return TryCut(from, to, -1, out reason);
    }

    /// <summary>
    /// 真实切割回放：优先试 <paramref name="preferredRotation"/>，再按 0 … n−1 顺序搜索。
    /// </summary>
    public static bool TryCut(
        ShapeDocument from,
        ShapeDocument to,
        int preferredRotation,
        out string reason)
    {
        int parts = from.PartCount;

        if (preferredRotation >= 0 &&
            preferredRotation < parts &&
            MatchesCut(from, to, preferredRotation))
        {
            reason = string.Empty;
            return true;
        }

        for (int rotation = 0; rotation < parts; rotation++)
        {
            if (MatchesCut(from, to, rotation))
            {
                reason = string.Empty;
                return true;
            }
        }

        reason = "真实 Cut 逐格不等于该步产物（0 … n−1 个旋转量都不成立，" +
                 "期望 " + DescribeColumn(to) + "）。";
        return false;
    }

    /// <summary>一次具体旋转量下的切割回放（先西侧后东侧）。</summary>
    private static bool MatchesCut(
        ShapeDocument from,
        ShapeDocument to,
        int rotation)
    {
        CutResult cut = ShapePhysics.Cut(ShapeOps.Rotate(from, rotation));

        if (!cut.Success)
        {
            return false;
        }

        if (cut.West is not null &&
            ShapeOps.Same(ShapeOps.Rotate(cut.West, -rotation), to))
        {
            return true;
        }

        return cut.East is not null &&
               ShapeOps.Same(ShapeOps.Rotate(cut.East, -rotation), to);
    }

    /// <summary>交换步的两路输入：整卡两输入（没有上一步）或最近两步的产物。</summary>
    private static (ShapeDocument? Left, ShapeDocument? Right) ResolveExchangeInputs(
        WorkOrderCardView card,
        List<ShapeDocument> recent,
        out string error)
    {
        if (recent.Count >= 2)
        {
            error = string.Empty;
            return (recent[^2], recent[^1]);
        }

        if (card.Inputs.Count >= 2)
        {
            error = string.Empty;
            return (card.Inputs[0], card.Inputs[1]);
        }

        error = "交换步需要两路输入（两张卡输入或前两步的产物），现在不足。";
        return (null, null);
    }

    /// <summary>结晶的结构校验：只允许「空位 / 顶针 → 晶体」，其余格子一格不动。</summary>
    private static bool OnlyFillsEmptyWithCrystal(
        ShapeDocument before,
        ShapeDocument after,
        out string reason)
    {
        int layers = Math.Max(before.LayerCount, after.LayerCount);
        int parts = Math.Max(before.PartCount, after.PartCount);

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                ShapeCell from = layer < before.LayerCount && part < before.PartCount
                    ? before[layer, part]
                    : ShapeCell.Empty();

                ShapeCell to = layer < after.LayerCount && part < after.PartCount
                    ? after[layer, part]
                    : ShapeCell.Empty();

                if (ShapeOps.SameCell(from, to))
                {
                    continue;
                }

                bool fill = to.Type == CellType.Crystal &&
                            from.Type is CellType.Empty or CellType.Pin;

                if (!fill)
                {
                    reason = $"第 {layer + 1} 层第 {part + 1} 象限不是「空位 / 顶针 → 晶体」的补晶。";
                    return false;
                }
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 结构校验：形态只剩「左柱 + 目标列 + 右柱」三列，两根柱子连续接地、
    /// 不高于目标列。
    /// </summary>
    public static bool IsCornerProduct(
        ShapeDocument shape,
        int column,
        out string reason)
    {
        return IsThreeColumnProduct(shape, column, out reason);
    }

    /// <summary>
    /// 结构校验：形态只剩「左柱 + 目标列 + 右柱」三列，两根柱子连续接地、
    /// 不高于目标列。
    /// </summary>
    private static bool IsThreeColumnProduct(
        ShapeDocument shape,
        int column,
        out string reason)
    {
        int parts = shape.PartCount;
        int left = ShapeOps.Wrap(column - 1, parts);
        int right = ShapeOps.Wrap(column + 1, parts);

        for (int part = 0; part < parts; part++)
        {
            if (part == column || part == left || part == right)
            {
                continue;
            }

            for (int layer = 0; layer < shape.LayerCount; layer++)
            {
                if (shape[layer, part].Type != CellType.Empty)
                {
                    reason = $"第 {part + 1} 象限还留着辅助列材料（第 {layer + 1} 层）。";
                    return false;
                }
            }
        }

        if (left == column || right == column || left == right)
        {
            reason = $"n = {parts}：放不下两根支撑柱。";
            return false;
        }

        int height = ShapeNormalization.ColumnHeight(shape, column);

        if (height == 0)
        {
            reason = "目标列全空。";
            return false;
        }

        foreach (int pillar in new[] { left, right })
        {
            for (int layer = 0; layer < height; layer++)
            {
                if (shape[layer, pillar].Type != CellType.Shape)
                {
                    reason = $"第 {pillar + 1} 象限第 {layer + 1} 层不是接地支撑柱。";
                    return false;
                }
            }

            for (int layer = height; layer < shape.LayerCount; layer++)
            {
                if (shape[layer, pillar].Type != CellType.Empty)
                {
                    reason = $"第 {pillar + 1} 象限第 {layer + 1} 层高过目标列。";
                    return false;
                }
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 两份形态的两列逐格相等：<paramref name="left"/> 的第
    /// <paramref name="leftColumn"/> 列 vs <paramref name="right"/> 的第
    /// <paramref name="rightColumn"/> 列（类型 / 形状码 / 颜色）。
    /// 「并半」用它核对「输入的 Q1 紧列真的进了前态的那一列」。
    /// </summary>
    public static bool SameColumnShape(
        ShapeDocument? left,
        int leftColumn,
        ShapeDocument? right,
        int rightColumn)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (leftColumn < 0 || leftColumn >= left.PartCount ||
            rightColumn < 0 || rightColumn >= right.PartCount)
        {
            return false;
        }

        int layers = Math.Max(left.LayerCount, right.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell a = layer < left.LayerCount
                ? left[layer, leftColumn]
                : ShapeCell.Empty();

            ShapeCell b = layer < right.LayerCount
                ? right[layer, rightColumn]
                : ShapeCell.Empty();

            if (!ShapeOps.SameCell(a, b))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>两列在 <paramref name="column"/> 这一列上逐格相等。</summary>
    public static bool SameColumn(
        ShapeDocument? left,
        ShapeDocument? right,
        int column)
    {
        if (left is null || right is null)
        {
            return false;
        }

        int layers = Math.Max(left.LayerCount, right.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell a = layer < left.LayerCount
                ? left[layer, column]
                : ShapeCell.Empty();

            ShapeCell b = layer < right.LayerCount
                ? right[layer, column]
                : ShapeCell.Empty();

            if (!ShapeOps.SameCell(a, b))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 构造 C 的「接回尾部」结构校验：目标列在原有高度之上<b>恰好</b>多一格，
    /// 其余列一格不动。
    /// </summary>
    private static bool IsTopExtension(
        ShapeDocument before,
        ShapeDocument after,
        int column,
        out string reason)
    {
        int layers = Math.Max(before.LayerCount, after.LayerCount);
        int parts = Math.Max(before.PartCount, after.PartCount);
        int height = ShapeNormalization.ColumnHeight(before, column);

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                ShapeCell a = layer < before.LayerCount && part < before.PartCount
                    ? before[layer, part]
                    : ShapeCell.Empty();

                ShapeCell b = layer < after.LayerCount && part < after.PartCount
                    ? after[layer, part]
                    : ShapeCell.Empty();

                if (part != column)
                {
                    if (!ShapeOps.SameCell(a, b))
                    {
                        reason = $"第 {part + 1} 象限被改动了（接尾部只允许改目标列）。";
                        return false;
                    }

                    continue;
                }

                if (layer < height)
                {
                    if (!ShapeOps.SameCell(a, b))
                    {
                        reason = $"目标列第 {layer + 1} 层被改动了。";
                        return false;
                    }
                }
                else if (layer == height)
                {
                    if (b.Type == CellType.Empty)
                    {
                        reason = "目标列没有接上新的一层尾部。";
                        return false;
                    }
                }
                else if (b.Type != CellType.Empty)
                {
                    reason = $"目标列第 {layer + 1} 层多出材料（一次只接一层）。";
                    return false;
                }
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>把某列写成 <c>Q1/Q2/…</c> 形式的文本，用于错误信息。</summary>
    public static string DescribeColumn(ShapeDocument shape)
    {
        if (shape is null)
        {
            return "（空）";
        }

        var texts = new List<string>();

        for (int part = 0; part < shape.PartCount; part++)
        {
            texts.Add(
                "Q" + (part + 1) + "=" + ShapeSignature.ColumnText(shape, part));
        }

        return string.Join(" ", texts);
    }

    private static string Diff(
        ShapeDocument from,
        ShapeDocument expected)
    {
        return "（输入 " + DescribeColumn(from) + "）";
    }
}
