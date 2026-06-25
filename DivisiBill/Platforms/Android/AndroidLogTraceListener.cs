using Android.Util;
using System.Diagnostics;

namespace DivisiBill.Platforms.Android;

public class AndroidLogTraceListener(string tag = "MAUI") : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Log.Debug(tag, message);
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Log.Debug(tag, message);
    }
}