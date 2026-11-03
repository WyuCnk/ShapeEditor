using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区「加到编辑区 / 分析」入口。
///
/// 目标可以是画布上选中的节点，也可以是素材库里选中的素材；
/// 节点优先，节点没有图形时退回素材。两个入口都只把短代码写进
/// <see cref="AppPending"/>，再由目标页在 OnAppearing 里取用，
/// 与 Web 版的 PendingLoad 机制一致。
/// </summary>
public partial class WorkspacePage
{
    private readonly ShortCodeExporter _navigationExporter = new();

    /// <summary>当前是否有可交给编辑器 / 分析的图形，用于按钮可用状态。</summary>
    private bool HasSelectableShape()
    {
        if (_store?.SelectedNode is WorkspaceNode node)
        {
            return _store.GetEffectiveDocument(node) is not null;
        }

        return _selectedMaterial is not null;
    }

    /// <summary>
    /// 取当前选中素材或节点的短代码及其原始尺寸。
    /// 节点优先（按当前有效图形重新导出），没有节点时用选中的素材。
    /// </summary>
    private (string Code, ShortCodeFormat Format, int Layers, int Parts)? SelectedShape()
    {
        if (_store?.SelectedNode is WorkspaceNode node &&
            _store.GetEffectiveDocument(node) is ShapeDocument document)
        {
            return (
                _navigationExporter.Export(document, omitTopEmptyLayers: true),
                ShortCodeFormat.Full,
                document.LayerCount,
                document.PartCount);
        }

        if (_selectedMaterial is WorkspaceItem material)
        {
            return (
                material.Code,
                material.Format,
                material.Layers,
                material.Parts);
        }

        return null;
    }

    /// <summary>
    /// 「加到编辑区」：把当前图形交给编辑器，并切到编辑器标签页。
    /// </summary>
    private async void OnAddToEditor(
        object? sender,
        EventArgs e)
    {
        if (SelectedShape() is not (string code, ShortCodeFormat format, int layers, int parts))
        {
            Show("请先选择带图形的素材或节点。");

            return;
        }

        AppPending.SetEditorLoad(new EditorLoadTarget(
            code,
            format,
            layers,
            parts));

        if (!await AppNavigation.GoToEditorAsync())
        {
            AppPending.ClearEditorLoad();

            Show("打开编辑器失败。");
        }
    }

    /// <summary>
    /// 「分析」：把当前图形交给分析页，并压入当前标签页的导航栈。
    /// </summary>
    private async void OnAnalyzeSelected(
        object? sender,
        EventArgs e)
    {
        if (SelectedShape() is not (string code, ShortCodeFormat format, int layers, int parts))
        {
            Show("请先选择带图形的素材或节点。");

            return;
        }

        AppPending.SetAnalysis(new AnalysisTarget(
            code,
            format,
            layers,
            parts));

        if (!await AppNavigation.GoToAnalysisAsync())
        {
            AppPending.ClearAnalysis();

            Show("打开分析页失败。");
        }
    }
}
