# 待定接口与替换点

本目录是「制作路径框架」的代码骨架（规格见 `docs/production-path-spec.md` §5.6）。
判据层与交换链已定稿并验证；**只有两个接口是保守实现**，替换点如下。

## 1. `ProductionPipeline.TowerFill(shape)`

**契约**：枚举所有候选前驱 `C`，满足

```
顶针(C) == shape            （用真实 ShapePhysics.Pin 验证）
C 稳定
C 的每一列过五项条件
```

**当前实现（保守）**：只做「直接填充」——

1. 检查收据行：`shape[0][c]` 只能是 `P` 或空，且 `shape[0][c] == P ⟺ shape[1][c] 非空`；
   不满足就直接放弃（这就是需要"抬升"的情形）。
2. 闭式逆：`C[j] = shape[j+1]`（`j = 0..L-2`），顶层留空。
3. 另一个变体：顶层铺满晶体。

**缺什么**：`抬升块后填充`。

- 症状：收据行不满足时，本实现直接返回空集合，图形落到「未覆盖」。
- 第一个失败的例子（claw，4 象限 3 层）：

  ```
  L3  c T P T
  L2  P . P .
  L1  P . P .
  ```

  期望：拆掉 (L3,第3象限) 的顶针得 `D`，再填塔得

  ```
  L3  . . c c
  L2  c T c T
  L1  P . P .
  ```

  该 `X` 稳定、可交换，且 `顶针(X) == D`。
- 第二个例子（二择，4 象限 5 层）见规格 §5.6.2.2，那里的 `X` 需要**把两块抬升一层**。
- 实现提示：抬升的判据是「收据行」——`X[0]` 必须非空当且仅当 `Y[0]` 是针；
  闭式逆给出的 `X₀[0] = Y[1]` 常常不满足，把多余的块抬升一层、让塔填进这些格即可。
  抬升后仍必须满足上面的三条契约。

## 2. `ProductionPipeline.HybridStrips(shape)`

**契约**：枚举 `(D, H)`，满足

```
H 自身可制作（自带 Recipe —— 这就是"可拆"）
Stack(D, H, cap) == shape       （真实物理验证）
```

**当前实现（保守）**：只拆**最顶层的单枚顶针**，并用交换链给这块顶针生成独立
`Recipe`（满足"H 自身可制作"）；交换链做不出来就不枚举。

**缺什么**：

1. **多层 / 多格的块**（老流程是"一层一层剥"，不是一个针一个针地剥）；
2. **二择的候选次序**：同一层可能有多处可拆，拆错要回溯。
   现在的递归**会**回溯（`SolveCore` 遍历候选），但候选集太小，够不到关键分支；
3. **默认规则**（规格 §5.6.2）：顶针可拆就拆；但若该顶针**左右有晶体**则不能拆
   （塔下不来 / 会撞原生晶体），也不能让它下到倒数第三层（角不合法）⇒ 要还回去。

**已知会失败的例子**：二择图形 `--P-P-P-:CuP---Cu:--P-CuCu:cuP---Cu:cuCu----`，
两个可拆方向里只有"拆第四象限最高的图"是对的，而两个单拆之后都还不可交换，
判别必须发生在下游（塔/顶针阶段）——这正是候选集够大 + 回溯才能覆盖的情形。

## 3. 已知 xfail 的测试

| 测试 | 现状 |
|---|---|
| claw（4 象限 3 层）⇒ 应 Producible（rank 1） | 现在 Uncovered，等 `TowerFill` 抬升 |
| 二择（4 象限 5 层）⇒ 应 Producible | 现在 Uncovered，等 `TowerFill` + `HybridStrips` |

其余语料（角判据计数、论文 10 个举例列、n=6 2 层与 3 层的「可交换 ⟺ 交换链」、
n=6 / n=8 交换链逐格复现）**现在就应该全绿**。

## 3.1 已确认的缺陷：角 `.T.c` 被误报成「做不出来」

`.T.c`（即 `-S-c`：空、普通、空、晶体，4 层）**是合法角，且可制作**：

- 五项条件通过（晶体下方有洞，但下方有普通格，条件 4 满足）；
- n=4 的 S 判据：**D 成立**（最低晶体下方有 2 个空格）⇒ 属于 S；
- 具体构造已手工验证（`Probe`）：

  ```
  紧列 cTcc + 相邻满柱   →  打碎 (L1) 与 (L3) 两段临时晶体  →  下落
  ⇒  恰好等于 S 形态（.T.c + 满柱）✔
  ```

  也就是说「填满成紧列 → 定向碎晶 → **全高托柱在每一层托住**」即可。

**当前实现的问题**（`AngleWorkOrderBuilder.Build(shape, 0, cap)` 返回 `IsComplete=false`）：

1. 构造 G 要求「t、t+1、t+2 三连晶」，`.T.c` 只有 1 颗晶体 ⇒ 该分支不适用（这是**未覆盖**，不是不可能）；
2. 填充+暴破把缺口填上后**没有保住托柱的侧向支撑**，普通格直接落地：
   错误信息 `实际列 = T..c`（期望 `.T.c`）就是证据；
3. **与自家求解器矛盾**：同一个 S 形态，`ProductionPipeline.Solve(shape, cap)` 返回 **Producible**，
   而角工序却说做不出来 ⇒ 问题在角工序生成器，不在图形。

**修法**：前态必须保证「碎晶之后，目标列每一格在同一层左右至少有一列是实体」（托柱覆盖全高）；
或者按论文实现 **算法 6（最低晶体下两空）+ 构造 H**。
另外 `AngleWorkOrder.Note` 要区分「未覆盖（还没实现）」与「不可制作」，否则 UI 会误报。

### 3.1.1 `.T.c` 的正解：先造塔 → 顶针 → 换支撑柱（已实测）

用户给出的塔：

```
X = --cgcgCu:CuCucgP-:cr--cgCu:----cg--

L4  .  .  c  .
L3  c  .  c  T
L2  T  T  c  P
L1  .  c  c  T
```

实测结果：

```
X 稳定                     = True
X 可交换                   = True
ProductionPipeline.Solve(X, 4) = Producible, rank 0     ← 交换链直接能造
顶针(X) = --P-P-P-:CuCu--Cu:------P-:cr----Cu
顶针(X) 第 1 列 = .T.c                                  ← 正是目标角
```

所以这一格的路径是**正向**的三步：

```
ExchangeChain.TryBuild(X)（rank 0）→ 顶针 → 构造 C（换一根支撑柱、拆掉辅助列）
```

对应论文：**算法 6（最低晶体下两空）→ 构造 H（一次顶针批量下落）→ 构造 C（支柱交接）**。

注意这条**不是** `TowerFill` 的 L 型逆推：`顶针(X)` 的收据行在列 1、列 3 都不成立
（`Y1=空` 但 `Y2` 非空 / `Y1=P` 但 `Y2` 为空），属于 D 型（塔被顶爆）。
所以框架里不需要为它写逆推，只需要把这个正向构造接进角工序。

> ⚠️ **曾经写错过一条建议（已撤回）**：「角可制作直接借 `Solve` 判」。
> 错在：角必须用文档的算法单独解析，不能借 `Solve`。
> `Solve` 给的是「整图 → 交换链配方」，而角工序要输出的是
> 「列级步骤：辅助列 → 碎晶/顶针 → 换柱 → 目标角」，两者的产物与形态都不同；
> `.T.c` 的 S 形态恰好能被 `Solve` 造只是巧合。
> 造塔 X 可以用**交换链**（见 §5），但判定与步骤编排必须归角算法。

## 4. 不要做的事

- 不要为了通过 xfail 而放宽 `Swappable` / 交换链的判据；
- 不要把「未覆盖」改写成「不可制作」；
- 不要引入按 `n × 层数` 的查表；
- 不要考虑循环支撑结构（本次范围外）。

---

## 5. 角工序与交换链的边界

开发者的结论，按这两句执行：

> **允许**：用**交换链**（`ExchangeChain.TryBuild`）造塔 X——这等价于论文里
> 「先做各列紧列 / 针槽柱，再交换拼装」。
> **不允许**：用 `ProductionPipeline.Solve` 的**判定**来决定角可制作，
> 也不要用它「顺便造 X」。

### 5.1 为什么是 `ExchangeChain` 而不是 `Solve`

| | `ExchangeChain.TryBuild` | `ProductionPipeline.Solve` |
|---|---|---|
| 语义 | **装配**：四个带柱半半边 → 交换 | **整图求解**：分层 A/B → 交换链 → 顶针逆 → 拆 hybrid |
| 会引入顶针吗 | 不会（rank 恒 0） | 可能会——角工序里混进隐藏顶针，**rank 记账就废了** |
| 失败语义 | 这个图形装不出来 | `Uncovered` / `NotProducible`（两回事） |
| 代价 | `(n/2)(half−1)²` 个方向 | 还要再乘上预算、拆块、塔枚举 |

角工序里只能出现「装配」这个原语，不能出现「再想想怎么造」这个求解器。

