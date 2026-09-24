namespace ShapeEditor.Core.Models;

public static class ShapeColor
{
    public static readonly char[] All =
    {
        'r', 'g', 'b', 'c', 'm',
        'y', 'w', 'k', 'u'
    };

    public static bool IsValid(char code)
    {
        return code is
            'r' or
            'g' or
            'b' or
            'c' or
            'm' or
            'y' or
            'w' or
            'k' or
            'u';
    }

    public static string GetName(char? code)
    {
        return code switch
        {
            'r' => "红色",
            'g' => "绿色",
            'b' => "蓝色",
            'c' => "青色",
            'm' => "洋红色",
            'y' => "黄色",
            'w' => "白色",
            'k' => "黑色",
            'u' => "未着色",
            _ => "无颜色"
        };
    }
}