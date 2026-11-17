# 残余 47 列：`t[1] ≠ T` 却仍走弱落的列

> 2026-11-17。来源：3–7 层全部 S 列（4861）里，`AngleWorkOrderBuilder` 生成的工序含「弱落」步、
> 且目标第 1 层不是普通格的列。共 **47** 列。

## 0. 为什么值得单独看

按 §0.11.6 的交叉统计，`t[1] = T` 的 1324 列里一次顶针成功 **0** 列——
那条边界（`d = 1` 违反构造 H 的前提 `2 ≤ d`）解释得通。

但这 47 列 **`t[1] ≠ T`**，落点都 `d ≥ 2`，按理应该能一次顶针。
它们仍然走弱落 ⇒ 存在**第二条边界**。

## 1. 清单（按层数、字典序）

| L | 列 | 工序形状 |
|---|---|---|
| 5 | `.TT.c` | `PreState→Pin→WeakDrop→Note→Target` |
| 6 | `.T.T.c` | `PreState→**WeakDrop**→Pin→Note→Target`（弱落在顶针**前**） |
| 6 | `.TT..c` | `PreState→Pin→WeakDrop→Note→Target` |
| 6 | `.TT.c.` | 同上 |
| 6 | `.TT.cc` | 同上 |
| 6 | `.TT.cP` | `PreState→Pin→WeakDrop→Note→Support→Stack→Target` |
| 6 | `.TT.cT` | 同上 |
| 6 | `.TT.Tc` | `PreState→Pin→WeakDrop→Note→Target` |
| 6 | `.TTT.c` | 同上 |
| 7 | `.T..T.c` | `PreState→**WeakDrop**→Pin→Note→Target` |
| 7 | `.T.T..c` | `PreState→**WeakDrop**→Pin→Note→Target` |
| 7 | `.T.T.c.` | `PreState→**WeakDrop**→Pin→Note→Target` |
| 7 | `.T.T.cc` | 同上 |
| 7 | `.T.T.cP` | `PreState→**WeakDrop**→Pin→Note→Support→Stack→Target` |
| 7 | `.T.T.cT` | 同上 |
| 7 | `.TT...c` | `PreState→Pin→WeakDrop→Note→Target` |
| 7 | `.TT..c.` | 同上 |
| 7 | `.TT..cc` | 同上 |
| 7 | `.TT..cP` | `…→Pin→WeakDrop→Note→Support→Stack→Target` |
| 7 | `.TT..cT` | 同上 |
| 7 | `.TT..Tc` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TT.c..` | 同上 |
| 7 | `.TT.c.T` | `…→Pin→WeakDrop→Note→Support→Stack→Target` |
| 7 | `.TT.cc.` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TT.ccc` | 同上 |
| 7 | `.TT.ccP` | `…→Support→Stack→Target` |
| 7 | `.TT.ccT` | 同上 |
| 7 | `.TT.cP.` | 同上 |
| 7 | `.TT.cPP` | `…→Support→Stack→Stack→Target` |
| 7 | `.TT.cPT` | 同上 |
| 7 | `.TT.cT.` | `…→Support→Stack→Target` |
| 7 | `.TT.cTc` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TT.cTP` | `…→Support→Stack→Stack→Target` |
| 7 | `.TT.cTT` | 同上 |
| 7 | `.TT.T.c` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TT.Tc.` | 同上 |
| 7 | `.TT.Tcc` | 同上 |
| 7 | `.TT.TcP` | `…→Support→Stack→Target` |
| 7 | `.TT.TcT` | 同上 |
| 7 | `.TT.TTc` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TTT..c` | 同上 |
| 7 | `.TTT.c.` | 同上 |
| 7 | `.TTT.cc` | 同上 |
| 7 | `.TTT.cP` | `…→Support→Stack→Target` |
| 7 | `.TTT.cT` | 同上 |
| 7 | `.TTT.Tc` | `…→Pin→WeakDrop→Note→Target` |
| 7 | `.TTTT.c` | 同上 |

## 2. 共同特征（观察，未证）

1. **全部以 `.` 开头**（L1 空），且 **L2 或 L3 起就是普通格**；
2. 晶体全部在**顶部附近**（距顶 ≤ 3 层）；
3. 最低晶体下方 **只有 1 个空层**（`D ✘`）—— 也就是说
   它们本应走**算法 4**（`D ✘` ⇒ 递归提升 + 环下落），而不是算法 6；
4. 两段不同的形状：
   - **A 型**：`PreState→Pin→WeakDrop`（弱落在顶针**后**）：`WeakDrop` 在恢复某个被提升的普通格；
   - **B 型**：`PreState→WeakDrop→Pin`（弱落在顶针**前**）：只有 `.T.T.c`、`.T..T.c`、`.T.T..c`、`.T.T.c.`、`.T.T.cc`、`.T.T.cP`、`.T.T.cT` 这 7 列。

## 3. 疑点（已于 2026-11-17 实测否定）

~~猜测：这 47 列 `D ✘`，被错误路由到算法 6 ⇒ 分档 bug。~~

**实测结果：47 列 `D` 全部为真**（`D=47, F=40, D∧F=40, ¬D∧¬F=0`）。

```
.TT.c   D=True  F=True
.T.T.c  D=True  F=False   ← 唯一 D∧¬F 的代表
...
合计 47 列：D 全真；F 真 40 列、假 7 列
```

**所以不是分档 bug**：`D ✔` ⇒ 按论文分档就该走算法 6，
弱落是算法 6 的正当机械（底块提升的逆序恢复 / 提前弱落）。

⇒ 这 47 列是**真正的第二条边界**：`D ✔` 但构造 H 的一次顶针**没有找到前态**。
它们与 `t[1]=T` 那族（`d=1`）不同——这里 `d ≥ 2`，理论上构造 H 应当可行。

**已知其中至少一部分可行**：`.TT.c` 用户指出「可以顶破水晶塔制作，也可以切割碎晶制作」，
所以 47 列并非全都必须弱落。

## 3.1 待查

```
对 47 列逐例：是否存在一个一次顶针前态？
  · 若存在 ⇒ 那几列不构成 O 的边界（是实现没找到前态）
  · 若不存在 ⇒ 与「最低晶体下方只有 1 个空层」这个结构有关，需要单独刻画
