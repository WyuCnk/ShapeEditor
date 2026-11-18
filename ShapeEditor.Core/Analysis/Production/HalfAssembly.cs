using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// k 列半边装配（落地清单第 5 条）：「交换链失败 → 用半边工序拼整图」。
///
/// <para><b>触发位置</b>：<see cref="ProductionPipeline"/> 的核心求解里，
/// 交换链（<see cref="ExchangeChain"/>）失败之后；交换链成功时一行都不走。</para>
///
/// <para><b>做法</b>（每条轴、两个方向，有界两次）</para>
/// <code>
/// for 轴 a in 0 … half−1:
///     左半边 L = BuildHalf(target, a, cap)          ← k = n/2 列
///     右半边 R = BuildHalf(target, a+half, cap)
///     if L / R 都可得，且各自半边目标逐格等于目标的两段:
///         方向 A：L 摆到西侧（转 −a），R 摆到东侧（转 −a）
///         方向 B：L 摆到东侧（转 half−a），R 摆到西侧（转 half−a）
///         每个方向都：
///             (输入 A, 输入 B) = (Rotate(L, 旋转量), Rotate(R, 旋转量))
///             真实 Cut：方向 A 取 West / East，方向 B 取 East / West
///             真实 Exchange：方向 A 取 second，方向 B 取 first
///             输出转回后逐格等于目标（或旋转等价）⇒ 成功
///         方向 A 失败才试方向 B；两次都失败 ⇒ 继续下一条轴
/// </code>
///
/// <para><b>为什么两个方向都要试</b></para>
/// <list type="number">
/// <item>半边工序给出的是「半边本身」，但它<b>带着朝向</b>：同一个半边可以放到
/// 西侧，也可以整体转半圈放到东侧。两种摆法的刀口两侧材料不同，
/// 真实 <see cref="ShapePhysics.Cut"/> / <see cref="ShapeExchange.Exchange"/>
/// 的输出也不同，所以必须都回放一次，不能只换标签。</item>
/// <item>两个方向都只做<b>一次</b>真实回放，不是枚举：先当前方向，失败再交换方向，
/// 两次都失败就放弃这条轴。</item>
/// <item>方向 B 的产物是「目标整体旋转 <c>half</c> 个象限」；与目标旋转等价时
/// 按 <see cref="ExchangeChain"/> 的同一口径转回目标本身，废料一起转。</item>
/// </list>
///
/// <para>
/// <b>不做任何「只靠判据」的结论</b>：<see cref="Verify"/> 会用真实
/// <see cref="ShapePhysics.Cut"/> / <see cref="ShapeExchange.Exchange"/> /
/// <see cref="ShapePhysics.Collapse"/> 把整条路径重算一遍，
/// <see cref="Replay.Verify"/> 也走这条校验。
/// </para>
/// </summary>
public static class HalfAssembly
{
    /// <summary>半边装配的最小象限数（n ≥ 4）。</summary>
    private const int MinParts = 4;

    /// <summary>
    /// 尝试用两条轴上的两个 k 列半边装配整图（先试方向 A，失败再试方向 B）。
    /// </summary>
    public static bool TryBuild(
        ShapeDocument target,
        int cap,
        out Recipe? recipe,
        out string note)
    {
        return TryBuild(
            target,
            cap,
            preferSwapped: false,
            out recipe,
            out note);
    }

