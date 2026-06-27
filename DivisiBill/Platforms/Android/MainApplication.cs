using Android.App;
using Android.Runtime;

namespace DivisiBill.Platforms.Android;


// This is the main application class for the Android platform in a .NET MAUI application.
// Allow cleartext traffic (http://) for debug builds so as to reach the debug web service.
[Application(UsesCleartextTraffic =
#if DEBUG
    true
#else
    false
#endif
)]
public class MainApplication(nint handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
