using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Maps;
using DivisiBill.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
#if WINDOWS
using Sentry.Profiling;
#endif

namespace DivisiBill;

[SupportedOSPlatform("windows10.0.10240.0")]
[SupportedOSPlatform("android21.1")]
[epj.RouteGenerator.AutoRoutes("Page")]
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSentry(options =>
            {
                // The DSN is the only required setting.
                options.Dsn = Generated.BuildInfo.DivisiBillSentryDsn;

                options.Release = Utilities.VersionName;
                options.Environment = Utilities.IsDebug ? "debug" : "production";
                options.AddExceptionFilterForType<OperationCanceledException>(); // Also filters out children, like TaskCanceledException
                options.AddEventProcessor(new Services.SentryEventProcessor());

                // Use debug mode if you want to see what the SDK is doing.
                // Debug messages are written to stdout with Console.Writeline,
                // and are viewable in your IDE's debug console or with 'adb logcat', etc.
                // This option is not recommended when deploying your application.
                options.Debug = false;

                // More detailed diagnostics
                options.IncludeTextInBreadcrumbs = true;
                options.IncludeTitleInBreadcrumbs = true;

                options.AttachScreenshot = true;

                // Set TracesSampleRate to 1.0 to capture 100% of transactions for performance monitoring.
                // We recommend adjusting this value in production.
                options.TracesSampleRate = 1.0;

                // Sample rate for profiling, applied on top of the TracesSampleRate,
                // e.g. 0.2 means we want to profile 20 % of the captured transactions.
                // We recommend adjusting this value in production.
                options.ProfilesSampleRate = 1.0;
#if WINDOWS
                // Requires NuGet package: Sentry.Profiling
                // Note: By default, the profiler is initialized asynchronously. This can
                // be tuned by passing a desired initialization timeout to the constructor.
                options.AddIntegration(new ProfilingIntegration(
                    // During startup, wait up to 500ms to profile the app startup code.
                    // This could make launching the app a bit slower so comment it out if you
                    // prefer profiling to start asynchronously
                    TimeSpan.FromMilliseconds(500)
                ));
#endif
            })
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("materialdesignicons-webfont.ttf", "mdicons");
#if WINDOWS
                fonts.AddFont("Segoe-UI.ttf", "monospace");
#endif
            })
#if WINDOWS
            .UseMauiCommunityToolkitMaps(Generated.BuildInfo.DivisiBillBingMapsSecret); // You should add your own key here from bingmapsportal.com
#else
            .UseMauiMaps();
#endif
        builder.Services.AddTransient<Views.CameraPage>();

        return builder.Build();
    }
}
