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

            /*
             * v4 半边：两个角都由「逆提升后各自做角」得到。角卡直接复用半边里
             * 已经真实回放过的角工序（liftHalf.CornerOrders），产物 = 角产物（Q1）。
             * n ≥ 6 的整半边没有这一层，走普通角卡。
             */
            HalfWorkOrder? liftHalf = null;
            int liftIndex = -1;

            if (TryGetLiftCorner(leftOrder, column, out liftIndex))
            {
                liftHalf = leftOrder;
            }
            else if (TryGetLiftCorner(rightOrder, column, out liftIndex))
            {
                liftHalf = rightOrder;
            }
            else
            {
                liftIndex = -1;
            }

            cornerCards.Add(BuildCornerCard(
                core,
                column,
                cap,
                liftHalf,
                liftIndex));
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
    /// <item><b>v4 半边里的角</b>（<paramref name="liftHalf"/> 非空）：直接复用
    /// <see cref="HalfWorkOrder.CornerOrders"/> 的角工序，产物 =
    /// <b>逆提升后</b>的目标列 + 左右支撑柱；角卡自己的碎晶 / 顶针仍然可见。</item>
    /// <item><b>独立角</b>：步骤来自角工序单（<see cref="AngleWorkOrder.Steps"/>）；
    /// 走半边工序的角（<see cref="AngleWorkOrder.Half"/> 非空）改用
    /// <see cref="HalfWorkOrder.Steps"/> 的两列视图。</item>
    /// </list>
    /// </summary>
    private static WorkOrderCardView BuildCornerCard(
        ShapeDocument core,
        int column,
        int cap,
        HalfWorkOrder? liftHalf = null,
        int liftIndex = -1)
    {
        int parts = core.PartCount;

        /*
         * v4 半边里的角：直接复用半边已经真实回放过的角工序
         * （<see cref="HalfWorkOrder.CornerOrders"/>）；产物 = 角产物（已归一化 Q1）。
         */
        AngleWorkOrder? liftOrder = null;
        ShapeDocument? liftOutput = null;
        ShapeDocument? liftProduct = null;

        if (liftHalf is { IsComplete: true } &&
            liftIndex >= 0 &&
            liftIndex < liftHalf.CornerOrders.Count &&
            liftIndex < liftHalf.CornerProducts.Count)
        {
            liftOrder = liftHalf.CornerOrders[liftIndex];
            liftOutput = liftHalf.CornerProducts[liftIndex];

            if (liftOrder is not null && liftOutput is not null)
            {
                liftProduct = ShapeOps.Rotate(
                    liftOutput,
                    -ShapeNormalization.Q1Rotation(parts, column));
            }
            else
            {
                liftOrder = null;
            }
        }

        ShapeDocument product = liftProduct ?? BuildCornerProduct(core, column);
        ShapeDocument output = liftOutput ??
                               ShapeNormalization.NormalizeToQ1(product, column);

        var card = new WorkOrderCardView(
            CardKind.Corner,
            "角" + (column + 1),
            output)
        {
            Column = column,
            LiftCornerProduct = liftOrder is not null
        };

        AngleWorkOrder order = liftOrder ??
                               AngleWorkOrderBuilder.Build(core, column, cap);

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

            card.Note = liftOrder is not null
                ? "v4 角：逆提升后由角工序单独做出（" + Describe(order) +
                  "）；产物 = 目标列 + 左右支撑柱（Q1）。"
                : halfBased
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
         * v4：真正的 A 类半边卡——两张角卡产物 → 交换 → 拆柱 → 跌落。
         * 不再有「碎晶」步：碎晶 / 顶针都留在各自的角卡里。
         */
        if (order is { IsComplete: true, Candidate: HalfCandidate.LiftCornerDrop } &&
            order.CornerProducts.Count == width &&
            order.CornerOrders.Count == width)
        {
            return BuildLiftCornerDropHalfCard(
                core,
                from,
                parts,
                cap,
                order,
                cornerCards,
                title,
                expected,
                columns);
        }

        var halfCorners = cornerCards
            .Where(corner => columns.Contains(corner.Column))
            .ToArray();

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
                : DescribeHalfNote(order!, string.Empty);
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
    /// v4 的 A 类半边卡：<b>Inputs = 两张角卡产物</b>，
    /// 卡内 交换 → 拆柱 → 跌落，<b>没有碎晶步</b>。
    ///
    /// <para>
    /// 卡片的每一步都与 <see cref="HalfWorkOrder"/> 里真实回放过的中间态一一对应
    /// （<c>ExchangedShape</c> / <c>StrippedShape</c> / <c>PreSettleShape</c>），
    /// 由 <see cref="CardReplay.Verify"/> 用真实 Exchange / Collapse 重算。
    /// </para>
    /// </summary>
    private static WorkOrderCardView BuildLiftCornerDropHalfCard(
        ShapeDocument core,
        int from,
        int parts,
        int cap,
        HalfWorkOrder order,
        IReadOnlyList<WorkOrderCardView> cornerCards,
        string title,
        ShapeDocument expected,
        int[] columns)
    {
        var card = new WorkOrderCardView(CardKind.Half, title, expected)
        {
            Column = from,
            Mode = HalfMode.FromCorners
        };

        var halfCorners = columns
            .Select(column => cornerCards.FirstOrDefault(corner => corner.Column == column))
            .ToArray();

        if (halfCorners.Any(corner => corner is null || !corner.Verified))
        {
            card.Steps = Array.Empty<CardStep>();
            card.Verified = false;
            card.Note = "未通过：v4 半边的两张角卡没有都通过真实回放。";
            return card;
        }

        card.Inputs = halfCorners.Select(corner => corner!.Output).ToArray();
        card.InputTitles = halfCorners.Select(corner => corner!.Title).ToArray();

        ShapeDocument? exchanged = order.ExchangedShape;
        ShapeDocument? stripped = order.StrippedShape;
        ShapeDocument? preSettle = order.PreSettleShape;

        if (exchanged is null || stripped is null || preSettle is null)
        {
            card.Steps = Array.Empty<CardStep>();
            card.Verified = false;
            card.Note = "未通过：v4 半边缺少交换 / 拆柱 / 跌落的中间态。";
            return card;
        }

        int align = ShapeOps.Wrap(from - 1, parts);

        var steps = new List<CardStep>
        {
            new(
                "旋转",
                ShapeOps.Rotate(card.Inputs[0], 1),
                hidden: true,
                detail: "第 1 张角产物顺转 1 个象限（准备交换）")
            {
                Rotation = 1,
                InputIndex = 0
            },
            new(
                "旋转",
                ShapeOps.Rotate(card.Inputs[1], 2),
                hidden: true,
                detail: "第 2 张角产物顺转 2 个象限（准备交换）")
            {
                Rotation = 2,
                InputIndex = 1
            },
            new(
                "交换",
                exchanged,
                detail: "两张角产物真实 Exchange：两根支撑柱各来自一张角卡"),
            new(
                "拆柱",
                stripped,
                detail: "去掉左柱 + 右柱"),
            new(
                "旋转",
                preSettle,
                hidden: true,
                detail: $"顺转 {align} 个象限，把两列对齐到目标半边")
            {
                Rotation = align
            },
            new(
                "跌落",
                expected,
                detail: "一次真实 Collapse：两列各自落定，等于目标半边")
        };

        card.Steps = steps;

        /*
         * 前态字段保留「拆柱后、跌落前」的形状：它不是回放链的起点，
         * 而是链中间的一步（VerifyPreState 对 FromCorners 允许中间快照）。
         */
        card.PreState = preSettle;
        card.PreStateTitle = "拆柱后（跌落前）";

        card.Note = title + "：v4（A 类，角产物交换）。Inputs = 两张角卡产物" +
                    "（目标列 + 左右支撑柱）；卡内 交换 → 拆柱 → 跌落，没有碎晶步；" +
                    "逆提升 = " +
                    (order.LiftNotes.Count == 0
                        ? "无需抬升"
                        : string.Join("，", order.LiftNotes)) + "。";

        if (!CardReplay.Verify(card, out IReadOnlyList<string> replayErrors))
        {
            card.Verified = false;
            card.Inputs = Array.Empty<ShapeDocument>();
            card.InputTitles = Array.Empty<string>();
            card.Note = "未通过：" + string.Join("；", replayErrors) +
                        "（" + card.Note + "）";
        }
        else
        {
            card.Verified = true;
        }

        return card;
    }


    /// <summary>
    /// A 类：两张（或 k 张）角卡产物 → 真实 <see cref="ShapeExchange.Exchange"/> → 半边。
    ///
    /// <para>
    /// 旋转量从 0 开始按字典序试（先第一张、再第二张，先第二路输出、再第一路），
    /// 命中即停——确定性，不随机。k ≠ 2 时不做 A 类尝试（记录原因）。
    /// </para>
    /// </summary>
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
    /// 这一列是否属于某条 v4 半边；是则给出它在 <see cref="HalfWorkOrder.CornerOrders"/>
    /// 里的下标。只认已完成、且候选为 <see cref="HalfCandidate.LiftCornerDrop"/> 的半边。
    /// </summary>
    private static bool TryGetLiftCorner(
        HalfWorkOrder? order,
        int column,
        out int index)
    {
        index = -1;

        if (order is not { IsComplete: true } ||
            order.Candidate != HalfCandidate.LiftCornerDrop)
        {
            return false;
        }

        IReadOnlyList<int> columns = order.Target.Columns;

        for (int part = 0; part < columns.Count; part++)
        {
            if (columns[part] == column)
            {
                index = part;
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
