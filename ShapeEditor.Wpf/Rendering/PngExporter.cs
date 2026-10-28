using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把图形导出成 PNG。
///
/// 单元格固定 128×96，与 Web 端导出一致；
/// 输出按 96 DPI 渲染，不做放大插值，保证边缘干净。
/// </summary>
public static class PngExporter
{
    public static BitmapSource Render(
        ShapeDocument document)
    {
        double width = document.PartCount * GridMetrics.ExportCellWidth;
        double height = document.LayerCount * GridMetrics.ExportCellHeight;

        if (width > GridMetrics.MaxExportSide ||
            height > GridMetrics.MaxExportSide)
        {
            throw new InvalidOperationException(
                $"图片尺寸过大：{width} × {height}");
        }

        DrawingGroup drawing = ShapeGridDrawing.Build(
            document,
            GridMetrics.ExportCellWidth,
            GridMetrics.ExportCellHeight);

        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, width, height));

            context.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(width),
            (int)Math.Round(height),
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(visual);

        bitmap.Freeze();

        return bitmap;
    }

    public static void Save(
        ShapeDocument document,
        string path)
    {
        BitmapSource bitmap = Render(document);

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(path);

        encoder.Save(stream);
    }
}
