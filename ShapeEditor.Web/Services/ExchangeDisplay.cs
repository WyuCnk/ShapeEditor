namespace ShapeEditor.Web.Services;

/// <summary>
/// 交换工序的显示文案（只改 Web 显示层，不改 Core 的 Direction / 节点标题）。
///
/// <para>
/// 当当前图形确实有角走了半边工序（<c>AngleWorkOrder.Half != null</c>）时，
/// 交换的两侧不再都写成普通的 <c>(1+2)+(3+4)</c>：
/// </para>
/// <list type="bullet">
/// <item>只有一侧命中 <c>Half.Target.Columns</c> ⇒ <c>半边（1+2） + (3+4)</c>
/// （命中后半边时写成 <c>(1+2) + 半边（3+4）</c>）；</item>
/// <item>两侧都命中 ⇒ <c>半边（1+2） + 半边（3+4）</c>；</item>
/// <item>两侧都不命中 ⇒ 保持 Core 的 <c>(1+2)+(3+4)</c>。</item>
/// </list>
/// <para>
/// 只处理 n = 4（半边工序只在 4 象限出现，每侧恰好 2 列）；
/// 其它象限数的方向文本原样返回，避免误改 n ≥ 6 的 <c>(12+3)+(4+56)</c> 这类写法。
/// </para>
/// </summary>
public static class ExchangeDisplay
{
    /// <summary>半边工序只在 4 象限出现；两侧各 2 列，方向里的列号都是 1–4。</summary>
    private const int HalfParts = 4;

    private const int HalfWidth = HalfParts / 2;

    /// <summary>
    /// 格式化交换方向。例如 <c>(1+2)+(3+4)</c> →
    /// <c>半边（1+2） + (3+4)</c>、<c>半边（1+2） + 半边（3+4)</c>。
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
            return $"半边（{leftText}） + 半边（{rightText}）";
        }

        return leftIsHalf
            ? $"半边（{leftText}） + ({rightText})"
            : $"({leftText}) + 半边（{rightText}）";
    }

    /// <summary>
    /// 格式化交换工序里的「交换得到左/右半边」节点标题。
    ///
    /// <para>
    /// 命中半边工序的一侧写成「半边（1+2）」；另一侧如果是配对半边，
    /// 写成普通的「(3+4)」；两侧都没有半边工序时保留 Core 原题。
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

        if (title == "交换得到左半边")
        {
            if (leftIsHalf)
            {
                return $"半边（{leftText}）";
            }

            return rightIsHalf
                ? $"({leftText})"
                : title;
        }

        if (title == "交换得到右半边")
        {
            if (rightIsHalf)
            {
                return $"半边（{rightText}）";
            }

            return leftIsHalf
                ? $"({rightText})"
                : title;
        }

        return title;
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
