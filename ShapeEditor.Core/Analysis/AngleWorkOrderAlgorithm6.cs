using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 算法 6 的论文四步（docs/angle-work-order-general-algorithm.md §0.4）：
///
/// <code>
/// ① 底块提升（至多两次）
/// ② 正规化（算法 1 ⇒ v + 恢复表 M），再决定底格恢复方式（M / 弱落 / e）
/// ③ 找出需要提前弱落的 M 记录（至多一条）
/// ④ 正向制作：v 低一层 → 提前弱落 → 构造 H（M − 提前 + e）→ 顶针
///    → 按底块提升逆序逐块弱落恢复 → 目标
/// </code>
///
/// <para>
/// 这里不再使用旧的 §13.6 七步 / R1 读法：四步全部由目标列逐格推出，
/// 每一步都有确定的几何依据；回放失败时只如实报未覆盖，不枚举、不退 FillBreak。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>一次底块提升：块的格子身份、原区间、上移距离与新区间（层号 1 起）。</summary>
    private sealed record BottomLift(
        IReadOnlyList<int> CellIds,
        int OriginalLow,
        int OriginalHigh,
        int Distance,
        int NewLow,
        int NewHigh);

    /// <summary>恢复表 / 下降表里的一条：源层 s → 落点 d（顶针后层号，1 起）。</summary>
    private sealed record FourStepDescent(int S, int D);

    /// <summary>算法 6 四步的完整中间值，既用于失败诊断，也用于 Limitations。</summary>
    private sealed record FourStepTrace(
        string Target,
        IReadOnlyList<BottomLift> Lifts,
        string W,
        string V,
        IReadOnlyList<FourStepDescent> M,
        IReadOnlyList<FourStepDescent> Descents,
        FourStepDescent? Extra,
        FourStepDescent? Early,
        int B,
        string A,
        string BColumn,
        string EColumn,
        string NColumn,
        int BTop,
        int NTop,
        string Pinned,
        string Restored,
        string? Failure)
    {
        public string Describe()
        {
            var builder = new System.Text.StringBuilder();

            builder.Append("目标列 = ").Append(Target);
            builder.Append("；底块提升 = ");

            if (Lifts.Count == 0)
            {
                builder.Append("无");
            }
            else
            {
                builder.Append(string.Join(
                    "、",
                    Lifts.Select((lift, index) =>
                        $"B{index + 1}[{lift.OriginalLow}–{lift.OriginalHigh}]" +
                        $"↑{lift.Distance}→[{lift.NewLow}–{lift.NewHigh}]")));
            }

            builder.Append("；整理后 w = ").Append(W);
            builder.Append("；正规列 v = ").Append(V);
            builder.Append("；M = ").Append(FormatDescents(M));

            if (Extra is not null)
            {
                builder.Append("；额外动作 e = ").Append(FormatDescent(Extra));
            }

            if (Early is not null)
            {
                builder.Append("；提前弱落 = ").Append(FormatDescent(Early));
            }

            builder.Append("；b = ").Append(B);
            builder.Append("；本次下降表 = ").Append(FormatDescents(Descents));
            builder.Append("；A（低一层 + 提前弱落）= ").Append(A);
            builder.Append("；B = ").Append(BColumn);
            builder.Append("（BTop = ").Append(BTop).Append("）");
            builder.Append("；E = ").Append(EColumn);
            builder.Append("；N = ").Append(NColumn);
            builder.Append("（NTop = ").Append(NTop).Append("）");
            builder.Append("；顶针后 = ").Append(Pinned);
            builder.Append("；弱落恢复后 = ").Append(Restored);

            if (Failure is not null)
            {
                builder.Append("；失败原因 = ").Append(Failure);
            }

            return builder.ToString();
        }
    }

    private static string FormatDescent(FourStepDescent descent)
    {
        return $"{descent.S}→{descent.D}";
    }

    private static string FormatDescents(
        IReadOnlyList<FourStepDescent> descents)
    {
        return descents.Count == 0
            ? "∅"
            : "{" + string.Join("、", descents.Select(FormatDescent)) + "}";
    }

    /// <summary>一次弱落里的一格：从 <see cref="From"/> 层落到 <see cref="To"/> 层。</summary>
    private sealed record WeakDropMove(int From, int To, string Support);

    /// <summary>
    /// 弱落落点下方的支撑材料（施工后的形态里最近的一枚非空格）：
    /// 例如「第 2 层的普通格」；落到地面时写「地面」。
    /// </summary>
    private static string DescribeSupportOf(
        ShapeDocument document,
        int column,
        int landing)
    {
        for (int layer = landing - 1; layer >= 1; layer--)
        {
            ShapeCell cell = document[layer - 1, column];

            if (cell.Type == CellType.Empty)
            {
                continue;
            }

            string material = cell.Type switch
            {
                CellType.Shape => "普通格",
                CellType.Crystal => "晶体",
                CellType.Pin => "顶针",
                _ => "材料"
            };

            return "第 " + layer + " 层的" + material;
        }

        return "地面";
    }

    /// <summary>
    /// §0.9.2 的弱落工序写法：五项必须写全（PreState / Support / Collapse / Support / PreState），
    /// 并写明「哪一格、落在哪一层的什么材料之上、跨了几层」。
    ///
    /// <para>
    /// 辅助列 <c>S′</c> 按 §0.9.1 给出：连续接地、仅在源层为针、其余普通格
    /// （本列 A 无针，所以源格上方没有随落的连续针段，标题里如实写明）。
    /// </para>
    /// </summary>
    private static string DescribeWeakDropStep(
        string phase,
        string beforeText,
        IReadOnlyList<WeakDropMove> moves,
        int layers,
        string afterText)
    {
        var pinLayers = moves
            .Select(move => move.From)
            .Distinct()
            .Order()
            .ToList();

        var supportColumn = new char[layers];

        Array.Fill(supportColumn, ShapeSignature.SolidCode);

        foreach (int layer in pinLayers)
        {
            if (layer >= 1 && layer <= layers)
            {
                supportColumn[layer - 1] = ShapeSignature.PinCode;
            }
        }

        var builder = new System.Text.StringBuilder();

        builder.Append("弱落（构造 E）：").Append(phase).Append("。");
        builder.Append("[PreState] A 列当前形态 = ").Append(beforeText);
        builder.Append("；辅助列 S′ = ").Append(new string(supportColumn));
        builder.Append("（连续接地，仅第 ").Append(string.Join("、", pinLayers));
        builder.Append(" 层为针，其余普通格）；");
        builder.Append("[Support] 撤掉原托柱（此时只剩 A 与 S′）；");
        builder.Append("[Collapse] ");

        for (int index = 0; index < moves.Count; index++)
        {
            WeakDropMove move = moves[index];

            if (index > 0)
            {
                builder.Append("；");
            }

            builder.Append("第 ").Append(move.From).Append(" 层的普通格");
            builder.Append("（该格上方没有连续针段）落到第 ").Append(move.To);
            builder.Append(" 层，落在").Append(move.Support).Append("之上，跨 ");
            builder.Append(move.From - move.To).Append(" 层");
        }

        builder.Append("；");
        builder.Append("[Support] 装回满高托柱；");
        builder.Append("[PreState] 拆掉 S′，得到 ").Append(afterText).Append("。");

        return builder.ToString();
    }

    /// <summary>
    /// 算法 6 的论文四步入口。返回 null 表示四步做不出来（由调用方决定
    /// 是否降级到 §15 多刀）；<paramref name="trace"/> 始终给出中间值。
    /// </summary>
    private static AngleWorkOrder? TryBuildFourStepAlgorithm6(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        out FourStepTrace trace)
    {
        trace = default!;

        string targetText = layers <= target.LayerCount
            ? ShapeSignature.ColumnText(target, column)
            : ShapeSignature.ColumnText(target, column) + new string('.', layers - target.LayerCount);

        if (parts != 4)
        {
            trace = EmptyTrace(targetText, "算法 6 需要四象限的环（A | B | E | N）。");
            return null;
        }

        if (layers < 3 || layers < target.LayerCount)
        {
            trace = EmptyTrace(targetText, "cap 装不下目标列的完整高度，四步前态放不下。");
            return null;
        }

        var targetCodes = new char[layers];

        for (int layer = 1; layer <= layers; layer++)
        {
            targetCodes[layer - 1] = layer <= target.LayerCount
                ? CodeOf(target[layer - 1, column])
                : ShapeSignature.EmptyCode;
        }

        // 算法 6 的核心只含普通格、晶体和空；底针走算法 5。
        for (int layer = 1; layer <= layers; layer++)
        {
            if (targetCodes[layer - 1] == ShapeSignature.PinCode)
            {
                trace = EmptyTrace(targetText, $"第 {layer} 层是针，算法 6 的核心只含 T/c/空。");
                return null;
            }
        }

        int lowestCrystal = 0;

        for (int layer = 1; layer <= layers; layer++)
        {
            if (targetCodes[layer - 1] == ShapeSignature.CrystalCode)
            {
                lowestCrystal = layer;
                break;
            }
        }

        if (lowestCrystal == 0)
        {
            trace = EmptyTrace(targetText, "目标列没有晶体，算法 6 不适用。");
            return null;
        }

        int gapsBelowLowest = 0;

        for (int layer = 1; layer < lowestCrystal; layer++)
        {
            if (targetCodes[layer - 1] == ShapeSignature.EmptyCode)
            {
                gapsBelowLowest++;
            }
        }

        if (gapsBelowLowest < 2)
        {
            trace = EmptyTrace(
                targetText,
                $"最低晶体（第 {lowestCrystal} 层）以下只有 {gapsBelowLowest} 个空格，D 不成立。");
            return null;
        }

        // ── 第一步：底块提升（至多两次）──
        var w = (char[])targetCodes.Clone();
        var cellId = new int[layers];
        var cellPosition = new Dictionary<int, int>();
        var lifts = new List<BottomLift>();

        Array.Fill(cellId, -1);

        int nextId = 0;

        for (int layer = 1; layer <= layers; layer++)
        {
            if (w[layer - 1] == ShapeSignature.EmptyCode)
            {
                continue;
            }

            cellId[layer - 1] = nextId;
            cellPosition[nextId] = layer;
            nextId++;
        }

        while (!(w[0] == ShapeSignature.EmptyCode &&
                 w[1] == ShapeSignature.EmptyCode))
        {
            int low = 1;

            while (low <= layers && w[low - 1] == ShapeSignature.EmptyCode)
            {
                low++;
            }

            if (low > layers)
            {
                break;
            }

            if (w[low - 1] != ShapeSignature.SolidCode)
            {
                trace = EmptyTrace(
                    targetText,
                    $"最下方非空位置（第 {low} 层）不是普通格，底块提升不适用。");
                return null;
            }

            int high = low;

            while (high + 1 <= layers && w[high] == ShapeSignature.SolidCode)
            {
                high++;
            }

            int nextMaterial = high + 1;

            while (nextMaterial <= layers &&
                   w[nextMaterial - 1] == ShapeSignature.EmptyCode)
            {
                nextMaterial++;
            }

            if (nextMaterial > layers)
            {
                trace = EmptyTrace(
                    targetText,
                    $"第 {low}–{high} 层的底块上方没有材料，无法确定提升终点。");
                return null;
            }

            int distance = nextMaterial - 1 - high;

            if (distance <= 0)
            {
                trace = EmptyTrace(
                    targetText,
                    $"第 {low}–{high} 层的底块已紧贴第 {nextMaterial} 层材料，无法再提升。");
                return null;
            }

            var ids = new List<int>();

            for (int layer = low; layer <= high; layer++)
            {
                ids.Add(cellId[layer - 1]);
            }

            for (int layer = low; layer <= high; layer++)
            {
                w[layer - 1] = ShapeSignature.EmptyCode;
                cellId[layer - 1] = -1;
            }

            for (int index = 0; index < ids.Count; index++)
            {
                int newLayer = low + distance + index;

                w[newLayer - 1] = ShapeSignature.SolidCode;
                cellId[newLayer - 1] = ids[index];
                cellPosition[ids[index]] = newLayer;
            }

            lifts.Add(new BottomLift(
                ids,
                low,
                high,
                distance,
                low + distance,
                high + distance));

            if (lifts.Count > 2)
            {
                trace = EmptyTrace(
                    targetText,
                    "底块提升超过两次仍未清空最底两层（论文保证至多两次）。");
                return null;
            }
        }

        // 底块提升结束后的位置就是 H 之后（弱落之前）各块格子的位置。
        var liftedPositions = new Dictionary<int, int>(cellPosition);
        string wText = new string(w);

        // ── 第二步：正规化（算法 1）⇒ v + M ──
        var v = (char[])w.Clone();
        var m = new List<FourStepDescent>();

        for (int crystal = 2; crystal <= layers; crystal++)
        {
            if (v[crystal - 1] != ShapeSignature.CrystalCode ||
                v[crystal - 2] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            int donor = crystal - 1;

            while (donor >= 1 && v[donor - 1] != ShapeSignature.SolidCode)
            {
                donor--;
            }

            if (donor < 1)
            {
                trace = EmptyTrace(
                    targetText,
                    $"第 {crystal} 层的晶体下方为空，且找不到可抬升的普通格。");
                return null;
            }

            // 算法 1 只沿连续空位向下找最近的普通格；中间隔着晶体就无法通过。
            for (int layer = donor + 1; layer <= crystal - 1; layer++)
            {
                if (v[layer - 1] != ShapeSignature.EmptyCode)
                {
                    trace = EmptyTrace(
                        targetText,
                        $"第 {crystal} 层晶体与第 {donor} 层普通格之间有材料，" +
                        "算法 1 的提升通道不连续。");
                    return null;
                }
            }

            v[donor - 1] = ShapeSignature.EmptyCode;
            v[crystal - 2] = ShapeSignature.SolidCode;
            m.Add(new FourStepDescent(crystal - 1, donor));
        }

        for (int crystal = 2; crystal <= layers; crystal++)
        {
            if (v[crystal - 1] == ShapeSignature.CrystalCode &&
                v[crystal - 2] == ShapeSignature.EmptyCode)
            {
                trace = EmptyTrace(
                    targetText,
                    $"算法 1 之后第 {crystal} 层的晶体下方仍为空（正规化失败）。");
                return null;
            }
        }

        string vText = new string(v);

        // ── 第二步（续）：最后一块的底格恢复方式 ──
        int b = 0;
        FourStepDescent? extra = null;

        if (lifts.Count > 0)
        {
            BottomLift last = lifts[^1];

            if (last.OriginalLow == 2)
            {
                b = last.NewLow;

                if (b <= 2)
                {
                    trace = EmptyTrace(
                        targetText,
                        $"最后一块现在的最低格 b = {b}，无法安排 e: b→2。");
                    return null;
                }

                extra = new FourStepDescent(b, 2);
            }
            else if (last.OriginalLow != 1)
            {
                trace = EmptyTrace(
                    targetText,
                    $"最后一块的原起点是第 {last.OriginalLow} 层（论文只允许 1 或 2）。");
                return null;
            }
        }

        // ── 第三步：找出需要提前弱落的 M 记录 ──
        FourStepDescent? early = null;

        if (extra is not null)
        {
            var candidates = new List<FourStepDescent>();

            foreach (FourStepDescent descent in m)
            {
                if (NearestMaterialBelow(v, descent.S) == b)
                {
                    candidates.Add(descent);
                }
            }

            if (candidates.Count > 1)
            {
                trace = EmptyTrace(
                    targetText,
                    "M 中有多条记录的源层下方最近普通格都是第 " + b +
                    " 层那枚（论文说至多一条）：" +
                    string.Join("、", candidates.Select(FormatDescent)));
                return null;
            }

            if (candidates.Count == 1)
            {
                early = candidates[0];

                // 提前弱落的落点是 b+1；只有 M 的原落点正好是 b+1 时，
                // 顶针后才回到 M 要求的目标层 d。
                if (early.D != b + 1)
                {
                    trace = EmptyTrace(
                        targetText,
                        $"提前记录 {FormatDescent(early)} 的落点不是 b+1 = {b + 1}，" +
                        "弱落会被第 " + b + " 层挡住，无法回到 d。");
                    return null;
                }
            }
        }

        // ── 第四步①：制作 v 整体低一层的 A，并执行提前弱落 ──
        var a = new char[layers];
        Array.Fill(a, ShapeSignature.EmptyCode);

        for (int layer = 1; layer < layers; layer++)
        {
            a[layer - 1] = v[layer];
        }

        string assembledA = new(a);

        if (early is not null)
        {
            int source = early.S - 1;
            int landing = early.D - 1;

            if (source < 1 || source > layers ||
                a[source - 1] != ShapeSignature.SolidCode)
            {
                trace = EmptyTrace(
                    targetText,
                    $"提前弱落的源格不在 A 的第 {source} 层（A = {assembledA}）。");
                return null;
            }

            if (landing < 1 || landing > layers ||
                a[landing - 1] != ShapeSignature.EmptyCode)
            {
                trace = EmptyTrace(
                    targetText,
                    $"提前弱落的落点 A 第 {landing} 层不是空位（A = {assembledA}）。");
                return null;
            }

            a[source - 1] = ShapeSignature.EmptyCode;
            a[landing - 1] = ShapeSignature.SolidCode;
        }

        // ── 第四步③：本次下降表 = M − 提前 + e；I 只含完整通道 ──
        var descents = new List<FourStepDescent>();

        foreach (FourStepDescent descent in m)
        {
            if (early is not null &&
                descent.S == early.S &&
                descent.D == early.D)
            {
                continue;
            }

            descents.Add(descent);
        }

        if (extra is not null)
        {
            descents.Add(extra);
        }

        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();

        foreach (FourStepDescent descent in descents)
        {
            if (descent.S <= descent.D || descent.S > layers || descent.D < 1)
            {
                trace = EmptyTrace(
                    targetText,
                    $"下降表条目 {FormatDescent(descent)} 不满足 1 ≤ d < s ≤ cap。");
                return null;
            }

            jLayers.Add(descent.S);

            for (int layer = descent.D; layer <= descent.S - 1; layer++)
            {
                iLayers.Add(layer);
            }
        }

        int maxSource = descents.Count == 0
            ? 0
            : descents.Max(descent => descent.S);

        int highestCrystal = 0;

        for (int layer = 1; layer <= layers; layer++)
        {
            if (targetCodes[layer - 1] == ShapeSignature.CrystalCode)
            {
                highestCrystal = layer;
            }
        }

        // §0.3：B 做到 BTop（最高源层 − 1）为止，N 做到 NTop 为止，以上留空。
        int bTop = Math.Max(0, maxSource - 1);
        int nTop = Math.Max(0, Math.Max(highestCrystal - 1, maxSource - 1));

        var bColumn = new char[layers];
        var eColumn = new char[layers];
        var nColumn = new char[layers];

        for (int layer = 1; layer <= layers; layer++)
        {
            bColumn[layer - 1] = iLayers.Contains(layer + 1)
                ? ShapeSignature.CrystalCode
                : layer <= bTop
                    ? ShapeSignature.SolidCode
                    : ShapeSignature.EmptyCode;

            eColumn[layer - 1] = ShapeSignature.CrystalCode;

            nColumn[layer - 1] = jLayers.Contains(layer + 1)
                ? ShapeSignature.PinCode
                : layer <= nTop
                    ? ShapeSignature.SolidCode
                    : ShapeSignature.EmptyCode;
        }

        char color = CrystalColor(target, column);
        int bPart = Wrap(column + 1, parts);
        int ePart = Wrap(column + 2, parts);
        int nPart = Wrap(column - 1, parts);

        ShapeDocument assembled = BuildFourStepCorner(
            layers,
            parts,
            column,
            assembledA,
            bColumn,
            eColumn,
            nColumn,
            color);

        ShapeDocument pinInput = BuildFourStepCorner(
            layers,
            parts,
            column,
            new string(a),
            bColumn,
            eColumn,
            nColumn,
            color);

        string aText = new(a);
        string bText = new(bColumn);
        string eText = new(eColumn);
        string nText = new(nColumn);

        if (ShapeSignature.ColumnText(assembled, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            trace = EmptyTrace(targetText, "四步推出的 A 与目标列相同（循环）。");
            return null;
        }

        // 初始前态 A 有洞 ⇒ 需要子配方证明它能装出来：
        // 优先用交换链（整份前态的 rank 0 装配）；交换链做不了时，
        // 退到 Q1 的一层子配方（紧列 → 半破 → 零件），它同样证明 A 的洞可造。
        bool tight = ShapeSignature.IsTightColumn(assembled, column);
        Recipe? subRecipe = null;
        Recipe? q1Fallback = null;

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
                        out q1Fallback,
                        out string? q1Description) ||
                    q1Fallback is null)
                {
                    trace = EmptyTrace(
                        targetText,
                        "四步推出的初始前态 A 带洞，交换链与 Q1 子配方都没能装配出这份前态" +
                        (q1Description is null ? "。" : "（" + q1Description + "）。"));
                    return null;
                }
            }
        }

        if (IsRejectedPreState(target, column, assembled, out int badColumn))
        {
            trace = EmptyTrace(
                targetText,
                DescribeIllegalPreState(assembled, badColumn));
            return null;
        }

        if (!ShapeOps.IsStable(assembled))
        {
            trace = EmptyTrace(targetText, "四步的初始前态不稳定。");
            return null;
        }

        if (!ShapeOps.IsStable(pinInput))
        {
            trace = EmptyTrace(targetText, "提前弱落后的前态不稳定（构造 E 的辅助柱撑不住）。");
            return null;
        }

        // ── 第四步⑤：顶针一次 ──
        PhysicsResult pinResult = ShapePhysics.Pin(pinInput);

        if (!pinResult.Success || pinResult.Document is null)
        {
            trace = EmptyTrace(
                targetText,
                "ShapePhysics.Pin 失败：" + (pinResult.Error ?? "未知原因"));
            return null;
        }

        ShapeDocument pinned = pinResult.Document;
        string pinnedText = ShapeSignature.ColumnText(pinned, column);

        // 顶针后应等于 w（最后一块的最低格已由 e 放到第 2 层）。
        var expectedPinned = (char[])w.Clone();

        if (extra is not null)
        {
            expectedPinned[extra.S - 1] = ShapeSignature.EmptyCode;
            expectedPinned[extra.D - 1] = ShapeSignature.SolidCode;
        }

        if (pinnedText != new string(expectedPinned))
        {
            trace = new FourStepTrace(
                targetText,
                lifts,
                wText,
                vText,
                m,
                descents,
                extra,
                early,
                b,
                aText,
                bText,
                eText,
                nText,
                bTop,
                nTop,
                pinnedText,
                string.Empty,
                "顶针后的目标列与 w（e 已放回第 2 层）不符：期望 " +
                new string(expectedPinned) + "，实际 " + pinnedText + "。");

            return null;
        }

        // ── 第四步⑥：按底块提升的逆序逐块弱落 ──
        ShapeDocument current = pinned;
        var weakSteps = new List<AngleWorkStep>();

        // e 已经把最后一块的最低格放回第 2 层；同步它的当前位置。
        if (extra is not null && lifts.Count > 0)
        {
            liftedPositions[lifts[^1].CellIds[0]] = extra.D;
        }

        for (int index = lifts.Count - 1; index >= 0; index--)
        {
            BottomLift lift = lifts[index];
            bool isLast = index == lifts.Count - 1;
            var ordered = lift.CellIds
                .OrderBy(id => liftedPositions[id])
                .ToList();
            var moves = new List<WeakDropMove>();
            string beforeText = ShapeSignature.ColumnText(current, column);

            foreach (int id in ordered)
            {
                // 已由 e 放回第 2 层的最低格跳过。
                if (isLast &&
                    extra is not null &&
                    id == lift.CellIds[0])
                {
                    continue;
                }

                int from = liftedPositions[id];
                int support = 0;

                for (int layer = from - 1; layer >= 1; layer--)
                {
                    if (current[layer - 1, column].Type != CellType.Empty)
                    {
                        support = layer;
                        break;
                    }
                }

                int to = support + 1;

                if (from <= to)
                {
                    trace = new FourStepTrace(
                        targetText,
                        lifts,
                        wText,
                        vText,
                        m,
                        descents,
                        extra,
                        early,
                        b,
                        aText,
                        bText,
                        eText,
                        nText,
                        bTop,
                        nTop,
                        pinnedText,
                        ShapeSignature.ColumnText(current, column),
                        $"弱落时第 {from} 层的格子找不到可下落的位置。");
                    return null;
                }

                ShapeCell cell = current[from - 1, column];

                if (cell.Type != CellType.Shape)
                {
                    trace = new FourStepTrace(
                        targetText,
                        lifts,
                        wText,
                        vText,
                        m,
                        descents,
                        extra,
                        early,
                        b,
                        aText,
                        bText,
                        eText,
                        nText,
                        bTop,
                        nTop,
                        pinnedText,
                        ShapeSignature.ColumnText(current, column),
                        $"弱落选中的第 {from} 层不是普通格。");
                    return null;
                }

                current = current.Clone();
                current[from - 1, column] = ShapeCell.Empty();
                current[to - 1, column] = cell;
                liftedPositions[id] = to;
                moves.Add(new WeakDropMove(from, to, DescribeSupportOf(current, column, to)));
            }

            if (moves.Count > 0)
            {
                weakSteps.Add(new AngleWorkStep(
                    DescribeWeakDropStep(
                        "恢复第 " + (index + 1) + " 次底块提升",
                        beforeText,
                        moves,
                        layers,
                        ShapeSignature.ColumnText(current, column)),
                    current.Clone(),
                    AngleWorkStepKind.WeakDrop));
            }
        }

        string restoredText = ShapeSignature.ColumnText(current, column);

        if (!MatchesTargetColumn(current, target, column, layers, out string? difference))
        {
            trace = new FourStepTrace(
                targetText,
                lifts,
                wText,
                vText,
                m,
                descents,
                extra,
                early,
                b,
                aText,
                bText,
                eText,
                nText,
                bTop,
                nTop,
                pinnedText,
                restoredText,
                "弱落恢复后目标列与目标不符：" + difference);
            return null;
        }

        // ── 组装步骤链：前态 →（提前弱落）→ 顶针 → 逐块弱落 → 目标 ──
        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（算法 6 四步：A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N）",
                assembled,
                AngleWorkStepKind.PreState)
        };

        if (early is not null)
        {
            int earlyFrom = early.S - 1;
            int earlyTo = early.D - 1;

            steps.Add(new AngleWorkStep(
                DescribeWeakDropStep(
                    "顶针前的提前弱落（§0.4 第三步：源层 / 落点各减一）",
                    assembledA,
                    new List<WeakDropMove>
                    {
                        new(
                            earlyFrom,
                            earlyTo,
                            DescribeSupportOf(pinInput, column, earlyTo))
                    },
                    layers,
                    ShapeSignature.ColumnText(pinInput, column)),
                pinInput.Clone(),
                AngleWorkStepKind.WeakDrop));
        }

        steps.Add(new AngleWorkStep(
            "顶针：一次 Pin 把整体抬一层，E 顶端超限整簇碎裂并连带删掉 B 的晶体桥，" +
            "源普通格沿完整通道落到各自的目标层",
            pinned,
            AngleWorkStepKind.Pin));

        steps.AddRange(weakSteps);
        steps.Add(CrystallizeNote());
        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        var limitations = BaseLimitations(
            "算法 6（§0.4 论文四步）：底块提升 → 算法 1 正规化 + 恢复表 M → " +
            "v 低一层制作 → 提前弱落（至多一条）→ 构造 H（M − 提前 + e）一次顶针 → " +
            "按底块提升逆序逐块弱落。");

        limitations.Add(
            "底块提升：" +
            (lifts.Count == 0
                ? "无（最底两层本来就空）。"
                : string.Join(
                    "；",
                    lifts.Select((lift, index) =>
                        $"B{index + 1} = 第 {lift.OriginalLow}–{lift.OriginalHigh} 层" +
                        $"↑{lift.Distance} → 第 {lift.NewLow}–{lift.NewHigh} 层"))));

        limitations.Add(
            "算法 1：w = " + wText + " ⇒ v = " + vText +
            "，M = " + FormatDescents(m) + "（s → d，顶针后层号）。");

        if (extra is not null)
        {
            limitations.Add(
                "最后一块原从第 2 层起，现在最低格在 b = " + b +
                " ⇒ 额外动作 e = " + FormatDescent(extra) + "。");
        }
        else if (lifts.Count > 0)
        {
            limitations.Add("最后一块原从第 1 层起 ⇒ 不使用 e，整块按弱落恢复。");
        }

        if (early is not null)
        {
            limitations.Add(
                "提前弱落：M 的 " + FormatDescent(early) +
                " 在 v 中源层以下最近的普通格正是第 " + b + " 层那枚 ⇒ 提前到顶针前执行" +
                "（源/落点各减一）。");
        }

        limitations.Add(
            "本次下降表 = " + FormatDescents(descents) +
            "；I = {" + string.Join(",", iLayers.Order()) + "}（只含完整通道 ∪[d,s−1]），" +
            "J = {" + string.Join(",", jLayers.Order()) + "}；B = " + bText +
            "（BTop = " + bTop + "），E = " + eText + "（满高），N = " + nText +
            "（NTop = " + nTop + "），以上留空。");

        if (weakSteps.Count > 0)
        {
            limitations.Add(
                "弱落恢复：" + weakSteps.Count + " 块，按底块提升逆序；" +
                "块内从最低格到最高格依次弱落" +
                (extra is not null ? "（已由 e 放回第 2 层的最低格跳过）" : string.Empty) +
                "。");
        }

        trace = new FourStepTrace(
            targetText,
            lifts,
            wText,
            vText,
            m,
            descents,
            extra,
            early,
            b,
            aText,
            bText,
            eText,
            nText,
            bTop,
            nTop,
            pinnedText,
            restoredText,
            null);

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
            Q1SubRecipe = q1Fallback,
            Rank = 1,
            Limitations = limitations
        };

        return q1Fallback is null
            ? AttachQ1SubRecipe(order)
            : order;
    }

    private static FourStepTrace EmptyTrace(
        string targetText,
        string failure)
    {
        return new FourStepTrace(
            targetText,
            Array.Empty<BottomLift>(),
            string.Empty,
            string.Empty,
            Array.Empty<FourStepDescent>(),
            Array.Empty<FourStepDescent>(),
            null,
            null,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            string.Empty,
            string.Empty,
            failure);
    }

    private static ShapeDocument BuildFourStepCorner(
        int layers,
        int parts,
        int column,
        string a,
        char[] b,
        char[] e,
        char[] n,
        char color)
    {
        var document = new ShapeDocument(layers, parts);
        int bPart = Wrap(column + 1, parts);
        int ePart = Wrap(column + 2, parts);
        int nPart = Wrap(column - 1, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            document[layer, column] = CellFromCode(
                layer < a.Length ? a[layer] : ShapeSignature.EmptyCode,
                color);
            document[layer, bPart] = CellFromCode(b[layer], color);
            document[layer, ePart] = CellFromCode(e[layer], color);
            document[layer, nPart] = CellFromCode(n[layer], color);
        }

        return document;
    }

    /// <summary>正规列 v 中第 s 层以下最近的一枚非空格所在层；没有时返回 0。</summary>
    private static int NearestMaterialBelow(
        char[] column,
        int source)
    {
        for (int layer = source - 1; layer >= 1; layer--)
        {
            if (column[layer - 1] != ShapeSignature.EmptyCode)
            {
                return layer;
            }
        }

        return 0;
    }
}
