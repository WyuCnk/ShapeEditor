using ShapeEditor.Presentation.Cards;

namespace ShapeEditor.Maui.Cards;

/// <summary>
/// 工序卡片用到的颜色（0xAARRGGBB）。
///
/// <para>
/// 页面、导出图与探针都从这里取同一套色值，避免三处各写一份十六进制。
/// 这里只保留整数，不引用 MAUI / Skia，页面用 <c>Color.FromUint</c>、
/// 导出图用 <c>SKColor</c> 转换即可。
/// </para>
/// </summary>
public static class WorkOrderCardColors
{
    public const uint CardBackground = 0xFFFFFFFF;

    public const uint CardStroke = 0xFFE6E2F2;

    public const uint CardUnverifiedBackground = 0xFFFFFAFA;

    public const uint CardUnverifiedStroke = 0xFFE7B6B6;

    public const uint LayerBackground = 0xFFFBFAFF;

    public const uint LayerStroke = 0xFFE6E2F2;

    public const uint TitleText = 0xFF3A3A44;

    public const uint SectionTitleText = 0xFF4527A0;

    public const uint PreStateTitleText = 0xFF5E35B1;

    public const uint MutedText = 0xFF6B6B76;

    public const uint BodyText = 0xFF333333;

    public const uint StepHeaderText = 0xFF4A3F72;

    public const uint Accent = 0xFF512BD4;

    public const uint Danger = 0xFFD64545;

    public const uint Success = 0xFF2E7D32;

    public const uint VerifiedFill = 0xFFEAF7EE;

    public const uint UnverifiedFill = 0xFFFDEAEA;

    public const uint KindChipFill = 0xFFEFEAF9;

    public const uint KindChipText = 0xFF512BD4;

    public const uint ModeChipFill = 0xFFEEF1FC;

    public const uint ModeChipText = 0xFF3F51B5;

    public const uint WarningFill = 0xFFFDF6F6;

    public const uint WarningStroke = 0xFFD64545;

    public const uint WarningTitleText = 0xFFC62828;

    public const uint LegacyFill = 0xFFFFFAEB;

    public const uint LegacyStroke = 0xFFC9A227;

    public const uint LegacyText = 0xFF7A6415;

    public const uint InputFill = 0xFFFAF8FF;

    public const uint InputStroke = 0xFFE6E2F2;

    public const uint PreStateFill = 0xFFF6F4FF;

    public const uint PreStateStroke = 0xFFB39DDD;

    public const uint OutputFill = 0xFFFAF8FF;

    public const uint OutputStroke = 0xFF512BD4;

    public const uint GridLine = 0xFFE6E2F2;

    /// <summary>层左侧强调色条：交换 / 半边 / 角。</summary>
    public static uint LayerAccent(
        WorkOrderCardLayerKind kind) =>
        kind switch
        {
            WorkOrderCardLayerKind.Exchange => 0xFF512BD4,
            WorkOrderCardLayerKind.Half => 0xFF5E35B1,
            _ => 0xFF4527A0
        };

    /// <summary>步骤节点边框色，沿用 Presentation 给的 angle-kind-* 样式类。</summary>
    public static uint StepAccent(
        string cssClass) =>
        cssClass switch
        {
            "angle-kind-cut" => 0xFFC62828,
            "angle-kind-exchange" => 0xFF8E24AA,
            "angle-kind-support" => 0xFF3F51B5,
            "angle-kind-weakdrop" => 0xFF00695C,
            "angle-kind-pin" => 0xFF0E7C86,
            "angle-kind-stack" => 0xFFB26A00,
            _ => 0xFF2E7D32
        };

    /// <summary>步骤节点底色。</summary>
    public static uint StepFill(
        string cssClass) =>
        cssClass switch
        {
            "angle-kind-cut" => 0xFFFFF1F1,
            "angle-kind-exchange" => 0xFFF7EBFA,
            "angle-kind-support" => 0xFFEEF1FC,
            "angle-kind-weakdrop" => 0xFFE4F4F1,
            "angle-kind-pin" => 0xFFF2FBFB,
            "angle-kind-stack" => 0xFFFFF8EE,
            _ => 0xFFF3FBF4
        };
}
