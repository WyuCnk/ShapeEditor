using System.Diagnostics;
using System.IO;
using System.Text;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Tests;

/// <summary>
/// 导出模式（不是测试项）：把 3–9 层的四份列清单写进 <c>docs/handoff/lists/</c>。
///
/// <para>
/// 由环境变量 <c>SHAPE_EXPORT_LISTS=1</c> 触发（见 <see cref="Program"/>）：
/// 只跑导出、不跑断言、不跑常规测试。不设置该变量时，常规测试行为完全不变。
/// </para>
/// <para>
/// 集合范围：3–9 层、全部 S 列（<see cref="PillarSCriterion.IsPillarColumn(string)"/>）
/// 且<b>含晶体</b>的列。其中「含晶体」按任务书的实测口径取
/// <see cref="AngleBuildKind.NeedsCrystalBreak"/>（最高晶体下方有间隙；本轮实测 3–9 层共 20100 列）。
/// 含晶体但最高晶体连续接地的简单角（本轮实测 24566 列）不进任何清单，只作诊断计数。
/// 分类口径与 <see cref="Probes"/> 的 stats 模式一致：
/// </para>
/// <list type="bullet">
/// <item>碎晶·一刀：<see cref="AngleWorkOrder.EffectiveRoute"/> 是
/// <see cref="AngleRoute.CrystalBreak"/> 且 <c>Cut</c> 步数 == 1；</item>
/// <item>碎晶·多刀：同路线且 <c>Cut</c> 步数 &gt;= 2；</item>
/// <item>弱落：工序里有 <see cref="AngleWorkStepKind.WeakDrop"/>；</item>
/// <item>多刀：<c>Cut</c> 步数 &gt;= 2（不分路线）。</item>
/// </list>
/// <para>
/// 每行一列，纯列字符串（<c>.</c> <c>T</c> <c>P</c> <c>c</c>，左端 = 第 1 层），
/// 按层数、再字典序（<see cref="StringComparer.Ordinal"/>）排序；
/// <c>multi-cut.txt</c> 行尾附 <c>\t刀数</c>。
/// </para>
/// </summary>
internal static class ExportLists
{
    /// <summary>枚举的层数下界（与 stats 模式一致）。</summary>
    private const int MinLayers = 3;

    /// <summary>枚举的层数上界（与 stats 模式一致）。</summary>
    private const int MaxLayers = 9;

