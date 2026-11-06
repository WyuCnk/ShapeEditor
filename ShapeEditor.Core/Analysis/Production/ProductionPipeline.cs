using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>制作路径求解的结论。</summary>
public enum PipelineOutcome
{
    /// <summary>找到路径。</summary>
    Producible,

    /// <summary>存在不合法角——唯一的否定判据。</summary>
    NotProducible,

    /// <summary>没找到路径，但也没证明不可能。</summary>
    Uncovered
}

/// <summary>求解结果。</summary>
public sealed record PipelineResult(
    PipelineOutcome Outcome,
    Recipe? Recipe,
    IReadOnlyList<string> Notes)
{
    public static PipelineResult Producible(
        Recipe recipe,
        List<string> notes)
    {
        return new PipelineResult(
            PipelineOutcome.Producible,
            recipe,
            notes);
    }

    public static PipelineResult NotProducible(
        string reason)
    {
        return new PipelineResult(
            PipelineOutcome.NotProducible,
            null,
            new[] { reason });
    }

    public static PipelineResult Uncovered(
        List<string> notes)
    {
        return new PipelineResult(
            PipelineOutcome.Uncovered,
            null,
            notes);
    }
}

/// <summary>
/// 制作路径框架（规格 §5.6.3）。
///
/// <code>
/// Stage 0  前置检查：每列过五项条件（不过 ⇒ 不可制作）
/// Stage 1  分层：按最高晶体拆成 A（尾部）与 B（核心）
/// Stage 2  求解核心 B：SolveCore(B, rank ≤ 3)
/// Stage 3  组装：把 A 堆回去
/// Stage 4  回放校验（见 Replay）
/// </code>
///
/// <see cref="TowerFill"/> 与 <see cref="HybridStrips"/> 目前是**保守实现**：
/// 只做「直接填充」与「拆单个顶针」，其余情形返回空集合，
/// 于是那些情形会落到「未覆盖」而不是误判。
/// </summary>
public static class ProductionPipeline
{
    /// <summary>默认的顶针次数上限。</summary>
    public const int DefaultMaxRank = 3;

    public static PipelineResult Solve(
        ShapeDocument target,
        int cap,
        int maxRank = DefaultMaxRank)
    {
        var notes = new List<string>();

        if (target.PartCount % 2 != 0)
        {
            return PipelineResult.NotProducible("象限数必须为偶数。");
        }

        if (cap < target.LayerCount)
        {
            // 目标本身就超过机器上限，属于「未覆盖」而不是「不可制作」：
            // 唯一的不可制作判据仍然只有不合法角。
            return PipelineResult.Uncovered(
                new List<string>
                {
                    $"目标高度 {target.LayerCount} 超过 cap {cap}（未覆盖）。"
                });
        }

        if (!ShapeSignature.AllColumnsLegal(
                target,
                out int badColumn))
        {
            return PipelineResult.NotProducible(
                $"第 {badColumn + 1} 象限不合法。");
        }

        SplitAtHighestCrystal(
            target,
            out ShapeDocument core,
            out ShapeDocument? tail,
            out string splitNote);

        notes.Add(splitNote);
        notes.Add($"cap = {cap}；目标文档 {target.LayerCount} 层；核心沿用 {core.LayerCount} 层。");

        PipelineResult coreResult =
            SolveCore(core, maxRank, notes);

        if (coreResult.Outcome != PipelineOutcome.Producible)
        {
            return new PipelineResult(
                coreResult.Outcome,
                null,
                notes);
        }

        Recipe result = coreResult.Recipe!;

        if (tail is not null)
        {
            ShapeDocument? stacked = ShapePhysics
                .Stack(core, tail, target.LayerCount)
                .Document;

            if (stacked is null ||
                !ShapeOps.Same(stacked, target))
            {
                // 分层方式该图不适用：退回整体求解
                notes.Add("尾部堆叠无法还原原图，改为整体求解。");

                var direct = new List<string>();
                PipelineResult whole = SolveCore(target, maxRank, direct);
                direct.ForEach(notes.Add);

                return whole;
            }

            result = Recipe.Stack(result, tail, target);
        }

        notes.Add($"rank = {result.Rank}，节点数 = {result.CountNodes()}。");

        return PipelineResult.Producible(result, notes);
    }

    /// <summary>
    /// 按最高晶体把图形拆成「核心 B」和「尾部 A」。
    /// </summary>
    private static void SplitAtHighestCrystal(
        ShapeDocument target,
        out ShapeDocument core,
        out ShapeDocument? tail,
        out string note)
    {
        int highest = -1;

        for (int layer = 0;
             layer < target.LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < target.PartCount;
                 part++)
            {
                if (target[layer, part].Type == CellType.Crystal)
                {
                    highest = layer;
                }
            }
        }

        if (highest < 0 || highest == target.LayerCount - 1)
        {
            core = target;
            tail = null;
            note = highest < 0
                ? "无晶体，整体求解。"
                : "最高晶体在顶层，没有尾部。";

            return;
        }

        int coreLayers = highest + 1;
        int tailLayers = target.LayerCount - coreLayers;

        core = ShapeOps.Project(target, 0, target.PartCount);

