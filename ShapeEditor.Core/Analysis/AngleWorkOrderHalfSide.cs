using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 半边工序的构造族。
///
/// <para>
/// <b>n = 4（k = 2）</b>：只有 <see cref="LiftCornerDrop"/>（v4）一条路——
/// 逆提升 → 两列各自交给角工序 → 交换 → 拆柱 → 一次跌落。
/// 任一步不成立就是 <b>不可制作</b>，不再回退碎晶 / 候选。
/// </para>
/// <para>
/// <b>n ≥ 6（k ≥ 3）</b>：整图装配（<see cref="HalfAssembly"/>）仍用两条
/// 「k 根 S 列拼起来再下落」的论文候选（<see cref="Algo8Self"/> /
/// <see cref="Algo8Uplift"/>）；v4 只替换 n = 4 的构造。
/// </para>
/// <para>
/// <see cref="TempCrystalFuse"/> 是 v3（临时晶体 + 保险丝 + 一刀）的历史枚举值，
/// 保留只为页面层（Web）的既有 switch 能编译；新工序<b>永不</b>产生它。
/// </para>
/// </summary>
public enum HalfCandidate
{
    /// <summary>算法 8 第一候选：Q 自身（k 列都在 S 且 Q 稳定、一次下落仍是 Q）。</summary>
    Algo8Self,

    /// <summary>算法 8 第二候选：联合逆提升（把一对相邻普通格整体抬高）。</summary>
    Algo8Uplift,

    /// <summary>
    /// 【历史】v3 的「临时晶体 + 保险丝 + 一刀」。已废弃：新工序不产生它，
    /// 枚举值只为页面层兼容保留。
    /// </summary>
    TempCrystalFuse,

    /// <summary>
    /// v4：逆提升 → 各自做角 → 交换 → 拆柱 → 一次跌落（n = 4 的唯一构造）。
    /// </summary>
    LiftCornerDrop
}

/// <summary>
/// 半边工序的目标：一段<b>连续 n/2 列</b>（对齐交换链的轴划分）。
///
/// <paramref name="Columns"/> 是原始象限索引，按环顺序排列（k = n/2 列全部列入）；
/// <paramref name="PrimaryColumn"/> 是本次求解的那个角所在象限
/// （半边里第一个被核对的列）。
/// </summary>
public sealed record WorkOrderTarget(
    IReadOnlyList<int> Columns,
    int PrimaryColumn);

/// <summary>
/// 半边工序（k 列一起做，k = n/2）。
///
/// <para>
/// n = 4 走 <see cref="HalfCandidate.LiftCornerDrop"/>：步骤链是
/// <c>逆提升前态 → 交换 → 拆柱 → 跌落目标</c>，全部由真实物理回放；
/// 任一步不过就不产生 <c>IsComplete</c> 的工序。
/// </para>
/// <para>
/// n ≥ 6 走 <see cref="HalfCandidate.Algo8Self"/> / <see cref="HalfCandidate.Algo8Uplift"/>：
/// 步骤链 <c>PreState → Target</c>（前态就是 Q，或 Q′ 一次下落）。
/// </para>
/// <para>
/// <see cref="Note"/> 为 null 表示已完成；非 null 时一律以
/// <see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/> 开头（v4 的失败就是
/// 「做不出」，不是「未覆盖」）。
/// </para>
/// </summary>
public sealed record HalfWorkOrder(
    WorkOrderTarget Target,
    HalfCandidate Candidate,
    IReadOnlyList<AngleWorkStep> Steps,
    bool IsComplete,
    string? Note,
    IReadOnlyList<string> Limitations,
    int Rank)
{
    /// <summary>
    /// k 列是否都是「带子配方」的目标列（n ≥ 6 的候选 1 / 2）。
    /// v4（n = 4）的两列由角工序直接做出，也置 true。
    /// </summary>
    public bool TargetColumnsHaveSubRecipe { get; init; }

    /// <summary>
    /// 切割前需要顺转的象限数；v4 的卡内旋转由卡片层表达，这里恒为 0。
    /// </summary>
    public int Rotation { get; init; }

    /// <summary>
    /// v4：逆提升后、各自交给角工序的两列前态（k 列，其余象限为空）。
    /// 也是「拆柱后跌落前」的输入（<see cref="AngleWorkStepKind.PreState"/> 步）。
    /// </summary>
    public ShapeDocument? LiftedHalf { get; init; }

    /// <summary>v4：两张角卡产物（已归一化到 Q1：目标列 + 左右支撑柱）。</summary>
    public IReadOnlyList<ShapeDocument> CornerProducts { get; init; }
        = Array.Empty<ShapeDocument>();

    /// <summary>v4：两张角卡背后的角工序（与 <see cref="CornerProducts"/> 一一对应）。</summary>
    public IReadOnlyList<AngleWorkOrder> CornerOrders { get; init; }
        = Array.Empty<AngleWorkOrder>();

    /// <summary>v4：两个角产物真实交换后的图形（两根支撑柱各来自一张角卡）。</summary>
    public ShapeDocument? ExchangedShape { get; init; }

    /// <summary>v4：交换后拆掉两根支撑柱、尚未对齐 / 跌落的图形。</summary>
    public ShapeDocument? StrippedShape { get; init; }

    /// <summary>v4：拆柱后、跌落前（已对齐到目标半边朝向）的图形。</summary>
    public ShapeDocument? PreSettleShape { get; init; }

    /// <summary>v4：逆提升的逐步说明（「L4→L5」这类）。</summary>
    public IReadOnlyList<string> LiftNotes { get; init; } = Array.Empty<string>();

    /// <summary>v4：被拆掉的两根支撑柱所在象限（交换产物里的位置）。</summary>
    public IReadOnlyList<int> RemovedPillarColumns { get; init; } = Array.Empty<int>();

    /// <summary>
    /// 这条半边工序从「前态」到「目标半边」实际做的操作，供卡片箭头 / 标题使用。
    /// </summary>
    public string OperationText => Candidate switch
    {
        HalfCandidate.LiftCornerDrop => "交换 + 拆柱 + 跌落",
        HalfCandidate.TempCrystalFuse => "半破",
        HalfCandidate.Algo8Uplift => "下落",
        HalfCandidate.Algo8Self => "无需操作",
        _ => "未知操作"
    };
}

