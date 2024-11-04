using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Collections.ObjectModel;

namespace DivisiBill.ViewModels;

public partial class FileListViewModel : ObservableObjectPlus
{
    private string itemTypeName;
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
        FileList = new ObservableCollection<RemoteItemInfo>(await RemoteWs.GetItemInfoListAsync(itemTypeName));
        if (FileList is null)
            return false;
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

    public TaskCompletionSource<RemoteItemInfo> SelectionCompleted = new TaskCompletionSource<RemoteItemInfo>();

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
    private RemoteItemInfo selectedItem;

    [ObservableProperty]
    List<RemoteItemInfo> selectedItems = [];

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
    private bool showAsSelectableList = false;
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
}
