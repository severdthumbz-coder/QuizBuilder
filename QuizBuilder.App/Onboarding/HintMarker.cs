using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace OnboardingKit;

/// <summary>
/// The subtle ⓘ info bubble. Place it next to any header:
/// <code>&lt;ob:HintMarker Hint="What this section does…" Margin="8,0,0,0"/&gt;</code>
/// Shows a wrapped tooltip on hover (fast 200 ms open, 20 s stay), uses the accent colour,
/// and hides itself automatically when "Show onboarding helpers" is switched off.
/// Works in any DataContext — it binds straight to <see cref="OnboardingController.Current"/>.
/// </summary>
public class HintMarker : TextBlock
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(HintMarker),
        new PropertyMetadata(null, (d, e) => ((HintMarker)d).UpdateTip()));

    /// <summary>The tooltip text. Plain text, wraps at ~320 px.</summary>
    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public HintMarker()
    {
        Text = "ⓘ";                       // ⓘ
        FontSize = 13;
        Cursor = Cursors.Help;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;
        SetResourceReference(ForegroundProperty, "OnbAccentBrush");
        ToolTipService.SetInitialShowDelay(this, 200);
        ToolTipService.SetShowDuration(this, 20000);

        SetBinding(VisibilityProperty, new Binding(nameof(OnboardingController.ShowHelpers))
        {
            Source = OnboardingController.Current,
            Converter = new OnbBoolToVisibilityConverter()
        });
    }

    private void UpdateTip()
    {
        ToolTip = string.IsNullOrWhiteSpace(Hint)
            ? null
            : new ToolTip
            {
                MaxWidth = 320,
                Content = new TextBlock { Text = Hint, TextWrapping = TextWrapping.Wrap }
            };
    }
}
