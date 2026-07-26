using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Interfaces;

public sealed class ThemeChangedEventArgs : EventArgs
{
    public ThemeChangedEventArgs(ThemeTokens tokens) => Tokens = tokens;

    public ThemeTokens Tokens { get; }
}

/// <summary>
/// Owns which theme is active and what its tokens are.
///
/// Lives in Core, not the App project, because the PDF and HTML exporters need
/// the same tokens without taking a WPF dependency. The App layer subscribes to
/// ThemeChanged and rebuilds its ResourceDictionary; the exporters read Current
/// directly.
/// </summary>
public interface IThemeService
{
    /// <summary>Tokens for the active theme: a built-in, or the custom one.</summary>
    ThemeTokens Current { get; }

    /// <summary>The five built-in themes, for the picker.</summary>
    IReadOnlyList<ThemeTokens> BuiltIn { get; }

    /// <summary>
    /// The user's custom theme. Null until they create one. Kept separate from
    /// Current so switching to a built-in and back does not lose their edits.
    /// </summary>
    ThemeTokens? Custom { get; }

    /// <summary>True when Current is the custom theme rather than a built-in.</summary>
    bool IsCustomActive { get; }

    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>Activates a built-in theme by id.</summary>
    void SelectBuiltIn(string themeId);

    /// <summary>
    /// Creates the custom theme by cloning a starting point, then activates it.
    /// Cloning rather than referencing matters: editing must not mutate the
    /// built-in, which is a shared static.
    /// </summary>
    void CreateCustomFrom(string themeId);

    /// <summary>Activates the existing custom theme. No-op when none exists.</summary>
    void SelectCustom();

    /// <summary>
    /// Applies an edit to the custom theme and raises ThemeChanged. The action
    /// receives the live custom tokens to mutate.
    /// </summary>
    void EditCustom(Action<ThemeTokens> edit);

    /// <summary>Discards the custom theme and falls back to a built-in.</summary>
    void DeleteCustom();

    /// <summary>
    /// Takes a snapshot of the committed custom theme, so <see cref="DiscardChanges"/>
    /// has something to go back to.
    ///
    /// Called when the editor is opened. Without it there is no record of what
    /// the theme looked like before the user started experimenting: edits go
    /// straight into the live object, and the only way out was DeleteCustom,
    /// which throws the whole theme away rather than the last few changes.
    /// </summary>
    void BeginEdit();

    /// <summary>
    /// Puts the custom theme back to the last snapshot, touching memory only.
    ///
    /// A theme created since the snapshot is removed entirely: discarding an
    /// experiment that was never committed should leave no trace of it.
    /// </summary>
    void DiscardChanges();

    /// <summary>
    /// True when the custom theme differs from the snapshot -- i.e. there is
    /// something to save or discard.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Persists the current selection to settings.</summary>
    void Save();
}
