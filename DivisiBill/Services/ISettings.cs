namespace DivisiBill.Services;

public interface ISettings
{
    /// <summary>
    /// The name on disk of the meal that is stored to the app.
    /// </summary>
    string StoredMeal { get; set; }

    /// <summary>
    /// The identity of the last app to update the people list. If it was someone else their GUIDs and ours will likely be different. 
    /// </summary>
    Guid PeopleUpdater { get; set; }

    /// <summary>
    /// The time when the People list was last changed. This is used to determine if the local copy of the people list needs to be pushed to the server.
    /// </summary>
    DateTime PeopleUpdateTime { get; set; }

    /// <summary>
    /// The identity of the last app to update the venue list.
    /// </summary>
    Guid VenueUpdater { get; set; }

    /// <summary>
    /// The time when the Venue list was last changed. This is used to determine if the local copy of the venue list needs to be pushed to the server.
    /// </summary>
    DateTime VenueUpdateTime { get; set; }

    /// <summary>
    /// The default tip rate to be used when creating a new meal. This is a percentage, e.g. 20 for 20%.
    /// </summary>
    int DefaultTipRate { get; set; }

    /// <summary>
    /// The default tax rate to be used when creating a new meal. This is a decimal fraction, e.g. 0.0775 for 7.75%.
    /// </summary>
    double DefaultTaxRate { get; set; }

    /// <summary>
    /// Whether the default tip should be calculated on the tax amount as well as the subtotal. This is a boolean value, usually false.
    /// </summary>
    bool DefaultTipOnTax { get; set; }

    /// <summary>
    /// Should be DefaultTaxOnCoupons but history
    /// </summary>
    bool DefaultTaxOnCoupon { get; set; }

    /// <summary>
    /// The Frozen attribute value of the meal in StoredMeal
    /// </summary>
    bool MealFrozen { get; set; }

    /// <summary>
    /// Initially the SavedToFile attribute value from the meal in StoredMeal. This attribute can change from false to true after the meal is stored
    /// even if the value in StoredMeal itself does not change (this happens when the meal is saved to a file).
    /// </summary>
    bool MealSavedToFile { get; set; }
    bool MealSavedToRemote { get; set; }

    /// <summary>
    /// Is Internet access permitted to be used for backup
    /// </summary>
    bool IsCloudAccessAllowed { get; set; }

    /// <summary>
    /// Forget the existing state early in initialization so we go through a startup as if newly installed. This is used for testing.
    /// </summary>
    bool StartFresh { get; set; }

    /// <summary>
    /// Is WiFi access required before the Internet can be used
    /// </summary>
    bool WiFiOnly { get; set; }

    /// <summary>
    /// Indicates whether this is the first time the app has been used. This is used to trigger tutorial mode.
    /// </summary>
    bool FirstUse { get; set; }

    /// <summary>
    /// The time when the app was last used. This is used to determine if the app has been idle for a long enough time that we need to freeze the
    /// current meal and start a new one. See <see cref="App.RecentlyUsed"/>.
    /// </summary>
    DateTime LastUse { get; set; }

    /// <summary>
    /// A generated token used to identify the user to the server. It is designed so as to be usable in a URL without encoding. Used to associate them
    /// with their meals, people, and venues on the server, and to determine if they have a Pro or OCR subscription. It is not a secret, it is only used for 
    /// identification, not authentication but it is tied to a user, not the app, so it is stored with licenses when they are requested and an incoming license value
    /// overrides any stored value. It is generated when the app is first used, and should only change after that if the user changes.
    /// </summary>
    string UserKey { get; set; }

    /// <summary>
    /// Indicates whether hints about line items should be shown. This is used to trigger the display of hints in the UI, and is set to false when the user decides.
    /// </summary>
    bool ShowLineItemsHint { get; set; }

    /// <summary>
    /// Indicates whether hints about totals should be shown. This is used to trigger the display of hints in the UI, and is set to false when the user decides.
    /// </summary>
    bool ShowTotalsHint { get; set; }

    /// <summary>
    /// Indicates whether hints about venues should be shown. This is used to trigger the display of hints in the UI, and is set to false when the user decides.
    /// </summary>
    bool ShowVenuesHint { get; set; }

    /// <summary>
    /// Indicates whether hints about people should be shown. This is used to trigger the display of hints in the UI, and is set to false when the user decides.
    /// </summary>
    bool ShowPeopleHint { get; set; }

    /// <summary>
    /// Indicates whether the user has agreed to send crash reports. This is used to determine whether to send crash reports, and is set to true only when the user explicitly agrees.
    /// </summary>
    bool SendCrashYes { get; set; }

    /// <summary>
    /// Indicates whether the user should be asked about sending crash reports. This is used to determine whether to prompt the user about sending crash reports, and is set based
    /// on their setting of a "do not ask again" checkbox.
    /// </summary>
    bool SendCrashAsk { get; set; }

    /// <summary>
    /// Indicates whether the tutorial should be shown. This is used to trigger the display of the tutorial, and is set to false when the user decides to skip it or when they complete it.
    /// </summary>
    bool ShowTutorial { get; set; }

    /// <summary>
    /// The position and size of the main window when the app is launched. This is used to restore the window to its previous position and size, and is updated whenever the window is moved or resized.
    /// Mainly useful for the Windows test environment.
    /// </summary>
    Rect InitialPosition { get; set; }

    /// <summary>
    /// A fake location to be used for testing. If this is set, it will be used instead of the actual location when the app tries to access the location. This is useful for testing location-based
    /// features (especially distance calculations) without having to physically move to different locations.
    /// </summary>
    Location? FakeLocation { get; set; }

    /// <summary>
    /// Indicates whether the user has ever had a Pro subscription. This is used to determine whether to warn them if they do not currently have one.
    /// It is set to true when they first subscribe to Pro, and never set back to false.
    /// </summary>
    bool HadProSubscription { get; set; }

    /// <summary>
    /// Indicates whether images should be included in backups.
    /// </summary>
    bool BackupImages { get; set; }

    /// <summary>
    /// Indicates whether image backups should only occur when on WiFi.
    /// </summary>
    bool BackupImagesOnlyWiFi { get; set; }

    /// <summary>
    /// The creation time of the last <see cref="Models.Meal"/> in the most recent archive
    /// </summary>
    DateTime PreviousArchiveEndTime { get; set; }
    void EnableHints()
    {
        ShowLineItemsHint = true;
        ShowTotalsHint = true;
        ShowVenuesHint = true;
        ShowPeopleHint = true;
    }

    /// <summary>
    /// Set the option so the tutorial will be shown again. This is used for testing, and also in the help page to allow the user to re-run the tutorial if they want to.
    /// </summary>
    void ResetCheckboxes() => ShowTutorial = true;
}
