namespace JitHubV3.Presentation;

public class ActivatablePage : Page
{
    private IActivatableViewModel? _activeVm;

    protected ActivatablePage()
    {
        DataContextChanged += (_, __) => TryActivateFromDataContext();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        TryActivateFromDataContext();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        DeactivateCurrent();
        base.OnNavigatedFrom(e);
    }

    private void TryActivateFromDataContext()
    {
        if (DataContext is not IActivatableViewModel vm)
        {
            return;
        }

        if (ReferenceEquals(_activeVm, vm))
        {
            return;
        }

        DeactivateCurrent();
        _activeVm = vm;

        _ = vm.ActivateAsync();
    }

    private void DeactivateCurrent()
    {
        if (_activeVm is not null)
        {
            _activeVm.Deactivate();
        }

        _activeVm = null;
    }
}
