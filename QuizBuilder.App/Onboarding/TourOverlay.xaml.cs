using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OnboardingKit;

public partial class TourOverlay : UserControl
{
    public TourOverlay()
    {
        InitializeComponent();

        // When the tour opens or moves to another step, put keyboard focus on "Next"
        // so Enter / arrows / Esc work immediately without clicking the card first.
        var c = OnboardingController.Current;
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (c.IsTourActive && (e.PropertyName == nameof(OnboardingController.IsTourActive)
                                || e.PropertyName == nameof(OnboardingController.StepIndex)))
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => NextButton.Focus()));
        };
        Loaded += (_, _) => c.PropertyChanged += handler;
        Unloaded += (_, _) => c.PropertyChanged -= handler;
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var c = OnboardingController.Current;
        if (!c.IsTourActive) return;

        switch (e.Key)
        {
            case Key.Escape: c.FinishTour(); e.Handled = true; break;
            case Key.Right: c.Next(); e.Handled = true; break;
            // Enter on a focused button (Back / Skip) should press THAT button, so only
            // treat Enter as "Next" when focus isn't already on a button.
            case Key.Enter when Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase:
                c.Next(); e.Handled = true; break;
            case Key.Left: c.Back(); e.Handled = true; break;
        }
    }
}
