using System.Windows.Input;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 简单的中继命令。
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;

    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(
        Action execute,
        Func<bool>? canExecute = null)
        : this(
            _ => execute(),
            canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(
        Action<object?> execute,
        Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(
        object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(
        object? parameter)
    {
        _execute(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
