using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Activity;
using Android.Views;

namespace DivisiBill.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        System.Diagnostics.Trace.Listeners.Add(new AndroidLogTraceListener("DivisiBill"));
        Log.Debug("OnCreate", $"MainActivity created: Intent = {Intent?.Action}");
        base.OnCreate(savedInstanceState);
        Platform.Init(this, savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new BackPress(this));
        // As long as we're forced to show an inset area in .NET10 as of 11/19/25, use it to display status
        // Window.AddFlags(WindowManagerFlags.Fullscreen); 
        Window.SetSoftInputMode(SoftInput.AdjustPan);
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }
}

internal class BackPress(Activity activity) : OnBackPressedCallback(true)
{
    private long backPressed;

    public override void HandleOnBackPressed()
    {
        if (Shell.Current is not AppShell shell || !shell.HandleBackRequest())
        {
            const int delay = 2000; // same as the lifetime of the toast
            if (backPressed + delay > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                activity.FinishAndRemoveTask();
                Process.KillProcess(Process.MyPid());
            }
            else
            {
                Toast.MakeText(activity, "Repeat to Close", ToastLength.Short)?.Show();
                backPressed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
    }
}