```

---

## 5. 补充：727 列「切割」的分类（2026-11-17）

| 项 | 数量 |
|---|---|
| 切割路线合计 | 727 |
| 其中 `L1 = T` | 383 |
| 其中 `L1 ≠ T` | **344** |

**这 344 列：`D = False` 全部、`F = True` 全部**（`¬D ∧ ¬F = 0`）。
样例：`.Tc`、`.Tc.`、`.TTc`、`.Tcc`、`.TTc.`、`.Tc.T`…

⇒ 它们正好是论文分档里的**算法 4 桶**（`D ✘ ∧ F ✔`，递归提升 + 环下落），
而算法 4 的恢复本就是「环下落 / 定向碎晶」= 切割。
**所以 344 列不是 O 的边界，是分档正确的结果。**

另外 383 列 `L1 = T` 的切割，与 210 列 `L1 = T` 的弱落同类（`d = 1` 边界）。

## 6. 汇总（4861 列 = 全部 S 列 ∧ 含晶体）

| 桶 | 判据 | 路线 | 列数 | 是否 O 的边界 |
|---|---|---|---|---|
| 算法 6 | `D ✔` | 一次顶针 | 3877 | — |
| 算法 6 | `D ✔` | 弱落 | **47** | **待查（唯一未解释）** |
| 算法 4 | `D ✘ ∧ F ✔` | 切割 | 344 | 否（分档正确） |
| 任意 | `L1 = T`（`d=1`） | 切割 383 + 弱落 210 | 593 | **是（构造 H 前提 2≤d）** |

合计 3877 + 47 + 344 + 593 = 4861 ✔

---

## 7. 四个样例的实测（2026-11-17，用户给前态）

全部验证通过（真实 `ShapePhysics` 回放）：

| 目标 | A | B | E | N | 操作 | 结果 |
|---|---|---|---|---|---|---|
| `.TT.c` | `.TTc.` | `cTT..` | `ccccc` | `TPPT.` | 顶针 | ✔ |
| `.TT.c` | `cTcTc` | `TTTPT` | 空 | `cTc..` | 半破 | ✔ |
| `.T.T.c` | `.T.Tc.` | `cTTT..` | `cccccc` | `TPPPT.` | 顶针 | ✔ |
| `.TTT.c` | `.TTTc.` | `cTTT..` | `cccccc` | `TPPPT.` | 顶针 | ✔ |
| `.TTT.c` | `cTTcTc` | `TTTTPT` | 空 | `cTTc..` | 半破 | ✔ |
| `.TT.Tc` | `.TTTc.` | `cTTT..` | `cccccc` | `TPPTT.` | 顶针 | ✔ |
| `.TT.Tc` | `cTTcTc` | `TTTTTT` | 空 | `cTTc..` | 半破 | ✔ |

### 7.1 顶针前态的共同结构

```
A = 目标列的【晶体下移一格】，再按 P1 把晶体正下方补成普通格
     .TT.c   (.,T,T,.,c)      → A = .TTc.    （晶体 5→4）
     .T.T.c  (.,T,.,T,.,c)    → A = .T.Tc.   （晶体 6→5）
     .TTT.c  (.,T,T,T,.,c)    → A = .TTTc.   （晶体 6→5）
     .TT.Tc  (.,T,T,.,T,c)    → A = .TTTc.   （晶体 6→5，原 T@5 落到 4）

B = cT…（第 1 层放晶体，其余普通格）
E = 满高纯晶体柱
N = 【从第 2 层起连续放针】，针的个数 = 晶体个数 + 1
```

### 7.2 与本算法公式的偏差

按 §0.3 的公式，`N[z] = (z+1∈J) ? 针 : 普通格`，J 来自下降表；
但这四个样例里 **N 的针是「从 L2 起连续一串」**，与 J 并不对应
（例：`.TT.c` 的 N=`TPPT.` ⇒ 针在 2、3，而 I 只有 `{2}`）。

⇒ 这 47 列的前态**不能用 §0.3 的公式直接推出**，需要一个额外规则：
**N 的针型是「底部一段连续针」**，长度 = 该列要下降的普通格个数 + 1。

### 7.3 结论

- 这 4 个样例**都存在一次顶针解** ⇒ 实现用弱落是「没找到」，不是「做不到」；
- 但通用的推导规则还差最后一块：**N 的连续针段长度怎么定**（7.2）；
- 半破解也存在（4 例全有），且更短（`cTcTc` 一刀）。
