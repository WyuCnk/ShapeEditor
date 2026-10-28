using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;

namespace ShapeEditor.Wpf.Services;

/// <summary>
/// 文件对话框与消息提示的薄封装。
/// </summary>
public sealed class DialogService
{
    private const string JsonFilter =
        "存档文件 (*.json)|*.json|所有文件 (*.*)|*.*";

    private const string TextFilter =
        "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

    private const string PngFilter =
        "PNG 图片 (*.png)|*.png";

    public string? OpenSaveFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入存档",
            Filter = JsonFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? SaveSaveFile(
        string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出存档",
            Filter = JsonFilter,
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = ".json"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? OpenTextFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入短代码文本",
            Filter = TextFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? SavePngFile(
        string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出图片",
            Filter = PngFilter,
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = ".png"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public void ShowError(
        string message)
    {
        MessageBox.Show(
            message,
            "ShapeEditor",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public void ShowInfo(
        string message)
    {
        MessageBox.Show(
            message,
            "ShapeEditor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public static string BuildTimestampedName(
        string prefix,
        string extension)
    {
        return $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
    }

    /// <summary>
    /// 读取文本文件，优先 UTF-8，失败时回退到 GB18030。
    /// </summary>
    public static string ReadTextFile(
        string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        try
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }
}
