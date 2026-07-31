using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _vm;

    public HistoryPage(HistoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // The VM subscribes to the singleton history service only while this page is
    // on screen. Pages are transient and nothing disposes their BindingContext,
    // so a constructor-time subscription would leak dead VMs into the singleton
    // and leave stale VMs reacting to later changes. Appearing also re-reads the
    // list, so returning from an attempt's detail reflects any change made there.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Attach();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }
}
