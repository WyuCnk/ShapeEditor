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

    /// <summary>顶针：一次顶针完成批量下落（构造 H）。</summary>
    Pin,

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

    /// <summary>构造 G：四象限单孔环下落，一次降一层（多层时重复多轮）。</summary>
    G,

    /// <summary>构造 F：至少六象限的受控下落，一次降到目标 d 层。</summary>
    F,

    /// <summary>构造 H：四象限的一次顶针批量下落（一次 Pin 完成整段下降）。</summary>
    H,

    /// <summary>填充+暴破：把塔下缺口填成晶体，再用切割打碎这一段晶体。</summary>
    FillBreak
}

/// <summary>
/// 一个角的工序单。
///
/// <para>
/// <see cref="Note"/> 为 null 表示工序<b>已逐步验证通过</b>；非 null 表示
/// <b>未覆盖</b>（还没实现，或还没验证通过），文字以「未覆盖：」开头，写明卡在哪一步。
/// 本生成器不会把失败写成「不可制作」——唯一的不可制作判据是「存在不合法角」，
/// 那不属于角工序生成器的职责。<see cref="Limitations"/> 始终给出 UI 需要展示的限制说明。
/// </para>
/// <para>
/// <see cref="Rotation"/> 是切割前需要顺转的象限数（顺时针为正，与
/// <see cref="ShapeOps.Rotate"/> 一致）；<see cref="AuxiliaryColumns"/> 是
/// 前态里辅助列所在的原始象限索引（按环顺序）。
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
/// 对简单角只返回一条说明；对碎晶角<b>优先尝试「填充 + 暴破」</b>：
/// 前态把目标列最高晶体以下的缺口填成晶体（必要时先把缺口下方的普通格抬到
/// 缺口顶端），再补上托柱/桥柱；一次 <see cref="ShapePhysics.Cut"/> 打碎这段
/// 跨刀口晶体，<see cref="ShapePhysics.Collapse"/> 后目标列自然成形。
/// </para>
/// <para>
/// 填充+暴破里还有一条「<b>满高托柱</b>」路径，专门处理 n = 4 的 S 形态
/// （最低晶体以下至少两空，例如 <c>.T.c</c>）：目标列本身就是碎晶后的形态，
/// 目标格停在目标层靠的是<b>同层侧向粘连</b>而不是下方支撑。前态直接取
/// 「目标列 + 西侧满高托柱（覆盖到 cap）+ 东侧辅助桥柱」；半破沿 n/2 刀口
/// 切掉东半边后，托柱仍与目标列同层相邻，所以每一格都不会下落。
/// </para>
/// <para>
/// 注意：这类形态<b>不能</b>先把缺口填成紧列（<c>.T.c → cTcc</c>）再碎晶。
/// 紧列里缺口正上方的临时晶体与目标保留晶体竖直相邻、同属一个晶体簇，
/// 一刀切下去会把保留晶体一起打碎（实测只能得到 <c>.T..</c>）。
/// </para>
/// <para>
/// 新方案做不了时，才退回原来的构造（按形态）：
/// </para>
/// <list type="bullet">
/// <item><c>n = 4、单层缺口</c> → 构造 G（一轮环下落）；</item>
/// <item><c>n = 4、多层缺口</c> → 先试构造 H（一次顶针批量下落），
/// 顶针做不了时退回多轮构造 G（每轮降一层）；</item>
/// <item><c>n ≥ 6</c> → 构造 F（一次受控下落到 d 层）；</item>
/// <item><b>缺口不在晶体塔正下方</b>的旧填充方案（只填安全缺口）。</item>
/// </list>
/// <para>
/// 前态 = 目标列 + 辅助列，画在 cap 高的示意画布上；其余象限留空。
/// 构造 G/F/H 的逆变换都是「把落点 d 的普通格搬到晶体塔正下方 s，
/// 再由碎晶/顶针让它受控落回 d」。
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
    /// <summary>
    /// 未覆盖说明的统一前缀。未完成的工序单绝不使用「不可制作」字样：
    /// 是否可制作只由角合法性决定，生成器只报告自己的覆盖范围。
    /// </summary>
    public const string UncoveredPrefix = "未覆盖：";

    /// <summary>
    /// 未覆盖的含义说明：避免 UI 把它误读成「不可制作」。
    /// 唯一能否定的判据是「存在不合法角」，那由分析层单独给出。
    /// </summary>
    public const string UncoveredDisclaimer =
        "未覆盖只说明角工序生成器还没覆盖这个形态，不代表不可制作。";

    /// <summary>
    /// 生成角工序：碎晶角优先尝试填充+暴破，失败后退回原有构造。
    /// </summary>
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

        if (kind == AngleBuildKind.Simple ||
            cap < target.LayerCount ||
            !ShapeRules.IsValidLayerCount(cap) ||
            !ShapeRules.IsValidPartCount(target.PartCount))
        {
            // 这些情况不需要（也无法）构造前态，交给原实现给出说明/错误。
            return BuildLegacy(target, column, cap);
        }

        int layers = cap;
        int parts = target.PartCount;
        int half = parts / 2;

        // === 首选：填充 + 暴破 ===

        AngleWorkOrder fillBreakOrder = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind);

        if (fillBreakOrder.IsComplete)
        {
            return fillBreakOrder;
        }

        // === 兜底：原有构造 H / 多轮 G / F / 旧填充方案 ===

        AngleWorkOrder fallbackOrder = BuildLegacy(
            target,
            column,
            cap);

        if (fallbackOrder.IsComplete)
        {
            return fallbackOrder;
        }

        return AppendFailure(
            fallbackOrder,
            "首选填充+暴破也未通过：" +
            StripUncoveredPrefix(fillBreakOrder.Note ?? "未知原因"));
    }

    /// <summary>
    /// 原有策略：构造 H（一次顶针）、多轮构造 G、构造 F，
    /// 以及缺口不在晶体塔正下方时的旧填充+暴破方案。
    /// </summary>
    private static AngleWorkOrder BuildLegacy(
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

        // t = 最高晶体段的底端（0 起）。
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

        // === 缺口不在晶体塔正下方：填充 + 暴破 ===

        if (target[s, column].Type != CellType.Empty)
        {
            /*
             * 塔底正下方不是空位，G/F/H 的逆变换都要求把 d 层的普通格搬到 s 层，
             * 这里没有 s 层的空位可用。
             *
             * 改用「填充 + 暴破」：把塔下的缺口区域填成晶体（相当于用构造 A
             * 在这里结晶），再用切割把这一整段晶体打碎，缺口就由切割造出来。
             */
            if (parts < 4)
            {
                return Incomplete(
                    column,
                    kind,
                    "缺口不在晶体塔正下方，填充+暴破至少要 4 个象限（目标列 + 托柱 + 桥柱）。");
            }

            return BuildLegacyFillBreak(
                target,
                column,
                layers,
                parts,
                half,
                t,
                kind);
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

        if (parts == 4)
        {
            // 构造 G 的 B 列要在 t+1 层放针；晶体塔本身至少三连晶（到 t+2），
            // 因此 t+2 必须仍落在 cap 之内。
            bool gPinRoom = s + 2 < layers;
            bool gTowerOk = topLength >= 3;

            if (singleLayerGap)
            {
                if (gTowerOk && gPinRoom)
                {
                    return BuildCutPath(
                        target,
                        column,
                        layers,
                        parts,
                        half,
                        s,
                        d,
                        kind,
                        AngleConstruction.G);
                }

                string reason;

                if (!gTowerOk)
                {
                    reason = "构造 G 需要晶体塔至少三连晶（t、t+1、t+2 都是晶体），该列不满足。";
                }
                else
                {
                    reason = "构造 G 需要在 t+1 层放针，但晶体塔已经顶到 cap，放不下 G 的支撑环。";
                }

                return Incomplete(column, kind, reason);
            }

            // === n = 4、多层缺口：优先一次顶针（构造 H），否则多轮 G ===

            AngleWorkOrder hOrder = TryBuildH(
                target,
                column,
                layers,
                parts,
                s,
                d,
                kind);

            if (hOrder.IsComplete)
            {
                return hOrder;
            }

            if (gTowerOk && gPinRoom)
            {
                AngleWorkOrder gOrder = BuildMultiRoundG(
                    target,
                    column,
                    layers,
                    parts,
                    half,
                    t,
                    s,
                    d,
                    kind);

                if (gOrder.IsComplete)
                {
                    return gOrder;
                }

                return AppendFailure(
                    gOrder,
                    "构造 H（一次顶针）也未通过：" +
                    StripUncoveredPrefix(hOrder.Note ?? "不适用"));
            }

            return AppendFailure(
                hOrder,
                "多轮构造 G 也不适用：晶体塔不足三连晶（t、t+1、t+2 都要是晶体）。");
        }

        if (parts >= 6)
        {
            return BuildCutPath(
                target,
                column,
                layers,
                parts,
                half,
                s,
                d,
                kind,
                AngleConstruction.F);
        }

        return Incomplete(
            column,
            kind,
            "象限数既不是 4 也不 ≥ 6，构造 G/F 都不适用。");
    }

    #region 填充 + 暴破（首选）

    /// <summary>
    /// 首选方案：填充 + 暴破。
    ///
    /// <para>
    /// 前态 = 目标列（缺口已填成晶体）+ 托柱（目标列西侧）+ 桥柱（东侧）。
    /// 目标列最高晶体以下的每一段缺口都填成晶体：
    /// </para>
    /// <list type="bullet">
    /// <item><b>直接填</b>：缺口上下都不是保留晶体时，整段缺口填成晶体；</item>
    /// <item><b>抬升填</b>：缺口正下方是普通格时，先把这枚普通格抬到缺口顶端，
    /// 再把它让出的位置填成晶体。托柱在抬升格所在层放顶针，
    /// 顶针不参与水平连通，抬升格在碎晶后才会真正下落。</item>
    /// </list>
    /// <para>
    /// 旋转后一次 <see cref="ShapePhysics.Cut"/> 沿 n/2 刀口把
    /// 「填充晶体 + 桥柱晶体」整块打碎，Collapse 后抬升的普通格落回原位、
    /// 缺口由碎晶造出。每一份候选都用真实 Cut/Collapse 回放并逐格核对目标列，
    /// 核对不过就丢弃，绝不保留未经验证的步骤。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildFillBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "优先使用「填充+暴破」：前态把目标列最高晶体以下的缺口填成晶体，" +
            "Cut 沿固定刀口 n/2 把跨刀口的填充晶体与桥柱晶体整块打碎，" +
            "Collapse 后抬升的普通格落回原位、目标列逐格对齐目标。");

        if (parts < 4)
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破至少要 4 个象限（目标列 + 托柱 + 桥柱）。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        if (highest <= 0)
        {
            return Incomplete(
                column,
                kind,
                "目标列没有可碎晶的晶体塔，填充+暴破不适用。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        // === 满高托柱：n = 4 的 S 形态，目标格靠同层侧向粘连停在目标层 ===

        if (CanUsePillarHolder(target, column, parts, highest))
        {
            AngleWorkOrder holderOrder = TryBuildPillarHolder(
                target,
                column,
                layers,
                parts,
                half,
                kind);

            if (holderOrder.IsComplete)
            {
                return holderOrder;
            }
        }

        // === 目标列最高晶体以下的缺口段 ===

        var gaps = new List<(int Bottom, int Top)>();

        int layer = 0;

        while (layer < highest)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                layer++;
                continue;
            }

            int bottom = layer;

            while (layer + 1 < highest &&
                   target[layer + 1, column].Type == CellType.Empty)
            {
                layer++;
            }

            gaps.Add((bottom, layer));
            layer++;
        }

        if (gaps.Count == 0)
        {
            return Incomplete(
                column,
                kind,
                "目标列最高晶体以下没有缺口，填充+暴破不适用。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        // === 每段缺口的候选填法：优先直接填，其次抬升普通格 ===

        var options = new List<bool[]>(gaps.Count);

        foreach ((int bottom, int top) in gaps)
        {
            bool bottomIsCrystal =
                bottom > 0 &&
                target[bottom - 1, column].Type == CellType.Crystal;

            bool topIsCrystal =
                target[top + 1, column].Type == CellType.Crystal;

            bool direct = !bottomIsCrystal && !topIsCrystal;

            bool lift =
                bottom > 0 &&
                target[bottom - 1, column].Type == CellType.Shape &&
                (bottom < 2 ||
                 target[bottom - 2, column].Type != CellType.Crystal);

            if (!direct && !lift)
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {bottom + 1}–{top + 1} 层的缺口上下都挨着保留晶体，" +
                    "且下方没有可抬升的普通格，填充+暴破会把保留晶体一起打碎。",
                    limitations,
                    construction: AngleConstruction.FillBreak);
            }

            options.Add(
                direct
                    ? (lift ? new[] { false, true } : new[] { false })
                    : new[] { true });
        }

        int combinations = 1;

        foreach (bool[] option in options)
        {
            combinations *= option.Length;
        }

        AngleWorkOrder? bestFailure = null;

        for (int code = 0; code < combinations; code++)
        {
            int remaining = code;

            var plan = new List<(int Bottom, int Top, bool Lift)>(
                gaps.Count);

            for (int index = 0; index < gaps.Count; index++)
            {
                bool[] option = options[index];

                bool lift = option[remaining % option.Length];

                remaining /= option.Length;

                plan.Add((gaps[index].Bottom, gaps[index].Top, lift));
            }

            AngleWorkOrder attempt = TryFillBreakCandidate(
                target,
                column,
                layers,
                parts,
                half,
                kind,
                plan,
                limitations);

            if (attempt.IsComplete)
            {
                return attempt;
            }

            if (bestFailure is null ||
                attempt.Steps.Count > bestFailure.Steps.Count)
            {
                bestFailure = attempt;
            }
        }

        return bestFailure ?? Incomplete(
            column,
            kind,
            "填充+暴破没有可用候选。",
            limitations,
            construction: AngleConstruction.FillBreak);
    }

    /// <summary>
    /// 按给定的填充方案构造前态并真实回放：
    /// PreState → Cut → Collapse → Target，任何一步核对不过都只保留已验证前缀。
    /// </summary>
    private static AngleWorkOrder TryFillBreakCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        List<(int Bottom, int Top, bool Lift)> plan,
        List<string> limitations)
    {
        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        var fills = new List<int>();

        foreach ((int bottom, int top, bool lift) in plan)
        {
            if (!lift)
            {
                for (int layer = bottom; layer <= top; layer++)
                {
                    fills.Add(layer);
                }

                continue;
            }

            // 抬升：普通格搬到缺口顶端，它让出的位置整段填成晶体。
            preState[top, column] = target[bottom - 1, column].Clone();

            for (int layer = bottom - 1; layer <= top - 1; layer++)
            {
                fills.Add(layer);
            }
        }

        foreach (int layer in fills)
        {
            preState[layer, column] = ShapeCell.Crystal(color);
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // 托柱：满高普通柱，留在西半边托住晶体塔与保留晶体。
        FillPillar(preState, holderColumn);

        // 桥柱：满高普通柱，只在填充层带晶体，跨刀口带走填充晶体。
        FillPillar(preState, eColumn);

        foreach ((int bottom, int top, bool lift) in plan)
        {
            _ = bottom;

            if (lift)
            {
                // 顶针不参与水平连通：抬升格在碎晶后才会下落。
                preState[top, holderColumn] = ShapeCell.Pin();
            }
        }

        foreach (int layer in fills)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        int p = half - 1;
        int rotation = Wrap(p - column, parts);
        int pRotated = Wrap(p, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（塔下缺口已填充成晶体，带托柱/桥柱）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的前态不稳定（一次下落后会变形），后续碎晶验证无法进行。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

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
                "填充+暴破的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        // === 结构性核对：填充晶体真的被打碎，保留晶体没有被误伤 ===

        bool hadFills = true;
        bool fillsGone = true;

        foreach (int layer in fills)
        {
            if (rotated[layer, pRotated].Type != CellType.Crystal)
            {
                hadFills = false;
            }

            if (rawWest[layer, pRotated].Type == CellType.Crystal)
            {
                fillsGone = false;
            }
        }

        bool keptCrystals = true;

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            if (target[layer, column].Type == CellType.Crystal &&
                rawWest[layer, pRotated].Type != CellType.Crystal)
            {
                keptCrystals = false;
            }
        }

        if (!hadFills || !fillsGone || !keptCrystals)
        {
            return Incomplete(
                column,
                kind,
                "切割没有只打碎填充晶体（或误伤了保留晶体），因此不保留 Cut 步。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "打碎填充晶体与桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

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
                "填充+暴破后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "填充+暴破结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：碎晶后材料落位，目标列的缺口由切割造出",
            collapsedBack,
            AngleWorkStepKind.Collapse));

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
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 是否可以用「满高托柱」作为碎晶前态。
    ///
    /// 条件是：n = 4、底层为空、最高晶体就在画布顶层（没有尾部）、
    /// 且目标列属于定理 3 的 S（五项条件 ∧ (D ∨ F)）。
    /// 满足时目标列本身就是碎晶后的形态，
    /// 它的每一格只能靠同层侧向粘连停在目标层。
    /// </summary>
    private static bool CanUsePillarHolder(
        ShapeDocument target,
        int column,
        int parts,
        int highest)
    {
        return parts == 4 &&
               highest == target.LayerCount - 1 &&
               target[0, column].Type == CellType.Empty &&
               PillarSCriterion.IsPillarColumn(target, column);
    }

    /// <summary>
    /// 「满高托柱」：前态 = 目标列 + 西侧满高普通柱（覆盖到 cap）+ 东侧辅助桥柱。
    ///
    /// <para>
    /// 托柱从地面连续覆盖到 cap，和目标列同层相邻，所以碎晶/半破之后目标列的
    /// 每一格都和托柱粘在同一个粘连组里，靠<b>侧向粘连</b>停在目标层，
    /// 不会因为下方是空位而下落。半破沿 n/2 刀口只丢弃东半边的辅助桥柱，
    /// 目标列与托柱留在西半边；目标列逐格与目标核对。
    /// </para>
    /// <para>
    /// 必须满足「碎晶后目标列每一目标格同层左右至少一列是实体」；
    /// 这里在 Cut 之后、Collapse 之前逐格检查，检查不过就退回其它构造。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildPillarHolder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "满高托柱的验证直接使用 ShapePhysics.Cut/Collapse：" +
            "前态是「目标列 + 西侧满高托柱 + 东侧辅助桥柱」，半破沿 n/2 刀口" +
            "丢弃东半边，目标列与托柱留在西半边。");

        limitations.Add(
            "该形态属于 n = 4 的 S（最低晶体以下至少两空）：目标列本身就是" +
            "碎晶后的形态，目标格停在目标层靠的是同层侧向粘连，不是下方支撑。" +
            "不能先把缺口填成紧列：临时晶体与保留晶体竖直同簇，" +
            "切割会把保留晶体一起打碎。");

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // 托柱：满高普通柱，覆盖 cap 的每一层（不是只覆盖到目标高度）。
        FillPillar(preState, holderColumn);

        // 桥柱：东侧辅助件，半破后整体丢弃。
        FillPillar(preState, eColumn);

        int p = half - 1;
        int rotation = Wrap(p - column, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（目标列 + 满高托柱，带东侧辅助柱）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱的前态不稳定（一次下落后会变形），无法继续验证。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

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
                "满高托柱的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        // 碎晶后、下落前：目标列每一目标格必须仍在目标层，
        // 且同层左右至少一列是可粘连的实体（托柱与目标列同层相邻，
        // 这是目标格不下落的唯一依据）。
        if (!HasLateralSupport(
                rawBack,
                target,
                column,
                layers,
                out string? supportDifference))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱碎晶后的前态没有托住目标格：" + supportDifference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "满高托柱碎晶结果（未通过验证，仅供参考）",
                    rawBack,
                    AngleWorkStepKind.Note));
        }

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
                "满高托柱下落结果与目标列不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "满高托柱结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，保留目标列与满高托柱，丢弃东侧辅助柱"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "保留目标列与满高托柱，丢弃东侧辅助柱",
            rawBack,
            AngleWorkStepKind.Cut));

        steps.Add(new AngleWorkStep(
            "下落：目标列每一格都由同层托柱侧向托住，不会下落",
            collapsedBack,
            AngleWorkStepKind.Collapse));

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
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 碎晶后、下落前的侧向支撑检查：目标列的每一目标格必须仍在目标层，
    /// 且同层左右至少有一列是可粘连的实体（普通格或晶体；顶针不粘连）。
    /// </summary>
    private static bool HasLateralSupport(
        ShapeDocument afterCut,
        ShapeDocument target,
        int column,
        int layers,
        out string? difference)
    {
        int parts = afterCut.PartCount;

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell expected = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            if (expected.Type == CellType.Empty)
            {
                continue;
            }

            ShapeCell got = afterCut[layer, column];

            if (!ShapeOps.SameCell(expected, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(expected)}，" +
                    $"实际 {Describe(got)}；碎晶后目标列 = " +
                    ShapeSignature.ColumnText(afterCut, column);

                return false;
            }

            ShapeCell left = afterCut[layer, Wrap(column - 1, parts)];
            ShapeCell right = afterCut[layer, Wrap(column + 1, parts)];

            bool leftAttaches =
                left.Type is CellType.Shape or CellType.Crystal;

            bool rightAttaches =
                right.Type is CellType.Shape or CellType.Crystal;

            if (!leftAttaches && !rightAttaches)
            {
                difference =
                    $"第 {layer + 1} 层的目标格碎晶后同层左右都没有实体" +
                    "（会失去侧向支撑）；碎晶后目标列 = " +
                    ShapeSignature.ColumnText(afterCut, column);

                return false;
            }
        }

        difference = null;

        return true;
    }

    #endregion

    #region 构造 G / F（切割路径）

    /// <summary>
    /// 构造 G（单层）或 F（n ≥ 6）的切割路径：前态 → Cut → Collapse → Target。
    /// </summary>
    private static AngleWorkOrder BuildCutPath(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int s,
        int d,
        AngleBuildKind kind,
        AngleConstruction construction)
    {
        var limitations = BaseLimitations(
            construction == AngleConstruction.G
                ? "构造 G 的验证直接使用 ShapePhysics 的现有语义：" +
                  "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。"
                : "构造 F 的验证直接使用 ShapePhysics 的现有语义：" +
                  "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。");

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

        steps.Add(CrystallizeNote());

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

    #endregion

    #region 构造 H（一次顶针）

    /// <summary>
    /// 构造 H：四象限的一次顶针批量下落。
    ///
    /// <para>
    /// 环上依次是 <c>目标 A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N</c>。
    /// 所有层号都按<b>顶针后</b>对齐：顶针把前态第 z 层抬到第 z+1 层。
    /// 目标列的源普通格写在顶针前的 s−1 层，顶针后位于 s，再沿被 E 连带打碎的
    /// B 晶体通道落到 d。
    /// </para>
    /// <para>
    /// 只有 n = 4 时四个辅助列刚好占满整圈；这里专门用于<b>多层缺口</b>
    /// （一次 Pin 完成整段下降）。若前提不满足或回放对不上，返回带
    /// Note 的未完成单，由调用方退回多轮 G。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildH(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int s,
        int d,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "构造 H 的验证直接使用 ShapePhysics.Pin：" +
            "顶针把整体上移一层，超限晶体整簇破碎后下落一次。");

        // === 构造 H 的前提 ===

        if (d < 1)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的落点 d 必须至少是第 2 层（1 起）：顶针会新增第 1 层顶针，" +
                "普通格不能落到第 1 层。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[d, column].Type != CellType.Shape)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 只搬运普通格，该列落点不是普通格。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[0, column].Type is not (CellType.Pin or CellType.Empty))
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的顶针会在第 1 层补顶针（若前态底层非空），" +
                "因此目标底层必须是顶针或空。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[0, column].Type == CellType.Pin)
        {
            if (d == 1)
            {
                return Incomplete(
                    column,
                    kind,
                    "构造 H 的底层顶针与落点 d = 1 冲突（顶针会占住第 1 层）。",
                    limitations,
                    construction: AngleConstruction.H);
            }

            if (target[1, column].Type == CellType.Empty)
            {
                return Incomplete(
                    column,
                    kind,
                    "构造 H 要生成底层顶针，前态第 1 层必须非空，" +
                    "但它会被顶针抬到第 2 层；该列第 2 层为空。",
                    limitations,
                    construction: AngleConstruction.H);
            }
        }
        else if (d > 1 && target[1, column].Type != CellType.Empty)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的底层没有顶针时，前态第 1 层必须为空，" +
                "该列第 2 层却非空，逆变换对不上。",
                limitations,
                construction: AngleConstruction.H);
        }

        // === 前态：目标列整体下移一层（源格放到 s−1），辅助列按顶针后层号对齐 ===

        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 1;
             layer < layers;
             layer++)
        {
            preState[layer - 1, column] = layer < target.LayerCount
                ? target[layer, column].Clone()
                : ShapeCell.Empty();
        }

        // 落点 d 在顶针前是 d−1 层；这里必须先空出来，源格才能落进去。
        preState[d - 1, column] = ShapeCell.Empty();

        // 源普通格：目标 d 层的材料，顶针前位于 s−1 层。
        preState[s - 1, column] = target[d, column].Clone();

        if (target[0, column].Type == CellType.Pin)
        {
            // 前态第 1 层非空 ⇒ 顶针补出目标第 1 层的顶针；
            // 同时这个格子被抬到第 2 层，正好是目标第 2 层。
            preState[0, column] = target[1, column].Clone();
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);
        int nColumn = Wrap(column - 1, parts);

        // B：第 z 层放晶体 ⇔ 顶针后的 z+1 ∈ I = [d, s−1]。
        FillPillar(preState, bColumn);

        for (int layer = d - 1;
             layer <= s - 2;
             layer++)
        {
            preState[layer, bColumn] = ShapeCell.Crystal(color);
        }

        // E：满高纯晶体柱；顶针后顶端超限，整根晶体柱碎掉。
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        // N：第 z 层放针 ⇔ 顶针后的 z+1 ∈ J = {s}；其他层普通格。
        FillPillar(preState, nColumn);
        preState[s - 1, nColumn] = ShapeCell.Pin();

        var auxiliaryColumns = new[] { nColumn, bColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（带辅助象限，按顶针后层号对齐）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的前态不稳定（一次下落后会变形），后续顶针验证无法进行。",
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        // === 一次顶针 ===

        PhysicsResult pinResult = ShapePhysics.Pin(preState);

        if (!pinResult.Success || pinResult.Document is null)
        {
            return Incomplete(
                column,
                kind,
                "ShapePhysics.Pin 失败：" + (pinResult.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        ShapeDocument pinned = pinResult.Document;

        if (!MatchesTargetColumn(
                pinned,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "一次顶针后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns,
                new AngleWorkStep(
                    "一次顶针结果（未通过验证，仅供参考）",
                    pinned,
                    AngleWorkStepKind.Note));
        }

        // 结构性核对：B 的晶体桥必须已被 E 的整柱破碎连带删除。
        int bRotated = bColumn;

        bool bridgeGone = true;

        for (int layer = d;
             layer <= s - 1;
             layer++)
        {
            if (pinned[layer, bRotated].Type == CellType.Crystal)
            {
                bridgeGone = false;
            }
        }

        if (!bridgeGone)
        {
            return Incomplete(
                column,
                kind,
                "顶针后晶体桥没有全部删除，构造 H 的前提不成立。",
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        steps.Add(new AngleWorkStep(
            "顶针：一次顶针把整体抬一层，E 顶端超限碎裂并连带删掉 B 的晶体桥，" +
            "源普通格沿通道落到目标 d 层",
            pinned,
            AngleWorkStepKind.Pin));

        steps.Add(CrystallizeNote());

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
            Construction = AngleConstruction.H,
            Rotation = 0,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #endregion

    #region 多轮构造 G

    /// <summary>
    /// 多轮构造 G：缺口高 k 层就做 k 轮，每轮只把源普通格降一层。
    ///
    /// <para>
    /// 第 k 轮的源格在 <c>s−k</c> 层，空位 h 取它正下方（<c>s−k−1</c>）；
    /// 每轮都重新装 B（第 h 层晶体、第 t+1 层针）与 E（第 h 层晶体），
    /// 切割碎晶后源格整体下降一层，空位被搬到晶体塔正下方。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildMultiRoundG(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int t,
        int s,
        int d,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "多轮构造 G 的验证直接使用 ShapePhysics 的现有语义：" +
            "每轮 Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。");

        char color = CrystalColor(target, column);

        int p = half - 2;
        int rotation = Wrap(p - column, parts);

        // 首轮前态：把 d 层的普通格搬到 s 层，缺口留在 d..s−1。
        var current = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            current[layer, column] = layer < target.LayerCount
                ? target[layer, column].Clone()
                : ShapeCell.Empty();
        }

        current[s, column] = target[d, column].Clone();
        current[d, column] = ShapeCell.Empty();

        var steps = new List<AngleWorkStep>();

        int rounds = s - d;

        for (int round = 0;
             round < rounds;
             round++)
        {
            int sourceLayer = s - round;
            int h = sourceLayer - 1;

            ShapeDocument preState = BuildRoundGPreState(
                current,
                layers,
                parts,
                column,
                t,
                h,
                color);

            int bColumn = Wrap(column + 1, parts);
            int eColumn = Wrap(column + 2, parts);

            var auxiliaryColumns = new[] { bColumn, eColumn };

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? "碎晶前的角（带辅助象限）"
                    : $"第 {round + 1}/{rounds} 轮碎晶前的角（带辅助象限）",
                preState,
                AngleWorkStepKind.PreState));

            if (!ShapeOps.IsStable(preState))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮前态不稳定（一次下落后会变形），后续碎晶/下落验证无法进行。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

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
                    $"第 {round + 1} 轮 ShapePhysics.Cut 失败：" +
                    (realCut.Error ?? "未知原因"),
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            ShapeDocument rawCollapsed =
                ShapePhysics.Collapse(rawWest).Document!;

            if (!ShapeOps.Same(rawCollapsed, realCut.West))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮结构性半破与 ShapePhysics.Cut 的结果对不上。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            int bRotated = Wrap(bColumn + rotation, parts);

            bool bridgeGone =
                rawWest[h, bRotated].Type != CellType.Crystal;

            ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

            ShapeDocument collapsedBack =
                ShapeOps.Rotate(rawCollapsed, -rotation);

            // 本轮预期：源格从 sourceLayer 落到 h，其他格子不动。
            var expected = new ShapeDocument(layers, parts);

            for (int layer = 0;
                 layer < layers;
                 layer++)
            {
                expected[layer, column] = current[layer, column].Clone();
            }

            expected[h, column] = current[sourceLayer, column].Clone();
            expected[sourceLayer, column] = ShapeCell.Empty();

            if (!bridgeGone)
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮切割没有打碎辅助列的晶体桥。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? (rotation == 0
                        ? "切割：沿 n/2 刀口半破，打碎桥柱晶体"
                        : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎桥柱晶体")
                    : $"第 {round + 1}/{rounds} 轮切割：打碎桥柱晶体",
                rawBack,
                AngleWorkStepKind.Cut));

            if (!MatchesColumn(
                    collapsedBack,
                    expected,
                    column,
                    layers,
                    out string? difference))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮下落结果与预期不符：{difference}",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns,
                    new AngleWorkStep(
                        $"第 {round + 1} 轮下落结果（未通过验证，仅供参考）",
                        collapsedBack,
                        AngleWorkStepKind.Note));
            }

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? "下落：材料落位，缺口移到晶体塔正下方"
                    : $"第 {round + 1}/{rounds} 轮下落：源普通格下降一层",
                collapsedBack,
                AngleWorkStepKind.Collapse));

            current = new ShapeDocument(layers, parts);

            for (int layer = 0;
                 layer < layers;
                 layer++)
            {
                current[layer, column] = collapsedBack[layer, column].Clone();
            }
        }

        if (!MatchesTargetColumn(
                current,
                target,
                column,
                layers,
                out string? finalDifference))
        {
            return Incomplete(
                column,
                kind,
                "多轮 G 全部完成后目标列仍与目标不符：" + finalDifference,
                limitations,
                steps,
                AngleConstruction.G,
                rotation,
                new[] { Wrap(column + 1, parts), Wrap(column + 2, parts) },
                new AngleWorkStep(
                    "多轮 G 最终结果（未通过验证，仅供参考）",
                    current,
                    AngleWorkStepKind.Note));
        }

        steps.Add(CrystallizeNote());

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
            Construction = AngleConstruction.G,
            Rotation = rotation,
            AuxiliaryColumns = new[]
            {
                Wrap(column + 1, parts),
                Wrap(column + 2, parts)
            },
            Limitations = limitations
        };
    }

    /// <summary>
    /// 多轮 G 每一轮的前态：当前目标列原样 + B/E 两列辅助，
    /// B 第 h 层晶体、第 t+1 层针，E 第 h 层晶体。
    /// </summary>
    private static ShapeDocument BuildRoundGPreState(
        ShapeDocument current,
        int layers,
        int parts,
        int column,
        int t,
        int h,
        char color)
    {
        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            preState[layer, column] = current[layer, column].Clone();
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);

        FillPillar(preState, bColumn);
        preState[h, bColumn] = ShapeCell.Crystal(color);
        preState[t + 1, bColumn] = ShapeCell.Pin();

        FillPillar(preState, eColumn);
        preState[h, eColumn] = ShapeCell.Crystal(color);

        return preState;
    }

    #endregion

    #region 填充 + 暴破（缺口不在塔下时的兜底）

    /// <summary>
    /// 缺口不在晶体塔正下方时的兜底构造：填充 + 暴破。
    ///
    /// <para>
    /// 前态直接使用目标列，但把塔下的空格<b>填充成晶体</b>；目标列西侧放一根
    /// 满高普通柱（holder）托住塔，东侧放一根只在这些填充层带晶体的桥柱 E。
    /// 半破沿 n/2 刀口把「填充晶体 + E 的晶体」一起打碎，缺口就由切割造出来；
    /// holder 留在西半边，保证塔不会随碎晶一起下落。
    /// </para>
    /// <para>
    /// 只填充<b>不与目标原有晶体上下相邻</b>的空格（safe 候选）；若 safe 为空，
    /// 再退而尝试全部空格。任何候选都必须用真实 Cut 回放并逐格核对目标列。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildLegacyFillBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int t,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "填充+暴破的验证直接使用 ShapePhysics 的现有语义：" +
            "结晶只把目标列塔下的缺口填成晶体；Cut 沿 n/2 刀口把跨刀口的填充晶体" +
            "与桥柱晶体整块打碎，两半各自下落一次。");

        var allEmpty = new List<int>();

        for (int layer = 0;
             layer < t;
             layer++)
        {
            if (target[layer, column].Type == CellType.Empty)
            {
                allEmpty.Add(layer);
            }
        }

        if (allEmpty.Count == 0)
        {
            return Incomplete(
                column,
                kind,
                "缺口不在晶体塔正下方，但塔下没有可填充的空位，无法用填充+暴破。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        var safe = new List<int>();

        foreach (int layer in allEmpty)
        {
            bool belowCrystal =
                layer > 0 &&
                target[layer - 1, column].Type == CellType.Crystal;

            bool aboveCrystal =
                layer + 1 < target.LayerCount &&
                target[layer + 1, column].Type == CellType.Crystal;

            if (!belowCrystal && !aboveCrystal)
            {
                safe.Add(layer);
            }
        }

        var candidates = new List<List<int>>();

        if (safe.Count > 0)
        {
            candidates.Add(safe);
        }

        if (allEmpty.Count > 0 &&
            (safe.Count == 0 || !SameLayers(safe, allEmpty)))
        {
            candidates.Add(allEmpty);
        }

        AngleWorkOrder? bestFailure = null;

        foreach (List<int> fill in candidates)
        {
            AngleWorkOrder attempt = TryLegacyFillBreakCandidate(
                target,
                column,
                layers,
                parts,
                half,
                kind,
                fill,
                limitations);
            if (attempt.IsComplete)
            {
                return attempt;
            }

            if (bestFailure is null ||
                attempt.Steps.Count > bestFailure.Steps.Count)
            {
                bestFailure = attempt;
            }
        }

        return bestFailure ?? Incomplete(
            column,
            kind,
            "填充+暴破没有可用候选。",
            limitations,
            construction: AngleConstruction.FillBreak);
    }

    private static AngleWorkOrder TryLegacyFillBreakCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        List<int> fill,
        List<string> limitations)
    {
        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers &&
             layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        foreach (int layer in fill)
        {
            preState[layer, column] = ShapeCell.Crystal(color);
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // holder：满高普通柱，留在西半边托住晶体塔。
        FillPillar(preState, holderColumn);

        // E：满高普通柱，只在填充层带晶体，用来跨刀口连带打碎填充晶体。
        FillPillar(preState, eColumn);

        foreach (int layer in fill)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        int p = half - 1;
        int rotation = Wrap(p - column, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（塔下缺口已填充成晶体）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的前态不稳定（一次下落后会变形），后续碎晶验证无法进行。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

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
                "填充+暴破的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        int pRotated = Wrap(p, parts);

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        // 填充的晶体必须真的被切割打碎。
        bool fillGone = true;

        foreach (int layer in fill)
        {
            if (rawWest[layer, pRotated].Type == CellType.Crystal)
            {
                fillGone = false;
            }
        }

        if (!fillGone)
        {
            return Incomplete(
                column,
                kind,
                "切割没有把填充的晶体全部打碎。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎填充晶体与桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

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
                "填充+暴破后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "填充+暴破结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：碎晶后材料落位，目标列的缺口由切割造出",
            collapsedBack,
            AngleWorkStepKind.Collapse));

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
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #endregion

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

        char color = CrystalColor(target, column);

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

    private static char CrystalColor(
        ShapeDocument target,
        int column)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        return highest >= 0
            ? target[highest, column].ColorCode ?? 'u'
            : 'u';
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

    private static List<string> BaseLimitations(
        string verificationText)
    {
        return new List<string>
        {
            "前态只画目标列与辅助列，其余象限留空；它们不参与本订单的验证。",
            "碎晶角必须「先有晶体塔、后造缺口」：目的地的缺口在结晶之后才出现，" +
            "所以本工序不含末步结晶（全局结晶会把缺口填平）。",
            verificationText
        };
    }

    private static AngleWorkStep CrystallizeNote()
    {
        return new AngleWorkStep(
            "补晶体：本角不需要末步结晶——Crystallize 是全局操作，" +
            "会把刚做出来的缺口重新填平；前态里的晶体塔应由构造 A 的逐层结晶预先做出。",
            null,
            AngleWorkStepKind.Note);
    }

    private static AngleWorkOrder AppendFailure(
        AngleWorkOrder order,
        string extra)
    {
        var steps = new List<AngleWorkStep>(order.Steps);

        steps.Add(new AngleWorkStep(
            "未覆盖（尚未实现）：" + extra,
            null,
            AngleWorkStepKind.Note));

        var limitations = new List<string>(order.Limitations);

        if (!limitations.Contains(extra))
        {
            limitations.Add(extra);
        }

        /*
         * Note 始终只有一个「未覆盖：」前缀：
         * 既有的失败说明可能已经带前缀，这里先剥掉再统一加一次。
         */
        string previous = order.Note is null
            ? string.Empty
            : StripUncoveredPrefix(order.Note);

        string body = previous.Length == 0
            ? extra
            : previous + "；" + extra;

        string note = UncoveredPrefix + body;

        return new AngleWorkOrder(
            order.Column,
            order.Kind,
            steps,
            note)
        {
            IsComplete = false,
            Construction = order.Construction,
            Rotation = order.Rotation,
            AuxiliaryColumns = order.AuxiliaryColumns,
            Limitations = limitations
        };
    }

    private static string StripUncoveredPrefix(
        string note)
    {
        return note.StartsWith(UncoveredPrefix, StringComparison.Ordinal)
            ? note[UncoveredPrefix.Length..]
            : note;
    }

    private static bool SameLayers(
        List<int> left,
        List<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0;
             index < left.Count;
             index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

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

    private static bool MatchesColumn(
        ShapeDocument actual,
        ShapeDocument expected,
        int column,
        int layers,
        out string? difference)
    {
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            ShapeCell want = expected[layer, column];
            ShapeCell got = actual[layer, column];

            if (!ShapeOps.SameCell(want, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(want)}，实际 {Describe(got)}；" +
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
            "未覆盖（尚未实现）：" + reason,
            null,
            AngleWorkStepKind.Note));

        var resultLimitations = limitations is null
            ? new List<string>()
            : new List<string>(limitations);

        if (!resultLimitations.Contains(reason))
        {
            resultLimitations.Add(reason);
        }

        if (!resultLimitations.Contains(UncoveredDisclaimer))
        {
            resultLimitations.Add(UncoveredDisclaimer);
        }

        return new AngleWorkOrder(
            column,
            kind,
            resultSteps,
            UncoveredPrefix + reason)
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
