using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 回放校验：不用相信路径的构造过程，直接从叶子往上重算一遍。
///
/// - 交换叶子：重新跑一次交换链；
/// - 顶针节点：对输入的产物执行 <see cref="ShapePhysics.Pin"/>；
/// - 堆叠节点：对输入的产物执行 <see cref="ShapePhysics.Stack"/>。
/// </summary>
public static class Replay
{
    public static bool Verify(
        Recipe recipe,
        ShapeDocument? expected,
        out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();

        VerifyNode(recipe, collected);

        if (expected is not null &&
            !ShapeOps.Same(recipe.Shape, expected))
        {
            collected.Add("最终产物与目标不一致。");
        }

        errors = collected;
        return collected.Count == 0;
    }

    private static void VerifyNode(
        Recipe recipe,
        List<string> errors)
    {
        switch (recipe.Kind)
        {
            case RecipeKind.Exchange:
            {
                if (!ExchangeChain.TryBuild(
                        recipe.Shape,
                        out Recipe? rebuilt) ||
                    !ShapeOps.Same(
                        rebuilt!.Shape,
                        recipe.Shape))
                {
                    errors.Add("交换链回放失败。");
                }

                break;
            }

            case RecipeKind.Pin:
            {
                if (recipe.Inputs.Count != 1)
                {
                    errors.Add("顶针节点输入数量错误。");
                    break;
                }

                VerifyNode(recipe.Inputs[0], errors);

                ShapeDocument? pinned = ShapePhysics
                    .Pin(recipe.Inputs[0].Shape)
                    .Document;

                if (!ShapeOps.Same(pinned, recipe.Shape))
                {
                    errors.Add("顶针回放失败。");
                }

                break;
            }

            case RecipeKind.Stack:
            {
                if (recipe.Inputs.Count != 1 ||
                    recipe.Block is null)
                {
                    errors.Add("堆叠节点输入数量错误。");
                    break;
                }

                VerifyNode(recipe.Inputs[0], errors);

                ShapeDocument? stacked = ShapePhysics
                    .Stack(
                        recipe.Inputs[0].Shape,
                        recipe.Block,
                        recipe.Shape.LayerCount)
                    .Document;

                if (!ShapeOps.Same(stacked, recipe.Shape))
                {
                    errors.Add("堆叠回放失败。");
                }

                break;
            }

            default:
                errors.Add("未知的工序类型。");
                break;
        }
    }

    /// <summary>
    /// 把路径摊平成「步骤名 + 图形」的线性列表，供 UI 画节点链。
    /// </summary>
    public static IReadOnlyList<RecipeStep> Flatten(
        Recipe recipe)
    {
        var steps = new List<RecipeStep>();

        FlattenInto(recipe, steps);

        return steps;
    }

    private static void FlattenInto(
        Recipe recipe,
        List<RecipeStep> steps)
    {
        foreach (Recipe input in recipe.Inputs)
        {
            FlattenInto(input, steps);
        }

        switch (recipe.Kind)
        {
            case RecipeKind.Exchange:
                steps.AddRange(recipe.Steps);
                break;

            case RecipeKind.Pin:
                steps.Add(new RecipeStep(
                    "顶针",
                    recipe.Shape));
                break;

            case RecipeKind.Stack:
                steps.Add(new RecipeStep(
                    "堆叠",
                    recipe.Shape));
                break;
        }
    }
}
