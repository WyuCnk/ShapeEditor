# 给主智能体的提示词：导出清单 + 打包交接

> 把下面整段发给主智能体。

---

## 任务

为「交给推理方做边界分析」准备一个交接包。两件事：**导出四份清单**、**复制文档到 `docs/handoff/`**。

文档我已经复制好了（`docs/handoff/` 下六个 .md），你只需要：**导出清单** + **写一份清单索引**。

---

## 一、要导出的四份清单

统一格式：每行一列，**纯列字符串**（`.` `T` `P` `c`，左端 = 第 1 层），
按层数、字典序排序；文件放 `docs/handoff/lists/`。

| # | 文件 | 内容 | 预计条数 |
|---|---|---|---|
| **L1** | `one-cut.txt` | 3–9 层全部 S 列 ∧ 含晶体 中，**碎晶·一刀成功**的列 | ≈ 12062 |
| **L2** | `no-one-cut.txt` | 同上集合中，**碎晶·一刀失败**的列 | ≈ 8038 |
| **L3** | `weakdrop.txt` | 生成的工序**含弱落步**（`WeakDrop`）的列 | 41 |
| **L4** | `multi-cut.txt` | 生成的工序里 **`Cut` 步数 ≥ 2** 的列，行尾附刀数 | 647 |

格式示例：

```
# one-cut.txt
.T.c
.T..c
...
# multi-cut.txt
T.T.c	2
T.T.T.c	3
```

> 层数范围：3–9。若某层太快/太慢，可先导 3–7 并在文件名加 `_L3-7` 后缀。

---

## 二、导出实现

复用现有的审计入口（`SHAPE_TWO_STAGE_AUDIT` 那一套），加一个模式：

```
环境变量 SHAPE_EXPORT_LISTS=1  → 只跑导出，不跑断言
输出目录 docs/handoff/lists/
```

分类口径（与当前实现一致）：

```
碎晶·一刀  = 最终路线是「碎晶」且 Cut 步数 == 1
碎晶·多刀  = 最终路线是「碎晶」且 Cut 步数 >= 2
弱落       = 工序里有 Kind == WeakDrop
```

请在导出结束后打印一行统计：

```
EXPORT oneCut=… noOneCut=… weak=… multiCut=… (L3-9)
```

---

## 三、写一份索引

`docs/handoff/README.md`，内容包含：

```markdown
# 交接包

## 文档
- angle-route-optimal-v2.md       O-v2 规格（①碎晶一刀 / ②多刀 / ③顶针 / ④算法6）
- angle-work-order-general-algorithm.md  §0 变量定义 + §0.12 三条约定 + §0.12.4 口径裁决
- work-order-metrics.md            M1–M7 指标与字典序
- handoff-boundary-question.md     要问推理方的三个问题（P1 主问题）
- residual-47-weakdrop.md          47 列清零过程（历史）
- 列与可分形状的判定和构造.md          论文原文

## 数据
- lists/one-cut.txt       一刀成功（约 12062 条）
- lists/no-one-cut.txt    一刀失败（约 8038 条）
- lists/weakdrop.txt      含弱落（41 条）
- lists/multi-cut.txt     多刀（647 条，行尾附刀数）

## 一句话
推理方只需要回答 P1：**碎晶·一刀的充要条件**。
有解/无解两批清单就是正负样本。
```

---

## 四、自检

```
1) docs/handoff/lists/ 下四个文件都存在且非空
2) 每行都是合法列字符串（只含 . T P c），长度 = 层数
3) L1 + L2 的条数 = 3–9 层全部「S ∧ 含晶体」的列数（应 = 20100）
4) L3 = 41、L4 = 647（与本轮报告一致）
5) docs/handoff/README.md 已写
6) 不要改动 Core / Tests 的任何行为（导出模式必须可关闭）
```

## 五、完成后回报

```
· 四个文件的条数
· L1 + L2 是否等于 20100
· 如果有层数没覆盖（例如 9 层太慢被跳过），说明跳过哪层
· README.md 的最终路径
```
