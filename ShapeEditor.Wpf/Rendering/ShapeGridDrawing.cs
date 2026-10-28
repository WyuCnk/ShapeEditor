using System.Windows;
using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把 ShapeDocument 画成一张可复用的 DrawingGroup。
///
/// 每个单元格按基准尺寸绘制，缩放由调用方通过变换完成，
/// 因此平移和缩放不会触发重新构建。
/// </summary>
public static class ShapeGridDrawing
{
    public static DrawingGroup Build(
        ShapeDocument document,
        double cellWidth,
        double cellHeight,
        int? selectedLayer = null,
        int? selectedPart = null,
        bool drawGrid = true)
    {
        var group = new DrawingGroup();

        using (DrawingContext context = group.Open())
        {
            var borderPen = new Pen(
                ShapeTheme.Instance.CellBorderPen.Brush,
                GridMetrics.GetCellBorderWidth(cellWidth, cellHeight));

            borderPen.Freeze();

            for (int visualRow = 0;
                 visualRow < document.LayerCount;
                 visualRow++)
            {
                /*
                 * 竖式图第一行是最高层，短代码从最低层开始写。
                 */
                int layer = document.LayerCount - 1 - visualRow;

                double y = visualRow * cellHeight;

                for (int part = 0;
                     part < document.PartCount;
                     part++)
                {
                    double x = part * cellWidth;

                    DrawCell(
                        context,
                        document[layer, part],
                        x,
                        y,
                        cellWidth,
                        cellHeight,
                        borderPen);
                }
            }

            if (drawGrid)
            {
                DrawGrid(
                    context,
                    document.LayerCount,
                    document.PartCount,
                    cellWidth,
                    cellHeight);
            }

            if (selectedLayer is int selectedLayerIndex &&
                selectedPart is int selectedPartIndex)
            {
                DrawSelection(
                    context,
                    document,
                    selectedLayerIndex,
                    selectedPartIndex,
                    cellWidth,
                    cellHeight);
            }
        }

        group.Freeze();

        return group;
    }

    /// <summary>
    /// 单元格在画布上的矩形，供命中测试与其他视图复用。
    /// </summary>
    public static Rect GetCellRect(
        ShapeDocument document,
        int layer,
        int part,
        double cellWidth,
        double cellHeight)
    {
        int visualRow = document.LayerCount - 1 - layer;

        return new Rect(
            part * cellWidth,
            visualRow * cellHeight,
            cellWidth,
            cellHeight);
    }

    private static void DrawCell(
        DrawingContext context,
        ShapeCell cell,
        double x,
        double y,
        double width,
        double height,
        Pen borderPen)
    {
        ShapeTheme theme = ShapeTheme.Instance;

        var rect = new Rect(x, y, width, height);

        if (cell.Type == CellType.Empty)
        {
            context.DrawRectangle(theme.EmptyBrush, null, rect);

            return;
        }

        if (cell.Type == CellType.Pin)
        {
            context.DrawRectangle(theme.EmptyBrush, null, rect);

            context.DrawRectangle(
                theme.PinBrush,
                null,
                ShapeTheme.GetPinBar(x, y, width, height));

            return;
        }

        if (cell.Type == CellType.Crystal)
        {
            SolidColorBrush[] bands = theme.GetCrystalBrushes(cell.ColorCode);

            double bandWidth = width / bands.Length;
            double bandX = x;

            for (int index = 0;
                 index < bands.Length;
                 index++)
            {
                /*
                 * 最后一段补齐到边界，避免浮点误差留下一条缝。
                 */
                double actualWidth = index == bands.Length - 1
                    ? x + width - bandX
                    : bandWidth;

                context.DrawRectangle(
                    bands[index],
                    null,
                    new Rect(bandX, y, actualWidth, height));

                bandX += bandWidth;
            }
        }
        else
        {
            context.DrawRectangle(
                theme.GetShapeBrush(cell.ColorCode),
                null,
                rect);
        }

        double inset = GridMetrics.GetCellBorderInset(width, height);

        context.DrawRectangle(
            null,
            borderPen,
            new Rect(
                x + inset,
                y + inset,
                Math.Max(0, width - inset * 2),
                Math.Max(0, height - inset * 2)));

        if (!ShapeTheme.HasText(cell.Type))
        {
            return;
        }

        double fontSize = GridMetrics.GetFontSize(width, height);

        FormattedText text = theme.GetText(
            ShapeTheme.GetCellText(cell.Type),
            ShapePalette.NeedsLightText(cell.ColorCode),
            fontSize);

        context.DrawText(
            text,
            new Point(
                x + (width - text.Width) / 2,
                y + (height - text.Height) / 2));
    }

    private static void DrawGrid(
        DrawingContext context,
        int rows,
        int columns,
        double cellWidth,
        double cellHeight)
    {
        double width = columns * cellWidth;
        double height = rows * cellHeight;

        /*
         * 外边框向内缩半个像素，避免线条落在边界上被裁掉。
         */
        const double half = 0.5;

        double right = Math.Max(half, width - half);
        double bottom = Math.Max(half, height - half);

        Pen pen = ShapeTheme.Instance.GridPen;

        for (int column = 0;
             column <= columns;
             column++)
        {
            double x = column == columns
                ? right
                : column * cellWidth;

            context.DrawLine(
                pen,
                new Point(x, half),
                new Point(x, bottom));
        }

        for (int row = 0;
             row <= rows;
             row++)
        {
            double y = row == rows
                ? bottom
                : row * cellHeight;

            context.DrawLine(
                pen,
                new Point(half, y),
                new Point(right, y));
        }
    }

    private static void DrawSelection(
        DrawingContext context,
        ShapeDocument document,
        int layer,
        int part,
        double cellWidth,
        double cellHeight)
    {
        int visualRow = document.LayerCount - 1 - layer;

        double inset = GridMetrics.GetSelectionInset(cellWidth, cellHeight);

        var pen = new Pen(
            ShapeTheme.CreateBrush(GridMetrics.SelectionColor),
            GridMetrics.GetSelectionWidth(cellWidth, cellHeight));

        pen.Freeze();

        context.DrawRectangle(
            null,
            pen,
            new Rect(
                part * cellWidth + inset,
                visualRow * cellHeight + inset,
                Math.Max(0, cellWidth - inset * 2),
                Math.Max(0, cellHeight - inset * 2)));
    }
}
