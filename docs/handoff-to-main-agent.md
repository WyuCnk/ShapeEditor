# 交接提示词：TMAM 制作路径框架落地

> 直接把下面整段发给主智能体。

---

## 0. 你的任务

仓库 `D:\ShapezEditor`（.NET 10，Web / WPF / MAUI 共用一个 `ShapeEditor.Core`）。

有人已经把「任意层、任意偶数象限」的**制作路径算法**整理成规格并写好了代码骨架。
你的任务是：**审阅骨架 → 把它接进 `ProductionPathParser` → 把验收语料写成测试 → 接到 UI**。
算法本身不要重新推导，规格里有结论和证据。

## 1. 先读这些（按顺序）

1. `docs/production-path-spec.md` —— **主规格**，判据 + 构造 + 交换链 + 框架，全部标了「已验证/待定」。
2. `docs/production-path-exchange-prompt.md`、`docs/analysis-production-path.md` —— 交换链的原始样例。
3. `docs/Shapez 2 列与可分形状的判定和构造 (1).pdf` —— 判据来源（中文论文，15 页）。
4. 代码：`ShapeEditor.Core/Physics/`（物理原语）、`Analysis/`（现有分析器与骨架）。

## 2. 已经定稿的结论（**不要重新推导**）

1. **角合法性 = 论文的五项条件**。现有 `ShapeAngleAnalyzer` 与它逐层等价（1–8 层计数完全一致：
   `4, 14, 47, 152, 476, 1451, 4328, 12688`）。**只有"存在不合法角"才判「不可制作」**。
2. **cap = 文档层数**（`ShapeDocument.LayerCount`），整条路径共用。注意 `Pin` 的上限是 cap 而不是内容高度。
3. **交换链**（规格 §4）：柱位公式、预旋转、切割拼接、判定。
   方向数 = `(n/2) · (half−1)²`，**轴必须遍历**（实测 11.7% 的图形只在 a≠0 时成功）。
4. **可交换 ⟺ 交换链成功**（n=6、2 层 232,425 个样本、3 层 91,708+15,307 个样本，**0 反例**）。
   所以「可交换」是第一道门；不满足 ⇒ 返回**未覆盖**，不是「不可制作」。
5. **顶针逆**（规格 §5.6.2）：
   - 收据行条件：`X[0][c] 非空 ⟺ Y[0][c] = P`；
   - 顺序：**先填水晶塔，再去掉最底层**；
   - 填充分两种：**直接填充** / **抬升块后填充**（块挡路或收据行不满足时抬升一层）。
6. **拆 hybrid**（规格 §5.6.2）：hybrid 必须**可制作**（自带 Recipe）且 `Stack(D, H) = Y`；
   同一层可能有**二择**，必须**回溯**，不能一次定死。
7. **rank ≤ 3**（顶针次数上限）。
8. 结论分级：**不合法角 → 不可制作；其余失败 → 未覆盖**。

## 3. 已交付的文件（只新增，未改动任何现有文件）

```
ShapeEditor.Core/Analysis/Production/
    ShapeSignature.cs      五项条件（纯函数，任意 n、任意层）
    Swappable.cs           可交换判定
    ExchangeChain.cs       交换链（柱位/预旋转/切割拼接）
    Recipe.cs              Recipe / Block 节点模型
    ProductionPipeline.cs  SolveCore 主循环 + TowerFill / HybridStrips 接口
    Replay.cs              用真实 ShapePhysics 逐步回放校验
    TODO.md                两个待定接口的契约与替换点
docs/production-path-spec.md
```

**若这些文件不存在**，说明还没落地——按 §5 的接口签名自己建，规格里有全部细节。

⚠️ **交付方没能跑测试**：他的环境 `dotnet build ShapeEditor.Tests` 报
`MSB4276`（SDK 10.0.401 缺 workload locator），只能编译 `ShapeEditor.Core` 并用独立 probe 验证算法。
**所以你必须完整跑一遍测试套件，不要假设骨架是绿 的。**

## 4. 你要做的（按顺序）

1. **审阅骨架**：签名是否合适、是否与现有代码风格一致；不合适就改，不必照抄。
2. **重写 `ProductionPathParser`**：内部改为调用 `ProductionPipeline.Solve(document, cap)`，
   输出节点链（步骤名 / 图形快照 / 连线 / 废料标记）。旧的骨架逻辑（无预旋转、精确比对、遍历轴）
   **整段删除**，不要保留兼容层。