### 5.2 复用的前提（必须由角算法显式检查）

交换链对每个零件的要求是「**这一列补一根满层柱之后稳定**」。
所以「塔可以用交换链造」不是无条件的，它是下面这条的推论：

```
∀ 列 c：把 c 单独取出并补一根满层 Cu 柱 → Collapse 后不变
```

不满足就退回论文的**列级构造**（构造 A 紧列 / 构造 B 针槽柱 / 构造 C 支柱交接）。

### 5.3 实测（`.T.c` 的塔）

```
X = --cgcgCu:CuCucgP-:cr--cgCu:----cg--

ExchangeChain.TryBuild(X) = True，Replay 校验 = True

  制作左半边零件 1（含柱）  Cu------:CuCu----:Cucr----:Cu------
  制作左半边零件 2（含柱）  ----cgCu:----CuCu:------Cu:------Cu
  交换得到左半边            --cgCuCu:CuCuCuCu:cr--CuCu:----CuCu
  制作右半边零件 1（含柱）  Cucg----:Cucg----:Cucg----:Cucg----
  制作右半边零件 2（含柱）  ----CuCu:----P-Cu:----CuCu:------Cu
  交换得到右半边            CuCucgCu:CuCucgP-:CuCucgCu:CuCucg--
  交换得到目标图形          --cgcgCu:CuCucgP-:cr--cgCu:----cg--   ✔
  废料                      CuCuCuCu:CuCuCuCu:CuCuCuCu:CuCuCuCu

四列补柱后是否稳定：
  列1 .Tc.  补柱 → 稳定 ✔
  列2 cT..  补柱 → 稳定 ✔
  列3 cccc  补柱 → 稳定 ✔
  列4 TPT.  补柱 → 稳定 ✔
```

### 5.4 落到角工序单

```
角工序 =
    [子配方] 造塔 X        ← ExchangeChain.TryBuild(X)；失败 → 构造 A/B 的列级配方
    [步骤]   顶针          ← 一次 ShapePhysics.Pin（构造 H 的那一次）
    [步骤]   构造 C        ← 装新支撑柱、拆旧柱与辅助列
    [步骤]   目标角（S 形态）
```

两条硬要求：

1. **X 的 Recipe 作为子节点嵌进角工序单**（`Recipe.Exchange` → 包 `Pin` → 包裹柱步骤），
   UI 能展开、`Replay.Verify` 能端到端重算；
2. **角工序的 `Rank` 只统计角自己的顶针**——子配方必须 rank 0（交换链），
   这也是不能用 `Solve` 造 X 的原因之一。

### 5.5 一句话

**判定和步骤编排归角算法；把「造一个整图/邻域形态」这件事交给交换链。**
`Solve` 属于整图求解层，角工序调用它就会把两层的语义搅在一起。

---

## 6. 角算法分档（`AngleWorkOrderBuilder` 的实现清单）

按论文选分支，**先选算法再动手**，不要先试「填充+暴破」：

```
角：五项条件 → 不合法即不可制作
├─ 无晶体                         → 构造 C（逐层追加 T/P）
├─ Simple（最高晶体连续接地）      → 构造 A（紧列）+ 构造 C（尾部）
└─ NeedsCrystalBreak
   ├─ n ≥ 6                       → 算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）
   └─ n = 4
      ├─ 底格是针                  → 算法 5（构造 H 一次性批量恢复）
      ├─ D 成立（最低晶体下 ≥2 空） → 算法 6（底块提升 → 正规化 → 构造 H → 构造 C）
      └─ 其余                      → 算法 4（递归提升 + 构造 G 环下落）
```

`.T.c` 落在 **n=4 且 D 成立** ⇒ 算法 6 → 构造 H → 构造 C，
与 §3.1.1 里用户给的塔 X 完全对应。

落地时的检查点：

1. 每个分支都要输出**列级步骤**（辅助列的创建与拆除都要写出），不能只给「前态 + 切一刀」；
2. 每一步用真实 `ShapePhysics` 回放验证，失败要能区分「未覆盖」与「不可制作」；
3. `Rank` 只统计角自己的顶针。

---

## 7. 角的回归用例（当前状态）

五角都是**合法角且属于 S**（判据实测通过）。§7.2–§7.5 落地后的现状：

| 角 | 五项 | D | F | S | 工序 IsComplete | 耗时 | 现状（已实现） |
|---|---|---|---|---|---|---|---|
| `.T.c`（4 层） | ✔ | ✔ | ✔ | ✔ | ✔ | ≈1.1 ms | 算法 6 塔式：`.Tc.` → Pin → 构造 C |
| `T.T.c`（5 层） | ✔ | ✔ | ✔ | ✔ | ✔ | ≈0.9 ms | 算法 6：底块提升 `.TTc.` → Pin → 构造 C |
| `P.T.c`（5 层） | ✔ | ✔ | ✘ | ✔ | ✔ | ≈0.9 ms | 算法 5：底针埋晶 `ccTc.` → Pin → 构造 C |
| `.TT.c`（5 层） | ✔ | ✔ | ✔ | ✔ | ✔ | ≈0.2 ms | 算法 2：两刀构造 D×2 + 构造 E + 构造 C（§7.4） |
| `.T..c`（5 层） | ✔ | ✔ | ✘ | ✔ | ✔ | ≈3 ms | 算法 6：缺口错开层 `..Tc.` → Pin → 构造 C |

> 下面 §7.1–§7.5 保留为推导与实测记录；实现已按 §7.2/§7.4/§7.5 改完，
> 五个角不再用「填充+暴破」造洞。落地细节见 §7.6。

### 7.1 `.TT.c` 是假阳性（前态 = 目标）

实测前态：

```
前态 = --Cu--Cu:CuCu--Cu:CuCu--Cu:--Cu--Cu:crCu--Cu
       前态第 1 列 = .TT.c   目标列 = .TT.c   相同 = True
       前态晶体总数 = 1      目标晶体数 = 1     ← 一颗填充晶体都没有
```

也就是说工序是「**先做出目标本身，再把辅助柱切掉**」——循环，没有任何构造。
它之所以被判成 `IsComplete=true`，是因为验收判据只检查
「前态 → 切 → 下落 == 目标」，而前态等于目标时这个等式**平凡成立**。

同一批里 `.T.c` 与 `.T..c` 的前态晶体数是 6 / 7（真正的塔），说明机制本身能生成塔；
`.TT.c` 是被兜底路径接住的。

**两条修法**：

1. **验收加一条必要条件**：`ColumnText(前态, 目标列) != ColumnText(目标, 目标列)`，
   且前态至少要含一处临时晶体或一次抬升；否则视为循环、不算完成。
2. **耗时**：`.TT.c` 用了 161.76 ms（`.T.c` 的 25 倍、`T.T.c` 的 500 倍），
   时间烧在 `构造 H` 的 support × block × fuse × 列编码 五重枚举上（`AngleWorkOrder.cs:866` 起）。
   应当先用「缺口形态」筛掉不适用的分支，再去枚举。

### 7.2 两个已实测的正解（用户给出）

**`T.T.c`**（算法 6：底块提升 → 构造 H → 恢复）

```
塔 X = --CucrP-:CucrcrP-:CuCucrP-:cr--crCu:----cr--

L5  .  .  c  .
L4  c  .  c  T
L3  T  T  c  P
L2  T  c  c  P
L1  .  T  c  P

X 稳定 ✔  可交换 ✔  每列合法 ✔
X 第 1 列 = .TTc.     ← 正是「底块提升」后的正规形态
顶针(X) 第 1 列 = T.T.c  ✔ 与目标完全一致
```

之后「第二象限堆叠 TT 补支撑」= 算法 6 第五步的恢复 + 构造 C 的追加材料。

**`P.T.c`**（算法 5：底针时的一次批量恢复）

```
塔 X = crcrcrP-:crCucrP-:CuCucrP-:cr--crCu:----cr--

X 第 1 列 = ccTc.     ← 底针位置先用临时晶体顶住（算法 5 第二步）
顶针(X) 第 1 列 = P.T.c  ✔
```

**结论：这两个角都不用碎晶。** 文档的路线是
**抬升 / 临时晶体 → 一次顶针（构造 H）→ 恢复支撑**。

> 早先「按缺口类型分流，能碎的就碎」的建议**已撤回**，理由有实测：
> 缺口贴着晶体下沿时，填充晶体与目标晶体是同一连通块，
> 碎晶必然连带炸掉目标晶体 —— `T.T.c` 两刀模拟的第二刀得到 `T.T..`，目标晶体没了。
> 所以**碎晶不是造洞的主力**；构造 D 的原意是「整段删掉一个已有晶体段」，不是造缺口。

### 7.3 通用规则（修正后）
1. **造洞一律走「抬升 / 临时晶体 + 一次顶针（构造 H）」**，不要指望碎晶造洞；
2. 缺口贴着晶体下沿 ⇒ 必须抬升（碎晶会连带炸掉目标晶体）；
   缺口夹在两个实心格之间 ⇒ 抬升同样可行，且能和其它缺口合并进同一次顶针；
