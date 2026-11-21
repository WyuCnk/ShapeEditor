using System.IO;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 半边工序的手动测试入口（<b>不是测试项</b>，不计入 <c>RunAll</c> 的项数）。
///
/// <para><b>用法</b></para>
/// <code>
/// ShapeEditor.Tests.exe --half "cT.T.cPT" "TTTT.ccc"     # 两列目标（n = 4）
/// ShapeEditor.Tests.exe --half "u" "v" "w" --parts 6      # k 列半边
/// ShapeEditor.Tests.exe --half-shape "短代码"             # 取第 1、2 象限
/// ShapeEditor.Tests.exe --half-file D:\cases.txt          # 每行「u<TAB>v」
/// $env:SHAPE_HALF = "cT.T.cPT|TTTT.ccc"
/// </code>
///
/// <para><b>n = 4 的单例输出</b>（v4）：① 逆提升 → ② 角 u / 角 v → ③ 交换 →
/// ④ 拆柱 → ⑤ 跌落 → 结果逐格对比 → VERDICT = PASS / NotProducible。</para>
/// </summary>
internal static class HalfTestCli
{
    /// <summary>批量文件里一行一对目标列（制表符分隔；也接受空格）。</summary>
    private const char PairSeparator = '\t';

    /// <summary>
    /// 命令行 / 环境变量入口。返回 false 表示「这次调用与半边无关」，调用方继续跑测试。
    /// </summary>
    public static bool TryRun(
        string[] args,
        out int exitCode)
    {
        exitCode = 0;

        int parts = 4;

        string? envParts = Environment.GetEnvironmentVariable("SHAPE_HALF_PARTS");

        if (int.TryParse(envParts, out int parsedEnv) &&
            parsedEnv >= 2 &&
            parsedEnv % 2 == 0)
        {
            parts = parsedEnv;
        }

        var cleaned = new List<string>();

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--parts")
            {
                if (index + 1 >= args.Length ||
                    !int.TryParse(args[index + 1], out int parsed) ||
                    parsed < 2 ||
                    parsed % 2 != 0)
                {
                    Console.WriteLine("--parts 需要一个偶数（例如 4 / 6 / 8）。");
                    exitCode = 2;
                    return true;
                }

                parts = parsed;
                index++;
                continue;
            }

            cleaned.Add(args[index]);
        }