    /// <summary>
    /// 入口：枚举 → 分类 → 写四份清单 → 打印统计行。
    /// </summary>
    public static int Run()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "docs", "handoff", "lists");

        Directory.CreateDirectory(directory);

        // 与 stats 模式相同的枚举字母表；顺序不影响结果（写盘前统一字典序排序）。
        char[] alphabet =
        {
            ShapeSignature.EmptyCode,
            ShapeSignature.SolidCode,
            ShapeSignature.PinCode,
            ShapeSignature.CrystalCode
        };

        var oneCut = new List<string>();
        var noOneCut = new List<string>();
        var weak = new List<string>();
        var multiCut = new List<(string Text, int Cuts)>();
        var layerCounts = new List<string>();

        int sColumns = 0;
        int crystalColumns = 0;
        int crystalNeedsBreak = 0;
        int crystalSimple = 0;
        int incomplete = 0;
        int weakAllS = 0;
        int multiAllS = 0;
        int broadWeak = 0;
        int broadMulti = 0;
        int breakdownWeak = 0;
        int breakdownMulti = 0;

        var watch = Stopwatch.StartNew();

        for (int layers = MinLayers; layers <= MaxLayers; layers++)
        {
            int combinations = (int)Math.Pow(alphabet.Length, layers);
            var buffer = new char[layers];
            int layerS = 0;
            int layerCrystal = 0;
            int layerNeedsBreak = 0;
            int layerOneCut = 0;
            int layerWeak = 0;
            int layerMulti = 0;

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = alphabet[remaining % alphabet.Length];
                    remaining /= alphabet.Length;
                }

                string text = new(buffer);

                // 一、合法列 ∧ S 判据（五项 ∧ (D∨F)）。
                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                sColumns++;
                layerS++;

                // 二、集合范围：只留含晶体的列。
                if (!text.Contains(ShapeSignature.CrystalCode))
                {
                    continue;
                }

                crystalColumns++;
                layerCrystal++;

                ShapeDocument target = BuildColumn(text);
                AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                    target,
                    0,
                    layers);

                int cuts = 0;
                bool hasWeak = false;

                foreach (AngleWorkStep step in order.Steps)
                {
                    if (step.Kind == AngleWorkStepKind.Cut)
                    {
                        cuts++;
                    }
                    else if (step.Kind == AngleWorkStepKind.WeakDrop)
                    {
                        hasWeak = true;
                    }
                }

                /*
                 * 三、集合范围：只留「碎晶角」（<see cref="AngleBuildKind.NeedsCrystalBreak"/>，
                 * 即最高晶体下方有间隙的列）。这是任务书里的 20100 列口径；
                 * 「含晶体但最高晶体连续接地」的简单角不进任何清单，只作诊断计数。
                 */
                if (order.Kind != AngleBuildKind.NeedsCrystalBreak)
                {
                    crystalSimple++;

                    if (order.IsComplete && hasWeak)
                    {
                        broadWeak++;
                    }

                    if (order.IsComplete && cuts >= 2)
                    {
                        broadMulti++;
                    }

                    continue;
                }

                crystalNeedsBreak++;
                layerNeedsBreak++;

                if (!order.IsComplete)
                {
                    incomplete++;
                }

                bool broken =
                    order.IsComplete &&
                    order.EffectiveRoute == AngleRoute.CrystalBreak &&
                    cuts == 1;

                if (broken)
                {
                    oneCut.Add(text);
                    layerOneCut++;
                }
                else
                {
                    noOneCut.Add(text);
                }

                /*
                 * 弱落 / 多刀只统计回放通过的工序（与 stats 模式的 continue 一致）；
                 * 失败工序的刀数与弱落不计入清单，但单独记一笔便于核对。
                 */
                if (!order.IsComplete)
                {
                    if (hasWeak)
                    {
                        breakdownWeak++;
                    }

                    if (cuts >= 2)
                    {
                        breakdownMulti++;
                    }

                    continue;
                }

                if (cuts >= 2)
                {
                    multiAllS++;
                }

                if (hasWeak)
                {
                    weakAllS++;
                }

                if (hasWeak)
                {
                    weak.Add(text);
                    layerWeak++;
                }
                if (cuts >= 2)
                {
                    multiCut.Add((text, cuts));
                    layerMulti++;
                }
            }

            layerCounts.Add(
                "EXPORT L=" + layers +
                " S=" + layerS +
                " crystal=" + layerCrystal +
                " needsBreak=" + layerNeedsBreak +
                " oneCut=" + layerOneCut +
                " weak=" + layerWeak +
                " multiCut=" + layerMulti);
        }

        watch.Stop();

        // 三、按层数、再字典序排序（长度就是层数，所以先比长度）。
        string[] oneCutSorted = oneCut
            .OrderBy(text => text.Length)
            .ThenBy(text => text, StringComparer.Ordinal)
            .ToArray();

        string[] noOneCutSorted = noOneCut
            .OrderBy(text => text.Length)
            .ThenBy(text => text, StringComparer.Ordinal)
            .ToArray();

        string[] weakSorted = weak
            .OrderBy(text => text.Length)
            .ThenBy(text => text, StringComparer.Ordinal)
            .ToArray();

        string[] multiSorted = multiCut
            .OrderBy(pair => pair.Text.Length)
            .ThenBy(pair => pair.Text, StringComparer.Ordinal)
            .Select(pair => pair.Text + "\t" + pair.Cuts)
            .ToArray();

        // 四、写盘（UTF-8 无 BOM，LF 换行）。
        string oneCutPath = Path.Combine(directory, "one-cut.txt");
        string noOneCutPath = Path.Combine(directory, "no-one-cut.txt");
        string weakPath = Path.Combine(directory, "weakdrop.txt");
        string multiPath = Path.Combine(directory, "multi-cut.txt");

        WriteLines(oneCutPath, oneCutSorted);
        WriteLines(noOneCutPath, noOneCutSorted);
        WriteLines(weakPath, weakSorted);
        WriteLines(multiPath, multiSorted);

        foreach (string line in layerCounts)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(
            "EXPORT files dir=" + directory +
            " oneCutFile=" + oneCutSorted.Length +
            " noOneCutFile=" + noOneCutSorted.Length +
            " weakFile=" + weakSorted.Length +
            " multiCutFile=" + multiSorted.Length);

        Console.WriteLine(
            "EXPORT check S=" + sColumns +
            " crystal=" + crystalColumns +
            " needsBreak=" + crystalNeedsBreak +
            " simple=" + crystalSimple +
            " incomplete=" + incomplete +
            " weakAllS=" + weakAllS +
            " multiAllS=" + multiAllS +
            " broadWeak=" + broadWeak +
            " broadMulti=" + broadMulti +
            " weakOnIncomplete=" + breakdownWeak +
            " multiOnIncomplete=" + breakdownMulti +
            " ms=" + watch.ElapsedMilliseconds);

        Console.WriteLine(
            "EXPORT oneCut=" + oneCutSorted.Length +
            " noOneCut=" + noOneCutSorted.Length +
            " weak=" + weakSorted.Length +
            " multiCut=" + multiSorted.Length +
            " (L3-" + MaxLayers + ")");

        return 0;
    }

    /// <summary>
    /// 仓库根目录：从程序所在目录向上找 <c>ShapeEditor.slnx</c>；
    /// 找不到再退回当前工作目录向上找；都没有就用当前工作目录。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        string? found = Probe(AppContext.BaseDirectory) ??
                        Probe(Directory.GetCurrentDirectory());

        return found ?? Directory.GetCurrentDirectory();
    }

    private static string? Probe(string? start)
    {
        string? directory = start;

        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "ShapeEditor.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(
                directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
        }

        return null;
    }

    /// <summary>写文本文件：UTF-8 无 BOM，行尾 LF，末尾留一个换行。</summary>
    private static void WriteLines(string path, IEnumerable<string> lines)
    {
        var builder = new StringBuilder();

        foreach (string line in lines)
        {
            builder.Append(line).Append('\n');
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    /// <summary>把列字符串变成只含第 0 象限的图形（与 Probes 的 BuildColumn 同构）。</summary>
    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, 4);

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
