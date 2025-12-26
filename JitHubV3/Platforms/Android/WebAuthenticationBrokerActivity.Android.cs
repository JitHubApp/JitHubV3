using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace JitHubV3.Droid;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Android.Content.Intent.ActionView },
    Categories = new[] { Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable },
    DataScheme = "jithubv3")]
public class WebAuthenticationBrokerActivity : Uno.AuthenticationBroker.WebAuthenticationBrokerActivityBase
{
}
