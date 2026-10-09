using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace OnboardingKit;

/// <summary>Self-contained ICommand so the kit has no dependency on your MVVM helpers.</summary>
public sealed class OnbCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public OnbCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

/// <summary>true → Visible, false → Collapsed. Pass ConverterParameter="Invert" to flip.</summary>
public sealed class OnbBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && (v == Visibility.Visible) ^ (parameter as string == "Invert");
}

/// <summary>Logical NOT, for IsEnabled="{Binding IsFirstStep, Converter=...}".</summary>
public sealed class OnbInverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
