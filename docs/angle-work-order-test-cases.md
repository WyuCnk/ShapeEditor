# 角工序测试用例（可直接粘贴的短代码）

> 2026-11-16。**只测角工序**。
> 拼法：**第 1、3 象限 = 目标角；第 2、4 象限 = 全普通形状（`Cu`）**，四列同高。
> 需要半边工序的角（如 `cT.T.cPT`）**不在此表**。
> 全部 18 条已在当前 Core 上逐条跑过 `Build`：`IsComplete = True`。

## 怎么用

1. 把「短代码」整行粘进编辑器；
2. 打开分析页 → 展开「角的工序」；
3. 对照每行的「预期」检查：**算法 / 构造 / 步骤节点 / 顶针次数**。

说明：
- 节点数 = 有形状的步骤数（`Note` 只显示为文字，不计节点）；
- 「顶针次数」= 工序单的 `Rank`（只统计该角自己的顶针）；
- 步骤链里的 `顶` = 顶针（Pin）、`弱落` = WeakDrop、`切` = Cut、`撑` = Support、`堆` = Stack。

---

## 一、算法 6（D 成立）：单次顶针 + 弱落恢复

| # | 目标角 | 层 | 短代码 | 构造 | 步骤链 | 节点 | 看点 |
|---|---|---|---|---|---|---|---|
| 1 | `.T.c` | 4 | `--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→注→目标 | 3 | 最小角，顶针后即目标 |
| 2 | `.T..c` | 5 | `--Cu--Cu:CuCuCuCu:--Cu--Cu:--Cu--Cu:crCucrCu` | H | 前态→顶→注→目标 | 3 | 晶下 2 空（深间隙） |
| 3 | `T.T.c` | 5 | `CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→弱落→弱落→注→目标 | 5 | **两个弱落块** |
| 4 | `.T.T.c` | 6 | `--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→弱落→顶→注→目标 | 4 | **弱落在顶针之前**（多块） |
| 5 | `.TT.c` | 5 | `--Cu--Cu:CuCuCuCu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→弱落→注→目标 | 4 | 双晶体 + 一个弱落 |
| 6 | `TT.T.c` | 6 | `CuCuCuCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→弱落→弱落→注→目标 | 5 | **论文例 2**：底块提升两次 |
| 7 | `T...cT.T.c` | 10 | `CuCuCuCu:--Cu--Cu:--Cu--Cu:--Cu--Cu:crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→弱落→注→目标 | 4 | 10 层、两个晶体段 |
| 8 | `T..cT.T.c` | 9 | `CuCuCuCu:--Cu--Cu:--Cu--Cu:crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→弱落→注→目标 | 4 | 同族 9 层 |
| 9 | `.T.T.T.cT.Tc` | 12 | `--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:crCucrCu` | H | 前态→顶→注→目标 | 3 | 12 层多段，一次顶针完成 |

## 二、算法 5（底格是针）：一次批量恢复

| # | 目标角 | 层 | 短代码 | 构造 | 步骤链 | 节点 | 看点 |
|---|---|---|---|---|---|---|---|
| 10 | `P.T.c` | 5 | `P-CuP-Cu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu` | H | 前态→顶→注→目标 | 3 | 最小底针角 |
| 11 | `P.T.T..c` | 8 | `P-CuP-Cu:--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:--Cu--Cu:crCucrCu` | H | 前态→顶→注→目标 | 3 | 底针；两个普通格下落 |
| 12 | `PP.T.cTc` | 8 | `P-CuP-Cu:P-CuP-Cu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu:CuCuCuCu:crCucrCu` | H | 前态→顶→注→目标 | 3 | **底部连续 2 枚针**（前态放 m−1 枚 + 顶针 1 次） |
| 13 | `P.T.T..cP` | 9 | `P-CuP-Cu:--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:--Cu--Cu:crCucrCu:P-CuP-Cu` | H | 前态→顶→注→撑→堆→目标 | 5 | **顶部的针走剥尾 + 堆叠** |

## 三、算法 4（D ✘、F ✔）：环下落（半破）

| # | 目标角 | 层 | 短代码 | 构造 | 步骤链 | 节点 | 看点 |
|---|---|---|---|---|---|---|---|
| 14 | `T.ccc` | 5 | `CuCuCuCu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu` | G | 前态→切→注→目标 | 3 | 三连晶、环下落 |
| 15 | `cT.T.ccc` | 8 | `crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu` | G | 前态→切→撑→目标 | 4 | 长段恢复 |
| 16 | `cTT.cccT.T.ccc` | 14 | `crCucrCu:CuCuCuCu:CuCuCuCu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu` | G | 前态→切→注→目标 | 3 | **14 层**、两处提升、一刀 |
| 17 | `cT.T.T.T...ccc` | 14 | `crCucrCu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:--Cu--Cu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu` | G | 前态→切→撑→目标 | 4 | **14 层阶梯式抬升** |
| 18 | `T.T.cccTc` | 9 | `CuCuCuCu:--Cu--Cu:CuCuCuCu:--Cu--Cu:crCucrCu:crCucrCu:crCucrCu:CuCuCuCu:crCucrCu` | H | 前态→顶→弱落→弱落→注→目标 | 5 | 混合形态（D 成立走算法 6） |

---

## 四、按看点挑着测

| 想验证 | 用哪几条 |
|---|---|
| 最小可用 | 1、10、14 |
| 弱落（WeakDrop）显示 | 3、4、5、6、18 |
| 弱落在顶针之前 | 4 |
| 深间隙 | 2、7 |
| 底针 / 多枚底针 | 10、11、12 |
| 顶部针走剥尾 | 13 |
| 半破（Cut）路线 | 14、15、16、17 |
| 长层数（10 层以上） | 7、9、16、17 |

## 五、每条的预期检查点

1. **算法标签**：一/二组应显示 `算法 6` / `算法 5`，三组显示 `算法 4`；
2. **构造标签**：H（顶针）或 G（环下落）；
3. **节点顺序**：与上表「步骤链」一致，箭头按顺序连接；
4. **顶针次数**：一/二/三组均为 `1`（`Rank = 1`）；
5. **末节点**：一定是「目标」，且图形等于第 1 象限的目标角（第 3 象限相同）；
6. **底部第二、四象限**：始终是满 T 柱，不参与角工序。
