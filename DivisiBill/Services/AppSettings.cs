namespace DivisiBill.Services;

public class AppSettings : ISettings
{
    public AppSettings()
    {
        if (StartFresh)
        {
            Preferences.Clear(); // This will include clearing StartFresh so we won't do this again until asked to
            SecureStorage.RemoveAll(); // Remove all secure storage items (like RSA keys for decryption - this is irreversible)
            Directory.Delete(App.BaseFolderPath, recursive: true); // Delete the app data folder and everything in it
        }
    }
    // TODO: Remove workaround for https://github.com/dotnet/maui/issues/27167 Intermittent Problem with Preferences on Windows
    private bool SetPreference(string key, DateTime value)
    {
        try
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preferences.Set(key, value);
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
    }

    private bool SetPreference(string key, string value)
    {
        try
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preferences.Set(key, value);
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
    }
    private bool SetPreference(string key, bool value)
    {
        try
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preferences.Set(key, value);
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
    }
    private bool SetPreference(string key, int value)
    {
        try
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preferences.Set(key, value);
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
    }
    private bool SetPreference(string key, double value)
    {
        try
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preferences.Set(key, value);
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
    }

    public string StoredMeal
    {
        get => Preferences.Get("Meal", string.Empty);
        set => SetPreference("Meal", value);
    }
    public Guid PeopleUpdater
    {
        get => Guid.Parse(Preferences.Get(nameof(PeopleUpdater), Guid.Empty.ToString()));
        set => SetPreference(nameof(PeopleUpdater), value.ToString());
    }
    public DateTime PeopleUpdateTime
    {
        get => Preferences.Get(nameof(PeopleUpdateTime), DateTime.MinValue);
        set => SetPreference(nameof(PeopleUpdateTime), value);
    }
    public Guid VenueUpdater
    {
        get => Guid.Parse(Preferences.Get(nameof(VenueUpdater), Guid.Empty.ToString()));
        set => SetPreference(nameof(VenueUpdater), value.ToString());
    }
    public DateTime VenueUpdateTime { get; set; } = DateTime.MinValue;
    public int DefaultTipRate
    {
        get => Preferences.Get(nameof(DefaultTipRate), 20);
        set => SetPreference(nameof(DefaultTipRate), value);
    }
    public double DefaultTaxRate
    {
        get => Preferences.Get(nameof(DefaultTaxRate), 0.0775);
        set => SetPreference(nameof(DefaultTaxRate), value);
    }
    public bool DefaultTipOnTax
    {
        get => Preferences.Get(nameof(DefaultTipOnTax), true);
        set => SetPreference(nameof(DefaultTipOnTax), value);
    }
    public bool DefaultTaxOnCoupon
    {
        get => Preferences.Get("DefaultTaxOnDiscount", false);
        set => SetPreference("DefaultTaxOnDiscount", value);
    }
    public bool MealFrozen
    {
        get => Preferences.Get(nameof(MealFrozen), true);
        set => SetPreference(nameof(MealFrozen), value);
    }
    public bool MealSavedToFile
    {
        get => Preferences.Get(nameof(MealSavedToFile), true);
        set => SetPreference(nameof(MealSavedToFile), value);
    }
    public bool MealSavedToRemote
    {
        get => Preferences.Get(nameof(MealSavedToRemote), true);
        set => SetPreference(nameof(MealSavedToRemote), value);
    }
    public bool IsCloudAccessAllowed
    {
        get => Preferences.Get(nameof(IsCloudAccessAllowed), false) && !App.IsLimited;
        set
        {
            SetPreference(nameof(IsCloudAccessAllowed), value);
            App.HandleActivityChanges();
        }
    }
    public bool StartFresh
    {
        get => Preferences.Get(nameof(StartFresh), false);
        set => SetPreference(nameof(StartFresh), value);
    }
    public bool WiFiOnly
    {
        get => Preferences.Get(nameof(WiFiOnly), DeviceInfo.Current.Idiom != DeviceIdiom.Desktop);
        set
        {
            SetPreference(nameof(WiFiOnly), value);
            App.EvaluateCloudAccessible();
        }
    }
    public bool FirstUse
    {
        get => Preferences.Get(nameof(FirstUse), true);
        set
        {
            SetPreference(nameof(FirstUse), value);
            App.HandleActivityChanges();
        }
    }
    public DateTime LastUse
    {
        get => Preferences.Get(nameof(LastUse), DateTime.MinValue);
        set => SetPreference(nameof(LastUse), value);
    }
    public string UserKey
    {
        get => Preferences.Get(nameof(UserKey), string.Empty);
        set => SetPreference(nameof(UserKey), value);
    }
    public bool ShowLineItemsHint
    {
        get => Preferences.Get(nameof(ShowLineItemsHint), true);
        set => SetPreference(nameof(ShowLineItemsHint), value);
    }
    public bool ShowTotalsHint
    {
        get => Preferences.Get(nameof(ShowTotalsHint), true);
        set => SetPreference(nameof(ShowTotalsHint), value);
    }
    public bool ShowVenuesHint
    {
        get => Preferences.Get(nameof(ShowVenuesHint), true);
        set => SetPreference(nameof(ShowVenuesHint), value);
    }
    public bool ShowPeopleHint
    {
        get => Preferences.Get(nameof(ShowPeopleHint), true);
        set => SetPreference(nameof(ShowPeopleHint), value);
    }
    public bool SendCrashYes
    {
        get => Preferences.Get(nameof(SendCrashYes), true);
        set => SetPreference(nameof(SendCrashYes), value);
    }
    public bool SendCrashAsk
    {
        get => Preferences.Get(nameof(SendCrashAsk), true);
        set => SetPreference(nameof(SendCrashAsk), value);
    }
    public bool ShowTutorial
    {
        get => Preferences.Get(nameof(ShowTutorial), true);
        set => SetPreference(nameof(ShowTutorial), value);
    }
    public bool HadProSubscription
    {
        get => Preferences.Get(nameof(HadProSubscription), false);
        set => SetPreference(nameof(HadProSubscription), value);
    }

