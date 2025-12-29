namespace JitHubV3.Presentation;

public sealed partial class Shell : UserControl
{
    public Shell()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
        {
            vm.StatusBar.AttachToCurrentThread();
        }
    }

#if WINDOWS || __SKIA__ || __ANDROID__ || __IOS__ || __MACOS__ || __MACCATALYST__
    public ContentControl ContentControl => Splash;
#endif
}
