using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 「可交换」判定：存在某个朝向，使纸上分开的两个静态半边都半破可得。
///
/// 按规格 §5.6.3 的接口契约分档：
/// - n = 2：定理 5——整图可分 ⟺ 左右两列都通过孤立列判据（定理 4）；
/// - n = 4：定理 6 + 算法 8——对每条轴静态取出左右两半，分别判断半边可获取性；
/// - n ≥ 6：实测（n=6、2 层 232,425 个样本；3 层 91,708 / 15,307 个样本）
///   静态半边稳定性与交换链的成功完全一致，0 反例，沿用这个判据。
///
/// n = 4 的旧实现要求「静态半边本身稳定」，那是充分不必要条件：
/// 定理 6 允许两根 S 列拼合后共同下落，因此会漏判联合逆提升的情形。
/// </summary>
public static class Swappable
{
    public static bool Test(
        ShapeDocument shape)
    {
        return Test(shape, out _, out _);
    }

    public static bool Test(
        ShapeDocument shape,
        out int axis,
        out string note)
    {
        int parts = shape.PartCount;

        if (parts % 2 != 0)
        {
            axis = -1;
            note = "象限数必须为偶数。";
            return false;
        }

        int half = parts / 2;

        // n = 2：定理 5，每个半边只有一列。
        if (half == 1)
        {
            bool left = IsolatedColumn.IsIsolatedColumn(shape, 0);
            bool right = IsolatedColumn.IsIsolatedColumn(shape, 1);

            axis = left && right ? 0 : -1;
            note =
                $"定理5：左列孤立列？{left}，右列孤立列？{right}。";
            return left && right;
        }

        var notes = new List<string>();

        for (int current = 0;
             current < half;
             current++)
        {
            // n = 4：定理 6 + 算法 8。
            if (half == 2)
            {
                bool leftOk = HalfObtainability.Test(
                    shape,
                    current,
                    out _,
                    out string leftNote);

                bool rightOk = HalfObtainability.Test(
                    shape,
                    current + half,
                    out _,
                    out string rightNote);

                notes.Add(
                    $"轴{current}：左{(leftOk ? "可得" : "不可得")}" +
                    $"右{(rightOk ? "可得" : "不可得")}；" +
                    $"左[{leftNote}] 右[{rightNote}]");

                if (leftOk && rightOk)
                {
                    axis = current;
                    note = notes[^1];
                    return true;
                }

                continue;
            }

            ShapeDocument left =
                ShapeOps.Project(shape, current, half);

            ShapeDocument right =
                ShapeOps.Project(shape, current + half, half);

            bool leftStable = ShapeOps.IsStable(left);
            bool rightStable = ShapeOps.IsStable(right);

            notes.Add(
                $"轴{current}：左{(leftStable ? "稳" : "不稳")}" +
                $"右{(rightStable ? "稳" : "不稳")}");

            if (leftStable && rightStable)
            {
                axis = current;
                note = notes[^1];
                return true;
            }
        }

        axis = -1;
        note = string.Join(" ", notes);
        return false;
    }
}
