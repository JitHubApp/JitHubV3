namespace JitHubV3.UITests;

public class Given_ModelPickerOverlay : TestBase
{
    [Test]
    [Explicit("Requires a configured UI test target platform/device and an authenticated session.")]
    public void When_OpenAndCancel_ModelPicker_ClosesOverlay()
    {
        Query openButton = q => q.All().Marked("DashboardComposeAiSettings");

        // If we're not on the dashboard (e.g., still on Login), skip.
        if (!App.Query(openButton).Any())
        {
            Assert.Ignore("Dashboard not available (likely not authenticated). Skipping model picker UI test.");
        }

        App.Tap(openButton);

        Query overlayDismiss = q => q.All().Marked("ModelPicker.Dismiss");
        App.WaitForElement(overlayDismiss);

        Query cancel = q => q.All().Marked("ModelPicker.Cancel");
        App.WaitForElement(cancel);
        App.Tap(cancel);

        App.WaitForNoElement(overlayDismiss);
        TakeScreenshot("ModelPicker_closed");
    }

    [Test]
    [Explicit("Requires a configured UI test target platform/device and an authenticated session.")]
    public void When_Open_ModelPicker_ShowsPrimaryAction()
    {
        Query openButton = q => q.All().Marked("DashboardComposeAiSettings");
        if (!App.Query(openButton).Any())
        {
            Assert.Ignore("Dashboard not available (likely not authenticated). Skipping model picker UI test.");
        }

        App.Tap(openButton);

        Query primary = q => q.All().Marked("ModelPicker.PrimaryAction");
        App.WaitForElement(primary);

        TakeScreenshot("ModelPicker_primary_action_visible");
    }
}
