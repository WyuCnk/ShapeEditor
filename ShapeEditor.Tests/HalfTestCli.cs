using System.IO;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 半边工序的<b>手动测试入口</b>（docs/prompt-for-main-agent-half-side-test.md）。
///
/// <para><b>不是测试项</b>：不参与 <c>RunAll</c> 的断言，只在命令行参数 /
/// 环境变量给出半边目标时才进入；没有它们时入口完全静默（行为与以前一致）。</para>
///
/// <para><b>两种调用方式</b></para>
/// <code>
/// ShapeEditor.Tests.exe --half "cT.T.cPT" "TTTT.ccc"      # 单例：两列目标
/// ShapeEditor.Tests.exe --half-shape "短代码"              # 单例：整图短代码（取第 1、2 象限）
/// ShapeEditor.Tests.exe --half-file D:\cases.txt          # 批量：每行「u<TAB>v」
/// $env:SHAPE_HALF = "cT.T.cPT|TTTT.ccc"                   # 环境变量等价写法
/// $env:SHAPE_HALF_FILE = "D:\cases.txt"
/// </code>
///
/// <para><b>单例输出</b>：[判定]（五项 / D / F / S / 最高晶体 / §2 的 m·r / HalfCriterion）、
/// [前态]（Q1–Q4 + 稳定性 / 四项五项 / P1）、[操作]、[结果]（逐格比较 + 第一处差异）。
/// 批量输出每例一行，最后一行是 <c>HALFTEST total=… pass=… fail=… error=…</c>。</para>
/// </summary>
internal static class HalfTestCli
{
    /// <summary>批量文件里一行一对目标列（制表符分隔；也接受空格）。</summary>
    private const char PairSeparator = '\t';

    /// <summary>
    /// 命令行 / 环境变量入口。返回 false 表示「这次调用与半边无关」，调用方继续跑测试。
    /// </summary>
    public static bool TryRun(
        string[] args,
        out int exitCode)
    {
        exitCode = 0;

        if (args is not null && args.Length > 0)
        {
            switch (args[0])
            {
                case "--half":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("用法：--half \"u\" \"v\"（两列目标，例如 --half \"cT.T.cPT\" \"TTTT.ccc\"）");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunSingle(args[1], args[2]);
                    return true;

                case "--half-shape":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("用法：--half-shape \"短代码\"");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunShape(args[1]);
                    return true;

                case "--half-file":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("用法：--half-file 路径（每行「u<TAB>v」）");
                        exitCode = 2;
                        return true;
                    }

                    exitCode = RunBatchFile(args[1]);
                    return true;
            }
        }

        string halfEnv = Environment.GetEnvironmentVariable("SHAPE_HALF") ?? string.Empty;

        if (halfEnv.Length > 0)
        {
            exitCode = RunSingleText(halfEnv, "--half / SHAPE_HALF");
            return true;
        }

        string fileEnv = Environment.GetEnvironmentVariable("SHAPE_HALF_FILE") ?? string.Empty;

        if (fileEnv.Length > 0)
        {
            exitCode = RunBatchFile(fileEnv);
            return true;
        }

