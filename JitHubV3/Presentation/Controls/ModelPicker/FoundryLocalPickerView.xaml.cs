using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class FoundryLocalPickerView : UserControl
{
    public FoundryLocalPickerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    internal FoundryLocalPickerViewModel? ViewModel => DataContext as FoundryLocalPickerViewModel;

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        UpdateVisualState();

        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FoundryLocalPickerViewModel.IsLoading)
                    or nameof(FoundryLocalPickerViewModel.IsNotAvailable)
                    or nameof(FoundryLocalPickerViewModel.ShowModels))
                {
                    UpdateVisualState();
                }
            };
        }
    }

    private void UpdateVisualState()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            VisualStateManager.GoToState(this, "ShowLoading", true);
            return;
        }

        if (vm.IsLoading)
        {
            VisualStateManager.GoToState(this, "ShowLoading", true);
            return;
        }

        if (vm.IsNotAvailable)
        {
            VisualStateManager.GoToState(this, "ShowNotAvailable", true);
            return;
        }

        VisualStateManager.GoToState(this, "ShowModels", true);
    }

    public static Visibility BoolToVisibilityInverse(bool value)
        => value ? Visibility.Collapsed : Visibility.Visible;
}
