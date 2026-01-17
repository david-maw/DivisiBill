using DivisiBill.Services;
using System.Diagnostics;
using static DivisiBill.Services.Utilities;
using File = System.IO.File;

namespace DivisiBill.Models;

/// <summary>
/// Provides functionality for managing meal data, including backup and recovery operations to and from remote storage.
/// </summary>
/// <remarks>The Meal class includes methods to enqueue meals and associated images for backup, initiate and stop
/// asynchronous backup processes, and recover missing meals from remote storage. Backup operations prioritize meal data
/// over images and support cancellation. Image backup may be disabled automatically if repeated upload failures occur.
/// This portion of the class is intended for use in scenarios where meal data integrity and cloud synchronization are
/// required.</remarks>
public partial class Meal
{
    // Also see Saver.RemoteLoop
    private static Task BackupTask = null;
    private static readonly CancellationTokenSource BackupCancellationTokenSource = new();
    private static readonly AwaitableQueue<MealSummary> backupQueue = new();
    private static readonly AwaitableQueue<MealSummary> imageBackupQueue = new();
    private static readonly AwaitableQueue<MealSummary> slowImageBackupQueue = new();

    internal static void StartBackupToRemote() => BackupTask ??= StartBackupToRemoteAsync(BackupCancellationTokenSource.Token);

    internal static async Task StopBackupToRemoteAsync()
    {
        if (BackupTask is null)
            return;
        Task OldBackupTask = BackupTask;
        BackupTask = null;
        BackupCancellationTokenSource.Cancel();
        await OldBackupTask;
    }

    /// <summary>
    /// First figure out whether there are any meals or images that are local, but not remote
    /// put each of them (more precisely, their MealSummary) in a queue to be transmitted. Then, on another
    /// thread, start up a loop sending each meal or image and removing it from the queue. In the meantime
    /// the main process may add additional meals or images to a queue as they are saved (by calling
    /// QueueForBackup). Note that there are multiple queues, one for meals, one for images and one for 
    /// missing images so that meals have highest priority and missing images have lowest priority.
    /// </summary>
    private static async Task BackupMissingAsync()
    {
        // This is where all the elapsed time goes, reaching out over the network
        List<RemoteItemInfo> remoteFileInfoList = await RemoteWs.GetItemInfoListAsync(RemoteWs.MealTypeName);
        // We use HashSet types to store the data, but the performance difference pales compared to the network time above        
        HashSet<string> remoteMealNames = remoteFileInfoList is null ? [] : [.. remoteFileInfoList.Select(x => x.Name)];
        await App.InitializationComplete.Task; // Wait until LocalMealList is established
        Dictionary<string, RemoteItemInfo> remoteFileInfoDict = remoteFileInfoList is null ? [] : remoteFileInfoList.ToDictionary(m => m.Name);
        HashSet<string> localMealNames = [];
        // Mark meals in the remote meal set as being remote and populate the set of local meals  
        foreach (MealSummary ms in LocalMealList)
        {
            if (remoteMealNames.Contains(ms.Id))
            {
                ms.IsRemote = true;
                ms.HasRemoteImage = remoteFileInfoDict[ms.Id].HasRemoteImage;
                ms.IsEncrypted = remoteFileInfoDict[ms.Id].IsEncrypted;
            }
            localMealNames.Add(ms.Id);
        }
        // Queue each MealSummary that is not remote for transmission
        HashSet<string> localOnlyMealNames = [.. localMealNames];
        localOnlyMealNames.ExceptWith(remoteMealNames);
        foreach (string mealName in localOnlyMealNames)
        {
            MealSummary ms = LocalMealList.First(foundMs => mealName.Equals(foundMs.Id));
            if (!ms.IsFake)
                QueueForBackup(ms);
        }
        // If we are backing up images, queue each image that is local but not remote for transmission
        if (App.IsCloudImageBackupAllowed)
        {
            foreach (MealSummary ms in LocalMealList.Where(ms => ms.HasImage && !ms.HasRemoteImage))
            {
                slowImageBackupQueue.Enqueue(ms);
            }
        }
    }

    /// <summary>
    /// Enter a loop sending each queued meal and removing it from the queue. In the meantime
    /// the main process may add additional meals to the queue as they are saved (by calling
    /// QueueForBackup).
    /// </summary>
    private static async Task StartBackupToRemoteAsync(CancellationToken cancellationToken)
    {
        Utilities.DebugMsg("Entered StartBackupToRemoteAsync, waiting for CloudAllowedSource");
        await App.CloudAllowedSource.WaitWhilePausedAsync();
        Utilities.DebugMsg("In StartBackupToRemoteAsync, CloudAllowedSource no longer paused");
        await BackupMissingAsync();
        // Start the actual transmission process - it will loop forever sending each MealSummary in the queue
        await BackupLoopAsync(cancellationToken);
    }

