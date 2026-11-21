using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 物理算子对齐基准（反编译源码的 semantics，见 docs/reasoner-repo-notes.md §7）：
/// 四条核对各一个用例，断言的都是「基准行为」，而不是当前实现的写法。
///
/// <list type="number">
/// <item><see cref="PinDropsAllTopCellsAndWholeFusedGroup"/>：
///       Pin 丢弃 <c>z == height-1</c> 的所有格，并整组删除含顶层晶体的晶体融合组；</item>
/// <item><see cref="PinUsesPreDeleteBottomMask"/>：
///       Pin 的针按【删除前】底层掩码补；</item>
/// <item><see cref="StackSettlesOnceWithoutPreDeletingTopCrystals"/>：
///       Stack 把上料抬上去后只统一结算一次、不预删上料晶体；</item>
/// <item><see cref="ExchangeDoesNotSettleAfterMerge"/>：
///       Exchange 交叉合并后不再跑重力（输出就是两半的并集）。</item>
/// </list>
/// </summary>
internal static class PhysicsBaselineAlignTests
{
    #region 核对 1：Pin 删掉所有 z == height-1 的格 + 整组

    /// <summary>
    /// 基准（§7.2）：
    /// <code>
    /// fused    = 晶体融合组(original)                    # 删除前算
    /// vanished = ∪{ group | 组内有格在 z == height-1 }
    /// cells    = { (z+1,q) | z &lt; height-1 且 (z,q) ∉ vanished }
    /// cells   += { (0,q) | 原 z == 0 的列 }
    /// collapse(cells)
    /// </code>
    ///
    /// 本图（height = 3，n = 4）：
    /// <code>
    /// z=2:  c T . c      ← (2,0)、(2,3) 晶体触顶；(2,1) 普通格触顶
    /// z=1:  c . T c      ← (0,0)(1,0) 晶体柱；(1,3) 晶体与 (1,2) 普通格横向相邻
    /// z=0:  c . T .
    /// </code>
    /// 基准结果：
    /// <code>
    /// z=2:  . . T .      ← 触顶格被丢弃；(2,2) 是 (1,2) 上移后的普通格
    /// z=1:  . . T .      ← (0,2) 的普通格上移一层
    /// z=0:  P . P .      ← 针按删前底层掩码补（第 0、2 列）
    /// </code>
    /// 三个关键点：
    /// ① 顶层的普通格 (2,1) 必须消失，不能落到更低的层；
    /// ② 触顶晶体的<b>晶体融合组</b>整组消失：第 0 列第 1、0 层的晶体不能作为「残余晶体」留下；
    /// ③ 融合组只含晶体：第 1 层与 (1,3) 晶体<b>横向相邻</b>的普通格 (1,2)
    ///    不属于晶体融合组，必须保留并上移到第 2 层（若把普通格也算进融合组，
    ///    这里就会被误删、(2,2) 变成空）。
    /// </summary>
    public static void PinDropsAllTopCellsAndWholeFusedGroup()
    {
        var document = new ShapeDocument(3, 4);

        document[0, 0] = ShapeCell.Crystal('r');
        document[1, 0] = ShapeCell.Crystal('r');

        document[0, 2] = ShapeCell.Shape('C', 'b');
        document[1, 2] = ShapeCell.Shape('C', 'g');

        document[2, 0] = ShapeCell.Crystal('r');

        // 顶层普通格：被丢弃后不能残留到更低的层。
        document[2, 1] = ShapeCell.Shape('C', 'y');

        // 第 3 列的晶体组触顶；它只按“晶体”连通，
        // 不把第 1 层横向相邻的普通格 (1,2) 也算进融合组。
        document[1, 3] = ShapeCell.Crystal('r');
        document[2, 3] = ShapeCell.Crystal('r');

        PhysicsResult result = ShapePhysics.Pin(document);

        Assert.True(result.Success, result.Error ?? "顶针失败");

        ShapeDocument pinned = result.Document!;

        Assert.Equal(3, pinned.LayerCount);

        // ① 顶层普通格被丢弃：不能落到第 0/1 层。
        for (int layer = 0; layer < pinned.LayerCount; layer++)
        {
            Assert.Equal(CellType.Empty, pinned[layer, 1].Type);
        }

        // ② 触顶晶体的整组消失：第 0 列第 1、0 层的晶体必须一起消失。
        Assert.Equal(CellType.Empty, pinned[1, 0].Type);
        Assert.Equal(CellType.Empty, pinned[2, 0].Type);

        // 第 3 列的触顶晶体组也整组消失。
        Assert.Equal(CellType.Empty, pinned[1, 3].Type);
        Assert.Equal(CellType.Empty, pinned[2, 3].Type);

        // 整张图里不应再有晶体。
        for (int layer = 0; layer < pinned.LayerCount; layer++)
        {
            for (int part = 0; part < pinned.PartCount; part++)
            {
                Assert.NotEqual(
                    CellType.Crystal,
                    pinned[layer, part].Type);
            }
        }

        // 针按删前底层掩码补：第 0 列（晶体）与第 2 列（普通格）各一枚。
        Assert.Equal(CellType.Pin, pinned[0, 0].Type);
        Assert.Equal(CellType.Pin, pinned[0, 2].Type);

        // 第 2 列的普通格整体上移一层，落到针上。
        Assert.Equal(CellType.Shape, pinned[1, 2].Type);
        Assert.Equal('C', pinned[1, 2].ShapeCode);
        Assert.Equal('b', pinned[1, 2].ColorCode);

        // ③ 与触顶晶体横向相邻的普通格不在融合组里：它必须还在，并上移一层。
        Assert.Equal(CellType.Shape, pinned[2, 2].Type);
        Assert.Equal('C', pinned[2, 2].ShapeCode);
        Assert.Equal('g', pinned[2, 2].ColorCode);
    }

