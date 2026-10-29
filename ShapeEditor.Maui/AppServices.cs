using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 进程内共享的实例：编辑器和「工作区」页必须用同一个，
/// 否则两边各自写文件会互相覆盖。
/// </summary>
public static class AppServices
{
    public static WorkspaceStore Workspace { get; } =
        new(Path.Combine(FileSystem.AppDataDirectory, "workspaces.json"));
}