3. **切割的保留侧必须同时含「目标列 + 托柱」**（构造 D 的原话：*保留目标和原普通柱，删去辅助列*）。
   现在 `T.T.c` / `P.T.c` 失败，是因为切完以后普通格失去托柱、直接落地
   （`实际列 = TT..c` 就是普通格从第 3 层掉到第 2 层的证据）；
4. **前态不能包含正在求解的那个角**：`.T..c` 的算法前态第 1 列是 `.T.c.`（含 `.T.c` 子形态），
   「造塔」又要解一次 `.T.c` ⇒ 多一次顶破；用户给的 `..Tc.` 版本可一次成型。
   规则：**目标列的缺口要错开层，用落块柱完成最后一层位移**；
5. **`.TT.c` 的「通过」是假阳性**（§7.1）：验收必须要求前态 ≠ 目标。

### 7.4 `.TT.c` 的完整实测配方（走 算法 2 + 构造 D/E/C，不是算法 6）

**前态**：scaffold `cTcTc` + 托柱 `TTTPT` + 桥柱（第 1 层放晶体）

```
L5  c  T  .  T
L4  T  P  .  T      ← 托柱第 4 层是【顶针】
L3  c  T  .  T
L2  T  T  .  T
L1  c  T  .  c      ← 桥柱第 1 层放晶体

短代码：crCu--cr:CuCu--Cu:crCu--Cu:CuP---Cu:crCu--Cu
```

**第一步**：切割（刀口穿过桥柱晶体）→ 第 1 层的填充晶体碎掉

```
col0 = .TcTc    col1 = TTTPT
短代码：--Cu----:CuCu----:crCu----:CuP-----:crCu----
```

**第二步**：重新装桥柱（第 3 层放晶体）→ 再切一刀 → 第 3 层的填充晶体碎掉，
第 4 层的普通格**失去侧向支撑**（托柱那一层是顶针），落回第 3 层

```
col0 = .TT.c  ✔    col1 = TTTPT
短代码：--Cu----:CuCu----:CuCu----:--Cu----:crCu----
```

**第三步**：构造 C——把托柱换成满高柱，得到目标 S 形态

```
稳定 ✔   col0 = .TT.c   col1 = TTTTT
短代码：--Cu----:CuCu----:CuCu----:--Cu----:crCu----
```

对应论文：**算法 2（临时晶体填成紧列）+ 构造 D ×2（定向碎晶）+ 构造 E（第 s 层放针做弱下落）+ 构造 C（换柱）**。

### 7.5 从这一格提炼的两条通用构造规则

**规则一：要让某格下落一格 ⇒ 把支撑柱在「那一层」换成顶针。**

顶针不参与横向粘连（§1.2），所以该层不提供侧向支撑，目标列那一格就会掉；
其余层仍是实心，其它格照旧被托住。这就是 **构造 E（弱下落）** 的逐层版本：
`TTTPT` 里的 `P` 只让第 4 层那格掉。

**规则二：要在某格正下方造洞 ⇒ 先把该格抬升一层，再填晶体。**

因为晶体纵向融合：填充晶体若直接贴在目标晶体下方，碎它必然连带炸掉目标晶体
（`T.T.c` 的第二刀实测得到 `T.T..`）。
把目标格抬升一层（`cTcTc` 里第 3 层由 `T` 变成 `c`、`T` 挪到第 4 层），
填充晶体就与目标晶体隔开了，可以单独炸。

### 7.6 落地记录（`AngleWorkOrderBuilder`）

| 角 | 所选算法 / 构造 | 步骤链（Kind 序列） |
|---|---|---|
| `.T.c` | 算法 6 / 构造 C | PreState → Pin → Support → Target |
| `T.T.c` | 算法 6 / 构造 C | PreState（`.TTc.` 塔）→ Pin → Support → Target |
| `P.T.c` | 算法 5 / 构造 C | PreState（`ccTc.` 塔）→ Pin → Support → Target |
| `.TT.c` | 算法 2 / 构造 D | PreState → Cut → PreState → Cut → Support → Target |
| `.T..c` | 算法 6 / 构造 C | PreState（`..Tc.` 塔）→ Pin → Support → Target |

- `T.T.c` / `P.T.c`：`TryBuildTowerStyleOrder` 从目标列直接生成
  「角列 + 托针柱 `PPPT.` + 落块柱 `TcT..` + 引爆柱 `ccccc`」，
  先做一次真实 Pin 再构造 C；`T.T.c` 的前态就是 §7.2 用户给的塔。
  `P.T.c` 的前态 `ccTc.` 也来自用户塔（落块柱取 `TcT..` 变体）。
- `.TT.c`：`TryBuildTwoCutCascade` 按 §7.4 装 scaffold `cTcTc` + 托柱 `TTTPT`
  + 桥柱，第一刀删第 1 层临时晶体，第二刀删缺口层临时晶体并让普通格落回，
  最后构造 C。**不再**用单刀填充+暴破。
- `BuildNormalizedTowerColumn` 拆成两层：`BuildShiftedTowerColumn` 只做
  「晶体下移一层」，底层非空的形态再交给 `LiftBottomSolid` 做底块提升。
- 兜底链（填充+暴破 / 多轮 G / F / 通用构造 H）保留，但只作回退；
  五个回归角都不再落到它上面。`Rank` 只统计 Pin 步：塔式为 1，两刀配方为 0
  （托柱里的针是前态的一部分，不是一次 Pin 操作）。

### 7.7 `.T.T.c`（第 6 个回归角）：当前碎晶前态是【非法列】

目标 `.T.T.c` 合法（五项 ✔、D ✔、S ✔，`F` 不成立），**但它现在被解析成碎晶式，前态非法**：

```
目标      .T.T.c     合法 = True
算法前态  cTcT.c     合法 = False    ← 条件五：晶体之间的洞缺少两枚普通资源
                                     （第 3 层与第 6 层是晶体，中间只有 1 枚 T）
```

而工序仍返回 `IsComplete=true` —— 说明**验收没检查前态的列合法性**。
这是继 §7.1「前态 = 目标」之后的**第二个假阳性来源**。

**必须新增的验收条件**（与 §7.1 并列）：

```
前态的每一列都必须通过五项条件
```

**正确做法（用户给出，已实测）—— 算法 6 的塔式路线，不是碎晶**：

```
X = --crcrP-:CuCucrP-:--CucrP-:CuCucrP-:cr--crCu:----cr--

L6  .  .  c  .
L5  c  .  c  T
L4  T  T  c  P
L3  .  T  c  P
L2  T  T  c  P
L1  .  c  c  P

X 稳定 ✔  可交换 ✔  每列合法 ✔
X 第 1 列 = .T.Tc.          ← 目标列「晶体下移一层、缺口封上」
顶针(X) 第 1 列 = .T.T.c    ✔ 与目标完全一致
```

与 `T.T.c` / `P.T.c` / `.T..c` 同族：`BuildShiftedTowerColumn` 生成前态 → 一次 Pin → 构造 C。
按 §7.6 的落地记录，这条路径已经存在，只需要把 `.T.T.c` 接进去并补上前态合法性检查。

### 7.8 n ≥ 6 的角工序已落地（文档 §9 第 1 条）

`TryBuildSixPlusOrder`（`ShapeEditor.Core/Analysis/AngleWorkOrder.cs`）接管
`NeedsCrystalBreak ∧ n ≥ 6` 的全部形态，分档固定为

```
算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）
```

三条算法都输出同一种**列级步骤链**：

```
PreState → Cut → Collapse → Support → Target
```

| 档 | 实现 | 构造 |
|---|---|---|
| 算法 1 | `TryBuildFillBreak(..., LiftOnly, withHandover: true)` | 缺口下方的普通格抬到缺口顶端（§5 规则二），让出的位置填成晶体 |
| 算法 2 | `TryBuildFillBreak(..., DirectOnly, withHandover: true)` | 把晶体塔下的缺口直接填成紧核心 |
| 算法 3 | `TryBuildConstructF` | 构造 F：`N | A | B | E` 四连位受控下落到 d 层 |

**构造 F 的布局**（层号全部由扫描得出，无常量）：

```
针槽柱 N | 目标 A | 辅助柱 B        | 碎晶桥柱 E
第 s 层针 | 目标列 | 第 d..s−1 层晶体 | 第 d 层晶体
其余 T   |        | 其余 T           | 其余 T
```

- `s`：某个晶体段的底端 `t` 减一（`target[s]` 必须是空位）；
- `d`：`s` 以下最高的非空层（必须是普通格）；
- 前态把 `d` 层那枚普通格**抬到** `s` 层、`d` 层让出（规则二 / 规则四）；
- 半破轴 `p = half − 2`，保留侧正好是 `N | A | B`（规则三），E 落在刀口另一侧；
- 切开后 B 的晶体通道整段碎掉，`A[s]` 与 `B[s]` 粘成双格下落，
  `B[d−1]` 的普通格当刹车，双格停在 `d` 层；
