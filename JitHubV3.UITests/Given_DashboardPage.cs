using FluentAssertions;

namespace JitHubV3.UITests;

public sealed class Given_DashboardPage : TestBase
{
    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_Navigating_to_dashboard_test_shows_key_surfaces()
    {
        // Allow the shell + login page to load.
        await Task.Delay(5000);

        App.WaitForElement(q => q.All().Marked("DashboardTestButton"));
        App.Tap(q => q.All().Marked("DashboardTestButton"));

        App.WaitForElement(q => q.All().Marked("DashboardNavView"));
        App.WaitForElement(q => q.All().Marked("DashboardComposeBox"));
        App.WaitForElement(q => q.All().Marked("DashboardComposeInput"));

        TakeScreenshot("Dashboard loaded");
    }

    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_Navigating_to_dashboard_test_shows_notifications_card_surface()
    {
        // Allow the shell + login page to load.
        await Task.Delay(5000);

        App.WaitForElement(q => q.All().Marked("DashboardTestButton"));
        App.Tap(q => q.All().Marked("DashboardTestButton"));

        App.WaitForElement(q => q.All().Marked("DashboardCard.Notifications"));
        TakeScreenshot("Notifications card surface");
    }

    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_Navigating_to_dashboard_test_shows_my_activity_card_surface()
    {
        // Allow the shell + login page to load.
        await Task.Delay(5000);

        App.WaitForElement(q => q.All().Marked("DashboardTestButton"));
        App.Tap(q => q.All().Marked("DashboardTestButton"));

        App.WaitForElement(q => q.All().Marked("DashboardCard.MyRecentActivity"));
        TakeScreenshot("My activity card surface");
    }

    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_Narrow_can_open_sidebar()
    {
        await Task.Delay(5000);

        App.WaitForElement(q => q.All().Marked("DashboardTestButton"));
        App.Tap(q => q.All().Marked("DashboardTestButton"));
        App.WaitForElement(q => q.All().Marked("DashboardNavView"));

        // Portrait should trigger the Narrow visual state on phones/tablets.
        // (On platforms where orientation is not supported, this is a no-op.)
        try { App.SetOrientationPortrait(); } catch { }

        // In narrow mode the pane is closed; repo list shouldn't be visible until opened.
        try
        {
            App.WaitForNoElement(q => q.All().Marked("DashboardRepoList"), timeout: TimeSpan.FromSeconds(2));
        }
        catch
        {
            // If the platform doesn't support pane toggling, don't fail here.
        }

        // NavigationView uses a standard toggle button automation id.
        // If this fails on a target, it's a strong signal we need a dedicated AutomationId for the pane toggle.
        App.Tap(q => q.All().Marked("TogglePaneButton"));

        App.WaitForElement(q => q.All().Marked("DashboardRepoList"));
        TakeScreenshot("Sidebar opened (narrow)");
    }

    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_Wide_compose_does_not_overlap_status_bar()
    {
        await Task.Delay(5000);

        App.WaitForElement(q => q.All().Marked("DashboardTestButton"));
        App.Tap(q => q.All().Marked("DashboardTestButton"));
        App.WaitForElement(q => q.All().Marked("DashboardComposeBox"));

        // Landscape should trigger the Wide visual state on phones/tablets.
        try { App.SetOrientationLandscape(); } catch { }

        // Validate we can see the shell bottom status bar and that the compose box stays above it.
        App.WaitForElement(q => q.All().Marked("ShellBottomStatusBar"));

        var compose = App.Query(q => q.All().Marked("DashboardComposeBox")).Single();
        var statusBar = App.Query(q => q.All().Marked("ShellBottomStatusBar")).Single();

        var composeBottom = compose.Rect.Y + compose.Rect.Height;
        var statusTop = statusBar.Rect.Y;
        composeBottom.Should().BeLessThanOrEqualTo(statusTop + 1);

        TakeScreenshot("Compose above status bar (wide)");
    }
}
