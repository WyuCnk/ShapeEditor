using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 半边工序的候选（docs/half-side-work-order.md §4.2）。
///
/// <para>
/// 论文算法 8 只给了两条候选（目标自身 / 联合逆提升），对 <c>cT.T.cPT</c>
/// 这一格两条都拒绝，但半边确实可得 ⇒ 需要第三条候选。
/// </para>
/// </summary>
public enum HalfCandidate
{
    /// <summary>算法 8 第一候选：Q 自身（两列都属 S 且 Q 稳定、一次下落仍是 Q）。</summary>
    Algo8Self,

    /// <summary>算法 8 第二候选：联合逆提升（把一对普通格整体抬高）。</summary>
    Algo8Uplift,

    /// <summary>
    /// 候选 3：临时晶体 + 保险丝 + 一刀。
    /// 目标列把 (s−2, s) 之间的普通格提到 s−1、洞填临时晶体；
    /// 环伙伴在顶部三层与目标列咬合；保险丝柱在临时晶体那一层放一颗晶体跨刀口。
    /// </summary>
    TempCrystalFuse
}

/// <summary>
/// 半边工序的目标：一段<b>连续 n/2 列</b>（对齐交换链的轴划分）。
///
/// <paramref name="Columns"/> 是原始象限索引，按环顺序排列；
/// <paramref name="PrimaryColumn"/> 是本次求解的那个角所在象限
/// （半边里第一个被核对的列）。
/// </summary>
public sealed record WorkOrderTarget(
    IReadOnlyList<int> Columns,
    int PrimaryColumn);

/// <summary>
/// 半边工序（两列一起做）。
///
/// <para>
/// 半边 = 连续 n/2 列；本记录只描述「两列一起做」的那一刀。
/// 子配方最多一层（docs/angle-work-order-algorithm.md §5.1），
/// 所以这里<b>不递归</b>：<see cref="Steps"/> 的前态全是紧列。
/// </para>
/// <para>
/// <see cref="Note"/> 为 null 表示两列都逐格回放通过；非 null 时一律以
/// <see cref="AngleWorkOrderBuilder.UncoveredPrefix"/> 开头（候选命中形态、
/// 但回放没过），且 <see cref="IsComplete"/> 为 false。
/// </para>
/// </summary>
public sealed record HalfWorkOrder(
    WorkOrderTarget Target,
    HalfCandidate Candidate,
    IReadOnlyList<AngleWorkStep> Steps,
    bool IsComplete,
    string? Note,
    IReadOnlyList<string> Limitations,
    int Rank);