3. **补测试**（见 §5 的验收语料）。测试工程是零依赖 runner，在 `ShapeEditor.Tests/Program.cs` 注册。
4. **接 UI**：把节点链画出来（步骤名 + 图形快照 + 连线 + 废料灰显）。
5. **汇报**（见 §7）。

## 5. 验收语料（必须做成测试）

| # | 语料 | 期望 |
|---|---|---|
| 1 | 角判据：宽度 1、层数 1–8 的 4^L 枚举 | 计数 `4, 14, 47, 152, 476, 1451, 4328, 12688` |
| 2 | 论文 10 个举例列（`T.c` `.c` `cT.c` `cT.T.c` `cTT..T.c` `cT.T.ccc` `.T..T.c` `PPTTc` `TPc` …） | 五项条件判定 + n=4 的 S 判定（`cT.T.c` 合法但**不是** S） |
| 3 | n=6、2 层、`{.,T,c}` 全枚举 531,441 | 稳定且每列合法 **232,425**；交换链成功 **230,601**；可交换⟺链成功，**0 反例** |
| 4 | n=6、3 层、`{.,T}` 全枚举 2^18 | 合格 **91,708**，0 反例 |
| 5 | n=6、3 层、`{.,T,c}` 随机 | 合格 **15,307**，0 反例 |
| 6 | claw 例子（4 象限 3 层）`c T P T / P . P . / P . P .` | 不可交换；拆掉 (L3,第3象限) 顶针 + 填塔得 X：**X 稳定 ✔ 可交换 ✔，顶针(X)==D ✔**。⚠️ 骨架当前是 **xfail**（`TowerFill` 只做直接填充，见 `Production/TODO.md`） |
| 7 | 二择例子（4 象限 5 层）`--P-P-P-:CuP---Cu:--P-CuCu:cuP---Cu:cuCu----` | 原图/两个单拆都「稳定但不可交换」；**两个都拆 ⇒ 不稳定**。⚠️ 同上 xfail |
| 8 | n=6、单层 `CrCgCbCcCmCy`（方向 `(12+3)+(4+56)`） | 交换链逐格复现（规格 §6.2 有全部中间结果） |
| 9 | n=8、单层（方向 `(12+34)+(56+78)`） | 同上（规格 §6.3） |

**接口签名**（若骨架缺失，按此落地）：

```csharp
namespace ShapeEditor.Core.Analysis.Production;

public static class ShapeSignature   { public static bool IsLegalColumn(ShapeDocument d, int col); }
public static class Swappable        { public static bool Test(ShapeDocument d); }
public static class ExchangeChain    { public static Recipe? Build(ShapeDocument target, int axis, int leftSplit, int rightSplit); }

public sealed record Recipe(RecipeKind Kind, ShapeDocument Shape, IReadOnlyList<Recipe> Inputs, ShapeDocument? Block = null);
public enum RecipeKind { Exchange, Pin, Stack }

public static class ProductionPipeline
{
    public static PipelineResult Solve(ShapeDocument target, int cap, int maxRank = 3);
}
public sealed record PipelineResult(bool Found, Recipe? Recipe, PipelineOutcome Outcome, IReadOnlyList<string> Notes);
public enum PipelineOutcome { Producible, NotProducible, Uncovered }
```

两个待定接口（`TowerFill` / `HybridStrips`）先按 `TODO.md` 的保守版实现即可：
只做「直接填充」和「拆单个顶针」，其余返回空集合；框架其余部分照跑。

## 6. 不要做的事

- **不要打表**：要求任意层、任意偶数象限，不能按 n × 层数枚举。
- **不要考虑循环支撑结构**（本次范围外）。
- **不要把「未覆盖」写成「不可制作」**——只有"存在不合法角"才是否定。
- **不要改交换语义**：刀口固定 `n/2`，`out1 = A东+B西`、`out2 = A西+B东`，合并「左非空取左」。
- 不要为了通过测试而放宽判据；测试里的数字是硬指标。

## 7. 汇报格式

1. 改了哪些文件、各自一句话说明；
2. 测试结果：§5 的 9 组语料逐条 PASS/FAIL + 失败原因；
3. 骨架里你改动了哪些设计、为什么；
4. 仍未覆盖的部分（按原因分类：`TowerFill` 规则不足 / `HybridStrips` 规则不足 / 其他）；
5. 下一步建议（按优先级）。

---

**一句话总结**：算法已经定稿且有证据，你负责把它变成能跑的代码和能过的测试；
遇到规格没写死的地方（水晶塔抬升规则、hybrid 二择规则）保持保守实现 + 明确标注，
不要自己发明新规则。
