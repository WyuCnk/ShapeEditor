namespace ShapeEditor.Web.Services;

/// <summary>
/// 交换工序的显示文案（只改 Web 显示层，不改 Core 的 Direction / 节点标题）。
///
/// <para>
/// 交换的两侧按「半边」为单位称呼：左侧是半边工序产物时叫「半边1」，
/// 右侧是半边工序产物时叫「半边2」。当当前图形确实有角走了半边工序
/// （<c>AngleWorkOrder.Half != null</c>）时：
/// </para>
/// <list type="bullet">
/// <item>两侧都是半边 ⇒ <c>半边1 + 半边2</c>（用户要求：不再带列号括注）；</item>
/// <item>只有一侧是半边、另一侧正好是同一轴的另一半（两侧列号互补）⇒
/// 同样写 <c>半边1 + 半边2</c>：用户已裁决「只有一个半边需要半边工序」时，
/// 普通角工序那一侧仍按「半边2」称呼，只是节点图里保留半半边展开；</item>
/// <item>只有一侧是半边、但两侧列号不互补 ⇒ 保留旧写法
/// <c>半边1 + (3+4)</c> / <c>(1+2) + 半边2</c>，避免把不相干的列叫成半边；</item>
/// <item>两侧都不是半边 ⇒ 保持 Core 的 <c>(1+2)+(3+4)</c>。</item>
/// </list>
/// <para>
/// 只处理 n = 4（半边工序只在 4 象限出现，每侧恰好 2 列）；
/// 其它象限数的方向文本原样返回，避免误改 n ≥ 6 的 <c>(12+3)+(4+56)</c> 这类写法。
/// 一侧的列号与某个 <c>Half.Target.Columns</c> 对不上时视为「无法判断」，
/// 该侧按非半边显示（两侧都对不上时整段方向文本原样返回）。
/// </para>
/// </summary>
public static class ExchangeDisplay
{
    /// <summary>半边工序只在 4 象限出现；两侧各 2 列，方向里的列号都是 1–4。</summary>
    private const int HalfParts = 4;

    private const int HalfWidth = HalfParts / 2;

    /// <summary>左侧（第一）半边的显示名。</summary>
    private const string LeftHalfText = "半边1";

    /// <summary>右侧（第二）半边的显示名。</summary>
    private const string RightHalfText = "半边2";

    /// <summary>
    /// 格式化交换方向。例如 <c>(1+2)+(3+4)</c> →
    /// <c>半边1 + 半边2</c>（两侧都是半边，或只有一侧是半边且另一侧是同一轴的另一半）或
    /// <c>半边1 + (3+4)</c> / <c>(1+2) + 半边2</c>（只有一侧是半边且列号不互补）。
    /// </summary>
    /// <param name="direction">Core 给出的方向文本。</param>
    /// <param name="partCount">图形象限数；只有 4 才改写。</param>
    /// <param name="halfTargetColumns">
    /// 已确认走了半边工序的 <c>Half.Target.Columns</c>（0 起）。
    /// 为空表示没有半边工序，原样返回。
    /// </param>
    public static string FormatDirection(
        string direction,
        int partCount,
        IReadOnlyList<IReadOnlyList<int>> halfTargetColumns)
    {
        if (partCount != HalfParts ||
            halfTargetColumns.Count == 0 ||
            !TryParseSides(direction, out IReadOnlyList<int> left, out IReadOnlyList<int> right))
        {
            return direction;
        }

        bool leftIsHalf = IsHalfSide(left, halfTargetColumns);
        bool rightIsHalf = IsHalfSide(right, halfTargetColumns);

        if (!leftIsHalf && !rightIsHalf)
        {
            return direction;
        }

        string leftText = string.Join("+", left);
        string rightText = string.Join("+", right);

        if (leftIsHalf && rightIsHalf)
        {
            return $"{LeftHalfText} + {RightHalfText}";
        }

        /*
         * 只有一侧是半边：若另一侧正好是同一轴的另一半（列号互补），
         * 交换的两侧仍然都是「半边」，按用户最新口径写 半边1 + 半边2；
         * 列号不互补时才退回旧的「半边1 + (3+4)」写法。
         */
        if (IsComplementary(left, right))
        {
            return $"{LeftHalfText} + {RightHalfText}";
        }

        return leftIsHalf
            ? $"{LeftHalfText} + ({rightText})"
            : $"({leftText}) + {RightHalfText}";
    }

