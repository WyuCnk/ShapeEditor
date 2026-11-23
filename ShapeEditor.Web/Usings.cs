/*
 * Web 端所有 C# 文件共用的命名空间。
 *
 * 工序卡片、交换文案、角工序视图、导出 DTO 与晶体 / 形状样式都已迁到
 * ShapeEditor.Presentation，这里统一导入，避免在几十个文件里各加一行 using。
 * 只导入共享展示模型；Core 的命名空间仍由各文件按需显式导入。
 */
global using ShapeEditor.Presentation.Angles;
global using ShapeEditor.Presentation.Cards;
global using ShapeEditor.Presentation.Exchange;
global using ShapeEditor.Presentation.Export;
global using ShapeEditor.Presentation.Styling;
