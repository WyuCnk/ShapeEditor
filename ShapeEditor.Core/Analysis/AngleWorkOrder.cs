using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序里的一步。
///
/// <see cref="Shape"/> 是这一步执行完后的图形快照；<see cref="AngleWorkStepKind.Note"/>
/// 步允许为 null（只承载说明文字）。
/// </summary>
public sealed record AngleWorkStep(
    string Title,
    ShapeDocument? Shape,
    AngleWorkStepKind Kind);

/// <summary>角工序的步骤类型。</summary>
public enum AngleWorkStepKind
{
    /// <summary>碎晶前的角（带辅助象限）。</summary>
    PreState,

    /// <summary>切割（半破）：沿刀口打碎桥柱/辅助列里的晶体。</summary>
    Cut,

    /// <summary>下落：碎晶后材料落位。</summary>
    Collapse,

    /// <summary>结晶：补晶体。</summary>
    Crystallize,

    /// <summary>目标角（清除辅助象限后）。</summary>
    Target,

    /// <summary>说明/限制，不改变图形。</summary>
    Note
}

/// <summary>
/// 碎晶角用的构造（规格 §3）。
/// </summary>
public enum AngleConstruction
{
    /// <summary>没有可用构造。</summary>
    None,

    /// <summary>构造 G：四象限单孔环下落，一次降一层。</summary>
    G,

    /// <summary>构造 F：至少六象限的受控下落，一次降到目标 d 层。</summary>
    F
}

/// <summary>
/// 一个角的工序单。
///
/// <para>
/// <see cref="Note"/> 为 null 表示工序<b>已逐步验证通过</b>；非 null 表示未实现/未通过，
/// 文字里写明卡在哪一步。<see cref="Limitations"/> 始终给出 UI 需要展示的限制说明。
/// </para>
/// <para>
/// <see cref="Rotation"/> 是切割前需要顺转的象限数（顺时针为正，与
/// <see cref="ShapeOps.Rotate"/> 一致）；<see cref="AuxiliaryColumns"/> 是
/// 前态里辅助列所在的原始象限索引。
/// </para>
/// </summary>
public sealed record AngleWorkOrder(
    int Column,
    AngleBuildKind Kind,
    IReadOnlyList<AngleWorkStep> Steps,
    string? Note)
{
    /// <summary>UI 展示用的限制/前提说明；即使工序验证通过也可能非空。</summary>
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    /// <summary>工序是否从头到尾用真实 <see cref="ShapePhysics"/> 验证通过。</summary>
    public bool IsComplete { get; init; }

    /// <summary>实际使用的构造。</summary>
    public AngleConstruction Construction { get; init; } = AngleConstruction.None;

    /// <summary>切割前需要顺转的象限数（顺时针为正）。</summary>
    public int Rotation { get; init; }

    /// <summary>前态里的辅助列（原始象限索引，按环顺序）。</summary>
    public IReadOnlyList<int> AuxiliaryColumns { get; init; } = Array.Empty<int>();
}

/// <summary>
/// 角工序生成器。
///
/// <para>
/// 对简单角只返回一条说明；对碎晶角按象限数选构造：
/// <c>n = 4 → 构造 G</c>，<c>n ≥ 6 → 构造 F</c>。
/// 前态 = 目标列 + 辅助列，画在 cap 高的示意画布上；其余象限留空。
/// </para>
/// <para>
/// 前态的算法是 G/F 的逆变换：目标列晶体塔（最高晶体所在的连续晶体段）正下方必须是空位，
/// 再往下的最高非空格 <c>d</c> 是普通格。把 <c>d</c> 的普通格搬到晶体塔正下方
/// <c>s = t−1</c>，再由 G/F 的碎晶+受控下落把它送回 <c>d</c>，缺口就出现在
/// <c>s</c>（也就是晶体塔正下方）。
/// </para>
/// <para>
/// <b>为什么末步不是结晶</b>：<see cref="ShapePhysics.Crystallize"/> 是全局操作，
/// 会把刚做出来的缺口一起填平。碎晶角的正确顺序是「先有晶体塔（构造 A 逐层结晶）
/// → 再碎晶造缺口」，所以本工序不包含 Collapse 之后的 Crystallize 步；
/// 这一步的原因会以 <see cref="AngleWorkStepKind.Note"/> 步和
/// <see cref="AngleWorkOrder.Limitations"/> 呈现。
/// </para>
/// </summary>
public static class AngleWorkOrderBuilder
{
    public static AngleWorkOrder Build(
        ShapeDocument target,
        int column,
        int cap)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (column < 0 || column >= target.PartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        AngleBuildKind kind = AngleClassifier.Classify(target, column);

        if (kind == AngleBuildKind.Simple)
        {
            return new AngleWorkOrder(
                column,
                kind,
                new[]
                {
                    new AngleWorkStep(
                        "简单角，无需工序",
                        null,
                        AngleWorkStepKind.Note)
                },
                null)
            {
                IsComplete = true
            };
        }

        if (cap < target.LayerCount)
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）小于目标层数（{target.LayerCount}），无法在前态里保留完整目标列。");
        }

