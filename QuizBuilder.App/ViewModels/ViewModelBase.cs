using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// Hand-rolled INotifyPropertyChanged base.
///
/// The spec calls for CommunityToolkit.Mvvm, and it is the right choice
/// eventually. It is deliberately deferred until the shell pattern is proven
/// on the target machine: the Toolkit works via source generators, and when a
/// generator does not fire the symptom is a missing member at compile time
/// with no obvious cause. Adding it while the surrounding pattern is still
/// unverified would confuse two failure modes.
///
/// Swapping to ObservableObject later is a base-class change and a [ObservableProperty]
/// attribute on each field; the property names and binding paths are unaffected.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Assigns <paramref name="field"/> and raises PropertyChanged when the
    /// value actually changed. Returns true when a change occurred, so callers
    /// can chain dependent updates.
    /// </summary>
    protected bool SetProperty<T>(
        ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
