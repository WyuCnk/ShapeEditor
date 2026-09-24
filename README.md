# 竖式图编辑器

一个用于将图形短代码转换为竖式图、编辑图形并导出结果的工具。目前提供 **Web 在线版**和 **Android 版**。项目使用 C# 编写核心逻辑，Web 版基于 Blazor WebAssembly。

## 在线使用与下载

- [打开 Web 在线版](https://wyucnk.github.io/ShapeEditor/)
- [下载 Android APK](https://github.com/WyuCnk/ShapeEditor/releases)

> Android APK 请从 Releases 中选择最新版本。Windows WPF 版计划后续整合进本解决方案，届时将在 Releases 中提供 EXE。

## 功能

- 解析、编辑和导出图形短代码
- 支持不同层数和象限数
- 以矩阵形式显示竖式图：**最高层在上，最低层在下**
- 显示空位、顶针、晶体、普通形状及其颜色
- 形状画笔、颜色画笔和组合画笔
- 图形整体上移、下移，以及按象限循环左移、右移
- 画布移动和缩放
- 撤销、重做
- 导出完整竖式图 PNG
- Web 版浏览器自动保存、JSON 存档导入和导出

部分浏览器不支持直接分享图片文件；此时可以先下载 PNG，再从设备中分享。

## 短代码规则

冒号 `:` 分隔不同层，**第一段是最低层**；每两个字符表示一个象限，同一层按短代码顺序从左到右排列。

例如：

```text
CrP---Gu:ckckckck
```

表示两层、每层四个象限。竖式图显示时，第二段位于第一段上方。

常见单元代码：

| 代码 | 含义                |
| ---- | ------------------- |
| `--` | 空位                |
| `P-` | 顶针                |
| `cr` | 红色晶体            |
| `Cu` | 未着色普通形状      |
| `Gu` | 未着色的 G 普通形状 |

颜色字符包括 `r`、`g`、`b`、`c`、`m`、`y`、`w`、`k`、`u`；其中 `u` 表示未着色，与空位或顶针使用的 `-` 不同。

## 项目结构

```text
ShapeEditor.Core/    图形模型、短代码解析与导出、编辑操作
ShapeEditor.Maui/    Android 应用
ShapeEditor.Web/     Blazor WebAssembly 在线版与 PWA
ShapeEditor.slnx     解决方案
```

WPF 版后续将迁移到同一解决方案，并复用 `ShapeEditor.Core`。

## 本地运行 Web 版

需要安装 .NET 10 SDK。在仓库根目录执行：

```powershell
dotnet restore .\ShapeEditor.Web\ShapeEditor.Web.csproj
dotnet watch --project .\ShapeEditor.Web\ShapeEditor.Web.csproj
```

然后打开终端显示的本地地址。

单独编译核心库：

```powershell
dotnet build .\ShapeEditor.Core\ShapeEditor.Core.csproj
```

Android 版的本地编译还需要 .NET MAUI 工作负载、Android SDK 和 JDK。

## 后续计划

- 将现有 WPF 版迁入本解决方案
- 提供 Windows EXE 下载
- 扩展基于 C# 的图形性质分析功能
- 持续优化 Web 版的移动端体验

## 反馈

如果发现短代码解析、显示或导出问题，欢迎在 [Issues](https://github.com/WyuCnk/ShapeEditor/issues) 中反馈，并尽量附上短代码、预期结果和实际截图。