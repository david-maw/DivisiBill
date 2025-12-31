#nullable enable
namespace DivisiBill;

public static class StreamDispatcher
{
    public static event Action<Stream, string>? Activated;

    public static void Dispatch(Android.Net.Uri uri, string mimeType)
    {
        var context = Android.App.Application.Context;
        var stream = context.ContentResolver?.OpenInputStream(uri);

        if (stream is not null)
        {
            Services.Utilities.DebugMsg($"In StreamDispatcher.Dispatch: Notifying stream with MIME type: {mimeType}");
            Activated?.Invoke(stream, mimeType);
            App.Current.IntentQueue.Enqueue(new Services.StreamRequest(stream, mimeType));
        }
    }
}