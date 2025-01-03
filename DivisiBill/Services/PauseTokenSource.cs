// http://stackoverflow.com/a/19616899/1768303
// http://blogs.msdn.com/b/pfxteam/archive/2013/01/13/cooperatively-pausing-async-methods.aspx

namespace DivisiBill.Services;

public class PauseTokenSource
{
    private volatile TaskCompletionSource<bool> m_paused;

    public bool IsPaused
    {
        get => m_paused is not null;
        set
        {
            if (value)
            {
                Interlocked.CompareExchange(
                    ref m_paused, new TaskCompletionSource<bool>(), null);
            }
            else
            {
                while (true)
                {
                    var tcs = m_paused;
                    if (tcs is null) return;
                    if (Interlocked.CompareExchange(ref m_paused, null, tcs) == tcs)
                    {
                        tcs.SetResult(true);
                        break;
                    }
                }
            }
        }
    }

    internal Task WaitWhilePausedAsync()
    {
        var cur = m_paused;
        return cur is not null ? cur.Task : s_completedTask;
    }

    internal static readonly Task s_completedTask = Task.FromResult(true);

    public PauseToken Token => new(this);
}

public struct PauseToken
{
    private readonly PauseTokenSource m_source;
    internal PauseToken(PauseTokenSource source) => m_source = source;

    public bool IsPaused => m_source is not null && m_source.IsPaused;

    public Task WaitWhilePausedAsync() => IsPaused ?
            m_source.WaitWhilePausedAsync() :
            PauseTokenSource.s_completedTask;
}
