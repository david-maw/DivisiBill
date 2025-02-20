using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Diagnostics;

namespace DivisiBill.ViewModels;

public partial class CheckWebPageViewModel(Action<object> ClosePopup, Task<HttpResponseMessage> webCallTask, Func<Task<HttpResponseMessage>> webCall) : ObservableObject
{
    /// <summary>
    /// Flag to indicate if we should keep trying to connect or not
    /// </summary>
    private bool keepTrying = true;

    /// <summary>
    /// Close the popup window and return the result
    /// </summary>
    /// <param name="result">True if the web service call worked, false if the user elected to abandon it</param>
    private void StopTrying(object result)
    {
        Utilities.DebugMsg($"In WaitForConnection.InvokeClose({result})");
        keepTrying = false;
        ClosePopup?.Invoke(result);
    }

    /// <summary>
    /// Set the status message to tell the user what is going on and the extra message to tell them how long for.
    /// </summary>
    /// <param name="message">What's happening</param>
    /// <param name="messageExtra">How long for or when it will end</param>
    private void SetStatusMessage(string message, string messageExtra = null)
    {
        if (message is not null)
        {
            Utilities.DebugMsg($"In WaitForConnection.SetStatusMessage({message},{messageExtra})");
            StatusMessage = message;
        }
        StatusMessageExtra = messageExtra;
    }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string StatusMessageExtra { get; set; }

    [RelayCommand]
    private void ClosePopupWindow() => StopTrying(false); // User elected to abandon the web service call

    /// <summary>
    /// Wait for a successful call to the version web service or until the user commands us to quit
    /// This is the main functionality of the popup window, to sit around until the web service works or is abandoned
    /// </summary>
    public async Task WaitForConnection()
    {

        // Ensure we were initialized correctly
        ArgumentNullException.ThrowIfNull(ClosePopup);
        ArgumentNullException.ThrowIfNull(webCallTask);
        #region Timer Handling
        PauseToken runningStatus = App.IsRunningSource.Token;
        Stopwatch stopwatch = new();
        int ElapsedSeconds() => (int)((stopwatch.Elapsed).TotalSeconds);
        string ToSecondsText(int i) => i + " second" + (i > 1 ? "s" : "");
        // prepare a timer for use later
        Timer elapsedTimer = new(e =>
            {
                if (runningStatus.IsPaused)
                    SetStatusMessage(null, "Paused");
                else
                    SetStatusMessage(null, "Waited " + ToSecondsText(ElapsedSeconds()));
            },
            null, 1000, 1000);
        elapsedTimer.Change(int.MaxValue, int.MaxValue);
        #endregion
        // Loop until we have a successful call or the user tells us to stop
        do
        {
            // If the 'version' call has completed, check the result (if it has not completed, there's nothing we can do but wait)
            if (webCallTask.IsCompleted)
            {
                Utilities.DebugMsg("In WaitForConnection, WsVersionTask.IsCompleted and result = " + webCallTask.Result.StatusCode);
                if (webCallTask.Result.IsSuccessStatusCode)
                    StopTrying(true); // The request completed without error, we can continue on
                else
                {
                    // The request completed but returned an error, so wait a bit then try again
                    SetStatusMessage("Call failed with Error: " + webCallTask.Result.StatusCode);
                    stopwatch.Restart();
                    do
                    {
                        await runningStatus.WaitWhilePausedAsync(); // Do not do this stuff if the app is paused
                        int i = 30 - ElapsedSeconds();
                        if (i > 0 && keepTrying)
                        {
                            await Task.Delay(1000);
                            if (runningStatus.IsPaused)
                                SetStatusMessage(null, "Paused");
                            else
                                SetStatusMessage(null, "Will retry in " + ToSecondsText(i));
                        }
                        else
                            break;
                    }
                    while (keepTrying);
                    if (keepTrying)
                        webCallTask = webCall();
                }
            }
            else
            {
                // The request has not completed yet, so just wait for it to complete
                SetStatusMessage("Waiting for web service call to complete");
                stopwatch.Restart();
                elapsedTimer.Change(200, 1000); // Start firing the timer but make sure the rounded seconds are correct (hence the extra 200mS)
                try
                {
                    await webCallTask;
                }
                catch (TaskCanceledException ex)
                {
                    Utilities.DebugMsg("In WaitForConnection, webCallTask was canceled, probably timed out. Exception message: " + ex.Message);
                }
                elapsedTimer.Change(int.MaxValue, int.MaxValue); // Stop firing the timer
            }
        } while (keepTrying);
        elapsedTimer.Dispose();
    }
}