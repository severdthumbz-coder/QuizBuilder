using System.IO;
using System.Text;
using System.Windows;
using QuizBuilder.Core;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IQuizDocumentService _document;

    /// <summary>
    /// Constructor injection. If DI is not wired correctly this throws at
    /// resolve time rather than silently producing an unconfigured window,
    /// which is exactly what a probe should do.
    /// </summary>
    public MainWindow(ISettingsService settings, IQuizDocumentService document)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _document = document ?? throw new ArgumentNullException(nameof(document));

        InitializeComponent();

        // Set from code rather than XAML: the version is only known at runtime,
        // and build.bat stamps it in via -p:InformationalVersion. A hardcoded
        // XAML Title would drift from the actual build immediately.
        Title = $"Quiz Builder {VersionInfo.Display}";

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = BuildDiagnostics();
    }

    /// <summary>
    /// Reports what actually resolved at runtime. Rendering this into the
    /// window rather than a debug log means the answer is visible on the
    /// machine that matters, which is the point of the probe.
    /// </summary>
    private string BuildDiagnostics()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"version      : {VersionInfo.Informational}");
        sb.AppendLine($"build        : {VersionInfo.Build}");
        sb.AppendLine($"runtime      : {Environment.Version}");
        sb.AppendLine($"settings.json: {_settings.SettingsFilePath}");
        sb.AppendLine($"  exists     : {File.Exists(_settings.SettingsFilePath)}");
        sb.AppendLine($"theme        : {_settings.Current.Theme.ActiveThemeId}");
        sb.AppendLine($"token mode   : {_settings.Current.GitHub.TokenProtection}");
        sb.AppendLine($"document     : \"{_document.Current.Title}\" " +
                      $"({_document.Current.Sections.Count} sections)");

        // Verify the theme bridge produced every key the XAML above asks for.
        // A missing key does not throw in WPF -- DynamicResource silently
        // falls back to nothing -- so check explicitly and report.
        var required = new[]
        {
            "Brush.Background", "Brush.Surface", "Brush.TextPrimary",
            "Brush.TextSecondary", "Brush.Primary", "Brush.OnPrimary",
            "Brush.Accent", "Brush.Success", "Brush.Warning", "Brush.Error",
            "Brush.Border", "Brush.BorderStrong", "Brush.SelectedOverlay",
            "Font.Family", "Font.Family.Mono", "Font.Size.Body",
            "Font.Size.Caption", "Font.Size.Display", "Font.Weight.Bold",
            "Thickness.Lg", "Thickness.Md", "Radius.Sm", "Radius.Md",
            "Border.Width",
        };

        var missing = required.Where(k => TryFindResource(k) is null).ToArray();

        sb.AppendLine();
        sb.Append(missing.Length == 0
            ? $"theme bridge : OK ({required.Length} keys resolved)"
            : $"theme bridge : MISSING {missing.Length} key(s): {string.Join(", ", missing)}");

        return sb.ToString();
    }
}
