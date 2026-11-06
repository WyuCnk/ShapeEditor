# 制作路径：交换的旋转与柱子规则（规格草案）

> **⚠️ 本文件已并入 [`production-path-spec.md`](production-path-spec.md)**：交换链部分对应那份文档的 §4。
> 这里保留作推导过程的历史记录；两者冲突时以 `production-path-spec.md` 为准。

> 状态：**交换阶段已定稿**，顶针/堆叠阶段待定。
> 依据：`docs/production-path-exchange-prompt.md` 的两个样例 + 6 象限已确认样例
> （`docs/analysis-production-path.md`）+ 开发者的两条口头约定。
> 文中所有样例都在 `ShapeEditor.Core` 的实际代码上跑通过（n = 6 / 8 / 10 穷举验证）。

## 0. 记号与约定

| 记号 | 含义 |
| --- | --- |
| `n` | 象限数（偶数，2–100） |
| `half` | `n/2` |
| `L` | 层数，`0` 是最底层 |
| `a` | 轴，`0` 起始，等价于「把刀口放在 `a+half` 与 `a+half-1` 之间」 |
| `ls` | 左切点：左半边第一段内容的列数，`1 ≤ ls ≤ half-1` |
| `rs` | 右切点：右半边第一段内容的列数，`1 ≤ rs ≤ half-1` |
| 柱 | 满层 `Cu`（普通形状，颜色 `u`），高度 = 目标层数 |
| 刀口 | 交换的切割线，`ShapePhysics.Cut` 固定切在 `n/2` |

方向记法沿用 `HalfStabilityAnalyzer.Display` 的**完整写法**：

```
(左前半段 + 左后半段) + (右前半段 + 右后半段)        例：(12+34)+(56+78)
```

括号里的数字是**象限编号（1 起，沿环顺序）**，`+` 标出这一半内部的切点。
`(12+34)` ⇒ 左半边第 1 段 2 列（`ls = 2`）、第 2 段 2 列；`(56+78)` ⇒ 右半边 `rs = 2`。
**不接受 `(1+2)+(3+4)` 这类偷懒写法**，一律写全。

## 1. 柱子放在哪一列

**每个半半边只有一根柱子**（即使概念上有两根，交换时只有一根能作为支撑保留，另一根进废料）。
一段内容的柱子放在「本段内容之外、靠本半边外沿的那一列」。

```
P1 = 内容 [a,        a+ls-1]      + 柱 @ (a - 1)      mod n
P2 = 内容 [a+ls,     a+half-1]    + 柱 @ (a + half)   mod n
P3 = 内容 [a+half,   a+half+rs-1] + 柱 @ (a + half-1) mod n
P4 = 内容 [a+half+rs, a-1]        + 柱 @ a            mod n
```

与样例对照（8 象限，`a=0, half=4`）：

| 样例 | 内容 | 柱 | 对应 |
| --- | --- | --- | --- |
| 样例 1 | 象限 1、2 | 象限 8 = `a-1` | **P1** ✔ |
| 样例 2 | 象限 3、4 | 象限 5 = `a+half` | **P2** ✔ |

两个样例都是「左半边」的两片，柱位公式完全吻合。

## 2. 交换前的旋转

**规则一句话：把「内容 + 两侧柱子」的整半转一下，让内部切点正好落在刀口（n/2）上。**

```
P1、P2：各自 顺转 (half - ls)
P3、P4：各自 逆转 rs
交换之后转回来：左 逆转 (half - ls)，右 顺转 rs
```

与 6 象限已确认样例对照（`a=0, half=3, ls=2, rs=1`）：

| 片 | 未旋转 | 旋转后（= 文档里的串） |
| --- | --- | --- |
| P1 | `CrCg------Cu` | 顺转 1 → `CuCrCg------` = `C12000` ✔ |
| P2 | `----CbCu----` | 顺转 1 → `------CbCu--` = `0003C0` ✔ |
| P3 | `----CuCc----` | 逆转 1 → `--CuCc------` = `0C4000` ✔ |
| P4 | `Cu------CmCy` | 逆转 1 → `------CmCyCu` = `00056C` ✔ |

