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
/// <para><b>做法</b>（每条轴各试一次）</para>
/// <code>
/// for 轴 a in 0 … half−1:
///     左半边 = 目标 [a … a+half−1]，右半边 = 目标 [a+half … a−1]
///     L = AngleWorkOrderBuilder.BuildHalf(target, a, cap)          ← k = n/2 列
///     R = AngleWorkOrderBuilder.BuildHalf(target, a+half, cap)
///     if L / R 都可得，且各自半边目标逐格等于目标的两段:
///         t = −a（把左半边摆到固定刀口的西侧）
///         A = Rotate(L.前态, t)      ← Cut(A).West = 左半边（目标方向旋转 t）
///         B = Rotate(R.前态, t)      ← Cut(B).East = 右半边（目标方向旋转 t）
///         (out1, out2) = Exchange(A, B)          ← 真实交换
///         out2 转回 −t 后逐格等于目标 ⇒ 成功
/// </code>
///
/// <para><b>为什么这样可以</b></para>
/// <list type="number">
/// <item>半边工序验证的是「前态一刀半破后的保留侧 = 目标半边」（候选 3），
/// 或「k 根 S 列拼合后一次完整下落 = 目标半边」（候选 1 / 2）；
/// 二者都保证 <c>Cut(Rotate(前态, 旋转量))</c> 的对应侧就是该半边。</item>
/// <item>右半边取 <c>Rotate(前态, −a)</c> 而不是「先转到西侧再转半圈」：
/// 刀口对半圈的对称性使 <c>Cut(Rotate(x, r+half)).East = Rotate(Cut(Rotate(x, r)).West, half)</c>，
/// 所以旋转 <c>−a = r+half</c> 之后右半边正好落在东侧。</item>
/// <item>交换的 <c>out2 = A.West + B.East</c> 就是「目标的左半边 + 右半边」，
/// 即 <c>Rotate(目标, −a)</c>；转回 <c>a</c> 即得目标。</item>
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
    /// 尝试用两条轴上的两个 k 列半边装配整图。
    /// </summary>
    /// <param name="target">整图目标。</param>
    /// <param name="cap">机器上限（通常是目标文档层数）。</param>
    /// <param name="recipe">成功时的配方（<see cref="RecipeKind.HalfAssembly"/>）。</param>
    /// <param name="note">结论说明（成功 / 失败都给出）。</param>
    /// <returns>找到并回放验证通过时返回 true。</returns>
    public static bool TryBuild(
        ShapeDocument target,
        int cap,
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

            int rotation = ShapeOps.Wrap(-axis, parts);

            ShapeDocument leftInput = ShapeOps.Rotate(leftPre!, rotation);
            ShapeDocument rightInput = ShapeOps.Rotate(rightPre!, rotation);

            /*
             * 前态必须是「机器能造出来的输入」：
             *   - 交换 / 顶针 / 堆叠的输出都塌陷过，所以输入必须稳定；
             *   - 半边输入用交换链（rank 0，与整图装配同一条链）装配，
             *     造不出来就不能声称它是半边工序的产物（不能只靠判据）。
             */
            if (!ShapeOps.IsStable(leftInput))
            {
                attempts.Add($"轴{axis}：左半边输入不稳定（候选 2 的 Q′ 是下落前状态）");
                continue;
            }

            if (!ShapeOps.IsStable(rightInput))
            {
                attempts.Add($"轴{axis}：右半边输入不稳定（候选 2 的 Q′ 是下落前状态）");
                continue;
            }

            if (!ExchangeChain.TryBuild(leftInput, out Recipe? leftRecipe) ||
                leftRecipe is null)
            {
                attempts.Add($"轴{axis}：左半边输入不能用交换链装配");
                continue;
            }

            if (!ExchangeChain.TryBuild(rightInput, out Recipe? rightRecipe) ||
                rightRecipe is null)
            {
                attempts.Add($"轴{axis}：右半边输入不能用交换链装配");
                continue;
            }

            CutResult leftCut = ShapePhysics.Cut(leftInput);

            if (!leftCut.Success || leftCut.West is null)
            {
                attempts.Add($"轴{axis}：左半边输入的 Cut 失败");
                continue;
            }

            CutResult rightCut = ShapePhysics.Cut(rightInput);

            if (!rightCut.Success || rightCut.East is null)
            {
                attempts.Add($"轴{axis}：右半边输入的 Cut 失败");
                continue;
            }

            ShapeDocument leftHalf = ShapeOps.Rotate(leftCut.West, -rotation);
            ShapeDocument rightHalf = ShapeOps.Rotate(rightCut.East, -rotation);

            if (!SameHalf(leftHalf, target, leftColumns, parts) ||
                !SameHalf(rightHalf, target, rightColumns, parts))
            {
                attempts.Add($"轴{axis}：真实半破后的两半与目标分段不一致");
                continue;
            }

            (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
                ShapeExchange.Exchange(leftInput, rightInput);

            if (first is null || second is null)
            {
                attempts.Add(
                    $"轴{axis}：真实交换失败（{exchangeError ?? "未知原因"}）");
                continue;
            }

            ShapeDocument product = ShapeOps.Rotate(second, -rotation);
            ShapeDocument waste = ShapeOps.Rotate(first, -rotation);

            /*
             * cap 可能高于目标层数：多出来的层在产物里一定是空的，
             * 截到目标层数才能「逐格等于目标」（Recipe.Shape 的层数与目标一致）。
             */
            if (product.LayerCount != target.LayerCount)
            {
                product = ShapeOps.Truncate(product, target.LayerCount);
            }

            if (!ShapeOps.Same(product, target))
            {
                attempts.Add($"轴{axis}：交换输出转回后与目标不逐格相等");
                continue;
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
                leftHalf,
                rightHalf,
                product,
                waste);

            var errors = new List<string>();

            if (!Verify(details, target, errors))
            {
                attempts.Add(
                    $"轴{axis}：半边装配自检失败（{string.Join("；", errors)}）");
                continue;
            }

            var steps = new List<RecipeStep>
            {
                new(
                    $"制作左半边（{half} 列半边工序，{Describe(left.Candidate)}；" +
                    $"交换链 rank {leftRecipe.Rank}）：" +
                    $"第 {axis + 1}–{ShapeOps.Wrap(axis + half - 1, parts) + 1} 象限",
                    leftInput),
                new(
                    $"制作右半边（{half} 列半边工序，{Describe(right.Candidate)}；" +
                    $"交换链 rank {rightRecipe.Rank}）：" +
                    $"第 {rightColumn + 1}–{ShapeOps.Wrap(rightColumn + half - 1, parts) + 1} 象限",
                    rightInput),
                new("交换得到目标图形", target),
                new("废料", waste, true)
            };

            recipe = Recipe.CreateHalfAssembly(target, steps, details);

            note =
                $"交换链失败，改走半边装配：第 {axis} 条轴的两个 {half} 列半边都可得" +
                $"（左 {Describe(left.Candidate)} / 右 {Describe(right.Candidate)}），" +
                "真实 Cut + Exchange 回放后逐格等于目标。";

            return true;
        }

        note = "半边装配：两条轴都没有给出可回放的两个半边（" +
               string.Join("；", attempts) + "）。";

        return false;
    }

    /// <summary>
    /// 半边装配的自检 / 回放：把明细里的每一步都换成真实物理重算一遍。
    ///
    /// <list type="number">
    /// <item>左右两个半边工序各自的步骤（真实 Collapse / Cut）能回到它的半边目标；</item>
    /// <item>两条输入被真实 <see cref="ShapePhysics.Cut"/> 切开后，保留下来的半边
    /// 转回目标方向逐格等于目标分段；</item>
    /// <item>真实 <see cref="ShapeExchange.Exchange"/> 的所需输出转回后逐格等于
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

        int[] leftColumns = HalfColumnIndexes(parts, details.Axis);
        int[] rightColumns = HalfColumnIndexes(parts, ShapeOps.Wrap(details.Axis + half, parts));

        int rotation = ShapeOps.Wrap(-details.Axis, parts);

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

            if (!ShapeOps.Same(
                    ShapeOps.Rotate(leftPre!, rotation),
                    details.LeftInput))
            {
                errors.Add("左半边输入与半边工序的前态不一致。");
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

            if (!ShapeOps.Same(
                    ShapeOps.Rotate(rightPre!, rotation),
                    details.RightInput))
            {
                errors.Add("右半边输入与半边工序的前态不一致。");
            }
        }

        ShapeDocument leftInput = details.LeftInput;
        ShapeDocument rightInput = details.RightInput;

        /*
         * 两条半边输入自身也必须能被交换链造出来（rank 0）：
         * 这是「半边工序的前态是真实可造输入」的唯一凭据。
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

        if (!leftCut.Success || leftCut.West is null)
        {
            errors.Add("左半边输入的真实 Cut 失败。");
        }
        else
        {
            ShapeDocument leftHalf = ShapeOps.Rotate(leftCut.West, -rotation);

            if (!ShapeOps.Same(leftHalf, details.LeftHalf))
            {
                errors.Add("真实半破后的左半边与记录不一致。");
            }
        }

        CutResult rightCut = ShapePhysics.Cut(rightInput);

        if (!rightCut.Success || rightCut.East is null)
        {
            errors.Add("右半边输入的真实 Cut 失败。");
        }
        else
        {
            ShapeDocument rightHalf = ShapeOps.Rotate(rightCut.East, -rotation);

            if (!ShapeOps.Same(rightHalf, details.RightHalf))
            {
                errors.Add("真实半破后的右半边与记录不一致。");
            }
        }

        (ShapeDocument? first, ShapeDocument? second, string? exchangeError) =
            ShapeExchange.Exchange(leftInput, rightInput);

        if (first is null || second is null)
        {
            errors.Add("真实交换失败：" + (exchangeError ?? "未知原因"));
            return errors.Count == before;
        }

        ShapeDocument product = ShapeOps.Rotate(second, -rotation);

        if (!ShapeOps.Same(product, details.Product))
        {
            errors.Add("真实交换的所需输出与记录的产物不一致。");
        }

        ShapeDocument waste = ShapeOps.Rotate(first, -rotation);

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
