namespace DivisiBill.Views;

public partial class FileListPage : ContentPage
{
    private readonly ViewModels.FileListViewModel fileListViewModel;
    public FileListPage(ViewModels.FileListViewModel fileListViewModelParameter)
    {
        BindingContext = fileListViewModel = fileListViewModelParameter;
        InitializeComponent();
        fileListViewModel.ScrollItemsTo = ScrollItemsTo;
    }

    ~FileListPage()
    {
        fileListViewModel.ScrollItemsTo = null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (await fileListViewModel.InitializeAsync())
            await fileListViewModel.SelectionCompleted.Task;
        else
        {
            // Something went wrong
            await Services.Utilities.DisplayAlertAsync("Error", "Unable to retrieve items from the cloud archive", "ok");
        }
        await Shell.Current.Navigation.PopAsync();
    }

    protected override void OnDisappearing()
    {
        fileListViewModel.SelectionCompleted.TrySetResult(null);
        base.OnDisappearing();
    }
    #region Collection Scrolling
    private void ScrollItemsTo(int index, bool toEnd) // Passed in to viewModel
        => ItemsCollectionView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        fileListViewModel.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        fileListViewModel.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}