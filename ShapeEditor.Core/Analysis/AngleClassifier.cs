using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角的制作类型。
///
/// 用户给出的定义：
/// - <see cref="Simple"/>：最高晶体连续触地（从地面到最高晶体之间没有空位）
///   ⇒ 不需要工序；
/// - <see cref="NeedsCrystalBreak"/>：最高晶体下方存在间隙
///   ⇒ 需要「碎晶」制造间隙，先给出碎晶前的角，再走工序。
///
/// 等价说法：没有图下间隙 / 晶下间隙的角是简单角。
/// 注意判定只看<b>最高晶体以下</b>：最高晶体上方的尾部留空不影响分类
/// （尾部本来就允许留空）。
/// </summary>
public enum AngleBuildKind
{
    /// <summary>简单角：最高晶体连续触地，不需要工序。</summary>
    Simple,

    /// <summary>碎晶角：最高晶体下方存在间隙，需要先碎晶再下落。</summary>
    NeedsCrystalBreak
}

/// <summary>
/// 角分类：把一列分成「简单角」与「碎晶角」。
///
/// 判定规则只有一条：
/// <code>
/// 最高晶体以下全部非空  ⇒ Simple
/// 否则                  ⇒ NeedsCrystalBreak
/// 没有晶体的列          ⇒ Simple
/// </code>
/// 这与「该列是否存在晶下间隙（空位正上方是晶体）」无关：间隙可以出现在
/// 最高晶体以上的尾部，那样的角不需要碎晶。
/// </summary>
public static class AngleClassifier
{
    /// <summary>
    /// 该列最高晶体所在的层索引；没有晶体时返回 -1。
    /// </summary>
    public static int HighestCrystalLayer(
        ShapeDocument document,
        int column)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (column < 0 || column >= document.PartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        for (int layer = document.LayerCount - 1;
             layer >= 0;
             layer--)
        {
            if (document[layer, column].Type == CellType.Crystal)
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>
    /// 判断该列是简单角还是碎晶角。
    /// </summary>
    public static AngleBuildKind Classify(
        ShapeDocument document,
        int column)
    {
        int highest = HighestCrystalLayer(document, column);

        if (highest < 0)
        {
            return AngleBuildKind.Simple;
        }

        for (int layer = 0;
             layer < highest;
             layer++)
        {
            if (document[layer, column].Type == CellType.Empty)
            {
                return AngleBuildKind.NeedsCrystalBreak;
            }
        }

        return AngleBuildKind.Simple;
    }
}
