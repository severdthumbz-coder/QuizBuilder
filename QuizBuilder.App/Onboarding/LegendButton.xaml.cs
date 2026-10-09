using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OnboardingKit;

public partial class LegendButton : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(object), typeof(LegendButton), new PropertyMetadata("❔")); // ❔

    /// <summary>What the button shows. Default ❔.</summary>
    public object Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly DependencyProperty ButtonStyleProperty = DependencyProperty.Register(
        nameof(ButtonStyle), typeof(Style), typeof(LegendButton),
        new PropertyMetadata(null, (d, e) =>
        {
            if (e.NewValue is Style s) ((LegendButton)d).Toggle.Style = s;
        }));

    /// <summary>
    /// Optional: a Style with TargetType="ToggleButton" (or ButtonBase) so the ❔ matches
    /// your other toolbar buttons, e.g. ButtonStyle="{StaticResource IconToggleButton}".
    /// </summary>
    public Style? ButtonStyle
    {
        get => (Style?)GetValue(ButtonStyleProperty);
        set => SetValue(ButtonStyleProperty, value);
    }

    /// <summary>The inner toggle, exposed in case you want to tweak it in code.</summary>
    public ToggleButton Button => Toggle;

    public LegendButton() => InitializeComponent();
}
