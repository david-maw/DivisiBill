namespace DivisiBill.Services;

public interface ISettings
{
    string StoredMeal { get; set; }
    Guid PeopleUpdater { get; set; }
    DateTime PeopleUpdateTime { get; set; }
    Guid VenueUpdater { get; set; }
    DateTime VenueUpdateTime { get; set; }
    int DefaultTipRate { get; set; }
    double DefaultTaxRate { get; set; }
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
    /// Is WiFi access required before the Internet can be used
    /// </summary>
    bool WiFiOnly { get; set; }
    bool FirstUse {  get; set; }
    DateTime LastUse { get; set; }
    string UserKey { get; set; }
    bool ShowLineItemsHint { get; set; }
    bool ShowTotalsHint { get; set; }
    bool ShowVenuesHint { get; set; }
    bool ShowPeopleHint { get; set; }
    bool SendCrashYes { get; set; }
    bool SendCrashAsk { get; set; }
    bool ShowTutorial { get; set; }
    Rect InitialPosition { get; set; }
    Location FakeLocation { get; set; }
    bool HadProSubscription { get; set; }
    void EnableHints()
    {
        ShowLineItemsHint = true;
        ShowTotalsHint = true;
        ShowVenuesHint = true;
        ShowPeopleHint = true;
    }
    void ResetCheckboxes()
    {
        ShowTutorial = true;
    }
}
