# 交接包

## 文档

- angle-route-optimal-v2.md       O-v2 规格（①碎晶一刀 / ②多刀 / ③顶针 / ④算法6）
- angle-work-order-general-algorithm.md  §0 变量定义 + §0.12 三条约定 + §0.12.4 口径裁决
- work-order-metrics.md            M1–M7 指标与字典序
- handoff-boundary-question.md     要问推理方的三个问题（P1 主问题）
- residual-47-weakdrop.md          47 列清零过程（历史）
- 列与可分形状的判定和构造.md          论文原文

## 数据

- lists/one-cut.txt       一刀成功（12062 条）
- lists/no-one-cut.txt    一刀失败（8038 条）
- lists/weakdrop.txt      含弱落（41 条）
- lists/multi-cut.txt     多刀（647 条，行尾附刀数）

口径（2026-11-17 实测）：3–9 层、全部 S 列 ∧ 碎晶角（`NeedsCrystalBreak`）共 **20100** 列；
`one-cut` + `no-one-cut` = 12062 + 8038 = 20100，两批互斥且无重复。

格式：每行一列，纯列字符串（`.` `T` `P` `c`，左端 = 第 1 层），
按层数、再字典序排序；`multi-cut.txt` 行尾为 `\t刀数`（2–4 刀）。
生成入口：`SHAPE_EXPORT_LISTS=1`（只跑导出，不跑断言）。

分布（列数）：

| 层数 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|
| one-cut | 2 | 13 | 59 | 227 | 796 | 2630 | 8335 |
| no-one-cut | 0 | 3 | 23 | 114 | 464 | 1690 | 5744 |
| weakdrop | 0 | 0 | 0 | 0 | 1 | 7 | 33 |
| multi-cut | 0 | 0 | 1 | 7 | 33 | 131 | 475 |

## 一句话

推理方只需要回答 P1：**碎晶·一刀的充要条件**。
有解/无解两批清单就是正负样本。
