using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Views;

/// <summary>
/// Renders a quiz description into a panel, honouring the safelist: bold,
/// italic, line breaks and bullet lists.
///
/// A plain <c>TextBlock Text="{Binding Description}"</c> cannot show any of
/// this -- Text is a single string. WPF's model for mixed formatting is a
/// collection of <see cref="Inline"/>s, which have to be built in code. This
/// attached property does that: set FormattedDescription on a Panel and it
/// fills the panel with the rendered blocks.
///
/// A Panel rather than a TextBlock because a bullet list is several stacked
/// lines with hanging indents, which one TextBlock cannot lay out. Paragraphs
/// become TextBlocks inside the panel; lists become a small Grid per item.
/// </summary>
public static class DescriptionPresenter
{
    public static readonly DependencyProperty FormattedDescriptionProperty =
        DependencyProperty.RegisterAttached(
            "FormattedDescription",
            typeof(string),
            typeof(DescriptionPresenter),
            new PropertyMetadata(null, OnChanged));

    public static string? GetFormattedDescription(DependencyObject obj) =>
        (string?)obj.GetValue(FormattedDescriptionProperty);

    public static void SetFormattedDescription(DependencyObject obj, string? value) =>
        obj.SetValue(FormattedDescriptionProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel) return;

        panel.Children.Clear();

        var text = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var block in DescriptionParser.Parse(text))
        {
            switch (block)
            {
                case DescriptionParagraph paragraph:
                    panel.Children.Add(BuildParagraph(panel, paragraph.Runs));
                    break;

                case DescriptionList list:
                    foreach (var item in list.Items)
                        panel.Children.Add(BuildBullet(panel, item));

                    break;
            }
        }
    }

    private static TextBlock BuildParagraph(Panel panel, IReadOnlyList<DescriptionRun> runs)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };

        Inherit(panel, block);
        AddRuns(block, runs);

        return block;
    }

    /// <summary>
    /// Copies the panel's text appearance onto a generated child.
    ///
    /// Children built in code do not pick up the XAML Style that used to sit on
    /// the single TextBlock, and the theme brushes are DynamicResource. Reading
    /// the panel's resolved values and stamping them keeps the description in the
    /// right colour and font without each caller having to restyle it.
    /// </summary>
    private static void Inherit(Panel panel, TextBlock child)
    {
        child.Foreground = TextElement.GetForeground(panel);
        child.FontFamily = TextElement.GetFontFamily(panel);
        child.FontSize = TextElement.GetFontSize(panel);
    }

    /// <summary>A bullet glyph in its own column, so wrapped text lines up.</summary>
    private static Grid BuildBullet(Panel panel, IReadOnlyList<DescriptionRun> runs)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bullet = new TextBlock { Text = "\u2022" };
        Inherit(panel, bullet);
        Grid.SetColumn(bullet, 0);

        var body = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Inherit(panel, body);
        AddRuns(body, runs);
        Grid.SetColumn(body, 1);

        grid.Children.Add(bullet);
        grid.Children.Add(body);

        return grid;
    }

    private static void AddRuns(TextBlock block, IReadOnlyList<DescriptionRun> runs)
    {
        foreach (var run in runs)
        {
            if (run.IsLineBreak)
            {
                block.Inlines.Add(new LineBreak());
                continue;
            }

            Inline inline = new Run(run.Text);

            // Order does not matter: Bold wrapping Italic and Italic wrapping
            // Bold render the same. Both wrappers are applied when both flags
            // are set, giving bold-italic.
            if (run.Bold) inline = new Bold(inline);
            if (run.Italic) inline = new Italic(inline);

            block.Inlines.Add(inline);
        }
    }
}
