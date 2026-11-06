using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 列的「五项共用条件」——角合法性的判定。
///
/// 来源：论文《Shapez 2 列与可分形状的判定和构造》定理 1。
/// 与现有 <see cref="ShapeAngleAnalyzer"/> 的四条规则逐层等价
/// （宽度 1、层数 1–8 的枚举计数完全一致：4, 14, 47, 152, 476, 1451, 4328, 12688）。
///
/// 一、针有直接支撑：每枚针要么接地，要么同列正下方非空。
/// 二、晶体以下的针只能在底部连续针段中：若一枚针上方还有晶体，
///     则从地面到这枚针必须全部为针。
/// 三、非最高晶体有上盖：若某枚晶体上方还有另一枚晶体，则它正上方一格必须非空。
/// 四、晶体下洞需要普通资源：对每枚晶体，只要它下方有空，
///     它下方就至少要有一枚普通格。
/// 五、晶体之间的洞需要两枚普通资源：任意两枚晶体之间只要有空，
///     严格位于它们之间的普通格至少有两枚。
/// </summary>
public static class ShapeSignature
{
    public const char EmptyCode = '.';

    public const char PinCode = 'P';

    public const char SolidCode = 'T';

    public const char CrystalCode = 'c';

    public static bool IsLegalColumn(
        ShapeDocument document,
        int column)
    {
        return CheckColumn(document, column).IsLegal;
    }

    public static ColumnCheck CheckColumn(
        ShapeDocument document,
        int column)
    {
        return CheckColumn(ColumnText(document, column));
    }

    /// <summary>
    /// 检查整个图形：所有列都必须合法。
    /// </summary>
    public static bool AllColumnsLegal(
        ShapeDocument document,
        out int firstBadColumn)
    {
        for (int part = 0;
             part < document.PartCount;
             part++)
        {
            if (!IsLegalColumn(document, part))
            {
                firstBadColumn = part;
                return false;
            }
        }

        firstBadColumn = -1;
        return true;
    }

    /// <summary>
    /// 把一列写成字符串：左边是地面，右边是顶层。
    /// </summary>
    public static string ColumnText(
        ShapeDocument document,
        int column)
    {
        var builder = new System.Text.StringBuilder(
            document.LayerCount);

        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            builder.Append(
                document[layer, column].Type switch
                {
                    CellType.Empty => EmptyCode,
                    CellType.Pin => PinCode,
                    CellType.Crystal => CrystalCode,
                    _ => SolidCode
                });
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按列字符串判定五项条件。
    /// </summary>
    public static ColumnCheck CheckColumn(
        string column)
    {
        var failures = new List<string>();

        if (!Condition1(column))
        {
            failures.Add("条件一：有线针没有直接支撑。");
        }

        if (!Condition2(column))
        {
            failures.Add("条件二：晶体以下的针不在底部连续针段中。");
        }

        if (!Condition3(column))
        {
            failures.Add("条件三：非最高晶体没有上盖。");
        }

        if (!Condition4(column))
        {
            failures.Add("条件四：晶体下方的洞缺少普通资源。");
        }

        if (!Condition5(column))
        {
            failures.Add("条件五：晶体之间的洞缺少两枚普通资源。");
        }

        return new ColumnCheck(
            failures.Count == 0,
            failures);
    }

    /// <summary>条件一：针有直接支撑。</summary>
    private static bool Condition1(
        string column)
    {
        for (int index = 0;
             index < column.Length;
             index++)
        {
            if (column[index] != PinCode ||
                index == 0)
            {
                continue;
            }

            if (column[index - 1] == EmptyCode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>条件二：晶体以下的针只能在底部连续针段中。</summary>
    private static bool Condition2(
        string column)
    {
        int highestCrystal =
            column.LastIndexOf(CrystalCode);

        if (highestCrystal < 0)
        {
            return true;
        }

        for (int index = 0;
             index < highestCrystal;
             index++)
        {
            if (column[index] != PinCode)
            {
                continue;
            }

            // 这枚针上方还有晶体 ⇒ 地面到它必须全是针
            for (int deeper = 0;
                 deeper <= index;
                 deeper++)
            {
                if (column[deeper] != PinCode)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>条件三：非最高晶体有上盖。</summary>
    private static bool Condition3(
        string column)
    {
        int highestCrystal =
            column.LastIndexOf(CrystalCode);

        for (int index = 0;
             index < highestCrystal;
             index++)
        {
            if (column[index] != CrystalCode)
            {
                continue;
            }

            if (index + 1 < column.Length &&
                column[index + 1] == EmptyCode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>条件四：晶体下洞需要普通资源。</summary>
    private static bool Condition4(
        string column)
    {
        for (int index = 0;
             index < column.Length;
             index++)
        {
            if (column[index] != CrystalCode)
            {
                continue;
            }

            bool hasGap = false;
            bool hasSolid = false;

            for (int deeper = 0;
                 deeper < index;
                 deeper++)
            {
                if (column[deeper] == EmptyCode)
                {
                    hasGap = true;
                }
                else if (column[deeper] == SolidCode)
                {
                    hasSolid = true;
                }
            }

            if (hasGap && !hasSolid)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>条件五：晶体之间的洞需要两枚普通资源。</summary>
    private static bool Condition5(
        string column)
    {
        var crystals = new List<int>();

        for (int index = 0;
             index < column.Length;
             index++)
        {
            if (column[index] == CrystalCode)
            {
                crystals.Add(index);
            }
        }

        for (int lower = 0;
             lower < crystals.Count;
             lower++)
        {
            for (int upper = lower + 1;
                 upper < crystals.Count;
                 upper++)
            {
                bool hasGap = false;
                int solids = 0;

                for (int middle = crystals[lower] + 1;
                     middle < crystals[upper];
                     middle++)
                {
                    if (column[middle] == EmptyCode)
                    {
                        hasGap = true;
                    }
                    else if (column[middle] == SolidCode)
                    {
                        solids++;
                    }
                }

                if (hasGap && solids < 2)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>单个角的判定结果。</summary>
public sealed record ColumnCheck(
    bool IsLegal,
    IReadOnlyList<string> Failures);
