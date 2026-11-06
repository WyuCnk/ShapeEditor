using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// n = 4 的半边可获取性（论文定理 6 + 算法 8，规格 §2.7）。
///
/// <para><b>定理 6</b>：一个四象限半边 Q 半破可得，当且仅当存在两根 S 列，
/// 使它们不带外柱、直接拼合后的一次完整下落恰好得到 Q。下落前的两列称为前态。</para>
///
/// <para><b>算法 8</b> 给出前态的两种候选：</para>
/// <list type="number">
/// <item><b>第一候选：Q 本身</b>——Q 的两列都属于 S，且 Q 稳定，则采用 Q。</item>
/// <item><b>第二候选：联合逆提升</b>——第一候选失败时：
///   1. 找两列各自最高的晶体；有一列没有晶体、或两枚最高晶体等高 ⇒ 拒绝本分支；
///   2. 选两枚中较低的那枚，按 §1.2 找与它粘连的全部材料，组成最大粘连组 G；
///   3. 令 b = G 的最低层；在 b 以下找最高的非空层 h（任一列有材料就算）；
///   4. 要求 h 存在，且第 h 层两列都是 T、h &lt; b−1、第 b−1 层两列都为空；
///   5. 把这一对 T 从第 h 层一起提高到第 b−1 层，其他格子不动，得到候选前态 Q′；
///   6. 检查 Q′ 的两列都属于 S，并按 §1.3 手算一次下落；结果恰好是 Q 则接受。</item>
/// </list>
///
/// 任一候选通过 ⇒ 半边可得；两个候选都失败 ⇒ 半边不可得。
/// 规格 §6.4 的例子：Q 的左列 <c>cT.T.cT.Tccc</c> 不在 S，
/// 但把第 4 层的 <c>TT</c> 提到第 5 层后，两列
/// <c>cT..TcT.Tccc</c>、<c>TTT.TcccccPT</c> 都在 S，共同下落一层即得 Q。
/// </summary>
public static class HalfObtainability
{
    /// <summary>
    /// 判断从 <paramref name="startColumn"/> 开始的两列（环形）构成的半边是否半破可得，
    /// 并返回算法 8 选中的前态。
    /// </summary>
    /// <param name="shape">至少包含这两列的图形（其余列会被忽略）。</param>
    /// <param name="startColumn">半边的第一列（0 起，环形）。</param>
    /// <param name="preState">前态；不可得时为 null。</param>
    /// <param name="note">判定说明。</param>
    public static bool Test(
        ShapeDocument shape,
        int startColumn,
        out ShapeDocument? preState,
        out string note)
    {
        if (shape.PartCount != 4)
        {
            preState = null;
            note = "算法 8 只适用于四象限半边。";
            return false;
        }

        int first = ShapeOps.Wrap(startColumn, shape.PartCount);
        int second = ShapeOps.Wrap(startColumn + 1, shape.PartCount);

        // 只保留这个半边的两列：定理 6 讨论的是静态半边。
        ShapeDocument half =
            ShapeOps.Project(shape, first, 2);

        // 第一候选：Q 本身。
        bool firstS = PillarSCriterion.IsPillarColumn(half, first);
        bool secondS = PillarSCriterion.IsPillarColumn(half, second);
        bool stable = ShapeOps.IsStable(half);

        if (firstS && secondS && stable)
        {
            preState = half;
            note = $"第一候选通过：两列都在 S，且半边稳定（列 {first + 1}、{second + 1}）。";
            return true;
        }

        // 第二候选：联合逆提升。
        if (TryLift(
                half,
                first,
                second,
                out ShapeDocument? lifted,
                out string liftNote))
        {
            preState = lifted;
            note = liftNote;
            return true;
        }

        preState = null;
        note =
            $"两列都在 S？{firstS}/{secondS}；半边稳定？{stable}；" +
            $"联合逆提升：{liftNote}";
        return false;
    }

