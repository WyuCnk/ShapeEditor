using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 卡片构建：把现有的工序数据投影成「角卡 → 半边卡 → 交换卡」三层
/// （docs/work-order-card-hierarchy-plan.md §3 / §3.5）。
///
/// <para><b>不改求解器</b></para>
/// <para>
/// 只读地使用 <see cref="AngleWorkOrderBuilder.Build"/>（角工序）、
/// <see cref="AngleWorkOrderBuilder.BuildHalf"/>（半边工序）与
/// <see cref="ShapeExchange.Exchange"/> / <see cref="ShapePhysics"/>（真实物理），
/// <see cref="Recipe"/> / <see cref="ProductionGraph"/> 一行不动。
/// </para>
///
/// <para><b>分组规则</b>（用户裁定 2026-11-20：碎晶是对整个图形的操作）</para>
/// <list type="number">
/// <item><b>角卡</b>：只给<b>非空角</b>画卡，产物固定 Q1（右柱 Q2、左柱 Qn），
/// <c>Inputs</c> 恒为空（角是叶子卡）。
/// <b>属于需要碎晶的半边</b>时，产物 = 该列在<b>半边前态</b>里的紧列 + 左右双柱，
/// <b>可见步骤里没有碎晶</b>（碎晶归半边卡）；独立角保持原样（紧列 → 碎晶 / 顶破 → 目标角）。</item>
/// <item><b>半边卡</b>：恒为 2 张（左半边 / 右半边，k = n/2 列）。
/// 需要碎晶时走 <see cref="HalfMode.FromTightColumns"/>：<c>Inputs</c> = 该半边各列的紧列
/// （角卡产物），卡内按真实 <c>Cut</c> 步数显示 1..N 个「碎晶」步，输出 = 目标半边。
/// 不需要碎晶时先试 A 类 <see cref="HalfMode.FromCorners"/>（角产物真实交换），
/// 不成立再用 <see cref="HalfMode.WholeHalf"/>（候选 1 / 2，Inputs 为空，前态卡内私有）。</item>
/// <item><b>交换卡</b>：<c>Inputs</c> = 两张半边卡的输出，卡内是真实两次切割 + 拼接的
/// <c>交换</c>；配方带顶针 / 堆叠收尾时再追加可见的「顶破」/「堆叠」步。</item>
/// </list>
///
/// <para><b>失败降级</b></para>
/// <para>
/// 每张卡最后都过一遍 <see cref="CardReplay.Verify"/>：任何一步回放不逐格相等，
/// 或 <c>Inputs → Steps → Output</c> 走不通，就 <c>Verified = false</c>、
/// <b>清空 Inputs</b>（不画未验证的输入链），并在 <c>Note</c> 里写清卡在哪一步。
/// </para>
/// </summary>
public static class CardBuilder
{
    /// <summary>只按目标图形建卡（不借助已有配方：顶针 / 堆叠收尾会被记进 Note）。</summary>
    public static WorkOrderCardSet Build(
        ShapeDocument target)
    {
        return Build(target, null);
    }

    /// <summary>
    /// 按目标图形建卡。<paramref name="recipe"/> 只用来识别「交换之后的顶针 / 堆叠收尾」，
    /// 不参与任何求解；传 null 时卡片只描述到交换产物为止。
    /// </summary>
    public static WorkOrderCardSet Build(
        ShapeDocument target,
        Recipe? recipe)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        int parts = target.PartCount;

        var set = new WorkOrderCardSet(target);

        if (parts < 4 || parts % 2 != 0)
        {
            set.Note = $"n = {parts}：卡片层级只处理 n ≥ 4 的偶数象限数" +
                       "（n = 2 是定理 5 的孤立列拼装，没有半边层）。";
            return set;
        }

        /*
         * 顶针 / 堆叠收尾：卡片描述的是「交换产物」core；顶针 / 堆叠作为交换卡的
         * 卡内步骤（plan §4 Q5），所以角卡 / 半边卡都按 core 建。
         */
        ShapeDocument core = target;
        RecipeKind? tail = null;

        if (recipe is not null &&
            recipe.Inputs.Count == 1 &&
            recipe.Kind is RecipeKind.Pin or RecipeKind.Stack)
        {
            core = recipe.Inputs[0].Shape;
            tail = recipe.Kind;

            set.Note = tail == RecipeKind.Pin
                ? "本图由顶针收尾：角卡 / 半边卡按顶针前的图形构造，交换卡的「顶破」步得到目标。"
                : "本图由堆叠收尾：角卡 / 半边卡按堆叠前的图形构造，交换卡的「堆叠」步得到目标。";
        }

        int cap = core.LayerCount;

        /*
         * 一、先解两条半边工序（只读求解器）。
         *
         * 角卡的分组依赖它：某个角是否属于「需要碎晶的半边」，只能由
         * HalfWorkOrder 里有没有真实 Cut 步判定（用户裁定 2026-11-20）。
         * 半边求解本身不改：轴仍取「最小可行列号」（plan §4 Q8）。
         */
        int half = parts / 2;

        int axis = 0;
        HalfWorkOrder? leftOrder = null;
        HalfWorkOrder? rightOrder = null;
        bool found = false;

        for (int candidate = 0; candidate < half; candidate++)
        {
            int rightColumn = ShapeOps.Wrap(candidate + half, parts);

            HalfWorkOrder? left = AngleWorkOrderBuilder.BuildHalf(core, candidate, cap);
            HalfWorkOrder? right = AngleWorkOrderBuilder.BuildHalf(core, rightColumn, cap);

            if (left is { IsComplete: true } && right is { IsComplete: true })
            {
                axis = candidate;
                leftOrder = left;
                rightOrder = right;
                found = true;
                break;
            }

            if (candidate == 0)
            {
                leftOrder = left;
                rightOrder = right;
            }
        }