/// <summary>
/// 角工序生成器（续）：<b>半边工序</b>（docs/half-side-work-order.md）。
///
/// <para><b>分层</b>（§4.1 / 本仓库任务的 P1 路由）</para>
/// <code>
/// 五项 ✘                         → NotProducible
/// n ≥ 6                          → 现有算法（不变）
/// n = 4 且 S ✔                   → 现有角工序（不变）
/// n = 4 且 S ✘ 但五项 ✔          → 先试半边工序（候选 3）
///                                   ├ 成功 → 角工序 + Half 字段
///                                   └ 失败 → NotProducible（保持现状语义）
/// </code>
///
/// <para><b>候选 3 的实测配方</b>（§2，目标 <c>cT.T.cPT</c>，8 层）</para>
/// <code>
/// Q1 目标列   cTccTcPT   ← 把 s−2 的普通格提到 s−1，洞全填临时晶体
/// Q2 环伙伴   TTTcTccc   ← 顶部三层 ccc 与 Q1 的 c-P-T 咬合成环
/// Q3 （空）
/// Q4 保险丝柱 TTTc....   ← 只在临时晶体那一层（s−2）放一颗晶体，跨刀口
/// 一刀半破 → Q1 = cT.T.cPT ✔，Q2 = TTTT.ccc
/// </code>
///
/// <para><b>为什么这样能成</b></para>
/// <list type="number">
/// <item>Q1 的临时晶体与 Q2/Q4 在 s−2 的晶体同属一个跨刀口连通块，
/// 半破时整块碎掉；Q1 的原生晶体都在西侧、不跨刀口，一颗不少。</item>
/// <item>Q1 抬到 s−1 的普通格与 Q2 同层普通格横向粘连，两格成一组整体下落一层；
/// Q2 在 s−3 的普通格当刹车，双格恰好停在 s−2（即目标层）。</item>
/// <item>环顶：Q1 的 s+1 是顶针（不参与横向粘连），Q2 在 s..s+2 的竖直三连晶
/// 从上方绕过去，与 Q1 的 s 层晶体、s+2 层普通格构成「针 ↔ 晶体段」的循环支撑，
/// 碎晶后顶层晶体段保持原位。</item>
/// </list>
///
/// <para>
/// <b>禁止打表</b>：<c>s</c> 由沿列扫描得出（最高晶体层），其余层号都是相对
/// <c>s</c> 的结构偏移；分支谓词只有「顶部三层是不是 c-P-T」「s−1 是不是洞」
/// 「临时晶体段是否连续」这类扫描结论。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>半边工序固定是 4 象限的一半（本候选只在 n = 4 启用）。</summary>
    private const int HalfParts = 4;

    /// <summary>保留侧（西半边）的列数：连续 n/2 列。</summary>
    private const int HalfWidth = 2;

    #region 候选 3：临时晶体 + 保险丝 + 一刀

    /// <summary>
    /// 候选 3：临时晶体 + 保险丝 + 一刀。
    ///
    /// <para>
    /// 返回 null 表示目标列<b>不在候选 3 的形态范围</b>里（顶部三层不是 c-P-T、
    /// s−1 不是洞、临时晶体段不连续……），调用方按原语义报「不可制作」。
    /// 返回非 null 时：要么是回放验证通过的完整工序（含 <see cref="HalfWorkOrder"/>），
    /// 要么是形态命中但回放没过的「未覆盖」工序（IsComplete = false，只保留已验证前缀）。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildHalfOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount != HalfParts ||
            cap < target.LayerCount)
        {
            return null;
        }

        string text = ShapeSignature.ColumnText(target, column);

        // 只处理「五项 ✔ 且不属于 S」的合法角（S 由调用方先判掉）。
        if (!ShapeSignature.CheckColumn(text).IsLegal ||
            PillarSCriterion.IsPillarColumn(text))
        {
            return null;
        }

        /*
         * 顶部三层的环：
         *   s   = 最高晶体层（沿列扫描，不是查表）
         *   s+1 = 顶针（不参与横向粘连 ⇒ 环必须从 Q2 的竖直晶体段绕过去）
         *   s+2 = 普通格（与 Q2 顶层晶体横向咬合，闭合循环支撑）
         */
        int s = text.LastIndexOf(ShapeSignature.CrystalCode);

        if (s < 2 || s + 2 != text.Length - 1)
        {
            return null;
        }

        if (text[s + 1] != ShapeSignature.PinCode ||
            text[s + 2] != ShapeSignature.SolidCode)
        {
            return null;
        }

        /*
         * 抬升：目标在 s−1 必须留洞、s−2 必须是普通格。
         * 把 s−2 的普通格搬到 s−1（与 Q2 同层普通格配对），s−2 让出的位置
         * 和其余洞一起填临时晶体——这正是「不写打表、只按结构扫描」的形态谓词。
         */
        if (text[s - 1] != ShapeSignature.EmptyCode ||
            text[s - 2] != ShapeSignature.SolidCode)
        {
            return null;
        }

        var preColumn = new char[cap];

        Array.Fill(preColumn, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < text.Length; layer++)
        {
            preColumn[layer] = text[layer];
        }

        preColumn[s - 1] = text[s - 2];
        preColumn[s - 2] = ShapeSignature.EmptyCode;

        for (int layer = 0; layer < s; layer++)
        {
            if (preColumn[layer] == ShapeSignature.EmptyCode)
            {
                preColumn[layer] = ShapeSignature.CrystalCode;
            }
        }

        // 临时晶体段必须连续、且贴着 s−2：整段才会与保险丝同属一个连通块。
        var temporaryLayers = new List<int>();

        for (int layer = 0; layer < s; layer++)
        {
            if (preColumn[layer] == ShapeSignature.CrystalCode &&
                text[layer] != ShapeSignature.CrystalCode)
            {
                temporaryLayers.Add(layer);
            }
        }

        if (temporaryLayers.Count == 0 ||
            temporaryLayers[^1] != s - 2)
        {
            return null;
        }

        for (int index = 0; index < temporaryLayers.Count; index++)
        {
            if (temporaryLayers[index] !=
                s - 2 - (temporaryLayers.Count - 1 - index))
            {
                return null;
            }
        }

        // 临时晶体段下端不能紧贴目标原生晶体：纵向融合会把原生晶体一起炸掉。
        int blockBottom = temporaryLayers[0];

        if (blockBottom > 0 &&
            text[blockBottom - 1] == ShapeSignature.CrystalCode)
        {
            return null;
        }

        int partnerColumn = Wrap(column + 1, HalfParts);
        int emptyColumn = Wrap(column + 2, HalfParts);
        int fuseColumn = Wrap(column - 1, HalfParts);
        int rotation = Wrap(-column, HalfParts);
        char color = CrystalColor(target, column);

        /*
         * Q2 环伙伴（紧列）：
         *   0 … s−3  普通格（s−3 这一枚是双格下落时的刹车）
         *   s−2      晶体（与临时晶体同层，一刀碎掉，腾出落点）
         *   s−1      普通格（与 Q1 抬升的普通格横向粘连、配对下落）
         *   s … s+2  三连晶（顶部咬合 Q1 的 c-P-T，构成循环支撑）
         */
        var partner = new char[cap];
        var fuse = new char[cap];

        Array.Fill(partner, ShapeSignature.EmptyCode);
        Array.Fill(fuse, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < s - 2; layer++)
        {
            partner[layer] = ShapeSignature.SolidCode;
        }

        partner[s - 2] = ShapeSignature.CrystalCode;
        partner[s - 1] = ShapeSignature.SolidCode;
        partner[s] = ShapeSignature.CrystalCode;
        partner[s + 1] = ShapeSignature.CrystalCode;
        partner[s + 2] = ShapeSignature.CrystalCode;

        /*
         * Q4 保险丝柱（紧列）：只在临时晶体所在的「s−2」层放一颗晶体，
         * 与 Q1 的临时晶体横向相邻（跨刀口），其余层用普通格垫满。
         */
        for (int layer = 0; layer <= s - 2; layer++)
        {
            fuse[layer] = ShapeSignature.SolidCode;
        }

        fuse[s - 2] = ShapeSignature.CrystalCode;

        string partnerText = new string(partner);
        string fuseText = new string(fuse);

        var preState = new ShapeDocument(cap, HalfParts);

        /*
         * Q1：保留目标列的原生格（形状码 / 颜色逐格沿用），
         * 抬升的那一格沿用 s−2 的原生格，临时晶体用目标晶体颜色。
         */
        for (int layer = 0; layer < cap; layer++)
        {
            preState[layer, column] =
                layer < target.LayerCount &&
                layer != s - 1 &&
                text[layer] == preColumn[layer]
                    ? target[layer, column].Clone()
                    : CellFromCode(preColumn[layer], color);
        }

        preState[s - 1, column] = target[s - 2, column].Clone();

        WriteColumn(preState, partnerColumn, partnerText, color);
        WriteColumn(preState, fuseColumn, fuseText, color);

        string expectedPartner = BuildHalfPartnerColumn(s, cap);

        ShapeDocument expectedHalf = BuildHalfExpectation(
            target,
            column,
            cap,
            partnerColumn,
            expectedPartner,
            color);

        var auxiliaryColumns = new[]
        {
            partnerColumn,
            emptyColumn,
            fuseColumn
        };

        var limitations = BaseLimitations(
            "半边工序（候选 3：临时晶体 + 保险丝 + 一刀）：半边 = 连续 2 列（n/2），" +
            "与交换链的轴划分一致。Q1 抬升 + 临时晶体、Q2 顶部 c-P-T ↔ c-c-c 咬合、" +
            "Q4 只在临时晶体那一层放保险丝晶体；半破后两列逐格核对。");

        limitations.Add(
            $"扫描结果：s = 第 {s + 1} 层（最高晶体），抬升层 = 第 {s - 1} 层 → 第 {s} 层，" +
            $"临时晶体段 = 第 {blockBottom + 1}–{s - 1} 层。");
        limitations.Add(
            $"前态：Q1 = {new string(preColumn).TrimEnd(ShapeSignature.EmptyCode)}，" +
            $"Q2（第 {partnerColumn + 1} 象限）= {partnerText.TrimEnd(ShapeSignature.EmptyCode)}，" +
            $"Q4（第 {fuseColumn + 1} 象限）= {fuseText.TrimEnd(ShapeSignature.EmptyCode)}，" +
            $"第 {emptyColumn + 1} 象限留空。");
        limitations.Add(
            $"切割轴：顺转 {rotation} 个象限，使保留侧（西 = 第 1、2 位）正好是" +
            $"「目标列 + 环伙伴」，保险丝柱落在被丢弃的一侧（第 4 位）。");
        limitations.Add(
            $"半破后的两列：第 {column + 1} 象限 = {text}，" +
            $"第 {partnerColumn + 1} 象限 = {expectedPartner.TrimEnd(ShapeSignature.EmptyCode)}" +
            "（都按类型 / 形状码 / 颜色逐格核对）。");
        limitations.Add(
            "辅助列全程紧列（docs §5 规则六 / §6 第 7 条 / §17.7）：" +
            "Q2 需要让位的那一层放晶体、Q4 的保险丝层放晶体，半破时与临时晶体一起碎掉。");

        // ── 验收条件一 / 七：前态每列合法且紧列，目标列 ≠ 目标 ──
        if (ShapeSignature.ColumnText(preState, column) == text ||
            !ShapeSignature.AllColumnsLegal(preState, out _) ||
            !ShapeOps.IsStable(preState) ||
            HasIllegalColumnHoles(
                preState,
                column,
                targetColumnHasSubRecipe: false,
                out _))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "前态不过验收（前态目标列 = 目标 / 有非法列 / 不稳定 / 辅助列有洞），" +
                "没有可保留的已验证前缀",
                preState: null,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                limitations);
        }

        // ── 一刀半破：真实 ShapePhysics.Cut ──
        ShapeDocument rotated = ShapeOps.Rotate(preState, rotation);

        int targetRotated = Wrap(column + rotation, HalfParts);
        int partnerRotated = Wrap(partnerColumn + rotation, HalfParts);
        int fuseRotated = Wrap(fuseColumn + rotation, HalfParts);

        // 保留侧必须同时含目标列与环伙伴；保险丝柱必须落在被丢弃的一侧。
        if (targetRotated >= HalfWidth ||
            partnerRotated >= HalfWidth ||
            fuseRotated < HalfWidth)
        {
            return null;
        }

        /*
         * 保险丝必须真的跨刀口：旋转后它在被丢弃侧（第 4 位），
         * 同层与目标列的临时晶体横向相邻——这样半破才会把它连临时晶体整段碎掉。
         */
        if (rotated[s - 2, fuseRotated].Type != CellType.Crystal ||
            rotated[s - 2, targetRotated].Type != CellType.Crystal)
        {
            return null;
        }

        CutResult cut = ShapePhysics.Cut(rotated);

        if (!cut.Success || cut.West is null)
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "真实 Cut 失败：" + (cut.Error ?? "未知原因"),
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                limitations);
        }

        ShapeDocument west = cut.West;

        if (!ShapeOps.IsStable(west) ||
            !ShapeSignature.AllColumnsLegal(west, out _))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破后的西半边不稳定或含非法列，两列无法逐格对上目标半边",
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                limitations);
        }

        // 断口两侧的晶体必须全部碎掉：临时晶体 + Q2 的闸门晶体；
        // 目标列的原生晶体一颗不少、Q2 顶部三连晶保留。
        int targetCrystals = CountCrystalsInText(text);

        if (CrystalsInColumn(west, targetRotated) != targetCrystals ||
            CrystalsInColumn(west, partnerRotated) != 3)
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破没有只碎辅助晶体：临时晶体 / 闸门晶体残留，或目标列晶体被误伤",
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                limitations);
        }

        ShapeDocument result = ShapeOps.Rotate(west, -rotation);

        // ── 两列逐格核对（类型 / 形状码 / 颜色）──
        if (!ShapeOps.Same(result, expectedHalf))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破后的两列与目标半边不符：" +
                $"第 {column + 1} 象限 = {ShapeSignature.ColumnText(result, column)}（期望 {text}），" +
                $"第 {partnerColumn + 1} 象限 = {ShapeSignature.ColumnText(result, partnerColumn)}" +
                $"（期望 {expectedPartner}）",
                preState,
                result,
                rotation,
                auxiliaryColumns,
                limitations);
        }

        return BuildHalfOrder(
            target,
            column,
            kind,
            preState,
            result,
            expectedHalf,
            rotation,
            auxiliaryColumns,
            text,
            expectedPartner,
            limitations);
    }

    #endregion

    #region 工序单

    /// <summary>把验证通过的两列半边装成角工序 + <see cref="HalfWorkOrder"/>。</summary>
    private static AngleWorkOrder BuildHalfOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        ShapeDocument preState,
        ShapeDocument cutResult,
        ShapeDocument expectedHalf,
        int rotation,
        int[] auxiliaryColumns,
        string targetText,
        string expectedPartnerText,
        List<string> limitations)
    {
        var halfSteps = new List<AngleWorkStep>
        {
            new(
                "半边工序前态：目标列抬升 + 洞填临时晶体；" +
                "环伙伴顶部三层 ccc 咬合、闸门层放晶体；保险丝柱只在临时晶体那一层放晶体",
                preState,
                AngleWorkStepKind.PreState),

            new(
                (rotation == 0
                    ? string.Empty
                    : $"先顺转 {rotation} 个象限，再") +
                "一刀半破（候选 3）：刀口跨过保险丝晶体与目标列的临时晶体，" +
                "临时晶体整段碎掉，抬升的普通格与环伙伴同层普通格配对只落一层",
                cutResult,
                AngleWorkStepKind.Cut),

            new(
                $"半边目标：第 {column + 1} 象限 = {targetText}，" +
                $"第 {auxiliaryColumns[0] + 1} 象限 = " +
                $"{expectedPartnerText.TrimEnd(ShapeSignature.EmptyCode)}（两列逐格核对）",
                expectedHalf,
                AngleWorkStepKind.Target)
        };

        var half = new HalfWorkOrder(
            new WorkOrderTarget(
                new[] { column, auxiliaryColumns[0] },
                column),
            HalfCandidate.TempCrystalFuse,
            halfSteps,
            true,
            null,
            limitations,
            0);

        var steps = new List<AngleWorkStep>
        {
            new(
                "半边工序（候选 3）前态：两列一起做——目标列抬升 + 临时晶体，" +
                "环伙伴提供顶部三层循环支撑，保险丝柱跨刀口",
                preState,
                AngleWorkStepKind.PreState),

            new(
                "一刀半破：仅碎掉临时晶体 / 闸门晶体 / 保险丝，" +
                $"半破后第 {column + 1} 象限逐格等于目标",
                cutResult,
                AngleWorkStepKind.Cut),

            new(
                "清除环伙伴象限，得到目标角",
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
            // Q1 前态本身是紧列（没有洞），这个标记只是防御性的。
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Half = half,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 候选 3 的形态命中、但回放没通过：如实报「未覆盖」，
    /// <b>只保留真实验证过的前缀</b>（前态 / 真实 Cut 结果），不伪造 Target 步。
    /// </summary>
    private static AngleWorkOrder BuildHalfFailure(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        string reason,
        ShapeDocument? preState,
        ShapeDocument? cutResult,
        int rotation,
        int[] auxiliaryColumns,
        List<string> limitations)
    {
        string note = UncoveredPrefix +
                      "半边工序（候选 3：临时晶体 + 保险丝 + 一刀）回放未通过：" +
                      reason;

        var halfSteps = new List<AngleWorkStep>();

        if (preState is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "半边工序前态（已验证：每列合法、紧列、稳定）",
                preState,
                AngleWorkStepKind.PreState));
        }

        if (cutResult is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "一刀半破的真实 Cut 结果（两列尚未对上目标半边）",
                cutResult,
                AngleWorkStepKind.Cut));
        }

        halfSteps.Add(new AngleWorkStep(
            note,
            null,
            AngleWorkStepKind.Note));

        var half = new HalfWorkOrder(
            new WorkOrderTarget(
                new[] { column, auxiliaryColumns[0] },
                column),
            HalfCandidate.TempCrystalFuse,
            halfSteps,
            false,
            note,
            limitations,
            0);

        var steps = new List<AngleWorkStep>();

        if (preState is not null)
        {
            steps.Add(new AngleWorkStep(
                "半边工序前态（已验证）",
                preState,
                AngleWorkStepKind.PreState));
        }

        if (cutResult is not null)
        {
            steps.Add(new AngleWorkStep(
                "一刀半破的真实 Cut 结果（尚未对上目标半边）",
                cutResult,
                AngleWorkStepKind.Cut));
        }

        steps.Add(new AngleWorkStep(note, null, AngleWorkStepKind.Note));

        var failureLimitations = new List<string>(limitations)
        {
            "未覆盖（尚未实现）：半边工序的候选 3 命中了这个形态，但回放没通过；" +
            "这不是「不可制作」的判定。"
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            note)
        {
            IsComplete = false,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Half = half,
            Limitations = failureLimitations
        };
    }

    #endregion

    #region 列扫描小工具（半边工序专用）

    /// <summary>
    /// 半边第二列（环伙伴）半破后的期望形态：
    /// <c>0 … s−2</c> 是普通格，<c>s−1</c> 是洞（配对下落让出的那一层），
    /// <c>s … s+2</c> 是顶部三连晶。
    /// </summary>
    private static string BuildHalfPartnerColumn(
        int s,
        int cap)
    {
        var cells = new char[cap];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < s - 1; layer++)
        {
            cells[layer] = ShapeSignature.SolidCode;
        }

        cells[s] = ShapeSignature.CrystalCode;
        cells[s + 1] = ShapeSignature.CrystalCode;
        cells[s + 2] = ShapeSignature.CrystalCode;

        return new string(cells);
    }

    /// <summary>
    /// 目标半边的期望图形：第 1 列逐格复制目标角列，第 2 列按
    /// <see cref="BuildHalfPartnerColumn"/> 写入（普通格 <c>C/u</c>、晶体用目标晶体颜色）。
    /// </summary>
    private static ShapeDocument BuildHalfExpectation(
        ShapeDocument target,
        int column,
        int cap,
        int partnerColumn,
        string partnerText,
        char color)
    {
        var expected = new ShapeDocument(cap, HalfParts);

        for (int layer = 0; layer < cap; layer++)
        {
            expected[layer, column] = layer < target.LayerCount
                ? target[layer, column].Clone()
                : ShapeCell.Empty();
        }

        WriteColumn(expected, partnerColumn, partnerText, color);

        return expected;
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

    /// <summary>列文本里晶体的颗数。</summary>
    private static int CountCrystalsInText(
        string text)
    {
        int count = 0;

        foreach (char code in text)
        {
            if (code == ShapeSignature.CrystalCode)
            {
                count++;
            }
        }

        return count;
    }

    #endregion
}
