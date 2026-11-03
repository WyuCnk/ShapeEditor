using System.Text;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 单次分析/解析调用内的稳定性缓存。
///
/// 稳定性（一次下落后图形是否不变）在旋转下不变：旋转只是列的循环移位，
/// 分组、支撑、强连通分量与「是否全部可达底层」都随之一一对应。
/// 因此缓存键使用「旋转归一化短代码」——把该片的起始列转到列 0 后再导出。
/// 这样同一个片在不同轴、不同切点上的重复判定只真正塌陷一次。
///
/// <para>
/// 作用域：<see cref="HalfStabilityAnalyzer.Analyze"/> 与
/// <see cref="ProductionPathParser.Parse"/> 每次调用各建一个实例，调用结束即丢弃，
/// 不跨调用共享，也不会因为调用方修改文档而失效。
/// </para>
/// </summary>
internal sealed class StabilityCache
{
    private readonly Dictionary<string, bool> _entries =
        new(StringComparer.Ordinal);

    private readonly CollapseMode _mode;

    public StabilityCache(CollapseMode mode)
    {
        _mode = mode;
    }

    /// <summary>
    /// 判断 <paramref name="shape"/> 是否稳定。
    ///
    /// <paramref name="contentStart"/> 是该片内容第一列的列号（起始列）；
    /// 缓存键会把这一列旋转到列 0 后再导出，因此不同轴上的同一片会命中同一条记录。
    /// 起始列只是调用方给出的定位标签：即便标签与图形不完全吻合，
    /// 相同键仍然意味着两个图形旋转等价，稳定性结论依旧一致，只是命中率会降低。
    /// </summary>
    public bool IsStable(
        ShapeDocument shape,
        int contentStart)
    {
        string key = BuildKey(shape, contentStart);

        if (_entries.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        bool stable = ShapeAngleAnalyzer.IsStable(shape, _mode);

        _entries[key] = stable;

        return stable;
    }

    /// <summary>
    /// 生成「旋转归一化短代码」：从 <paramref name="start"/> 列开始按环顺序导出。
    ///
    /// 导出规则与 <see cref="Services.ShortCodeExporter"/> 一致：
    /// 省略顶部的全空层，层与层之间用 <c>:</c> 分隔，每格两位短代码。
    /// 省略空顶层不影响稳定性（空层不参与下落），因此可以安全地作为缓存键。
    /// </summary>
    private static string BuildKey(
        ShapeDocument shape,
        int start)
    {
        int parts = shape.PartCount;
        int highest = shape.LayerCount - 1;

        while (highest > 0 && shape.IsLayerEmpty(highest))
        {
            highest--;
        }

        // 每层 parts 格 × 2 字符，再加层分隔符。
        var builder = new StringBuilder((highest + 1) * parts * 2 + highest);

        for (int layer = 0; layer <= highest; layer++)
        {
            if (layer > 0)
            {
                builder.Append(':');
            }

            for (int offset = 0; offset < parts; offset++)
            {
                int column = ((start + offset) % parts + parts) % parts;

                builder.Append(shape[layer, column].ToShortCode());
            }
        }

        return builder.ToString();
    }
}