> 文档里 `C34C0000 → 000C34C0` 是转 3 格，已确认。
> 注意那类串是「半边（两柱）」的压缩写法；本规格一律按**半半边（一柱）**建模。

## 3. 切割与交叉拼接

- **切割线固定为 `n/2`**，不是「两根柱子之间的中点」；`ShapePhysics.Cut` 写死，无法改。
- `Cut` 内含：跨刀口的晶体整块破碎 + **两个半边各自再 `Collapse` 一次**。
  ⇒ 交换的输入必须稳定，否则会被塌陷改写。
- 交叉拼接：

```
out1 = Merge(A.东, B.西)      // 通常是废料
out2 = Merge(A.西, B.东)      // 通常是需要的那个
Merge：逐格「左非空取左，否则取右」（重叠时右边内容丢弃）
```

- 前两次交换之后需要转回来（§2）；**末次交换（两半边 → 图形）不需要额外旋转**。

## 4. 成功与废料的判定

- 末次交换的两个输出：**任意一个与目标旋转等价即成功**（旋转等价只在最终产物上判）。
- 另一个输出是废料，柱子和被切掉的内容都进这里。
- **中间步骤不需要「期望图形」**：整条链是确定性构造，每步结果由公式直接算出。
  若要做自检，左半边的期望值是：
  `目标列 [a, a+half-1] + 柱 @(a-1) + 柱 @(a+half)`。

## 5. 伪代码

```
BuildPillarHalfHalves(target, a, ls, rs) -> (P1, P2, P3, P4)
    n ← target.QuadrantCount; half ← n/2
    P1 ← 只保留列 [a .. a+ls-1]            ; 列 (a-1)      置满层 Cu
    P2 ← 只保留列 [a+ls .. a+half-1]       ; 列 (a+half)   置满层 Cu
    P3 ← 只保留列 [a+half .. a+half+rs-1]  ; 列 (a+half-1) 置满层 Cu
    P4 ← 只保留列 [a+half+rs .. a-1]       ; 列 a          置满层 Cu
    P1 ← RotateRight(P1, half - ls);  P2 ← RotateRight(P2, half - ls)
    P3 ← RotateRight(P3, -rs);        P4 ← RotateRight(P4, -rs)
    return (P1, P2, P3, P4)
```

```
Exchange(A, B) -> (out1, out2)
    (Aw, Ae) ← Cut(A)          // 刀口 n/2；内含晶体破碎 + 两半各自 Collapse
    (Bw, Be) ← Cut(B)
    out1 ← Merge(Ae, Bw)
    out2 ← Merge(Aw, Be)
```

```
ParseProductionPath(target) -> 路径 | 失败
    if 目标存在不合法角 → 失败                        // 硬否决（约定：有非法角即不可制作）
    half ← n/2;  if half < 2 → 失败
    for ls ← 1 .. half-1:
      for rs ← 1 .. half-1:
        a ← 0                                        // 轴可被最终旋转等价吸收，不必遍历
        (P1,P2,P3,P4) ← BuildPillarHalfHalves(target, a, ls, rs)
        if 四片里有不稳定的 → continue                 // Cut 会塌陷它们
        (_, L') ← Exchange(P1, P2);  leftHalf  ← RotateRight(L', -(half-ls))
        (_, R') ← Exchange(P3, P4);  rightHalf ← RotateRight(R', rs)
        if leftHalf 或 rightHalf 不稳定 → continue
        (waste, out) ← Exchange(leftHalf, rightHalf)
        if out ≡ target（旋转等价）→ 返回路径
    失败
```

**复杂度**：方向数 `(half-1)²`，每个方向约 7 次 `Collapse`。
n = 20 → 81 个方向 ≈ 570 次塌陷（每次 ≤ 100×20 格），实测量级在毫秒~几十毫秒，不需要剪枝。

