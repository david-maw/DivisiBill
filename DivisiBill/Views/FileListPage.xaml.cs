namespace DivisiBill.Views;

public partial class FileListPage : ContentPage
{
    private readonly ViewModels.FileListViewModel fileListViewModel;
    public FileListPage(ViewModels.FileListViewModel fileListViewModelParameter)
    {
        BindingContext = fileListViewModel = fileListViewModelParameter;
        InitializeComponent();
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
}