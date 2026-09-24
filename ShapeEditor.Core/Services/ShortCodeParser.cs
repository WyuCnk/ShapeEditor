using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ShortCodeParser
{
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