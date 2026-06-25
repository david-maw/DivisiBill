using AndroidX.Activity;
using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;
using global::Android.Views;
using global::Android.Widget;

namespace DivisiBill.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
[IntentFilter(
    [Intent.ActionView, Intent.ActionOpenDocument, Intent.ActionGetContent],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "application/zip"
    )]
[IntentFilter(
    [Intent.ActionView, Intent.ActionOpenDocument, Intent.ActionGetContent],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "application/xml"
    )]
[IntentFilter(
    [Intent.ActionView, Intent.ActionOpenDocument, Intent.ActionGetContent],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "text/xml"
    )]
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "application/zip"
)]
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "application/xml"
)]
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataMimeType = "text/xml"
)]
public class MainActivity : MauiAppCompatActivity
{
    // Indicates that the process was started via a file intent
    public static bool IsIntentLaunch { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (!System.Diagnostics.Debugger.IsAttached) // log the messages only if there's no debugger listening
            System.Diagnostics.Trace.Listeners.Add(new AndroidLogTraceListener());
        Services.Utilities.DebugMsg($"MainActivity.OnCreate: MainActivity created: Intent = {Intent?.Action}");

        // Evaluate before base.OnCreate so MAUI can query this very early
        IsIntentLaunch = IsFileIntent(Intent);

        base.OnCreate(savedInstanceState);
        if (Intent?.Action == Intent.ActionMain)
        {
            Platform.Init(this, savedInstanceState);
            OnBackPressedDispatcher.AddCallback(this, new BackPress(this));
            // As long as we're forced to show an inset area in .NET10 as of 11/19/25, use it to display status
            // Window.AddFlags(WindowManagerFlags.Fullscreen); 
            Window?.SetSoftInputMode(SoftInput.AdjustPan);
        }
        else if (Intent is not null)
            HandleIntent(Intent);
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }
    private static bool IsFileIntent(Intent? intent)
    {
        if (intent is null)
            return false;

        string? action = intent.Action;
        return action is Intent.ActionView
            or Intent.ActionOpenDocument
            or Intent.ActionGetContent
            or Intent.ActionSend;
    }
    private void HandleIntent(Intent? intent)
    {
        if (intent is null || !IsFileIntent(intent))
            return;

        global::Android.Net.Uri? uri;
        string? mimeType;

        if (intent.Action == Intent.ActionSend)
        {
            uri = GetUriFromIntent(intent);
            mimeType = intent.Type;
        }
        else
        {
            uri = intent.Data;
            mimeType = intent.Type;
        }

        if (uri is not null && !string.IsNullOrEmpty(mimeType))
            StreamDispatcher.Dispatch(uri, mimeType);
    }
    private global::Android.Net.Uri? GetUriFromIntent(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // New API (Android 13+)
            return intent.GetParcelableExtra(
                Intent.ExtraStream,
                Java.Lang.Class.FromType(typeof(global::Android.Net.Uri))
            ) as global::Android.Net.Uri;
        }
        else
        {
            // Old API (Android 12 and below)
            return intent.GetParcelableExtra(Intent.ExtraStream) as global::Android.Net.Uri;
        }
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
                activity.Finish();
            else
            {
                Toast.MakeText(activity, "Repeat to Close", ToastLength.Short)?.Show();
                backPressed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
    }
}
