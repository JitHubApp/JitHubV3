namespace JitHubV3.Presentation.Controls.ModelPicker;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class ModelOrApiPickerOverlay : UserControl
{
    public ModelOrApiPickerOverlay()
    {
        this.InitializeComponent();

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Best-effort focus: keep keyboard users inside the dialog.
        if (DataContext is ModelOrApiPickerViewModel { IsOpen: true })
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                CategoryList?.Focus(FocusState.Programmatic);
            });
        }
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        if (DataContext is ModelOrApiPickerViewModel vm)
        {
            if (vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
