using System.Reflection;

namespace ShapeEditor.Web.Services;

/// <summary>
/// Web 端展示用的产品版本号。
///
/// 版本来自仓库根目录 Directory.Build.props 的 ProductVersion，
/// 构建时写入程序集的 InformationalVersion。
/// </summary>
public static class AppVersion
{
    public static string Value { get; } =
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
}