        for (int layer = coreLayers;
             layer < target.LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < target.PartCount;
                 part++)
            {
                core[layer, part] = ShapeCell.Empty();
            }
        }

        // 核心保留 cap 层数（上面的尾部层清空但不截断）：
        // 规格 §0.2 要求 Pin 的上限是 cap，而不是内容高度。
        var builtTail = new ShapeDocument(
            tailLayers,
            target.PartCount);

        for (int layer = 0;
             layer < tailLayers;
             layer++)
        {
            for (int part = 0;
                 part < target.PartCount;
                 part++)
            {
                builtTail[layer, part] =
                    target[
                        coreLayers + layer,
                        part].Clone();
            }
        }

        tail = builtTail;

        note = $"按最高晶体（第 {highest + 1} 层）拆分：" +
               $"核心内容 {coreLayers} 层，尾部 {tailLayers} 层；" +
               $"核心保留 cap 的 {target.LayerCount} 层。";
    }

    /// <summary>
    /// 核心求解：可交换 → 顶针逆 → 拆 hybrid。
    /// </summary>
    private static PipelineResult SolveCore(
        ShapeDocument shape,
        int rankBudget,
        List<string> notes)
    {
        if (ExchangeChain.TryBuild(shape, out Recipe? exchange))
        {
            notes.Add($"rank {0}：交换链直接组装成功。");
            return PipelineResult.Producible(
                exchange!,
                notes);
        }

        if (rankBudget > 0)
        {
            foreach (ShapeDocument candidate in TowerFill(shape))
            {
                if (!ShapeSignature.AllColumnsLegal(candidate, out _))
                {
                    continue;
                }

                if (!ShapeOps.IsStable(candidate))
                {
                    continue;
                }

                ShapeDocument? pinned = ShapePhysics
                    .Pin(candidate)
                    .Document;

                if (pinned is null ||
                    !ShapeOps.Same(pinned, shape))
                {
                    continue;
                }

                var innerNotes = new List<string>();
                PipelineResult inner = SolveCore(
                    candidate,
                    rankBudget - 1,
                    innerNotes);

                if (inner.Outcome == PipelineOutcome.Producible)
                {
                    notes.AddRange(innerNotes);
                    notes.Add($"rank {rankBudget}：填水晶塔 + 顶针。");

                    return PipelineResult.Producible(
                        Recipe.Pin(inner.Recipe!, shape),
                        notes);
                }
            }
        }

        foreach ((ShapeDocument reduced,
                  ShapeDocument block,
                  Recipe blockRecipe)
                 in HybridStrips(shape))
        {
            ShapeDocument? stacked = ShapePhysics
                .Stack(reduced, block, shape.LayerCount)
                .Document;

            if (stacked is null ||
                !ShapeOps.Same(stacked, shape))
            {
                continue;
            }

            var innerNotes = new List<string>();
            PipelineResult inner = SolveCore(
                reduced,
                rankBudget,
                innerNotes);

            if (inner.Outcome == PipelineOutcome.Producible)
            {
                notes.AddRange(innerNotes);
                notes.Add("拆 hybrid 后堆叠还原。");

                return PipelineResult.Producible(
                    Recipe.Stack(
                        inner.Recipe!,
                        block,
                        shape,
                        blockRecipe),
                    notes);
            }
        }

        return PipelineResult.Uncovered(notes);
    }

    /// <summary>
    /// 顶针逆的前驱候选（保守版：只做「直接填充」，不实现「抬升块后填充」）。
    ///
    /// 候选图形保持 <see cref="ShapeDocument.LayerCount"/> 层不变；对核心子问题而言
    /// 这个层数就是规格 §0.2 的 cap（Stage 1 不再把核心截到内容高度）。
    /// </summary>
    private static IEnumerable<ShapeDocument> TowerFill(
        ShapeDocument shape)
    {
        int layers = shape.LayerCount;
        int parts = shape.PartCount;

        if (layers < 2)
        {
            yield break;
        }

        // 收据行：Y[0] 只能是 P 或空，且 Y[0] 是 P ⟺ Y[1] 非空
        for (int part = 0;
             part < parts;
             part++)
        {
            bool isPin = shape[0, part].Type == CellType.Pin;
            bool nonEmptyAbove =
                shape[1, part].Type != CellType.Empty;

            if (isPin != nonEmptyAbove)
            {
                yield break;
            }
        }

        var plain = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers - 1;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                plain[layer, part] =
                    shape[layer + 1, part].Clone();
            }
        }

        yield return plain;

        // 变体：顶层铺满晶体（最大填充）
        ShapeDocument filled = plain.Clone();

        for (int part = 0;
             part < parts;
             part++)
        {
            filled[layers - 1, part] =
                ShapeCell.Crystal('r');
        }

        yield return filled;
    }

    /// <summary>
    /// 拆 hybrid 的候选（保守版：只拆最顶层的单枚顶针）。
    ///
    /// 契约要求 <c>H</c> 自身可制作，所以这里用交换链给这块顶针做一份
    /// <see cref="Recipe"/>；做不出来就不枚举，保持「未覆盖」而不是误判。
    /// 多格 / 多层的块与二择回溯仍未实现，见 TODO.md。
    /// </summary>
    private static IEnumerable<(ShapeDocument Reduced,
                                 ShapeDocument Block,
                                 Recipe BlockRecipe)>
        HybridStrips(ShapeDocument shape)
    {
        int top = shape.LayerCount - 1;

        // 核心可能保留 cap 的空顶层：要剥的是最高的非空层。
        while (top >= 1 && shape.IsLayerEmpty(top))
        {
            top--;
        }

        if (top < 1)
        {
            yield break;
        }

        for (int part = 0;
             part < shape.PartCount;
             part++)
        {
            // 保守实现：只把「单枚顶针」当作可拆的 hybrid。
            if (shape[top, part].Type != CellType.Pin)
            {
                continue;
            }

            var block = new ShapeDocument(
                1,
                shape.PartCount);

            block[0, part] = shape[top, part].Clone();

            if (!ExchangeChain.TryBuild(block, out Recipe? blockRecipe) ||
                blockRecipe is null)
            {
                continue;
            }

            ShapeDocument reduced = shape.Clone();
            reduced[top, part] = ShapeCell.Empty();

            yield return (reduced, block, blockRecipe);
        }
    }
}