    /// <summary>算法 8 第二候选：联合逆提升。</summary>
    private static bool TryLift(
        ShapeDocument half,
        int first,
        int second,
        out ShapeDocument? lifted,
        out string note)
    {
        lifted = null;

        int firstCrystal = HighestCrystal(half, first);
        int secondCrystal = HighestCrystal(half, second);

        // 1. 有一列没有晶体、或两枚最高晶体等高 ⇒ 拒绝本分支。
        if (firstCrystal < 0 || secondCrystal < 0)
        {
            note = "有一列没有晶体，拒绝。";
            return false;
        }

        if (firstCrystal == secondCrystal)
        {
            note = "两枚最高晶体等高，拒绝。";
            return false;
        }

        // 2. 选较低的那枚，找与它粘连的最大组 G。
        int lowerLayer = Math.Min(firstCrystal, secondCrystal);
        int lowerColumn = firstCrystal < secondCrystal ? first : second;

        int bottom = GroupBottom(half, lowerLayer, lowerColumn);

        if (bottom < 0)
        {
            note = "找不到粘连组，拒绝。";
            return false;
        }

        // 3. 在 b 以下找最高的非空层 h（任一列有材料就算）。
        int h = -1;

        for (int layer = bottom - 1;
             layer >= 0;
             layer--)
        {
            if (half[layer, first].Type != CellType.Empty ||
                half[layer, second].Type != CellType.Empty)
            {
                h = layer;
                break;
            }
        }

        if (h < 0)
        {
            note = "b 以下没有非空层，拒绝。";
            return false;
        }

        // 4. 第 h 层两列都是 T、h < b−1、第 b−1 层两列都为空。
        if (half[h, first].Type != CellType.Shape ||
            half[h, second].Type != CellType.Shape)
        {
            note = $"第 {h + 1} 层不是两列普通格，拒绝。";
            return false;
        }

        if (h >= bottom - 1)
        {
            note = $"h = {h + 1} 不满足 h < b−1（b = {bottom + 1}），拒绝。";
            return false;
        }

        if (half[bottom - 1, first].Type != CellType.Empty ||
            half[bottom - 1, second].Type != CellType.Empty)
        {
            note = $"第 {bottom} 层不是两列都为空，拒绝。";
            return false;
        }

        // 5. 把这一对 T 从第 h 层一起提高到第 b−1 层。
        ShapeDocument candidate = half.Clone();

        ShapeCell firstCell = half[h, first].Clone();
        ShapeCell secondCell = half[h, second].Clone();

        candidate[h, first] = ShapeCell.Empty();
        candidate[h, second] = ShapeCell.Empty();

        candidate[bottom - 1, first] = firstCell;
        candidate[bottom - 1, second] = secondCell;

        // 6. Q′ 的两列都属于 S，且一次完整下落恰好得到 Q。
        if (!PillarSCriterion.IsPillarColumn(candidate, first) ||
            !PillarSCriterion.IsPillarColumn(candidate, second))
        {
            note = "提升后两列不都在 S，拒绝。";
            return false;
        }

        ShapeDocument? collapsed =
            ShapePhysics.Collapse(candidate).Document;

        if (!ShapeOps.Same(collapsed, half))
        {
            note = "提升后下落结果不等于 Q，拒绝。";
            return false;
        }

        lifted = candidate;
        note =
            $"联合逆提升通过：把第 {h + 1} 层的 TT 提到第 {bottom} 层，下落即得 Q。";
        return true;
    }

    /// <summary>某一列最高晶体的层号（0 起），没有晶体返回 −1。</summary>
    private static int HighestCrystal(
        ShapeDocument shape,
        int column)
    {
        for (int layer = shape.LayerCount - 1;
             layer >= 0;
             layer--)
        {
            if (shape[layer, column].Type == CellType.Crystal)
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>
    /// 按 §1.2 找包含指定格子的最大粘连组，返回组的最低层（0 起）；
    /// 找不到返回 −1。分组规则直接复用物理层的实现，
    /// 保证「顶针各自成组、只沿晶体竖直粘连」等细节一致。
    /// </summary>
    private static int GroupBottom(
        ShapeDocument shape,
        int layer,
        int column)
    {
        var cells = PhysicsGrid.Clone(shape);

        List<List<(int Layer, int Part)>> groups =
            PhysicsGrid.BuildGroups(
                cells,
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
}
