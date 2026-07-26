using System.Windows.Input;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// Minimal ICommand. Replaced by CommunityToolkit.Mvvm's [RelayCommand] once
/// the shell pattern is confirmed working; see ViewModelBase for why that is
/// deferred.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>
    /// Routed through CommandManager so WPF re-queries CanExecute whenever the
    /// UI state changes. Without this, a command that becomes executable does
    /// not re-enable its button until something else forces a requery.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Forces a CanExecute re-query for every command.</summary>
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
