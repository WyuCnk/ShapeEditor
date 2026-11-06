using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 交接规格 §5 的 9 组验收语料。
///
/// 判定用的数字全部来自 docs/handoff-to-main-agent.md §5 与
/// docs/production-path-spec.md；这里只把它们写成可执行断言，不重新推导判据。
/// 第 6、7 组按规格标注为 xfail（TowerFill 只做直接填充、HybridStrips 只拆单顶针）。
/// </summary>
internal static class ProductionPathAcceptanceTests
{
    #region 语料 1：角判据

    /// <summary>
    /// 语料 1：宽度 1、层数 1–8，{., P, T, c} 全枚举，
    /// 合法列计数必须是 4, 14, 47, 152, 476, 1451, 4328, 12688。
    /// </summary>
    public static void AngleCriterionCounts()
    {
        int[] expected = { 4, 14, 47, 152, 476, 1451, 4328, 12688 };

        var alphabet = new[] { '.', 'P', 'T', 'c' };

        for (int layers = 1; layers <= expected.Length; layers++)
        {
            int total = 1;

            for (int index = 0; index < layers; index++)
            {
                total *= alphabet.Length;
            }

            int legal = 0;

            var buffer = new char[layers];

            for (int code = 0; code < total; code++)
            {
                int rest = code;

                for (int index = 0; index < layers; index++)
                {
                    buffer[index] = alphabet[rest % alphabet.Length];
                    rest /= alphabet.Length;
                }

                if (ShapeSignature.CheckColumn(new string(buffer)).IsLegal)
                {
                    legal++;
                }
            }

            Assert.Equal(
                expected[layers - 1],
                legal,
                $"宽度 1、层数 {layers} 的合法列计数不符：期望 {expected[layers - 1]}，实际 {legal}");
        }
    }

    #endregion

    #region 语料 2：论文举例列与 n=4 的 S 判定

    /// <summary>
    /// 语料 2：论文 §6.1 的 10 个举例列。
    ///
    /// - 五项条件按 <see cref="ShapeSignature"/> 判定；
    /// - n = 4 的 S 判定用论文定理 3：五项 ∧ (D ∨ F)。
    ///   `cT.T.c` 合法但不是 S，是本组的边界样本。
    /// </summary>
    public static void PaperExampleColumns()
    {
        // 列字符串：左端是地面（第 1 层），右端是顶层。
        (string Column, bool Legal, bool? SAtN4)[] cases =
        {
            ("T.c", true, null),
            (".c", false, false),
            ("cT.c", false, false),
            ("cT.T.c", true, false),
            ("cTT..T.c", true, true),
            ("cT.T.ccc", true, true),
            (".T..T.c", true, true),
            ("PPTTc", true, null),
            ("TPc", false, false),
            ("cT.T.cccPT", true, true)
        };

        foreach ((string column, bool legal, bool? sAtN4) in cases)
        {
            Assert.Equal(
                legal,
                ShapeSignature.CheckColumn(column).IsLegal,
                $"{column} 的五项条件判定不符");

            if (sAtN4 is not null)
            {
                Assert.Equal(
                    sAtN4.Value,
                    IsSAtN4(column),
                    $"{column} 的 n=4 S 判定不符");
            }
        }
    }

    /// <summary>
    /// 论文定理 3：n = 4 时 S ⇔ 五项 ∧ (D ∨ F)。
    /// 这是把论文判据写进测试，不是重新推导。
    /// </summary>
    private static bool IsSAtN4(string column)
    {
        return ShapeSignature.CheckColumn(column).IsLegal &&
               (BottomResourceD(column) || RecursiveTerminalF(column));
    }

    /// <summary>定义 D（底部资源条件）。</summary>
    private static bool BottomResourceD(string column)
    {
        if (!column.Contains('c'))
        {
            return true;
        }

        if (column[0] == 'P')
        {
            return true;
        }

        int lowestCrystal = column.IndexOf('c');

        int gaps = 0;

        for (int index = 0; index < lowestCrystal; index++)
        {
            if (column[index] == '.')
            {
                gaps++;
            }
        }

        return gaps >= 2;
    }

