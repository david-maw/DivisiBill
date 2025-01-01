using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using AndroidX.Activity;

namespace DivisiBill;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Platform.Init(this, savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new BackPress(this));
    }
    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data) => base.OnActivityResult(requestCode, resultCode, data);
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }
}
class BackPress : OnBackPressedCallback
{
    private readonly Activity activity;
    private long backPressed;

    public BackPress(Activity activity) : base(true)
    {
        this.activity = activity;
    }

    public override void HandleOnBackPressed()
    {
        var shell = Shell.Current as AppShell;
        if (shell is null || !shell.HandleBackRequest())
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

