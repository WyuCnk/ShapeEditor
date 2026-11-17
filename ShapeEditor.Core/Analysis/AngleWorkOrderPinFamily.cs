using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器（续）：<b>「N 连续针段」族的一次顶针</b>（任务书改动 2）。
///
/// <para>
/// 残余 47 列（<c>docs/residual-47-weakdrop.md</c>）此前走算法 6 的论文四步，
/// 字面结果都带一次弱落；但其中大部分存在「一次顶针」前态。前态由下列
/// <b>结构规则</b>直接生成（不打表、也不枚举）：
/// </para>
///
/// <code>
/// A = 目标列的最低晶体及其以上整体下移一格；最低晶体正下方是材料时，
///     那枚普通格（以及它下面连续的普通块）跟着下移（§0.11.2 ⑤ 的边界外扩）；
///     下移后若某颗晶体正下方为空，按 P1 把下方最近的普通格抬到它正下方。
/// B = 第 1 层放晶体，其余普通格，到 BTop 为止（深落差的落点层 d 再补一颗晶体桥）。
/// E = 满高纯晶体柱。
/// N = 第 2 层起连续放针，枚数 = 本次最高通道层（max(I)）− 1；
///     其余普通格，到 NTop 为止。
/// </code>
///
/// <para>
/// 通道与辅助列高度沿用 §0.3：<c>I = ∪[d, s−1]</c>、<c>J = {所有源层 s}</c>，
/// <c>BTop = max(max(I), 最高源层 − 1)</c>、
/// <c>NTop = max(max(J), 最高原生晶体目标层 − 1)</c>，以上留空。
/// </para>
///
/// <para>
/// 适用范围是四个<b>结构谓词</b>：<c>D ✔</c>、目标第 1 层为空、晶体距顶 ≤ 3 层、
/// 最低晶体下方空层数 ≥ 1。生成后必须通过真实 <see cref="ShapePhysics.Pin"/>
/// 逐格回放才采用；回放不过就如实返回 null，由现有路线接手（不许特判）。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>「N 连续针段」族的一次顶针候选。</summary>
    private static AngleWorkOrder? TryBuildPinFamilyOrder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        out bool applicable,
        out string? failure)
    {
        applicable = false;
        failure = null;

        if (parts != 4)
        {
            failure = "「N 连续针段」族需要四象限的环（A | B | E | N）。";
            return null;
        }

        if (layers < target.LayerCount || layers < 3)
        {
            failure = "cap 装不下目标列的完整高度，族前态放不下。";
            return null;
        }

        var codes = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            codes[layer] = layer < target.LayerCount
                ? CodeOf(target[layer, column])
                : ShapeSignature.EmptyCode;
        }

        string text = new(codes);

        // ── 四个结构谓词 ──
        if (text.Contains(ShapeSignature.PinCode))
        {
            failure = "目标列含针（底针走算法 5），不属于「N 连续针段」族。";
            return null;
        }

        if (!PillarSCriterion.BottomResourceD(text))
        {
            failure = "D 不成立（最低晶体以下不足两个空格）。";
            return null;
        }

        if (codes[0] != ShapeSignature.EmptyCode)
        {
            // 改动 1 的裁决：目标第 1 层是普通格 ⇒ 那枚格必须落到第 1 层 ⇒ d = 1，
            // 违反构造 H 的前提 2 ≤ d，纯构造 H 直接跳过（不尝试、不打日志）。
            failure = "目标第 1 层不是空：构造 H 的前提 2 ≤ d 不成立，按改动 1 直接跳过。";
            return null;
        }

        int lowest = Array.IndexOf(codes, ShapeSignature.CrystalCode);
        int highest = Array.LastIndexOf(codes, ShapeSignature.CrystalCode);

        if (lowest < 0)
        {
            failure = "目标列没有晶体。";
            return null;
        }

        int topDistance = 0;

        for (int layer = layers - 1; layer > highest; layer--)
        {
            if (codes[layer] != ShapeSignature.EmptyCode)
            {
                topDistance = layer - highest;
                break;
            }
        }

        if (topDistance > 3)
        {
            failure = "晶体距顶 " + topDistance + " 层（> 3），不属于该族。";
            return null;
        }

        int gapsBelow = 0;

        for (int layer = 0; layer < lowest; layer++)
        {
            if (codes[layer] == ShapeSignature.EmptyCode)
            {
                gapsBelow++;
            }
        }

        if (gapsBelow < 1)
        {
            failure = "最低晶体下方没有空层，不属于该族。";
            return null;
        }

        // 四个结构谓词全部成立：该列属于「N 连续针段」族。
        applicable = true;

        // ── A：最低晶体及以上下移一格（边界外扩到最低晶体正下方的连续普通块）──
        int f0 = lowest + 1;
        int low = f0;

        while (low >= 2 && codes[low - 2] == ShapeSignature.SolidCode)
        {
            low--;
        }

        var a = new char[layers];
        Array.Fill(a, ShapeSignature.EmptyCode);

        for (int f = 1; f < low; f++)
        {
            a[f - 1] = codes[f - 1];
        }

        for (int f = low; f <= layers; f++)
        {
            a[f - 2] = f <= codes.Length
                ? codes[f - 1]
                : ShapeSignature.EmptyCode;
        }

        // P1：晶体正下方为空 ⇒ 把下方最近的普通格抬到它正下方（§6.1 算法 1）。
        for (int layer = 2; layer <= layers; layer++)
        {
            if (a[layer - 1] != ShapeSignature.CrystalCode ||
                a[layer - 2] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            int donor = layer - 2;

            while (donor >= 1 && a[donor - 1] != ShapeSignature.SolidCode)
            {
                donor--;
            }

            if (donor < 1)
            {
                failure = $"A 第 {layer} 层的晶体下方为空，且找不到可抬升的普通格（P1）。";
                return null;
            }

            a[donor - 1] = ShapeSignature.EmptyCode;
            a[layer - 2] = ShapeSignature.SolidCode;
        }

        string aText = new(a);

        if (aText == text)
        {
            failure = "族前态 A 与目标列相同（循环）。";
            return null;
        }

        // ── 逐层配对：A 的普通格（顶针后层号）→ 目标的普通格层号 = 下降记录 ──
        var aSolids = new List<int>();
        var tSolids = new List<int>();
        var aCrystals = new List<int>();
        var tCrystals = new List<int>();

        for (int layer = 1; layer <= layers; layer++)
        {
            if (a[layer - 1] == ShapeSignature.SolidCode)
            {
                aSolids.Add(layer);
            }

            if (a[layer - 1] == ShapeSignature.CrystalCode)
            {
                aCrystals.Add(layer);
            }

            if (codes[layer - 1] == ShapeSignature.SolidCode)
            {
                tSolids.Add(layer);
            }

            if (codes[layer - 1] == ShapeSignature.CrystalCode)
            {
                tCrystals.Add(layer);
            }
        }

        if (aSolids.Count != tSolids.Count)
        {
            failure = "A 的普通格数与目标不符（" + aSolids.Count + " vs " + tSolids.Count + "）。";
            return null;
        }

        if (aCrystals.Count != tCrystals.Count)
        {
            failure = "A 的晶体数与目标不符（" + aCrystals.Count + " vs " + tCrystals.Count + "）。";
            return null;
        }

        var descents = new List<(int S, int D)>();

        for (int index = 0; index < aSolids.Count; index++)
        {
            int source = aSolids[index] + 1;
            int destination = tSolids[index];

            if (source < destination)
            {
                failure = $"普通格配对 {source}→{destination} 不满足 d ≤ s。";
                return null;
            }

            if (source > destination)
            {
                descents.Add((source, destination));
            }
        }

        for (int index = 0; index < aCrystals.Count; index++)
        {
            if (aCrystals[index] + 1 != tCrystals[index])
            {
                failure = $"晶体层配对不符：A 第 {aCrystals[index]} 层顶针后为 " +
                          $"{aCrystals[index] + 1}，目标在 {tCrystals[index]}。";
                return null;
            }
        }

        if (descents.Count == 0)
        {
            failure = "A 顶针后逐格等于目标，不需要下降（循环）。";
            return null;
        }

        // ── I / J 与辅助列高度（§0.3）──
        var iLayers = new HashSet<int>();

        foreach ((int s, int d) in descents)
        {
            for (int layer = d; layer <= s - 1; layer++)
            {
                iLayers.Add(layer);
            }
        }

        int maxSource = descents.Max(entry => entry.S);
        int maxI = iLayers.Max();
        int highestTarget = tCrystals[^1];

        /*
         * BTop 的两个读法（都从 §0.3 的「B 做到最高有效层」出发）：
         * · 浅落差（每段 s − d = 1）：BTop = max(max(I), 最高源层 − 1, 最高晶体层 − 2)；
         *   四个回归列因此给出文档表格里的 B = cTT.. / cTTT..。
         * · 有深落差（s − d ≥ 2）：桥柱要一直铺到晶体下一层，否则顶针后
         *   源格会在中途被挡住（`.TT...c` 的实测）；
         *   BTop = max(上面的值, 最高源层, 最高晶体层 − 1)。
         */
        bool hasDeepDescent = descents.Any(entry => entry.S - entry.D >= 2);
        int bTop = Math.Max(
            Math.Max(maxI, maxSource - 1),
            highestTarget - 2);

        if (hasDeepDescent)
        {
            bTop = Math.Max(Math.Max(bTop, maxSource), highestTarget - 1);
        }

        bTop = Math.Max(1, Math.Min(bTop, layers));
        int nTop = Math.Max(maxSource, highestTarget - 1);
        int pinCount = maxI - 1;

        if (pinCount < 1)
        {
            failure = "max(I) = " + maxI + "，写不出连续针段。";
            return null;
        }

        // 深落差（s − d ≥ 2）在源格下一层再补一颗晶体桥（§13.6.2 改正 1 / 3）：
        // 桥必须跨在刀口一侧的「源层 − 1」，落点那一层的晶体在深通道里不够。
        var bCrystals = new HashSet<int> { 1 };

        foreach ((int s, int d) in descents)
        {
            if (s - d >= 2 && s - 1 >= 2)
            {
                bCrystals.Add(s - 1);
            }
        }

        var b = new char[layers];
        var e = new char[layers];
        var n = new char[layers];

        for (int layer = 1; layer <= layers; layer++)
        {
            e[layer - 1] = ShapeSignature.CrystalCode;

            b[layer - 1] = bCrystals.Contains(layer)
                ? ShapeSignature.CrystalCode
                : layer <= bTop
                    ? ShapeSignature.SolidCode
                    : ShapeSignature.EmptyCode;

            n[layer - 1] = layer >= 2 && layer <= maxI
                ? ShapeSignature.PinCode
                : layer <= nTop
                    ? ShapeSignature.SolidCode
                    : ShapeSignature.EmptyCode;
        }

        string bText = new(b);
        string eText = new(e);
        string nText = new(n);

        char color = CrystalColor(target, column);

        var assembled = new ShapeDocument(layers, parts);
        int bPart = Wrap(column + 1, parts);
        int ePart = Wrap(column + 2, parts);
        int nPart = Wrap(column - 1, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            assembled[layer, column] = CellFromCode(a[layer], color);
            assembled[layer, bPart] = CellFromCode(b[layer], color);
            assembled[layer, ePart] = CellFromCode(e[layer], color);
            assembled[layer, nPart] = CellFromCode(n[layer], color);
        }

        // ── 初始前态闸门：每列过五项、整体稳定、P1（晶体坐在材料上）──
        if (ShapeSignature.ColumnText(assembled, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            failure = "族前态 A 与目标列相同（循环）。";
            return null;
        }

        if (IsRejectedPreState(target, column, assembled, out int badColumn))
        {
            failure = DescribeIllegalPreState(assembled, badColumn);
            return null;
        }

        if (!ShapeOps.IsStable(assembled))
        {
            failure = "族前态不稳定（A = " + aText + "，B = " + bText + "，N = " + nText + "）。";
            return null;
        }

        for (int layer = 1; layer < layers; layer++)
        {
            if (a[layer] == ShapeSignature.CrystalCode &&
                a[layer - 1] == ShapeSignature.EmptyCode)
            {
                failure = $"族前态 A 第 {layer + 1} 层晶体下方为空（P1）。";
                return null;
            }
        }

        // ── 一次顶针回放（硬验收；先做最便宜也最硬的闸门）──
        PhysicsResult pinResult = ShapePhysics.Pin(assembled);

        if (!pinResult.Success || pinResult.Document is null)
        {
            failure = "族前态的 ShapePhysics.Pin 失败：" + (pinResult.Error ?? "未知原因");
            return null;
        }

        string pinnedText = ShapeSignature.ColumnText(pinResult.Document, column);

        if (pinnedText != text)
        {
            failure = "族前态顶针后得到 " + pinnedText + "，不等于目标 " + text + "。";
            return null;
        }

        // ── 带洞前态：交换链或 Q1 一层子配方证明可造（§19.4 / §5.1）──
        bool tight = ShapeSignature.IsTightColumn(assembled, column);
        Recipe? subRecipe = null;
        Recipe? q1Recipe = null;

        if (!tight)
        {
            if (ExchangeChain.TryBuild(assembled, out subRecipe) &&
                subRecipe is not null &&
                subRecipe.Rank == 0 &&
                Replay.Verify(subRecipe, assembled, out _))
            {
                // 交换链可用。
            }
            else
            {
                subRecipe = null;

                if (!TryBuildColumnPartSubRecipe(
                        assembled,
                        column,
                        out q1Recipe,
                        out string? q1Description) ||
                    q1Recipe is null)
                {
                    failure = "族前态 A 带洞，交换链与 Q1 子配方都没能装配出这份前态" +
                              (q1Description is null ? "。" : "（" + q1Description + "）。");
                    return null;
                }
            }
        }

        var limitations = BaseLimitations(
            "「N 连续针段」族（任务书改动 2）：A = 目标列最低晶体及以上下移一格" +
            "（最低晶体下方是材料时边界外扩），再按 P1 把晶体正下方补成普通格；" +
            "B = 第 1 层放晶体、其余普通格到 BTop（深落差的源层下一层再补一颗晶体桥）；" +
            "E = 满高纯晶体柱；N = 从第 2 层起连续放针（" + pinCount +
            " 枚，到 max(I) 为止），其余普通格到 NTop。一次顶针完成全部下降。");

        limitations.Add(
            "结构谓词：D ✔；目标第 1 层为空；晶体距顶 " + topDistance +
            " 层（≤ 3）；最低晶体（第 " + f0 + " 层）下方空层 " + gapsBelow +
            "（≥ 1）。");

        limitations.Add(
            "A = " + aText + "；下降表（源层 s → 落点 d，层号用顶针后）：" +
            (descents.Count > 0
                ? string.Join("、", descents.Select(entry => entry.S + "→" + entry.D))
                : "无") + "。");

        limitations.Add(
            "I = {" + string.Join(",", iLayers.Order()) + "}（完整通道 ∪[d, s−1]），" +
            "J = {" + string.Join(",", Enumerable.Range(2, pinCount).Select(layer => layer + 1)) +
            "}（连续针段，1 起）；BTop = " + bTop + "，NTop = " + nTop + "；" +
            "B = " + bText + "，E = " + eText + "，N = " + nText + "，以上留空。");

        limitations.Add(
            "顶针后 col0 = " + pinnedText + "，逐格等于目标（真实 ShapePhysics.Pin 回放通过）。");

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（构造 H：「N 连续针段」族：A | 晶体桥柱 B | 满高纯晶体柱 E | " +
                "针槽柱 N）",
                assembled,
                AngleWorkStepKind.PreState),
            new(
                "顶针：一次 Pin 把整体抬一层，E 顶端超限整簇碎裂并连带删掉 B 的晶体桥，" +
                "A 的普通格沿完整通道落到各自的目标层",
                pinResult.Document,
                AngleWorkStepKind.Pin),
            CrystallizeNote(),
            new AngleWorkStep(
                "清除辅助象限，得到目标角",
                target,
                AngleWorkStepKind.Target)
        };

        var order = new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.H,
            Algorithm = AngleAlgorithm.Algorithm6,
            Rotation = 0,
            AuxiliaryColumns = new[] { nPart, bPart, ePart },
            SubRecipe = subRecipe,
            Q1SubRecipe = q1Recipe,
            Rank = 1,
            Limitations = limitations
        };

        return q1Recipe is null
            ? AttachQ1SubRecipe(order)
            : order;
    }
}