- `Support` 步 = 构造 C：装满高新支撑柱、拆掉 `N | B | E`，目标列逐格不变。

`EnumerateDropWindows` 从最高晶体段往下枚举 (s, d)，所以「缺口不在最高晶体下方」
的形态（例如 `.T.cTc`）也能走构造 F；扫描窗口里没有可用窗口时才退兜底链。

**覆盖实测**（单角语料，`Build` 全量枚举）：

| 语料 | 合法列 | 完成 | 未覆盖 |
|---|---|---|---|
| n=6、6 层、{空,T,晶体,顶针} | 1451 | 1451 | 0 |
| n=6、7 层、{空,T,晶体,顶针} | 4328 | 4328 | 0 |
| n=6、9 层、{空,T,晶体} | 7764 | 7764 | 0 |
| n=8、6/7/8 层、{空,T,晶体,顶针} | 1451 / 4328 / 2984 | 全中 | 0 |

这与文档 §1.3 的「n ≥ 6 时 S ⟺ 五项条件」一致：凡是五项条件通过的碎晶角都做得出。
测试见 `SixPlusCornersFollowAlgorithmTiers` / `ConstructFLaysFourColumns` /
`SixPlusLegalColumnsAreAllConstructible` / `SixPlusDistinguishesUncoveredFromNotProducible`。

> 注意：`Cap` 小于目标层数时仍返回「未覆盖：」——那是画布不够，不是做不出来；
> 五项条件不过才返回「不可制作：」。两条失败语义不可互换。

### 7.9 构造 G 已落地（文档 §15 / §9 第 2 条）

**实现位置**：`ShapeEditor.Core/Analysis/AngleWorkOrderConstructG.cs`
（`AngleWorkOrderBuilder` 的新 partial 文件；`Build` 里只多一次分派，旧路线一行未删）。

**路由（路线 F 内部）**：

```
Build → SelectAlgorithm = Algorithm4
      → TryBuildConstructGOrder        ← 首选：§15 单孔环下落（Construction = G）
      → TryBuildGenericFourQuadrantOrder  ← 回退：§11 通用实现（Construction = D）
      → BuildLegacyChain               ← 最后回退：旧的多轮 G / 填充+暴破 / H / F
```

旧路线不是「删掉」，而是降级：G 的 G1–G14 不成立或回放没通过 §15.3 的不变量时，
`TryBuildConstructGOrder` 返回 null，流程照旧落到构造 D。**覆盖率只增不减**（见下表）。

**G1–G14 的判定**（全部由沿列扫描得出，`TryPlanConstructG`；层号一律 0 起）：

| # | 实现里的判定 |
|---|---|
| G1 | 最高下落记录（`q`）正上方 `core[s..s+2]` 三颗都是**目标晶体**（段长 ≥ 3），`s = q+1` |
| G2 | 每条下落记录 `q → d` 的窗口 `d+1 … q−1` 里只有临时晶体 / 空（无针、无目标晶体） |
| G3 | 落点层能腾空：核心列该层是临时晶体 / 本来是空 / 是另一条下落记录的源层（§4.8.1 的阶梯抬升） |
| G4 | 每条记录的闸门 `g = q−1` 切掉后只降一层；多层位移由后续记录接力（§4.8.1 / §15.6） |
| G5 | 环伙伴 B 只由 `T`/`P`/晶体构成（**闸门层放晶体**，辅助列必须是紧列，§17.7）；整列过五项条件 |
| G6 | `B[s+1]` 是针、下方 `B[s]` 是 T（针有直接支撑） |
| G7 | 针层 `s+1 ∈ [s, s+2]`：正是环里那层 |
| G8 | 保险丝层 `k < s`（环下方）；G8a：`target[k]` 不是晶体，核心列该层是临时晶体 |
| G9 | `B[s]=T`、`B[s+1]=P`、`B[s+2]=T`：B 侧 T-P-T 咬住目标侧 c-c-c |
| G10 | 两端都咬合：`target[s]` 与 `target[s+2]` 都是晶体 |
| G11 | 保险丝柱 B′ 全实心，只在每个锚层 `k` 放晶体；`k` = 临时晶体块的**顶层** |
| G12 | 旋转后 B′ 落在被丢弃的一侧（第 3 位），每个 `k` 层与目标列闸门晶体**横向相邻（跨刀口）** |
| G13 | 真实 `Cut` 之后：B′/B 的晶体全没、闸门临时晶体整块没（按晶体颗数核对） |
| G14 | 保留侧（第 0、1 位）= 目标列 + B |

**§15.3 四条不变量的验证**（`ReplayConstructG`，每条都跑真实 `ShapePhysics.Cut`）：

1. **只降一层 / 落到目标层**：`result[d]` 与 `preState[q]` 逐格相同（类型 + 形状码 + 颜色），
   且整列逐格等于目标；
2. **其余材料原位**：`d` 以下、`q` 以上的普通格逐格不变（`ShapeOps.SameCell`）；
3. **只碎辅助晶体**：切割后目标列晶体数 = 目标晶体数，目标里的每颗晶体仍在；B/B′ 晶体为 0；
4. **空位搬回源层**：最高那条记录的源层（环正下方）必须为空
   （它上面就是环锁定的晶体段，没有材料会补进来）。

**验收语料（六例）**：

| 目标 | 层数 | 走 G | 前态四列（col0 \| B \| 满高柱 \| B′） | 步骤链 | 耗时 |
|---|---|---|---|---|---|---|
| `cT.T.ccc` | 8 | ✔ | `cTccTccc` \| `TTTcTTPT` \| `TTTTTTTT` \| `TTTcTTTT` | PreState → Cut → Support → Target | 0.2 ms |
| `T.ccc` | 5 | ✔ | `cTccc` \| `cTTPT` \| `TTTTT` \| `cTTTT` | 同上 | 0.1 ms |
| `cTT.cccT.T.ccc` | 14 | ✔ | `cTT.cccT..Tccc` \| `TTTTTTTTTcTTPT` \| `TTTTTTTTTcTTTT` \| 空 | PreState → Cut → Collapse → Note → Target | 0.3 ms |
| `cT.T.T.T...ccc` | 14 | ✔ | `cTccTccTccTccc` \| `TTTcTTcTTcTTPT` \| 满 T \| `TTTcTTcTTcTTTT` | PreState → Cut → Support → Target | 0.3 ms |
| `cTT..T.c` | 8 | ✘（G1 不成立） | `cTccTcTc` \| `TTTPPTPT` \| 满 T \| `TTTTTcTT` | PreState → Cut → PreState → Cut → Support → Target | 1.1 ms |
| `cT.T.T.c` | 8 | ✘（F ✘） | —（未覆盖） | Note → Note | 0.5 ms |

- `cTT..T.c` 没有 ≥ 3 的连续晶体段 ⇒ G1 不成立 ⇒ 回退构造 D（两刀，见 §4.9）；
- `cT.T.T.c` 的 F 不成立（§4.10）⇒ 构造 G 与 §11 通用实现都在前置谓词处让位，
  兜底链也做不出 ⇒ 仍然只能判「未覆盖」。

**覆盖实测**（4 象限、目标只放第 1 列、`{.,T,c}`、`cap = 层数`、`Build` 全量枚举；
「F ✔」= 走路线 F 且 `RecursiveTerminalF` 成立的子集）：

| 层数 | route F | 其中 F ✔ | 走 G | 走 D | route F 未覆盖 | 其中 F ✔ 未覆盖 |
|---|---|---|---|---|---|---|
| 4 | 9 | 9 | 0 | 9 | 0 | **0** |
| 5 | 31 | 31 | 1 | 30 | 0 | **0** |
| 6 | 97 | 96 | 4 | 92 | 1 | **0** |
| 7 | 293 | 286 | 13 | 273 | 7 | **0** |
| 8 | 867 | 834 | 39 | 795 | 33 | **0** |
| 9 | 2519 | 2392 | 112 | 2280 | 127 | **0** |

「route F 未覆盖」一列全是 **F ✘** 的形态（`cT.T.c` 一族：五项 ✔ 但不是 n = 4 的 S），
与实现 G 之前逐项一致；**F ✔ 子集仍然 0 未覆盖**，覆盖率没有退化。

**用例**：`ConstructGIsStandardRouteForLongCrystalRuns`（六例逐格验收 + 前态布局锚点）、
`ConstructionGMatchesSpecExample`（§15.9 实例）、`Algorithm4GenericPlanCoversAcceptanceCorpus`
（语料构造标记改为「长段 G / G1 不成立回退 D」）。

**文档待更新**（`docs/angle-work-order-algorithm.md`，开发者本人维护，本次未动；
行号按本次阅读时的版本）：

