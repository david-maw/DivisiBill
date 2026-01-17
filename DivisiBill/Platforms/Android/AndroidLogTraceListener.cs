#nullable enable
using Android.Util;
using System.Diagnostics;

namespace DivisiBill.Platforms.Android;

public class AndroidLogTraceListener : TraceListener
{
    private readonly string _tag;

    public AndroidLogTraceListener(string tag = "MAUI") => _tag = tag;

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