    /// <summary>定义 F（递归终点条件）。</summary>
    private static bool RecursiveTerminalF(string column)
    {
        for (int index = 0; index < column.Length; index++)
        {
            if (column[index] != 'c' ||
                index == 0 ||
                column[index - 1] != '.')
            {
                continue;
            }

            // 环下落：从这里开始至少三枚连续晶体。
            bool ringFall =
                index + 2 < column.Length &&
                column[index + 1] == 'c' &&
                column[index + 2] == 'c';

            if (ringFall)
            {
                continue;
            }

            int below = -1;

            for (int probe = index - 1; probe >= 0; probe--)
            {
                if (column[probe] == 'c')
                {
                    below = probe;
                    break;
                }
            }

            // 弱下落终点：与下方最近晶体之间存在相邻 TT。
            bool adjacentPair = false;

            for (int probe = index - 1;
                 probe >= 1 && probe > below;
                 probe--)
            {
                if (column[probe] == 'T' &&
                    column[probe - 1] == 'T')
                {
                    adjacentPair = true;
                    break;
                }
            }

            if (adjacentPair)
            {
                continue;
            }

            // 下方没有晶体时，允许以第 1 层的普通格为终点。
            if (below < 0 && column[0] == 'T')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    #endregion

    #region 语料 3/4/5：可交换 ⟺ 交换链成功

    /// <summary>
    /// 语料 3：n = 6、2 层、{., T, c} 全枚举（3^12 = 531,441）。
    /// 稳定 440,081；稳定且每列合法 232,425；交换链成功 230,601；
    /// 可交换 ⟺ 链成功，0 反例。
    /// </summary>
    public static void TwoLayerEnumeration()
    {
        EnumerationResult result = Enumerate(
            layers: 2,
            parts: 6,
            alphabetSize: 3,
            total: 531441);

        Assert.Equal(440081L, result.Stable, "稳定计数不符");
        Assert.Equal(232425L, result.Legal, "稳定且每列合法的计数不符");
        Assert.Equal(230601L, result.Chain, "交换链成功计数不符");
        Assert.Equal(230601L, result.Swappable, "可交换计数不符");
        Assert.Equal(0L, result.Inconsistent, "可交换 ⟺ 交换链成功 出现反例");
        Assert.Equal(
            1824L,
            result.Legal - result.Chain,
            "不可交换且链失败的样本数应为 1,824（规格 §5.4.1）");
    }

    /// <summary>
    /// 语料 4：n = 6、3 层、{., T} 全枚举（2^18 = 262,144）。
    /// 合格（稳定且每列合法）91,708；可交换 ⟺ 链成功，0 反例。
    /// </summary>
    public static void ThreeLayerBinaryEnumeration()
    {
        EnumerationResult result = Enumerate(
            layers: 3,
            parts: 6,
            alphabetSize: 2,
            total: 262144);

        Assert.Equal(91708L, result.Legal, "合格计数不符");
        Assert.Equal(0L, result.Inconsistent, "可交换 ⟺ 交换链成功 出现反例");
    }

    /// <summary>
    /// 语料 5：n = 6、3 层、{., T, c} 随机抽样。
    ///
    /// 规格只给出了「15,307 个合格样本、0 不一致」，没有记录随机种子与抽样规模，
    /// 因此字面数字无法复现。这里用固定种子 + 固定抽样规模（可复现），
    /// 判定核心结论「0 反例」，并记录本次抽样规模与合格数。
    /// </summary>
    public static void ThreeLayerRandomSample()
    {
        const int trials = 200000;
        const ulong seed = 20250101;

        var random = new SplitMix64(seed);

        var document = new ShapeDocument(3, 6);

        long stable = 0;
        long legal = 0;
        long inconsistent = 0;

        for (int trial = 0; trial < trials; trial++)
        {
            long code = (long)(random.Next() % 387420489UL); // 3^18

            Fill(document, code, 3);

            if (!ShapeOps.IsStable(document))
            {
                continue;
            }

            stable++;

            if (!ShapeSignature.AllColumnsLegal(document, out _))
            {
                continue;
            }

            legal++;

            bool swappable = Swappable.Test(document);
            bool chain = ExchangeChain.TryBuild(document, out _);

            if (swappable != chain)
            {
                inconsistent++;
            }
        }

        Assert.Equal(0L, inconsistent, "抽样中出现「可交换 ⟺ 交换链成功」反例");
        Assert.True(
            legal > 0,
            "抽样没有命中任何合格样本，抽样规模或种子需要调整");

        Console.WriteLine(
            $"        语料5 抽样：种子 {seed}，规模 {trials}，" +
            $"稳定 {stable}，合格 {legal}，反例 {inconsistent}");
    }

    /// <summary>全枚举的统计结果。</summary>
    private readonly record struct EnumerationResult(
        long Stable,
        long Legal,
        long Swappable,
        long Chain,
        long Inconsistent);

    private static EnumerationResult Enumerate(
        int layers,
        int parts,
        int alphabetSize,
        int total)
    {
        var document = new ShapeDocument(layers, parts);

        long stable = 0;
        long legal = 0;
        long swappable = 0;
        long chain = 0;
        long inconsistent = 0;

        for (int code = 0; code < total; code++)
        {
            Fill(document, code, alphabetSize);

            if (!ShapeOps.IsStable(document))
            {
                continue;
            }

            stable++;

            if (!ShapeSignature.AllColumnsLegal(document, out _))
            {
                continue;
            }

            legal++;

            bool currentSwappable = Swappable.Test(document);
            bool currentChain = ExchangeChain.TryBuild(document, out _);

            if (currentSwappable)
            {
                swappable++;
            }

            if (currentChain)
            {
                chain++;
            }

            if (currentSwappable != currentChain)
            {
                inconsistent++;
            }
        }

        return new EnumerationResult(
            stable,
            legal,
            swappable,
            chain,
            inconsistent);
    }

    /// <summary>
    /// 把 <paramref name="code"/> 按低位优先填进文档。
    /// 字母表：0 = 空，1 = 普通格，2 = 晶体。
    /// </summary>
    private static void Fill(
        ShapeDocument document,
        long code,
        int alphabetSize)
    {
        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            for (int part = 0; part < document.PartCount; part++)
            {
                int digit = (int)(code % alphabetSize);

                code /= alphabetSize;

                document[layer, part] = digit switch
                {
                    0 => ShapeCell.Empty(),
                    1 => ShapeCell.Shape('C', 'u'),
                    _ => ShapeCell.Crystal('r')
                };
            }
        }
    }

    /// <summary>
    /// 固定种子的 SplitMix64，避免依赖 <see cref="Random"/> 的实现细节，
    /// 保证抽样在任何机器上都可复现。
    /// </summary>
    private sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;

            ulong value = _state;

            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;

            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

            return value ^ (value >> 31);
        }
    }

    #endregion

    #region 语料 6：claw

    /// <summary>
    /// 语料 6（可验证部分）：4 象限 3 层的 claw。
    ///
    /// 规格记录：原图不可交换；拆掉 (L3, 第 3 象限) 的顶针、填塔得到 X，
    /// X 稳定 ✔ 可交换 ✔，顶针(X) == D ✔；再把 H 堆回去得到原图。
    /// </summary>
    public static void ClawIntermediateFacts()
    {
        ShapeDocument claw = Claw();

        Assert.True(ShapeOps.IsStable(claw), "claw 原图应稳定");
        Assert.False(Swappable.Test(claw), "claw 原图不应可交换");

        ShapeDocument x = ClawTowerFill();

        Assert.True(ShapeOps.IsStable(x), "claw 的 X 应稳定");
        Assert.True(Swappable.Test(x), "claw 的 X 应可交换");

        ShapeDocument d = ClawWithoutTopPin();

        Assert.True(
            ShapeOps.Same(ShapePhysics.Pin(x).Document, d),
            "顶针(X) 应等于 D");

        ShapeDocument h = Parse("----P---", 1, 4);

        Assert.True(
            ShapeOps.Same(
                ShapePhysics.Stack(d, h, claw.LayerCount).Document,
                claw),
            "Stack(D, H) 应还原 claw");

        // H 自身必须可制作：交换链能造出这块单顶针。
        Assert.True(
            ExchangeChain.TryBuild(h, out _),
            "H（单枚顶针）应能由交换链制作");

        // 保守实现允许返回「未覆盖」，但不允许写成「不可制作」。
        Assert.NotEqual(
            PipelineOutcome.NotProducible,
            ProductionPipeline.Solve(claw, 3).Outcome,
            "claw 没有不合法角，不能判成不可制作");
    }

    /// <summary>
    /// 语料 6（规格标注 xfail）：完整路径应为 Producible、rank = 1。
    /// 骨架的 TowerFill 只做直接填充，当前只能返回「未覆盖」。
    /// </summary>
    public static void ClawShouldBeProducible()
    {
        ShapeDocument claw = Claw();

        PipelineResult result = ProductionPipeline.Solve(claw, 3);

        Assert.Equal(
            PipelineOutcome.Producible,
            result.Outcome,
            "claw 应可制作（TowerFill 抬升块后填充尚未实现）");

        Assert.True(result.Recipe is not null, "可制作时必须给出 Recipe");
        Assert.Equal(1, result.Recipe!.Rank, "claw 的 rank 应为 1");

        Assert.True(
            Replay.Verify(result.Recipe, claw, out IReadOnlyList<string> errors),
            "claw 的 Recipe 回放失败：" + string.Join(" / ", errors));
    }

    #endregion

    #region 语料 7：二择

    /// <summary>
    /// 语料 7（可验证部分）：4 象限 5 层的二择图形。
    ///
    /// 规格记录：原图与两个单拆都「稳定但不可交换」；两个都拆 ⇒ 不稳定。
    /// </summary>
    public static void TwoChoiceStabilityTable()
    {
        ShapeDocument original = TwoChoice();

        Assert.True(ShapeOps.IsStable(original), "二择原图应稳定");
        Assert.False(Swappable.Test(original), "二择原图不应可交换");

        ShapeDocument stripSecondQuadrant = original.Clone();
        stripSecondQuadrant[4, 1] = ShapeCell.Empty(); // (L5, 第 2 象限) 的 T

        Assert.True(ShapeOps.IsStable(stripSecondQuadrant), "单拆第 2 象限后应稳定");
        Assert.False(Swappable.Test(stripSecondQuadrant), "单拆第 2 象限后仍不可交换");

        ShapeDocument stripFourthQuadrant = original.Clone();
        stripFourthQuadrant[3, 3] = ShapeCell.Empty(); // (L4, 第 4 象限) 的 T

        Assert.True(ShapeOps.IsStable(stripFourthQuadrant), "单拆第 4 象限后应稳定");
        Assert.False(Swappable.Test(stripFourthQuadrant), "单拆第 4 象限后仍不可交换");

        ShapeDocument stripBoth = original.Clone();
        stripBoth[4, 1] = ShapeCell.Empty();
        stripBoth[3, 3] = ShapeCell.Empty();

        Assert.False(ShapeOps.IsStable(stripBoth), "两个都拆后应不稳定");

        // 规格 §5.6.2.2 的下游事实：D 拆完仍不可交换；X 稳定 + 可交换 + 顶针(X)=D。
        ShapeDocument d = Parse(
            "--P-P-P-:CuP---Cu:--P-----:cuP-----:cuCu----",
            5,
            4);

        ShapeDocument x = Parse(
            "--P-crcr:CuP-crCu:cuP-cr--:cuCucr--:----cr--",
            5,
            4);

        Assert.False(Swappable.Test(d), "二择的 D 仍不应可交换");
        Assert.True(ShapeOps.IsStable(x), "二择的 X 应稳定");
        Assert.True(Swappable.Test(x), "二择的 X 应可交换");
        Assert.True(
            ShapeOps.Same(ShapePhysics.Pin(x).Document, d),
            "二择的 顶针(X) 应等于 D");

        // 保守实现允许返回「未覆盖」，但不允许写成「不可制作」。
        Assert.NotEqual(
            PipelineOutcome.NotProducible,
            ProductionPipeline.Solve(original, 5).Outcome,
            "二择没有不合法角，不能判成不可制作");
    }

    /// <summary>
    /// 语料 7（规格标注 xfail）：二择图形应可制作。
    /// 需要 HybridStrips 的多格块 + 二择回溯，当前保守实现只能返回「未覆盖」。
    /// </summary>
    public static void TwoChoiceShouldBeProducible()
    {
        ShapeDocument original = TwoChoice();

        PipelineResult result = ProductionPipeline.Solve(original, 5);

        Assert.Equal(
            PipelineOutcome.Producible,
            result.Outcome,
            "二择图形应可制作（HybridStrips 二择回溯尚未实现）");

        Assert.True(result.Recipe is not null, "可制作时必须给出 Recipe");

        Assert.True(
            Replay.Verify(result.Recipe!, original, out IReadOnlyList<string> errors),
            "二择的 Recipe 回放失败：" + string.Join(" / ", errors));
    }

    #endregion

    #region 语料 8/9：交换链逐格复现

    /// <summary>
    /// 语料 8：n = 6、单层 `CrCgCbCcCmCy`，方向 (12+3)+(4+56)。
    /// 逐格复现规格 §6.2 的全部中间结果。
    /// </summary>
    public static void SixQuadrantExchangeChain()
    {
        ShapeDocument target = Parse("CrCgCbCcCmCy", 1, 6);

        Assert.True(
            ExchangeChain.TryBuild(target, 0, 2, 1, out Recipe? recipe),
            "6 象限样例在方向 (12+3)+(4+56) 上应成功");

        ExchangeDetails details = recipe!.Exchange!;

        var exporter = new ShortCodeExporter();

        Assert.Equal("CuCrCg------", exporter.Export(details.P1.Rotated));
        Assert.Equal("------CbCu--", exporter.Export(details.P2.Rotated));
        Assert.Equal("--CuCc------", exporter.Export(details.P3.Rotated));
        Assert.Equal("------CmCyCu", exporter.Export(details.P4.Rotated));

        Assert.Equal("CuCrCgCbCu--", exporter.Export(details.Left.Raw));
        Assert.Equal("CrCgCbCu--Cu", exporter.Export(details.Left.Result));

        /*
         * 规格 §6.2 的右交换中间串写成 `--CuCbCcCmCu`，与它自己给出的
         * 最终右半边 `Cu--CuCcCmCy`、目标 `CrCgCbCcCmCy` 对不上（Cb 属于左半边，
         * 且尾部的 Cu 位置也不一致）。按 §4 的交换语义逐格推导应为下面的值；
         * 这里以可自洽的交换语义为准。
         */
        Assert.Equal("--CuCcCmCyCu", exporter.Export(details.Right.Raw));
        Assert.Equal("Cu--CuCcCmCy", exporter.Export(details.Right.Result));

        Assert.Equal("CrCgCbCcCmCy", exporter.Export(details.Product));
        Assert.Equal("Cu--CuCu--Cu", exporter.Export(details.Waste!));

        Assert.Equal(0, details.Axis);
        Assert.Equal(2, details.LeftSplit);
        Assert.Equal(1, details.RightSplit);
    }

    /// <summary>
    /// 语料 9：n = 8、单层 `CrCgCbCcCmCyCwCk`，方向 (12+34)+(56+78)。
    /// 逐格复现规格 §6.3 的全部中间结果。
    /// </summary>
    public static void EightQuadrantExchangeChain()
    {
        ShapeDocument target = Parse("CrCgCbCcCmCyCwCk", 1, 8);

        Assert.True(
            ExchangeChain.TryBuild(target, 0, 2, 2, out Recipe? recipe),
            "8 象限样例在方向 (12+34)+(56+78) 上应成功");

        ExchangeDetails details = recipe!.Exchange!;

        var exporter = new ShortCodeExporter();

        Assert.Equal("--CuCrCg--------", exporter.Export(details.P1.Rotated));
        Assert.Equal("--------CbCcCu--", exporter.Export(details.P2.Rotated));
        Assert.Equal("--CuCmCy--------", exporter.Export(details.P3.Rotated));
        Assert.Equal("--------CwCkCu--", exporter.Export(details.P4.Rotated));

        Assert.Equal("--CuCrCgCbCcCu--", exporter.Export(details.Left.Raw));
        Assert.Equal("CrCgCbCcCu----Cu", exporter.Export(details.Left.Result));

        Assert.Equal("--CuCmCyCwCkCu--", exporter.Export(details.Right.Raw));
        Assert.Equal("Cu----CuCmCyCwCk", exporter.Export(details.Right.Result));

        Assert.Equal("CrCgCbCcCmCyCwCk", exporter.Export(details.Product));
        Assert.Equal("Cu----CuCu----Cu", exporter.Export(details.Waste!));

        Assert.Equal(0, details.Axis);
        Assert.Equal(2, details.LeftSplit);
        Assert.Equal(2, details.RightSplit);
    }

    #endregion

    #region 解析器节点链

    /// <summary>
    /// 解析器节点链：pipeline 返回带堆叠的路径时，必须输出
    /// 工序节点 + 图形快照 + 连线 + 目标节点。
    ///
    /// `cr------:Cu------` 是核心（单晶体）可交换、尾部（单普通格）单独堆回的
    /// 最小样例；它的节点链由交换叶 + 堆叠块 + 堆叠 + 目标组成。
    /// </summary>
    public static void ParserEmitsOperationChain()
    {
        ShapeDocument target = Parse("cr------:Cu------", 2, 4);

        ProductionPathResult parsed = ProductionPathParser.Parse(target);

        Assert.True(parsed.IsProducible, parsed.Error ?? "应可制作");
        Assert.Equal(PipelineOutcome.Producible, parsed.Outcome);

        ProductionPath path = parsed.Path!;

        Assert.True(
            path.Nodes.All(node => node.Shape is not null),
            "所有节点都必须带图形快照（含废料）");

        ProductionNode stack = path.Nodes.Single(
            node => node.Kind == ProductionNodeKind.Stack);

        ProductionNode result = path.Nodes.Single(
            node => node.Kind == ProductionNodeKind.Target);

        Assert.True(
            path.Nodes.Any(node => node.Kind == ProductionNodeKind.Block),
            "堆叠路径应包含堆叠块节点");

        Assert.True(
            path.Edges.Any(edge => edge.ToId == stack.Id),
            "堆叠节点必须有入边");

        Assert.True(
            path.Edges.Any(edge =>
                edge.FromId == stack.Id &&
                edge.ToId == result.Id),
            "堆叠节点应连到目标节点");

        Assert.True(
            path.Steps.Any(step => step.Kind == ProductionStepKind.Stack),
            "线性工序里应有堆叠步骤");

        Assert.True(
            path.Steps.Any(step => step.Kind == ProductionStepKind.Target),
            "线性工序里应有目标步骤");

        PipelineResult solved = ProductionPipeline.Solve(
            target,
            target.LayerCount);

        Assert.True(
            Replay.Verify(
                solved.Recipe!,
                target,
                out IReadOnlyList<string> errors),
            "节点链对应的 Recipe 回放失败：" +
            string.Join(" / ", errors));
    }

    /// <summary>
    /// 结论分级：不合法角 ⇒ 不可制作；n = 2 尚未覆盖 ⇒ 未覆盖。
    /// 这两种否定必须能被调用方区分开。
    /// </summary>
    public static void ParserClassifiesOutcomes()
    {
        // 第 1 层为空、第 2 层是晶体：五项条件四失败（`.c`）。
        var illegal = new ShapeDocument(2, 4);
        illegal[1, 0] = ShapeCell.Crystal('r');

        ProductionPathResult notProducible = ProductionPathParser.Parse(illegal);

        Assert.False(notProducible.IsProducible);
        Assert.Equal(PipelineOutcome.NotProducible, notProducible.Outcome);
        Assert.True(notProducible.Path is null);
        Assert.True(
            notProducible.Error is not null &&
            notProducible.Error.Contains("不合法"),
            "不可制作必须给出不合法角原因");

        // n = 2 的孤立列拼装还没接进 pipeline：只能是未覆盖。
        var twoQuadrants = new ShapeDocument(1, 2);
        twoQuadrants[0, 0] = ShapeCell.Crystal('r');
        twoQuadrants[0, 1] = ShapeCell.Crystal('g');

        ProductionPathResult uncovered = ProductionPathParser.Parse(twoQuadrants);

        Assert.False(uncovered.IsProducible);
        Assert.Equal(PipelineOutcome.Uncovered, uncovered.Outcome);
        Assert.True(
            uncovered.Error is not null &&
            uncovered.Error.Contains("未覆盖"),
            "未覆盖必须明确标注");
    }

    #endregion

    #region 语料构造

    /// <summary>
    /// claw：4 象限 3 层。
    /// <code>
    /// L3  c T P T
    /// L2  P . P .
    /// L1  P . P .
    /// </code>
    /// </summary>
    private static ShapeDocument Claw()
    {
        return Parse("P---P---:P---P---:crCuP-Cu", 3, 4);
    }

    /// <summary>
    /// 规格 §5.6.2.1 的 X：填塔后的前驱。
    /// <code>
    /// L3  . . c c
    /// L2  c T c T
    /// L1  P . P .
    /// </code>
    /// </summary>
    private static ShapeDocument ClawTowerFill()
    {
        return Parse("P---P---:crCucrCu:----crcr", 3, 4);
    }

    /// <summary>D = claw 去掉 (L3, 第 3 象限) 的顶针。</summary>
    private static ShapeDocument ClawWithoutTopPin()
    {
        ShapeDocument d = Claw();

        d[2, 2] = ShapeCell.Empty();

        return d;
    }

    /// <summary>
    /// 二择：4 象限 5 层，短代码按底层到顶层书写。
    /// </summary>
    private static ShapeDocument TwoChoice()
    {
        return Parse(
            "--P-P-P-:CuP---Cu:--P-CuCu:cuP---Cu:cuCu----",
            5,
            4);
    }

    private static ShapeDocument Parse(
        string code,
        int layers,
        int parts)
    {
        var parser = new ShortCodeParser();

        ParseResult parsed = parser.Parse(code, layers, parts);

        Assert.True(parsed.Success, parsed.Error ?? "短代码解析失败");

        return parsed.Document!;
    }

    #endregion
}
