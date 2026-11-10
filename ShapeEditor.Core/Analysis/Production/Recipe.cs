using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 制作路径里的一步。
///
/// 路径是一棵树：每个节点是一个工序，输入是子节点。
/// 节点自带产物快照，UI 可以直接按它画节点链。
/// </summary>
public sealed class Recipe
{
    private Recipe(
        RecipeKind kind,
        ShapeDocument shape,
        IReadOnlyList<Recipe> inputs,
        ShapeDocument? block,
        Recipe? blockRecipe,
        IReadOnlyList<RecipeStep> steps,
        int rank,
        ExchangeDetails? exchange,
        HalfAssemblyDetails? halfAssembly)
    {
        Kind = kind;
        Shape = shape;
        Inputs = inputs;
        Block = block;
        BlockRecipe = blockRecipe;
        Steps = steps;
        Rank = rank;
        Exchange = exchange;
        HalfAssembly = halfAssembly;
    }

    public RecipeKind Kind { get; }

    /// <summary>该工序的产物。</summary>
    public ShapeDocument Shape { get; }

    /// <summary>上游节点。交换是叶子，没有输入。</summary>
    public IReadOnlyList<Recipe> Inputs { get; }

    /// <summary>堆叠时压上去的那一块（hybrid）。</summary>
    public ShapeDocument? Block { get; }

    /// <summary>那块 hybrid 自己的制作路径；保守实现里可能为 null。</summary>
    public Recipe? BlockRecipe { get; }

    /// <summary>过程快照，供 UI 展开显示。</summary>
    public IReadOnlyList<RecipeStep> Steps { get; }

    /// <summary>这条路径里顶针的次数。</summary>
    public int Rank { get; }

    /// <summary>
    /// 交换叶子的完整构造明细（四个带柱半半边、两次交换、末次交换）。
    /// 只有 <see cref="RecipeKind.Exchange"/> 节点会带；解析器据此画节点图。
    /// </summary>
    public ExchangeDetails? Exchange { get; }

    /// <summary>
    /// k 列半边装配的完整明细（两个半边各自的 k 列半边工序 + 两条真实输入 + 一次交换）。
    /// 只有 <see cref="RecipeKind.HalfAssembly"/> 节点会带。
    /// </summary>
    public HalfAssemblyDetails? HalfAssembly { get; }

    public static Recipe CreateExchange(
        ShapeDocument shape,
        IReadOnlyList<RecipeStep> steps,
        ExchangeDetails? exchange = null)
    {
        return new Recipe(
            RecipeKind.Exchange,
            shape,
            Array.Empty<Recipe>(),
            null,
            null,
            steps,
            0,
            exchange,
            null);
    }

    /// <summary>
    /// k 列半边装配（落地清单第 5 条）：两个半边各自用「k 列半边工序」做出来，
    /// 再送进交换机合并成整图。没有顶针，所以 <c>Rank = 0</c>。
    /// </summary>
    public static Recipe CreateHalfAssembly(
        ShapeDocument shape,
        IReadOnlyList<RecipeStep> steps,
        HalfAssemblyDetails details)
    {
        return new Recipe(
            RecipeKind.HalfAssembly,
            shape,
            Array.Empty<Recipe>(),
            null,
            null,
            steps,
            0,
            null,
            details);
    }

    public static Recipe Pin(
        Recipe input,
        ShapeDocument result)
    {
        return new Recipe(
            RecipeKind.Pin,
            result,
            new[] { input },
            null,
            null,
            Array.Empty<RecipeStep>(),
            input.Rank + 1,
            null,
            null);
    }

    public static Recipe Stack(
        Recipe input,
        ShapeDocument block,
        ShapeDocument result,
        Recipe? blockRecipe = null)
    {
        return new Recipe(
            RecipeKind.Stack,
            result,
            new[] { input },
            block,
            blockRecipe,
            Array.Empty<RecipeStep>(),
            input.Rank,
            null,
            null);
    }

    /// <summary>整棵树的节点数，便于 UI 与统计。</summary>
    public int CountNodes()
    {
        int total = 1;

        foreach (Recipe input in Inputs)
        {
            total += input.CountNodes();
        }

        if (BlockRecipe is not null)
        {
            total += BlockRecipe.CountNodes();
        }

        return total;
    }
}

/// <summary>工序类型。</summary>
public enum RecipeKind
{
    /// <summary>交换链组装（rank 0 终点，叶子）。</summary>
    Exchange,

    /// <summary>先造输入，再顶针一次。</summary>
    Pin,

    /// <summary>先造输入，再把一块 hybrid 堆上去。</summary>
    Stack,

    /// <summary>
    /// k 列半边装配（落地清单第 5 条）：两个半边各自按半边工序做好，
    /// 再送进交换机合并。交换链做不出来时才使用（见 <see cref="HalfAssembly"/>）。
    /// </summary>
    HalfAssembly
}

/// <summary>过程快照：给 UI 显示「这一步做完了长什么样」。</summary>
public sealed record RecipeStep(
    string Title,
    ShapeDocument Shape,
    bool IsWaste = false);

/// <summary>
/// 一个带柱子的半半边：<see cref="Raw"/> 是刚构造出来的样子，
/// <see cref="Rotated"/> 是做过规格 §4.2 预旋转后、真正送入交换机的样子。
/// </summary>
public sealed record ExchangePiece(
    ShapeDocument Raw,
    ShapeDocument Rotated);

/// <summary>
/// 一次同侧交换的两路输出：<see cref="Raw"/> 是交换机出来的所需半边（转回前），
/// <see cref="Result"/> 是转回后的半边，<see cref="Waste"/> 是另一路输出。
/// </summary>
public sealed record ExchangeHalf(
    ShapeDocument Raw,
    ShapeDocument Result,
    ShapeDocument? Waste);

/// <summary>
/// 交换链的完整中间结果，供节点图逐格复现（规格 §6.2 / §6.3）。
/// </summary>
public sealed record ExchangeDetails(
    int Axis,
    int LeftSplit,
    int RightSplit,
    ExchangePiece P1,
    ExchangePiece P2,
    ExchangePiece P3,
    ExchangePiece P4,
    ExchangeHalf Left,
    ExchangeHalf Right,
    ShapeDocument Product,
    ShapeDocument? Waste);

/// <summary>
/// k 列半边装配的明细（落地清单第 5 条）。
///
/// <para>
/// <see cref="LeftInput"/> / <see cref="RightInput"/> 是真正送进交换机的两个图形：
/// 它们各自是「半球工序的前态」，旋转 <c>−Axis</c> 后半边落在固定刀口的一侧。
/// 真实物理回放：<c>Cut(LeftInput).West</c> / <c>Cut(RightInput).East</c> 分别等于
/// 目标的左 / 右半边，<c>Exchange(LeftInput, RightInput)</c> 的所需输出转回后等于目标。
/// </para>
/// </summary>
public sealed record HalfAssemblyDetails(
    int Axis,
    int HalfWidth,
    HalfWorkOrder Left,
    HalfWorkOrder Right,
    Recipe LeftRecipe,
    Recipe RightRecipe,
    ShapeDocument LeftInput,
    ShapeDocument RightInput,
    ShapeDocument LeftHalf,
    ShapeDocument RightHalf,
    ShapeDocument Product,
    ShapeDocument? Waste);