    /// <summary>
    /// 尝试用两个 k 列半边装配整图。
    ///
    /// <para>
    /// <paramref name="preferSwapped"/> 只影响「先试哪个方向」，不改变有界两次的规则：
    /// 先试 <c>preferSwapped ? 方向 B : 方向 A</c>，失败再试另一个方向。
    /// 默认 <c>false</c>，即先试当前方向（左半边 → 西侧，右半边 → 东侧）。
    /// </para>
    /// </summary>
    /// <param name="target">整图目标。</param>
    /// <param name="cap">机器上限（通常是目标文档层数）。</param>
    /// <param name="preferSwapped">是否先试交换方向（方向 B）。</param>
    /// <param name="recipe">成功时的配方（<see cref="RecipeKind.HalfAssembly"/>）。</param>
    /// <param name="note">结论说明（成功 / 失败都给出）。</param>
    /// <returns>找到并回放验证通过时返回 true。</returns>
    public static bool TryBuild(
        ShapeDocument target,
        int cap,
        bool preferSwapped,
        out Recipe? recipe,
        out string note)
    {
        recipe = null;

        int parts = target.PartCount;
        int half = parts / 2;

        if (parts < MinParts || parts % 2 != 0)
        {
            note = $"n = {parts}：半边装配只处理 n ≥ 4 的偶数象限数。";
            return false;
        }

        if (cap < target.LayerCount)
        {
            note = $"cap（{cap}）小于目标层数（{target.LayerCount}）：无法在前态里保留完整目标。";
            return false;
        }

        var attempts = new List<string>();

        for (int axis = 0; axis < half; axis++)
        {
            int rightColumn = ShapeOps.Wrap(axis + half, parts);

            HalfWorkOrder? left = AngleWorkOrderBuilder.BuildHalf(
                target,
                axis,
                cap);

            HalfWorkOrder? right = AngleWorkOrderBuilder.BuildHalf(
                target,
                rightColumn,
                cap);

            if (left is null || right is null)
            {
                attempts.Add(
                    $"轴{axis}：左半边{(left is null ? "不可得" : "可得")}、" +
                    $"右半边{(right is null ? "不可得" : "可得")}");

                continue;
            }

            int[] leftColumns = HalfColumnIndexes(parts, axis);
            int[] rightColumns = HalfColumnIndexes(parts, rightColumn);

            // 半边工序给出的半边必须真的等于目标的那一段（不能是别的形状）。
            if (!SameHalf(left.Steps[^1].Shape, target, leftColumns, parts) ||
                !SameHalf(right.Steps[^1].Shape, target, rightColumns, parts))
            {
                attempts.Add($"轴{axis}：半边工序的半边与目标分段不一致");
                continue;
            }

            if (!TryGetPreState(left, out ShapeDocument? leftPre, out string leftError))
            {
                attempts.Add($"轴{axis}：左半边没有前态（{leftError}）");
                continue;
            }

            if (!TryGetPreState(right, out ShapeDocument? rightPre, out string rightError))
            {
                attempts.Add($"轴{axis}：右半边没有前态（{rightError}）");
                continue;
            }

            /*
             * 半边工序自己的前态 → 半边目标必须能真实回放。
             * 这是「这一侧确实由半边工序做出来」的唯一凭据；
             * 候选 2 的前态是「下落前」，由真实 Collapse 负责（见 ReplayHalf）。
             */
            if (!ReplayHalf(left, leftPre!, out leftError))
            {
                attempts.Add($"轴{axis}：左半边回放失败（{leftError}）");
                continue;
            }

            if (!ReplayHalf(right, rightPre!, out rightError))
            {
                attempts.Add($"轴{axis}：右半边回放失败（{rightError}）");
                continue;
            }

            /*
             * 装配的输入取「半边工序的最后一步产物」：它是真实 Cut / Collapse 出来的
             * 稳定半边，也是机器上可以独立摆放的半边本身。
             *
             * 不能拿候选 2 的 Q′ 当前态当输入：Q′ 是下落前状态，
             * 它只在「半边工序内部」成立；装配层要摆的是已经落定的半边。
             */
            ShapeDocument leftHalf = left.Steps[^1].Shape!;
            ShapeDocument rightHalf = right.Steps[^1].Shape!;

            string? firstDirectionFailure = null;

            for (int index = 0; index < 2; index++)
            {
                bool swapped = index == 0
                    ? preferSwapped
                    : !preferSwapped;

                int direction = swapped ? 1 : 0;

                if (TryAssembleDirection(
                        target,
                        axis,
                        rightColumn,
                        half,
                        parts,
                        leftColumns,
                        rightColumns,
                        left,
                        right,
                        leftHalf,
                        rightHalf,
                        direction,
                        out Recipe? assembled,
                        out string reason))
                {
                    recipe = assembled;

                    string directionText = DescribeDirection(direction);

                    note =
                        $"交换链失败，改走半边装配：第 {axis} 条轴的两个 {half} 列半边都可得" +
                        $"（左 {Describe(left.Candidate)} / 右 {Describe(right.Candidate)}）；" +
                        $"{directionText}的真实 Cut + Exchange 回放后逐格等于目标。" +
                        (firstDirectionFailure is null
                            ? string.Empty
                            : " 先试的方向未通过（" + firstDirectionFailure + "）。");

                    return true;
                }

                string attemptText =
                    $"轴{axis}/{DescribeDirection(direction)}：{reason}";

                attempts.Add(attemptText);

                if (firstDirectionFailure is null)
                {
                    firstDirectionFailure =
                        $"方向 {DirectionLabel(direction)}，{reason}";
                }
            }
        }

        note = "半边装配：两条轴都没有给出可回放的两个半边（" +
               string.Join("；", attempts) + "）。";

        return false;
    }

