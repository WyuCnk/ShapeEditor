using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 构造 H（§0.4 七步 + §17.1 R1）的诊断入口。
///
/// <para>
/// 只在环境变量 <c>SHAPE_TWO_STAGE_AUDIT=1</c> 时由 <see cref="Program"/> 调用，
/// 不是常规测试项（不计入项数）：它把 ≤N 层全部 S 列在「只走 §0.4 七步」的
/// 口径下跑一遍，打印每层失败数、失败清单与每条失败例的 7 步中间值，
/// 供 <c>docs/angle-work-order-uncovered-h.md</c> 使用。
/// 输出仍沿用 <c>TWOSTAGE</c> 前缀，方便与删除旧兜底段之前的基线直接对比。
/// </para>
/// </summary>
internal static class TwoStageAudit
{
    private const int Parts = 4;

    private static readonly char[] Alphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    public static int Run()
    {
        return RunCore();
    }

    private static int RunCore()
    {
        string? diagColumns = Environment.GetEnvironmentVariable("SHAPE_DIAG_COLUMNS");

        if (!string.IsNullOrEmpty(diagColumns))
        {
            foreach (string text in diagColumns.Split(
                         new[] { ',' },
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                Console.WriteLine();
                Console.WriteLine(AngleWorkOrderBuilder.DescribeConstructedH(
                    BuildColumn(text),
                    0,
                    text.Length));
            }

            return 0;
        }

        (int minLayers, int maxLayers) = ReadRange();

        int details = ReadInt("SHAPE_TWO_STAGE_DETAILS", 0);
        var failures = new List<(string Text, int Layers)>();
        int totalFailures = 0;

        for (int layers = minLayers; layers <= maxLayers; layers++)
        {
            int combinations = 1 << (2 * layers);
            var buffer = new char[layers];
            int s = 0;
            int success = 0;
            int layerFailures = 0;

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = Alphabet[remaining % Alphabet.Length];
                    remaining /= Alphabet.Length;
                }

                string text = new(buffer);
                ShapeDocument target = BuildColumn(text);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(target, 0))
                {
                    continue;
                }

                s++;

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (order.IsComplete)
                {
                    success++;
                    continue;
                }

                layerFailures++;
                failures.Add((text, layers));
            }

            totalFailures += layerFailures;

            Console.WriteLine(
                $"  TWOSTAGE layers={layers} s={s} strictSuccess={success} " +
                $"strictFailures={layerFailures}");
        }

        Console.WriteLine($"  TWOSTAGE strictFailures={totalFailures}");

        // 失败按「目标形态」分组：把晶体层保留、其余压成计数字形，便于看族。
        foreach (var group in failures
                     .GroupBy(pair => Family(pair.Text))
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"  TWOSTAGEFAMILY n={group.Count()} family={group.Key} " +
                "例：" + string.Join(" ", group.Take(6).Select(pair => pair.Text)));
        }

        foreach ((string text, int layers) in failures)
        {
            Console.WriteLine($"  TWOSTAGEFAIL {text}（{layers} 层）");
        }

        if (details > 0)
        {
            foreach ((string text, int layers) in failures.Take(details))
            {
                Console.WriteLine();
                Console.WriteLine(AngleWorkOrderBuilder.DescribeConstructedH(
                    BuildColumn(text),
                    0,
                    layers));
            }
        }

        RunNamedCorpora();

        return 0;
    }

    /// <summary>把列文本压成「族」：只保留晶体与针的层，其余记作长度。</summary>
    private static string Family(string text)
    {
        var key = new System.Text.StringBuilder();

        for (int layer = 0; layer < text.Length; layer++)
        {
            key.Append(text[layer] switch
            {
                ShapeSignature.PinCode => 'P',
                ShapeSignature.CrystalCode => 'c',
                _ => '.'
            });
        }

        return key.ToString();
    }

    private static void RunNamedCorpora()
    {
        string[] corpora =
        {
            // 任务指定的三语料（§17 的三个代表例）。
            "P.T.T..c", "PP.T.cTc", "P.T.T..cP",
            // §0.4 的深间隙 / 双段 / 双晶体语料。
            "P.T..c", "P.T...c", "P.T....c", "P.T...cT", "P.T...cc",
            "..T.cc", "..T.ccT", "..T.cc..",
            "..T.cTc", "..T.cTcT", "..T.cTc..",
            // 多刀三语料。
            "TT.T.c", "TT.T.T.c", "cTT.T.c",
            // §17.1 R1 / §17.2 R2 / §17.3 R3 的代表列。
            "P..T.T.c", "P.T.T...c", "P.T..T..c", "P.TT.T..c",
            "P..T..T.c", "P...T.T.c", "..T.T.cTc", "P.T.T.cTc",
            "PP.T..cTc", "PPP.T.cTc", "PP.T.ccTc",
            "P..T.T.cP"
        };

        foreach (string text in corpora)
        {
            ShapeDocument target = BuildColumn(text);

            if (!ShapeSignature.CheckColumn(text).IsLegal)
            {
                Console.WriteLine($"  TWOSTAGECORPUS {text}：不合法（跳过）");
                continue;
            }

            /*
             * 旧的两段开关已删除：现在只剩一条 §0.4 构造 H。
             * default / strict 两个名字保留为「同一口径」，便于和旧基线对比。
             */
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);
            AngleWorkOrder strictOrder = order;

            Console.WriteLine(
                $"  TWOSTAGECORPUS {text} default={(order.IsComplete ? "OK" : "FAIL")}" +
                $" strict={(strictOrder.IsComplete ? "OK" : "FAIL")}" +
                (strictOrder.IsComplete
                    ? string.Empty
                    : "：" + (strictOrder.Note ?? "无原因")));

            if (order.IsComplete && order.Steps.Count > 0 &&
                order.Steps[0].Shape is { } pre)
            {
                var columns = new List<string>();

                for (int part = 0; part < pre.PartCount; part++)
                {
                    columns.Add(ShapeSignature.ColumnText(pre, part));
                }

                Console.WriteLine(
                    $"  TWOSTAGEPRE {text} constr={order.Construction} " +
                    "pre=" + string.Join("|", columns));

                if (Environment.GetEnvironmentVariable("SHAPE_TWO_STAGE_PIN") == "1")
                {
                    PhysicsResult pin = ShapePhysics.Pin(pre);

                    if (!pin.Success || pin.Document is null)
                    {
                        Console.WriteLine(
                            $"  TWOSTAGEPIN {text} FAIL {pin.Error}");
                    }
                    else
                    {
                        var pinned = new List<string>();

                        for (int part = 0; part < pin.Document.PartCount; part++)
                        {
                            pinned.Add(ShapeSignature.ColumnText(pin.Document, part));
                        }

                        Console.WriteLine(
                            $"  TWOSTAGEPIN {text} pin=" + string.Join("|", pinned));
                    }
                }
            }
        }
    }

    private static (int Min, int Max) ReadRange()
    {
        string? raw = Environment.GetEnvironmentVariable("SHAPE_TWO_STAGE_LAYERS");

        if (string.IsNullOrEmpty(raw))
        {
            return (3, 8);
        }

        int dash = raw.IndexOf('-');

        if (dash < 0)
        {
            int single = int.Parse(raw);

            return (single, single);
        }

        return (int.Parse(raw[..dash]), int.Parse(raw[(dash + 1)..]));
    }

    private static int ReadInt(string name, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);

        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, Parts);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                'c' => ShapeCell.Crystal('r'),
                'P' => ShapeCell.Pin(),
                'T' => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }
}