        if (!ShapeRules.IsValidLayerCount(cap))
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）不在 {ShapeRules.MinLayerCount}–{ShapeRules.MaxLayerCount} 之间。");
        }

        if (!ShapeRules.IsValidPartCount(target.PartCount))
        {
            return Incomplete(
                column,
                kind,
                $"象限数（{target.PartCount}）必须是偶数。");
        }

        int layers = cap;
        int parts = target.PartCount;
        int half = parts / 2;

        // === 目标列的形态分析 ===

        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        int t = highest;

        while (t > 0 &&
               target[t - 1, column].Type == CellType.Crystal)
        {
            t--;
        }

        int topLength = highest - t + 1;
        int s = t - 1;

        if (t <= 0)
        {
            return Incomplete(
                column,
                kind,
                "最高晶体段从地面开始，缺口不在晶体塔正下方，G/F 不适用。");
        }

        if (target[s, column].Type != CellType.Empty)
        {
            return Incomplete(
                column,
                kind,
                $"最高晶体塔正下方（第 {s + 1} 层）不是空位；G/F 的缺口必须直接位于晶体塔之下。");
        }

        int d = -1;

        for (int layer = s - 1;
             layer >= 0;
             layer--)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                d = layer;
                break;
            }
        }

        if (d < 0)
        {
            return Incomplete(
                column,
                kind,
                "晶体塔下方的窗口里没有普通格，G/F 没有可下降的材料。");
        }

        if (target[d, column].Type != CellType.Shape)
        {
            return Incomplete(
                column,
                kind,
                $"缺口下方的最高非空格（第 {d + 1} 层）不是普通格；G/F 只下降普通格。");
        }

        bool singleLayerGap = d == s - 1;

        // 构造 G 的 B 列要在 t+1 层放针；晶体塔本身至少三连晶（到 t+2），
        // 因此 t+2 必须仍落在 cap 之内。
        bool gPinRoom = s + 2 < layers;

        bool gPossible =
            parts == 4 &&
            topLength >= 3 &&
            singleLayerGap &&
            gPinRoom;

        bool fPossible = parts >= 6;

        if (!gPossible && !fPossible)
        {
            string reason;

            if (parts == 4)
            {
                if (topLength < 3)
                {
                    reason = "构造 G 需要晶体塔至少三连晶（t、t+1、t+2 都是晶体），该列不满足。";
                }
                else if (!singleLayerGap)
                {
                    reason = "构造 G 一次只能把普通格降一层；该列的缺口高于一层，需要多轮 G（尚未实现）。";
                }
                else
                {
                    reason = "构造 G 需要在 t+1 层放针，但晶体塔已经顶到 cap，放不下 G 的支撑环。";
                }
            }
            else
            {
                reason = "象限数既不是 4 也不 ≥ 6，构造 G/F 都不适用。";
            }

            return Incomplete(column, kind, reason);
        }

        var limitations = new List<string>
        {
            "前态只画目标列与辅助列，其余象限留空；它们不参与本订单的验证。",
            "碎晶角必须「先有晶体塔、后造缺口」：目的地的缺口在结晶之后才出现，" +
            "所以本工序不含末步结晶（全局结晶会把缺口填平）。",
            "构造 G/F 的验证直接使用 ShapePhysics 的现有语义：" +
            "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。"
        };

        // === 选构造并搭前态 ===

        AngleConstruction construction = parts == 4
            ? AngleConstruction.G
            : AngleConstruction.F;

        ShapeDocument preState = BuildPreState(
            target,
            layers,
            column,
            s,
            d,
            construction,
            out int bColumn,
            out int eColumn,
            out int nColumn);

        var auxiliaryColumns = construction == AngleConstruction.G
            ? new[] { bColumn, eColumn }
            : new[] { nColumn, bColumn, eColumn };

        int p = half - 2;

        int rotation = Wrap(p - column, parts);

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（带辅助象限）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "前态不稳定（一次下落后会变形），后续碎晶/下落验证无法进行。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        int bRotated = Wrap(bColumn + rotation, parts);

        int brokenFrom = d;
        int brokenTo = construction == AngleConstruction.G
            ? d
            : s - 1;

        // === 切割：先做一次「不塌陷」的结构性半破，再用真实 Cut 核对 ===

        var rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success ||
            realCut.West is null ||
            realCut.East is null)
        {
            return Incomplete(
                column,
                kind,
                "ShapePhysics.Cut 失败：" + (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        bool rawMatchesReal = ShapeOps.Same(rawCollapsed, realCut.West);

        if (!rawMatchesReal)
        {
            return Incomplete(
                column,
                kind,
                "结构性半破与 ShapePhysics.Cut 的结果对不上，不能保证 Cut 步是真实的。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        bool hadBridgeCrystals = true;

        for (int layer = brokenFrom;
             layer <= brokenTo;
             layer++)
        {
            if (rotated[layer, bRotated].Type != CellType.Crystal)
            {
                hadBridgeCrystals = false;
            }
        }

        bool bridgeCrystalsGone = true;

        for (int layer = brokenFrom;
             layer <= brokenTo;
             layer++)
        {
            if (rawWest[layer, bRotated].Type == CellType.Crystal)
            {
                bridgeCrystalsGone = false;
            }
        }

        bool targetCrystalsKept = true;

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            if (rotated[layer, p].Type == CellType.Crystal &&
                rawWest[layer, p].Type != CellType.Crystal)
            {
                targetCrystalsKept = false;
            }
        }

        if (!hadBridgeCrystals ||
            !bridgeCrystalsGone ||
            !targetCrystalsKept)
        {
            return Incomplete(
                column,
                kind,
                "切割没有按预期打碎桥柱晶体（或误伤了目标列晶体），" +
                "因此不保留 Cut 步。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

        // === 下落 ===

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        if (!MatchesTargetColumn(
                collapsedBack,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "下落结果与目标列不符：" + difference,
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "下落结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：材料落位，缺口移到晶体塔正下方",
            collapsedBack,
            AngleWorkStepKind.Collapse));

        steps.Add(new AngleWorkStep(
            "补晶体：本角不需要末步结晶——Crystallize 是全局操作，" +
            "会把刚做出来的缺口重新填平；前态里的晶体塔应由构造 A 的逐层结晶预先做出。",
            null,
            AngleWorkStepKind.Note));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = construction,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #region 前态

    private static ShapeDocument BuildPreState(
        ShapeDocument target,
        int layers,
        int column,
        int s,
        int d,
        AngleConstruction construction,
        out int bColumn,
        out int eColumn,
        out int nColumn)
    {
        int parts = target.PartCount;

        char color =
            target[AngleClassifier.HighestCrystalLayer(target, column), column]
                .ColorCode ?? 'u';

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < target.LayerCount && layer < layers;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        // 逆变换：把 d 层的普通格搬到晶体塔正下方 s 层，d 层空出来。
        preState[s, column] = target[d, column].Clone();
        preState[d, column] = ShapeCell.Empty();

        bColumn = Wrap(column + 1, parts);
        eColumn = Wrap(column + 2, parts);
        nColumn = Wrap(column - 1, parts);

        if (construction == AngleConstruction.G)
        {
            FillPillar(preState, bColumn);
            preState[d, bColumn] = ShapeCell.Crystal(color);

            if (s + 2 < layers)
            {
                // G 的 B 列：第 h 层晶体、第 t+1 层针、其他层普通格。
                preState[s + 2, bColumn] = ShapeCell.Pin();
            }

            FillPillar(preState, eColumn);
            preState[d, eColumn] = ShapeCell.Crystal(color);
        }
        else
        {
            // F：N | A | B | E。
            FillPillar(preState, nColumn);
            preState[s, nColumn] = ShapeCell.Pin();

            FillPillar(preState, bColumn);

            for (int layer = d;
                 layer <= s - 1;
                 layer++)
            {
                preState[layer, bColumn] = ShapeCell.Crystal(color);
            }

            FillPillar(preState, eColumn);
            preState[d, eColumn] = ShapeCell.Crystal(color);
        }

        return preState;
    }

    private static void FillPillar(
        ShapeDocument document,
        int column)
    {
        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            document[layer, column] = ShapeCell.Shape('C', 'u');
        }
    }

    #endregion

    #region 结构性半破

    /// <summary>
    /// 只做「打碎跨刀口晶体 + 取西半边」的结构性半破，<b>不做下落</b>。
    ///
    /// 用于把 Cut 与 Collapse 拆成两步展示；结果会用
    /// <see cref="ShapePhysics.Cut"/> + <see cref="ShapePhysics.Collapse"/> 核对。
    /// </summary>
    private static ShapeDocument CutWestBeforeCollapse(
        ShapeDocument document,
        int half)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        var cells = new ShapeCell[layers, parts];

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                cells[layer, part] = document[layer, part].Clone();
            }
        }

        var visited = new bool[layers, parts];

        var component = new List<(int Layer, int Part)>();

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                if (visited[layer, part] ||
                    cells[layer, part].Type != CellType.Crystal)
                {
                    continue;
                }

                component.Clear();

                bool west = false;
                bool east = false;

                var queue = new Queue<(int Layer, int Part)>();

                queue.Enqueue((layer, part));
                visited[layer, part] = true;

                while (queue.Count > 0)
                {
                    (int currentLayer, int currentPart) = queue.Dequeue();

                    component.Add((currentLayer, currentPart));

                    if (currentPart < half)
                    {
                        west = true;
                    }
                    else
                    {
                        east = true;
                    }

                    int left = (currentPart - 1 + parts) % parts;
                    int right = (currentPart + 1) % parts;

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        left,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        right,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer - 1,
                        currentPart,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer + 1,
                        currentPart,
                        layers,
                        parts);
                }

                if (west && east)
                {
                    foreach ((int layerIndex, int partIndex) in component)
                    {
                        cells[layerIndex, partIndex] = ShapeCell.Empty();
                    }
                }
            }
        }

        var result = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < half;
                 part++)
            {
                result[layer, part] = cells[layer, part];
            }
        }

        return result;
    }

    private static void Enqueue(
        ShapeCell[,] cells,
        bool[,] visited,
        Queue<(int Layer, int Part)> queue,
        int layer,
        int part,
        int layers,
        int parts)
    {
        if (layer < 0 ||
            layer >= layers ||
            part < 0 ||
            part >= parts ||
            visited[layer, part] ||
            cells[layer, part].Type != CellType.Crystal)
        {
            return;
        }

        visited[layer, part] = true;

        queue.Enqueue((layer, part));
    }

    #endregion

    #region 校验与返回

    private static bool MatchesTargetColumn(
        ShapeDocument actual,
        ShapeDocument target,
        int column,
        int layers,
        out string? difference)
    {
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            ShapeCell expected = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            ShapeCell got = actual[layer, column];

            if (!ShapeOps.SameCell(expected, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(expected)}，实际 {Describe(got)}；" +
                    $"实际列 = {ShapeSignature.ColumnText(actual, column)}";

                return false;
            }
        }

        difference = null;

        return true;
    }

    private static string Describe(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Empty => "空",
            CellType.Pin => "顶针",
            CellType.Crystal => $"晶体({cell.ColorCode})",
            _ => $"普通格({cell.ShapeCode},{cell.ColorCode})"
        };
    }

    private static AngleWorkOrder Incomplete(
        int column,
        AngleBuildKind kind,
        string reason,
        List<string>? limitations = null,
        List<AngleWorkStep>? steps = null,
        AngleConstruction construction = AngleConstruction.None,
        int rotation = 0,
        IReadOnlyList<int>? auxiliaryColumns = null,
        AngleWorkStep? diagnostic = null)
    {
        var resultSteps = steps is null
            ? new List<AngleWorkStep>()
            : new List<AngleWorkStep>(steps);

        if (diagnostic is not null)
        {
            resultSteps.Add(diagnostic);
        }

        resultSteps.Add(new AngleWorkStep(
            "未实现：" + reason,
            null,
            AngleWorkStepKind.Note));

        var resultLimitations = limitations is null
            ? new List<string>()
            : new List<string>(limitations);

        if (!resultLimitations.Contains(reason))
        {
            resultLimitations.Add(reason);
        }

        return new AngleWorkOrder(
            column,
            kind,
            resultSteps,
            reason)
        {
            IsComplete = false,
            Construction = construction,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns ?? Array.Empty<int>(),
            Limitations = resultLimitations
        };
    }

    private static int Wrap(
        int value,
        int size)
    {
        return ((value % size) + size) % size;
    }

    #endregion
}
