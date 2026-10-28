using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ShortCodeParser
{
    public const int MaxLayerCount = 100;

    public const int MaxPartCount = 100;

    public const int MaxCellCount = 10_000;

    public ParseResult Parse(
        string code,
        int layerCount,
        int partCount)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ParseResult.Fail(
                "短代码不能为空。");
        }

        if (layerCount <= 0 ||
            partCount <= 0)
        {
            return ParseResult.Fail(
                "层数和象限数必须大于 0。");
        }

        string[] layerCodes =
            code.Trim().Split(
                ':',
                StringSplitOptions.None);

        if (layerCodes.Length > layerCount)
        {
            return ParseResult.Fail(
                $"短代码包含 {layerCodes.Length} 层，" +
                $"当前只能容纳 {layerCount} 层。");
        }

        int expectedLength = checked(partCount * 2);

        var document = new ShapeDocument(
            layerCount,
            partCount);

        /*
         * document 初始就是全空。
         * 输入层从 layer 0 开始，因此不足的层自动位于顶部。
         */
        for (int inputLayer = 0;
             inputLayer < layerCodes.Length;
             inputLayer++)
        {
            string layerCode =
                layerCodes[inputLayer];

            if (layerCode.Length > expectedLength)
            {
                return ParseResult.Fail(
                    $"第 {inputLayer + 1} 层象限数超过目标尺寸。");
            }

            if (layerCode.Length % 2 != 0)
            {
                return ParseResult.Fail(
                    $"第 {inputLayer + 1} 层代码长度必须是偶数。");
            }

            int inputPartCount = layerCode.Length / 2;

            for (int part = 0;
                 part < inputPartCount;
                 part++)
            {
                char typeCode =
                    layerCode[part * 2];

                char colorCode =
                    layerCode[part * 2 + 1];

                if (!TryParseCell(
                        typeCode,
                        colorCode,
                        out ShapeCell? cell,
                        out string? error))
                {
                    return ParseResult.Fail(
                        $"第 {inputLayer + 1} 层、" +
                        $"第 {part + 1} 象限错误：{error}");
                }

                document[inputLayer, part] =
                    cell!;
            }
        }

        return ParseResult.Ok(document);
    }

    /// <summary>
    /// 推断短代码对应的层数与象限数，供批量查看这类只读场景使用。
    /// 不生成文档，只计算尺寸：
    /// 层数等于冒号分隔的段数，象限数按最宽的一层计算，
    /// 因此层与层之间宽度不一致的短代码也能推断出可显示的尺寸。
    /// </summary>
    public static bool TryInferDimensions(
        string code,
        out int layerCount,
        out int partCount,
        out string? error)
    {
        layerCount = 0;
        partCount = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "短代码不能为空。";
            return false;
        }

        string[] layerCodes =
            code.Trim().Split(
                ':',
                StringSplitOptions.None);

        int widestLayer = 0;

        for (int index = 0;
             index < layerCodes.Length;
             index++)
        {
            int length =
                layerCodes[index].Length;

            if (length % 2 != 0)
            {
                error =
                    $"第 {index + 1} 层代码长度必须是偶数。";

                return false;
            }

            widestLayer =
                Math.Max(
                    widestLayer,
                    length);
        }

        if (widestLayer == 0)
        {
            error = "短代码不能为空。";
            return false;
        }

        layerCount = layerCodes.Length;
        partCount = widestLayer / 2;

        if (layerCount > MaxLayerCount)
        {
            error = $"层数不能超过 {MaxLayerCount}。";
            return false;
        }

        if (partCount > MaxPartCount)
        {
            error = $"象限数不能超过 {MaxPartCount}。";
            return false;
        }

        if ((long)layerCount * partCount > MaxCellCount)
        {
            error =
                $"单元格总数不能超过 {MaxCellCount}。";

            return false;
        }

        return true;
    }

    private static bool TryParseCell(
        char typeCode,
        char colorCode,
        out ShapeCell? cell,
        out string? error)
    {
        cell = null;
        error = null;

        if (typeCode == '-')
        {
            if (colorCode != '-')
            {
                error = "空位必须写成 --。";
                return false;
            }

            cell = ShapeCell.Empty();
            return true;
        }

        if (typeCode == 'P')
        {
            if (colorCode != '-')
            {
                error = "顶针必须写成 P-。";
                return false;
            }

            cell = ShapeCell.Pin();
            return true;
        }

        if (typeCode == 'c')
        {
            if (!ShapeColor.IsValid(colorCode))
            {
                error =
                    $"颜色代码 {colorCode} 无效。";

                return false;
            }

            cell = ShapeCell.Crystal(colorCode);
            return true;
        }

        if (ShapeCell.IsNormalShapeCode(typeCode))
        {
            if (!ShapeColor.IsValid(colorCode))
            {
                error =
                    $"颜色代码 {colorCode} 无效。";

                return false;
            }

            cell = ShapeCell.Shape(
                typeCode,
                colorCode);

            return true;
        }

        error =
            $"无法识别形状类型 {typeCode}。";

        return false;
    }
}