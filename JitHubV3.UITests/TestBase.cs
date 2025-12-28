
namespace JitHubV3.UITests;

public class TestBase
{
    private IApp? _app;

    private static readonly object InitLock = new();
    private static bool IsInitialized;

    [OneTimeSetUp]
    public void OneTimeSetUpFixture()
    {
        lock (InitLock)
        {
            if (IsInitialized)
            {
                return;
            }

            AppInitializer.TestEnvironment.AndroidAppName = Constants.AndroidAppName;
            AppInitializer.TestEnvironment.iOSAppName = Constants.iOSAppName;
            AppInitializer.TestEnvironment.iOSDeviceNameOrId = Constants.iOSDeviceNameOrId;
            AppInitializer.TestEnvironment.CurrentPlatform = Constants.CurrentPlatform;

            // Start the app only once, so the tests runs don't restart it.
            try
            {
                AppInitializer.ColdStartApp();
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Assert.Ignore($"UI tests skipped: {ex.Message}");
            }
        }
    }

    protected IApp App
    {
        get => _app!;
        private set
        {
            _app = value;
            Uno.UITest.Helpers.Queries.Helpers.App = value;
        }
    }

    [SetUp]
    public void SetUpTest()
    {
        App = AppInitializer.AttachToApp();
    }

    [TearDown]
    public void TearDownTest()
    {
        TakeScreenshot("teardown");
    }

    public FileInfo TakeScreenshot(string stepName)
    {
        var title = $"{TestContext.CurrentContext.Test.Name}_{stepName}"
            .Replace(" ", "_")
            .Replace(".", "_");

        var fileInfo = App.Screenshot(title);

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
        if (fileNameWithoutExt != title && fileInfo.DirectoryName != null)
        {
            var destFileName = Path
                .Combine(fileInfo.DirectoryName, title + Path.GetExtension(fileInfo.Name));

            if (File.Exists(destFileName))
            {
                File.Delete(destFileName);
            }

            File.Move(fileInfo.FullName, destFileName);

            TestContext.AddTestAttachment(destFileName, stepName);

            fileInfo = new FileInfo(destFileName);
        }
        else
        {
            TestContext.AddTestAttachment(fileInfo.FullName, stepName);
        }

        return fileInfo;
    }

}
