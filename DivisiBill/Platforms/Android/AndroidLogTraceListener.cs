#nullable enable
using System.Diagnostics;
using Android.Util;

namespace DivisiBill.Platforms.ShouldBeAndroid;

public class AndroidLogTraceListener : TraceListener
{
    private readonly string _tag;

    public AndroidLogTraceListener(string tag = "MAUI")
    {
        _tag = tag;
    }

    public override void Write(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Log.Debug(_tag, message);
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Log.Debug(_tag, message);
    }
}