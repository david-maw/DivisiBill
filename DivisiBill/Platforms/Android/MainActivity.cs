#nullable enable

using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Activity;
using Android.Views;
using Android.Content;
using Android.Util;

namespace DivisiBill.Platforms.ShouldBeAndroid;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
[IntentFilter(
    [Intent.ActionView, Intent.ActionOpenDocument, Intent.ActionGetContent],
    Categories = [ Intent.CategoryDefault, Intent.CategoryBrowsable ],
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
        if (!System.Diagnostics.Debugger.IsAttached) // log the messages if there's no debugger listening
            System.Diagnostics.Trace.Listeners.Add(new AndroidLogTraceListener("DivisiBill"));
        Log.Debug("OnCreate", $"MainActivity created: Intent = {Intent?.Action}");

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
    static bool IsFileIntent(Intent? intent)
    {
        if (intent is null)
            return false;

        var action = intent.Action;
        return action == Intent.ActionView
            || action == Intent.ActionOpenDocument
            || action == Intent.ActionGetContent
            || action == Intent.ActionSend;
    }
    void HandleIntent(Intent? intent)
    {
        if (intent is null || !IsFileIntent(intent))
            return;

        Android.Net.Uri? uri;
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
    Android.Net.Uri? GetUriFromIntent(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // New API (Android 13+)
            return intent.GetParcelableExtra(
                Intent.ExtraStream,
                Java.Lang.Class.FromType(typeof(Android.Net.Uri))
            ) as Android.Net.Uri;
        }
        else
        {
            // Old API (Android 12 and below)
            return intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
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
