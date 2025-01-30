using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Collections.ObjectModel;

namespace DivisiBill.ViewModels;

public partial class FileListViewModel : ObservableObjectPlus
{
    private readonly string itemTypeName;
    public FileListViewModel(string itemTypeNameParameter)
    {
        Utilities.DebugMsg("In FileListViewModel constructor");
        itemTypeName = itemTypeNameParameter;
    }

    ~FileListViewModel()
    {
        if (FileList is not null)
            FileList.CollectionChanged -= FileList_CollectionChanged;
    }

    public async Task<bool> InitializeAsync()
    {
        Utilities.DebugMsg($"In FileListViewModel.InitializeAsync FileList is {(FileList is null ? "" : "not ")} null");
        if (FileList is not null)
            return true; // already initialized
        var returnedItems = await RemoteWs.GetItemInfoListAsync(itemTypeName);
        if (returnedItems is null)
            return false;
        FileList = [.. returnedItems];
        OnPropertyChanged(nameof(FileList));
        FileList.CollectionChanged += FileList_CollectionChanged;
        return true;
    }

    private void FileList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ItemsFound));
        OnPropertyChanged(nameof(FileListCount));
    }

    public void Terminate() => SelectionCompleted.TrySetResult(null);
    public ObservableCollection<RemoteItemInfo> FileList { get; set; }

    public bool ItemsFound => FileList.Count > 0;

    public int FileListCount => FileList.Count;

    public TaskCompletionSource<RemoteItemInfo> SelectionCompleted = new();

    public string ItemTypePlural => RemoteWs.ItemTypeNameToPlural[itemTypeName];

    [RelayCommand]
    private async Task UseAsync(RemoteItemInfo remoteItemInfo)
    {
        // TODO MAUI WORKAROUND Weirdly, this seems to be called twice on Android in .NET 8 RC 2, no idea why
#if ANDROID 
        if (SelectionCompleted.Task.IsCompleted)
            Utilities.DebugMsg("In FileListViewModel.Select trying to multiple-set SelectionCompleted");
        else
#endif
        {
            string action = await Utilities.DisplayActionSheetAsync("What Do you want to do?", "Cancel", "Replace", "Merge");
            if (!action.Equals("Cancel"))
            {
                remoteItemInfo.ReplaceRequested = action.Equals("Replace");
                SelectionCompleted.SetResult(remoteItemInfo);
            }
        }
    }

    [ObservableProperty]
    public partial RemoteItemInfo SelectedItem { get; set; }

    [ObservableProperty]
    public partial List<RemoteItemInfo> SelectedItems { get; set; } = [];

    [RelayCommand]
    private void Select(RemoteItemInfo remoteItemInfo)
    {
        if (ShowAsSelectableList)
        {
            if (remoteItemInfo.Selected)
                SelectedItems.Remove(remoteItemInfo);
            else
                SelectedItems.Add(remoteItemInfo);
        }
        else // Selecting a single item
        {

            if (remoteItemInfo == SelectedItem)
                SelectedItem = null;  // deselecting current item
            else
            {
                if (SelectedItem is not null) // Selecting a new item where something was selected before
                    SelectedItem.Selected = false; // so deselect the old one 
                SelectedItem = remoteItemInfo;
            }
        }
        remoteItemInfo.Selected = !remoteItemInfo.Selected;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (ShowAsSelectableList)
        {
            List<RemoteItemInfo> list = FileList.Where((rii) => rii.Selected).ToList();
            foreach (RemoteItemInfo item in list)
            {
                await DeleteThisItemAsync(item);
            }
        }
        else if (SelectedItem is not null)
        {
            RemoteItemInfo alternate = FileList.Alternate(SelectedItem);
            await DeleteThisItemAsync(SelectedItem);
            SelectedItem = alternate;
        }
    }

    [RelayCommand]
    private async Task DeleteThisItemAsync(RemoteItemInfo remoteItemInfo)
    {
        if (await RemoteWs.DeleteItemAsync(itemTypeName, remoteItemInfo.Name))
            FileList.Remove(remoteItemInfo);
    }

    [ObservableProperty]
    public partial bool ShowAsSelectableList { get; set; } = false;

    [RelayCommand]
    private void ChangeList()
    {
        ShowAsSelectableList = !ShowAsSelectableList;
        if (ShowAsSelectableList)
        {
            if (SelectedItem is not null)
            {
                SelectedItem.Selected = false; // deselect the old one 
                SelectedItem = null;
            }
        }
        else
        {
            foreach (RemoteItemInfo item in SelectedItems)
            {
                item.Selected = false;
            }
            SelectedItems.Clear();
        }
    }
    #region Scrolling Item list
    [ObservableProperty]
    public partial bool IsSwipeUpAllowed { get; set; }

    [ObservableProperty]
    public partial bool IsSwipeDownAllowed { get; set; }

    [ObservableProperty]
    public partial int FirstVisibleItemIndex { get; set; }

    partial void OnFirstVisibleItemIndexChanged(int value) => IsSwipeDownAllowed = value > 0;

    [ObservableProperty]
    public partial int LastVisibleItemIndex { get; set; }

    partial void OnLastVisibleItemIndexChanged(int value) => IsSwipeUpAllowed = value > 0 && value < FileList.Count - 1;

    public Action<int, bool> ScrollItemsTo = null;

    [RelayCommand]
    private void ScrollItems(string whereTo)
    {
        if (FirstVisibleItemIndex == LastVisibleItemIndex || ScrollItemsTo is null || FileList is null)
            return;
        int lastItemIndex = FileList.Count - 1;
        if (lastItemIndex < 2)
            return;
        try
        {
            switch (whereTo)
            {
                case "Up": if (LastVisibleItemIndex < lastItemIndex) ScrollItemsTo(LastVisibleItemIndex, false); break;
                case "Down": if (FirstVisibleItemIndex > 0) ScrollItemsTo(FirstVisibleItemIndex, true); break;
                case "End": if (LastVisibleItemIndex < lastItemIndex) ScrollItemsTo(lastItemIndex, false); break;
                case "Start": if (FirstVisibleItemIndex > 0) ScrollItemsTo(0, true); break;
                default: break;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash("fault attempting to scroll");
            // Do nothing, we do not really care if a scroll attempt fails
        }
    }
    #endregion
}