    /// <summary>
    /// 格式化交换工序里的「交换得到左/右半边」节点标题。
    ///
    /// <para>
    /// 与 <see cref="FormatDirection"/> 用同一套半边称呼：
    /// 命中半边工序的一侧写成「半边1」/「半边2」；只有一侧是半边、
    /// 另一侧列号互补时，另一侧的交换输出也叫「半边1」/「半边2」；
    /// 两侧都没有半边工序时保留 Core 原题。
    /// 两侧都是半边时节点标题正好是 <see cref="LeftHalfText"/> 与
    /// <see cref="RightHalfText"/>，与方向文本的 <c>半边1 + 半边2</c> 一致。
    /// </para>
    /// </summary>
    public static string FormatSideNodeTitle(
        string title,
        string direction,
        int partCount,
        IReadOnlyList<IReadOnlyList<int>> halfTargetColumns)
    {
        if (partCount != HalfParts ||
            halfTargetColumns.Count == 0 ||
            !TryParseSides(direction, out IReadOnlyList<int> left, out IReadOnlyList<int> right))
        {
            return title;
        }

        bool leftIsHalf = IsHalfSide(left, halfTargetColumns);
        bool rightIsHalf = IsHalfSide(right, halfTargetColumns);

        if (!leftIsHalf && !rightIsHalf)
        {
            return title;
        }

        string leftText = string.Join("+", left);
        string rightText = string.Join("+", right);

        // 一侧是半边、另一侧正好是同一轴的另一半 ⇒ 两侧都按半边称呼。
        bool complementary = IsComplementary(left, right);

        if (title == "交换得到左半边")
        {
            if (leftIsHalf || (rightIsHalf && complementary))
            {
                return LeftHalfText;
            }

            return rightIsHalf
                ? $"({leftText})"
                : title;
        }

        if (title == "交换得到右半边")
        {
            if (rightIsHalf || (leftIsHalf && complementary))
            {
                return RightHalfText;
            }

            return leftIsHalf
                ? $"({rightText})"
                : title;
        }

        return title;
    }

    /// <summary>
    /// 两侧列号是否正好互补（合起来恰好覆盖 n = 4 的全部 4 列且不重复）：
    /// 是则它们是同一条轴划分出的两个半边。
    /// </summary>
    private static bool IsComplementary(
        IReadOnlyList<int> left,
        IReadOnlyList<int> right)
    {
        if (left.Count + right.Count != HalfParts)
        {
            return false;
        }

        return left
            .Concat(right)
            .Distinct()
            .Count() == HalfParts;
    }

    /// <summary>某一侧的列集合是否就是某个 Half 的目标列（都用 0 起比较）。</summary>
    private static bool IsHalfSide(
        IReadOnlyList<int> side,
        IReadOnlyList<IReadOnlyList<int>> halfTargetColumns)
    {
        // side 是 1 起的列号；转成 0 起再与 Target.Columns 比。
        var normalized = side
            .Select(column => column - 1)
            .ToArray();

        foreach (IReadOnlyList<int> target in halfTargetColumns)
        {
            if (target.Count != normalized.Length)
            {
                continue;
            }

            if (target
                .OrderBy(column => column)
                .SequenceEqual(normalized.OrderBy(column => column)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把方向文本拆成左右两段列号（1 起）。
    ///
    /// 支持两种 n = 4 写法：
    /// <c>(1+2)+(3+4)</c>（交换链的分段写法）与
    /// <c>(1-2)+(3-4)</c>（半边装配的范围写法）。
    /// 每侧必须恰好 2 列且列号在 1–4，否则视为不可改写。
    /// </summary>
    private static bool TryParseSides(
        string direction,
        out IReadOnlyList<int> left,
        out IReadOnlyList<int> right)
    {
        left = Array.Empty<int>();
        right = Array.Empty<int>();

        if (string.IsNullOrWhiteSpace(direction))
        {
            return false;
        }

        string text = direction.Trim();

        int separator = text.IndexOf(")+(", StringComparison.Ordinal);

        if (!text.StartsWith('(') ||
            !text.EndsWith(')') ||
            separator <= 0)
        {
            return false;
        }

        string leftText = text[1..separator];
        string rightText = text[(separator + 3)..^1];

        if (!TryParseSide(leftText, out left) ||
            !TryParseSide(rightText, out right))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseSide(
        string text,
        out IReadOnlyList<int> columns)
    {
        columns = Array.Empty<int>();

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parsed = new List<int>();

        // 范围写法：(1-2) / (1-3)。
        if (text.Contains('-'))
        {
            string[] range = text.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries);

            if (range.Length != 2 ||
                !int.TryParse(range[0], out int start) ||
                !int.TryParse(range[1], out int end) ||
                start < 1 ||
                end < start ||
                end > HalfParts)
            {
                return false;
            }

            for (int column = start; column <= end; column++)
            {
                parsed.Add(column);
            }
        }
        else
        {
            // 分段写法：(1+2) / (2+3)。
            foreach (string part in text.Split('+'))
            {
                if (!int.TryParse(part, out int column) ||
                    column < 1 ||
                    column > HalfParts)
                {
                    return false;
                }

                parsed.Add(column);
            }
        }

        if (parsed.Count != HalfWidth ||
            parsed.Distinct().Count() != HalfWidth)
        {
            return false;
        }

        columns = parsed;

        return true;
    }
}
