namespace web;

internal class Program : MauiApplication
{
    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }

    private static void Main(string[] args)
    {
        Program app = new();
        app.Run(args);
    }
}
