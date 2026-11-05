using ShapeEditor.Core.Models;

namespace ShapeEditor.Maui;

/// <summary>
/// 分析页顶部竖式图的自适应版本。
///
/// 以 30 像素单元格为 1:1 基准：宽度按栏宽等比缩小、高度上限 320，
/// 缩放比例永远不超过 1，因此小图形保持原始大小、不会被放大。
/// 单元格缩小后字号与线宽随之减小，仍复用 <see cref="WorkspaceCellDrawable"/>
/// 的字母（c / S / P）与配色。
/// </summary>
public sealed class AnalysisPreviewDrawable : IDrawable
{
    private readonly ShapeDocument _document;

    private readonly float _cellSize;

    public AnalysisPreviewDrawable(
        ShapeDocument document,
        double cellSize)
    {
        _document = document;
        _cellSize = (float)cellSize;
    }

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        WorkspaceCellDrawable.DrawDocument(
            canvas,
            _document,
            0,
            0,
            _cellSize);
    }
}
