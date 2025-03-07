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
    /// Set the status message fields to tell the user what is going on and the extra message to tell them how long for.
    /// The extra field can be updated without changing the main message
    /// </summary>
    /// <param name="message">What's happening</param>
    /// <param name="messageExtra">How long for or when it will end</param>
    private void SetStatusMessage(string message, string messageExtra = null)
    {
        static string Quoted(string s) => s is not null ? "\"" + s + "\"" : "null";

        if (message is not null)
        { // Only update the message if it is not null
            Utilities.DebugMsg($"In CheckWebPageViewModel.WaitForConnection.SetStatusMessage({Quoted(message)}, {Quoted(messageExtra)})");
            StatusMessage = message;
        }
        StatusMessageExtra = messageExtra;
    }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string StatusMessageExtra { get; set; }

    [ObservableProperty]
    public partial float Progress { get; set; }


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
        const int waitSeconds = 30;
        PauseToken runningStatus = App.IsRunningSource.Token;
        int ElapsedSeconds() => (int)((webStopwatch.Elapsed).TotalSeconds);
        string ToSecondsText(int i) => i + " second" + (i == 1 ? "" : "s");
        // prepare a timer for use later
        Timer elapsedTimer = new(e =>
            {
                if (runningStatus.IsPaused)
                    SetStatusMessage(null, "Paused");
                else if (ElapsedSeconds() > 0)
                {
                    SetStatusMessage(null, "Waited " + ToSecondsText(ElapsedSeconds()));
                    Progress = (float)webStopwatch.Elapsed.Ticks / CallWs.CallTimeout.Ticks;
                }
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
                    Progress = 1;
                    do
                    {
                        if (runningStatus.IsPaused)
                        {
                            SetStatusMessage(null, "Paused");
                            await runningStatus.WaitWhilePausedAsync(); // Do not do this stuff if the app is paused
                        }
                        int i = waitSeconds - ElapsedSeconds();
                        if (i > 0 && keepTrying)
                        {
                            SetStatusMessage(null, "Will retry in " + ToSecondsText(i));
                            Progress = (float)i / waitSeconds;
                            await Task.Delay(1000);
                        }
                        else
                        {
                            Progress = 0;
                            break;
                        }
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
                    int remainingMilliseconds = (int)(CallWs.CallTimeout.TotalMilliseconds - webStopwatch.Elapsed.TotalMilliseconds);
                    if (remainingMilliseconds > 0)
                        await webCallTask.OrDelay(remainingMilliseconds);
                    if (!webCallTask.IsCompleted)
                    {
                        // The call ran longer than the timeout (which seems to happen on Android), so we need to pretend it was canceled
                        Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask ran past Timeout, ignore it and go on anyway");
                        webCallTask = Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true));
                    }
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