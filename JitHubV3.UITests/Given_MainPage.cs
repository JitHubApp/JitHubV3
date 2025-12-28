namespace JitHubV3.UITests;

public class Given_MainPage : TestBase
{
    [Test]
    [Explicit("Requires a configured UI test target platform/device.")]
    public async Task When_SmokeTest()
    {
        // Add delay to allow for the splash screen to disappear
        await Task.Delay(5000);

        // Validate that the app shell has loaded by detecting the Login page.
        // This test intentionally does not attempt interactive GitHub login.
        Query loginButton = q => q.All().Marked("LoginButton");
        App.WaitForElement(loginButton);

        // Take a screenshot and add it to the test results
        TakeScreenshot("Login page loaded");
    }
}
