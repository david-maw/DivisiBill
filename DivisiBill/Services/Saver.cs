using DivisiBill.Models;

namespace DivisiBill.Services;

internal class Saver
{
    public static TaskCompletionSource<bool> SaveNowRequested = new TaskCompletionSource<bool>();
    public static bool SavedRemote = false;
    /// <summary>
    /// Save current meal on request
    /// </summary>
    /// <returns></returns>
    private static async Task SaveRequestLoop()
    {
        Utilities.DebugMsg("Enter SaveRequestLoop");
        for (int i = 0; i < 1000; i++) // test - wait until we explicitly allow continue 
        {
            if (!App.pauseInitialization) break;
            await Task.Delay(10000);
        }
        while (true)
        {
            App.SaveProcessCancellationTokenSource.Token.ThrowIfCancellationRequested();
            await SaveNowRequested.Task;
            await App.IsRunningSource.WaitWhilePausedAsync(); // Do not do this stuff if the app is paused
            await Meal.CurrentMeal.SaveIfChangedAsync(SaveRemote: false); // this is a routine save, don't bother with a costly remote update
            SaveNowRequested = new TaskCompletionSource<bool>(); // So it no longer shows up as completed
            SaveHappenedTCS.SetResult(true); // Notify interested parties
        }
    }
    /// <summary>
    /// Periodically save current meal if it has changed, usually this is a local save, remote save is used only for 
    /// protection from catastrophic failure, save to remote is normally triggered by a meal being saved locally.
    /// See Meal.QueueForBackup for the normal save mechanism.
    /// </summary>
    /// <returns></returns>
    private static async Task TimedLoop(int delayTime, bool remote = false)
    {
        for (int i = 0; i < 1000; i++) // test - wait until we explicitly allow continue 
        {
            if (!App.pauseInitialization) break;
            await Task.Delay(10000);
        }
        Utilities.DebugMsg($"Enter TimedLoop({delayTime},{remote})");
        while (true)
        {
            App.SaveProcessCancellationTokenSource.Token.ThrowIfCancellationRequested();
            await Task.Delay(delayTime * 1000);
            await App.CloudAllowedSource.WaitWhilePausedAsync(); // Do not do this stuff if cloud is unavailable
            Meal currentMeal = Meal.CurrentMeal;
            currentMeal.SaveReason = "time";
            await currentMeal.SaveIfChangedAsync(SaveFile: !remote, SaveRemote: remote); 
            // Do not save the image, reading it may confuse other threads
        }
    }
    public static async Task MainLoop()
    {
        Utilities.DebugMsg("Enter MainLoop");
        Task SaveRequestLoopTask = SaveRequestLoop();
        await App.InitializationComplete.Task; // let initialization sort itself out before starting timed saves
        Utilities.DebugMsg("Starting MainLoop Timed Tasks");
        await Task.WhenAll(SaveRequestLoopTask, Meal.PeriodicSaveAsync(10), TimedLoop(30, remote:false), TimedLoop(120, remote: true));
        Utilities.DebugMsg("Leave MainLoop");
    }

    // Used to allow SaveNow to give an Async response only after the save actually happens
    private static TaskCompletionSource<bool> SaveHappenedTCS = new TaskCompletionSource<bool>();

    /// <summary>
    /// Save to the App and Locally as soon as possible
    /// </summary>
    /// <param name="why">Reason for the save</param>
    /// <returns></returns>
    public static async Task SaveCurrentMealIfChangedAsync(string why)
    {
        if (!Meal.CurrentMeal.SavedToFile && !Meal.CurrentMeal.Frozen) // it has already been done, perhaps by another thread or async method
        {
            App.SaveProcessCancellationTokenSource.Token.ThrowIfCancellationRequested();
            if (SaveNowRequested.Task.IsCompleted)
                Utilities.DebugMsg($"In SaveCurrentMealIfChangedAsync(\"{why}\") SaveNow - already in process");
            else
            {
                Utilities.DebugMsg($"In SaveCurrentMealIfChangedAsync(\"{why}\")");
                Meal.CurrentMeal.SaveReason = why;
                SaveNowRequested.TrySetResult(true);
                await SaveHappenedTCS.Task;
                SaveHappenedTCS = new TaskCompletionSource<bool>(); // Ready for next time
            }
        }
        return;
    }
}