        if (!found)
        {
            set.Note = JoinNotes(
                set.Note,
                "两条轴都没有给出两个可得的半边：半边卡按不可得处理。");
        }

        int rightAxis = ShapeOps.Wrap(axis + half, parts);

        /*
         * 二、角卡：只给非空角画卡。
         *
         * 用户裁定（2026-11-20）：
         * · 角属于一个需要碎晶的半边 ⇒ 产物 = 该列在【半边前态】里的紧列 + 左右双柱，
         *   可见步骤里没有碎晶（碎晶由半边卡完成）；
         * · 否则（独立角）⇒ 角卡保持原样（紧列 → 碎晶 / 顶破 → 目标角 + 双柱）。
         */
        var cornerCards = new List<WorkOrderCardView>();

        for (int column = 0; column < parts; column++)
        {
            if (!ColumnHasContent(core, column))
            {
                continue;
            }

            HalfWorkOrder? cutHalf = null;
            string? cutHalfTitle = null;

            if (HalfNeedsCut(leftOrder) &&
                ColumnInHalf(column, axis, half, parts))
            {
                cutHalf = leftOrder;
                cutHalfTitle = "半边1";
            }
            else if (HalfNeedsCut(rightOrder) &&
                     ColumnInHalf(column, rightAxis, half, parts))
            {
                cutHalf = rightOrder;
                cutHalfTitle = "半边2";
            }

            cornerCards.Add(BuildCornerCard(
                core,
                column,
                cap,
                cutHalf,
                cutHalfTitle));
        }

        set.CornerCards = cornerCards;

        // 三、半边卡。
        var halfCards = new List<WorkOrderCardView>
        {
            BuildHalfCard(core, axis, parts, cap, leftOrder, cornerCards, "半边1"),
            BuildHalfCard(core, rightAxis, parts, cap, rightOrder, cornerCards, "半边2")
        };

        set.HalfCards = halfCards;

        // 四、交换卡。
        set.ExchangeCard = BuildExchangeCard(target, core, halfCards, recipe, tail);

