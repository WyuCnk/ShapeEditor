using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 逐列诊断入口（<b>不是测试项</b>，不计入 <c>RunAll</c> 的项数）。
///
/// <para><b>单列诊断</b>：打印五项 / D / F / S、路线、前态的每一列、P1 检查与真实回放。</para>
/// <code>
/// ShapeEditor.Tests.exe --col ".T.cTT.c" --parts 6
/// $env:SHAPE_DIAG_COLUMNS = ".T.cTT.c"
/// $env:SHAPE_DIAG_PARTS   = "6"      # 新增；省略时默认 4
/// </code>
/// <para>
/// <b>审计</b>（任务 3）：n = 6 / n = 8 的全部 S 列（层 3–9）与 n = 4 的 3–9 层回归；
/// 每层都统计 failures / P1 违规 / <see cref="AngleConstruction.FillBreak"/> 例数
/// （n ≥ 6 必须为 0）。
/// </para>
/// <code>
/// ShapeEditor.Tests.exe --audit --parts 6
/// ShapeEditor.Tests.exe --audit --parts 8
/// ShapeEditor.Tests.exe --audit --parts 4
/// ShapeEditor.Tests.exe --audit                 # 三组一起跑
/// $env:SHAPE_DIAG_AUDIT = "1"
/// </code>
/// <para>
/// 只有显式给出参数 / 环境变量时才进入；正常运行测试时完全静默。
/// 旧入口 <c>SHAPE_TWO_STAGE_AUDIT=1</c> + <c>SHAPE_DIAG_COLUMNS=...</c> 的行为保持不变
/// （本入口在那种组合下让位给 <see cref="TwoStageAudit"/>）。
/// </para>
/// </summary>
internal static class AngleDiagCli
{
    public static bool TryRun(
        string[] args,
        out int exitCode)
    {
        exitCode = 0;

        string? column = null;
        bool auditRequested = false;
        int parts = ReadParts(out bool hasParts);

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--col":
                    if (index + 1 >= args.Length)
                    {
                        Console.WriteLine("用法：--col \"列文本\" [--parts N]");
                        exitCode = 2;
                        return true;
                    }

                    column = args[++index];
                    break;

                case "--parts":
                    if (index + 1 >= args.Length ||
                        !int.TryParse(args[index + 1], out parts) ||
                        parts < 2 ||
                        parts % 2 != 0)
                    {
                        Console.WriteLine("--parts 需要一个偶数（例如 4 / 6 / 8）。");
                        exitCode = 2;
                        return true;
                    }

                    hasParts = true;
                    index++;
                    break;

                case "--audit":
                    auditRequested = true;
                    break;
            }
        }

        string envAudit = Environment.GetEnvironmentVariable("SHAPE_DIAG_AUDIT") ?? string.Empty;

        if (!auditRequested && envAudit == "1")
        {
            auditRequested = true;
        }

        if (auditRequested)
        {
            if (hasParts)
            {
                exitCode = RunAudit(parts);
            }
            else
            {
                exitCode = RunAllAudits();
            }

            return true;
        }

        string envColumns = Environment.GetEnvironmentVariable("SHAPE_DIAG_COLUMNS") ?? string.Empty;

        if (column is null)
        {
            if (string.IsNullOrEmpty(envColumns))
            {
                return false;
            }

            /*
             * 向后兼容：SHAPE_TWO_STAGE_AUDIT=1 且没有设置新变量 SHAPE_DIAG_PARTS 时，
             * SHAPE_DIAG_COLUMNS 仍归 TwoStageAudit（它打印 DescribeConstructedH）。
             */
            if (Environment.GetEnvironmentVariable("SHAPE_TWO_STAGE_AUDIT") == "1" &&
                Environment.GetEnvironmentVariable("SHAPE_DIAG_PARTS") is null)
            {
                return false;
            }

            column = envColumns;
        }

        if (column is null)
        {
            return false;
        }

        foreach (string text in column.Split(
                     new[] { ',' },
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            Console.WriteLine();
            RunSingle(text, parts);
        }

        return true;
    }

    private static int ReadParts(out bool hasParts)
    {
        hasParts = int.TryParse(
            Environment.GetEnvironmentVariable("SHAPE_DIAG_PARTS"),
            out int parts);

        return hasParts ? parts : 4;
    }

    #region 单列诊断

    private static void RunSingle(
        string text,
        int parts)
    {
        int layers = text.Length;

        ShapeDocument target = BuildColumn(text, layers, parts);

        ColumnCheck check = ShapeSignature.CheckColumn(text);
        bool d = PillarSCriterion.BottomResourceD(text);
        bool f = PillarSCriterion.RecursiveTerminalF(text);
        bool s = parts >= 6 ? check.IsLegal : check.IsLegal && (d || f);

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

        Console.WriteLine($"parts = {parts}, layers = {layers}");
        Console.WriteLine("目标列   = " + text);
        Console.WriteLine(
            "五项     = " + Lower(check.IsLegal) +
            " ; D = " + Lower(d) +
            " ; F = " + Lower(f) +
            " ; " + SLabel(parts) + " = " + Lower(s));
        Console.WriteLine("路线     = " + RouteName(order));
        Console.WriteLine(
            "分档     = " + AlgorithmName(order.Algorithm) +
            " ; 构造 = " + order.Construction +
            " ; 完成 = " + Lower(order.IsComplete));

        if (!order.IsComplete)
        {
            Console.WriteLine("未覆盖原因 = " + (order.Note ?? "（未给出）"));
        }

        ShapeDocument? preState = FirstPreState(order);

        if (preState is null)
        {
            Console.WriteLine("前态：       （该路线没有前态）");
            Console.WriteLine("P1 检查  = —（无前态）");
        }
        else
        {
            Console.WriteLine("前态：");

            for (int part = 0; part < preState.PartCount; part++)
            {
                Console.WriteLine(
                    "  col" + part + " = " +
                    ShapeSignature.ColumnText(preState, part));
            }

            if (AngleWorkOrderBuilder.P1Violation(
                    preState,
                    out int p1Part,
                    out int p1Layer))
            {
                Console.WriteLine(
                    "P1 检查  = FAIL（第 " + (p1Part + 1) + " 象限第 " +
                    (p1Layer + 1) + " 层晶体下方为空）");
            }
            else
            {
                Console.WriteLine("P1 检查  = PASS");
            }
        }

        Console.WriteLine(DescribeReplay(order, preState, text));
    }

    /// <summary>
    /// 真实回放：Pin 路线用 <see cref="ShapePhysics.Pin"/>，Cut 路线用
    /// 顺转 → <see cref="ShapePhysics.Cut"/> → 逆转。逐格比较 col0 与目标列。
    /// </summary>
    private static string DescribeReplay(
        AngleWorkOrder order,
        ShapeDocument? preState,
        string expected)
    {
        if (preState is null)
        {
            return "回放     = —（无前态）";
        }

        AngleWorkStep? pin = order.Steps.FirstOrDefault(
            step => step.Kind == AngleWorkStepKind.Pin);

        if (pin is not null)
        {
            PhysicsResult pinned = ShapePhysics.Pin(preState);

            if (!pinned.Success || pinned.Document is null)
            {
                return "回放     = Pin 失败：" + pinned.Error;
            }

            return Compare("Pin", pinned.Document, 0, expected);
        }

        bool hasCut = order.Steps.Any(step => step.Kind == AngleWorkStepKind.Cut);

        if (!hasCut)
        {
            return "回放     = —（该路线没有 Cut / Pin 物理步骤）";
        }

        CutResult cut = ShapePhysics.Cut(ShapeOps.Rotate(preState, order.Rotation));

        if (!cut.Success || cut.West is null)
        {
            return "回放     = Cut 失败：" + (cut.Error ?? "未知原因");
        }

        ShapeDocument back = ShapeOps.Rotate(cut.West!, -order.Rotation);

        return Compare("Cut", back, order.Column, expected);
    }

    private static string Compare(
        string operation,
        ShapeDocument result,
        int column,
        string expected)
    {
        string actual = ShapeSignature.ColumnText(result, column);
        string padded = expected.PadRight(result.LayerCount, '.');

        return "回放     = " + operation + " → col" + column + " = " + actual +
               (actual == padded ? " ✔" : " ✘（期望 " + padded + "）");
    }

    private static ShapeDocument? FirstPreState(
        AngleWorkOrder order)
    {
        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind == AngleWorkStepKind.PreState && step.Shape is not null)
            {
                return step.Shape;
            }
        }

        return null;
    }

    private static string SLabel(
        int parts)
    {
        return parts >= 6
            ? "S(" + ChineseParts(parts) + "=五项)"
            : "S(四象限=五项∧(D∨F))";
    }

    private static string ChineseParts(
        int parts)
    {
        return parts switch
        {
            6 => "六象限",
            8 => "八象限",
            _ => parts + " 象限"
        };
    }

    private static string RouteName(
        AngleWorkOrder order)
    {
        return order.Construction switch
        {
            AngleConstruction.F => "构造 F",
            AngleConstruction.H => "构造 H",
            AngleConstruction.C => "构造 C",
            AngleConstruction.A => "构造 A",
            AngleConstruction.G => "构造 G",
            AngleConstruction.D => "构造 D",
            AngleConstruction.CrystalBreak => "碎晶（正规核心 + 托柱 + 保险丝，一刀）",
            AngleConstruction.FillBreak => "填充+暴破",
            AngleConstruction.Half => "半边工序",
            _ => "无构造"
        };
    }

    private static string AlgorithmName(
        AngleAlgorithm algorithm)
    {
        return algorithm switch
        {
            AngleAlgorithm.None => "无",
            AngleAlgorithm.NoCrystal => "构造 C（无晶体）",
            AngleAlgorithm.SimpleTail => "构造 A + C（Simple）",
            AngleAlgorithm.Algorithm1 => "算法 1",
            AngleAlgorithm.Algorithm2 => "算法 2",
            AngleAlgorithm.Algorithm3 => "算法 3",
            AngleAlgorithm.Algorithm4 => "算法 4",
            AngleAlgorithm.Algorithm5 => "算法 5",
            AngleAlgorithm.Algorithm6 => "算法 6",
            AngleAlgorithm.Fallback => "兜底",
            _ => algorithm.ToString()
        };
    }

    private static string Lower(
        bool value)
    {
        return value ? "true" : "false";
    }

    private static ShapeDocument BuildColumn(
        string text,
        int layers,
        int parts)
    {
        var document = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers && layer < text.Length; layer++)
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

    #endregion

    #region 审计

    private static readonly char[] AuditAlphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    private static int RunAllAudits()
    {
        int failures = 0;
        int p1 = 0;
        int fillBreak = 0;

        foreach (int parts in new[] { 6, 8 })
        {
            (int auditFailures, int auditP1, int auditFillBreak) =
                AuditParts(parts, 3, 9);
            failures += auditFailures;
            p1 += auditP1;
            fillBreak += auditFillBreak;
        }

        (int fourFailures, int fourP1, int fourFillBreak) = AuditParts(4, 3, 9);
        failures += fourFailures;
        p1 += fourP1;
        fillBreak += fourFillBreak;

        Console.WriteLine(
            "AUDIT-ALL parts=4/6/8 failures=" + failures + " p1Violations=" + p1 +
            " fillBreak=" + fillBreak +
            (failures == 0 && p1 == 0 ? " ✔" : " ✘"));

        return failures == 0 && p1 == 0 ? 0 : 1;
    }

    private static int RunAudit(
        int parts)
    {
        (int failures, int p1, int fillBreak) = AuditParts(
            parts,
            3,
            9);

        Console.WriteLine(
            "AUDIT-ALL parts=" + parts + " failures=" + failures +
            " p1Violations=" + p1 + " fillBreak=" + fillBreak +
            (failures == 0 && p1 == 0 ? " ✔" : " ✘"));

        return failures == 0 && p1 == 0 ? 0 : 1;
    }

    private static (int Failures, int P1, int FillBreak) AuditParts(
        int parts,
        int minLayers,
        int maxLayers)
    {
        int totalFailures = 0;
        int totalP1 = 0;
        int totalFillBreak = 0;
        int totalS = 0;
        var summary =
            new List<(int Layers, int S, int Failures, int P1, int FillBreak)>();

        for (int layers = minLayers; layers <= maxLayers; layers++)
        {
            long combinations = 1;

            for (int index = 0; index < layers; index++)
            {
                combinations *= AuditAlphabet.Length;
            }

            var buffer = new char[layers];
            int legal = 0;
            int s = 0;
            int failures = 0;
            int p1 = 0;
            int fillBreak = 0;
            var failedSamples = new List<string>();
            var p1Samples = new List<string>();

            for (long code = 0; code < combinations; code++)
            {
                long rest = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = AuditAlphabet[rest % AuditAlphabet.Length];
                    rest /= AuditAlphabet.Length;
                }

                string text = new(buffer);
                ShapeDocument target = BuildColumn(text, layers, parts);

                if (!ShapeSignature.IsLegalColumn(target, 0))
                {
                    continue;
                }

                legal++;

                bool isS = parts >= 6 ||
                           PillarSCriterion.IsPillarColumn(target, 0);

                if (!isS)
                {
                    continue;
                }

                s++;

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                    target,
                    0,
                    layers);

                if (order.Construction == AngleConstruction.FillBreak)
                {
                    fillBreak++;
                }

                if (!order.IsComplete)
                {
                    failures++;

                    if (failedSamples.Count < 10)
                    {
                        failedSamples.Add(text + "：" + (order.Note ?? "?"));
                    }

                    continue;
                }

                ShapeDocument? pre = FirstPreState(order);

                if (pre is not null &&
                    AngleWorkOrderBuilder.P1Violation(
                        pre,
                        out int p1Part,
                        out int p1Layer))
                {
                    p1++;

                    if (p1Samples.Count < 10)
                    {
                        p1Samples.Add(
                            text + "（第 " + (p1Part + 1) + " 象限第 " +
                            (p1Layer + 1) + " 层）");
                    }
                }
            }

            totalFailures += failures;
            totalP1 += p1;
            totalFillBreak += fillBreak;
            totalS += s;

            Console.WriteLine(
                "AUDIT n=" + parts + " layers=" + layers +
                " legal=" + legal + " S=" + s +
                " failures=" + failures + " p1Violations=" + p1 +
                " fillBreak=" + fillBreak);

            foreach (string sample in failedSamples)
            {
                Console.WriteLine("  FAIL " + sample);
            }

            foreach (string sample in p1Samples)
            {
                Console.WriteLine("  P1   " + sample);
            }

            summary.Add((layers, s, failures, p1, fillBreak));
        }

        if (parts == 4)
        {
            int low = summary
                .Where(entry => entry.Layers <= 6)
                .Sum(entry => entry.S);
            int lowFailures = summary
                .Where(entry => entry.Layers <= 6)
                .Sum(entry => entry.Failures);

            Console.WriteLine(
                "AUDIT n=4 3–6 层：S=" + low + "（记录值 2125） failures=" +
                lowFailures);

            foreach ((int layers, int s, int failures, int p1, int fillBreak)
                     in summary.Where(entry => entry.Layers >= 7))
            {
                Console.WriteLine(
                    "AUDIT n=4 " + layers + " 层：S=" + s + " failures=" + failures +
                    " p1Violations=" + p1 + " fillBreak=" + fillBreak);
            }
        }

        Console.WriteLine(
            "AUDIT n=" + parts + " 合计：S=" + totalS + " failures=" + totalFailures +
            " p1Violations=" + totalP1 + " fillBreak=" + totalFillBreak +
            (totalFailures == 0 && totalP1 == 0 ? " ✔" : " ✘"));

        return (totalFailures, totalP1, totalFillBreak);
    }

    #endregion
}