1. **§9 未覆盖第 2 条**（第 585–587 行）：「构造 G 尚未实现」→ 已实现、已实测；
2. **§13.2 构造 G 行**（第 842 行）：「已写算法，未实现」→ 已实现；
3. **§13.3 算法 4 行**（第 852 行）：「已实测（2 例）」→ 补上构造 G 的语料；
4. **§13.4 结论与剩余缺口**（第 860–868 行）：删掉「构造 G 的实现」这条缺口；
5. **§15.8 现状**（第 1055–1063 行）：改成「已实现（`AngleWorkOrderConstructG.cs`）」，
   并把四条实现要点改成现在的扫描顺序；
6. **§15.2 的 G3**（第 981–984 行）：措辞建议改成「落点层 `d` 在目标里是普通格、
   在核心列里能被腾空」，因为实测里 `d` 是**落点**（§15.9 的 `cT.T.ccc` 中 `d` 是第 4 层，不是空层）；
7. **§4.7 末尾的注**（第 252–254 行）：「G 与 D 可互换」→ 现在 G 是首选、D 是回退；
8. 可选：§15.9 的保险丝柱写成短列 `TTTc....`，实现里用满高列 `TTTcTTTT`（等价，回放已验），
   若要逐字对齐可注明「保险丝柱的高度是自由度」。

### 7.10 §17.7 辅助列的洞换晶体 + 前态洞闸门（2026-11）

**任务一：7 个「仅辅助列有洞」案例（≤6 层审计口径）**

洞一律换成**同层晶体**；辅助列全程紧列，半破/引爆时与临时晶体一起碎掉。

| 目标 | 旧的辅助列 | 新的辅助列 | 说明 |
|---|---|---|---|
| `T.ccc` | `.TTPT` | `cTTPT` | 构造 G 的环伙伴 B，闸门在第 1 层 |
| `T.ccc.` | `.TTPTT` | `cTTPTT` | 同上 |
| `T.cccT` | `.TTPTT` | `cTTPTT` | 同上 |
| `T.cccP` | `.TTPTT` | `cTTPTT` | 同上 |
| `P.T..c` | `cT.T..` | `cTcT..` | **不是构造 G**：算法 5 / 构造 C 的落块柱，洞在第 3 层 |
| `TT.ccc` | `T.TTPT` | `TcTTPT` | 构造 G，闸门在第 2 层 |
| `T.cccc` | `.TTPTT` | `cTTPTT` | 构造 G（6 层，cap 必须取 6） |

每个案例都断言：`IsComplete`、末步逐格等于目标、所有辅助列 `IsTightColumn`、
辅助列文本等于上表；构造 G 走真实 `Cut`，`P.T..c` 走真实 `Pin`。

**任务二：§6 第 7 条 / §17.7 的闸门**

- `ShapeSignature.IsTightColumn(string / document, column)`：材料之间夹空层即为假；
  末尾空层、整列为空都算紧列。
- `AngleWorkOrderBuilder.HasIllegalColumnHoles(preState, targetColumn, targetColumnHasSubRecipe, out bad)`：
  - 非目标列有洞 ⇒ **拒绝**（辅助列必须是紧列）；
  - 目标列有洞且 `targetColumnHasSubRecipe == false` ⇒ **拒绝**；
  - 目标列有洞且带子配方 ⇒ 允许。
- `IsRejectedPreState`（候选生成）与 `HasIllegalPreState`（工序单自检）都接这条规则。
- `AngleWorkOrder.TargetColumnHasSubRecipe`：目标列有洞时必须为 true（或 `SubRecipe != null`）。
  生成器在装出前态时置 true（目标列由本工序的子构造给出）；外部伪造的工序单默认 false，
  会被 `HasIllegalPreState` 判成假阳性。
- 塔式构造（算法 5/6 的用户塔）在写辅助列后调用 `TightenAuxiliaryColumn`，
  把候选里带洞的落块柱补成同层晶体，再跑真实 `Pin` 回放。

**审计数字**（≤6 层、4 象限、`{.,T,P,c}`、2125 个 S 列；本仓库口径统计每条 PreState 步）：

| 口径 | PreState 步数 | 全紧 | 仅目标列有洞 | 仅辅助列有洞 | 两者都有 | S 生成成功 |
|---|---|---|---|---|---|---|
| 修复前 | 446 | 375 | 64 | **7** | 0 | 2125 |
| 修复后 | 446 | 382 | 64 | **0** | 0 | 2125 |

> 文档 §17.7 写的是 441 / 375 / 66（59 + 7）；本仓库口径多 5 条 PreState 步
> （`.TT.c`、`T.T..c`、`T..T.c`、`TT.T.c`、`.TTT.c` 这 5 个构造 D 案例各有两条 PreState），
> 分类里的 7 个辅助列案例完全一致。
> 现有测试：`AuxiliaryColumnHolesBecomeCrystals`、`PreStateHoleGateDistinguishesAuxiliaryAndTarget`、
> `PreStateTightnessAudit`。

**断言更新（原因）**：`cTT.cccT.T.ccc` 的双闸门前态（闸门第 3、10 层）若在环伙伴
放闸门晶体，会同时要求第 6 层放针——针上方出现晶体 ⇒ 违反五项条件二，
新构造 G 的候选因此作废；单发 G（`PreState → Cut → Collapse → Note → Target`）
仍能逐格得到目标且辅助列全紧，所以该案例改断言单发 G 的前态布局与步型。
`ReplayGenericAlgorithm4` 相应允许「下落前」的 Cut 快照（再 Collapse 一次核对）。

**文档待更新**（`docs/` 由开发者本人维护，本次未动；行号按本次阅读时的版本）：

1. §17.7 第 1510–1511 行：「全是构造 G，托柱写成 `.TTPT`，L1 留了个洞」→
   实际是 6 个构造 G + 1 个算法 5/构造 C（`P.T..c` 的落块柱 `cT.T..`，洞在第 3 层），
   且 `TT.ccc` 的洞在第 2 层；建议按上表逐例写清；
2. §17.7 第 1474–1480、1502–1510 行的审计数字：441/375/66 与当前仓库口径
   446/382/71（修复前）不同——差在 `.TT.c`、`T.T..c`、`T..T.c`、`TT.T.c`、`.TTT.c`
   这 5 个构造 D 案例各有两条 PreState 步；建议统一口径（是否把多刀工序的第二条 PreState 计入）；
3. §17.7「修法」第 1513–1529 行：改为「已落地」并链接
   `ShapeSignature.IsTightColumn` / `AngleWorkOrderBuilder.HasIllegalColumnHoles` /
   `AngleWorkOrder.TargetColumnHasSubRecipe`；
4. §6 第 7 条的「实现检查」第 631–641 行：补一句「目标列有洞时由
   `TargetColumnHasSubRecipe`（或 `SubRecipe`）判定子配方是否存在」；
5. §5 规则六第 562 行「辅助列一律按紧列生成」：可补「闸门层放晶体而不是洞」的实例
   （`T.ccc` 的 `cTTPT`）。

### 7.11 半边工序（候选 3：临时晶体 + 保险丝 + 一刀，2026-11）

**背景**：`cT.T.cPT`（8 层）五项 ✔、D ✘、F ✘ ⇒ 合法但非 S（论文定理 3 判单角不可做），
但它确实可得——必须与相邻象限一起做。论文算法 8 的两条候选都拒绝这一格，
所以新增第三候选（`docs/half-side-work-order.md` §2 / §4.2）。

**实现位置**：`ShapeEditor.Core/Analysis/AngleWorkOrderHalfSide.cs`
（`AngleWorkOrderBuilder` 的新 partial；`Build` 里只在 n = 4 非 S 分支多一次分派）。
`AngleWorkOrder` 增加可空字段 `Half`；`AngleConstruction` 增加 `Half`。角工序 API 不破坏。

**数据结构**：

```
HalfCandidate { Algo8Self, Algo8Uplift, TempCrystalFuse }   // 候选 1/2 见 §7.12
WorkOrderTarget(Columns, PrimaryColumn)                     // 半边 = 连续 n/2 列
HalfWorkOrder(Target, Candidate, Steps, IsComplete, Note, Limitations, Rank)
AngleWorkOrder.Half : HalfWorkOrder?                        // 默认 null
```

**路由**（`Build` 的 `parts == 4 && enforceFiveConditions && !IsPillarColumn` 分支）：

```
五项 ✘                 → NotProducible
n ≥ 6                  → 现有算法（不变）
n = 4 且 S ✔           → 现有角工序（不变）
n = 4 且 S ✘ 但五项 ✔  → TryBuildHalfOrder(候选 1 → 2 → 3)
                         ├ 任一候选两列回放通过 → 角工序 + Half（Construction = Half）
                         └ 三个候选全失败       → NotProducible（不写成「未覆盖」）
```

**候选 3 的前态（层号全部扫描得出，s = 最高晶体层）**：

| 列 | 规则 |
|---|---|
| Q1 目标列 | 目标在 s−1 必须是洞、s−2 必须是普通格；把 s−2 的普通格提到 s−1，低于 s 的洞全部填临时晶体（整段连续） |
| Q2 环伙伴 | 0…s−3 普通格（s−3 是双格下落的刹车）、s−2 晶体（闸门，碎掉腾出落点）、s−1 普通格（与 Q1 配对）、s…s+2 三连晶（咬合 Q1 的 c-P-T） |
| Q3 | 空 |
| Q4 保险丝 | 0…s−2 普通格 + s−2 一颗晶体（与 Q1 临时晶体同层、跨刀口），其余空 |

