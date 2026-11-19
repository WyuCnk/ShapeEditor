using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// §2 半边等价判定的结论（docs/half-side-work-order-v2.md §2）。
///
/// <para>
/// 层号一律<b>0 起</b>（与 <see cref="ShapeDocument"/> 的下标一致）；
/// 打印给人看时统一 +1 写成「第 N 层」。
/// </para>
/// </summary>
/// <param name="Passed">判定是否通过（§2 的三条都满足）。</param>
/// <param name="Reason">不通过的第一个原因；通过时为「§2 三条全部满足」。</param>
/// <param name="HighestU">u 的最高晶体层；没有晶体为 −1。</param>
/// <param name="HighestV">v 的最高晶体层；没有晶体为 −1。</param>
/// <param name="LowerColumn">较低那枚最高晶体在哪一列：0 = u，1 = v；无法判定为 −1。</param>
/// <param name="M">较低最高晶体所在粘连组的最低层；未算出为 −1。</param>
/// <param name="R">m 以下两列最后一个有材料的层（且必须是水平 <c>TT</c>）；未算出为 −1。</param>
public sealed record HalfPairVerdict(
    bool Passed,
    string Reason,
    int HighestU,
    int HighestV,
    int LowerColumn,
    int M,
    int R);

/// <summary>
/// 半边可得的§2等价条件（docs/half-side-work-order-v2.md §2 / §8 的 <c>HalfCriterion</c>）。
///
/// <para><b>判定</b>（<paramref name="u"/> = 目标列，<paramref name="v"/> = 邻列）</para>
/// <list type="number">
/// <item>两列都要有晶体，且两列的最高晶体<b>不能等高</b>；</item>
/// <item>取较低那颗最高晶体，找它所在的<b>最大粘连组</b>，记组的最低层为 m；</item>
/// <item>在 m 以下找两列<b>最后一个有材料的层</b> r，要求该层是水平普通双格 <c>TT</c>、
/// <c>r + 1 &lt; m</c>，且第 <c>m−1</c> 层两格全空。</item>
/// </list>
/// <para>
/// 三条都满足 ⇒ 把这对 <c>TT</c> 从 r 提回 m−1（见 <see cref="BuildPreState"/>），
/// 其余格子不动。这就是「目标决定的前态」：提回后两列都属 S 时，
/// 撤掉外柱一次共同下落应恰好还原 (u, v)。
/// </para>
/// <para>
/// <b>为什么 m 用粘连组而不是最高晶体本身</b>：十二层样例
/// <c>cT.T.cTcccTc | TTTT.cccPT..</c> 里 v 的最高晶体挂在 v 的 <c>ccc</c> 段上，
/// 段的底端才是「这一组最低层」；m 取组底，m−1 才正好是那段晶体正下方的空层。
/// </para>
/// <para>
/// 本类只做判定与「提回那一对 TT」这一件事：<b>不</b>负责补临时晶体、也不负责
/// 半破回放。调用方拿到前态后必须用真实 <see cref="ShapePhysics.Collapse"/>
/// 复核，做不到就如实报「未覆盖」。
/// </para>
/// </summary>
public static class HalfPairCriterion
{
    /// <summary>目标列与邻列的判定（两列都在同一个图形里）。</summary>
    public static HalfPairVerdict Test(
        ShapeDocument shape,
        int firstColumn,
        int secondColumn)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        return Test(
            ShapeSignature.ColumnText(shape, firstColumn),
            ShapeSignature.ColumnText(shape, secondColumn));
    }

    /// <summary>
    /// 按两列的列文本判定（<c>.</c> / <c>T</c> / <c>P</c> / <c>c</c>）。
    /// 两列长度可以不同：短的那列视为顶部补空。
    /// </summary>
    public static HalfPairVerdict Test(
        string u,
        string v)
    {
        if (u is null)
        {
            throw new ArgumentNullException(nameof(u));
        }

        if (v is null)
        {
            throw new ArgumentNullException(nameof(v));
        }

        int highestU = HighestCrystal(u);
        int highestV = HighestCrystal(v);

        // ① 两列都要有晶体。
        if (highestU < 0 || highestV < 0)
        {
            return Fail(
                highestU < 0 && highestV < 0
                    ? "两列都没有晶体"
                    : (highestU < 0 ? "u 没有晶体" : "v 没有晶体"),
                highestU,
                highestV);
        }

        // ① 两列的最高晶体不能等高。
        if (highestU == highestV)
        {
            return Fail(
                $"最高晶体等高（都在第 {highestU + 1} 层）",
                highestU,
                highestV);
        }

        int lowerColumn = highestU < highestV ? 0 : 1;
        int lowerLayer = lowerColumn == 0 ? highestU : highestV;
        string lowerText = lowerColumn == 0 ? u : v;

        // ② 较低那枚最高晶体所在粘连组的最低层 m。
        int m = GroupBottom(u, v, lowerLayer, lowerColumn);

        if (m < 0)
        {
            return Fail("找不到较低最高晶体所在的粘连组", highestU, highestV);
        }

        if (m == 0)
        {
            return Fail(
                $"粘连组最低层是第 1 层，m−1 不存在",
                highestU,
                highestV) with
            { M = m };
        }

        // ③ 在 m 以下找两列最后一个有材料的层 r：必须是水平双格 TT。
        int r = -1;

        for (int layer = m - 1; layer >= 1; layer--)
        {
            if (At(u, layer) == ShapeSignature.SolidCode &&
                At(v, layer) == ShapeSignature.SolidCode)
            {
                r = layer;
                break;
            }
        }

        if (r < 0)
        {
            return Fail(
                "m 以下没有两列同时是普通格（TT）的层",
                highestU,
                highestV) with
            { M = m };
        }

        // ③ r + 1 < m：r 与 m 之间要留出至少一层，提回才有落点。
        if (r + 1 >= m)
        {
            return Fail(
                $"r = 第 {r + 1} 层不满足 r+1 < m（m = 第 {m + 1} 层）",
                highestU,
                highestV) with
            { M = m, R = r };
        }

        // ③ 第 m−1 层两格全空。
        if (At(u, m - 1) != ShapeSignature.EmptyCode ||
            At(v, m - 1) != ShapeSignature.EmptyCode)
        {
            return Fail(
                $"第 {m} 层不是两格全空（u[m−1] = {Display(At(u, m - 1))}，" +
                $"v[m−1] = {Display(At(v, m - 1))}）",
                highestU,
                highestV) with
            { M = m, R = r };
        }

        // 记一下「较低那列」本身有材料，防止把空列判成可用（防御性）。
        _ = lowerText;

        return new HalfPairVerdict(
            true,
            "§2 三条全部满足",
            highestU,
            highestV,
            lowerColumn,
            m,
            r);
    }

    /// <summary>
    /// 按 §2 ③ 造前态：把 r 层那一对普通格<b>提回</b> m−1 层，其余格子不动。
    /// 返回新图形（不修改入参）；<paramref name="verdict"/> 必须是通过的判定。
    /// </summary>
    public static ShapeDocument BuildPreState(
        ShapeDocument shape,
        int firstColumn,
        int secondColumn,
        HalfPairVerdict verdict)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        if (!verdict.Passed)
        {
            throw new ArgumentException("§2 判定没有通过，不能造前态。", nameof(verdict));
        }

        ShapeDocument preState = shape.Clone();

        ShapeCell firstCell = shape[verdict.R, firstColumn].Clone();
        ShapeCell secondCell = shape[verdict.R, secondColumn].Clone();

        preState[verdict.R, firstColumn] = ShapeCell.Empty();
        preState[verdict.R, secondColumn] = ShapeCell.Empty();

        preState[verdict.M - 1, firstColumn] = firstCell;
        preState[verdict.M - 1, secondColumn] = secondCell;

        return preState;
    }

    /// <summary>
    /// 找包含指定格子的<b>最大粘连组</b>，返回组的最低层（0 起）。
    /// 分组直接复用物理层的 <see cref="PhysicsGrid.BuildGroups"/>：
    /// 普通格 / 晶体横向粘连、晶体纵向粘连、顶针各自成组，保证与下落语义一致。
    /// </summary>
    private static int GroupBottom(
        string u,
        string v,
        int layer,
        int column)
    {
        int layers = Math.Max(u.Length, v.Length);

        var cells = new ShapeCell[layers, 4];

        for (int index = 0; index < layers; index++)
        {
            cells[index, 0] = CellOf(At(u, index));
            cells[index, 1] = CellOf(At(v, index));
            cells[index, 2] = ShapeCell.Empty();
            cells[index, 3] = ShapeCell.Empty();
        }

        List<List<(int Layer, int Part)>> groups =
            PhysicsGrid.BuildGroups(cells, layers, 4);

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

    /// <summary>某一列最高晶体的层号（0 起）；没有晶体返回 −1。</summary>
    public static int HighestCrystal(
        string column)
    {
        return column.LastIndexOf(ShapeSignature.CrystalCode);
    }

    /// <summary>取列文本的第 <paramref name="layer"/> 层；越界视为空。</summary>
    private static char At(
        string column,
        int layer)
    {
        return layer >= 0 && layer < column.Length
            ? column[layer]
            : ShapeSignature.EmptyCode;
    }

    private static ShapeCell CellOf(
        char code)
    {
        return code switch
        {
            ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
            ShapeSignature.PinCode => ShapeCell.Pin(),
            ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
            _ => ShapeCell.Empty()
        };
    }

    private static string Display(
        char code)
    {
        return code == ShapeSignature.EmptyCode
            ? "."
            : code.ToString();
    }

    private static HalfPairVerdict Fail(
        string reason,
        int highestU,
        int highestV)
    {
        return new HalfPairVerdict(
            false,
            reason,
            highestU,
            highestV,
            -1,
            -1,
            -1);
    }
}