    #endregion

    #region 核对 2：Pin 的针按【删前】底层掩码补

    /// <summary>
    /// 基准（§7.2）：<c>cells += { (0,q) | 原 z == 0 的列 }</c>——
    /// 针只看<b>删除前</b>的第 0 层，不看删除后的结果。
    ///
    /// 本图（height = 2，n = 4）：
    /// <code>
    /// z=1:  c . . .      ← 触顶晶体
    /// z=0:  c . . .      ← 同一晶体柱的底格：(0,0) 会随整组一起被删
    /// </code>
    /// 基准结果：<c>z=0</c> 仍然补一枚针（因为删前 (0,0) 非空），
    /// 上面什么都不剩。若针改成按「删除后」的掩码补，这里就一枚针都没有。
    /// </summary>
    public static void PinUsesPreDeleteBottomMask()
    {
        var document = new ShapeDocument(2, 4);

        document[0, 0] = ShapeCell.Crystal('r');
        document[1, 0] = ShapeCell.Crystal('r');

        PhysicsResult result = ShapePhysics.Pin(document);

        Assert.True(result.Success, result.Error ?? "顶针失败");

        ShapeDocument pinned = result.Document!;

        Assert.Equal(2, pinned.LayerCount);

        // 删前 z == 0 非空 ⇒ 补针。
        Assert.Equal(CellType.Pin, pinned[0, 0].Type);

        // 触顶组整组删除：第 1 层不应残留晶体或任何格。
        for (int layer = 0; layer < pinned.LayerCount; layer++)
        {
            Assert.NotEqual(CellType.Crystal, pinned[layer, 0].Type);
        }

        Assert.Equal(CellType.Empty, pinned[1, 0].Type);

        // 其余列本来就是空的：针不能凭空冒出来。
        for (int part = 1; part < pinned.PartCount; part++)
        {
            Assert.Equal(CellType.Empty, pinned[0, part].Type);
        }
    }

    #endregion

    #region 核对 3：Stack 统一结算一次、不预删上料晶体

    /// <summary>
    /// 基准（§7.4）：上料整体抬到 <c>z + height + 1</c>，统一结算一次并截高，
    /// <b>不预删上料的晶体</b>。抬高多少是整体平移，落点由落点公式决定；
    /// 真正要钉住的是「上料原样参与唯一的一次结算」。
    ///
    /// 本图（heightLimit = 4，n = 4）：
    /// <code>
    /// 下方（2 层）：z=0 晶体；z=1 晶体（竖直融合、受支撑）
    /// 上料（2 层）：z=0 晶体；z=1 普通格
    /// </code>
    /// 基准结果：上料的晶体和下方晶体之间隔着空层（或抬到很高），
    /// 它是<b>下落组</b>的晶体 ⇒ 下落消晶；上料第 1 层的普通格落到下方晶体顶上。
    /// <code>
    /// z=2:  T          ← 上料的普通格
    /// z=1:  c          ← 下方晶体
    /// z=0:  c          ← 下方晶体
    /// </code>
    /// 若把上料直接贴在下方（不留空层、不抬高），两颗晶体会先融合成受支撑的一组，
    /// 上料的晶体就会留下来——那不是基准行为。
    /// </summary>
    public static void StackSettlesOnceWithoutPreDeletingTopCrystals()
    {
        var baseDocument = new ShapeDocument(2, 4);

        baseDocument[0, 0] = ShapeCell.Crystal('r');
        baseDocument[1, 0] = ShapeCell.Crystal('r');

        var topDocument = new ShapeDocument(2, 4);

        topDocument[0, 0] = ShapeCell.Crystal('b');
        topDocument[1, 0] = ShapeCell.Shape('C', 'y');

        PhysicsResult result = ShapePhysics.Stack(
            baseDocument,
            topDocument,
            4);

        Assert.True(result.Success, result.Error ?? "堆叠失败");

        ShapeDocument merged = result.Document!;

        Assert.Equal(4, merged.LayerCount);

        Assert.Equal(CellType.Crystal, merged[0, 0].Type);
        Assert.Equal(CellType.Crystal, merged[1, 0].Type);

        // 上料晶体在唯一的一次结算里就是下落组晶体：消晶，不能留下来。
        Assert.Equal(CellType.Empty, merged[3, 0].Type);

        // 上料普通格按落点公式停在下方晶体顶上。
        Assert.Equal(CellType.Shape, merged[2, 0].Type);
        Assert.Equal('C', merged[2, 0].ShapeCode);
        Assert.Equal('y', merged[2, 0].ColorCode);

        for (int part = 1; part < merged.PartCount; part++)
        {
            for (int layer = 0; layer < merged.LayerCount; layer++)
            {
                Assert.Equal(CellType.Empty, merged[layer, part].Type);
            }
        }
    }

