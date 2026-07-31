using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class GitHubView : UserControl
{
    private readonly GitHubViewModel _viewModel;

    public GitHubView(GitHubViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnActivated();
        };
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        // Read at the point of use. The token is never assigned to a field or a
        // bound property, so it lives as a local for the length of one call.
        await _viewModel.ConnectAsync(TokenBox.Password);

        // Clear on success: the token is stored encrypted now, and leaving it in
        // the box means it sits in a UI element for the rest of the session.
        if (_viewModel.HasStoredToken) TokenBox.Clear();
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Forget the stored GitHub token on this machine?",
            "Forget token",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirm != MessageBoxResult.OK) return;

        _viewModel.Disconnect();
        TokenBox.Clear();
    }

    private async void OnPublishClick(object sender, RoutedEventArgs e)
    {
        // Prefer what is typed; fall back to the stored token. Someone who has
        // just pasted a token should not have to press Connect first.
        var typed = TokenBox.Password;

        await _viewModel.PublishAsync(string.IsNullOrWhiteSpace(typed) ? null : typed);
    }

    private void OnOpenPublishedClick(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.LastPublishedUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the link: {ex.Message}",
                "Open published page",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCopyApkLinkClick(object sender, RoutedEventArgs e)
    {
        var link = _viewModel.NormalizedApkLink;
        if (string.IsNullOrWhiteSpace(link)) return;

        try
        {
            Clipboard.SetText(link);
        }
        catch (Exception ex)
        {
            // The clipboard can be transiently locked by another app; a failed
            // copy is a minor annoyance, not worth more than a quiet note.
            MessageBox.Show(
                $"Could not copy the link: {ex.Message}",
                "Copy link",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnSaveQrClick(object sender, RoutedEventArgs e)
    {
        var png = _viewModel.QrPng;
        if (png is not { Length: > 0 }) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save QR image",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = "quiz-app-qr.png",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            System.IO.File.WriteAllBytes(dialog.FileName, png);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save the image: {ex.Message}",
                "Save QR image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
