using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 「可交换」判定：存在某个朝向，使纸上分开的两个静态半边都稳定。
///
/// 这是论文定理 5/6 的保守版本：
/// - n = 2：定理 5 的「两列分别通过孤立列判据」比它更强；
/// - n = 4：定理 6 允许「两根 S 列拼合后共同下落」，比它更宽；
/// - n ≥ 6：实测（n=6、2 层 232,425 个样本；3 层 91,708 / 15,307 个样本）
///   该判据与交换链的成功完全一致，0 反例。
///
/// 保守方向是安全的：判 false 只会让上游多走一次顶针/堆叠分支，不会误判可制作。
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
        int half = shape.PartCount / 2;

        var notes = new List<string>();

        for (int current = 0;
             current < half;
             current++)
        {
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