/// <summary>
/// 角工序生成器（续）：<b>半边工序</b>。
///
/// <para><b>n = 4 的唯一构造（v4，docs/half-side-v4-lift-corner-drop.md）</b></para>
/// <code>
/// ① 逆提升：两列里「晶体正下方为空」的位置，把下方最近的普通格抬到晶体正下方
/// ② 各自做角：两列分别用现有角工序（AngleWorkOrderBuilder.Build）做出
///              「目标列 + 左右支撑柱」的角产物
/// ③ 交换：两张角产物真实 ShapeExchange.Exchange → 两根支撑柱各来自一张角卡
/// ④ 拆柱：去掉左柱 + 右柱
/// ⑤ 跌落：一次真实 Collapse → 两列各自落定
/// ⑥ 验收：两列逐格 == 目标半边
/// </code>
/// <para>
/// ② 的「角」是角工序本身：逆提升后每列都属于 S，交给
/// <see cref="AngleWorkOrderBuilder.Build"/> 真实构造（碎晶 / 顶针 / 多刀都留在角卡里）。
/// </para>
/// <para>
/// ① 的抬升集合不是唯一的：某处间隙可以在半边工序里抬、也可以留给角工序自己造。
/// 本实现按「抬升数从少到多」枚举间隙子集，用 ⑥ 的真实逐格验收挑出可行的一组；
/// 这不是另写一套角工序，角仍然只由 <see cref="AngleWorkOrderBuilder.Build"/> 产出。
/// </para>
/// <para>
/// <b>失败判定</b>：① 抬不动 / ② 角做不出 / ⑤ 跌落≠目标 全都写
/// <see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/>（n = 4 不再有「未覆盖」）。
/// </para>
/// <para><b>n ≥ 6（k ≥ 3）不变</b>：仍用两条论文候选（<see cref="HalfCandidate.Algo8Self"/> /
/// <see cref="HalfCandidate.Algo8Uplift"/>），供整图装配使用。</para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>半边的最小象限数（n = 4 才有两列半边的特化；n ≥ 6 走 k 列推广）。</summary>
    private const int HalfMinParts = 4;

    /// <summary>v4 逆提升子集枚举的上限（间隙数；2^6 = 64 个子集）。</summary>
    private const int HalfMaxLiftChoices = 6;

    #region 对外入口

    /// <summary>
    /// 角工序的 n = 4、S ✘ 分支：跑 v4 并把结果包成角工序。
    ///
    /// <para>
    /// 通过时 <see cref="AngleWorkOrder.Half"/> 是完整工序；不通过时返回 null，
    /// 具体失败原因（①/②/⑤）由调用方从 <see cref="SolveHalf"/> 的诊断里取。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildHalfV4Order(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        out string? failureNote)
    {
        HalfWorkOrder half = SolveHalfV4(target, column, cap, kind);

        failureNote = half.Note;

        if (!half.IsComplete ||
            half.Steps.Count == 0 ||
            half.Steps[^1].Shape is null ||
            !SameHalfColumns(
                half.Steps[^1].Shape,
                target,
                HalfColumns(column, target.PartCount),
                target.PartCount))
        {
            return null;
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                "半边工序（v4）前态：两条目标列逆提升后各自交给角工序",
                half.LiftedHalf,
                AngleWorkStepKind.PreState),
            new(
                "交换 + 拆柱 + 一次跌落（v4）：两列逐格等于目标半边",
                target,
                AngleWorkStepKind.Target)
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = 0,
            AuxiliaryColumns = Array.Empty<int>(),
            Rank = 0,
            Half = half,
            Limitations = half.Limitations
        };
    }

    /// <summary>
    /// 独立入口：把一段半边（连续 n/2 列）求解成 <see cref="HalfWorkOrder"/>。
    ///
    /// <para>
    /// n = 4 走 v4（逆提升 → 各自做角 → 交换 → 拆柱 → 跌落）；
    /// n ≥ 6 走两条论文候选。返回 null 表示不可得 / 不适用。
    /// </para>
    /// <para>
    /// 本入口要求「<b>这个目标</b>的那 k 列」：返回的半边逐格等于目标的那 k 列，
    /// 不会用环伙伴改写成别的形状来冒充。
    /// </para>
    /// </summary>
    public static HalfWorkOrder? BuildHalf(
        ShapeDocument target,
        int column,
        int cap)
    {
        HalfWorkOrder? order = SolveHalf(target, column, cap);

        if (order is not { IsComplete: true } ||
            order.Steps.Count == 0 ||
            order.Steps[^1].Shape is null)
        {
            return null;
        }

        if (!SameHalfColumns(
                order.Steps[^1].Shape,
                target,
                HalfColumns(column, target.PartCount),
                target.PartCount))
        {
            return null;
        }

        return order;
    }

    /// <summary>
    /// 诊断入口：返回完整工序，失败时返回带
    /// <see cref="NotProduciblePrefix"/> Note 的未完成记录（不返回 null）。
    /// 命令行 <c>--half</c> 用它打印五步与 VERDICT。
    /// </summary>
    public static HalfWorkOrder? SolveHalf(
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

        int parts = target.PartCount;

        if (parts < HalfMinParts ||
            parts % 2 != 0 ||
            cap < target.LayerCount)
        {
            return null;
        }

        AngleBuildKind kind = AngleClassifier.Classify(target, column);

        if (parts == 4)
        {
            return SolveHalfV4(target, column, cap, kind);
        }

        return SolveHalfKColumns(target, column, cap, kind);
    }

    #endregion

    #region v4：逆提升 → 各自做角 → 交换 → 拆柱 → 跌落

    /// <summary>
    /// v4 的单列「逆提升」候选：<paramref name="Column"/> 是半边内第几列（0 / 1），
    /// <paramref name="Landing"/> 是晶体正下方那一层，<paramref name="Source"/> 是
    /// 下方最近的普通格（−1 表示抬不动）。
    /// </summary>
    private sealed record HalfLiftGap(
        int Column,
        int Landing,
        int Source);

    /// <summary>v4 一次候选（一组抬升子集）的构造结果。</summary>
    private sealed record HalfV4Attempt(
        HalfWorkOrder? Order,
        HalfV4Stage Stage,
        string Reason);

    /// <summary>v4 候选走到了哪一步（用于失败时说明短板）。</summary>
    private enum HalfV4Stage
    {
        /// <summary>① 逆提升（两列都还没到 S）。</summary>
        Lift,

        /// <summary>② 角工序做不出。</summary>
        Corner,

        /// <summary>③ 交换失败。</summary>
        Exchange,

        /// <summary>④ 拆柱失败。</summary>
        Strip,

        /// <summary>⑤ 跌落与目标不符。</summary>
        Settle,

        /// <summary>⑥ 全部通过。</summary>
        Pass
    }

    /// <summary>
    /// v4 一次尝试的中间数据：成功时装成完整工序，失败时装成「已验证前缀 + Note」，
    /// 让命令行能打印走到哪一步。
    /// </summary>
    private sealed class HalfV4Trace
    {
        public ShapeDocument Half = null!;

        public int Column;

        public AngleBuildKind Kind;

        public int[] Columns = Array.Empty<int>();

        public int Parts;

        public string LiftedU = string.Empty;

        public string LiftedV = string.Empty;

        public List<string> LiftNotes = new();

        public ShapeDocument? LiftedHalf;

        public AngleWorkOrder? OrderU;

        public AngleWorkOrder? OrderV;

        public ShapeDocument? ProductU;

        public ShapeDocument? ProductV;

        public ShapeDocument? Exchanged;

        public ShapeDocument? Stripped;

        public ShapeDocument? PreSettle;

        public ShapeDocument? Settled;
    }

    /// <summary>
    /// v4：n = 4 的半边求解。返回的工序要么逐格通过，要么带 NotProducible 说明。
    /// </summary>
    private static HalfWorkOrder SolveHalfV4(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int[] columns = HalfColumns(column, parts);
        ShapeDocument half = HalfProjection(target, column, cap);

        /*
         * 整段半边全空：这是整图装配里的合法输入（另一半只占一侧）。
         * 没有目标列、没有晶体、没有可跌落的材料 ⇒ Q 自身就是稳定产物，
         * 直接给一条 2 步的空半边（角工序 / 支撑柱都不需要）。
         */
        if (!HasAnyContent(half))
        {
            var emptyLimitations = new List<string>
            {
                "半边工序（v4）：目标半边整段为空，Q 自身稳定，无需逆提升 / 角工序 / 拆柱。",
                "这是整图装配里「另一半为空」的退化输入；不是 229 个非 S 单列目标的形态。"
            };

            var emptyHalf = new HalfWorkOrder(
                new WorkOrderTarget(columns, column),
                HalfCandidate.Algo8Self,
                new List<AngleWorkStep>
                {
                    new("半边工序前态（空半边）", half, AngleWorkStepKind.PreState),
                    new("半边目标（空半边）：无需操作", half, AngleWorkStepKind.Target)
                },
                true,
                null,
                emptyLimitations,
                0)
            {
                TargetColumnsHaveSubRecipe = true,
                Rotation = 0
            };

            return emptyHalf;
        }

        string[] texts =
        {
            ShapeSignature.ColumnText(half, columns[0]),
            ShapeSignature.ColumnText(half, columns[1])
        };

        // ① 找所有「晶体正下方为空」的间隙及其可抬来源。
        var gaps = new List<HalfLiftGap>();

        for (int index = 0; index < 2; index++)
        {
            for (int crystal = 1; crystal < texts[index].Length; crystal++)
            {
                if (texts[index][crystal] != ShapeSignature.CrystalCode ||
                    texts[index][crystal - 1] != ShapeSignature.EmptyCode)
                {
                    continue;
                }

                gaps.Add(new HalfLiftGap(
                    index,
                    crystal - 1,
                    NearestSolidBelow(texts[index], crystal)));
            }
        }

        int choices = gaps.Count > HalfMaxLiftChoices
            ? HalfMaxLiftChoices
            : gaps.Count;

        var masks = Enumerable.Range(0, 1 << choices)
            .OrderBy(PopCount)
            .ThenBy(mask => mask)
            .ToArray();

        HalfV4Attempt? best = null;

        foreach (int mask in masks)
        {
            HalfV4Attempt attempt = TryBuildHalfV4(
                target,
                column,
                cap,
                kind,
                half,
                columns,
                texts,
                gaps,
                mask);

            if (attempt.Order is { IsComplete: true })
            {
                return attempt.Order;
            }

            // 记录「走到最远」的失败（同一步取第一条，保证确定性）。
            if (best is null || attempt.Stage > best.Stage)
            {
                best = attempt;
            }
        }

        string reason = best?.Reason ?? "逆提升没有可行解";

        return FailureHalf(
            target,
            column,
            kind,
            NotProduciblePrefix + reason,
            best?.Order);
    }

    /// <summary>按一组抬升子集造前态，然后走 ②–⑥。任一关卡不过就返回带阶段的结果。</summary>
    private static HalfV4Attempt TryBuildHalfV4(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        ShapeDocument half,
        int[] columns,
        string[] texts,
        List<HalfLiftGap> gaps,
        int mask)
    {
        int parts = target.PartCount;
        var trace = new HalfV4Trace
        {
            Half = half,
            Column = column,
            Kind = kind,
            Columns = columns,
            Parts = parts
        };

        var lifted = new char[2][];

        for (int index = 0; index < 2; index++)
        {
            lifted[index] = texts[index].ToCharArray();
        }

        /*
         * 前态按单元格复制（形状码 / 颜色逐格保留）：
         * 逆提升只是把「下方最近的普通格」搬到晶体正下方，其它格子原样不动。
         */
        ShapeDocument liftedHalf = half.Clone();

        for (int index = 0; index < gaps.Count; index++)
        {
            if ((mask & (1 << index)) == 0)
            {
                continue;
            }

            HalfLiftGap gap = gaps[index];

            if (gap.Source < 0)
            {
                return Fail(
                    target,
                    trace,
                    HalfV4Stage.Lift,
                    "逆提升失败：第 " + (gap.Landing + 1) +
                    " 层无普通格可抬");
            }

            char[] core = lifted[gap.Column];

            // 同一组子集内，抬升源 / 落点必须仍然可用。
            if (core[gap.Landing] != ShapeSignature.EmptyCode ||
                core[gap.Source] != ShapeSignature.SolidCode)
            {
                return Fail(
                    target,
                    trace,
                    HalfV4Stage.Lift,
                    "逆提升失败：第 " + (gap.Source + 1) +
                    " 层的普通格被两处间隙同时占用");
            }

            core[gap.Source] = ShapeSignature.EmptyCode;
            core[gap.Landing] = ShapeSignature.SolidCode;

            int liftColumn = columns[gap.Column];
            ShapeCell liftedCell = liftedHalf[gap.Source, liftColumn].Clone();

            liftedHalf[gap.Source, liftColumn] = ShapeCell.Empty();
            liftedHalf[gap.Landing, liftColumn] = liftedCell;

            trace.LiftNotes.Add(
                (gap.Column == 0 ? "u" : "v") +
                " L" + (gap.Source + 1) + "→L" + (gap.Landing + 1));
        }

        trace.LiftedU = new string(lifted[0]);
        trace.LiftedV = new string(lifted[1]);
        trace.LiftedHalf = liftedHalf;

        // ① 之后两列都要能由角工序做出（逆提升的目的就是让它们属于 S）。
        if (!PillarSCriterion.IsPillarColumn(trace.LiftedU))
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Lift,
                "逆提升失败：u = " + Trim(trace.LiftedU) + " 仍不属于 S");
        }

        if (!PillarSCriterion.IsPillarColumn(trace.LiftedV))
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Lift,
                "逆提升失败：v = " + Trim(trace.LiftedV) + " 仍不属于 S");
        }

        // ② 各自做角：u、v 分别调现有角工序（单列图形，其余象限为空）。
        ShapeDocument cornerU = ProjectColumn(trace.LiftedHalf, columns[0]);
        ShapeDocument cornerV = ProjectColumn(trace.LiftedHalf, columns[1]);

        trace.OrderU = Build(cornerU, columns[0], cap);
        trace.OrderV = Build(cornerV, columns[1], cap);

        if (!trace.OrderU.IsComplete)
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Corner,
                "角 u 不可制作：" + CornerReason(trace.OrderU));
        }

        if (!trace.OrderV.IsComplete)
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Corner,
                "角 v 不可制作：" + CornerReason(trace.OrderV));
        }

        // 角产物 = 目标列（逆提升后）+ 左右支撑柱，归一化到 Q1。
        trace.ProductU = NormalizeToQ1(
            BuildCornerProduct(cornerU, columns[0]),
            columns[0],
            parts);
        trace.ProductV = NormalizeToQ1(
            BuildCornerProduct(cornerV, columns[1]),
            columns[1],
            parts);

        // ③ 交换：两张角产物真实 Exchange，两根支撑柱各来自一张角卡。
        ShapeDocument left = ShapeOps.Rotate(trace.ProductU, 1);
        ShapeDocument right = ShapeOps.Rotate(trace.ProductV, 2);

        (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
            ShapeExchange.Exchange(left, right);

        if (first is null || second is null)
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Exchange,
                "交换失败：" + (exchangeError ?? "未知原因"));
        }

        trace.Exchanged = FindCombined(
            first,
            second,
            trace.LiftedU,
            trace.LiftedV);

        if (trace.Exchanged is null)
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Exchange,
                "交换产物里没有「柱 + u + v + 柱」");
        }

        // ④ 拆柱：去掉交换产物最左 / 最右两根支撑柱。
        trace.Stripped = trace.Exchanged.Clone();

        for (int layer = 0; layer < trace.Stripped.LayerCount; layer++)
        {
            trace.Stripped[layer, 0] = ShapeCell.Empty();
            trace.Stripped[layer, parts - 1] = ShapeCell.Empty();
        }

        // 拆柱后把两列对齐到目标半边朝向（旋转是免费操作，不改变形状）。
        int alignRotation = ShapeOps.Wrap(column - 1, parts);
        trace.PreSettle = ShapeOps.Rotate(trace.Stripped, alignRotation);

        if (!SameHalfColumns(
                trace.PreSettle,
                trace.LiftedHalf,
                columns,
                parts))
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Strip,
                "拆柱后不是「逆提升后的两列」");
        }

        // ⑤ 跌落：一次真实 Collapse → 两列各自落定。
        trace.Settled = ShapePhysics.Collapse(trace.PreSettle).Document!;

        // ⑥ 验收：两列逐格 == 目标半边。
        string? difference = FirstHalfDifference(
            trace.Settled,
            target,
            columns,
            parts);

        if (difference is not null)
        {
            return Fail(
                target,
                trace,
                HalfV4Stage.Settle,
                "拆柱跌落与目标不符：" + difference);
        }

        return new HalfV4Attempt(
            BuildHalfV4Order(trace, null, true),
            HalfV4Stage.Pass,
            string.Empty);
    }

    /// <summary>一次失败：保留已验证前缀（前态 / 中间态）并写 NotProducible 说明。</summary>
    private static HalfV4Attempt Fail(
        ShapeDocument target,
        HalfV4Trace trace,
        HalfV4Stage stage,
        string reason)
    {
        return new HalfV4Attempt(
            BuildHalfV4Order(
                trace,
                NotProduciblePrefix + reason,
                false),
            stage,
            reason);
    }

    /// <summary>
    /// 把 v4 的中间数据装成工序 + <see cref="HalfWorkOrder"/>。
    /// <paramref name="reason"/> 为 null 表示完整工序；非 null 表示失败前缀。
    /// </summary>
    private static HalfWorkOrder BuildHalfV4Order(
        HalfV4Trace trace,
        string? reason,
        bool complete)
    {
        int parts = trace.Parts;
        int column = trace.Column;

        var limitations = new List<string>
        {
            "半边工序（v4：逆提升 → 各自做角 → 交换 → 拆柱 → 跌落）：" +
            "先把两列「晶体正下方为空」的位置抬到晶体正下方，让每列都能由角工序单独做出；" +
            "两张角产物交换后拆掉左右两根支撑柱，一次真实 Collapse 落定。",
            "扫描结果：" + DescribeColumns(trace.Half, trace.Columns, parts) +
            "；逆提升 = " + (trace.LiftNotes.Count == 0
                ? "无需抬升"
                : string.Join("，", trace.LiftNotes)) + "。"
        };

        if (trace.OrderU is not null)
        {
            limitations.Add(
                "② 角 u 路线：" + DescribeRoute(trace.OrderU) +
                (trace.ProductU is null
                    ? string.Empty
                    : "；产物 = " + DescribeColumns(
                        trace.ProductU,
                        new[] { 0 },
                        parts,
                        string.Empty)));
        }

        if (trace.OrderV is not null)
        {
            limitations.Add(
                "② 角 v 路线：" + DescribeRoute(trace.OrderV) +
                (trace.ProductV is null
                    ? string.Empty
                    : "；产物 = " + DescribeColumns(
                        trace.ProductV,
                        new[] { 0 },
                        parts,
                        string.Empty)));
        }

        if (trace.Exchanged is not null)
        {
            limitations.Add(
                "③ 交换后 = " + DescribeColumns(
                    trace.Exchanged,
                    new[] { 0, 1, 2, 3 },
                    parts,
                    string.Empty));
        }

        if (trace.Stripped is not null)
        {
            limitations.Add(
                "④ 拆柱去掉第 1、" + parts + " 象限，剩 " +
                DescribeColumns(trace.Stripped, trace.Columns, parts, string.Empty) + "。");
        }

        limitations.Add(complete
            ? "⑤ 一次真实 Collapse 后两列逐格等于目标半边（没有第二次结算）。"
            : reason ?? string.Empty);

        var halfSteps = new List<AngleWorkStep>();

        if (trace.LiftedHalf is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "半边工序前态（v4：逆提升后，两列各自交给角工序）",
                trace.LiftedHalf,
                AngleWorkStepKind.PreState));
        }

        if (trace.Exchanged is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "交换（v4）：两张角产物真实 Exchange，两根支撑柱各来自一张角卡",
                trace.Exchanged,
                AngleWorkStepKind.Exchange));
        }

        if (trace.Stripped is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "拆柱（v4）：去掉左柱 + 右柱",
                trace.Stripped,
                AngleWorkStepKind.Support));
        }

        if (complete && trace.Settled is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "半边目标（v4）：拆柱后一次 Collapse 落定，k 列逐格等于目标",
                trace.Settled,
                AngleWorkStepKind.Target));
        }
        else
        {
            halfSteps.Add(new AngleWorkStep(
                reason ?? "不可制作",
                null,
                AngleWorkStepKind.Note));
        }

        return new HalfWorkOrder(
            new WorkOrderTarget(trace.Columns, column),
            HalfCandidate.LiftCornerDrop,
            halfSteps,
            complete,
            complete ? null : reason,
            limitations,
            0)
        {
            TargetColumnsHaveSubRecipe = true,
            Rotation = 0,
            LiftedHalf = trace.LiftedHalf,
            CornerProducts = trace.ProductU is null || trace.ProductV is null
                ? Array.Empty<ShapeDocument>()
                : new[] { trace.ProductU, trace.ProductV },
            CornerOrders = trace.OrderU is null || trace.OrderV is null
                ? Array.Empty<AngleWorkOrder>()
                : new[] { trace.OrderU, trace.OrderV },
            ExchangedShape = trace.Exchanged,
            StrippedShape = trace.Stripped,
            PreSettleShape = trace.PreSettle,
            LiftNotes = trace.LiftNotes,
            RemovedPillarColumns = new[] { 0, parts - 1 }
        };
    }

    /// <summary>
    /// 一次角工序的路线说明（构造 / 算法 / 步数），只写给人看。
    /// </summary>
    private static string DescribeRoute(
        AngleWorkOrder order)
    {
        return order.Construction + " / " + order.Algorithm +
               " / " + order.Steps.Count + " 步";
    }

    /// <summary>角工序失败原因（去掉统一前缀，拼进半边的 Note）。</summary>
    private static string CornerReason(
        AngleWorkOrder order)
    {
        string note = order.Note ?? "未给出原因";

        if (note.StartsWith(UncoveredPrefix, StringComparison.Ordinal))
        {
            return note[UncoveredPrefix.Length..];
        }

        if (note.StartsWith(NotProduciblePrefix, StringComparison.Ordinal))
        {
            return note[NotProduciblePrefix.Length..];
        }

        return note;
    }

    /// <summary>
    /// 只保留某一列的图形（其余象限清空），交给角工序单独构造。
    /// 逐格保留形状码 / 颜色。
    /// </summary>
    private static ShapeDocument ProjectColumn(
        ShapeDocument source,
        int column)
    {
        var document = new ShapeDocument(source.LayerCount, source.PartCount);

        for (int layer = 0; layer < source.LayerCount; layer++)
        {
            document[layer, column] = source[layer, column].Clone();
        }

        return document;
    }

    /// <summary>
    /// 角产物（原朝向）：目标列 + 左右两根连续接地的支撑柱（柱高 = 目标列高度）。
    /// 与角卡里的构造 C 对齐。
    /// </summary>
    private static ShapeDocument BuildCornerProduct(
        ShapeDocument source,
        int column)
    {
        int parts = source.PartCount;
        int layers = source.LayerCount;

        var product = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            product[layer, column] = source[layer, column].Clone();
        }

        int height = ShapeNormalization.ColumnHeight(source, column);

        if (height == 0)
        {
            return product;
        }

        int left = ShapeOps.Wrap(column - 1, parts);
        int right = ShapeOps.Wrap(column + 1, parts);

        for (int layer = 0; layer < height; layer++)
        {
            if (product[layer, left].Type == CellType.Empty)
            {
                product[layer, left] = ShapeCell.Shape('C', 'u');
            }

            if (product[layer, right].Type == CellType.Empty)
            {
                product[layer, right] = ShapeCell.Shape('C', 'u');
            }
        }

        return product;
    }

    /// <summary>把角产物旋到「目标列 Q1、右柱 Q2、左柱 Q4」。</summary>
    private static ShapeDocument NormalizeToQ1(
        ShapeDocument product,
        int column,
        int parts)
    {
        return ShapeOps.Rotate(product, ShapeNormalization.Q1Rotation(parts, column));
    }

    /// <summary>
    /// 在交换的两路输出里找「柱 + u + v + 柱」的那一路：
    /// 第 2、3 象限逐格等于逆提升后的 u、v，第 1、4 象限是支撑柱。
    /// </summary>
    private static ShapeDocument? FindCombined(
        ShapeDocument first,
        ShapeDocument second,
        string liftedU,
        string liftedV)
    {
        foreach (ShapeDocument candidate in new[] { first, second })
        {
            if (candidate.PartCount != 4)
            {
                continue;
            }

            if (!SameColumnText(candidate, 1, liftedU) ||
                !SameColumnText(candidate, 2, liftedV))
            {
                continue;
            }

            if (!IsSupportPillar(candidate, 0) ||
                !IsSupportPillar(candidate, 3))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>某一列的文本（去掉尾部空层）是否等于 <paramref name="text"/>。</summary>
    private static bool SameColumnText(
        ShapeDocument shape,
        int column,
        string text)
    {
        return Trim(ShapeSignature.ColumnText(shape, column)) == Trim(text);
    }

    /// <summary>某列是否是「普通格组成的支撑柱」（只允许普通格 / 空，且至少一格）。</summary>
    private static bool IsSupportPillar(
        ShapeDocument shape,
        int column)
    {
        bool any = false;

        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            CellType type = shape[layer, column].Type;

            if (type == CellType.Empty)
            {
                continue;
            }

            if (type != CellType.Shape)
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    #endregion

    #region 列扫描小工具（半边工序专用）

    /// <summary>
    /// <paramref name="crystal"/> 下方最近的普通格：只允许跨过空层；
    /// 先碰到针 / 晶体就返回 −1（抬不动）。
    /// </summary>
    private static int NearestSolidBelow(
        string text,
        int crystal)
    {
        for (int layer = crystal - 2; layer >= 0; layer--)
        {
            if (text[layer] == ShapeSignature.SolidCode)
            {
                return layer;
            }

            if (text[layer] != ShapeSignature.EmptyCode)
            {
                return -1;
            }
        }

        return -1;
    }

    private static int PopCount(
        int value)
    {
        int count = 0;

        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    /// <summary>逐格比较两列的半边目标，返回第一处差异（写进 Note）。</summary>
    private static string? FirstHalfDifference(
        ShapeDocument actual,
        ShapeDocument expected,
        int[] columns,
        int parts)
    {
        int layers = Math.Max(actual.LayerCount, expected.LayerCount);

        foreach (int layer in Enumerable.Range(0, layers))
        {
            foreach (int column in columns)
            {
                ShapeCell left = layer < actual.LayerCount
                    ? actual[layer, ShapeOps.Wrap(column, parts)]
                    : ShapeCell.Empty();
                ShapeCell right = layer < expected.LayerCount
                    ? expected[layer, ShapeOps.Wrap(column, parts)]
                    : ShapeCell.Empty();

                if (!ShapeOps.SameCell(left, right))
                {
                    return "第 " + (layer + 1) + " 层" +
                           (column == columns[0] ? "u" : "v") +
                           "期望 " + Display(right) +
                           " 实际 " + Display(left);
                }
            }
        }

        return null;
    }

    private static string Display(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Empty => ".",
            CellType.Pin => "P",
            CellType.Crystal => "c",
            _ => cell.ShapeCode.ToString()
        };
    }

    /// <summary>把验证通过的 k 列半边装成角工序（外层）与未完成的诊断记录。</summary>
    private static HalfWorkOrder FailureHalf(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        string note,
        HalfWorkOrder? inner)
    {
        int parts = target.PartCount;
        int[] columns = HalfColumns(column, parts);

        if (inner is not null)
        {
            return inner with
            {
                IsComplete = false,
                Note = note
            };
        }

        return new HalfWorkOrder(
            new WorkOrderTarget(columns, column),
            HalfCandidate.LiftCornerDrop,
            new List<AngleWorkStep>
            {
                new(note, null, AngleWorkStepKind.Note)
            },
            false,
            note,
            new List<string> { note },
            0);
    }

    #endregion

    #region k ≥ 3：两条论文候选（n ≥ 6 不变）

    /// <summary>
    /// n ≥ 6 的 k 列半边：两条论文候选（Q 自身 / 联合逆提升）。
    /// v4 只替换 n = 4；整图装配（<see cref="HalfAssembly"/>）仍靠这里。
    /// </summary>
    private static HalfWorkOrder? SolveHalfKColumns(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        AngleWorkOrder? order = TryBuildHalfOrder(
            target,
            column,
            cap,
            kind);

        return order?.Half;
    }

    #endregion

    #region 候选 1 / 候选 2（n ≥ 6 的 k 列半边）

    /// <summary>
    /// k ≥ 3 的半边候选派发：候选 1（Q 自身）→ 候选 2（联合逆提升）。
    /// n = 4 不走这里（v4 已覆盖）。
    /// </summary>
    private static AngleWorkOrder? TryBuildHalfOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount < HalfMinParts ||
            target.PartCount % 2 != 0 ||
            cap < target.LayerCount)
        {
            return null;
        }

        int parts = target.PartCount;
        int[] halfColumns = HalfColumns(column, parts);

        bool MatchesTargetHalf(
            AngleWorkOrder order) =>
            order.Half is not null &&
            SameHalfColumns(
                order.Half.Steps[^1].Shape,
                target,
                halfColumns,
                target.PartCount);

        AngleWorkOrder? self = TryBuildAlgo8Self(
            target,
            column,
            cap,
            kind);

        if (self is not null && MatchesTargetHalf(self))
        {
            return self;
        }

        AngleWorkOrder? uplift = TryBuildAlgo8Uplift(
            target,
            column,
            cap,
            kind);

        if (uplift is not null && MatchesTargetHalf(uplift))
        {
            return uplift;
        }

        return null;
    }

    /// <summary>
    /// 候选 1：Q 自身（论文算法 8 第一候选）。
    ///
    /// <list type="number">
    /// <item>半边的 k 列都属于该 n 的 S；</item>
    /// <item>Q 稳定：一次真实 <see cref="ShapePhysics.Collapse"/> 后仍逐格等于 Q；</item>
    /// <item>都通过 ⇒ 前态 = Q 本身，步骤链 <c>PreState → Target</c>。</item>
    /// </list>
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgo8Self(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        ShapeDocument half = HalfProjection(target, column, cap);

        if (!AllHalfColumnsAreS(half, column, parts))
        {
            return null;
        }

        ShapeDocument? collapsed = ShapePhysics.Collapse(half).Document;

        if (!ShapeOps.Same(collapsed, half))
        {
            return null;
        }

        var limitations = BaseLimitations(
            $"半边工序（候选 1：Q 自身）：半边 = 连续 {width} 列（n/2 = {width}）。" +
            "k 列都属于该象限数下的 S，且 Q 一次下落后仍是 Q，所以前态就是目标半边本身。");

        var columns = HalfColumns(column, parts);

        limitations.Add(
            "扫描结果：" + DescribeColumns(half, columns, parts) +
            $"；{width} 列都通过该 n 的 S 判据（" +
            (parts >= 6 ? "n ≥ 6：五项条件" : "n = 4：五项 ∧ (D∨F)") + "）。");
        limitations.Add(
            "前态即目标半边：一次完整下落逐格核对无变化（Q 稳定）；" +
            "k 列都是带子配方的目标列，允许带洞。");

        return BuildPaperHalfOrder(
            target,
            column,
            kind,
            HalfCandidate.Algo8Self,
            half,
            half,
            null,
            columns,
            limitations);
    }

    /// <summary>
    /// 候选 2：联合逆提升（论文算法 8 第二候选，k 列推广）。
    /// 把较低最高晶体粘连组下方那一对相邻普通格整体抬高，再要求一次真实下落回到 Q。
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgo8Uplift(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        ShapeDocument half = HalfProjection(target, column, cap);
        int[] columns = HalfColumns(column, parts);

        var highest = new int[width];
        int lowest = int.MaxValue;

        for (int index = 0; index < width; index++)
        {
            highest[index] = HighestCrystal(half, columns[index]);

            if (highest[index] < 0)
            {
                return null;
            }

            lowest = Math.Min(lowest, highest[index]);
        }

        bool allEqual = true;

        for (int index = 0; index < width; index++)
        {
            if (highest[index] != lowest)
            {
                allEqual = false;
                break;
            }
        }

        if (allEqual)
        {
            return null;
        }

        for (int lowerIndex = 0; lowerIndex < width; lowerIndex++)
        {
            if (highest[lowerIndex] != lowest)
            {
                continue;
            }

            AngleWorkOrder? order = TryLiftHalf(
                target,
                column,
                cap,
                kind,
                half,
                columns,
                lowest,
                columns[lowerIndex]);

            if (order is not null)
            {
                return order;
            }
        }

        return null;
    }

    /// <summary>候选 2：以较低那枚晶体为起点尝试联合逆提升。</summary>
    private static AngleWorkOrder? TryLiftHalf(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        ShapeDocument half,
        int[] columns,
        int lowerLayer,
        int lowerColumn)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        int bottom = GroupBottom(half, lowerLayer, lowerColumn);

        if (bottom <= 0)
        {
            return null;
        }

        int liftFrom = -1;

        for (int layer = bottom - 1; layer >= 0; layer--)
        {
            bool nonEmpty = false;

            foreach (int part in columns)
            {
                if (half[layer, part].Type != CellType.Empty)
                {
                    nonEmpty = true;
                    break;
                }
            }

            if (nonEmpty)
            {
                liftFrom = layer;
                break;
            }
        }

        if (liftFrom < 0 || liftFrom >= bottom - 1)
        {
            return null;
        }

        for (int layer = liftFrom + 1; layer <= bottom - 1; layer++)
        {
            foreach (int part in columns)
            {
                if (half[layer, part].Type != CellType.Empty)
                {
                    return null;
                }
            }
        }

        int liftTo = bottom - 1;
        int liftedPairFirst = -1;

        for (int index = 0; index + 1 < width; index++)
        {
            int first = columns[index];
            int second = columns[index + 1];

            if (half[liftFrom, first].Type != CellType.Shape ||
                half[liftFrom, second].Type != CellType.Shape)
            {
                continue;
            }

            liftedPairFirst = index;
            break;
        }

        if (liftedPairFirst < 0)
        {
            return null;
        }

        int liftFirst = columns[liftedPairFirst];
        int liftSecond = columns[liftedPairFirst + 1];

        ShapeDocument preState = half.Clone();

        ShapeCell firstCell = half[liftFrom, liftFirst].Clone();
        ShapeCell secondCell = half[liftFrom, liftSecond].Clone();

        preState[liftFrom, liftFirst] = ShapeCell.Empty();
        preState[liftFrom, liftSecond] = ShapeCell.Empty();

        preState[liftTo, liftFirst] = firstCell;
        preState[liftTo, liftSecond] = secondCell;

        if (!AllHalfColumnsAreS(preState, column, parts))
        {
            return null;
        }

        ShapeDocument? collapsed = ShapePhysics.Collapse(preState).Document;

        if (!ShapeOps.Same(collapsed, half))
        {
            return null;
        }

        var limitations = BaseLimitations(
            $"半边工序（候选 2：联合逆提升）：半边 = 连续 {width} 列（n/2 = {width}）。" +
            "把较低晶体粘连组下方那一对相邻普通格整体抬高，使 k 根 S 列拼合后" +
            "一次完整下落恰好得到目标半边。");

        limitations.Add(
            "扫描结果：" + DescribeColumns(half, columns, parts) +
            $"；粘连组最低层 b = 第 {bottom + 1} 层（起点：第 {lowerColumn + 1} 象限第 {lowerLayer + 1} 层）。");
        limitations.Add(
            $"逆提升：a = 第 {liftFrom + 1} 层（b 以下最高非空层，相邻普通格在第 " +
            $"{liftFirst + 1}、{liftSecond + 1} 象限）→ 第 {liftTo + 1} 层。");

        return BuildPaperHalfOrder(
            target,
            column,
            kind,
            HalfCandidate.Algo8Uplift,
            preState,
            half,
            collapsed,
            columns,
            limitations);
    }

    #endregion

    #region 工序单

    /// <summary>把候选 1 / 2 的 k 列半边装成角工序 + <see cref="HalfWorkOrder"/>。</summary>
    private static AngleWorkOrder BuildPaperHalfOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        HalfCandidate candidate,
        ShapeDocument preState,
        ShapeDocument expectedHalf,
        ShapeDocument? collapseStep,
        int[] columns,
        List<string> limitations)
    {
        string candidateName = candidate switch
        {
            HalfCandidate.Algo8Self => "候选 1（Q 自身）",
            HalfCandidate.Algo8Uplift => "候选 2（联合逆提升）",
            _ => throw new ArgumentOutOfRangeException(
                nameof(candidate),
                candidate,
                "BuildPaperHalfOrder 只处理算法 8 的两条候选。")
        };

        string operation = collapseStep is null
            ? "前态就是 Q，无需切割、无需下落"
            : "Q′ 装配后自动下落一次（不是切割，没有 Cut 步）";

        var halfSteps = new List<AngleWorkStep>
        {
            new(
                $"半边工序前态（{candidateName}）：" +
                DescribeColumns(preState, columns, preState.PartCount),
                preState,
                AngleWorkStepKind.PreState),
            new(
                $"半边目标（{candidateName}）：" +
                DescribeColumns(expectedHalf, columns, expectedHalf.PartCount) +
                $"（{operation}；k 列逐格核对类型 / 形状码 / 颜色）",
                expectedHalf,
                AngleWorkStepKind.Target)
        };

        var half = new HalfWorkOrder(
            new WorkOrderTarget(columns, column),
            candidate,
            halfSteps,
            true,
            null,
            limitations,
            0)
        {
            TargetColumnsHaveSubRecipe = true,
            Rotation = 0
        };

        var steps = new List<AngleWorkStep>
        {
            new(
                $"半边工序（{candidateName}）前态：{columns.Length} 列一起做，" +
                "每一列都属于该象限数下的 S",
                preState,
                AngleWorkStepKind.PreState),
            new(
                collapseStep is null
                    ? "k 列逐格等于目标半边，得到目标角（无需切割、无需下落）"
                    : "k 列逐格等于目标半边，得到目标角（一次下落，不是切割）",
                target,
                AngleWorkStepKind.Target)
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = 0,
            AuxiliaryColumns = Array.Empty<int>(),
            Rank = 0,
            Half = half,
            Limitations = limitations
        };
    }

    #endregion

    #region 半边公共几何

    /// <summary>把目标里的半边（从 <paramref name="column"/> 起连续 n/2 列）投影成独立图形。</summary>
    private static ShapeDocument HalfProjection(
        ShapeDocument target,
        int column,
        int cap)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        var half = new ShapeDocument(cap, parts);

        for (int layer = 0;
             layer < target.LayerCount && layer < cap;
             layer++)
        {
            for (int index = 0; index < width; index++)
            {
                int part = Wrap(column + index, parts);

                half[layer, part] = target[layer, part].Clone();
            }
        }

        return half;
    }

    /// <summary>半边覆盖的 k 个象限索引（按环顺序）。</summary>
    private static int[] HalfColumns(
        int column,
        int parts)
    {
        int width = parts / 2;
        var columns = new int[width];

        for (int index = 0; index < width; index++)
        {
            columns[index] = Wrap(column + index, parts);
        }

        return columns;
    }

    /// <summary>
    /// 某个 n 下的「带柱列 S」判据：
    /// n = 4 用论文定理 3（五项 ∧ (D∨F)）；n ≥ 6 用定理 2（S ⟺ 五项）。
    /// </summary>
    private static bool IsSColumn(
        string text,
        int parts)
    {
        return parts >= 6
            ? ShapeSignature.CheckColumn(text).IsLegal
            : PillarSCriterion.IsPillarColumn(text);
    }

    /// <summary>k 列是否全部都属于该 n 的 S。</summary>
    private static bool AllHalfColumnsAreS(
        ShapeDocument half,
        int column,
        int parts)
    {
        int width = parts / 2;

        for (int index = 0; index < width; index++)
        {
            int part = Wrap(column + index, parts);

            if (!IsSColumn(ShapeSignature.ColumnText(half, part), parts))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>某一列最高晶体的层号（0 起），没有晶体返回 −1。</summary>
    private static int HighestCrystal(
        ShapeDocument shape,
        int column)
    {
        for (int layer = shape.LayerCount - 1; layer >= 0; layer--)
        {
            if (shape[layer, column].Type == CellType.Crystal)
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>找包含指定格子的最大粘连组，返回组的最低层 b（0 起）。</summary>
    private static int GroupBottom(
        ShapeDocument shape,
        int layer,
        int column)
    {
        List<List<(int Layer, int Part)>> groups = PhysicsGrid.BuildGroups(
            PhysicsGrid.Clone(shape),
            shape.LayerCount,
            shape.PartCount);

        foreach (List<(int Layer, int Part)> group in groups)
        {
            bool contains = false;
            int bottom = int.MaxValue;

            foreach ((int memberLayer, int memberPart) in group)
            {
                if (memberLayer == layer && memberPart == column)
                {
                    contains = true;
                }

                bottom = Math.Min(bottom, memberLayer);
            }

            if (contains)
            {
                return bottom;
            }
        }

        return -1;
    }

    /// <summary>
    /// 逐格比较「某 k 列」：类型 / 形状码 / 颜色都要一致；
    /// 两侧层数不同时，多出来的层必须为空。
    /// </summary>
    private static bool SameHalfColumns(
        ShapeDocument? shape,
        ShapeDocument? other,
        int[] columns,
        int parts)
    {
        if (shape is null || other is null)
        {
            return false;
        }

        int layers = Math.Max(shape.LayerCount, other.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            foreach (int column in columns)
            {
                ShapeCell left = layer < shape.LayerCount
                    ? shape[layer, Wrap(column, parts)]
                    : ShapeCell.Empty();

                ShapeCell right = layer < other.LayerCount
                    ? other[layer, Wrap(column, parts)]
                    : ShapeCell.Empty();

                if (!ShapeOps.SameCell(left, right))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>把 k 列写成「第 i 象限 = 列文本」，用于说明文字。</summary>
    private static string DescribeColumns(
        ShapeDocument shape,
        int[] columns,
        int parts,
        string prefix = "扫描结果：")
    {
        var lines = new List<string>();

        foreach (int part in columns)
        {
            lines.Add(
                $"第 {part + 1} 象限 = " +
                Trim(ShapeSignature.ColumnText(shape, part)));
        }

        return prefix + string.Join("，", lines);
    }

    /// <summary>这段图形是否有任何非空格（半边整段为空时走退化分支）。</summary>
    private static bool HasAnyContent(
        ShapeDocument shape)
    {
        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            for (int part = 0; part < shape.PartCount; part++)
            {
                if (shape[layer, part].Type != CellType.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>去掉列文本末尾的空层，只用于说明文字。</summary>
    private static string Trim(
        string text)
    {
        return text.TrimEnd(ShapeSignature.EmptyCode);
    }

    /// <summary>把列代码转成单元格（普通格用 <c>C/u</c>，晶体用指定颜色）。</summary>
    private static ShapeCell CellFromCode(
        char code,
        char crystalColor)
    {
        return code switch
        {
            ShapeSignature.CrystalCode => ShapeCell.Crystal(crystalColor),
            ShapeSignature.PinCode => ShapeCell.Pin(),
            ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
            _ => ShapeCell.Empty()
        };
    }

    #endregion
}