    /// <summary>
    /// The position and size the app window should use initially
    /// </summary>
    public Rect InitialPosition
    {
        get
        {
            int x = Preferences.Get("PositionX", 0);
            int y = Preferences.Get("PositionY", 0);
            int width = Preferences.Get("PositionWidth", 0);
            int height = Preferences.Get("PositionHeight", 0);
            return new Rect(x, y, width, height);
        }

        set
        {
            try
            {
                int x = Math.Abs(value.X) < int.MaxValue ? (int)value.X : 0;
                int y = Math.Abs(value.Y) < int.MaxValue ? (int)value.Y : 0;
                int width = Math.Abs(value.Width) < int.MaxValue ? (int)value.Width : 0;
                int height = Math.Abs(value.Height) < int.MaxValue ? (int)value.Height : 0;
                SetPreference("PositionX", x);
                SetPreference("PositionY", y);
                SetPreference("PositionWidth", width.ToString());
                SetPreference("PositionHeight", height.ToString());
            }
            catch (Exception ex)
            {
                ex.ReportCrash("Error persisting window size and position");
                // Do nothing, it does no great harm if this data is not stored
            }
        }
    }

    /// <summary>
    /// The Fake Location is stored as three simple values accuracy, latitude and longitude and accuracy
    /// The accuracy also acts as a validity specifier inf it is greater than Distances.AccuracyLimit it is deemed invalid 
    /// </summary>
    public Location? FakeLocation
    {
        get => FakeAccuracy >= Distances.AccuracyLimit ? null : new Location(FakeLatitude, FakeLongitude) { Accuracy = FakeAccuracy };
        set
        {
            if (value is null)
            {
                FakeLatitude = 0;
                FakeLongitude = 0;
                FakeAccuracy = Distances.Inaccurate;
            }
            else
            {
                FakeAccuracy = value.AccuracyOrDefault();
                if (FakeAccuracy >= Distances.AccuracyLimit) // clear it
                {
                    FakeLatitude = 0;
                    FakeLongitude = 0;
                }
                else
                {
                    FakeLatitude = Utilities.Adjusted(value.Latitude, FakeAccuracy);
                    FakeLongitude = Utilities.Adjusted(value.Longitude, FakeAccuracy);
                }
            }
        }
    }

    private int FakeAccuracy
    {
        get => Preferences.Get(nameof(FakeAccuracy), Distances.Inaccurate);
        set
        {
            if (value is 0 or >= Distances.AccuracyLimit) // clear it
                Preferences.Remove(nameof(FakeAccuracy)); // invalidates FakeLatitude/Longitude as well
            else
                SetPreference(nameof(FakeAccuracy), value);
        }
    }

    private double FakeLatitude
    {
        get => Preferences.Get(nameof(FakeLatitude), 0.0);
        set
        {
            if (value == 0)
                Preferences.Remove(nameof(FakeLatitude));
            else
                SetPreference(nameof(FakeLatitude), value);
        }
    }

    private double FakeLongitude
    {
        get => Preferences.Get(nameof(FakeLongitude), 0.0);
        set
        {
            if (value == 0)
                Preferences.Remove(nameof(FakeLongitude));
            else
                SetPreference(nameof(FakeLongitude), value);
        }
    }

    public bool BackupImages
    {
        get => Preferences.Get(nameof(BackupImages), false);
        set => SetPreference(nameof(BackupImages), value);
    }

    public bool BackupImagesOnlyWiFi
    {
        get => Preferences.Get(nameof(BackupImagesOnlyWiFi), true);
        set => SetPreference(nameof(BackupImagesOnlyWiFi), value);
    }
    public DateTime PreviousArchiveEndTime
    {
        get => Preferences.Get(nameof(PreviousArchiveEndTime), DateTime.MinValue);
        set => SetPreference(nameof(PreviousArchiveEndTime), value);
    }
}