    /// <summary>
    /// 单次方向装配：把两半摆到指定一侧，真实 Cut + Exchange + 回放。
    ///
    /// <list type="number">
    /// <item>方向 A（<paramref name="direction"/> = 0）：左半边 → 西侧、
    /// 右半边 → 东侧，整体转 <c>−axis</c>；</item>
    /// <item>方向 B（<paramref name="direction"/> = 1）：左半边 → 东侧、
    /// 右半边 → 西侧，整体转 <c>half−axis</c>（真的把两半换边，含半圈旋转）；</item>
    /// <item>每条输入都要求稳定、能被交换链造出来（rank 0 子配方）；</item>
    /// <item>真实 <see cref="ShapePhysics.Cut"/> 的保留侧必须逐格等于目标分段；</item>
    /// <item>真实 <see cref="ShapeExchange.Exchange"/> 的所需输出转回后逐格等于目标
    /// （或旋转等价，按 <see cref="ExchangeChain"/> 的口径转回）。</item>
    /// </list>
    /// </summary>
    private static bool TryAssembleDirection(
        ShapeDocument target,
        int axis,
        int rightColumn,
        int half,
        int parts,
        int[] leftColumns,
        int[] rightColumns,
        HalfWorkOrder left,
        HalfWorkOrder right,
        ShapeDocument leftHalf,
        ShapeDocument rightHalf,
        int direction,
        out Recipe? recipe,
        out string reason)
    {
        recipe = null;

        /*
         * 方向 A：转 −axis，左半边落在西侧（第 0…half−1 位）；
         * 方向 B：转 half−axis，左半边落在东侧（第 half…n−1 位）。
         * 两边都用同一个旋转量，保证「换边」是整体旋转而不是只换标签。
         */
        int rotation = direction == 0
            ? ShapeOps.Wrap(-axis, parts)
            : ShapeOps.Wrap(half - axis, parts);

        ShapeDocument leftInput = ShapeOps.Rotate(leftHalf, rotation);
        ShapeDocument rightInput = ShapeOps.Rotate(rightHalf, rotation);

        /*
         * 输入必须是机器造得出来的：交换 / 顶针 / 堆叠的输出都塌陷过，
         * 所以输入必须稳定；再用交换链给出 rank 0 子配方（不能只靠判据）。
         */
        if (!ShapeOps.IsStable(leftInput))
        {
            reason = "左半边输入不稳定";
            return false;
        }

        if (!ShapeOps.IsStable(rightInput))
        {
            reason = "右半边输入不稳定";
            return false;
        }

        if (!ExchangeChain.TryBuild(leftInput, out Recipe? leftRecipe) ||
            leftRecipe is null)
        {
            reason = "左半边输入不能用交换链装配";
            return false;
        }

        if (!ExchangeChain.TryBuild(rightInput, out Recipe? rightRecipe) ||
            rightRecipe is null)
        {
            reason = "右半边输入不能用交换链装配";
            return false;
        }

        CutResult leftCut = ShapePhysics.Cut(leftInput);

        if (!leftCut.Success)
        {
            reason = "左半边输入的 Cut 失败：" +
                     (leftCut.Error ?? "未知原因");
            return false;
        }

        // 方向 A 保留左输入的西侧；方向 B 把左半边摆到了东侧，保留东侧。
        ShapeDocument? keptLeft = direction == 0
            ? leftCut.West
            : leftCut.East;

        if (keptLeft is null)
        {
            reason = direction == 0
                ? "左半边输入的 Cut 没有西半边"
                : "左半边输入的 Cut 没有东半边";
            return false;
        }

        CutResult rightCut = ShapePhysics.Cut(rightInput);

        if (!rightCut.Success)
        {
            reason = "右半边输入的 Cut 失败：" +
                     (rightCut.Error ?? "未知原因");
            return false;
        }

        // 方向 A 保留右输入的东侧；方向 B 把右半边摆到了西侧，保留西侧。
        ShapeDocument? keptRight = direction == 0
            ? rightCut.East
            : rightCut.West;

        if (keptRight is null)
        {
            reason = direction == 0
                ? "右半边输入的 Cut 没有东半边"
                : "右半边输入的 Cut 没有西半边";
            return false;
        }

        ShapeDocument leftReplay = ShapeOps.Rotate(keptLeft, -rotation);
        ShapeDocument rightReplay = ShapeOps.Rotate(keptRight, -rotation);

        if (!ShapeOps.Same(leftReplay, leftHalf))
        {
            reason = "左半边真实半破后转回与半边工序的产物不一致";
            return false;
        }

        if (!ShapeOps.Same(rightReplay, rightHalf))
        {
            reason = "右半边真实半破后转回与半边工序的产物不一致";
            return false;
        }

        if (!SameHalf(leftReplay, target, leftColumns, parts) ||
            !SameHalf(rightReplay, target, rightColumns, parts))
        {
            reason = "真实半破后的两半与目标分段不一致";
            return false;
        }

        (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
            ShapeExchange.Exchange(leftInput, rightInput);

        if (first is null || second is null)
        {
            reason = "真实交换失败（" + (exchangeError ?? "未知原因") + "）";
            return false;
        }

        /*
         * 方向 A：左半边在西、右半边在东 ⇒ 所需输出是 second（West + East）。
         * 方向 B：左半边在东、右半边在西 ⇒ 所需输出是 first（East + West）。
         */
        ShapeDocument raw = direction == 0 ? second : first;
        ShapeDocument rawWaste = direction == 0 ? first : second;

        ShapeDocument product = ShapeOps.Rotate(raw, -rotation);
        ShapeDocument waste = ShapeOps.Rotate(rawWaste, -rotation);

        /*
         * cap 可能高于目标层数：多出来的层在产物里一定是空的，
         * 截到目标层数才能「逐格等于目标」（Recipe.Shape 的层数与目标一致）。
         */
        if (product.LayerCount != target.LayerCount)
        {
            product = ShapeOps.Truncate(product, target.LayerCount);
        }

        /*
         * 方向 B 的产物等于目标整体旋转 half 个象限；旋转等价时按
         * ExchangeChain 的同一口径转回目标本身（废料一起转）。
         */
        int finalRotation = 0;

        if (!ShapeOps.Same(product, target))
        {
            for (int k = 1; k < parts; k++)
            {
                if (ShapeOps.Same(ShapeOps.Rotate(product, k), target))
                {
                    finalRotation = k;
                    break;
                }
            }

            if (finalRotation == 0)
            {
                reason = "交换输出转回后与目标不逐格相等";
                return false;
            }

            product = ShapeOps.Rotate(product, finalRotation);
            waste = ShapeOps.Rotate(waste, finalRotation);
        }

        var details = new HalfAssemblyDetails(
            axis,
            half,
            left,
            right,
            leftRecipe,
            rightRecipe,
            leftInput,
            rightInput,
            leftReplay,
            rightReplay,
            product,
            waste,
            direction,
            finalRotation);

        var errors = new List<string>();

        if (!Verify(details, target, errors))
        {
            reason = "半边装配自检失败（" + string.Join("；", errors) + "）";
            return false;
        }

        var steps = new List<RecipeStep>
        {
            new(
                $"制作左半边（{half} 列半边工序，{Describe(left.Candidate)}；" +
                $"交换链 rank {leftRecipe.Rank}；{DescribeDirection(direction)}）：" +
                $"第 {axis + 1}–{ShapeOps.Wrap(axis + half - 1, parts) + 1} 象限",
                leftInput),
            new(
                $"制作右半边（{half} 列半边工序，{Describe(right.Candidate)}；" +
                $"交换链 rank {rightRecipe.Rank}；{DescribeDirection(direction)}）：" +
                $"第 {rightColumn + 1}–{ShapeOps.Wrap(rightColumn + half - 1, parts) + 1} 象限",
                rightInput),
            new(
                direction == 0
                    ? "交换得到目标图形（左半边在西、右半边在东）"
                    : "交换得到目标图形（左半边在东、右半边在西，输出先整体回转）",
                target),
            new("废料", waste, true)
        };

        recipe = Recipe.CreateHalfAssembly(target, steps, details);

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 半边装配的自检 / 回放：把明细里的每一步都换成真实物理重算一遍。
    ///
    /// <list type="number">
    /// <item>左右两个半边工序各自的步骤（真实 Collapse / Cut）能回到它的半边目标；</item>
    /// <item>两条装配输入就是两个半边按本次方向旋转后的结果（含方向 B 的半圈旋转）；</item>
    /// <item>两条输入被真实 <see cref="ShapePhysics.Cut"/> 切开后，本次方向保留的那一侧
    /// 转回目标方向逐格等于半边目标；</item>
    /// <item>真实 <see cref="ShapeExchange.Exchange"/> 的所需输出转回（再按
    /// <see cref="HalfAssemblyDetails.FinalRotation"/> 对齐）后逐格等于
    /// <see cref="HalfAssemblyDetails.Product"/>（也等于整图目标）。</item>
    /// </list>
    /// </summary>
    public static bool Verify(
        HalfAssemblyDetails details,
        ShapeDocument? expected,
        List<string> errors)
    {
        int before = errors.Count;

        if (details.LeftInput.PartCount != details.RightInput.PartCount)
        {
            errors.Add("两个半边输入的象限数不一致。");
            return false;
        }

        int parts = details.LeftInput.PartCount;
        int half = parts / 2;

        if (details.HalfWidth != half)
        {
            errors.Add($"半边宽度 {details.HalfWidth} 与 n/2 = {half} 不一致。");
        }

        if (details.Direction is not (0 or 1))
        {
            errors.Add($"未知的交换方向 {details.Direction}。");
        }

        int[] leftColumns = HalfColumnIndexes(parts, details.Axis);
        int[] rightColumns = HalfColumnIndexes(parts, ShapeOps.Wrap(details.Axis + half, parts));

        int rotation = details.Direction == 0
            ? ShapeOps.Wrap(-details.Axis, parts)
            : ShapeOps.Wrap(half - details.Axis, parts);

        if (!TryGetPreState(details.Left, out ShapeDocument? leftPre, out string leftError))
        {
            errors.Add("左半边工序没有前态：" + leftError);
        }
        else
        {
            if (!ReplayHalf(details.Left, leftPre!, out leftError))
            {
                errors.Add("左半边工序回放失败：" + leftError);
            }
        }

        if (!TryGetPreState(details.Right, out ShapeDocument? rightPre, out string rightError))
        {
            errors.Add("右半边工序没有前态：" + rightError);
        }
        else
        {
            if (!ReplayHalf(details.Right, rightPre!, out rightError))
            {
                errors.Add("右半边工序回放失败：" + rightError);
            }
        }

        ShapeDocument leftInput = details.LeftInput;
        ShapeDocument rightInput = details.RightInput;

        /*
         * 装配输入 = 半边工序产物按本次方向旋转后的结果。
         * 这条把「哪一侧、转到哪一边」固定下来，方向 B 不会被误当成方向 A。
         */
        if (!ShapeOps.Same(
                ShapeOps.Rotate(details.LeftHalf, rotation),
                leftInput))
        {
            errors.Add("左半边输入与半边工序产物按本次方向旋转后的结果不一致。");
        }

        if (!ShapeOps.Same(
                ShapeOps.Rotate(details.RightHalf, rotation),
                rightInput))
        {
            errors.Add("右半边输入与半边工序产物按本次方向旋转后的结果不一致。");
        }

        /*
         * 两条半边输入自身也必须能被交换链造出来（rank 0）：
         * 这是「装配输入是真实可造输入」的唯一凭据。
         */
        if (!ShapeOps.Same(details.LeftRecipe.Shape, leftInput))
        {
            errors.Add("左半边输入的交换链配方产物与输入不一致。");
        }
        else if (!Replay.Verify(
                     details.LeftRecipe,
                     leftInput,
                     out IReadOnlyList<string> leftRecipeErrors))
        {
            errors.Add(
                "左半边输入的交换链回放失败：" +
                string.Join("；", leftRecipeErrors));
        }

        if (!ShapeOps.Same(details.RightRecipe.Shape, rightInput))
        {
            errors.Add("右半边输入的交换链配方产物与输入不一致。");
        }
        else if (!Replay.Verify(
                     details.RightRecipe,
                     rightInput,
                     out IReadOnlyList<string> rightRecipeErrors))
        {
            errors.Add(
                "右半边输入的交换链回放失败：" +
                string.Join("；", rightRecipeErrors));
        }

        CutResult leftCut = ShapePhysics.Cut(leftInput);

        if (!leftCut.Success)
        {
            errors.Add("左半边输入的真实 Cut 失败。");
        }
        else
        {
            ShapeDocument? keptLeft = details.Direction == 0
                ? leftCut.West
                : leftCut.East;

            if (keptLeft is null)
            {
                errors.Add("左半边输入的真实 Cut 缺少本次方向保留的一侧。");
            }
            else
            {
                ShapeDocument leftHalf = ShapeOps.Rotate(keptLeft, -rotation);

                if (!ShapeOps.Same(leftHalf, details.LeftHalf))
                {
                    errors.Add("真实半破后的左半边与记录不一致。");
                }
            }
        }

        CutResult rightCut = ShapePhysics.Cut(rightInput);

        if (!rightCut.Success)
        {
            errors.Add("右半边输入的真实 Cut 失败。");
        }
        else
        {
            ShapeDocument? keptRight = details.Direction == 0
                ? rightCut.East
                : rightCut.West;

            if (keptRight is null)
            {
                errors.Add("右半边输入的真实 Cut 缺少本次方向保留的一侧。");
            }
            else
            {
                ShapeDocument rightHalf = ShapeOps.Rotate(keptRight, -rotation);

                if (!ShapeOps.Same(rightHalf, details.RightHalf))
                {
                    errors.Add("真实半破后的右半边与记录不一致。");
                }
            }
        }

        (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
            ShapeExchange.Exchange(leftInput, rightInput);

        if (first is null || second is null)
        {
            errors.Add("真实交换失败：" + (exchangeError ?? "未知原因"));
            return errors.Count == before;
        }

        ShapeDocument raw = details.Direction == 0 ? second : first;
        ShapeDocument rawWaste = details.Direction == 0 ? first : second;

        ShapeDocument product = ShapeOps.Rotate(raw, -rotation);

        if (product.LayerCount != details.Product.LayerCount)
        {
            product = ShapeOps.Truncate(product, details.Product.LayerCount);
        }

        if (details.FinalRotation != 0)
        {
            product = ShapeOps.Rotate(product, details.FinalRotation);
        }

        if (!ShapeOps.Same(product, details.Product))
        {
            errors.Add("真实交换的所需输出与记录的产物不一致。");
        }

        ShapeDocument waste = ShapeOps.Rotate(rawWaste, -rotation);

        if (details.FinalRotation != 0)
        {
            waste = ShapeOps.Rotate(waste, details.FinalRotation);
        }

        if (details.Waste is not null &&
            !ShapeOps.Same(waste, details.Waste))
        {
            errors.Add("真实交换的废料与记录不一致。");
        }

        if (expected is not null &&
            !ShapeOps.Same(details.Product, expected))
        {
            errors.Add("半边装配的产物与目标不一致。");
        }

        /*
         * 两半必须拼成产物：产物就是「左半边的列 + 右半边的列」，
         * 不允许有第三处内容（否则交换输出里混进了辅助列残留）。
         */
        if (!SameHalf(details.LeftHalf, details.Product, leftColumns, parts) ||
            !SameHalf(details.RightHalf, details.Product, rightColumns, parts))
        {
            errors.Add("两个半边拼不回产物（有辅助列残留或半边缺格）。");
        }

        return errors.Count == before;
    }

    /// <summary>
    /// 真实回放一个半边工序：候选 1 / 2 用 <see cref="ShapePhysics.Collapse"/>，
    /// 候选 3 用 <see cref="ShapePhysics.Cut"/> / <see cref="ShapeOps.Rotate"/>。
    /// </summary>
    private static bool ReplayHalf(
        HalfWorkOrder order,
        ShapeDocument preState,
        out string error)
    {
        if (!order.IsComplete ||
            order.Steps.Count == 0 ||
            order.Steps[^1].Shape is null)
        {
            error = "半边工序未完成或没有半边目标。";
            return false;
        }

        ShapeDocument expected = order.Steps[^1].Shape!;

        switch (order.Candidate)
        {
            case HalfCandidate.Algo8Self:
            {
                if (!ShapeOps.Same(preState, expected))
                {
                    error = "候选 1 的前态应当就是半边目标。";
                    return false;
                }

                ShapeDocument? collapsed = ShapePhysics.Collapse(preState).Document;

                if (!ShapeOps.Same(collapsed, preState))
                {
                    error = "候选 1 的 Q 一次下落后不再是 Q。";
                    return false;
                }

                break;
            }

            case HalfCandidate.Algo8Uplift:
            {
                ShapeDocument? collapsed = ShapePhysics.Collapse(preState).Document;

                if (!ShapeOps.Same(collapsed, expected))
                {
                    error = "候选 2 的 Q′ 一次下落没有得到半边目标。";
                    return false;
                }

                break;
            }

            case HalfCandidate.TempCrystalFuse:
            {
                CutResult cut = ShapePhysics.Cut(
                    ShapeOps.Rotate(preState, order.Rotation));

                if (!cut.Success || cut.West is null)
                {
                    error = "候选 3 的真实 Cut 失败。";
                    return false;
                }

                ShapeDocument replayed = ShapeOps.Rotate(
                    cut.West,
                    -order.Rotation);

                if (!ShapeOps.Same(replayed, expected))
                {
                    error = "候选 3 半破后的 k 列与半边目标不符。";
                    return false;
                }

                break;
            }

            default:
                error = "未知的半边候选。";
                return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>取半边工序的前态（第一条 PreState 步）。</summary>
    private static bool TryGetPreState(
        HalfWorkOrder order,
        out ShapeDocument? preState,
        out string error)
    {
        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind == AngleWorkStepKind.PreState &&
                step.Shape is not null)
            {
                preState = step.Shape;
                error = string.Empty;
                return true;
            }
        }

        preState = null;
        error = "半边工序没有前态。";
        return false;
    }

    /// <summary>k 列在整图里的象限索引（从 <paramref name="from"/> 起连续 k 列）。</summary>
    private static int[] HalfColumnIndexes(
        int parts,
        int from)
    {
        int width = parts / 2;
        var columns = new int[width];

        for (int index = 0; index < width; index++)
        {
            columns[index] = ShapeOps.Wrap(from + index, parts);
        }

        return columns;
    }

    /// <summary>
    /// 逐格比较「某 k 列」：类型 / 形状码 / 颜色都要一致；
    /// 两侧层数不同时，多出来的层必须为空。
    /// </summary>
    private static bool SameHalf(
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
                    ? shape[layer, ShapeOps.Wrap(column, parts)]
                    : ShapeCell.Empty();

                ShapeCell right = layer < other.LayerCount
                    ? other[layer, ShapeOps.Wrap(column, parts)]
                    : ShapeCell.Empty();

                if (!ShapeOps.SameCell(left, right))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>交换方向的字母（A = 当前方向，B = 交换两侧）。</summary>
    private static string DirectionLabel(
        int direction)
    {
        return direction == 0 ? "A" : "B";
    }

    /// <summary>交换方向的说明文字（写进步骤标题与 Note）。</summary>
    private static string DescribeDirection(
        int direction)
    {
        return direction == 0
            ? "方向 A：左半边 → 西侧、右半边 → 东侧"
            : "方向 B：左半边 → 东侧、右半边 → 西侧（含半圈旋转）";
    }

    private static string Describe(
        HalfCandidate candidate)
    {
        return candidate switch
        {
            HalfCandidate.Algo8Self => "候选 1：Q 自身",
            HalfCandidate.Algo8Uplift => "候选 2：联合逆提升",
            _ => "候选 3：临时晶体 + 保险丝 + 一刀"
        };
    }
}
