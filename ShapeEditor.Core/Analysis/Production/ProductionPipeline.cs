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
/// n 分档按规格 §5：
/// n = 2 走定理 5 的孤立列拼装（算法 7 + 交换）；
/// n = 4 的交换链按定理 6 / 算法 8 取前态（见 <see cref="HalfObtainability"/>）；
/// n ≥ 6 交换链直接组装。
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

        // n = 2：定理 5 的孤立列拼装（规格 §5.2）。
        // 每个半边只有一列，没有交换切点，不能走交换链。
        if (target.PartCount == 2)
        {
            return SolveTwoQuadrants(target, notes);
        }

        SplitAtHighestCrystal(
            target,
            out ShapeDocument core,
            out ShapeDocument? tail,
            out string splitNote);

        notes.Add(splitNote);
        notes.Add($"cap = {cap}；目标文档 {target.LayerCount} 层；核心沿用 {core.LayerCount} 层。");

        /*
         * 没有尾部（最高晶体已在顶层）时，先真实算一遍交换链，再决定用哪条配方。
         *
         * 交换链的末次交换输入是「半边 + 另一侧的辅助柱」，所以另一路输出（废料）
         * 是辅助柱拼成的「全普通形状」。这确实是那次 Exchange 的真实副产物，
         * 但当目标有一半只占最底层（例如 q3 = q4 = `c.......`）时，废料会在
         * 目标本来空着的格子上显示满柱，和用户看到的半边对不上。
         *
         * 判据（只影响「交换链能做、半边装配也能做」的目标）：
         *   1. 两半都能由 BuildHalf 得到（HalfAssembly.TryBuild 成功）；
         *   2. 目标有一半只占最底层（HasFlatHalf；上一轮的「整半全空」是它的特例）；
         *   3. 交换链废料里确实有目标空位上的材料（WasteOutsideTarget > 0）。
         * 满足时改走半边装配：两条输入就是目标的两个半边本身，
         * 废料 = ShapeExchange.Exchange(LeftInput, RightInput) 的第一路输出，
         * 也就是「被舍弃的另一半」（逐格为空），不是辅助柱占位。
         * 见 TODO.md §7.33。
         */
        bool chainAttempted = false;

        if (tail is null &&
            HasFlatHalf(core) &&
            HasEmptyCell(core))
        {
            chainAttempted = true;

            if (ExchangeChain.TryBuild(core, out Recipe? chainRecipe) &&
                chainRecipe is not null)
            {
                int wasteOutside = CountWasteOutsideTarget(chainRecipe, core);

                if (wasteOutside > 0 &&
                    HalfAssembly.TryBuild(
                        core,
                        core.LayerCount,
                        out Recipe? halfRecipe,
                        out _) &&
                    halfRecipe is not null)
                {
                    HalfAssemblyDetails details = halfRecipe.HalfAssembly!;

                    notes.Add(
                        "交换链的废料在目标空位上有 " + wasteOutside +
                        " 格辅助柱（末次交换的输入带辅助柱）；改走半边装配：" +
                        $"第 {details.Axis} 条轴的两个 {details.HalfWidth} 列半边都可得" +
                        $"（左 {details.Left.Candidate} / 右 {details.Right.Candidate}），" +
                        "废料 = Exchange(左半边, 右半边) 的真实另一路输出。");
                    notes.Add(
                        $"rank = {halfRecipe.Rank}，节点数 = {halfRecipe.CountNodes()}。");

                    return PipelineResult.Producible(halfRecipe, notes);
                }

                // 不满足上面的判据：交换链就是正确答案，直接返回，避免重算一次。
                notes.Add("rank 0：交换链直接组装成功。");
                notes.Add(
                    $"rank = {chainRecipe.Rank}，节点数 = {chainRecipe.CountNodes()}。");

                return PipelineResult.Producible(chainRecipe, notes);
            }
        }

        PipelineResult coreResult =
            SolveCore(core, maxRank, notes, skipChain: chainAttempted);

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
    /// n = 2 的孤立列拼装（规格 §5.2、定理 5、算法 7）：
    /// 左右两列分别通过定理 4（孤立列判据）⇒ 整图可分；
    /// 分别做成孤立列，再送入交换拼合。
    ///
    /// 失败时返回「未覆盖」而不是「不可制作」：唯一的否定判据仍然只有不合法角。
    /// </summary>
    private static PipelineResult SolveTwoQuadrants(
        ShapeDocument target,
        List<string> notes)
    {
        bool left = IsolatedColumn.IsIsolatedColumn(target, 0);
        bool right = IsolatedColumn.IsIsolatedColumn(target, 1);

        notes.Add(
            $"n = 2：定理 5——左列通过孤立列判据？{left}；" +
            $"右列通过？{right}。");

        if (!left || !right)
        {
            notes.Add("有列不满足定理 4，未覆盖（不是不可制作）。");
            return PipelineResult.Uncovered(notes);
        }

        ShapeDocument leftHalf = ShapeOps.Project(target, 0, 1);
        ShapeDocument rightHalf = ShapeOps.Project(target, 1, 1);

        var steps = new List<RecipeStep>
        {
            new("制作左孤立列（算法 7）", leftHalf),
            new("制作右孤立列（算法 7）", rightHalf),
            new("交换得到目标图形", target)
        };

        notes.Add("n = 2：两列按算法 7 做成孤立列后直接交换拼合。");

        return PipelineResult.Producible(
            Recipe.CreateExchange(target, steps),
            notes);
    }

    /// <summary>
    /// 目标是否有一整半（连续 n/2 列）只占最底层：第 2 层及以上的格子全空。
    ///
    /// <para>
    /// 这是上一轮「一整半全空」的直接推广：用户反馈的两个反例里，
    /// 第 3、4 象限分别是「全空」与「只有底层的两颗晶体」，
    /// 视觉上都是「这一半没有高度」。交换链在这种目标上会用一根满高辅助柱
    /// 顶替这一半的位置，废料因此显示成全是普通形状；半边装配把两个半边
    /// 真正送进 <see cref="ShapeExchange.Exchange"/>，废料才是真实副产物。
    /// </para>
    /// <para>
    /// 只有 n ≥ 4 才有「两半」可言；n = 2 已经走定理 5 的孤立列拼装。
    /// </para>
    /// </summary>
    private static bool HasFlatHalf(
        ShapeDocument shape)
    {
        int parts = shape.PartCount;
        int half = parts / 2;

        if (parts < 4 || half <= 0)
        {
            return false;
        }

        for (int side = 0; side < 2; side++)
        {
            bool flat = true;

            for (int offset = 0; offset < half && flat; offset++)
            {
                int part = side == 0 ? offset : half + offset;

                // 第 1 层（索引 0）允许有内容：底层的晶体 / 普通格都算「贴地」。
                for (int layer = 1; layer < shape.LayerCount; layer++)
                {
                    if (shape[layer, part].Type != CellType.Empty)
                    {
                        flat = false;
                        break;
                    }
                }
            }

            if (flat)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 目标是否有空位。没有空位时交换链废料不可能「越出目标」，
    /// 不必为了废料再算一遍交换链（n = 20 的性能用例会走这条短路）。
    /// </summary>
    private static bool HasEmptyCell(ShapeDocument shape)
    {
        for (int layer = 0;
             layer < shape.LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < shape.PartCount;
                 part++)
            {
                if (shape[layer, part].Type == CellType.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 交换链废料里「目标本来空、废料却非空」的格子数。
    ///
    /// <para>
    /// 这些格子就是辅助柱泄漏到目标空位上的部分，也是用户看到的
    /// 「废料全都是普通形状」的来源。只有 &gt; 0 时才值得改走半边装配；
    /// 目标本身铺满时废料即使全是柱子也不越界，保持交换链即可。
    /// </para>
    /// </summary>
    private static int CountWasteOutsideTarget(
        Recipe recipe,
        ShapeDocument target)
    {
        ShapeDocument? waste = null;

        foreach (RecipeStep step in recipe.Steps)
        {
            if (step.IsWaste)
            {
                waste = step.Shape;
            }
        }

        if (waste is null)
        {
            return 0;
        }

        int layers = Math.Max(waste.LayerCount, target.LayerCount);
        int parts = Math.Max(waste.PartCount, target.PartCount);
        int count = 0;

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                bool inWaste = layer < waste.LayerCount &&
                               part < waste.PartCount &&
                               waste[layer, part].Type != CellType.Empty;

                bool inTarget = layer < target.LayerCount &&
                                part < target.PartCount &&
                                target[layer, part].Type != CellType.Empty;

                if (inWaste && !inTarget)
                {
                    count++;
                }
            }
        }

        return count;
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
    ///
    /// <paramref name="skipChain"/> 只用于 <see cref="Solve"/> 已经试过交换链
    /// 且失败的情形，避免同一条链重算两遍（n 大、链失败时开销很高）。
    /// 递归的顶针逆 / 拆 hybrid 仍然各自照常试交换链。
    /// </summary>
    private static PipelineResult SolveCore(
        ShapeDocument shape,
        int rankBudget,
        List<string> notes,
        bool skipChain = false)
    {
        if (!skipChain &&
            ExchangeChain.TryBuild(shape, out Recipe? exchange))
        {
            notes.Add($"rank {0}：交换链直接组装成功。");
            return PipelineResult.Producible(
                exchange!,
                notes);
        }

        /*
         * 落地清单第 5 条：交换链失败 ⇒ 用两条轴上的两个 k 列半边装配整图。
         * 每一步都是真实 Cut / Exchange 回放（见 HalfAssembly.Verify），
         * 只靠判据不算成功。
         */
        if (HalfAssembly.TryBuild(
                shape,
                shape.LayerCount,
                out Recipe? halfAssembly,
                out string halfNote))
        {
            notes.Add(halfNote);
            return PipelineResult.Producible(
                halfAssembly!,
                notes);
        }

        notes.Add(halfNote);

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
