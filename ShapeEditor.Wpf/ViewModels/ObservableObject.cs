using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 最小实现的属性变更通知基类。
///
/// 项目不引入 MVVM 框架，保持零第三方依赖。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;

        OnPropertyChanged(propertyName);

        return true;
    }
}
