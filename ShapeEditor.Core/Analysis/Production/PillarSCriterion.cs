using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// n = 4 的「带柱列 S」判据（论文定理 3）：
/// <code>
/// S  ⟺  五项条件  ∧  ( D ∨ F )
/// </code>
/// 判据只依赖列字符串，适用于任意正高度上限。
///
/// 定义 D（底部资源条件），满足任一项即可：
/// 1. 没有晶体；
/// 2. 最底格为针；
/// 3. 最低晶体以下至少有两个空格（可不相邻）。
///
/// 定义 F（递归终点条件）：对每个「正下方为空的晶体段底端」，
/// 必须满足下面一项：
/// 1. 环下落条件：从这里开始至少三枚连续晶体；
/// 2. 弱下落终点条件：在它与下方最近晶体之间存在相邻的普通双格 <c>TT</c>；
///    若下方没有晶体，也允许以第 1 层的普通格为终点。
///
/// 这是对规格 §2.4 的逐字实现，不重新推导、不放宽。
/// </summary>
public static class PillarSCriterion
{
    /// <summary>判断文档中某一列是否属于 n = 4 的 S。</summary>
    public static bool IsPillarColumn(
        ShapeDocument document,
        int column)
    {
        return IsPillarColumn(
            ShapeSignature.ColumnText(document, column));
    }

    /// <summary>按列字符串判断是否属于 n = 4 的 S。</summary>
    public static bool IsPillarColumn(
        string column)
    {
        return ShapeSignature.CheckColumn(column).IsLegal &&
               (BottomResourceD(column) ||
                RecursiveTerminalF(column));
    }

    /// <summary>定义 D：底部资源条件。</summary>
    public static bool BottomResourceD(
        string column)
    {
        if (!column.Contains(ShapeSignature.CrystalCode))
        {
            return true;
        }

        if (column[0] == ShapeSignature.PinCode)
        {
            return true;
        }

        int lowestCrystal =
            column.IndexOf(ShapeSignature.CrystalCode);

        int gaps = 0;

        for (int index = 0;
             index < lowestCrystal;
             index++)
        {
            if (column[index] == ShapeSignature.EmptyCode)
            {
                gaps++;
            }
        }

        return gaps >= 2;
    }

    /// <summary>定义 F：递归终点条件。</summary>
    public static bool RecursiveTerminalF(
        string column)
    {
        for (int index = 0;
             index < column.Length;
             index++)
        {
            // 只看「正下方为空的晶体段底端」。
            if (column[index] != ShapeSignature.CrystalCode ||
                index == 0 ||
                column[index - 1] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            // 环下落：从这里开始至少三枚连续晶体。
            bool ringFall =
                index + 2 < column.Length &&
                column[index + 1] == ShapeSignature.CrystalCode &&
                column[index + 2] == ShapeSignature.CrystalCode;

            if (ringFall)
            {
                continue;
            }

            int below = -1;

            for (int probe = index - 1;
                 probe >= 0;
                 probe--)
            {
                if (column[probe] == ShapeSignature.CrystalCode)
                {
                    below = probe;
                    break;
                }
            }

            // 弱下落终点：与下方最近晶体之间存在相邻的普通双格 TT。
            bool adjacentPair = false;

            for (int probe = index - 1;
                 probe >= 1 && probe > below;
                 probe--)
            {
                if (column[probe] == ShapeSignature.SolidCode &&
                    column[probe - 1] == ShapeSignature.SolidCode)
                {
                    adjacentPair = true;
                    break;
                }
            }

            if (adjacentPair)
            {
                continue;
            }

            // 下方没有晶体时，允许以第 1 层的普通格为终点。
            if (below < 0 &&
                column[0] == ShapeSignature.SolidCode)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
