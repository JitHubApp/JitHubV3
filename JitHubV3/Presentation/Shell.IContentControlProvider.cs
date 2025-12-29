#if WINDOWS || __SKIA__ || __ANDROID__ || __IOS__ || __MACOS__ || __MACCATALYST__
using Uno.Extensions.Navigation.UI;

namespace JitHubV3.Presentation;

public sealed partial class Shell : IContentControlProvider
{
}
#endif