    /// <summary>
    /// Download all the remote meals that have valid content (meaning they can be deserialized) and are
    /// not stored locally.
    /// </summary>
    /// <param name="cancellationToken">Set this to cancel the operation </param>
    /// <returns>A task to track the status of the operation.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the task is canceled</exception>
    public static async Task RecoverFromRemoteAsync(Action<int, int, int> ReportProgress, CancellationToken cancellationToken)
    {
        DebugMsg("Entered RecoverFromRemoteAsync, waiting for CloudAllowedSource");
        await App.CloudAllowedSource.WaitWhilePausedAsync();
        DebugMsg("In RecoverFromRemoteAsync, CloudAllowedSource no longer paused");
        List<RemoteItemInfo> remoteFileInfoList = await RemoteWs.GetItemInfoListAsync(RemoteWs.MealTypeName);
        IEnumerable<string> remoteMealNames = remoteFileInfoList.Select(x => x.Name);
        await App.InitializationComplete.Task; // Wait until LocalMealList is established
        // Get a dictionary of local file names
        Dictionary<string, MealSummary> localMealDict = [];
        await GetLocalMealListAsync();
        foreach (MealSummary ms in LocalMealList)
            localMealDict.Add(ms.Id, ms);
        // Get a list of remote only files (quite inefficient but that probably doesn't matter)
        var remoteOnlyFileInfoList = remoteFileInfoList.Where(rfi => !localMealDict.ContainsKey(rfi.Name)).ToList();
        int totalFiles = remoteOnlyFileInfoList.Count, filesWithoutError = 0, filesInError = 0, costMismatches = 0;
        decimal totalDifference = 0;
        ReportProgress(totalFiles, filesWithoutError, filesInError);
        foreach (RemoteItemInfo rfi in remoteOnlyFileInfoList)
        {
            using Stream sourceStream = await RemoteWs.GetItemStreamAsync(RemoteWs.MealTypeName, rfi.Name);
            cancellationToken.ThrowIfCancellationRequested();
            try // if one file fails, just report it and go on to the next 
            {
                Meal m = LoadFromStream(sourceStream);
                if (m is null)
                {
                    // The stream was bad so do not store it
                    DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned null for {rfi.Name}");
                    filesInError++;
                    if (Utilities.IsDebug)
                        Debugger.Break();
                }
                else if (m.Size <= 0)
                {
                    // The stream was bad so do not store it
                    DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned a negative size for {rfi.Name}");
                    // This could represent a networking error, in which case all subsequent files will probably fail too, so just give up
                    filesInError++;
                    if (!App.IsCloudAccessible)
                    {
                        DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream detected the cloud was no longer accessible, so exit the loop");
                        break;
                    }
                    if (Utilities.IsDebug)
                        Debugger.Break();
                }
                else
                {
                    if (m.Summary.FileNameInconsistent(rfi.Name))
                    {
                        // The creation time stored in the stream did not match the file name 
                        DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned a mismatched name for {rfi.Name + ".xml"} not {m.DebugDisplay}");
                        // TODO: Move the file so it will not cause trouble in future 
                        filesInError++;
                    }
                    else // The MealSummary is good as far as we can tell
                    {
                        // this is a handy place to scan multiple bills to check for differences between the old and new DistributeCosts algorithms
                        decimal difference = m.CompareCostDistribution(report: false);
                        if (Utilities.IsDebug && difference > 0)
                        {
                            DebugMsg($"In Meal.RecoverFromRemoteAsync: Cost Mismatch of {difference:C} in {m.DebugDisplay}");
                            costMismatches++;
                            totalDifference += difference;
                        }
                        m.SavedToRemote = true;
                        m.Summary.IsRemote = true;
                        m.Summary.HasRemoteImage = rfi.HasRemoteImage;
                        m.Summary.IsEncrypted = rfi.IsEncrypted;
                        m.SaveToFile();
                        m.Summary.LocationChanged(isLocal: true);
                        filesWithoutError++;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugMsg($"In Meal.RecoverFromRemoteAsync, exception: {ex.Message}");
                filesInError++;
            }
            finally
            {
                ReportProgress(totalFiles, filesWithoutError, filesInError);
            }
        }
        if (costMismatches > 0)
            DebugMsg($"In Meal.RecoverFromRemoteAsync: {costMismatches} Cost Mismatches totaling {totalDifference:C}");
        if (totalFiles <= 0)
            await Utilities.DisplayAlertAsync("Download Bills", $"There were no cloud-only bills to download");
        else if (filesInError == 0)
            await Utilities.DisplayAlertAsync("Download Bills", $"All remaining cloud-only bills ({filesWithoutError}) have been downloaded without error");
        else if (filesWithoutError + filesInError < totalFiles)
            await Utilities.DisplayAlertAsync("Download Bills", $"The download was interrupted, {filesWithoutError} of {totalFiles} bills were downloaded without error");
        else
            await Utilities.DisplayAlertAsync("Download Bills",
                $"{filesWithoutError} cloud-only bills have been downloaded, {filesInError} more had errors");
    }

    /// <summary>
    /// Continuously processes backup tasks for meal summaries and associated images, uploading them to remote storage
    /// as items become available in the backup queues.
    /// </summary>
    /// <remarks>The method monitors multiple backup queues in priority order and uploads either meal
    /// summaries or their images to the cloud, depending on the queue source. Image backup may be disabled
    /// automatically if repeated upload failures occur. The method runs indefinitely until cancellation is requested
    /// via the provided token.</remarks>
    /// <param name="cancellationToken">A cancellation token that can be used to request cancellation of the backup loop. If cancellation is requested,
    /// the operation will terminate promptly.</param>
    /// <returns>A task that represents the asynchronous operation of the backup loop. The task loops indefinitely until cancellation is
    /// requested (normally at program shutdown).</returns>
    private static async Task BackupLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Priority order: backupQueue > imageBackupQueue > slowImageBackupQueue
            bool backupImage = false;

            // Try to get from backupQueue first
            if (backupQueue.TryDequeue(out MealSummary ms))
                backupImage = false;
            // Then try image queues
            else if (imageBackupQueue.TryDequeue(out ms) || slowImageBackupQueue.TryDequeue(out ms))
                backupImage = true;
            else
            {
                // If all queues are empty, wait for any to have an item
                Task<MealSummary> summaryTask = backupQueue.DequeueAsync(cancellationToken);
                Task<MealSummary> imageTask = imageBackupQueue.DequeueAsync(cancellationToken);
                Task<MealSummary> slowImageTask = slowImageBackupQueue.DequeueAsync(cancellationToken);
                Task<MealSummary> firstCompleted = await Task.WhenAny(summaryTask, imageTask, slowImageTask);
                // One or more queues have an item, so get it from the one that completed first
                backupImage = firstCompleted != summaryTask;
                ms = await firstCompleted;
            }

            // Do not attempt to actually send anything if he cloud is not allowed (or accessible)
            await App.CloudAllowedSource.WaitWhilePausedAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (backupImage)
            {
                // backup the image for this MealSummary to the cloud
                if (ms.HasImage)
                {
                    if (File.Exists(ms.ImagePath))
                    {
                        static void StopImageBackup()
                        {
                            App.Settings.BackupImages = false;
                            slowImageBackupQueue.Clear();
                            imageBackupQueue.Clear();
                        }

                        try
                        {
                            HttpResponseMessage resp = await RemoteWs.PutImageAsync(ms);
                            if (resp.IsSuccessStatusCode)
                            {
                                DebugMsg($"Image for {ms.DebugDisplay} saved to remote storage");
                                ms.HasRemoteImage = true; // An image is now in remote storage
                            }
                            else
                            {
                                DebugMsg($"In BackupLoopAsync: Failed to save image for {ms.DebugDisplay} to remote storage: {resp.ReasonPhrase}");
                                // This is a background process so it is not a good idea to keep retrying these, just turn off image backup
                                StopImageBackup();
                                await Utilities.DisplayAlertAsync("Cloud Image Backup", $"Image backup has been disabled because of an error: {resp.ReasonPhrase}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugMsg($"In BackupLoopAsync: Failed to save image for {ms.DebugDisplay} to remote storage: {ex.Message}");
                            // This is a background process so it is not a good idea to keep retrying these, just turn off image backup
                            StopImageBackup();
                            await Utilities.DisplayAlertAsync("Cloud Image Backup", $"Image backup has been disabled because of a fault: {ex.Message}");
                        }
                    }
                    else
                        DebugMsg($"In BackupLoopAsync: Image file {ms.ImageName} does not exist for {ms.DebugDisplay}");
                }
                else
                    DebugMsg($"In BackupLoopAsync: {ms.DebugDisplay} has no associated image");
            }
            else
            {
                // Backup the MealSummary to the cloud
                Meal m = LoadFromFile(ms);
                if (m is not null) // there's a small timing hole where the file might be removed while the request is in the queue
                {
                    await m.SaveToRemoteAsync();
                }
                else
                    DebugMsg($"Null meal detected in BackupLoopAsync for summary: {ms}");
            }
        }
    }

    /// <summary>
    /// Enqueue a meal or image for backup to remote storage, usually as a result of saving a new
    /// meal or image to local storage. These functions exist mostly to permit other code to access
    /// the queues without having to know about them.
    /// </summary>
    /// <param name="ms"></param>
    public static void QueueForBackup(MealSummary ms) => backupQueue.Enqueue(ms);

    public static void QueueForImageBackup(MealSummary ms) => imageBackupQueue.Enqueue(ms);
}
