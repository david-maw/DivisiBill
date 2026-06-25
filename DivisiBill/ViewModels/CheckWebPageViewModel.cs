using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Diagnostics;
using System.Net;

namespace DivisiBill.ViewModels;

public partial class CheckWebPageViewModel(Func<HttpResponseMessage, Task> ClosePopupAsync, Task<HttpResponseMessage> webCallTask,
    Func<CancellationTokenSource, Task<HttpResponseMessage>> webCall, Stopwatch webStopwatch, CancellationTokenSource tokenSource) : ObservableObject
{
    /// <summary>
    /// Flag to indicate if we should keep trying to connect or not
    /// </summary>
    private bool keepTrying = true;

    private bool retryImmediately = false;

    /// <summary>
    /// Close the popup window and return the result
    /// </summary>
    /// <param name="result">The value to return for the abandoned web service call</param>
    private async Task StopTrying(HttpResponseMessage result)
    {
        Utilities.DebugMsg($"In CheckWebPageViewModel.WaitForConnection.StopTrying passing an HttpResponseMessage with status code {(int)result.StatusCode} - {result.StatusCode}");
        keepTrying = false;
        try
        {
            tokenSource.Cancel();
            await ClosePopupAsync.Invoke(result);
        }
        catch (Exception ex)
        {
            // This sometimes faults while debugging, so just catch it and report it
            Utilities.ReportCrash(ex);
        }
    }

    /// <summary>
    /// Set the status message fields to tell the user what is going on and the extra message to tell them how long for.
    /// The extra field can be updated without changing the main message
    /// </summary>
    /// <param name="message">What's happening</param>
    /// <param name="messageExtra">How long for or when it will end</param>
    private void SetStatusMessage(string? message, string? messageExtra = null)
    {
        static string Quoted(string? s) => s is null ? "null" : "\"" + s + "\"";

        if (message is not null)
        { // Only update the message if it is not null
            Utilities.DebugMsg($"In CheckWebPageViewModel.SetStatusMessage({Quoted(message)}, {Quoted(messageExtra)})");
            StatusMessage = message;
        }
        StatusMessageExtra = messageExtra;
    }

    [ObservableProperty]
    public partial string PopupTitle { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessageExtra { get; set; }

    [ObservableProperty]
    public partial float Progress { get; set; }

    [ObservableProperty]
    public partial bool IsCountingDown { get; set; }

    [RelayCommand]
    private async Task ClosePopupWindow() => await StopTrying(new HttpResponseMessage(HttpStatusCode.RequestTimeout)); // User elected to abandon the web service call

    [RelayCommand]
    private void RequestRetry() => retryImmediately = true; // User elected not to wait for automatic retry but to do it immediately

    /// <summary>
    /// Wait for a successful call to the version web service or until the user commands us to quit
    /// This is the main functionality of the popup window, to sit around until the web service works or is abandoned
    /// </summary>
    public async Task WaitForConnection()
    {

        // Ensure we were initialized correctly
        ArgumentNullException.ThrowIfNull(ClosePopupAsync);
        ArgumentNullException.ThrowIfNull(webCallTask);
        #region Timer Handling
        const int waitSeconds = 30;
        PauseToken runningStatus = App.IsRunningSource.Token;
        int ElapsedSeconds() => (int)webStopwatch.Elapsed.TotalSeconds;
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
                IsCountingDown = false;
                if (webCallTask.IsCompletedSuccessfully && webCallTask.Result.IsSuccessStatusCode)
                {
                    Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask.IsCompletedSuccessfully and successful result = " + webCallTask.Result.StatusCode + " in " + ToSecondsText(ElapsedSeconds()));
                    await StopTrying(webCallTask.Result); // The request completed without error, or should not be retried, we can continue on
                }
                else
                {
                    // The request failed, or completed but returned an error we can retry, so wait a bit then try again
                    PopupTitle = "Web Error";
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
                    IsCountingDown = true;
                    Progress = 1;
                    do
                    {
                        if (runningStatus.IsPaused)
                        {
                            SetStatusMessage(null, "Paused");
                            await runningStatus.WaitWhilePausedAsync(); // Do not do this stuff if the app is paused
                        }
                        int i = waitSeconds - ElapsedSeconds();
                        if (retryImmediately)
                        {
                            Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, user requested immediate retry");
                            tokenSource.Cancel(); // Cancel any pending web call (if it is still running) so that we can start a new one immediately
                            tokenSource.Dispose();
                            tokenSource = new CancellationTokenSource(); // Create a new token source for the new web call
                            retryImmediately = false;
                            i = 0;
                        }
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
                        webCallTask = webCall(tokenSource); // Initiate the call but do not wait on it
                    }
                    else
                        IsCountingDown = false;
                }
            }
            else
            {
                // The request has not completed yet, so just wait for it to complete
                PopupTitle = "Slow Response";
                SetStatusMessage("Waiting for web service call to complete");
                IsCountingDown = false;
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
                    if (tokenSource.IsCancellationRequested)
                        // The user canceled the operation, so we can just ignore this and exit the loop
                        Utilities.DebugMsg("In CheckWebPageViewModel.WaitForConnection, webCallTask was canceled by user, exiting loop. Exception message: " + ex.Message);
                    else
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