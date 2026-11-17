namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器的运行期诊断计数器（只读，不影响任何生成结果）。
///
/// <para>
/// 用途：回归测试要断言「路由短路」真的生效——<c>t[1]=='T'</c> 的列
/// （普通格必须落到第 1 层，<c>d = 1</c>，违反构造 H 的前提 <c>2 ≤ d</c>）
/// 不得再尝试构造 H。测试不靠日志文本，而是直接看这里的计数。
/// </para>
///
/// <para>
/// 计数器在每次「进入构造 H 生成器」时 +1；其中「第一层是普通格」的尝试
/// 单独记在 <see cref="ConstructHOnSolidFirstLayer"/>，正常应为 0。
/// 计数器是进程级的，测试开始前调用 <see cref="Reset"/> 即可。
/// </para>
/// </summary>
public static class AngleWorkOrderDiagnostics
{
    private static long constructHAttempts;
    private static long constructHOnSolidFirstLayer;

    /// <summary>构造 H 生成器的进入次数（含各档、含失败尝试）。</summary>
    public static long ConstructHAttempts =>
        Interlocked.Read(ref constructHAttempts);

    /// <summary>目标第 1 层是普通格时仍进入构造 H 的次数（改动 1 要求恒为 0）。</summary>
    public static long ConstructHOnSolidFirstLayer =>
        Interlocked.Read(ref constructHOnSolidFirstLayer);

    /// <summary>清零全部计数器（测试用）。</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref constructHAttempts, 0);
        Interlocked.Exchange(ref constructHOnSolidFirstLayer, 0);
    }

    internal static void RecordConstructHAttempt(bool solidFirstLayer)
    {
        Interlocked.Increment(ref constructHAttempts);

        if (solidFirstLayer)
        {
            Interlocked.Increment(ref constructHOnSolidFirstLayer);
        }
    }
}
