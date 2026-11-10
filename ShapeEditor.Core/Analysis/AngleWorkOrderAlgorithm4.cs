using System.Diagnostics;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器（续）：n = 4 的「路线 F」——算法 4 的 §11 通用实现。
///
/// <para>
/// 这里不再手写任何前态清单，而是按 <c>docs/angle-work-order-algorithm.md</c> §11 走：
/// </para>
/// <code>
/// CanonicalPlan(route = F, target, column, cap)      // §11.2：六个参数全部由扫描得出
/// foreach θ in Variants(plan):                       // §11.3：有界、字典序
///     order = Replay(θ)                              // 真实 ShapePhysics 逐格回放
///     if order.IsComplete: return order
/// return null                                        // 交给兜底链，最终报「未覆盖」
/// </code>
/// <para>
/// <b>禁止打表</b>：分支谓词只有 D、F、底格是否针、段长、锚层是否相同这类结构谓词，
/// 层号一律由沿列扫描得出（§10）。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>变体搜索的候选回放次数上限（§11.3「预算上限」，建议 ≤ 数百）。</summary>
    private const int GenericReplayBudget = 320;

    /// <summary>单角时间上限（毫秒）：超过就停止枚举，交给兜底链。</summary>
    private const int GenericTimeBudgetMs = 40;

    #region 计划参数

    /// <summary>切割排程（§11.2 的 <c>cuts</c> 参数）。</summary>
    private enum GenericCutOrder
    {
        /// <summary>一刀全碎：桥柱在全部临时晶体块的锚层各放一颗晶体，一次 Cut。</summary>
        OneCut,

        /// <summary>逐块各一刀，从最上面的块开始（§4.9 的实测顺序）。</summary>
        BlocksTopDown,

        /// <summary>逐块各一刀，从最下面的块开始（反例对照）。</summary>
        BlocksBottomUp
    }

    /// <summary>托柱的针位策略（§11.3 的 V3）。</summary>
    private enum GenericSupportMode
    {
        /// <summary>规范值：能靠目标列自身材料刹车时用「下落路径放针」，否则「伙伴 + 洞」。</summary>
        Canonical,

        /// <summary>全实心（不放针、不留洞）。</summary>
        AllSolid,

        /// <summary>全部按「下落路径放针」。</summary>
        AllPinPath
    }

    /// <summary>填充集（§11.3 的 V2）。</summary>
    private enum GenericFillMode
    {
        /// <summary>规范值：最高晶体以下的全部空位（正规核心 ⇒ 紧列）。</summary>
        AllHoles,

        /// <summary>只填「晶体正下方」的洞。</summary>
        CrystalHolesOnly
    }

    /*
     * 规范计划 θ = (lift, fill, support, bridge, cuts, order)（§11.2），
     * 六个参数的生成位置：
     *
     *   lift    ← ResolveCanonicalDestinations / ResolveRecursiveDestinations（沿列扫描）
     *   fill    ← BuildCoreColumn（GenericFillMode）
     *   support ← BuildSupportColumn（HasBrakeBelow + HoldCrystalTower）
     *   bridge  ← BridgeColumnText（锚层 = 临时晶体块的最高层）
     *   cuts    ← CutSchedule（GenericCutOrder）
     *   order   ← 与 cuts 同源：BlocksTopDown / BlocksBottomUp 就是恢复顺序
     *
     * 枚举顺序是「规范值优先、其余字典序」，每个候选都跑一遍真实回放。
     */

    #endregion

    #region 入口

    /// <summary>
    /// 路线 F（n = 4、底格非针、D 不成立、F 成立）的通用实现（§11）。
    ///
    /// <para>
    /// 返回 null 表示「计划 + 变体都没通过回放」，由 <see cref="Build"/> 退回现有兜底链；
    /// 最终失败仍然由上层区分为 <c>Uncovered</c>（计划试完）而不是 <c>NotProducible</c>。
    /// </para>
    /// </summary>
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
         * 而且 §11 的四列布局（核心 / 托柱 / 满高柱 / 桥柱）里没有针位。
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

        var board = Stopwatch.StartNew();
        int replays = 0;

        foreach (int[] destinations in EnumerateLiftPlans(targetColumn, highestCrystal))
        {
            foreach (GenericFillMode fill in new[]
                     {
                         GenericFillMode.AllHoles,
                         GenericFillMode.CrystalHolesOnly
                     })
            {
                char[]? core = BuildCoreColumn(targetColumn, destinations, highestCrystal, fill);

                if (core is null)
                {
                    continue;
                }

                string coreText = new(core);
                List<(int Bottom, int Top)> blocks = FindTemporaryBlocks(targetColumn, core);

                if (blocks.Count == 0)
                {
                    continue;
                }

                List<(int Core, int Target)> lifts = FindLifts(targetColumn, core);

                foreach (GenericCutOrder cuts in new[]
                         {
                             GenericCutOrder.OneCut,
                             GenericCutOrder.BlocksTopDown,
                             GenericCutOrder.BlocksBottomUp
                         })
                {
                    foreach (GenericSupportMode supportMode in new[]
                             {
                                 GenericSupportMode.Canonical,
                                 GenericSupportMode.AllSolid,
                                 GenericSupportMode.AllPinPath
                             })
                    {
                        if (replays >= GenericReplayBudget ||
                            board.ElapsedMilliseconds > GenericTimeBudgetMs)
                        {
                            return null;
                        }

                        replays++;

                        string support = BuildSupportColumn(
                            coreText,
                            lifts,
                            blocks,
                            cuts,
                            supportMode);

                        AngleWorkOrder? order = ReplayGenericPlan(
                            target,
                            column,
                            cap,
                            kind,
                            coreText,
                            support,
                            blocks,
                            lifts,
                            cuts);

                        if (order is not null)
                        {
                            return order;
                        }
                    }
                }
            }
        }

        return null;
    }

    #endregion

    #region §11.2 规范计划的六个参数

    /// <summary>
    /// 规范提升落点（§11.2 的 <c>lift</c>）：全部由扫描得出。
    ///
    /// <para>
    /// 规则（每个「正下方为空的晶体段底端」都要被服务）：
    /// </para>
    /// <list type="number">
    /// <item>晶体段正下方的空格必须由「下方最近的普通格」填上：这段下落就不会把晶体段拖下来；</item>
    /// <item>被提升的普通格要落回它自己的目标层，所以它上方还要留出缓冲：相邻两枚普通格之间
    /// 留 <c>2</c> 格临时晶体（这就是 §4.8.1 的阶梯式抬升：<c>T cc T cc T cc T</c>）；</item>
    /// <item>提升不能越过上方的晶体段底端（否则会顶到晶体）；</item>
    /// <item>最上面那枚普通格必须紧贴晶体段底端（否则晶体段会悬空）。</item>
    /// </list>
    /// <para>
    /// 四条规则都只是沿列扫描 + 相邻关系，没有任何按层数的常量（§10）。
    /// </para>
    /// </summary>
    private static int[] ResolveCanonicalDestinations(
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
    /// §11.2 的「递归提升」变体：只服务晶体段底端，段长 1–2 时继续向下递归。
    ///
    /// <para>
    /// 这是文档条文最直译的版本（§4.9 的 <c>cTccTcTc</c> 就是它），
    /// 但对阶梯式抬升的形态会失败，因此作为 V1 的第二个候选。
    /// </para>
    /// </summary>
    private static int[] ResolveRecursiveDestinations(
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
    /// V1 的候选序列：规范计划 → 递归提升 → 单格微调（有界）。
    /// </summary>
    private static IEnumerable<int[]> EnumerateLiftPlans(
        string targetColumn,
        int highestCrystal)
    {
        int[] canonical = ResolveCanonicalDestinations(targetColumn, highestCrystal);

        yield return canonical;

        int[] recursive = ResolveRecursiveDestinations(targetColumn, highestCrystal);

        if (!recursive.SequenceEqual(canonical))
        {
            yield return recursive;
        }

        // 单格微调：只动一枚普通格，向上/向下各 1–2 层（字典序、有界）。
        for (int index = 0; index < canonical.Length; index++)
        {
            foreach (int delta in new[] { 1, -1, 2, -2 })
            {
                int[] variant = (int[])canonical.Clone();
                variant[index] += delta;

                if (variant[index] < canonical[index] ||
                    variant[^1] > highestCrystal - 1 ||
                    !IsStrictlyIncreasing(variant, firstMinimum: 0))
                {
                    continue;
                }

                yield return variant;
            }
        }
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
        int highestCrystal,
        GenericFillMode fill)
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

        for (int layer = 0; layer < highestCrystal; layer++)
        {
            if (core[layer] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            bool fillHere = fill == GenericFillMode.AllHoles ||
                            (layer + 1 < cap &&
                             targetColumn[layer + 1] == ShapeSignature.CrystalCode);

            if (fillHere)
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
    /// 托柱（§11.2 的 <c>support</c>）：默认全实心，按「要落回哪一层」放针或留洞。
    ///
    /// <para>
    /// 每一处提升 <c>core → target</c> 都要让那枚普通格从核心层掉回目标层：
    /// </para>
    /// <list type="bullet">
    /// <item><b>下落路径放针</b>：目标层下方还有材料当刹车时，只要把路径上的托柱换成顶针
    /// （顶针不参与横向粘连 ⇒ 该层的格子掉下来）；</item>
    /// <item><b>伙伴 + 洞</b>：目标层下方没有刹车（要靠托柱里的同层普通格一起下落来刹车）时，
    /// 在核心层放一枚「伙伴」普通格，并在它正下方留一个洞，让两者一起降一层。</item>
    /// </list>
    /// <para>
    /// 刹车是否存在由「下方那一格的切割顺序」决定：本刀就删掉的临时晶体当不了刹车，
    /// 更晚切的块才当得了（§4.9 的逐块上→下正是靠这个）。
    /// </para>
    /// </summary>
    private static string BuildSupportColumn(
        string coreText,
        List<(int Core, int Target)> lifts,
        List<(int Bottom, int Top)> blocks,
        GenericCutOrder cuts,
        GenericSupportMode mode)
    {
        int cap = coreText.Length;
        char[] support = new char[cap];
        Array.Fill(support, ShapeSignature.SolidCode);

        if (mode == GenericSupportMode.AllSolid)
        {
            return new string(support);
        }

        foreach ((int core, int target) in lifts)
        {
            if (core <= target)
            {
                continue;
            }

            bool pinPath = mode == GenericSupportMode.AllPinPath ||
                           HasBrakeBelow(coreText, target, core, blocks, cuts);

            if (pinPath)
            {
                for (int layer = target + 1; layer <= core; layer++)
                {
                    if (support[layer] == ShapeSignature.SolidCode)
                    {
                        support[layer] = ShapeSignature.PinCode;
                    }
                }
            }
            else
            {
                support[core] = ShapeSignature.SolidCode;

                if (core - 1 > 0)
                {
                    support[core - 1] = ShapeSignature.EmptyCode;
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

        HoldCrystalTower(coreText, support);

        return new string(support);
    }

    /// <summary>目标层下方是否有「这一刀之后还在」的材料当刹车。</summary>
    private static bool HasBrakeBelow(
        string coreText,
        int target,
        int core,
        List<(int Bottom, int Top)> blocks,
        GenericCutOrder cuts)
    {
        if (target <= 0)
        {
            return true;
        }

        int brakeLayer = target - 1;

        // 目标层正下方：先看是不是临时晶体（那要看它属于哪一刀）。
        int brakeCut = BlockCutIndex(blocks, brakeLayer, cuts);

        if (brakeCut < 0)
        {
            // 不是临时晶体：再看它是不是「也要下落的普通格」（那样它会先走一步）。
            brakeCut = LiftCutIndex(coreText, brakeLayer, blocks, cuts);
        }

        if (brakeCut < 0)
        {
            // 目标列自己停在目标层的普通格：永远在。
            return true;
        }

        int ownCut = BlockCutIndex(blocks, core - 1, cuts);

        return brakeCut > ownCut;
    }

    /// <summary>某一层若正好是另一枚被提升普通格的核心层，返回它所在的切割序号。</summary>
    private static int LiftCutIndex(
        string coreText,
        int layer,
        List<(int Bottom, int Top)> blocks,
        GenericCutOrder cuts)
    {
        if (layer < 0 || layer >= coreText.Length)
        {
            return -1;
        }

        if (coreText[layer] != ShapeSignature.SolidCode)
        {
            return -1;
        }

        return BlockCutIndex(blocks, layer - 1, cuts);
    }

    /// <summary>某一层落在哪个临时块里、该块在排程里是第几刀；不在块里返回 -1。</summary>
    private static int BlockCutIndex(
        List<(int Bottom, int Top)> blocks,
        int layer,
        GenericCutOrder cuts)
    {
        for (int index = 0; index < blocks.Count; index++)
        {
            (int bottom, int top) = blocks[index];

            if (layer >= bottom && layer <= top)
            {
                return cuts switch
                {
                    GenericCutOrder.OneCut => 0,
                    GenericCutOrder.BlocksTopDown => blocks.Count - 1 - index,
                    _ => index
                };
            }
        }

        return -1;
    }

    /// <summary>
    /// 顶晶体段的「2-环支撑」：托柱被洞断开时，晶体段失去接地支撑，
    /// 需要在晶体段里放一枚针制造构造 G 那种环（§4.8 的托柱第 12 层就是它）。
    /// </summary>
    private static void HoldCrystalTower(
        string coreText,
        char[] support)
    {
        int highestHole = -1;

        for (int layer = 0; layer < support.Length; layer++)
        {
            if (support[layer] == ShapeSignature.EmptyCode)
            {
                highestHole = layer;
            }
        }

        if (highestHole < 0)
        {
            return;
        }

        int top = -1;

        for (int layer = coreText.Length - 1; layer >= 0; layer--)
        {
            if (coreText[layer] == ShapeSignature.CrystalCode)
            {
                top = layer;
                break;
            }
        }

        if (top < 0)
        {
            return;
        }

        int bottom = top;

        while (bottom > 0 &&
               coreText[bottom - 1] == ShapeSignature.CrystalCode)
        {
            bottom--;
        }

        if (bottom <= highestHole)
        {
            return;
        }

        int middle = bottom + (top - bottom) / 2;

        foreach (int layer in new[] { middle }
                     .Concat(Enumerable.Range(bottom, top - bottom + 1)))
        {
            if (layer <= highestHole ||
                layer >= support.Length ||
                support[layer] != ShapeSignature.SolidCode)
            {
                continue;
            }

            if (layer > 0 && support[layer - 1] == ShapeSignature.EmptyCode)
            {
                continue;
            }

            if (layer + 1 < support.Length &&
                support[layer + 1] == ShapeSignature.EmptyCode)
            {
                continue;
            }

            support[layer] = ShapeSignature.PinCode;

            return;
        }
    }

    #endregion

    #region 真实物理回放（§11.3 / §11.4）

    /// <summary>
    /// 把一份候选（核心列 + 托柱 + 桥柱 + 排程）用真实 <see cref="ShapePhysics"/> 回放。
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
        GenericCutOrder cuts)
    {
        int parts = target.PartCount;
        int holderColumn = Wrap(column + 1, parts);
        int farColumn = Wrap(column + 2, parts);
        int bridgeColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char crystalColor = CrystalColor(target, column);

        List<List<(int Bottom, int Top)>> schedule = CutSchedule(blocks, cuts);

        string targetColumn = ColumnTextPadded(target, column, cap);
        var steps = new List<AngleWorkStep>();
        ShapeDocument state = BuildFourColumnState(
            cap,
            parts,
            coreText,
            supportText,
            crystalColor,
            column,
            holderColumn,
            farColumn);

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
                cutIndex == 0
                    ? "前态：规范核心列（提升 + 临时晶体）+ 托柱 + 满高普通柱 + 桥柱" +
                      AnchorsText(cutBlocks)
                    : "重新装桥柱（" + AnchorsText(cutBlocks) + "），准备下一刀",
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
                cuts == GenericCutOrder.OneCut
                    ? "一刀全碎：桥柱把每个临时晶体块接到刀口另一侧，West 只保留「目标列 + 托柱」"
                    : $"第 {cutIndex + 1} 刀：删掉" + AnchorsText(cutBlocks) +
                      "的临时晶体块，West 只保留「目标列 + 托柱」",
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
            "构造 C：托柱换成满高普通柱、拆掉桥柱与满高柱，目标列逐格不变",
            handover,
            AngleWorkStepKind.Support));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

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
            // §4.8 的四列布局：目标列 | 托柱 | 满高普通柱 | 桥柱（按环顺序）。
            AuxiliaryColumns = new[] { holderColumn, farColumn, bridgeColumn },
            Rank = 0,
            Limitations = GenericLimitations(
                coreText,
                supportText,
                blocks,
                lifts,
                cuts)
        };
    }

    /// <summary>把一份候选切成若干刀（V4）。</summary>
    private static List<List<(int Bottom, int Top)>> CutSchedule(
        List<(int Bottom, int Top)> blocks,
        GenericCutOrder cuts)
    {
        if (cuts == GenericCutOrder.OneCut)
        {
            return new List<List<(int, int)>> { new(blocks) };
        }

        var schedule = new List<List<(int, int)>>();
        var ordered = new List<(int Bottom, int Top)>(blocks);

        if (cuts == GenericCutOrder.BlocksTopDown)
        {
            ordered.Reverse();
        }

        foreach ((int bottom, int top) in ordered)
        {
            schedule.Add(new List<(int, int)> { (bottom, top) });
        }

        return schedule;
    }

    /// <summary>桥柱列：锚层放晶体，其余满普通格。</summary>
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

    /// <summary>四列布局：目标列 / 托柱 / 满高普通柱 / 桥柱（§4.8 的实测布局）。</summary>
    private static ShapeDocument BuildFourColumnState(
        int cap,
        int parts,
        string coreText,
        string supportText,
        char crystalColor,
        int column,
        int holderColumn,
        int farColumn)
    {
        var state = new ShapeDocument(cap, parts);

        WriteColumn(state, column, coreText, crystalColor);
        WriteColumn(state, holderColumn, supportText, crystalColor);
        WriteColumn(state, farColumn, new string(ShapeSignature.SolidCode, cap), crystalColor);

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
        List<(int Core, int Target)> lifts,
        GenericCutOrder cuts)
    {
        var limitations = BaseLimitations(
            "算法 4（§11 通用实现）：规范计划 = 提升 + 正规核心（临时晶体）+" +
            " 定向碎晶（构造 D）+ 构造 C。计划六个参数全部由扫描得出，" +
            "每个候选都用真实 ShapePhysics 逐格回放。");

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
            "；" +
            (cuts == GenericCutOrder.OneCut
                ? "锚层互不相同 ⇒ 一刀全碎。"
                : "按锚层逐块各一刀，" +
                  (cuts == GenericCutOrder.BlocksTopDown ? "从最上面的块开始。" : "从最下面的块开始。")));

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

    private static bool IsStrictlyIncreasing(int[] values, int firstMinimum)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index] < firstMinimum)
            {
                return false;
            }

            if (index > 0 && values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
