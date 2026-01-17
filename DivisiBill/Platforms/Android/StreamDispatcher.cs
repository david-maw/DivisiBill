#nullable enable
using Android.Content;

namespace DivisiBill.Platforms.Android;

public static class StreamDispatcher
{
    public static event Action<Stream, string>? Activated;

    public static void Dispatch(global::Android.Net.Uri uri, string mimeType)
    {
        Context context = global::Android.App.Application.Context;
        Stream? stream = context.ContentResolver?.OpenInputStream(uri);

        if (stream is not null)
        {
            Services.Utilities.DebugMsg($"In StreamDispatcher.Dispatch: Notifying stream with MIME type: {mimeType}");
            Activated?.Invoke(stream, mimeType);
            App.Current.IntentQueue.Enqueue(new Services.StreamRequest(stream, mimeType));
        }
    }
}