**切割轴**：`rotation = Wrap(−column, 4)`，让保留侧（西 = 第 0、1 位）正好是
「目标列 + 环伙伴」，保险丝柱 / 空象限落在被丢弃的一侧（第 2、3 位）。
`Cut` 只碎跨刀口晶体块（临时晶体 + Q2 闸门晶体 + Q4 保险丝），
目标原生晶体不跨刀口、一颗不少；抬升的普通格与 Q2 同层普通格配对只落一层。

**`cT.T.cPT` 的逐步回放（8 层 × 4 象限）**：

```
前态  Q1 cTccTcPT | Q2 TTTcTccc | Q3 ........ | Q4 TTTc....
一刀  Q1 cT.T.cPT ✔ | Q2 TTTT.ccc ✔        （逐格核对类型 / 形状码 / 颜色）
步骤  PreState → Cut → Target（外层）；
      Half.Steps = 前态 → 一刀 → 半边目标（两列）
```

**其它案例**：7–9 层审计里候选 3 命中 4 个合法非 S 列：
`cT.T.cPT`、`cT..T.cPT`、`TcT.T.cPT`、`ccT.T.cPT`（全部逐格通过）。
测试另覆盖 9 / 10 层的 `cT..T.cPT`、`cT...T.cPT`；形态外的 `cT.T.c`、`cT.T.cT`
仍报「不可制作」且不带 `Half`。非 `HalfCandidate.TempCrystalFuse` 的列不伪造 `Half`。

**验收数字（P1 当时；P2 后见 §7.12）**：

| 测试 | 结果 |
|---|---|
| 全量测试 | 149/149 通过（其中 2 项 XFAIL：claw / 二择，保持不变） |
| `AUDIT`（≤6 层 S） | `total=5440 legal=2126 s=2125 success=2125 legalNonS=1 failures=0 falsePositives=0`（未退化） |
| `TIGHT`（§17.7） | `totalS=2125 success=2125 steps=446 tight=382 onlyTarget=64 onlyAux=0 both=0`（未退化） |
| `HALFAUDIT`（7–9 层非 S） | `legalNonS=229 halfComplete=4 notProducible=225 uncovered=0 problems=0` |

`HALFAUDIT` 对每个半边完成的列独立核对：`Half` 非空且 `IsComplete`、候选正确、
前态每列合法 / 紧列 / 稳定、前态≠目标、真实 `Cut` 后两列逐格等于半边目标、末步等于目标。

**文档待更新**（`docs/` 由开发者本人维护，本次未动；行号按本次阅读时的版本）：

1. `docs/half-side-work-order.md` 第 18–19 行（§1 末尾「缺口：算法里没有『两列一起做』的入口」）
   → 已实现（候选 3），候选 1/2 仍在 P2；
2. 同文件 §6 第 181–183 行（落地清单第 3–5 条）：第 3 条「实现三个候选 + 回放验证」→
   候选 3 已完成、候选 1/2 未做；第 4 条「返回类型扩展」→ 已实现（`AngleWorkOrder.Half`）；
   第 5 条（与交换链对接）→ 仍未做；
3. 同文件 §7 结论第 189–194 行 → 改为「候选 3 已落地，`cT.T.cPT` 通过」；
4. `docs/angle-work-order-algorithm.md` 第 691 行（§9 第 4 条「循环支撑结构：本次范围外」）
   → 半边候选 3 已使用「针 ↔ 晶体段」的循环支撑；
5. 同文件 §17.5 第 4 条（第 1451 行）与 §17.6 表「判定收尾」行（第 1465 行）→
   补一句「n = 4 非 S 且五项 ✔ 时先试半边工序；候选不命中才报 NotProducible」；
6. 同文件 §13.3 第 961 行（算法 8「属整图层」）→ 可注明「其候选 3 已由角工序的半边实现」。

### 7.12 半边工序候选 1 / 2（P2：论文算法 8，2026-11）

**背景**：P1 只实现了候选 3（兜底）。P2 按论文优先补齐算法 8 的两条候选，
顺序固定为 **候选 1 → 候选 2 → 候选 3**；每个候选都自己扫描判定、造前态、
用真实 `ShapePhysics` 回放并逐格核对（类型 / 形状码 / 颜色），失败才试下一个。

**实现位置**：`ShapeEditor.Core/Analysis/AngleWorkOrderHalfSide.cs`
（候选派发 `TryBuildHalfOrder` + `TryBuildAlgo8Self` / `TryBuildAlgo8Uplift` /
`TryBuildTempCrystalFuse`；半边装单 `BuildPaperHalfOrder` / `BuildHalfOrder`）。
新公开入口 `AngleWorkOrderBuilder.BuildHalf(half, column, cap)`：不经过 S 闸门，
直接对一段半边跑三个候选，返回第一个通过的 `HalfWorkOrder?`。

**候选 1（`HalfCandidate.Algo8Self`，Q 自身）**：

```
① 第 column 列与 column+1 列都通过 n = 4 的 S 判据（PillarSCriterion：五项 ∧ (D∨F)）
② Q 稳定：真实 Collapse(Q) 逐格等于 Q
③ 通过 ⇒ 前态 = Q 本身；步骤链 PreState → Target（不切割、不下落）
```

两列都是 S ⇒ 各自能按单角工序（目标列 + 托柱）单独做出来再拼合，所以两列都算
「带子配方」的目标列（`HalfWorkOrder.TargetColumnsHaveSubRecipe = true`，允许带洞），
不是辅助列。**路由注意**：`Build` 只在目标列 S ✘ 时进入半边分支，而候选 1 要求
目标列 S ✔，所以候选 1 只能由 `BuildHalf` 这个独立入口命中（定理 6 本身就直接
讨论半边，不经过单角 S 闸门）；`Build` 里它必然失败、继续试候选 2/3。

**候选 2（`HalfCandidate.Algo8Uplift`，联合逆提升）**：

```
① 两列各有晶体，且最高晶体不等高（没有晶体 / 等高 ⇒ 拒绝）
② 取较低那枚，用 PhysicsGrid.BuildGroups 找最大粘连组 G，最低层 b
③ 在 b 以下找最高非空层 a（任一列有材料就算）
④ 要求 a 层两列都是普通格（水平 TT）、a < b−1、a+1…b−1 两列都空
⑤ 把 a 层那对普通格整体提到 b−1，其他格子不动 ⇒ Q′
⑥ Q′ 两列都属于 S，且真实 Collapse(Q′) 逐格等于 Q ⇒ 接受
```

层号全部扫描得出：最高晶体 = 沿列向下找第一个晶体；b = 粘连组最低层；
a = b 以下最高非空层。没有按 `n × 层数` 的查表。前态 Q′ 是「两根 S 列拼合、
即将整体下落」的状态，所以**候选 2 不要求 Q′ 稳定**——一次完整下落就是产物步骤
（步骤链 `PreState(Q′) → Collapse → Target`），验收按「真实下落 == Q」判。

**论文例 3 实测（12 层）**：

```
Q  = cT.T.cT.Tccc | TTTT.cccccPT
Q′ = cT..TcT.Tccc | TTT.TcccccPT     ← 第 4 层 TT 提到第 5 层（b = 6，b−1 = 5）
Collapse(Q′) = Q   ✔（两列逐格，含形状码 / 颜色）
```

**候选顺序与回退**：候选 1 → 候选 2 → 候选 3。三个候选都不适用或回放失败 ⇒
`TryBuildHalfOrder` 返回 null（或候选 3 的未完成诊断），`Build` 统一转成
`NotProducibleBySCriterion`：**全失败一律「不可制作」，不写成「未覆盖」**；
候选 3 的最后一条诊断只放进 `Limitations`，不作为 Note 前缀。

**实测案例表**（全量测试的实际命中）：

| 目标半边 | 入口 | 命中候选 | 步骤链 | 耗时（预热后单次） |
|---|---|---|---|---|
| `cTcccccc` / `cTcccccc`（8 层） | `BuildHalf` | `Algo8Self` | PreState(Q) → Target(Q) | 0.02–0.05 ms |
| `cT.T.cT.Tccc` / `TTTT.cccccPT`（12 层，论文例 3） | `Build` | `Algo8Uplift` | PreState(Q′) → Collapse → Target | 0.03–0.10 ms |
| `cT.T.cPT`（8 层） | `Build` | `TempCrystalFuse`（未回归） | PreState → Cut → Target | 0.07–0.29 ms |
| `cT..T.cPT` / `TcT.T.cPT` / `ccT.T.cPT` | `Build` | `TempCrystalFuse` | PreState → Cut → Target | 0.1–0.3 ms 量级 |

**验收数字（P2 后，本仓库口径）**：

