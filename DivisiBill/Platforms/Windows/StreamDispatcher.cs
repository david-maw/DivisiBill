namespace DivisiBill.Platforms.Windows;

public static class StreamDispatcher
{
#pragma warning disable CS0067    
    public static event Action<Stream, string> Activated;
#pragma warning restore CS0067
    public static void Dispatch() => throw new Exception("Not implemented for Windows");
}