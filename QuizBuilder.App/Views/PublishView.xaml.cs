using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuizBuilder.App.ViewModels;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.Views;

public partial class PublishView : UserControl
{
    private readonly PublishViewModel _viewModel;

    public PublishView(PublishViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // Settings and the theme live on other tabs and raise no document
        // event, so the summary would otherwise describe a stale paper.
        IsVisibleChanged += (_, e) =>
        {
            // Both branches matter. The shell keeps every tab alive and toggles
            // Visibility, so without the else the ViewModel would latch to
            // visible on first activation and never defer again -- the tab would
            // go on doing full rebuilds while the user typed on another tab.
            if (e.NewValue is true) _viewModel.OnActivated();
            else _viewModel.OnDeactivated();
        };
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Web page (*.html)|*.html|All files (*.*)|*.*",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = _viewModel.SuggestFileName(),
            Title = "Export web page",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.ExportHtmlAsync(dialog.FileName);
    }

    private async void OnExportWebQuizClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Web page (*.html)|*.html|All files (*.*)|*.*",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = _viewModel.SuggestFileName(".html", " quiz"),
            Title = "Export self-grading quiz",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.ExportWebAsync(dialog.FileName);
    }

    private async void OnExportWordClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Word document (*.docx)|*.docx|All files (*.*)|*.*",
            DefaultExt = ".docx",
            AddExtension = true,
            FileName = _viewModel.SuggestFileName(".docx"),
            Title = "Export Word document",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.ExportWordAsync(dialog.FileName);
    }

    private async void OnExportExcelClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = _viewModel.SuggestFileName(".xlsx"),
            Title = "Export spreadsheet",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.ExportExcelAsync(dialog.FileName);
    }

    private async void OnImportExcelClick(object sender, RoutedEventArgs e)
    {
        // Import replaces the whole quiz. Doing that to unsaved work without
        // asking would be unforgivable, and the ViewModel cannot ask because a
        // ViewModel that opens dialogs cannot be tested.
        if (_viewModel.ImportWouldDiscardChanges)
        {
            var confirm = MessageBox.Show(
                "Importing will replace every question in the current quiz, and you have unsaved changes.\n\n"
                + "Continue?",
                "Import spreadsheet",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (confirm != MessageBoxResult.OK) return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = ".xlsx",
            Title = "Import spreadsheet",
        };

        if (dialog.ShowDialog() != true) return;

        var result = await _viewModel.ImportExcelAsync(dialog.FileName);

        if (!result.Success)
        {
            ShowProblems(
                result.Error ?? "That file could not be read.",
                result.Problems,
                MessageBoxImage.Warning);

            return;
        }

        if (result.Problems.Count > 0)
        {
            // Every note, not a count. "3 problems" tells the author nothing
            // they can act on; "Row 9: no Type" sends them to the cell.
            ShowProblems(
                $"Imported {result.QuestionCount} question{(result.QuestionCount == 1 ? "" : "s")}, "
                + "but some rows needed attention:",
                result.Problems,
                MessageBoxImage.Information);
        }
    }

    private static void ShowProblems(string heading, IReadOnlyList<string> problems, MessageBoxImage icon)
    {
        var text = problems.Count == 0
            ? heading
            : heading + "\n\n" + string.Join("\n", problems.Take(15))
              + (problems.Count > 15 ? $"\n...and {problems.Count - 15} more." : string.Empty);

        MessageBox.Show(text, "Import spreadsheet", MessageBoxButton.OK, icon);
    }

    private void OnOpenExportClick(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.LastExportPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            // UseShellExecute opens it in the default browser. Without it,
            // .NET Core tries to execute the .html as a program and throws.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the file: {ex.Message}",
                "Open export",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
