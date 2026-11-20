using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 工序卡片命令行入口（<b>不是测试项</b>，不计入 <c>RunAll</c> 的项数）。
///
/// <para><b>用法</b></para>
/// <code>
/// ShapeEditor.Tests.exe --cards "crCu----:CuCu----:--Cu----:CuCu----:--------:crcr----:P-cr----:Cucr----"
/// ShapeEditor.Tests.exe --cards-col ".T.cTT.c" --parts 6
/// $env:SHAPE_CARDS = "短代码"
/// </code>
/// <para>
/// 逐卡打印「角卡 / 半边卡 / 交换卡」的数量、Verified、Inputs 数量、可见步骤与输出，
/// 最后一行是汇总（便于肉眼核对，也便于贴到汇报里）。
/// 没有 <c>--cards</c> / <c>--cards-col</c> / <c>SHAPE_CARDS</c> 时<b>完全静默</b>，
/// 不影响现有测试。
/// </para>
/// </summary>
internal static class CardCli
{
    public static bool TryRun(
        string[] args,
        out int exitCode)
    {
        exitCode = 0;

        string? code = null;
        string? columnText = null;
        int parts = 4;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--cards":
                    if (index + 1 >= args.Length)
                    {
                        Console.WriteLine("用法：--cards \"完整短代码\"");
                        exitCode = 2;
                        return true;
                    }

                    code = args[++index];
                    break;

                case "--cards-col":
                    if (index + 1 >= args.Length)
                    {
                        Console.WriteLine("用法：--cards-col \"列文本\" [--parts N]");
                        exitCode = 2;
                        return true;
                    }

                    columnText = args[++index];
                    break;

                case "--parts":
                    if (index + 1 < args.Length &&
                        int.TryParse(args[index + 1], out int parsed) &&
                        parsed >= 2 &&
                        parsed % 2 == 0)
                    {
                        parts = parsed;
                        index++;
                    }
                    else
                    {
                        Console.WriteLine("--parts 需要一个偶数（例如 4 / 6 / 8）。");
                        exitCode = 2;
                        return true;
                    }

                    break;
            }
        }

        if (code is null && columnText is null)
        {
            string env = Environment.GetEnvironmentVariable("SHAPE_CARDS") ?? string.Empty;
            code = env.Length == 0 ? null : env;
        }

        if (code is null && columnText is null)
        {
            return false;
        }

        exitCode = Run(code, columnText, parts);

        return true;
    }

    private static int Run(
        string? code,
        string? columnText,
        int parts)
    {
        ShapeDocument? target = null;

        if (code is not null)
        {
            if (!ShortCodeParser.TryInferDimensions(
                    code,
                    out int layers,
                    out int inferredParts,
                    out string? inferError))
            {
                Console.WriteLine("短代码尺寸推断失败：" + inferError);
                return 2;
            }

            ParseResult parsed = new ShortCodeParser().Parse(code, layers, inferredParts);

            if (!parsed.Success || parsed.Document is null)
            {
                Console.WriteLine("短代码解析失败：" + parsed.Error);
                return 2;
            }

            target = parsed.Document;
        }
        else
        {
            target = BuildColumn(columnText!, parts);
        }

        Recipe? recipe = null;

        PipelineResult solved = ProductionPipeline.Solve(
            target,
            target.LayerCount,
            ProductionPipeline.DefaultMaxRank);

        if (solved.Outcome == PipelineOutcome.Producible)
        {
            recipe = solved.Recipe;
        }

        WorkOrderCardSet set = CardBuilder.Build(target, recipe);

        Print(set, target);

        return 0;
    }

    private static void Print(
        WorkOrderCardSet set,
        ShapeDocument target)
    {
        string exporter = new ShortCodeExporter().Export(set.Target);

        Console.WriteLine(
            "图形：" + exporter +
            "（" + set.Target.LayerCount + " 层，n=" + set.Target.PartCount +
            "，cap=" + set.Target.LayerCount + "）");
        Console.WriteLine();

        foreach (WorkOrderCardView card in set.CornerCards)
        {
            PrintCorner(card);
        }

        foreach (WorkOrderCardView card in set.HalfCards)
        {
            PrintHalf(card);
        }

        if (set.ExchangeCard is not null)
        {
            PrintExchange(set.ExchangeCard);
        }

        if (!string.IsNullOrEmpty(set.Note))
        {
            Console.WriteLine("说明：" + set.Note);
        }

        Console.WriteLine();
        Console.WriteLine(
            "汇总：cards=" + set.Cards.Count +
            " corner=" + set.CornerCards.Count +
            " half=" + set.HalfCards.Count +
            " exchange=" + (set.ExchangeCard is null ? 0 : 1) +
            " verified=" + set.VerifiedCount + "/" + set.Cards.Count);
    }

    private static void PrintCorner(
        WorkOrderCardView card)
    {
        Console.WriteLine(
            $"[角卡] {card.Title}\tVerified={Lower(card.Verified)}\tInputs={card.Inputs.Count}");

        Console.WriteLine("  输出（Q1 固定）：" + new ShortCodeExporter().Export(card.Output));

        for (int part = 0; part < card.Output.PartCount; part++)
        {
            string label = part switch
            {
                0 => "Q1（目标角）",
                1 when card.Output.PartCount > 4 => "Q2（右柱）",
                1 => "Q2（右柱）",
                _ => part == card.Output.PartCount - 1
                    ? "Q" + (part + 1) + "（左柱）"
                    : "Q" + (part + 1) + "（空）"
            };

            Console.WriteLine(
                "    " + label + " = " + ShapeSignature.ColumnText(card.Output, part));
        }

        CardStep? preState = card.Steps.FirstOrDefault(
            step => step.Operation == "前态");

        if (preState is not null)
        {
            Console.WriteLine("  前态：" + new ShortCodeExporter().Export(preState.Output));
        }

        Console.WriteLine("  步骤：" + DescribeSteps(card));
        PrintHidden(card);
        PrintNote(card);
        Console.WriteLine();
    }

    private static void PrintHalf(
        WorkOrderCardView card)
    {
        Console.WriteLine(
            $"[半边卡] {card.Title}\tMode={card.Mode}\tVerified={Lower(card.Verified)}\t" +
            "Inputs=" + card.Inputs.Count +
            (card.Inputs.Count > 0
                ? "（" + string.Join(", ", card.InputTitles) + "）"
                : string.Empty));

        for (int index = 0; index < card.Inputs.Count; index++)
        {
            Console.WriteLine(
                "  输入[" + index + "] = " + new ShortCodeExporter().Export(card.Inputs[index]));
        }

        CardStep? preState = card.Steps.FirstOrDefault(
            step => step.Operation == "前态");

        if (preState is not null)
        {
            Console.WriteLine("  前态：" + new ShortCodeExporter().Export(preState.Output));
        }

        Console.WriteLine("  步骤：" + DescribeSteps(card));
        PrintHidden(card);
        Console.WriteLine("  输出 = " + new ShortCodeExporter().Export(card.Output));
        PrintNote(card);
        Console.WriteLine();
    }

    private static void PrintExchange(
        WorkOrderCardView card)
    {
        Console.WriteLine(
            "[交换卡] " + card.Title +
            "\tVerified=" + Lower(card.Verified) +
            "\tInputs=" + card.Inputs.Count +
            (card.Inputs.Count > 0
                ? "（" + string.Join(", ", card.InputTitles) + "）"
                : string.Empty));

        for (int index = 0; index < card.Inputs.Count; index++)
        {
            Console.WriteLine(
                "  输入[" + index + "] = " + new ShortCodeExporter().Export(card.Inputs[index]));
        }

        Console.WriteLine("  步骤：" + DescribeSteps(card));
        PrintHidden(card);
        Console.WriteLine("  输出 = 目标图形 " + new ShortCodeExporter().Export(card.Output));
        PrintNote(card);
        Console.WriteLine();
    }

    private static string DescribeSteps(
        WorkOrderCardView card)
    {
        var visible = card.VisibleSteps;

        if (visible.Count == 0)
        {
            return "（没有可见步骤）";
        }

        return string.Join(
            " → ",
            visible.Select(step => step.Operation));
    }

    private static string DescribeHidden(
        WorkOrderCardView card)
    {
        var hidden = card.Steps.Where(step => step.Hidden).ToArray();

        return hidden.Length == 0
            ? string.Empty
            : string.Join(
                " → ",
                hidden.Select(step => step.Operation));
    }

    private static void PrintNote(
        WorkOrderCardView card)
    {
        if (!string.IsNullOrEmpty(card.Note))
        {
            Console.WriteLine("  Note = " + card.Note);
        }
    }

    /// <summary>隐藏步骤（旋转 / 废料 / 纯支撑交接）单独打一行，便于核对「数据保留」。</summary>
    private static void PrintHidden(
        WorkOrderCardView card)
    {
        string hidden = DescribeHidden(card);

        if (hidden.Length > 0)
        {
            Console.WriteLine("  隐藏：" + hidden);
        }
    }

    private static string Lower(
        bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>按「一列文本 + 象限数」造一个只有第 1 象限有材料的图形。</summary>
    private static ShapeDocument BuildColumn(
        string text,
        int parts)
    {
        var document = new ShapeDocument(text.Length, parts);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }
}