        if (cleaned.Count > 0)
        {
            string[] remaining = cleaned.ToArray();

            switch (remaining[0])
            {
                case "--half":
                    if (remaining.Length < 3)
                    {
                        Console.WriteLine(
                            "用法：--half \"u\" \"v\" [\"w\"] [--parts N]");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunSingle(
                        remaining.Skip(1).ToArray(),
                        parts);
                    return true;

                case "--half-shape":
                    if (remaining.Length < 2)
                    {
                        Console.WriteLine("用法：--half-shape \"短代码\"");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunShape(remaining[1]);
                    return true;

                case "--half-file":
                    if (remaining.Length < 2)
                    {
                        Console.WriteLine("用法：--half-file 路径（每行「u<TAB>v」）");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunBatchFile(remaining[1]);
                    return true;
            }
        }

        string halfEnv = Environment.GetEnvironmentVariable("SHAPE_HALF") ?? string.Empty;

        if (halfEnv.Length > 0)
        {
            exitCode = RunSingleText(halfEnv, "--half / SHAPE_HALF", parts);
            return true;
        }

        string fileEnv = Environment.GetEnvironmentVariable("SHAPE_HALF_FILE") ?? string.Empty;

        if (fileEnv.Length > 0)
        {
            exitCode = RunBatchFile(fileEnv);
            return true;
        }

        return false;
    }

    #region 单例

    private static int RunSingle(
        string[] columns,
        int parts)
    {
        int width = Math.Max(2, parts / 2);

        if (columns.Length > width)
        {
            Console.WriteLine(
                "给了 " + columns.Length + " 列，但 n = " + parts +
                " 的半边只有 " + width + " 列。");
            return 2;
        }

        if (parts == 4 && columns.Length == 2)
        {
            return RunV4(columns[0], columns[1]);
        }

        return RunHalfColumns(columns, parts);
    }

    private static int RunSingleText(
        string text,
        string source,
        int parts)
    {
        string[] pieces = text.Split(
            new[] { '|', '\t', ',' },
            StringSplitOptions.None);

        if (pieces.Length < 2)
        {
            Console.WriteLine(
                source + " 需要「u|v」两列，例如 SHAPE_HALF=\"cT.T.cPT|TTTT.ccc\"");
            return 2;
        }

        return RunSingle(
            pieces.Select(piece => piece.Trim()).ToArray(),
            parts);
    }

    /// <summary>n = 4 的 v4 单例：五步 + 逐格结果。</summary>
    private static int RunV4(
        string uText,
        string vText)
    {
        int layers = Math.Max(uText.Length, vText.Length);
        string u = Pad(uText, layers);
        string v = Pad(vText, layers);

        ShapeDocument target = BuildPair(u, v, layers);

        Console.WriteLine();
        Console.WriteLine(
            "=== target u = " + uText + " ; v = " + vText +
            " ; layers = " + layers + " ===");
        Console.WriteLine();

        HalfWorkOrder? half = AngleWorkOrderBuilder.SolveHalf(target, 0, layers);

        if (half is null)
        {
            Console.WriteLine("[结果] —");
            Console.WriteLine("VERDICT = NotProducible（半边的尺寸 / 层数不适用）");
            return 1;
        }

        Console.WriteLine(
            "[① 逆提升] " +
            (half.LiftNotes.Count == 0
                ? "无需抬升"
                : string.Join("，", half.LiftNotes)));

        if (half.CornerProducts.Count >= 2 && half.CornerOrders.Count >= 2)
        {
            Console.WriteLine(
                "[② 角 u] 产物 = " + Export(half.CornerProducts[0]) +
                "（" + DescribeRoute(half.CornerOrders[0]) + "）");
            Console.WriteLine(
                "[② 角 v] 产物 = " + Export(half.CornerProducts[1]) +
                "（" + DescribeRoute(half.CornerOrders[1]) + "）");
        }
        else
        {
            Console.WriteLine("[② 角 u] —（角工序还没做出）");
            Console.WriteLine("[② 角 v] —");
        }

        Console.WriteLine(
            "[③ 交换] = " +
            (half.ExchangedShape is null ? "—" : Export(half.ExchangedShape)));
        Console.WriteLine(
            "[④ 拆柱] = " +
            (half.StrippedShape is null ? "—" : Export(half.StrippedShape)));
        Console.WriteLine(
            "[⑤ 跌落] = " +
            (half.PreSettleShape is null
                ? "—"
                : Export(ShapePhysics.Collapse(half.PreSettleShape).Document!)));

        Console.WriteLine("[结果]");

        ShapeDocument? result = half.IsComplete && half.Steps.Count > 0
            ? half.Steps[^1].Shape
            : null;

        string uActual = result is null
            ? "—"
            : Trim(ShapeSignature.ColumnText(result, 0));
        string vActual = result is null
            ? "—"
            : Trim(ShapeSignature.ColumnText(result, 1));

        string uExpected = Trim(u);
        string vExpected = Trim(v);

        bool uOk = uActual == uExpected;
        bool vOk = vActual == vExpected;

        Console.WriteLine(
            "  u' = " + Align(uActual, uExpected) + "     期望 " + uExpected +
            "   " + (uOk ? "✔" : "✘"));
        Console.WriteLine(
            "  v' = " + Align(vActual, vExpected) + "     期望 " + vExpected +
            "   " + (vOk ? "✔" : "✘"));

        if (half.Note is not null)
        {
            Console.WriteLine("  Note = " + half.Note);
        }

        bool pass = half.IsComplete && uOk && vOk;

        Console.WriteLine(
            "VERDICT = " + (pass ? "PASS" : "NotProducible"));

        return pass ? 0 : 1;
    }

    /// <summary>n ≥ 6 的 k 列半边诊断（保持 k 列视角）。</summary>
    private static int RunHalfColumns(
        string[] givenColumns,
        int parts)
    {
        int width = parts / 2;
        int layers = Math.Max(
            1,
            givenColumns.Length == 0
                ? 1
                : givenColumns.Max(column => column.Length));

        var padded = new string[width];

        for (int index = 0; index < width; index++)
        {
            padded[index] = index < givenColumns.Length
                ? Pad(givenColumns[index], layers)
                : new string(ShapeSignature.EmptyCode, layers);
        }

        var target = new ShapeDocument(layers, parts);

        for (int index = 0; index < width; index++)
        {
            Write(target, index, padded[index]);
        }

        Console.WriteLine(
            "=== half target: parts = " + parts + ", k = " + width +
            " 列；layers = " + layers + " ===");

        HalfWorkOrder? half = AngleWorkOrderBuilder.SolveHalf(target, 0, layers);

        if (half is null)
        {
            Console.WriteLine("VERDICT = NotProducible（k 列半边不适用）");
            return 1;
        }

        Console.WriteLine(
            "[① 前态] " +
            (half.LiftedHalf is null
                ? "—"
                : Export(half.LiftedHalf)));

        Console.WriteLine(
            "[⑤ 跌落] = " +
            (half.Steps.Count > 0 && half.Steps[^1].Shape is not null
                ? Export(half.Steps[^1].Shape!)
                : "—"));

        bool pass = half.IsComplete;

        Console.WriteLine("VERDICT = " + (pass ? "PASS" : "NotProducible"));

        if (half.Note is not null)
        {
            Console.WriteLine("Note = " + half.Note);
        }

        return pass ? 0 : 1;
    }

    private static int RunShape(
        string code)
    {
        if (!ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError))
        {
            Console.WriteLine("短代码尺寸推断失败：" + inferError);
            return 2;
        }

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        if (!parsed.Success || parsed.Document is null)
        {
            Console.WriteLine("短代码解析失败：" + parsed.Error);
            return 2;
        }

        ShapeDocument shape = parsed.Document;

        if (parts < 2)
        {
            Console.WriteLine("短代码只有 " + parts + " 个象限，半边至少需要 2 列。");
            return 2;
        }

        return RunV4(
            ShapeSignature.ColumnText(shape, 0),
            ShapeSignature.ColumnText(shape, 1));
    }

    #endregion

    #region 批量

    private static int RunBatchFile(
        string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("找不到批量文件：" + path);
            return 2;
        }

        string[] lines = File.ReadAllLines(path);

        int total = 0;
        int pass = 0;
        int fail = 0;
        int error = 0;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] pieces = line.Split(
                new[] { PairSeparator, ' ', ',' },
                2,
                StringSplitOptions.RemoveEmptyEntries);

