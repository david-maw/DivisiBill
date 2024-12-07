namespace DivisiBill.Services;

public class AppSettings : ISettings
{
    public string StoredMeal
    {
        get => Preferences.Get("Meal", string.Empty);
        set => Preferences.Set("Meal", value);
    }
    public Guid PeopleUpdater
    {
        get => Guid.Parse(Preferences.Get(nameof(PeopleUpdater), Guid.Empty.ToString()));
        set => Preferences.Set(nameof(PeopleUpdater), value.ToString());
    }
    /// <summary>
    /// Apologizes for the change in stored format for DateTime preferences in .NET 9
    /// </summary>
    /// <param name="name">The name of the preference</param>
    /// <param name="defaultDateTime">The default value to be returned if the preference is not set</param>
    /// <returns></returns>
    private DateTime PreferencesGet(string name, DateTime defaultDateTime)
    {
        if (!Preferences.ContainsKey(name))
            return defaultDateTime;
        DateTime dt = defaultDateTime;
        try
        {
            dt = Preferences.Get(name, defaultDateTime);
        }
        catch (Exception)
        {
            // Probably means we were presented with a string written by .NET 8 or before
            string s = Preferences.Get(name, "");
            dt = (DateTime.TryParse(s, out DateTime result)) ? result : defaultDateTime;
        }
        return dt;
    }
    public DateTime PeopleUpdateTime
    {
        get => PreferencesGet(nameof(PeopleUpdateTime), DateTime.MinValue);
        set => Preferences.Set(nameof(PeopleUpdateTime), value);
    }
    public Guid VenueUpdater
    {
        get => Guid.Parse(Preferences.Get(nameof(VenueUpdater), Guid.Empty.ToString()));
        set => Preferences.Set(nameof(VenueUpdater), value.ToString());
    }
    public DateTime VenueUpdateTime { get; set; } = DateTime.MinValue;
    public int DefaultTipRate
    {
        get => Preferences.Get(nameof(DefaultTipRate), 20);
        set => Preferences.Set(nameof(DefaultTipRate), value);
    }
    public double DefaultTaxRate
    {
        get => Preferences.Get(nameof(DefaultTaxRate), 0.0775);
        set => Preferences.Set(nameof(DefaultTaxRate), value);
    }
    public bool DefaultTipOnTax
    {
        get => Preferences.Get(nameof(DefaultTipOnTax), true);
        set => Preferences.Set(nameof(DefaultTipOnTax), value);
    }
    public bool DefaultTaxOnCoupon
    {
        get => Preferences.Get("DefaultTaxOnDiscount", false);
        set => Preferences.Set("DefaultTaxOnDiscount", value);
    }
    public bool MealFrozen
    {
        get => Preferences.Get(nameof(MealFrozen), true);
        set => Preferences.Set(nameof(MealFrozen), value);
    }
    public bool MealSavedToFile
    {
        get => Preferences.Get(nameof(MealSavedToFile), true);
        set => Preferences.Set(nameof(MealSavedToFile), value);
    }
    public bool MealSavedToRemote
    {
        get => Preferences.Get(nameof(MealSavedToRemote), true);
        set => Preferences.Set(nameof(MealSavedToRemote), value);
    }
    public bool IsCloudAccessAllowed
    {
        get => Preferences.Get(nameof(IsCloudAccessAllowed), false) && !App.IsLimited;
        set
        {
            Preferences.Set(nameof(IsCloudAccessAllowed), value);
            App.HandleActivityChanges();
        }
    }
    public bool WiFiOnly
    {
        get => Preferences.Get(nameof(WiFiOnly), DeviceInfo.Current.Idiom != DeviceIdiom.Desktop);
        set
        {
            Preferences.Set(nameof(WiFiOnly), value);
            App.EvaluateCloudAccessible();
        }
    }
    public bool FirstUse
    {
        get => Preferences.Get(nameof(FirstUse), true);
        set
        {
            Preferences.Set(nameof(FirstUse), value);
            App.HandleActivityChanges();
        }
    }
    public DateTime LastUse
    {
        get => PreferencesGet(nameof(LastUse), DateTime.MinValue);
        set => Preferences.Set(nameof(LastUse), value);
    }
    public string UserKey
    {
        get => Preferences.Get(nameof(UserKey), string.Empty);
        set => Preferences.Set(nameof(UserKey), value);
    }
    public bool ShowLineItemsHint
    {
        get => Preferences.Get(nameof(ShowLineItemsHint), true);
        set => Preferences.Set(nameof(ShowLineItemsHint), value);
    }
    public bool ShowTotalsHint
    {
        get => Preferences.Get(nameof(ShowTotalsHint), true);
        set => Preferences.Set(nameof(ShowTotalsHint), value);
    }
    public bool ShowVenuesHint
    {
        get => Preferences.Get(nameof(ShowVenuesHint), true);
        set => Preferences.Set(nameof(ShowVenuesHint), value);
    }
    public bool ShowPeopleHint
    {
        get => Preferences.Get(nameof(ShowPeopleHint), true);
        set => Preferences.Set(nameof(ShowPeopleHint), value);
    }
    public bool SendCrashYes
    {
        get => Preferences.Get(nameof(SendCrashYes), true);
        set => Preferences.Set(nameof(SendCrashYes), value);
    }
    public bool SendCrashAsk
    {
        get => Preferences.Get(nameof(SendCrashAsk), true);
        set => Preferences.Set(nameof(SendCrashAsk), value);
    }
    public bool ShowTutorial
    {
        get => Preferences.Get(nameof(ShowTutorial), true);
        set => Preferences.Set(nameof(ShowTutorial), value);
    }
    public bool HadProSubscription
    {
        get => Preferences.Get(nameof(HadProSubscription), false);
        set => Preferences.Set(nameof(HadProSubscription), value);
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
            int height = Preferences.Get("PositionHeight",  0);
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
                Preferences.Set("PositionX", x);
                Preferences.Set("PositionY", y);
                Preferences.Set("PositionWidth", width);
                Preferences.Set("PositionHeight", height);
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
    public Location FakeLocation
    {
        get
        {
            if (FakeAccuracy >= Distances.AccuracyLimit)
                return null;
            else
                return new Location(FakeLatitude, FakeLongitude) { Accuracy = FakeAccuracy };
        }
        set
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
    private int fakeAccuracy = Distances.Inaccurate;
    private int FakeAccuracy
    {
        get => fakeAccuracy;
        set
        {
            if (value == 0 || value >= Distances.AccuracyLimit) // clear it
            {
                Preferences.Clear(nameof(FakeAccuracy)); // invalidates FakeLatitude/Longitude as well
                fakeAccuracy = Distances.Inaccurate;
            }
            else
            {
                Preferences.Set(nameof(FakeAccuracy), value);
                fakeAccuracy = value;
            }
        }
    }

    private double FakeLatitude
    {
        get => Preferences.Get(nameof(FakeLatitude), 0.0);
        set
        {
            if (value == 0)
                Preferences.Clear(nameof(FakeLatitude));
            else
                Preferences.Set(nameof(FakeLatitude), value);
        }
    }

    private double FakeLongitude
    {
        get => Preferences.Get(nameof(FakeLongitude), 0.0);
        set
        {
            if (value == 0)
                Preferences.Clear(nameof(FakeLongitude));
            else
                Preferences.Set(nameof(FakeLongitude), value);
        }
    }
}