    #endregion

    #region 核对 4：Exchange 合并后不再跑重力

    /// <summary>
    /// 基准（§7.5）：<c>前半A/后半A ← cut(A)</c>、<c>前半B/后半B ← cut(B)</c>、
    /// 交叉合并，<b>合并后不再额外结算重力</b>。
    ///
    /// 本图（height = 3，n = 4，刀口固定在 n/2 = 2）：
    /// <code>
    /// A:   z=1:  . T . .        B:   z=1:  . . c .
    ///      z=0:  T c . .             z=0:  . . c R
    /// </code>
    /// A 的西半在刀口（part 1）是晶体，B 的东半在刀口（part 2）也是晶体；
    /// 交叉合并后它们在 part 1–2 之间<b>正好产生新的侧向粘连</b>（融合成一块晶体）。
    /// 基准结果就是两半的并集：
    /// <code>
    /// z=1:  . T c .      ← (1,1) 普通格；(1,2) 晶体
    /// z=0:  T c c R      ← (0,1) 与 (0,2) 两颗晶体跨刀口融合
    /// </code>
    /// 断言输出逐格等于 <c>Merge(Cut(A).West, Cut(B).East)</c>：
    /// 一旦合并后又跑了 Collapse / 碎晶 / 再切，这个并集就不再成立。
    /// </summary>
    public static void ExchangeDoesNotSettleAfterMerge()
    {
        var first = new ShapeDocument(3, 4);

        first[0, 0] = ShapeCell.Shape('C', 'r');
        first[0, 1] = ShapeCell.Crystal('y');
        first[1, 1] = ShapeCell.Shape('C', 'g');

        var second = new ShapeDocument(3, 4);

        second[0, 2] = ShapeCell.Crystal('y');
        second[1, 2] = ShapeCell.Crystal('y');
        second[0, 3] = ShapeCell.Shape('R', 'r');

        CutResult firstCut = ShapePhysics.Cut(first);
        CutResult secondCut = ShapePhysics.Cut(second);

        Assert.True(firstCut.Success, firstCut.Error ?? "A 切割失败");
        Assert.True(secondCut.Success, secondCut.Error ?? "B 切割失败");

        ShapeDocument pureMerge = ShapeExchange.Merge(
            firstCut.West!,
            secondCut.East!)!;

        (ShapeDocument? other, ShapeDocument? output, string? error) =
            ShapeExchange.Exchange(first, second);

        Assert.True(other is not null && output is not null, error ?? "交换失败");

        // 输出就是两半的并集：合并之后再结算 / 再切都会破坏这条等式。
        for (int layer = 0; layer < output!.LayerCount; layer++)
        {
            for (int part = 0; part < output.PartCount; part++)
            {
                Assert.Equal(
                    pureMerge[layer, part].Type,
                    output[layer, part].Type);

                Assert.Equal(
                    pureMerge[layer, part].ShapeCode,
                    output[layer, part].ShapeCode);

                Assert.Equal(
                    pureMerge[layer, part].ColorCode,
                    output[layer, part].ColorCode);
            }
        }

        // 新的侧向粘连确实存在：part 1 与 part 2 的晶体都还在、且跨刀口相邻。
        Assert.Equal(CellType.Crystal, output[0, 1].Type);
        Assert.Equal(CellType.Crystal, output[0, 2].Type);
        Assert.Equal(CellType.Crystal, output[1, 2].Type);

        // 合并后没有多掉 / 少掉任何一格。
        Assert.Equal(CellType.Shape, output[0, 0].Type);
        Assert.Equal('C', output[0, 0].ShapeCode);
        Assert.Equal(CellType.Shape, output[1, 1].Type);
        Assert.Equal('C', output[1, 1].ShapeCode);
        Assert.Equal(CellType.Shape, output[0, 3].Type);
        Assert.Equal('R', output[0, 3].ShapeCode);

        // 交叉的另一路（A 东 + B 西）应该是空的，避免拿错输出蒙对。
        for (int layer = 0; layer < other!.LayerCount; layer++)
        {
            for (int part = 0; part < other.PartCount; part++)
            {
                Assert.Equal(CellType.Empty, other[layer, part].Type);
            }
        }
    }

    #endregion
}
