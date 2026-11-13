using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器（续）：n = 4 的「路线 F」——算法 4 的 §11 通用实现。
///
/// <para>
/// 规范计划 <c>θ = (lift, fill, support, bridge, cuts, order)</c> 的六个参数
/// 全部由沿列扫描得出（<c>docs/angle-work-order-algorithm.md</c> §11.2），
/// <b>只生成一次</b>，再用真实 <see cref="ShapePhysics"/> 逐格回放验收：
/// </para>
/// <code>
/// θ = CanonicalPlan(target, column, cap)
/// order = Replay(θ)                    // 真实物理回放
/// if order.IsComplete: return order
/// return null                          // 调用方报「未覆盖：缺哪条规则」
/// </code>
/// <para>
/// <b>没有候选枚举</b>：不再有 V1–V5 变体搜索、回放预算、时间预算，
/// 也不再有「生成一批前态 → 逐个回放筛选」的循环。
/// 层号一律由扫描得出（§10），分支谓词只有 D、F、底格是否针、段长这类结构谓词。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    #region 入口

    /// <summary>
    /// 路线 F（n = 4、底格非针、D 不成立、F 成立）的通用实现（§11）。
    /// </summary>
    /// <returns>
    /// 返回 null 表示「规范计划没有通过回放」，由 <see cref="Build"/> 退回现有兜底链；
    /// 最终失败再由上层区分为 <c>Uncovered</c>（未覆盖）而不是 <c>NotProducible</c>。
    /// </returns>
    private static AngleWorkOrder? TryBuildGenericFourQuadrantOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount != 4 || cap < target.LayerCount)
        {
            return null;
        }

        /*
         * 只处理「合法、无针、有晶体」的列：路线 F 的前提是五项条件通过，
         * 而且 §11 的四列布局（核心 / 托柱 / 桥柱）里没有针位。
         */
        string targetColumn = ColumnTextPadded(target, column, cap);
        string tightTarget = ColumnTextPadded(target, column, target.LayerCount);

        if (!ShapeSignature.CheckColumn(tightTarget).IsLegal ||
            !targetColumn.Contains(ShapeSignature.CrystalCode) ||
            targetColumn.Contains(ShapeSignature.PinCode))
        {
            return null;
        }

        /*
         * 算法 4 的适用前提是 F 成立（§1.3 / §11.1：route F 需 F）。
         * F 不成立就直接让位——它是 §10 允许的结构谓词，不是层数特化。
         */
        if (!PillarSCriterion.RecursiveTerminalF(tightTarget))
        {
            return null;
        }

        int highestCrystal = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (highestCrystal <= 0)
        {
            return null;
        }

        // ── lift（§11.2）：只解一次，不枚举抬高量 ──
        int[] destinations = ResolveRecursiveLiftPlan(targetColumn, highestCrystal);

        // ── fill（§11.2）：最高晶体以下的所有空位都填成临时晶体 ──
        char[]? core = BuildCoreColumn(targetColumn, destinations, highestCrystal);

        if (core is null)
        {
            return null;
        }

        string coreText = new(core);
        List<(int Bottom, int Top)> blocks = FindTemporaryBlocks(targetColumn, core);

        if (blocks.Count == 0)
        {
            return null;
        }

        List<(int Core, int Target)> lifts = FindLifts(targetColumn, core);

        // ── support / bridge / cuts（§11.2）：全部由扫描得出 ──
        string support = BuildCanonicalSupportColumn(coreText, lifts);

        /*
         * 切割排程只有两种<b>固定</b>形态（顺序固定，不是候选搜索）：
         * ① 规范值：锚层互不相同 ⇒ 一刀全碎；
         * ② 逐块各一刀、从上到下——阶梯式抬升（相邻两枚普通格之间隔着临时晶体）时，
         *    上一刀留下的临时晶体正好当下一刀的刹车，普通格才会停在各自的落点。
         * 两种形态都不改 lift / fill / support，也不再用预算裁剪。
         */
        AngleWorkOrder? singleCut = ReplayGenericPlan(
            target,
            column,
            cap,
            kind,
            coreText,
            support,
            blocks,
            lifts,
            perBlockCuts: false);

        if (singleCut is not null)
        {
            return singleCut;
        }

        return ReplayGenericPlan(
            target,
            column,
            cap,
            kind,
            coreText,
            support,
            blocks,
            lifts,
            perBlockCuts: true);
    }

    #endregion

    #region §11.2 规范计划的六个参数

    /// <summary>
    /// 规范提升落点（§11.2 的 <c>lift</c>）：全部由扫描得出，只算一遍。
    ///
    /// <para>规则（§11.2 原文，逐条直译）：</para>
    /// <list type="number">
    /// <item>对每个「晶体段底端正下方为空」的段：把<b>下方最近的普通格</b>提到段底正下方；</item>
    /// <item>若该格原位置下方仍为空，段长 1–2 时<b>继续递归</b>，
    /// 段长 ≥3 时停下并记录「环下落」（§4.8.1 的阶梯式抬升 <c>T cc T cc T cc T</c>）。</item>
    /// </list>
    /// <para>
    /// 递归是规范计划的一部分，不是「V1 抬高量变体」：结果只生成一次，
    /// 没有按「从近到远」枚举落点。
    /// </para>
    /// </summary>
    private static int[] ResolveRecursiveLiftPlan(
        string targetColumn,
        int highestCrystal)
    {
        List<int> solids = FindSolidLayers(targetColumn, highestCrystal);
        int[] destinations = solids.ToArray();
        char[] working = targetColumn.ToCharArray();

        for (int index = 0; index <= highestCrystal; index++)
        {
            bool segmentBottom =
                working[index] == ShapeSignature.CrystalCode &&
                index > 0 &&
                working[index - 1] == ShapeSignature.EmptyCode;

            if (!segmentBottom)
            {
                continue;
            }

            int segmentLength = CrystalRunLength(targetColumn, index);
            int gap = index - 1;

            while (gap >= 0)
            {
                int solid = LastSolidBelow(working, gap);

                if (solid < 0)
                {
                    break;
                }

                working[solid] = ShapeSignature.EmptyCode;
                working[gap] = ShapeSignature.SolidCode;

                int solidIndex = solids.IndexOf(solid);

                if (solidIndex >= 0)
                {
                    destinations[solidIndex] = gap;
                }

                // 段长 ≥ 3：停下并记录「环下落」（§11.2）。
                if (segmentLength >= 3 || solid == 0 || !IsEmpty(working, solid - 1))
                {
                    break;
                }

                gap = solid - 1;
            }
        }

        return destinations;
    }

    /// <summary>
    /// 构造 G 的阶梯式提升计划（§4.8.1）：相邻两枚普通格之间留两格临时晶体。
    ///
    /// <para>
    /// 长晶体段（≥3）的环下落需要这个形态：普通格按 <c>T cc T cc T</c> 阶梯排布，
    /// 上一刀留下的临时晶体当下一刀的刹车；G1–G14 全部由它扫描判定。
    /// 这是 G 路线的<b>规范形态</b>，只算一遍，不做多候选筛选。
    /// </para>
    /// </summary>
    private static int[] ResolveStaircaseLiftPlan(
        string targetColumn,
        int highestCrystal)
    {
        List<int> solids = FindSolidLayers(targetColumn, highestCrystal);
        int[] destinations = new int[solids.Count];
        int previous = int.MinValue;

        for (int index = 0; index < solids.Count; index++)
        {
            int layer = solids[index];
            int destination = Math.Max(layer, previous + 3);

            // 不能顶进上方最近的晶体段：留出段底端下面那一格。
            int ceiling = NearestCrystalBottomAbove(targetColumn, layer, highestCrystal);

            destination = Math.Min(destination, ceiling - 1);
            destination = Math.Max(destination, layer);

            if (previous != int.MinValue && destination <= previous)
            {
                destination = previous + 1;
            }

            destinations[index] = destination;
            previous = destination;
        }

        // 最上面的普通格紧贴晶体段底端（§4.8 的 T@3→4 与 §4.8.2 的 T@3→4 都是这一条）。
        int topCeiling = NearestCrystalBottomAbove(
            targetColumn,
            solids.Count == 0 ? highestCrystal : solids[^1],
            highestCrystal);

        if (solids.Count > 0 && topCeiling - 1 > destinations[^1])
        {
            destinations[^1] = topCeiling - 1;
        }

        return destinations;
    }

    /// <summary>
    /// 建核心列（§11.2 的 <c>fill</c>）：目标晶体 + 提升后的普通格 + 填充的临时晶体。
    ///
    /// <para>
    /// 最高晶体**之上**的尾部材料原样保留（它们不参与提升，也不填晶体），
    /// 这样带尾部的列（例如 <c>cT.T.cccT</c>）也能走同一条路线。
    /// </para>
    /// </summary>
    private static char[]? BuildCoreColumn(
        string targetColumn,
        int[] destinations,
        int highestCrystal)
    {
        int cap = targetColumn.Length;
        char[] core = new char[cap];
        Array.Fill(core, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < cap; layer++)
        {
            if (targetColumn[layer] == ShapeSignature.CrystalCode ||
                (layer > highestCrystal &&
                 targetColumn[layer] == ShapeSignature.SolidCode))
            {
                core[layer] = targetColumn[layer];
            }
        }

        List<int> solids = FindSolidLayers(targetColumn, highestCrystal);

        if (solids.Count != destinations.Length)
        {
            return null;
        }

        for (int index = 0; index < solids.Count; index++)
        {
            int destination = destinations[index];

            if (destination < 0 ||
                destination >= highestCrystal ||
                core[destination] != ShapeSignature.EmptyCode)
            {
                return null;
            }

            core[destination] = ShapeSignature.SolidCode;
        }

        // fill：最高晶体以下的所有洞都填成临时晶体（正规核心 ⇒ 紧列）。
        for (int layer = 0; layer < highestCrystal; layer++)
        {
            if (core[layer] == ShapeSignature.EmptyCode)
            {
                core[layer] = ShapeSignature.CrystalCode;
            }
        }

        return core;
    }

    /// <summary>临时晶体块：核心列里比目标列多出来的晶体连通块（<c>Bottom..Top</c>）。</summary>
    private static List<(int Bottom, int Top)> FindTemporaryBlocks(
        string targetColumn,
        char[] core)
    {
        var blocks = new List<(int Bottom, int Top)>();
        int index = 0;

        while (index < core.Length)
        {
            bool temporary =
                core[index] == ShapeSignature.CrystalCode &&
                targetColumn[index] != ShapeSignature.CrystalCode;

            if (!temporary)
            {
                index++;
                continue;
            }

            int start = index;

            while (index < core.Length &&
                   core[index] == ShapeSignature.CrystalCode &&
                   targetColumn[index] != ShapeSignature.CrystalCode)
            {
                index++;
            }

            blocks.Add((start, index - 1));
        }

        return blocks;
    }

    /// <summary>提升记录：按顺序把核心列里的普通格与目标列里的普通格一一对应。</summary>
    private static List<(int Core, int Target)> FindLifts(
        string targetColumn,
        char[] core)
    {
        var targetSolids = new List<int>();
        var coreSolids = new List<int>();

        for (int layer = 0; layer < core.Length; layer++)
        {
            if (targetColumn[layer] == ShapeSignature.SolidCode)
            {
                targetSolids.Add(layer);
            }

            if (core[layer] == ShapeSignature.SolidCode)
            {
                coreSolids.Add(layer);
            }
        }

        var lifts = new List<(int Core, int Target)>();

        for (int index = 0; index < Math.Min(targetSolids.Count, coreSolids.Count); index++)
        {
            if (coreSolids[index] != targetSolids[index])
            {
                lifts.Add((coreSolids[index], targetSolids[index]));
            }
        }

        return lifts;
    }

    /// <summary>
    /// 托柱（§11.2 的 <c>support</c>）：全实心，在被提升格的下落路径层放针。
    ///
    /// <para>
    /// 每条提升 <c>core → target</c> 都让那枚普通格从核心层落回目标层；
    /// 构造 E（§5 规则一）：针不参与横向粘连，所以路径经过的层换成顶针，
    /// 目标层正下方仍是实心，普通格才会停在目标层。
    /// </para>
    /// <para>
    /// 针数、针位全部来自扫描出的提升记录，没有候选、没有模式开关。
    /// 针下方是洞时改为洞（五项条件：针必须有直接支撑）。
    /// </para>
    /// </summary>
    private static string BuildCanonicalSupportColumn(
        string coreText,
        List<(int Core, int Target)> lifts)
    {
        int cap = coreText.Length;
        char[] support = new char[cap];
        Array.Fill(support, ShapeSignature.SolidCode);

        foreach ((int core, int target) in lifts)
        {
            if (core <= target)
            {
                continue;
            }

            for (int layer = target + 1; layer <= core; layer++)
            {
                if (support[layer] == ShapeSignature.SolidCode)
                {
                    support[layer] = ShapeSignature.PinCode;
                }
            }
        }

        // 针必须有直接支撑（五项条件之一）：下方是洞的针换成洞。
        for (int layer = 1; layer < cap; layer++)
        {
            if (support[layer] == ShapeSignature.PinCode &&
                support[layer - 1] == ShapeSignature.EmptyCode)
            {
                support[layer] = ShapeSignature.EmptyCode;
            }
        }

        if (support[0] == ShapeSignature.PinCode)
        {
            support[0] = ShapeSignature.EmptyCode;
        }

        return new string(support);
    }

    #endregion

    #region 真实物理回放（§11.3 / §11.4）

    /// <summary>
    /// 把规范计划（核心列 + 托柱 + 桥柱）用真实 <see cref="ShapePhysics"/> 回放。
    ///
    /// <para>每一步都按 §11.4 的六条核对，任何一条不过就返回 null：</para>
    /// <list type="number">
    /// <item>前态每一列过五项条件；</item>
    /// <item>前态的目标列 ≠ 目标列（防「先做出目标再切柱子」的循环）；</item>
    /// <item>每个中间态稳定；</item>
    /// <item>每一步等于真实 <see cref="ShapePhysics.Cut"/> 的结果；</item>
    /// <item>最终列逐格等于目标；</item>
    /// <item>失败由调用方分级（这里只返回 null，不伪造「不可制作」）。</item>
    /// </list>
    /// </summary>
    private static AngleWorkOrder? ReplayGenericPlan(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        string coreText,
        string supportText,
        List<(int Bottom, int Top)> blocks,
        List<(int Core, int Target)> lifts,
        bool perBlockCuts)
    {
        int parts = target.PartCount;
        int holderColumn = Wrap(column + 1, parts);
        int bridgeColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char crystalColor = CrystalColor(target, column);

        string targetColumn = ColumnTextPadded(target, column, cap);

        /*
         * cuts / order（§11.2）：两种固定形态，不是候选搜索。
         *   ① 一刀全碎：全部临时晶体块一次切掉；
         *   ② 逐块各一刀，从上到下：上一刀的临时晶体留作下一刀的刹车。
         * 桥柱在每个锚层（临时晶体块最高层）各放一颗晶体。
         */
        List<List<(int Bottom, int Top)>> schedule = perBlockCuts
            ? blocks
                .OrderByDescending(block => block.Top)
                .Select(block => new List<(int Bottom, int Top)> { block })
                .ToList()
            : new List<List<(int Bottom, int Top)>> { new(blocks) };

        var steps = new List<AngleWorkStep>();
        ShapeDocument state = BuildThreeColumnState(
            cap,
            parts,
            coreText,
            supportText,
            crystalColor,
            column,
            holderColumn);

        for (int cutIndex = 0; cutIndex < schedule.Count; cutIndex++)
        {
            List<(int Bottom, int Top)> cutBlocks = schedule[cutIndex];

            state = state.Clone();
            WriteColumn(
                state,
                bridgeColumn,
                BridgeColumnText(cap, cutBlocks),
                crystalColor);

            /*
             * 验收条件一：前态每一列都过五项条件。
             * 验收条件二：前态的目标列 ≠ 目标列。
             */
            if (!ShapeSignature.AllColumnsLegal(state, out _) ||
                !ShapeOps.IsStable(state))
            {
                return null;
            }

            if (ShapeSignature.ColumnText(state, column) == targetColumn)
            {
                return null;
            }

            steps.Add(new AngleWorkStep(
                (cutIndex == 0
                    ? "前态：规范核心列（提升 + 临时晶体）+ 托柱 + 桥柱"
                    : "重新装桥柱，准备下一刀") + AnchorsText(cutBlocks),
                state,
                AngleWorkStepKind.PreState));

            CutResult cut = ShapePhysics.Cut(ShapeOps.Rotate(state, rotation));

            if (!cut.Success || cut.West is null)
            {
                return null;
            }

            state = ShapeOps.Rotate(cut.West, -rotation);

            if (!ShapeOps.IsStable(state))
            {
                return null;
            }

            steps.Add(new AngleWorkStep(
                perBlockCuts
                    ? $"第 {cutIndex + 1} 刀：删掉" + AnchorsText(cutBlocks) +
                      "的临时晶体块，West 只保留「目标列 + 托柱」"
                    : "一刀全碎：桥柱把每个临时晶体块接到刀口另一侧，" +
                      "West 只保留「目标列 + 托柱」",
                state,
                AngleWorkStepKind.Cut));
        }

        // 验收条件五：最终列逐格等于目标（类型 / 形状码 / 颜色都一致）。
        if (!MatchesTargetColumn(state, target, column, cap, out _))
        {
            return null;
        }

        var handover = new ShapeDocument(cap, parts);

        for (int layer = 0; layer < cap; layer++)
        {
            handover[layer, column] = state[layer, column].Clone();
        }

        FillPillar(handover, holderColumn);

        if (!ShapeOps.IsStable(handover) ||
            !MatchesTargetColumn(handover, target, column, cap, out _))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "构造 C：托柱换成满高普通柱、拆掉桥柱，目标列逐格不变",
            handover,
            AngleWorkStepKind.Support));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        List<string> limitations = GenericLimitations(
            coreText,
            supportText,
            blocks,
            lifts);

        limitations.Add(
            "辅助列最少化（§5.2）：只保留「支撑（托柱）+ 爆破（桥柱）」两列，" +
            "第三象限整列为空；真实 Cut + 落定逐格核对通过。");

        limitations.Add(
            perBlockCuts
                ? "切割排程（固定形态②）：逐块各一刀、从最上面的块开始；" +
                  "上一刀留下的临时晶体当下一刀的刹车。"
                : "切割排程（固定形态①）：锚层互不相同 ⇒ 一刀全碎（§11.2 的规范 cuts）。");

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.D,
            Algorithm = AngleAlgorithm.Algorithm4,
            Rotation = rotation,
            // §4.8 的布局：目标列 | 托柱 | 桥柱（按环顺序）。
            AuxiliaryColumns = new[] { holderColumn, bridgeColumn },
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>桥柱列：锚层（临时晶体块的最高层）放晶体，其余满普通格。</summary>
    private static string BridgeColumnText(
        int cap,
        IReadOnlyList<(int Bottom, int Top)> blocks)
    {
        char[] bridge = new char[cap];
        Array.Fill(bridge, ShapeSignature.SolidCode);

        foreach ((int _, int top) in blocks)
        {
            if (top >= 0 && top < cap)
            {
                bridge[top] = ShapeSignature.CrystalCode;
            }
        }

        return new string(bridge);
    }

    /// <summary>
    /// 布局：目标列 / 托柱 / 桥柱（§4.8 的两列辅助布局，第三象限留空）。
    /// </summary>
    private static ShapeDocument BuildThreeColumnState(
        int cap,
        int parts,
        string coreText,
        string supportText,
        char crystalColor,
        int column,
        int holderColumn)
    {
        var state = new ShapeDocument(cap, parts);

        WriteColumn(state, column, coreText, crystalColor);
        WriteColumn(state, holderColumn, supportText, crystalColor);

        return state;
    }

    private static string AnchorsText(IReadOnlyList<(int Bottom, int Top)> blocks)
    {
        var layers = new List<string>();

        foreach ((int _, int top) in blocks)
        {
            layers.Add($"第 {top + 1} 层");
        }

        return "（锚层：" + string.Join("、", layers) + "）";
    }

    private static List<string> GenericLimitations(
        string coreText,
        string supportText,
        List<(int Bottom, int Top)> blocks,
        List<(int Core, int Target)> lifts)
    {
        var limitations = BaseLimitations(
            "算法 4（§11 通用实现）：规范计划 = 提升 + 正规核心（临时晶体）+" +
            " 定向碎晶（构造 D）+ 构造 C。计划六个参数全部由扫描得出，" +
            "只生成一次并用真实 ShapePhysics 逐格回放，不做变体搜索。");

        var liftText = lifts.Count == 0
            ? "无（目标列已经是紧列）"
            : string.Join("、", lifts.Select(lift => $"第 {lift.Core + 1} 层 → 第 {lift.Target + 1} 层"));

        limitations.Add($"提升：{liftText}。");
        limitations.Add($"核心列 = {coreText}；托柱 = {supportText}。");
        limitations.Add(
            "临时晶体块：" +
            string.Join(
                "、",
                blocks.Select(block => $"第 {block.Bottom + 1}–{block.Top + 1} 层")) +
            "；桥柱在每个块的锚层（最高层）放一颗晶体。");

        limitations.Add(
            "恢复顺序从上到下：先删上方的临时晶体块，下方还没删的临时晶体当刹车，" +
            "普通格才会停在目标层（§4.9 的实测）。");

        limitations.Add(
            "本档不含末步结晶：Crystallize 是全局操作，会把刚做出来的缺口填平。");

        return limitations;
    }

    #endregion

    #region 列扫描小工具

    /// <summary>把目标列写成 cap 长度的文本（不足的层补空）。</summary>
    private static string ColumnTextPadded(
        ShapeDocument target,
        int column,
        int cap)
    {
        var text = ShapeSignature.ColumnText(target, column);

        return text.Length >= cap ? text[..cap] : text.PadRight(cap, ShapeSignature.EmptyCode);
    }

    /// <summary>最高晶体以下的全部普通格层号（升序）。</summary>
    private static List<int> FindSolidLayers(
        string targetColumn,
        int highestCrystal)
    {
        var solids = new List<int>();

        for (int layer = 0; layer < highestCrystal; layer++)
        {
            if (targetColumn[layer] == ShapeSignature.SolidCode)
            {
                solids.Add(layer);
            }
        }

        return solids;
    }

    /// <summary>某个层上方最近的晶体段底端（沿列向下扫描）。</summary>
    private static int NearestCrystalBottomAbove(
        string targetColumn,
        int layer,
        int highestCrystal)
    {
        for (int probe = layer + 1; probe <= highestCrystal; probe++)
        {
            if (targetColumn[probe] != ShapeSignature.CrystalCode)
            {
                continue;
            }

            int bottom = probe;

            while (bottom > 0 &&
                   targetColumn[bottom - 1] == ShapeSignature.CrystalCode)
            {
                bottom--;
            }

            return bottom;
        }

        return highestCrystal + 1;
    }

    /// <summary>从 start 开始向上的连续晶体长度（段长）。</summary>
    private static int CrystalRunLength(string targetColumn, int start)
    {
        int length = 0;

        for (int layer = start;
             layer < targetColumn.Length &&
             targetColumn[layer] == ShapeSignature.CrystalCode;
             layer++)
        {
            length++;
        }

        return length;
    }

    /// <summary>gap 下方最近的普通格层号；没有时返回 -1。</summary>
    private static int LastSolidBelow(char[] column, int gap)
    {
        for (int layer = gap - 1; layer >= 0; layer--)
        {
            if (column[layer] == ShapeSignature.SolidCode)
            {
                return layer;
            }
        }

        return -1;
    }

    private static bool IsEmpty(char[] column, int layer)
    {
        return layer < 0 ||
               layer >= column.Length ||
               column[layer] == ShapeSignature.EmptyCode;
    }

    #endregion
}