            total++;

            if (pieces.Length < 2)
            {
                error++;
                Console.WriteLine("ERROR " + line + " | 原因：这行不是「u<TAB>v」");
                continue;
            }

            string u = pieces[0].Trim();
            string v = pieces[1].Trim();
            int layers = Math.Max(u.Length, v.Length);
            HalfWorkOrder? half = AngleWorkOrderBuilder.SolveHalf(
                BuildPair(Pad(u, layers), Pad(v, layers), layers),
                0,
                layers);

            if (half is { IsComplete: true })
            {
                pass++;
                Console.WriteLine("PASS  " + u + " | " + v);
            }
            else
            {
                fail++;
                Console.WriteLine(
                    "FAIL  " + u + " | " + v + " | 原因：" +
                    (half?.Note ?? "半边工序不适用"));
            }
        }

        Console.WriteLine(
            $"HALFTEST total={total} pass={pass} fail={fail} error={error}");

        return fail == 0 && error == 0 ? 0 : 1;
    }

    #endregion

    #region 小工具

    private static string DescribeRoute(
        AngleWorkOrder order)
    {
        return order.Construction + " / " + order.Algorithm;
    }

    private static string Export(
        ShapeDocument shape)
    {
        return new ShortCodeExporter().Export(shape);
    }

    private static string Pad(
        string text,
        int layers)
    {
        return text.Length >= layers
            ? text
            : text.PadRight(layers, ShapeSignature.EmptyCode);
    }

    private static string Trim(
        string text)
    {
        return text.TrimEnd(ShapeSignature.EmptyCode);
    }

    private static string Align(
        string actual,
        string expected)
    {
        return actual.Length >= expected.Length
            ? actual
            : actual.PadRight(expected.Length);
    }

    /// <summary>把两列目标装进四象限图形（第 1、2 象限有料，其余为空）。</summary>
    private static ShapeDocument BuildPair(
        string u,
        string v,
        int layers)
    {
        var document = new ShapeDocument(layers, 4);

        Write(document, 0, u);
        Write(document, 1, v);

        return document;
    }

    private static void Write(
        ShapeDocument document,
        int part,
        string text)
    {
        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            char code = layer < text.Length
                ? text[layer]
                : ShapeSignature.EmptyCode;

            document[layer, part] = code switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }
    }

    #endregion
}
