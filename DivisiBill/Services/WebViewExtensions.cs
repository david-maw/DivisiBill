#if ANDROID
using Android.Webkit;
#endif

namespace DivisiBill.Services;

public static class WebViewExtensions
{
    public static List<WebHistoryEntry> GetAndroidHistory(this Microsoft.Maui.Controls.WebView webView)
    {
        var result = new List<WebHistoryEntry>();

#if ANDROID
        if (webView.Handler?.PlatformView is Android.Webkit.WebView native)
        {
            var list = native.CopyBackForwardList();
            int count = list.Size;

            // list doesn't implement IEnumerable, so we have to loop through it with an index
            for (int i = 0; i < count; i++)
            {
                var item = list.GetItemAtIndex(i);
                result.Add(new WebHistoryEntry
                {
                    Index = i,
                    Url = item?.Url ?? string.Empty,
                    OriginalUrl = item?.OriginalUrl ?? string.Empty,
                    Title = item?.Title ?? string.Empty,
                    IsCurrent = (i == list.CurrentIndex)
                });
            }
        }
#endif
        return result;
    }
    public static bool ClearAndroidHistory(this Microsoft.Maui.Controls.WebView webView)
    {
#if ANDROID
        if (webView.Handler?.PlatformView is Android.Webkit.WebView native)
        {
            native.ClearHistory();
            return true;
        }
#endif
        return false;
    }
}

public class WebHistoryEntry
{
    public int Index { get; set; }
    public string Url { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}
