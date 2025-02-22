using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Diagnostics;
using System.Net;

namespace DivisiBill.ViewModels;

public partial class CheckWebPageViewModel(Action<object> ClosePopup, Task<HttpResponseMessage> webCallTask, Func<Task<HttpResponseMessage>> webCall, Stopwatch webStopwatch) : ObservableObject
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
        Utilities.DebugMsg($"In CheckWebPageViewModel.WaitForConnection.InvokeClose({result})");
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
            Utilities.DebugMsg($"In CheckWebPageViewModel.WaitForConnection.SetStatusMessage({message},{messageExtra})");
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
        int ElapsedSeconds() => (int)((webStopwatch.Elapsed).TotalSeconds);
        string ToSecondsText(int i) => i + " second" + (i == 1 ? "" : "s");
        // prepare a timer for use later
        Timer elapsedTimer = new(e =>
            {
                if (runningStatus.IsPaused)
                    SetStatusMessage(null, "Paused");
                else
                    SetStatusMessage(null, "Waited " + ToSecondsText(ElapsedSeconds()));
            },
            null, int.MaxValue, int.MaxValue);
        #endregion
        // Loop until we have a successful call or the user tells us to stop
        do
        {
            // If the call has completed, check the result (if it has not completed, there's nothing we can do but wait)
            if (webCallTask.IsCompleted)
            {
                webStopwatch.Stop();
                if (webCallTask.IsCompletedSuccessfully && webCallTask.Result.IsSuccessStatusCode)
                {
                    Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask.IsCompletedSuccessfully and successful result = " + webCallTask.Result.StatusCode + " in " + ToSecondsText(ElapsedSeconds()));
                    StopTrying(true); // The request completed without error, we can continue on
                }
                else
                {
                    // The request failed, or completed but returned an error, so wait a bit then try again
                    if (webCallTask.IsCompletedSuccessfully)
                    {
                        // Completed but returned a failed status code
                        SetStatusMessage("Call returned result = " + webCallTask.Result.StatusCode);
                        Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask.IsCompleted with fail result = " + webCallTask.Result.StatusCode);
                    }
                    else
                    {
                        SetStatusMessage("Call failed with status = " + webCallTask.Status);
                        Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask.IsCompleted but unsuccessfully, status = " + webCallTask.Status);
                    }
                    // restart the stopwatch and wait a bit before trying again
                    webStopwatch.Restart();
                    do
                    {
                        if (runningStatus.IsPaused)
                        {
                            SetStatusMessage(null, "Paused");
                            await runningStatus.WaitWhilePausedAsync(); // Do not do this stuff if the app is paused
                        }
                        int i = 30 - ElapsedSeconds();
                        if (i > 0 && keepTrying)
                        {
                            SetStatusMessage(null, "Will retry in " + ToSecondsText(i));
                            await Task.Delay(1000);
                        }
                        else
                            break;
                    }
                    while (keepTrying);
                    if (keepTrying)
                    {
                        webStopwatch.Restart();
                        webCallTask = webCall(); // Initiate the call but do not wait on it
                    }
                }
            }
            else
            {
                // The request has not completed yet, so just wait for it to complete
                SetStatusMessage("Waiting for web service call to complete");
                elapsedTimer.Change(200, 1000); // Start firing the timer but make sure the rounded seconds are correct (hence the extra 200mS)
                try
                {
                    await webCallTask;
                }
                catch (TaskCanceledException ex)
                {
                    // Connection timeouts on Windows seem to go here
                    Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask was canceled, probably timed out. Exception message: " + ex.Message);
                }
                catch (WebException ex)
                {
                    // Connection timeouts on Android seem to go here
                    Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask threw a WebException: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask threw an exception: " + ex.Message);
                }
                elapsedTimer.Change(int.MaxValue, int.MaxValue); // Stop firing the timer
            }
        } while (keepTrying);
        elapsedTimer.Dispose();
    }
}