| 测试 | 结果 |
|---|---|
| 全量测试 | **151/151** 通过（其中 2 项 XFAIL：claw / 二择，保持不变） |
| `AUDIT`（≤6 层 S） | `total=5440 legal=2126 s=2125 success=2125 legalNonS=1 failures=0 falsePositives=0`（未退化） |
| `TIGHT`（§17.7） | `totalS=2125 success=2125 steps=446 tight=382 onlyTarget=64 onlyAux=0 both=0`（未退化） |
| `HALFAUDIT`（7–9 层非 S + 两条论文半边） | `legalNonS=229 halfTargets=2 halfComplete=6 notProducible=225 uncovered=0 problems=0`（P1 为 `halfComplete=4`） |

`HALFAUDIT` 现在按候选分别核对：候选 1 检查 Q 稳定 / 一次下落恒等 / 步骤链
`PreState → Target`；候选 2 检查前态确实由「提升一对普通格」得到、真实 `Collapse`
等于 Q、步骤链含 `Collapse`；候选 3 保持真实 `Cut` 回放。三者都核对两列逐格等于
半边目标、`Rank = 0`、外层 `HasIllegalPreState` 为 false。

**七条验收的适用口径**：

1. 前态每列五项 ✔：候选 1/2 的 S 蕴含五项；候选 3 原样；
2. 前态目标列 ≠ 目标：候选 2/3 适用；候选 1 的前态就是 Q（不切割、不循环，
   两列都由 S 单角工序得到），按定理 6 的「Q 自身」语义豁免；
3. 稳定性：候选 1 要求 Q 稳定、候选 3 要求前态稳定；候选 2 的前态是「下落前」，
   由真实 `Collapse` 回放负责（§2.7 的定义即如此）；
4. 真实物理回放：候选 1/2 用 `Collapse`，候选 3 用 `Cut`；
5. 末步逐格等于目标；
6. 失败分级：三个候选全失败统一 `NotProducible`（P2 起不再把候选 3 的
   「形态命中但回放失败」写成未覆盖；诊断移入 `Limitations`）；
7. 前态可造：候选 1/2 两列都是带子配方的 S 列（允许带洞），没有辅助列；
   候选 3 的辅助列仍一律紧列。

**文档待更新**（`docs/` 由开发者本人维护，本次未动；行号按本次阅读时的版本）：

1. `docs/half-side-work-order.md` §3 第 104–114 行：原文「对这一格逐条检查 ⇒ 两个候选
   都拒绝」是 **P1 的事实**；P2 已实现候选 1/2，建议补一句「候选 1 由 `BuildHalf`
   直接触发（`Build` 的非 S 分支命中不了），候选 2 已用论文例 3 实测」；
2. 同文件 §3 第 115–116 行「算法 8 相对定理 6 不完备」保持不变，但补上
   「P2 已按论文条文实现两条候选，不完备性仍由候选 3 兜底」；
3. 同文件 §4.2 第 132–144 行：三个候选的状态从「候选 1/2 待做」改为「全部已实现」，
   并注明候选 1 需要独立半边入口；
4. 同文件 §6 第 177–183 行落地清单第 3 条 → 改为「候选 1/2/3 全部实现并有
   `BuildHalf` 入口」；第 5 条（与交换链对接）仍未做；
5. 同文件 §7 第 189–194 行结论 → 改为「算法 8 的两条候选已落地（候选 2 用论文例 3
   实测），候选 3 继续覆盖 `cT.T.cPT`」；
6. `docs/angle-work-order-algorithm.md` §6 第 614–642 行（七条验收）：第 2、3 条
   需补「半边候选 1 的前态=Q（Q 自身）、候选 2 的前态=下落前的 Q′」这两个例外；
   第 6 条需补「半边候选全失败统一 NotProducible」；
7. 同文件 §7 第 645–661 行工序单结构：`HalfWorkOrder` 新增
   `TargetColumnsHaveSubRecipe`（候选 1/2 两列都是带子配方的 S 列）；
8. 同文件 §13.3 第 961 行（算法 8「属整图层」）→ 改为「算法 8 的候选 1/2 与
   本仓库新增的候选 3 都已在半边工序中实现」；
9. `docs/production-path-spec.md` §2.7 第 224–245 行：条文与实现一致；可补一句
   「候选 1 的判定不经过 `Build` 的 S 闸门，需直接调用半边入口」。
### 7.13 k 列半边（n ≥ 6）与整图装配（P3，落地清单第 5 条，2026-11）

**背景**：P1/P2 的半边工序固定 2 列（n = 4）。P3 把它推广到 **k = n/2 列**
（n = 6 ⇒ 3 列、n = 8 ⇒ 4 列），并接进整图装配层（`docs/half-side-work-order.md`
§6 落地清单第 5 条「与交换链对接」）。

**实现位置**

| 文件 | 改动 |
|---|---|
| `Analysis/AngleWorkOrderHalfSide.cs` | 去掉 `HalfParts = 4 / HalfWidth = 2` 常量，改由 `target.PartCount` 推 k；`HalfProjection`、候选 1/2/3、`HalfColumns`、`BuildHalfPartnerColumn` / `BuildHalfExpectation` 全部按 k 列推广 |
| `Analysis/AngleWorkOrderHalfSide.cs` | `BuildHalf` 增加「半边目标必须就是这份目标的那 k 列」逐格闸门；`HalfWorkOrder` 增加 `Rotation` 字段 |
| `Analysis/Production/HalfAssembly.cs`（新） | 整图半边装配：两条轴 × 两个 k 列半边 → 真实 `Cut` + `Exchange` + 回放自检 |
| `Analysis/Production/Recipe.cs` | 新增 `RecipeKind.HalfAssembly` + `HalfAssemblyDetails`（含两条半边的交换链子配方） |
| `Analysis/Production/Replay.cs` | `HalfAssembly` 节点用真实 `Cut` / `Exchange` / `Collapse` 重算 |
| `Analysis/Production/ProductionPipeline.cs` | `SolveCore` 里「交换链失败 → 半边装配」，失败时把诊断写进 Notes |
| `Analysis/ProductionPathParser.cs` | 新增 `FromRecipe`（按配方出节点链）；半边装配输出「两个半边输入 + 交换 + 废料」 |

**k 列推广（与 2 列版的差异）**

| 环节 | 2 列版（n = 4） | k 列版（n ≥ 6） |
|---|---|---|
| 投影 | 固定取 2 列 | 取从 `column` 起连续 k 列，其余象限置空 |
| S 判据 | `PillarSCriterion`（五项 ∧ (D∨F)） | `IsSColumn`：n ≥ 6 用五项（定理 2） |
| 候选 1 | 两列都 S + Q 稳定 | **k 列都 S + Q 稳定**；`Target.Columns` 列出全部 k 列 |
| 候选 2 | 两列最高晶体不等高；a 层两列都是 T | **k 列最高晶体不全等高**；取最低那枚的粘连组，a 层只要求**存在一对相邻普通格**（水平 TT 即可，不必全列）；`a+1…b−1` 要求**所有列**都空 |
| 候选 3 | 单个临时晶体块 + 一颗保险丝 | 临时晶体按**连通块**分组，**每块顶层各放一颗保险丝**（`TemporaryCrystalBlocks`）；k−2 个其余半边列原样保留并逐格核对；环伙伴仍取半边内部的相邻列 |
| 验收 | 两列逐格 | **k 列逐格**（类型 / 形状码 / 颜色）；辅助列仍必须紧列 |

**实测（k 列）**

| 目标 | 半边 | 候选 | 前态 | 步骤链 | 耗时 |
|---|---|---|---|---|---|
| `cT.T.cPT` + `TTTT.ccc`（n=6，8 层） | 3 列（第 3–5 象限） | `Algo8Self` | = Q | PreState → Target | 0.09 ms |
| `.` + `cT.T.cPT` + `TTTT.ccc` + `.`（n=8，8 层） | 4 列（第 6–8、1 象限） | `Algo8Self` | = Q | PreState → Target | 0.07 ms |
| `cT.T.T.cPT`（n=4，10 层） | 2 列 | `TempCrystalFuse` | `cTcTccTcPT` \| `TTTTTcTccc` \| 空 \| `TTcTTc....` | PreState → Cut → Target | 0.18 ms |
| `cT.T..T.cPT`（n=4，11 层） | 2 列 | `TempCrystalFuse` | `cTcTcccTcPT` \| `TTTTTTcTccc` \| 空 \| `TTcTTTc....` | 同上 | 0.20 ms |

多块保险丝两例的半破结果逐格等于目标（`cT.T.T.cPT` / `cT.T..T.cPT`），
证明「每块顶层一颗保险丝」的一刀多段是有效的。

**装配新路径（`HalfAssembly`）**