## 6. 样例（可直接写成测试）

### 6.1 n = 6，单层，目标 `CrCgCbCcCmCy`，方向 `(12+3)+(4+56)`

```
P1 = CuCrCg------     P2 = ------CbCu--
交换 → out2 = CuCrCgCbCu--        → 逆转 1 → CrCgCbCu--Cu   (123C0C)
P3 = --CuCc------     P4 = ------CmCyCu
交换 → out2 = --CuCbCcCmCu        → 顺转 1 → Cu--CuCcCmCy   (C0C456)
交换(左,右) → out2 = CrCgCbCcCmCy ✓    out1 = Cu--CuCu--Cu（废料）
```

### 6.2 n = 8，单层，目标 `CrCgCbCcCmCyCwCk`，方向 `(12+34)+(56+78)`

```
P1 = CrCg----------Cu   （柱 8）      顺转 2 → --CuCrCg--------
P2 = ----CbCcCu------   （柱 5）      顺转 2 → --------CbCcCu--
交换 → out2 = --CuCrCgCbCcCu--  → 逆转 2 → CrCgCbCcCu----Cu   （左半边）

P3 = ------CuCmCy----   （柱 4）      逆转 2 → --CuCmCy--------
P4 = Cu----------CwCk   （柱 1）      逆转 2 → --------CwCkCu--
交换 → out2 = --CuCmCyCwCkCu--  → 顺转 2 → Cu----CuCmCyCwCk   （右半边）

交换(左,右) → out2 = CrCgCbCcCmCyCwCk ✓   out1 = Cu----CuCu----Cu（废料）
```

### 6.3 退化情形

- `n = 4`：只有 `ls = rs = 1` 一个方向；四片各是「1 列内容 + 1 柱」，同样成立（已实测）。
- `n = 2`：`half = 1`，没有合法切点 ⇒ 不可制作。

## 7. 反例

| 图形 | 否决步骤 | 原因 |
| --- | --- | --- |
| 2 层 2 象限，`[L2] = cr`，其下全空 | 角合法性 | 最低晶体不在底层、下方既无普通形状也非连续顶针（规则四） |
| 8 象限 9 层，文档样例 2（柱在象限 5） | 稳定性 | 象限 3 的 L5–L7 悬空，两片不稳定 → `Cut` 会改写它们 |
| 任意含「跨刀口晶体」且该晶体在目标中必现的图形 | 交换阶段 | 碎晶不算不可制作，但被碎掉的那块内容不会出现在产物里 |

## 8. 现有代码要改的地方

1. `ProductionPathParser.PillarShape`：柱位是对的（每片一柱），但**缺 §2 的预旋转**；
2. `ProductionPathParser.ExchangeMatches`：目前要求**逐格精确相等**（含柱子、含另一半的空列），
   应改成「中间步骤按半边弧内内容比对」+「最终产物按旋转等价比对」；
3. `HalfStabilityAnalyzer.SubStable`：给右半边用的是左半边的柱位
   （固定 `(axis-1+n)%n` 与 `(axis+half)%n`，见 `HalfStabilityAnalyzer.cs:108`、`:125`），
   应改为 `(axis+half-1)` 和 `axis`；这也是「轴 0 的 9 个方向全被判合法」的根因；
4. `ProductionPathParser.Parse`：`Axis` 无需遍历，可固定 0（旋转等价吸收），
   方向数从 `n/2×(half-1)²` 降到 `(half-1)²`；
5. `HalfStabilityResult` 里 `subShapes` 字典是死代码，可删。

## 9. 待确认 / 未定

1. **顶针 / 堆叠**接在交换之后，作用在哪个图形上、补哪些格子，尚未定义（开发者后续给出）。
2. 交换阶段的产物**可能只是目标的一部分**，因此 `ParseProductionPath` 的最终判据还要在顶针/堆叠定下来后
   改为「交换产物 == 目标的某个中间形态」。
3. 目标本身是否必须稳定（否则产物会塌陷）——待确认。
