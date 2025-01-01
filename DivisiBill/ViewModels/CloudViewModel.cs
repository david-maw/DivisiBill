using DivisiBill.Services;

namespace DivisiBill.ViewModels;

public class CloudViewModel : ObservableObjectPlus
{
    public CloudViewModel()
    {
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
    }

    ~CloudViewModel()
    {
        Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
    }

    private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        OnPropertyChanged(nameof(WiFiStatus));
        OnPropertyChanged(nameof(InternetEnabled));
        OnPropertyChanged(nameof(InternetEnabledAndLicensed));
    }

    public void NotifyProPurchase() => OnPropertyChanged(nameof(InternetEnabledAndLicensed));
    public bool IsCloudAccessAllowed
    {
        get => App.Settings.IsCloudAccessAllowed;
        set
        {
            if (App.Settings.IsCloudAccessAllowed != value)
            {
                App.Settings.IsCloudAccessAllowed = value;
                if (!value) WiFiOnly = true; // so that if it's turned on again wifi is required
                OnPropertyChanged();
            }
        }
    }
    public bool WiFiOnly
    {
        get => App.Settings.WiFiOnly;
        set
        {
            if (App.Settings.WiFiOnly != value)
            {
                App.Settings.WiFiOnly = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether or not Internet access exists
    /// </summary>
    public bool InternetEnabled
    {
        get => Connectivity.NetworkAccess == NetworkAccess.Internet;
    }

    /// <summary>
    /// Whether or not Internet access exists and we are running the Professional edition
    /// Note that cloud archiving may still not be allowed by the user
    /// </summary>
    public bool InternetEnabledAndLicensed
    {
        get => InternetEnabled && !App.IsLimited;
    }
    public string WiFiStatus
    {
        get
        {
            var profiles = Connectivity.ConnectionProfiles;
            if (profiles.Contains(ConnectionProfile.WiFi))
            {
                return "WiFi enabled";// Active Wi-Fi connection.
            }
            else
                return "No WiFi detected";
        }
    }
}
