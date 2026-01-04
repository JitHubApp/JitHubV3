namespace JitHubV3.Presentation.Controls.ModelPicker;

using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using JitHubV3.Services.Ai.ModelPicker;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

public sealed partial class ModelOrApiPickerOverlay : UserControl
{
    private DependencyObject? _lastFocusedElement;
    private ModelOrApiPickerViewModel? _vm;
    private bool _isAnimating;
    private INotifyCollectionChanged? _categoriesChanged;

    public event EventHandler? Closed;
    public event EventHandler? Confirmed;
    public event EventHandler? Canceled;

    public ModelOrApiPickerOverlay()
    {
        this.InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToViewModel();
        UpdateSidePaneState();
        if (_vm is { IsOpen: true })
        {
            BeginOpen();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachFromViewModel();
    }

    public void Show()
    {
        if (DataContext is ModelOrApiPickerViewModel vm)
        {
            vm.IsOpen = true;
        }
    }

    public void Hide()
    {
        if (DataContext is ModelOrApiPickerViewModel vm)
        {
            vm.IsOpen = false;
        }
    }

    private void AttachToViewModel()
    {
        if (ReferenceEquals(_vm, DataContext))
        {
            return;
        }

        DetachFromViewModel();

        _vm = DataContext as ModelOrApiPickerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;

            _categoriesChanged = _vm.Categories;
            _categoriesChanged.CollectionChanged += OnCategoriesChanged;
        }

        UpdateSidePaneState();
    }

    private void UpdateSidePaneState()
    {
        var showSidePane = _vm?.Categories.Count > 1;
        _ = VisualStateManager.GoToState(this, showSidePane ? "SidePaneVisible" : "SidePaneCollapsed", true);
    }

    private void DetachFromViewModel()
    {
        if (_categoriesChanged is not null)
        {
            _categoriesChanged.CollectionChanged -= OnCategoriesChanged;
            _categoriesChanged = null;
        }

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        }
    }

    private void OnCategoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSidePaneState();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ModelOrApiPickerViewModel.IsOpen), StringComparison.Ordinal))
        {
            return;
        }

        if (_vm is null)
        {
            return;
        }

        if (_vm.IsOpen)
        {
            BeginOpen();
        }
        else
        {
            BeginClose();
        }
    }

    private void BeginOpen()
    {
        if (_isAnimating)
        {
            return;
        }

        _isAnimating = true;
        Root.IsHitTestVisible = true;

        UpdateSidePaneState();

        var xamlRoot = XamlRoot ?? Root.XamlRoot;
        _lastFocusedElement = xamlRoot is null
            ? null
            : FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;

        var storyboard = Root.Resources["ShowOverlayStoryboard"] as Storyboard;
        storyboard?.Stop();

        // Ensure we're visible immediately; storyboard also sets it, but do it defensively.
        Root.Visibility = Visibility.Visible;

        storyboard?.Begin();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            // AI Dev Gallery parity: focus cancel/close on open (gap report 2.1).
            if (FooterCancelButton is not null && FooterCancelButton.Focus(FocusState.Programmatic))
            {
                _isAnimating = false;
                return;
            }

            HeaderCloseButton?.Focus(FocusState.Programmatic);
            _isAnimating = false;
        });
    }

    private void BeginClose()
    {
        if (_isAnimating)
        {
            return;
        }

        _isAnimating = true;

        // Allow interaction with the underlying UI while we animate out.
        Root.IsHitTestVisible = false;

        var storyboard = Root.Resources["HideOverlayStoryboard"] as Storyboard;
        storyboard?.Stop();

        if (storyboard is null)
        {
            CompleteClose();
            return;
        }

        void Completed(object? s, object e)
        {
            storyboard.Completed -= Completed;
            CompleteClose();
        }

        storyboard.Completed += Completed;
        storyboard.Begin();
    }

    private void CompleteClose()
    {
        Root.Visibility = Visibility.Collapsed;

        if (_lastFocusedElement is Control c)
        {
            _ = DispatcherQueue.TryEnqueue(() => c.Focus(FocusState.Programmatic));
        }

        _lastFocusedElement = null;
        _isAnimating = false;

        if (_vm is not null)
        {
            switch (_vm.LastCloseReason)
            {
                case ModelPickerCloseReason.Confirmed:
                    Confirmed?.Invoke(this, EventArgs.Empty);
                    break;
                case ModelPickerCloseReason.Canceled:
                    Canceled?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        Closed?.Invoke(this, EventArgs.Empty);
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
