using DivisiBill.Services;

namespace DivisiBill.Views;
public partial class TutorialPage : ContentPage
{
    public TutorialPage()
    {
        InitializeComponent();
    }
    public bool ShowTutorial
    {
        get => App.Settings.ShowTutorial;
        set
        {
            if (ShowTutorial != value)
            {
                App.Settings.ShowTutorial = value;
                OnPropertyChanged();
            }
        }
    }
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        App.isTutorialMode = true;
        base.OnNavigatedTo(args);
    }
    private async void OnDone(object sender, System.EventArgs e)
    {
        App.isTutorialMode = false;
        await App.GoToHomeAsync();
    }

    private void OnAddPeopleChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new PeopleListPage());
    }
    private void OnAddVenueChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new VenueListPage());
    }
    private void OnLineItemsChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new LineItemsPage());
    }

    private void OnParticipantsChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new TotalsPage());
    }
    private void OnBillPropertiesChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new PropertiesPage());
    }
    private void OnTakePictureChanged(object sender, CheckedChangedEventArgs e)
    {
        // This one is a bit tricky because we need to tell the image page to start the camera
        if (e.Value)
            App.PushAsync(Routes.ImagePage, "StartWithCamera", "true");
    }

    private void OnBuyChanged(object sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
            Navigation.PushAsync(new SettingsPage());
    }

    public bool IsNotLicensed => Billing.ScansLeft < 1;
}