using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 孤立列判据（论文定理 4，规格 §2.5）：任意偶数宽度都适用。
///
/// 一个孤立列可制作，当且仅当：
/// 1. 非空格从地面起连续排列；
/// 2. 晶体以下的针全部属于底部连续针段。
///
/// 空列也允许。含晶体时结构依次为：
/// <code>
/// 地面 → 连续 P 段 → 连续 T/c 核心 → 连续 T/P 尾部 → 顶部空
/// </code>
/// 没有晶体时，任意连续接地的 <c>T</c>/<c>P</c> 列都可以。
///
/// n = 2 时每个半边只有一列，所以整图可分（定理 5）
/// ⟺ 左右两列都通过本判据。
/// </summary>
public static class IsolatedColumn
{
    /// <summary>判断文档中某一列是否是孤立列（定理 4）。</summary>
    public static bool IsIsolatedColumn(
        ShapeDocument document,
        int column)
    {
        return IsIsolatedColumn(
            ShapeSignature.ColumnText(document, column));
    }

    /// <summary>按列字符串判断是否是孤立列（定理 4）。</summary>
    public static bool IsIsolatedColumn(
        string column)
    {
        // 条件 1：非空格从地面起连续排列（空列也允许）。
        int highest = -1;

        for (int index = 0;
             index < column.Length;
             index++)
        {
            if (column[index] != ShapeSignature.EmptyCode)
            {
                highest = index;
            }
        }

        for (int index = 0;
             index <= highest;
             index++)
        {
            if (column[index] == ShapeSignature.EmptyCode)
            {
                return false;
            }
        }

        // 条件 2：晶体以下的针全部属于底部连续针段。
        int highestCrystal =
            column.LastIndexOf(ShapeSignature.CrystalCode);

        if (highestCrystal < 0)
        {
            return true;
        }

        for (int index = 0;
             index < highestCrystal;
             index++)
        {
            if (column[index] != ShapeSignature.PinCode)
            {
                continue;
            }

            // 这枚针上方还有晶体 ⇒ 地面到它必须全是针
            for (int deeper = 0;
                 deeper <= index;
                 deeper++)
            {
                if (column[deeper] != ShapeSignature.PinCode)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