        return set;
    }

    #region 角卡

    /// <summary>
    /// 一张角卡：目标列 + 左右柱，产物归一化到 Q1。
    ///
    /// <list type="bullet">
    /// <item><b>属于需要碎晶的半边</b>（<paramref name="cutHalf"/> 非空）：
    /// 由 <see cref="TryBuildTightCornerCard"/> 接管——产物 = 该列在半边前态里的
    /// 紧列 + 左右双柱，<b>没有碎晶步</b>（碎晶归半边卡）。</item>
    /// <item><b>独立角</b>：步骤来自角工序单（<see cref="AngleWorkOrder.Steps"/>）；
    /// 走半边工序的角（<see cref="AngleWorkOrder.Half"/> 非空）改用
    /// <see cref="HalfWorkOrder.Steps"/> 的两列视图。</item>
    /// </list>
    /// </summary>
    private static WorkOrderCardView BuildCornerCard(
        ShapeDocument core,
        int column,
        int cap,
        HalfWorkOrder? cutHalf = null,
        string? cutHalfTitle = null)
    {
        /*
         * 属于「需要碎晶的半边」的角：碎晶归半边卡，本卡只产出
         * 「该列在半边前态里的紧列 + 左右双柱」（用户裁定 2026-11-20）。
         */
        if (cutHalf is not null)
        {
            WorkOrderCardView? tightCard = TryBuildTightCornerCard(
                column,
                cap,
                cutHalf,
                cutHalfTitle);

            if (tightCard is not null)
            {
                return tightCard;
            }
        }

        int parts = core.PartCount;

        ShapeDocument product = BuildCornerProduct(core, column);
        ShapeDocument output = ShapeNormalization.NormalizeToQ1(product, column);

        var card = new WorkOrderCardView(
            CardKind.Corner,
            "角" + (column + 1),
            output)
        {
            Column = column
        };

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(core, column, cap);

        bool halfBased = order.Half is not null;

        IReadOnlyList<AngleWorkStep> source =
            halfBased && order.Half!.IsComplete && order.Half.Steps.Count > 0
                ? order.Half.Steps
                : order.Steps;

        var steps = new List<CardStep>();
        var failure = new List<string>();

        if (!order.IsComplete)
        {
            failure.Add(order.Note ?? "角工序未完成。");
        }

        ShapeDocument? previous = null;
        bool firstPreState = true;

        foreach (AngleWorkStep step in source)
        {
            if (step.Shape is null)
            {
                continue;
            }

            ShapeDocument shape = step.Shape;

            switch (step.Kind)
            {
                case AngleWorkStepKind.PreState:
                    if (firstPreState)
                    {
                        steps.Add(new CardStep(
                            "前态",
                            shape,
                            hidden: true,
                            detail: halfBased
                                ? "半边工序前态（k 列一起做；卡内私有，不作为输入）"
                                : "角工序前态（卡内私有，不作为输入）"));
                        firstPreState = false;
                    }
                    else
                    {
                        steps.Add(new CardStep(
                            "纯支撑交接",
                            shape,
                            hidden: true,
                            detail: "重新装辅助列（目标列逐格保持）"));
                    }

                    break;

                case AngleWorkStepKind.Cut:
                    steps.Add(new CardStep(
                        "碎晶",
                        shape,
                        detail: halfBased
                            ? "半边工序的一刀：真实 Cut 沿 n/2 刀口切开"
                            : "切割（碎晶并落定）"));
                    break;

                case AngleWorkStepKind.Pin:
                    steps.Add(new CardStep("顶破", shape));
                    break;

                case AngleWorkStepKind.Collapse:
                    steps.Add(new CardStep("下落", shape, hidden: true));
                    break;

                case AngleWorkStepKind.WeakDrop:
                    steps.Add(new CardStep("弱落", shape, hidden: true));
                    break;

                case AngleWorkStepKind.Exchange:
                    steps.Add(new CardStep(
                        "纯支撑交接",
                        shape,
                        hidden: true,
                        detail: "轮间交换（支柱交接）"));
                    break;

                case AngleWorkStepKind.Support:
                    steps.Add(new CardStep(
                        "纯支撑交接",
                        shape,
                        hidden: true,
                        detail: "构造 C：装新支撑柱、拆旧柱"));
                    break;

                case AngleWorkStepKind.Stack:
                    steps.Add(new CardStep(
                        "堆叠尾",
                        shape,
                        hidden: true,
                        detail: "构造 C：逐层追加上层尾部（结构步骤）"));
                    break;

                case AngleWorkStepKind.Crystallize:
                    steps.Add(new CardStep("结晶", shape, hidden: true));
                    break;

                case AngleWorkStepKind.Target:
                    if (IsThreeColumnProduct(shape, column))
                    {
                        steps.Add(new CardStep(
                            "构造 C",
                            shape,
                            hidden: true,
                            detail: "清辅助列 + 装左右柱，目标列逐格保持"));
                    }
                    else
                    {
                        steps.Add(new CardStep(
                            "收尾",
                            shape,
                            hidden: true,
                            detail: "末尾快照（目标列逐格保持）"));
                    }

                    break;

                case AngleWorkStepKind.Note:
                    continue;
            }

            previous = shape;
        }

        /* 产物：目标列 + 左右柱。最后一步不是这个形态时，用构造 C 显式接上。 */
        if (previous is null)
        {
            steps.Add(new CardStep(
                "构造 A",
                product,
                hidden: true,
                detail: "紧列（无洞）：逐层追加材料，没有碎晶 / 顶破步"));
        }
        else if (!ShapeOps.Same(previous, product))
        {
            steps.Add(new CardStep(
                "构造 C",
                product,
                hidden: true,
                detail: "清辅助列 + 装左右柱，目标列逐格保持"));
        }

        int rotation = ShapeNormalization.Q1Rotation(parts, column);

        steps.Add(new CardStep(
            "旋转",
            output,
            hidden: true,
            detail: $"顺转 {rotation} 个象限：目标列 Q1、右柱 Q2、左柱 Q{parts}")
        {
            Rotation = rotation
        });

        card.Steps = steps;

        /*
         * 角卡的可见前态：只有回放链第一步就是隐藏「前态」时才填。
         * 与半边卡不同，角卡的前态不是输入链，只是单独展示的起点。
         */
        if (steps.Count > 0 && steps[0].Operation == "前态")
        {
            card.PreState = steps[0].Output;
            card.PreStateTitle = "前态";
        }

        bool ok = CardReplay.Verify(card, out IReadOnlyList<string> replayErrors);

        if (!ok || failure.Count > 0)
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();

            card.Note = "未通过：" + (failure.Count > 0
                ? string.Join("；", failure)
                : string.Join("；", replayErrors));
        }
        else
        {
            card.Verified = true;

            card.Note = halfBased
                ? "B 类角（S ✘）：目标列由半边工序的一刀真实产出，必须与半边一起做；" +
                  "左右柱是构造 C 的结构交接（隐藏步）。"
                : "S 角：单角可做（" + Describe(order) + "）；产物已归一化到 Q1。";
        }

        return card;
    }

    /// <summary>
    /// 「左柱 + 目标列 + 右柱」三列形态（原朝向）：目标列原样，两侧补接地的满高普通柱。
    /// </summary>
    private static ShapeDocument BuildCornerProduct(
        ShapeDocument core,
        int column)
    {
        int parts = core.PartCount;
        int layers = core.LayerCount;

        var product = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            product[layer, column] = core[layer, column].Clone();
        }

        int height = ShapeNormalization.ColumnHeight(core, column);

        if (height == 0)
        {
            return product;
        }

        int left = ShapeOps.Wrap(column - 1, parts);
        int right = ShapeOps.Wrap(column + 1, parts);

        for (int layer = 0; layer < height; layer++)
        {
            if (product[layer, left].Type == CellType.Empty)
            {
                product[layer, left] = ShapeCell.Shape('C', 'u');
            }

            if (product[layer, right].Type == CellType.Empty)
            {
                product[layer, right] = ShapeCell.Shape('C', 'u');
            }
        }

        return product;
    }

    /// <summary>
    /// 角属于一个「需要碎晶的半边」时的角卡：
    /// <c>Output = NormalizeToQ1(该列在半边前态里的紧列 + 左右双柱)</c>，<b>没有碎晶步</b>。
    ///
    /// <para>
    /// 真实回放：<c>构造 A</c> 用结构校验确认产物是紧列 + 三列形态，
    /// <c>旋转</c> 用真实 <see cref="ShapeOps.Rotate"/> 把目标列旋到 Q1。
    /// 前态列不紧（候选 3 之外的半边）或层数对不上时返回 null，调用方退回原角卡路径。
    /// </para>
    /// </summary>
    private static WorkOrderCardView? TryBuildTightCornerCard(
        int column,
        int cap,
        HalfWorkOrder cutHalf,
        string? cutHalfTitle)
    {
        ShapeDocument? preState = FirstPreStateShape(cutHalf.Steps);

        if (preState is null ||
            preState.LayerCount != cap ||
            !ShapeSignature.IsTightColumn(preState, column))
        {
            return null;
        }

        int parts = preState.PartCount;

        ShapeDocument product = BuildTightCornerProduct(preState, column);
        ShapeDocument output = ShapeNormalization.NormalizeToQ1(product, column);

        var card = new WorkOrderCardView(
            CardKind.Corner,
            "角" + (column + 1),
            output)
        {
            Column = column,
            TightColumnProduct = true
        };

        var steps = new List<CardStep>
        {
            new(
                "构造 A",
                product,
                hidden: true,
                detail: "紧列（该列在半边前态里的形态）+ 左右双柱；" +
                        "提升 / 临时晶体都在这一列里完成，碎晶归半边卡"),
            new(
                "旋转",
                output,
                hidden: true,
                detail: $"顺转 {ShapeNormalization.Q1Rotation(parts, column)} 个象限：" +
                        "目标列 Q1、右柱 Q2、左柱 Q" + parts)
            {
                Rotation = ShapeNormalization.Q1Rotation(parts, column)
            }
        };

        card.Steps = steps;

        if (!CardReplay.Verify(card, out IReadOnlyList<string> replayErrors))
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.Note = "未通过：" + string.Join("；", replayErrors);
        }
        else
        {
            card.Verified = true;
            card.Note = (string.IsNullOrEmpty(cutHalfTitle)
                    ? "属于需要碎晶的半边"
                    : "属于" + cutHalfTitle + "（需要碎晶）") +
                "：产物 = 该列在半边前态里的紧列 + 左右双柱；" +
                "碎晶由半边卡完成，本卡不显示碎晶步。";
        }

        return card;
    }

    /// <summary>
    /// 「左柱 + 紧列 + 右柱」三列形态（原朝向）：目标列取自半边前态，两侧补接地满高普通柱。
    /// </summary>
    private static ShapeDocument BuildTightCornerProduct(
        ShapeDocument preState,
        int column)
    {
        int parts = preState.PartCount;
        int layers = preState.LayerCount;

        var product = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            product[layer, column] = preState[layer, column].Clone();
        }

        int height = ShapeNormalization.ColumnHeight(preState, column);

        if (height == 0)
        {
            return product;
        }

        int left = ShapeOps.Wrap(column - 1, parts);
        int right = ShapeOps.Wrap(column + 1, parts);

        for (int layer = 0; layer < height; layer++)
        {
            if (product[layer, left].Type == CellType.Empty)
            {
                product[layer, left] = ShapeCell.Shape('C', 'u');
            }

            if (product[layer, right].Type == CellType.Empty)
            {
                product[layer, right] = ShapeCell.Shape('C', 'u');
            }
        }

        return product;
    }

    #endregion

    #region 半边卡

    /// <summary>
    /// 一张半边卡：k = n/2 列。
    ///
    /// <list type="bullet">
    /// <item><b>需要碎晶的半边</b>（半边工序里有真实 <c>Cut</c> 步）：
    /// <see cref="HalfMode.FromTightColumns"/>——Inputs = 该半边各列的紧列（角卡产物），
    /// 卡内 = 1..N 个真实「碎晶」步（N = 真实 Cut 步数），Output = 目标半边；</item>
    /// <item>不需要碎晶：先试 A 类 <see cref="HalfMode.FromCorners"/>（两个角产物真实交换），
    /// 不成立再用 <see cref="HalfMode.WholeHalf"/>（候选 1 / 2，Inputs 为空）。</item>
    /// </list>
    /// </summary>
    private static WorkOrderCardView BuildHalfCard(
        ShapeDocument core,
        int from,
        int parts,
        int cap,
        HalfWorkOrder? order,
        IReadOnlyList<WorkOrderCardView> cornerCards,
        string title)
    {
        int width = parts / 2;

        var columns = new int[width];

        for (int index = 0; index < width; index++)
        {
            columns[index] = ShapeOps.Wrap(from + index, parts);
        }

        ShapeDocument expected = ShapeOps.Project(core, from, width);

        var card = new WorkOrderCardView(CardKind.Half, title, expected)
        {
            Column = from
        };

        /*
         * 用户裁定（2026-11-20）：碎晶是对整个图形的操作，一刀贯穿所有列。
         * 所以「需要碎晶的半边」单独成一种卡：输入各列紧列，卡内按真实 Cut 步数碎晶。
         */
        if (HalfNeedsCut(order))
        {
            return BuildCutHalfCard(core, from, parts, cap, order!, cornerCards, title);
        }

        var halfCorners = cornerCards
            .Where(corner => columns.Contains(corner.Column))
            .ToArray();

        /* A 类先试：两个角卡产物经真实 Exchange 拼成半边。 */
        List<CardStep>? aSteps = TryBuildFromCorners(
            halfCorners,
            expected,
            parts,
            out string aReason);

        if (aSteps is not null)
        {
            card.Mode = HalfMode.FromCorners;
            card.Inputs = halfCorners.Select(corner => corner.Output).ToArray();
            card.InputTitles = halfCorners.Select(corner => corner.Title).ToArray();
            card.Steps = aSteps;
        }
        else
        {
            card.Mode = HalfMode.WholeHalf;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.InputTitles = Array.Empty<string>();

            var steps = new List<CardStep>();
            var failure = new List<string>();

            if (order is null ||
                !order.IsComplete ||
                order.Steps.Count == 0 ||
                order.Steps[^1].Shape is null)
            {
                failure.Add(order?.Note ?? "半边工序不可得（BuildHalf 返回 null）。");
            }
            else
            {
                ShapeDocument half = order.Steps[^1].Shape!;

                if (!SameColumns(half, expected, columns))
                {
                    failure.Add("半边工序的产物与目标的那 k 列不一致。");
                }

                ShapeDocument? preState = FirstPreStateShape(order.Steps);

                if (preState is null)
                {
                    failure.Add("半边工序没有前态。");
                }
                else
                {
                    /* 整半边卡（候选 1 / 2）的前态也放进卡片字段，供页面 / CLI 单独展示。 */
                    card.PreState = preState;
                    card.PreStateTitle = "前态";

                    steps.Add(new CardStep(
                        "前态",
                        preState,
                        hidden: true,
                        detail: "半边工序前态（k 列一起做；卡内私有，不作为输入）"));

                    switch (order.Candidate)
                    {
                        case HalfCandidate.TempCrystalFuse:
                        {
                            ShapeDocument? cut = LastShapeOfKind(
                                order.Steps,
                                AngleWorkStepKind.Cut);

                            if (cut is null)
                            {
                                failure.Add("候选 3 缺少真实 Cut 快照。");
                            }
                            else
                            {
                                steps.Add(new CardStep(
                                    "碎晶",
                                    cut,
                                    detail: "真实 Cut：顺转 " + order.Rotation +
                                            " 个象限后沿 n/2 刀口切开，打碎临时晶体 / 闸门晶体 / 保险丝"));

                                if (!ShapeOps.Same(cut, half))
                                {
                                    failure.Add("候选 3 的 Cut 产物与半边目标不一致。");
                                }
                            }

                            break;
                        }

                        case HalfCandidate.Algo8Uplift:
                        {
                            ShapeDocument? collapsed =
                                ShapePhysics.Collapse(preState).Document;

                            if (collapsed is null ||
                                !ShapeOps.Same(collapsed, half))
                            {
                                failure.Add("候选 2 的真实下落没有得到半边目标。");
                            }

                            steps.Add(new CardStep(
                                "下落",
                                collapsed ?? half,
                                hidden: true,
                                detail: "候选 2：Q′ 装配后自动下落一次（不是切割）"));

                            break;
                        }

                        default:
                        {
                            ShapeDocument? collapsed =
                                ShapePhysics.Collapse(preState).Document;

                            if (!ShapeOps.Same(preState, half) ||
                                !ShapeOps.Same(collapsed, preState))
                            {
                                failure.Add("候选 1 的前态应当就是稳定的半边目标。");
                            }

                            steps.Add(new CardStep(
                                "下落",
                                collapsed ?? preState,
                                hidden: true,
                                detail: "候选 1：前态就是半边，真实下落恒等（无需切割）"));

                            break;
                        }
                    }
                }
            }

            card.Steps = steps;

            card.Note = failure.Count > 0
                ? "未通过：" + string.Join("；", failure)
                : DescribeHalfNote(order!, aReason);
        }

        if (!CardReplay.Verify(card, out IReadOnlyList<string> replayErrors))
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.InputTitles = Array.Empty<string>();

            card.Note = "未通过：" + string.Join("；", replayErrors) +
                        (string.IsNullOrEmpty(card.Note) ? string.Empty : "（" + card.Note + "）");
        }
        else
        {
            card.Verified = true;
        }

        return card;
    }

    /// <summary>
    /// 需要碎晶的半边卡（<see cref="HalfMode.FromTightColumns"/>）：
    ///
    /// <code>
    /// Inputs = 该半边各列的紧列（角卡产物 = 紧列 + 双柱，归一化 Q1）
    /// 卡内   = 并半（隐藏：把各输入紧列放回前态）→ 碎晶 × N（N = 真实 Cut 步数）→ 收尾
    /// Output = 目标半边（k 列）
    /// </code>
    ///
    /// <para><b>真实回放</b>：<c>并半</c> 逐格核对每张输入的 Q1 紧列确实进了前态；
    /// 每个 <c>碎晶</c> 步都用真实 <see cref="ShapePhysics.Cut"/> 重算；
    /// 刀数 N 直接取半边工序里的真实 <c>Cut</c> 步数（单刀 = 1 步，多刀 = N 步）。</para>
    /// </summary>
    private static WorkOrderCardView BuildCutHalfCard(
        ShapeDocument core,
        int from,
        int parts,
        int cap,
        HalfWorkOrder order,
        IReadOnlyList<WorkOrderCardView> cornerCards,
        string title)
    {
        int width = parts / 2;

        var columns = new int[width];

        for (int index = 0; index < width; index++)
        {
            columns[index] = ShapeOps.Wrap(from + index, parts);
        }

        ShapeDocument expected = ShapeOps.Project(core, from, width);

        var card = new WorkOrderCardView(CardKind.Half, title, expected)
        {
            Column = from,
            Mode = HalfMode.FromTightColumns
        };

        /*
         * 输入只取「已经是紧列 + 双柱」的角卡产物：这才是半边前态里的那一列。
         * （候选 3 的目标列 / 环伙伴列都是紧列；万一某列退回旧角卡，就不冒充输入。）
         */
        var halfCorners = cornerCards
            .Where(corner => columns.Contains(corner.Column) &&
                             corner.TightColumnProduct)
            .ToArray();

        card.Inputs = halfCorners.Select(corner => corner.Output).ToArray();
        card.InputTitles = halfCorners.Select(corner => corner.Title).ToArray();
        card.InputColumns = halfCorners.Select(corner => corner.Column).ToArray();

        ShapeDocument? preState = FirstPreStateShape(order.Steps);

        /*
         * 可见前态 =「并半」后的真实 Cut 输入：就是下面「并半」/「前态」步骤用的
         * 同一个 ShapeDocument（FirstPreStateShape 返回的引用），不是另算一份。
         * 它单独挂在卡片上展示；「并半」步本身仍保持 Hidden。
         */
        card.PreState = preState;
        card.PreStateTitle = preState is null
            ? null
            : "碎晶前（并半后，真实 Cut 的输入）";

        var steps = new List<CardStep>();
        var failure = new List<string>();

        if (preState is null)
        {
            failure.Add("半边工序没有前态。");
        }
        else if (card.Inputs.Count > 0)
        {
            steps.Add(new CardStep(
                "并半",
                preState,
                hidden: true,
                detail: "把各列的紧列（角卡产物）放回半边前态；" +
                        "辅助列 / 保险丝由本卡构造（前态仍是卡内私有）"));
        }
        else
        {
            steps.Add(new CardStep(
                "前态",
                preState,
                hidden: true,
                detail: "半边工序前态（没有可用的紧列角卡输入，前态卡内私有）"));
        }

        int knife = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Shape is null)
            {
                continue;
            }

            switch (step.Kind)
            {
                case AngleWorkStepKind.PreState:
                case AngleWorkStepKind.Target:
                case AngleWorkStepKind.Note:
                    break;

                case AngleWorkStepKind.Cut:
                    knife++;

                    steps.Add(new CardStep(
                        "碎晶",
                        step.Shape,
                        detail: $"第 {knife} 刀：真实 Cut 贯穿整个图形，打碎临时晶体 / " +
                                "闸门晶体 / 保险丝后落定"));
                    break;

                case AngleWorkStepKind.Exchange:
                    steps.Add(new CardStep(
                        "纯支撑交接",
                        step.Shape,
                        hidden: true,
                        detail: "轮间支柱交接：重装桥梁柱 / 保险丝柱，目标列逐格保持"));
                    break;

                default:
                    steps.Add(new CardStep(
                        step.Kind.ToString(),
                        step.Shape,
                        hidden: true,
                        detail: "半边工序的中间态（数据保留，不作可见步骤）"));
                    break;
            }
        }

        if (knife == 0)
        {
            failure.Add("半边工序没有真实 Cut 步，不能按「需要碎晶」建卡。");
        }

        if (steps.Count == 0)
        {
            failure.Add("半边卡没有任何步骤。");
        }
        else if (!ShapeOps.Same(steps[^1].Output, expected))
        {
            /*
             * 末步不是目标半边时，用结构收尾接上（目标列逐格保持）。
             * 正常情况下候选 3 的最后一刀就是目标半边，不会走到这里。
             */
            steps.Add(new CardStep(
                "收尾",
                expected,
                hidden: true,
                detail: "最后一刀之后清掉辅助象限，得到目标半边"));

            if (steps.Count(step => step.Operation == "碎晶") == 0)
            {
                failure.Add("半边卡没有可见碎晶步。");
            }
        }

        card.Steps = steps;

        int blocks = preState is null
            ? 0
            : CountTemporaryBlocks(preState, expected, columns);

        string missingInputs = halfCorners.Length < columns.Length
            ? $"（{columns.Length - halfCorners.Length} 列不是紧列，未作为输入）"
            : string.Empty;

        card.Note = failure.Count > 0
            ? "未通过：" + string.Join("；", failure)
            : $"{title}：输入 = 该半边各列的紧列（角卡产物，各带双柱）{missingInputs}；" +
              $"卡内 {knife} 个真实 Cut 步（临时晶体块 K = {blocks}，" +
              "保险丝在每个块顶层各放一颗，单刀即可同时打碎）；" +
              "输出 = 目标半边。";

        if (!CardReplay.Verify(card, out IReadOnlyList<string> replayErrors))
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.InputTitles = Array.Empty<string>();
            card.InputColumns = Array.Empty<int>();

            card.Note = "未通过：" + string.Join("；", replayErrors) +
                        (failure.Count > 0 ? "（" + string.Join("；", failure) + "）" : string.Empty);
        }
        else
        {
            card.Verified = true;
        }

        return card;
    }

    /// <summary>
    /// 半边前态里的临时晶体连通块数 K：只在「前态是晶体、目标半边不是晶体」的格子上
    /// 按纵向连通分组。它只是说明文字（刀数由真实 Cut 步数决定）。
    /// </summary>
    private static int CountTemporaryBlocks(
        ShapeDocument preState,
        ShapeDocument expected,
        IReadOnlyList<int> columns)
    {
        int blocks = 0;

        foreach (int column in columns)
        {
            for (int layer = 0; layer < preState.LayerCount; layer++)
            {
                if (preState[layer, column].Type != CellType.Crystal ||
                    (layer < expected.LayerCount &&
                     expected[layer, column].Type == CellType.Crystal))
                {
                    continue;
                }

                blocks++;

                while (layer + 1 < preState.LayerCount &&
                       preState[layer + 1, column].Type == CellType.Crystal &&
                       (layer + 1 >= expected.LayerCount ||
                        expected[layer + 1, column].Type != CellType.Crystal))
                {
                    layer++;
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// A 类：两张（或 k 张）角卡产物 → 真实 <see cref="ShapeExchange.Exchange"/> → 半边。
    ///
    /// <para>
    /// 旋转量从 0 开始按字典序试（先第一张、再第二张，先第二路输出、再第一路），
    /// 命中即停——确定性，不随机。k ≠ 2 时不做 A 类尝试（记录原因）。
    /// </para>
    /// </summary>
    private static List<CardStep>? TryBuildFromCorners(
        IReadOnlyList<WorkOrderCardView> corners,
        ShapeDocument expected,
        int parts,
        out string reason)
    {
        int width = parts / 2;

        if (width != 2)
        {
            reason = $"k = {width}：A 类只尝试「两个角」的切分，n ≥ 6 交给半边工序。";
            return null;
        }

        if (corners.Count != 2 ||
            corners.Any(corner => !corner.Verified))
        {
            reason = "该半边没有两张都通过校验的角卡。";
            return null;
        }

        ShapeDocument first = corners[0].Output;
        ShapeDocument second = corners[1].Output;

        for (int ra = 0; ra < parts; ra++)
        {
            ShapeDocument left = ShapeOps.Rotate(first, ra);

            for (int rb = 0; rb < parts; rb++)
            {
                ShapeDocument right = ShapeOps.Rotate(second, rb);

                (ShapeDocument? a, ShapeDocument? b, _) =
                    ShapeExchange.Exchange(left, right);

                if (a is not null && ShapeOps.Same(b, expected) ||
                    b is not null && ShapeOps.Same(a, expected))
                {
                    ShapeDocument product = ShapeOps.Same(a, expected) ? a! : b!;

                    reason = string.Empty;

                    return new List<CardStep>
                    {
                        new("旋转", left, hidden: true,
                            detail: $"第 1 个角产物顺转 {ra} 个象限（Inputs[0]）")
                        {
                            Rotation = ra,
                            InputIndex = 0
                        },
                        new("旋转", right, hidden: true,
                            detail: $"第 2 个角产物顺转 {rb} 个象限（Inputs[1]）")
                        {
                            Rotation = rb,
                            InputIndex = 1
                        },
                        new("交换", product, detail: "两个角产物交换成半边")
                    };
                }
            }
        }

        reason = "两个角产物的任何一种真实 Exchange（旋转量 0…n−1 全试）都拼不出半边：" +
                 "角产物各占 3 列（目标列 + 左右柱），横跨 n/2 刀口两侧，" +
                 "交换在保留一侧的同时必然把另一侧的支撑柱一起带回来。";
        return null;
    }

    #endregion

    #region 交换卡

    /// <summary>
    /// 交换卡：两张半边卡的输出 → 真实交换 → 目标（+ 顶针 / 堆叠收尾）。
    /// </summary>
    private static WorkOrderCardView BuildExchangeCard(
        ShapeDocument target,
        ShapeDocument core,
        IReadOnlyList<WorkOrderCardView> halfCards,
        Recipe? recipe,
        RecipeKind? tail)
    {
        int parts = core.PartCount;

        var card = new WorkOrderCardView(CardKind.Exchange, "交换", target);

        if (halfCards.Count != 2 ||
            !halfCards[0].Verified ||
            !halfCards[1].Verified)
        {
            card.Steps = Array.Empty<CardStep>();
            card.Verified = false;
            card.Note = "未通过：两张半边卡没有都通过真实回放，交换卡不给输入链。";
            return card;
        }

        List<CardStep>? exchangeSteps = TryBuildExchangeSteps(
            halfCards[0].Output,
            halfCards[1].Output,
            core,
            parts,
            out ShapeDocument? waste,
            out string exchangeReason);

        if (exchangeSteps is null)
        {
            card.Steps = Array.Empty<CardStep>();
            card.Verified = false;
            card.Note = "未通过：" + exchangeReason;
            return card;
        }

        var steps = new List<CardStep>(exchangeSteps);

        /* 废料是真实 Exchange 的另一路输出：数据保留（Hidden），UI 不画。 */
        if (waste is not null)
        {
            steps.Add(new CardStep(
                "废料",
                waste,
                hidden: true,
                detail: "真实交换的另一路输出（plan §4 Q4：数据保留、不展示）"));
        }

        card.Inputs = new[]
        {
            halfCards[0].Output,
            halfCards[1].Output
        };

        card.InputTitles = new[]
        {
            halfCards[0].Title,
            halfCards[1].Title
        };

        if (tail == RecipeKind.Pin)
        {
            steps.Add(new CardStep(
                "顶破",
                target,
                detail: "交换之后再来一次顶针，得到目标图形"));
        }
        else if (tail == RecipeKind.Stack && recipe?.Block is not null)
        {
            steps.Add(new CardStep(
                "堆叠",
                target,
                detail: "交换之后把 hybrid 块堆上去，得到目标图形")
            {
                Block = recipe.Block
            });
        }
        else if (!ShapeOps.Same(core, target))
        {
            card.Steps = steps;
            card.Verified = false;
            card.Note = "未通过：交换产物不是目标，但配方里没有可展示的顶针 / 堆叠步。";
            return card;
        }

        card.Steps = steps;

        if (!CardReplay.Verify(card, out IReadOnlyList<string> replayErrors))
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.InputTitles = Array.Empty<string>();
            card.Note = "未通过：" + string.Join("；", replayErrors);
            return card;
        }

        card.Verified = true;
        card.Note = tail is null
            ? "两个半边经真实 Exchange 拼成目标。"
            : "两个半边经真实 Exchange 拼成中间图形，再由" +
              (tail == RecipeKind.Pin ? "顶破" : "堆叠") + "得到目标。";

        return card;
    }

    /// <summary>
    /// 交换步：方向 A（左半边在西、右半边在东）优先，失败再试方向 B
    /// （两边整体转半圈，交换后转回）——有界两次，确定性。
    /// <paramref name="waste"/> 只在方向 A 下给出（方向 B 的废料在旋转后的坐标系里，
    /// 不再混进卡片数据）。
    /// </summary>
    private static List<CardStep>? TryBuildExchangeSteps(
        ShapeDocument left,
        ShapeDocument right,
        ShapeDocument expected,
        int parts,
        out ShapeDocument? waste,
        out string reason)
    {
        waste = null;

        (ShapeDocument? first, ShapeDocument? second, string? error) =
            ShapeExchange.Exchange(left, right);

        if (first is null || second is null)
        {
            reason = "真实交换失败（" + (error ?? "未知原因") + "）。";
            return null;
        }

        if (ShapeOps.Same(second, expected))
        {
            waste = first;
            reason = string.Empty;
            return new List<CardStep>
            {
                new("交换", second, detail: "方向 A：取交换的第二路输出（西半 + 东半）")
            };
        }

        if (ShapeOps.Same(first, expected))
        {
            waste = second;
            reason = string.Empty;
            return new List<CardStep>
            {
                new("交换", first, detail: "方向 A：取交换的第一路输出")
            };
        }

        /* 方向 B：两个半边整体转半圈（真的换边），交换后再转回。 */
        int rotation = parts / 2;

        ShapeDocument rotatedLeft = ShapeOps.Rotate(left, rotation);
        ShapeDocument rotatedRight = ShapeOps.Rotate(right, rotation);

        (ShapeDocument? firstB, ShapeDocument? secondB, string? errorB) =
            ShapeExchange.Exchange(rotatedLeft, rotatedRight);

        ShapeDocument?[] candidatesB = { secondB, firstB };

        foreach (ShapeDocument? candidate in candidatesB)
        {
            if (candidate is null)
            {
                continue;
            }

            if (ShapeOps.Same(ShapeOps.Rotate(candidate, -rotation), expected))
            {
                reason = string.Empty;
                return new List<CardStep>
                {
                    new("旋转", rotatedLeft, hidden: true,
                        detail: $"方向 B：左半边整体转 {rotation} 个象限（Inputs[0]）")
                    {
                        Rotation = rotation,
                        InputIndex = 0
                    },
                    new("旋转", rotatedRight, hidden: true,
                        detail: $"方向 B：右半边整体转 {rotation} 个象限（Inputs[1]）")
                    {
                        Rotation = rotation,
                        InputIndex = 1
                    },
                    new("交换", candidate, detail: "方向 B：两半换边后交换"),
                    new("旋转", expected, hidden: true,
                        detail: $"把交换输出转回 {rotation} 个象限，得到目标")
                    {
                        Rotation = ShapeOps.Wrap(-rotation, parts)
                    }
                };
            }
        }

        reason = "两个半边的真实 Exchange（方向 A / 方向 B 各一次）都不逐格等于目标" +
                 "（" + (errorB ?? "未知原因") + "）。";
        return null;
    }

    #endregion

    #region 工具

    /// <summary>
    /// 这条半边工序是否需要碎晶：完整、且有至少一个真实 <see cref="AngleWorkStepKind.Cut"/> 步。
    ///
    /// <para>
    /// 用户裁定 2026-11-20：碎晶是对整个图形的操作，必须由半边卡承担。
    /// 判据只读求解器给出的 <see cref="HalfWorkOrder.Steps"/>，不重新判定候选。
    /// </para>
    /// </summary>
    private static bool HalfNeedsCut(
        HalfWorkOrder? order)
    {
        return order is { IsComplete: true } &&
               order.Steps.Any(step =>
                   step.Kind == AngleWorkStepKind.Cut &&
                   step.Shape is not null);
    }

    /// <summary>某一列是否落在这条半边（从 <paramref name="from"/> 起连续 width 列）里。</summary>
    private static bool ColumnInHalf(
        int column,
        int from,
        int width,
        int parts)
    {
        for (int index = 0; index < width; index++)
        {
            if (ShapeOps.Wrap(from + index, parts) == column)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>该列是否有材料（只给非空角画卡）。</summary>
    private static bool ColumnHasContent(
        ShapeDocument shape,
        int column)
    {
        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            if (shape[layer, column].Type != CellType.Empty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>形态是否就是「左柱 + 目标列 + 右柱」（复用回放层的结构校验）。</summary>
    private static bool IsThreeColumnProduct(
        ShapeDocument shape,
        int column)
    {
        return CardReplay.IsCornerProduct(shape, column, out _);
    }

    /// <summary>两份形态在给定的 k 列上逐格相等。</summary>
    private static bool SameColumns(
        ShapeDocument shape,
        ShapeDocument other,
        IReadOnlyList<int> columns)
    {
        int layers = Math.Max(shape.LayerCount, other.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            foreach (int column in columns)
            {
                ShapeCell a = layer < shape.LayerCount
                    ? shape[layer, column]
                    : ShapeCell.Empty();

                ShapeCell b = layer < other.LayerCount
                    ? other[layer, column]
                    : ShapeCell.Empty();

                if (!ShapeOps.SameCell(a, b))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ShapeDocument? FirstPreStateShape(
        IReadOnlyList<AngleWorkStep> steps)
    {
        foreach (AngleWorkStep step in steps)
        {
            if (step.Kind == AngleWorkStepKind.PreState && step.Shape is not null)
            {
                return step.Shape;
            }
        }

        return null;
    }

    private static ShapeDocument? LastShapeOfKind(
        IReadOnlyList<AngleWorkStep> steps,
        AngleWorkStepKind kind)
    {
        for (int index = steps.Count - 1; index >= 0; index--)
        {
            if (steps[index].Kind == kind && steps[index].Shape is not null)
            {
                return steps[index].Shape;
            }
        }

        return null;
    }

    private static string DescribeHalfNote(
        HalfWorkOrder order,
        string aReason)
    {
        string b = order.Candidate switch
        {
            HalfCandidate.TempCrystalFuse =>
                "B 类（半破）：候选 3「临时晶体 + 保险丝 + 一刀」，真实 Cut 回放通过。",
            HalfCandidate.Algo8Uplift =>
                "整半边一次做出（候选 2：联合逆提升，一次真实下落）。",
            _ =>
                "整半边一次做出（候选 1：Q 自身，无需切割 / 下落）。"
        };

        return string.IsNullOrEmpty(aReason)
            ? b
            : b + " A 类（角产物交换）不可行：" + aReason;
    }

    private static string Describe(
        AngleWorkOrder order)
    {
        return order.Construction switch
        {
            AngleConstruction.A => "构造 A：紧列",
            AngleConstruction.C => "构造 C：支柱交接",
            AngleConstruction.D => "构造 D：定向碎晶",
            AngleConstruction.G => "构造 G：单孔环下落",
            AngleConstruction.F => "构造 F：受控下落",
            AngleConstruction.H => "构造 H：一次顶针",
            AngleConstruction.CrystalBreak => "碎晶（正规核心 + 托柱 + 保险丝，一刀）",
            AngleConstruction.FillBreak => "填充 + 暴破",
            AngleConstruction.Half => "半边工序",
            _ => order.Route.ToString()
        };
    }

    private static string? JoinNotes(
        string? first,
        string second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return second;
        }

        return first + " " + second;
    }

    #endregion
}
