using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 构造 H（§13.6 七步 + 三处更正）的<b>诊断</b>入口：把每一步的中间值摊开打印，
/// 只用于失败诊断文档（<c>docs/angle-work-order-uncovered-h.md</c>），不参与生成。
///
/// <para>
/// 它走的是同一条生成链：<c>BuildPreStateH</c> → <c>BuildDescents</c> →
/// <c>BuildAuxColumns</c> → 真实 <see cref="ShapePhysics.Pin"/>，
/// 所以失败清单与 <see cref="AngleWorkOrderBuilder.Build"/> 在两段开关下的口径一致。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>一条构造 H 失败例的 7 步诊断文本。</summary>
    public static string DescribeConstructedH(
        ShapeDocument target,
        int column,
        int layers)
    {
        var report = new List<string>();
        var targetCodes = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            targetCodes[layer] = CodeOf(
                layer < target.LayerCount
                    ? target[layer, column]
                    : ShapeCell.Empty());
        }

        string targetText = new(targetCodes);

        report.Add($"## {targetText}（{layers} 层）");
        report.Add(string.Empty);
        report.Add("- 目标列（1 起）：" + DescribeColumn(targetCodes));

        // 第 2 步的前置：目标里的针只能出现在第 1 层。
        for (int layer = 1; layer < layers; layer++)
        {
            if (targetCodes[layer] == ShapeSignature.PinCode)
            {
                report.Add(
                    $"- 第 2 步结果 A：**没有生成**（第 {layer + 1} 层的针不在第 1 层：" +
                    "构造 H 的顶针只能补出第 1 层的针）");
                report.Add("- 第 3–7 步：未走到");
                report.Add("- 判定：卡在 第 2 步（目标里的针）。");
                return string.Join("\n", report);
            }
        }

        HPreState? plan = BuildPreStateH(
            targetCodes,
            layers,
            out string? buildFailure,
            out string? buildStage);

        if (plan is null)
        {
            report.Add("- 第 2 步结果 A：**没有生成**（" + buildFailure + "）");
            report.Add("- 第 3–7 步：未走到");
            report.Add("- 判定：卡在 " + (buildStage ?? "第 2 步") + "。");
            return string.Join("\n", report);
        }

        string aText = new(plan.A);

        report.Add("- 第 2 步结果 A（逐格）：`" + aText + "`");
        report.Add("- A 逐格来源：" + DescribeA(plan));

        foreach (string note in plan.Notes)
        {
            report.Add("- " + note);
        }

        (HashSet<int> iLayers, HashSet<int> jLayers) = BuildDescents(
            plan,
            targetCodes,
            layers,
            extendTemporary: false,
            out string? descentFailure);

        if (descentFailure is not null)
        {
            report.Add("- 第 5 步降表：**没有生成**（" + descentFailure + "）");
            report.Add("- 第 6–7 步：未走到");
            report.Add("- 判定：卡在 第 5 步（降表）。");
            return string.Join("\n", report);
        }

        report.Add(
            "- 第 5 步降表（层号用顶针后）：" +
            (plan.Descents.Count > 0
                ? string.Join(
                    "、",
                    plan.Descents.Select(pair =>
                        $"{pair.S}→{pair.D}（通道 [{pair.D}, {pair.S - 1}]，" +
                        $"长 {pair.S - pair.D}）"))
                : "无"));
        report.Add(
            "- 第 5 步 I / J（顶针前层号 = B / N 的格子位置）：I = {" +
            string.Join(",", iLayers.Order()) + "}，J = {" +
            string.Join(",", jLayers.Order()) + "}；" +
            "顶针后 I = {" + string.Join(",", iLayers.Order().Select(layer => layer + 1)) +
            "}，J = {" + string.Join(",", jLayers.Order().Select(layer => layer + 1)) + "}");

        (char[] bCodes, char[] eCodes, char[] nCodes) = BuildAuxColumns(
            iLayers,
            jLayers,
            layers,
            plan.BTop,
            plan.NTop);

        report.Add(
            "- 第 6 步辅助列（改正 2：B 最高有效层 = " + plan.BTop +
            " = 最高源层 − 1；N 最高有效层 = " + plan.NTop +
            " = 最高原生晶体目标层 − 1；再往上留空）：B = `" + new string(bCodes) +
            "`，E = `" + new string(eCodes) + "`，N = `" + new string(nCodes) + "`");

        // 按 I / J / 高度装配前态，跑真实 Pin。
        char color = CrystalColor(target, column);
        var preState = new ShapeDocument(layers, 4);

        for (int layer = 0; layer < layers; layer++)
        {
            preState[layer, column] = CellFromCode(plan.A[layer], color);
        }

        int bColumn = Wrap(column + 1, 4);
        int eColumn = Wrap(column + 2, 4);
        int nColumn = Wrap(column - 1, 4);

        for (int layer = 0; layer < layers; layer++)
        {
            preState[layer, bColumn] = CellFromCode(bCodes[layer], color);
            preState[layer, eColumn] = CellFromCode(eCodes[layer], color);
            preState[layer, nColumn] = CellFromCode(nCodes[layer], color);
        }

        string preA = ShapeSignature.ColumnText(preState, column);

        if (IsRejectedPreState(target, column, preState, out int badColumn))
        {
            report.Add("- 第 6 步验收：前态第 " + (badColumn + 1) + " 象限不合法（" +
                       DescribeIllegalPreState(preState, badColumn) + "）");
            report.Add("- 判定：卡在 第 6 步（辅助列）。");
            return string.Join("\n", report);
        }

        PhysicsResult pin = ShapePhysics.Pin(preState);

        if (!pin.Success || pin.Document is null)
        {
            report.Add("- 第 7 步 Pin：失败（" + (pin.Error ?? "未知原因") + "）");
            report.Add("- 判定：卡在 第 7 步（真实 Pin）。");
            return string.Join("\n", report);
        }

        ShapeDocument pinned = pin.Document;
        string pinnedText = ShapeSignature.ColumnText(pinned, column);
        bool matched = MatchesTargetColumn(
            pinned,
            target,
            column,
            layers,
            out string? difference);

        report.Add(
            "- 第 7 步 Pin 后目标列：" + (matched ? "与目标逐格相等 ✔" : "与目标不符 ✘") +
            "（实际 `" + pinnedText + "`，目标 `" + targetText + "`）");

        if (!matched)
        {
            report.Add("- 第 7 步差异：" + difference);
        }

        var leftovers = new List<int>();

        for (int layer = 0; layer < layers; layer++)
        {
            if (pinned[layer, bColumn].Type == CellType.Crystal)
            {
                leftovers.Add(layer + 1);
            }
        }

        if (leftovers.Count > 0)
        {
            report.Add("- 第 7 步残留晶体桥（B）：L" +
                       string.Join(",L", leftovers) + " 没有碎掉");
        }

        report.Add(
            "- A 前态列：`" + preA + "`（目标列本身）" +
            (ShapeSignature.IsTightColumn(preState, column) ? "，紧列" : "，带洞（需子配方）"));

        if (matched && leftovers.Count == 0)
        {
            report.Add("- 判定：通过（两段开关下应可完成）。");
        }
        else
        {
            report.Add("- 判定：卡在 第 7 步（真实 Pin 回放与目标不符）。");
        }

        return string.Join("\n", report);
    }

    private static string DescribeColumn(char[] codes)
    {
        var parts = new List<string>();

        for (int layer = 0; layer < codes.Length; layer++)
        {
            string name = codes[layer] switch
            {
                ShapeSignature.PinCode => "针",
                ShapeSignature.SolidCode => "普通格",
                ShapeSignature.CrystalCode => "晶体",
                _ => null!
            };

            if (name is not null)
            {
                parts.Add($"L{layer + 1} {name}");
            }
        }

        return parts.Count == 0 ? "全空" : string.Join("、", parts);
    }

    /// <summary>逐格写出 A 里每个非空格来自第几步 / 目标第几层。</summary>
    private static string DescribeA(HPreState plan)
    {
        var parts = new List<string>();

        for (int layer = 0; layer < plan.A.Length; layer++)
        {
            char code = plan.A[layer];

            if (code == ShapeSignature.EmptyCode)
            {
                continue;
            }

            string cell = code switch
            {
                ShapeSignature.SolidCode => "T",
                ShapeSignature.CrystalCode => "c",
                ShapeSignature.PinCode => "P",
                _ => code.ToString()
            };

            if (plan.Temporary[layer])
            {
                parts.Add($"L{layer + 1} {cell}←第 4 步临时晶体");
                continue;
            }

            if (plan.Origin[layer] > 0)
            {
                parts.Add(code == ShapeSignature.CrystalCode
                    ? $"L{layer + 1} c←第 2 步（目标 L{plan.Origin[layer]} 下移一格）"
                    : $"L{layer + 1} T←目标 L{plan.Origin[layer]}（第 4 步抬到 L{layer + 1}）");
                continue;
            }

            parts.Add($"L{layer + 1} {cell}←第 3 步");
        }

        return parts.Count == 0 ? "全空" : string.Join("；", parts);
    }
}
