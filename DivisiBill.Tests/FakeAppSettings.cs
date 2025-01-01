using DivisiBill.Services;

namespace DivisiBill.Tests
{
    class FakeAppSettings : ISettings
    {
        public string StoredMeal { get; set; } = String.Empty;
        public Guid PeopleUpdater { get; set; } = Guid.Empty;
        public DateTime PeopleUpdateTime { get; set; } = DateTime.MinValue;
        public Guid VenueUpdater { get; set; } = Guid.Empty;
        public DateTime VenueUpdateTime { get; set; } = DateTime.MinValue;
        public int DefaultTipRate { get; set; } = 20;
        public double DefaultTaxRate { get; set; } = 0.0775;
        public bool DefaultTipOnTax { get; set; } = true;
        public bool DefaultTaxOnCoupon { get; set; } = false;
        public bool MealFrozen { get; set; } = true;
        public bool MealSavedToFile { get; set; } = true;
        public bool MealSavedToRemote { get; set; } = true;
        public bool IsCloudAccessAllowed { get; set; } = true;
        public bool WiFiOnly { get; set; } = false;
        public bool FirstUse { get; set; } = false;
        public DateTime LastUse { get; set; } = DateTime.Now - TimeSpan.FromMinutes(30);
        public DateTime ProLicenseValidTime { get; set; } = DateTime.Now;
        int OcrScansLeft { get; set; } = 10;
        public string UserKey { get; set; } = String.Empty;
        public bool ShowLineItemsHint { get; set; } = false;
        public bool ShowTotalsHint { get; set; } = false;
        public bool ShowVenuesHint { get; set; } = false;
        public bool ShowPeopleHint { get; set; } = false;
        public bool SendCrashYes { get; set; } = false;
        public bool SendCrashAsk { get; set; } = false;
        public bool ShowTutorial { get; set; } = false;
        public Location FakeLocation { get; set; } = null;
        public bool HadProSubscription { get; set; } = false;
        public Rect InitialPosition { get; set; } = new Rect(0, 0, 0, 0);
    }
}