        return false;
    }

    #region 单例

    /// <summary>单例：两列目标直接给列字符串。</summary>
    private static int RunSingle(
        string u,
        string v)
    {
        CaseOutcome outcome = Evaluate(u, v);

        PrintSingle(outcome);

        return outcome.Verdict == VerdictKind.Pass ? 0 : 1;
    }

    /// <summary>环境变量 <c>SHAPE_HALF="u|v"</c>：也接受制表符 / 逗号分割。</summary>
    private static int RunSingleText(
        string text,
        string source)
    {
        string[] pieces = text.Split(
            new[] { '|', '\t', ',' },
            StringSplitOptions.None);

        if (pieces.Length < 2)
        {
            Console.WriteLine(
                source + " 需要「u|v」两列，例如 SHAPE_HALF=\"cT.T.cPT|TTTT.ccc\"");
            return 2;
        }

        return RunSingle(pieces[0].Trim(), pieces[1].Trim());
    }

    /// <summary>单例：整图短代码（取第 1、2 象限当作 u、v）。</summary>
    private static int RunShape(
        string code)
    {
        if (!ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError))
        {
            Console.WriteLine("短代码尺寸推断失败：" + inferError);
            return 2;
        }

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        if (!parsed.Success || parsed.Document is null)
        {
            Console.WriteLine("短代码解析失败：" + parsed.Error);
            return 2;
        }

        ShapeDocument shape = parsed.Document;

        if (parts < 2)
        {
            Console.WriteLine("短代码只有 " + parts + " 个象限，半边至少需要 2 列。");
            return 2;
        }

        string u = ShapeSignature.ColumnText(shape, 0);
        string v = ShapeSignature.ColumnText(shape, 1);

        CaseOutcome outcome = Evaluate(u, v, shape, layers);

        PrintSingle(outcome);

        return outcome.Verdict == VerdictKind.Pass ? 0 : 1;
    }

    #endregion

    #region 批量

    /// <summary>批量：文件每行一对目标列（制表符分隔）。</summary>
    private static int RunBatchFile(
        string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("找不到批量文件：" + path);
            return 2;
        }

        string[] lines = File.ReadAllLines(path);

        int total = 0;
        int pass = 0;
        int fail = 0;
        int error = 0;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();

            if (line.Length == 0 ||
                line.StartsWith('#'))
            {
                continue;
            }

            string[] pieces = line.Split(
                new[] { PairSeparator, ' ', ',' },
                2,
                StringSplitOptions.RemoveEmptyEntries);

            total++;

            if (pieces.Length < 2)
            {
                error++;
                Console.WriteLine("ERROR " + line + " | 原因：这行不是「u<TAB>v」");
                continue;
            }

            CaseOutcome outcome = Evaluate(pieces[0].Trim(), pieces[1].Trim());

            switch (outcome.Verdict)
            {
                case VerdictKind.Pass:
                    pass++;
                    Console.WriteLine(
                        "PASS  " + outcome.U + " | " + outcome.V + " | " +
                        outcome.Layers + " 层 | " + outcome.ShortDetail());
                    break;

                case VerdictKind.Fail:
                    fail++;
                    Console.WriteLine(
                        "FAIL  " + outcome.U + " | " + outcome.V + " | 原因：" +
                        outcome.Reason);
                    break;

                default:
                    error++;
                    Console.WriteLine(
                        "ERROR " + outcome.U + " | " + outcome.V + " | 原因：" +
                        outcome.Reason);
                    break;
            }
        }

        Console.WriteLine(
            $"HALFTEST total={total} pass={pass} fail={fail} error={error}");

        return fail == 0 && error == 0 ? 0 : 1;
    }

    #endregion

    #region 求解与判定

    private enum VerdictKind
    {
        Pass,
        Fail,
        Error
    }

    /// <summary>一个半边目标（u、v 两列）的全部诊断结果。</summary>
    private sealed record CaseOutcome(
        string U,
        string V,
        int Layers,
        VerdictKind Verdict,
        string Reason,
        HalfPairVerdict Criterion,
        bool FiveU,
        bool DU,
        bool FU,
        bool SU,
        bool FiveV,
        bool DV,
        bool FV,
        bool SV,
        int HighestU,
        int HighestV,
        HalfWorkOrder? Half,
        AngleWorkOrder? Order,
        ShapeDocument? PreState,
        ShapeDocument? Result,
        string? StableText,
        string? LegalText,
        string? P1Text,
        string? FirstDiffText)
    {
        public string ShortDetail()
        {
            if (Half is not null && PreState is not null)
            {
                string columns = string.Join(
                    ",",
                    Enumerable.Range(0, PreState.PartCount)
                        .Select(part => ShapeSignature.ColumnText(PreState, part)));

                return $"m={Criterion.M + 1} r={Criterion.R + 1} | Q=({columns})";
            }

            if (Order is not null)
            {
                return "单角（S ✔） | 构造 = " + Order.Construction;
            }

            return "m=— r=— | 工序未覆盖";
        }
    }

    /// <summary>把一个半边目标（u、v）跑完：判定 → 半边候选 → 真实回放核对。</summary>
    private static CaseOutcome Evaluate(
        string uText,
        string vText,
        ShapeDocument? fullShape = null,
        int fullLayers = 0)
    {
        int layers = Math.Max(
            Math.Max(uText.Length, vText.Length),
            fullLayers);

        string u = Pad(uText, layers);
        string v = Pad(vText, layers);

        ShapeDocument target = fullShape is not null && fullShape.PartCount >= 2
            ? fullShape
            : BuildPair(u, v, layers);

        ColumnCheck checkU = ShapeSignature.CheckColumn(u);
        ColumnCheck checkV = ShapeSignature.CheckColumn(v);

        bool fiveU = checkU.IsLegal;
        bool fiveV = checkV.IsLegal;
        bool du = PillarSCriterion.BottomResourceD(u);
        bool fu = PillarSCriterion.RecursiveTerminalF(u);
        bool su = PillarSCriterion.IsPillarColumn(u);
        bool dv = PillarSCriterion.BottomResourceD(v);
        bool fv = PillarSCriterion.RecursiveTerminalF(v);
        bool sv = PillarSCriterion.IsPillarColumn(v);

        HalfPairVerdict criterion = HalfPairCriterion.Test(u, v);

        int highestU = HalfPairCriterion.HighestCrystal(u);
        int highestV = HalfPairCriterion.HighestCrystal(v);

        // ① 五项 ✘：连角工序都不成立，不进入半边。
        if (!fiveU)
        {
            return new CaseOutcome(
                uText, vText, layers, VerdictKind.Error,
                "五项(u) 不成立：" + checkU.Failures[0],
                criterion, fiveU, du, fu, su, fiveV, dv, fv, sv,
                highestU, highestV, null, null, null, null,
                null, null, null, null);
        }

        // ② S ✔ ⇒ 单角路线，不进入半边。
        if (su)
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                target.LayerCount,
                preferCrystalBreak: false);

            return new CaseOutcome(
                uText, vText, layers,
                order.IsComplete ? VerdictKind.Pass : VerdictKind.Fail,
                order.IsComplete
                    ? "S(u) ✔ 走单角"
                    : "S(u) ✔ 但单角路线未完成：" + (order.Note ?? "未给出原因"),
                criterion, fiveU, du, fu, su, fiveV, dv, fv, sv,
                highestU, highestV, null, order, null, null,
                null, null, null, null);
        }

        // ③ S ✘ ⇒ 半边。
        HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(
            target,
            0,
            target.LayerCount);

        ShapeDocument? preState = null;
        ShapeDocument? result = null;
        string? stableText = null;
        string? legalText = null;
        string? p1Text = null;
        string? firstDiff = null;

        if (half is not null && half.Steps.Count > 0 && half.Steps[0].Shape is not null)
        {
            ShapeDocument pre = half.Steps[0].Shape!;

            preState = pre;

            stableText = ShapeOps.IsStable(pre) ? "true" : "false";

            legalText = ShapeSignature.AllColumnsLegal(pre, out int badColumn)
                ? "true"
                : "false（第 " + (badColumn + 1) + " 象限不合法）";

            p1Text = CheckP1(pre);

            result = ReplayHalf(half);

            if (result is not null)
            {
                firstDiff = FirstDifference(
                    result,
                    target,
                    u,
                    v);
            }
        }

        bool ok = half is { IsComplete: true } && result is not null && firstDiff is null;

        string reason = ok
            ? "§2 判定与真实回放都通过"
            : BuildReason(criterion, half, firstDiff);

        return new CaseOutcome(
            uText, vText, layers,
            ok ? VerdictKind.Pass : (fiveU ? VerdictKind.Fail : VerdictKind.Error),
            reason,
            criterion, fiveU, du, fu, su, fiveV, dv, fv, sv,
            highestU, highestV, half, null, preState, result,
            stableText, legalText, p1Text, firstDiff);
    }

    private static string BuildReason(
        HalfPairVerdict criterion,
        HalfWorkOrder? half,
        string? firstDiff)
    {
        if (firstDiff is not null)
        {
            return "半破结果与目标不符（" + firstDiff + "）";
        }

        if (half is null)
        {
            return criterion.Passed
                ? "§2 判定 PASS，但候选 1 / 2 / 3 都没有给出可行工序（未覆盖）"
                : "§2 判定不通过：" + criterion.Reason;
        }

        return "半边工序未完成：" + (half.Note ?? "未给出原因");
    }

    /// <summary>按候选的真实操作回放：候选 3 是 Cut，候选 1 / 2 是一次下落。</summary>
    private static ShapeDocument? ReplayHalf(
        HalfWorkOrder half)
    {
        if (half.Steps.Count == 0 || half.Steps[0].Shape is null)
        {
            return null;
        }

        ShapeDocument preState = half.Steps[0].Shape!;

        if (half.Candidate == HalfCandidate.TempCrystalFuse)
        {
            CutResult cut = ShapePhysics.Cut(
                ShapeOps.Rotate(preState, half.Rotation));

            if (!cut.Success || cut.West is null)
            {
                return null;
            }

            return ShapeOps.Rotate(cut.West!, -half.Rotation);
        }

        return ShapePhysics.Collapse(preState).Document;
    }

    /// <summary>P1：原生晶体正下方不能为空（只查紧列里的晶体，临时晶体不在此列）。</summary>
    private static string CheckP1(
        ShapeDocument preState)
    {
        for (int part = 0; part < preState.PartCount; part++)
        {
            string text = ShapeSignature.ColumnText(preState, part);

            for (int layer = 1; layer < text.Length; layer++)
            {
                if (text[layer] == ShapeSignature.CrystalCode &&
                    text[layer - 1] == ShapeSignature.EmptyCode)
                {
                    return "false（第 " + (part + 1) + " 象限第 " + (layer + 1) + " 层晶体下方为空）";
                }
            }
        }

        return "true";
    }

    /// <summary>逐格比较回放结果与目标（前两列），返回第一处差异。</summary>
    private static string? FirstDifference(
        ShapeDocument result,
        ShapeDocument target,
        string u,
        string v)
    {
        for (int layer = 0; layer < target.LayerCount; layer++)
        {
            for (int part = 0; part < 2; part++)
            {
                ShapeCell actual = result[layer, part];
                ShapeCell expected = target[layer, part];

                if (!ShapeOps.SameCell(actual, expected))
                {
                    return "第 " + (layer + 1) + " 层 " +
                           (part == 0 ? "u" : "v") +
                           "：期望 " + Display(expected) +
                           "，实际 " + Display(actual);
                }
            }
        }

        return null;
    }

    private static string Display(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Empty => ".",
            CellType.Pin => "P",
            CellType.Crystal => "c",
            _ => cell.ShapeCode.ToString()
        };
    }

    #endregion

    #region 打印

    private static void PrintSingle(
        CaseOutcome outcome)
    {
        Console.WriteLine();
        Console.WriteLine(
            "=== target u = " + outcome.U + " ; v = " + outcome.V +
            " ; layers = " + outcome.Layers + " ===");
        Console.WriteLine();

        Console.WriteLine("[判定]");
        Console.WriteLine(
            "  五项(u)      = " + Bool(outcome.FiveU) +
            (outcome.FiveU ? string.Empty : "（" + outcome.Reason + "）"));
        Console.WriteLine(
            "  D(u) / F(u)  = " + Bool(outcome.DU) + " / " + Bool(outcome.FU));
        Console.WriteLine("  S(u)         = " + Bool(outcome.SU));
        Console.WriteLine("  五项(v)      = " + Bool(outcome.FiveV));
        Console.WriteLine(
            "  D(v) / F(v)  = " + Bool(outcome.DV) + " / " + Bool(outcome.FV));
        Console.WriteLine("  S(v)         = " + Bool(outcome.SV));

        string highest =
            "u@" + (outcome.HighestU + 1) + ", v@" + (outcome.HighestV + 1);

        if (outcome.HighestU >= 0 && outcome.HighestV >= 0)
        {
            highest += outcome.HighestU == outcome.HighestV
                ? "  （等高 ✘）"
                : "  （不等高 ✔）";
        }

        Console.WriteLine("  最高晶体      " + highest);

        if (outcome.Criterion.M >= 0)
        {
            Console.WriteLine(
                "  m            = " + (outcome.Criterion.M + 1) +
                "（第 " + (outcome.Criterion.M + 1) + " 层 = 较低最高晶体的粘连组最低层，" +
                "较低那列 = " + (outcome.Criterion.LowerColumn == 0 ? "u" : "v") + "）");
        }
        else
        {
            Console.WriteLine("  m            = —");
        }

        if (outcome.Criterion.R >= 0 &&
            outcome.Criterion.M >= 0 &&
            outcome.Criterion.R + 1 < outcome.Criterion.M)
        {
            int r = outcome.Criterion.R;
            int m = outcome.Criterion.M;

            Console.WriteLine(
                "  r            = " + (r + 1) +
                "（第 " + (r + 1) + " 层；u[" + r + "]=" +
                Chip(CellAt(outcome.U, r)) + ", v[" + r + "]=" +
                Chip(CellAt(outcome.V, r)) + "，TT ✔）");
            Console.WriteLine(
                "  r+1<m ✔；第 " + m + " 层两格全空 ✔（u[" + (m - 1) + "]=" +
                Chip(CellAt(outcome.U, m - 1)) + "，v[" + (m - 1) + "]=" +
                Chip(CellAt(outcome.V, m - 1)) + "）");
        }
        else
        {
            Console.WriteLine("  r            = —（" + outcome.Criterion.Reason + "）");
        }

        Console.WriteLine(
            "  HalfCriterion = " + (outcome.SU
                ? "—（S(u) ✔ ⇒ 单角路线，不要求 §2）"
                : (outcome.Criterion.Passed
                    ? "PASS"
                    : "FAIL（" + outcome.Criterion.Reason + "）")));

        if (outcome.SU)
        {
            Console.WriteLine();
            Console.WriteLine(
                "[单角] S(u) ✔ ⇒ 走单角路线，不进入半边" +
                (outcome.Order is null
                    ? string.Empty
                    : "；构造 = " + outcome.Order.Construction +
                      "，算法 = " + outcome.Order.Algorithm +
                      "，步骤 = " + outcome.Order.Steps.Count + " 步"));
            Console.WriteLine(
                "  VERDICT = " + (outcome.Verdict == VerdictKind.Pass
                    ? "PASS（单角）"
                    : "FAIL（" + outcome.Reason + "）"));
            return;
        }

        if (!outcome.FiveU)
        {
            Console.WriteLine();
            Console.WriteLine("  VERDICT = ERROR（" + outcome.Reason + "）");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("[前态]");

        if (outcome.PreState is null)
        {
            Console.WriteLine(
                "  （无：候选 1 / 2 / 3 都没有通过真实回放——§2 判定 " +
                (outcome.Criterion.Passed ? "PASS" : "FAIL") + "，工序仍未覆盖）");
            Console.WriteLine();
            Console.WriteLine("[操作] —");
            Console.WriteLine();
            Console.WriteLine("[结果] —");
            Console.WriteLine(
                "  VERDICT = " +
                (outcome.Criterion.Passed
                    ? "§2 判定 PASS / 工序 UNCOVERED（未覆盖，不是「不可制作」）"
                    : "FAIL（§2 判定不通过：" + outcome.Criterion.Reason + "）"));
            return;
        }

        for (int part = 0; part < outcome.PreState.PartCount; part++)
        {
            Console.WriteLine(
                "  Q" + (part + 1) + " = " +
                ShapeSignature.ColumnText(outcome.PreState, part));
        }

        Console.WriteLine("  稳定性       = " + outcome.StableText);
        Console.WriteLine("  四列五项     = " + outcome.LegalText);
        Console.WriteLine("  P1           = " + outcome.P1Text);

        Console.WriteLine();
        Console.WriteLine(
            "[操作] " + (outcome.Half?.OperationText ?? "—") +
            "（" + RouteName(outcome.Half) + "；顺转 " +
            (outcome.Half?.Rotation ?? 0) + " 个象限）");

        Console.WriteLine();
        Console.WriteLine("[结果]");

        string uActual = outcome.Result is null
            ? "—"
            : Trim(ShapeSignature.ColumnText(outcome.Result, 0));
        string vActual = outcome.Result is null
            ? "—"
            : Trim(ShapeSignature.ColumnText(outcome.Result, 1));

        string uExpected = Trim(outcome.U);
        string vExpected = Trim(outcome.V);

        bool uOk = uActual == uExpected;
        bool vOk = vActual == vExpected;

        Console.WriteLine(
            "  u' = " + Align(uActual, uExpected) + "     期望 " + uExpected +
            "   " + (uOk ? "✔" : "✘"));
        Console.WriteLine(
            "  v' = " + Align(vActual, vExpected) + "     期望 " + vExpected +
            "   " + (vOk ? "✔" : "✘"));

        if (outcome.FirstDiffText is not null)
        {
            Console.WriteLine("  第一处差异：" + outcome.FirstDiffText);
        }

        Console.WriteLine(
            "  VERDICT = " +
            (outcome.Verdict == VerdictKind.Pass
                ? "PASS"
                : "FAIL（" + outcome.Reason + "）"));
    }

    /// <summary>这条半边走的是哪条路线（§2 / 候选 3 / 候选 1 / 候选 2）。</summary>
    private static string RouteName(
        HalfWorkOrder? half)
    {
        if (half is null)
        {
            return "—";
        }

        if (half.Limitations.Any(line => line.StartsWith(
                "半边工序（§2 TT 双格条件）",
                StringComparison.Ordinal)))
        {
            return "§2 TT 双格条件 / 候选 2：联合逆提升";
        }

        return CandidateName(half.Candidate);
    }

    private static string CandidateName(
        HalfCandidate? candidate)
    {
        return candidate switch
        {
            HalfCandidate.TempCrystalFuse => "候选 3：临时晶体 + 保险丝 + 一刀",
            HalfCandidate.Algo8Self => "候选 1：Q 自身",
            HalfCandidate.Algo8Uplift => "候选 2：联合逆提升",
            _ => "—"
        };
    }

    private static string Bool(
        bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>单格显示：空格写成 <c>·</c>，其余原样。</summary>
    private static string Chip(
        string text)
    {
        return text.Length == 0
            ? "."
            : text.Replace(' ', '·');
    }

    private static string CellAt(
        string column,
        int layer)
    {
        return layer >= 0 && layer < column.Length
            ? column[layer].ToString()
            : ".";
    }

    private static string Align(
        string actual,
        string expected)
    {
        return actual.Length >= expected.Length
            ? actual
            : actual.PadRight(expected.Length);
    }

    #endregion

    #region 小工具

    private static string Pad(
        string text,
        int layers)
    {
        if (text.Length >= layers)
        {
            return text;
        }

        return text.PadRight(layers, ShapeSignature.EmptyCode);
    }

    private static string Trim(
        string text)
    {
        return text.TrimEnd(ShapeSignature.EmptyCode);
    }

    /// <summary>把两列目标装进四象限图形（第 1、2 象限有料，其余为空）。</summary>
    private static ShapeDocument BuildPair(
        string u,
        string v,
        int layers)
    {
        var document = new ShapeDocument(layers, 4);

        Write(document, 0, u);
        Write(document, 1, v);

        return document;
    }

    private static void Write(
        ShapeDocument document,
        int part,
        string text)
    {
        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            char code = layer < text.Length
                ? text[layer]
                : ShapeSignature.EmptyCode;

            document[layer, part] = code switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }
    }

    #endregion
}
