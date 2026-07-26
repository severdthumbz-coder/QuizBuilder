using System.Windows;
using System.Windows.Input;

namespace QuizBuilder.App.Controls;

/// <summary>
/// WPF's missing :focus-visible.
///
/// The problem: WPF sets IsKeyboardFocused whenever an element holds focus,
/// regardless of how focus arrived. So a focus ring bound to IsKeyboardFocused
/// appears when the window merely opens (the first tab stop is focused
/// automatically) and when the user clicks with a mouse. Both are noise: a
/// focus ring is meant to answer "where will my keyboard go next?", which only
/// matters once someone is actually using a keyboard.
///
/// CSS solved this with :focus-visible. WPF has no equivalent, and the usual
/// workarounds (moving focus elsewhere on load, or suppressing FocusVisualStyle)
/// either fight the focus system or disable rings entirely, which breaks
/// keyboard accessibility.
///
/// This tracks the last input device application-wide. Rings show only after a
/// real keyboard interaction, and hide again on the next mouse click. Bind a
/// ring's Visibility to the attached IsFocusVisible property and it behaves the
/// way users expect on every other platform.
/// </summary>
public static class FocusVisible
{
    private static bool _keyboardWasLastInput;
    private static bool _hooked;

    /// <summary>
    /// True when this element has keyboard focus AND the user's most recent
    /// input came from a keyboard.
    /// </summary>
    public static readonly DependencyProperty IsFocusVisibleProperty =
        DependencyProperty.RegisterAttached(
            "IsFocusVisible",
            typeof(bool),
            typeof(FocusVisible),
            new PropertyMetadata(false));

    public static bool GetIsFocusVisible(DependencyObject obj)
        => (bool)obj.GetValue(IsFocusVisibleProperty);

    public static void SetIsFocusVisible(DependencyObject obj, bool value)
        => obj.SetValue(IsFocusVisibleProperty, value);

    /// <summary>
    /// Set to true on a control to opt it into focus-visible tracking.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(FocusVisible),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if (e.NewValue is true)
        {
            EnsureHooked();
            element.GotKeyboardFocus += OnGotKeyboardFocus;
            element.LostKeyboardFocus += OnLostKeyboardFocus;
        }
        else
        {
            element.GotKeyboardFocus -= OnGotKeyboardFocus;
            element.LostKeyboardFocus -= OnLostKeyboardFocus;
        }
    }

    /// <summary>
    /// Hooks the global input stream once. Static because the "was the last
    /// input a keyboard?" question is application-wide, not per-control.
    /// </summary>
    private static void EnsureHooked()
    {
        if (_hooked) return;
        _hooked = true;

        // PreviewInputEvent sees every input before it is routed, so the flag
        // is already correct by the time GotKeyboardFocus fires.
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            switch (e.StagingItem.Input)
            {
                case KeyEventArgs:
                    _keyboardWasLastInput = true;
                    break;

                // A mouse click hides rings again, matching what users expect
                // from :focus-visible elsewhere.
                case MouseButtonEventArgs:
                    _keyboardWasLastInput = false;
                    RefreshAll();
                    break;
            }
        };
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DependencyObject d)
            SetIsFocusVisible(d, _keyboardWasLastInput);
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DependencyObject d)
            SetIsFocusVisible(d, false);
    }

    /// <summary>
    /// Clears the ring on whatever currently holds focus, so clicking with the
    /// mouse hides a ring that a previous Tab left behind.
    /// </summary>
    private static void RefreshAll()
    {
        if (Keyboard.FocusedElement is DependencyObject focused)
            SetIsFocusVisible(focused, false);
    }
}