```
SolveCore(shape):
    1. ExchangeChain.TryBuild(shape)                     ← 现状，成功即返回（行为不变）
    2. HalfAssembly.TryBuild(shape, cap)                 ← 新路径
         for 轴 a in 0 … half−1:
             L = BuildHalf(shape, a, cap)；R = BuildHalf(shape, a+half, cap)
             两条半边目标必须逐格等于 shape 的两段
             两条输入 A = Rotate(L.前态, −a)、B = Rotate(R.前态, −a) 必须
                 「稳定」且「能被交换链装配」（rank 0 子配方嵌进明细）
             真实 Cut(A).West = 左段、Cut(B).East = 右段
             真实 Exchange(A, B) 的所需输出转回后 = shape
         ├ 成功 → RecipeKind.HalfAssembly（rank 0，两条子配方都是交换链）
         └ 失败 → 继续 TowerFill / HybridStrips / Uncovered（现有语义）
    3. 失败时 Notes 里保留半边装配诊断（哪个轴、哪一侧不可得 / 不可造）
```

- **触发**：`ExchangeChain.TryBuild` 失败之后、`TowerFill` / `HybridStrips` 之前；
  交换链成功时一行都不走，现有 2125 / 446 / 6 等数字全部不变。
- **回放**：`HalfAssembly.Verify` 与 `Replay.Verify` 都用真实
  `ShapePhysics.Cut` / `ShapeExchange.Exchange` / `ShapePhysics.Collapse`
  重算两条半边工序、两条输入、两条子配方与末次交换，逐格比对；只靠判据不算成功。
- **失败分级**：半边不可得 / 输入不可造 ⇒ 不产出配方 ⇒ 仍由管线区分
  `Uncovered`（计划失败）与 `NotProducible`（五项条件失败）；新路径不产生新的
  「不可制作」。
- **诚实要求**：候选 2 的 Q′ 是「下落前」状态，不稳定 ⇒ 不能当机器输入，
  装配层显式拒绝（这会挡住 `cT.T.cPT + TTTT.ccc` 这类 n = 4 目标的半边装配，
  它们的交换链本来就能做）。

**重要发现：n ≥ 6 上「交换链失败但半边可得」不存在（实测 0 反例）**

1. **候选 1 在 n ≥ 6 上支配候选 2 / 3**：S ⟺ 五项条件，而候选 2 的产物是
   `Collapse(Q′)`、候选 3 的产物是 `Cut` 的输出——两者都必然稳定。
   所以只要半边「每列合法 + 稳定」，候选 1 就先命中；候选 2 / 3 只在
   半边列不合法时才可能被考虑，而候选 2 / 3 的前态又要求每列合法
   （候选 3 的洞一旦贴着原生晶体就纵向融合，代码直接拒绝）。
2. **整图层面**：`HalfAssembly` 需要两侧半边「每列合法 + 稳定 + 输入可造」，
   这恰好就是整图交换链的前提；因此新路径**不增加可制作整图**，
   它是同一能力的另一种装配写法 + 失败诊断。

**围栏实测（`HalfAssemblyFenceOnChainFailures`，固定种子随机语料）**

| n | 层数 | 样本 | 合法 | 稳定 | 交换链失败 | 半边装配接住 |
|---|---|---|---|---|---|---|
| 4 | 5 | 6000 | 268 | 185 | 45 | **0** |
| 6 | 4 | 12000 | 515 | 350 | 13 | **0** |
| 8 | 3 | 8000 | 652 | 430 | 2 | **0** |
| 8 | 4 | 20000 | 327 | 169 | 2 | **0** |

另有结构化搜索：n = 6 的 428 个合法稳定半边两两组合（183,184 组）全部交换链成功；
n = 6/8 的 120+ 个链失败样本里，**没有**一个满足「两侧半边各自都能被交换链造出来」。
⇒ 这就是「n=6 与 n=8 各至少 1 个『交换链失败、半边工序成功』整图案例」
**做不出来**的原因（不是没实现，是数学上不存在），详见 §7.13.1。

**n = 6 / n = 8 的半边装配实测（新路径本身可回放）**

| 整图 | n | 是否走半边（管线） | 步骤链 | 耗时 |
|---|---|---|---|---|
| `cT.T.cPT` \| `TTTT.ccc` \| `.` \| `cT.T.cPT` \| `TTTT.ccc` \| `.` | 6 | 否（交换链先成功；`HalfAssembly.TryBuild` 单独调用成功） | 左半边 → 右半边 → 交换 → 废料 | 1.83 ms |
| `.` \| `cT.T.cPT` \| `TTTT.ccc` \| `.` \| `.` \| `cT.T.cPT` \| `TTTT.ccc` \| `.` | 8 | 否（同上） | 同上 | 1.84 ms |

两例的 `LeftRecipe` / `RightRecipe` 都是 rank 0 交换链、`Replay.Verify` 端到端通过、
末次交换的真实输出逐格等于目标；解析器 `ProductionPathParser.FromRecipe` 能展开成
「两个半边输入 + 交换 + 废料」。

**验收数字（P3 后）**

| 测试 | 结果 |
|---|---|
| 全量测试 | **157/157** 通过（其中 2 项 XFAIL：claw / 二择，保持不变） |
| `AUDIT`（≤6 层 S） | `total=5440 legal=2126 s=2125 success=2125 legalNonS=1 failures=0 falsePositives=0`（未退化） |
| `TIGHT`（§17.7） | `totalS=2125 success=2125 steps=446 tight=382 onlyTarget=64 onlyAux=0 both=0`（未退化） |
| `HALFAUDIT`（7–9 层非 S + 论文半边） | `legalNonS=229 halfTargets=2 halfComplete=6 notProducible=225 uncovered=0 problems=0`（未退化，`cT.T.cPT` 仍命中候选 3） |
| `HALFFENCE`（P3 围栏） | 上表 4 行，`claimed=0` |

新增用例：`HalfSideGeneralizesToKColumns`（n=6 三列 / n=8 四列）、
`HalfSideKColumnsDoesNotFakeUnstableHalf`、`HalfSideFuseHandlesMultipleTemporaryBlocks`、
`HalfAssemblyBuildsSixAndEightQuadrantGraphs`、`HalfAssemblyFenceOnChainFailures`、
`HalfAssemblyPipelineReportsDiagnosisOnChainFailure`。

#### 7.13.1 为什么「n=6/8 交换链失败 + 半边工序成功」不可行（结论与替代）

```
候选 1 = 每列 S（n≥6 即五项）∧ Q 稳定
候选 2 ⇒ Q = Collapse(Q′) ⇒ Q 稳定
候选 3 ⇒ Q = Cut(前态) 的保留侧 ⇒ Q 稳定
```
所以 n ≥ 6 时「半边可得」⟹「静态半边每列合法 + 稳定」；
而整图交换链的失败在实测语料里**等价于**「至少一侧半边不满足这条」。
两边同时成立 ⇒ 整图交换链也能装配 ⇒ 新路径拿不到新整图。

要让这条路径真正比交换链强，需要**半边输入不可由交换链装配、但可由别的
rank 0 装配原语造出来**的形态，例如：

- 用 `Stack` 逐层拼 k 列（`docs/production-path-spec.md` §5.6 的 `Stack` 逆），
  但 `Stack` 需要「一块可独立制作的 hybrid」，本仓库目前只有单顶针版；
- 或者把论文算法 9 的「带外框输入」显式建模（现在交换链把外框折算成柱子，
  外框的朝向/位置自由度被丢掉了）。

这两条都超出本次范围，**没有伪造**：新路径只做真实 Cut + Exchange + 回放的结论。

**文档待更新**（`docs/` 由开发者本人维护，本次未动；行号按本次阅读时的版本）：

1. `docs/half-side-work-order.md` §4.1 第 124–130 行分层图：n ≥ 6 不再是「现有算法（不变）」
   独占——`BuildHalf` 现在也接受 n ≥ 6 的 k 列半边（只在装配层调用）；
2. 同文件 §4.2 第 132–144 行三个候选：补「k = n/2 列推广」，候选 3 改为
   「每块临时晶体顶层各一颗保险丝」；
3. 同文件 §6 第 177–183 行落地清单第 5 条：**已实现**（`HalfAssembly`），
   但要补一句「n ≥ 6 上不增加可制作整图，见 §7.13.1 的支配关系」；
4. 同文件 §3 第 104–116 行「算法 8 不完备」：补「候选 3 的 k 列推广已实现；
   n ≥ 6 时候选 1 支配 2/3」；
5. `docs/angle-work-order-algorithm.md` §6 第 614–642 行七条验收：半边现在是 k 列，
   第 1 / 4 / 5 条的「两列」改成「k 列」；
6. 同文件 §7 第 645–661 行工序单结构：`HalfWorkOrder` 增加 `Rotation`；
7. 同文件 §13.3 第 961 行（算法 8 / 9 / 10 属整图层）：可补「本仓库已把
   算法 8 的三个候选推广到 k 列，并用它做整图装配的兜底路径（`HalfAssembly`）」；
8. `docs/production-path-spec.md` §5.4.1 第 562–569 行「可交换 & 交换链失败 = 0」：
   本轮在 n = 6/8、4–5 层随机语料上复验（链失败 120+ 个样本，两侧半边都可得 0 个），
   可补一句「半边可得性判据与交换链成功在该语料上仍然一致」。
