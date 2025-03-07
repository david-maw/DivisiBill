using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;

namespace DivisiBill.Views;

public partial class ScanPage : ContentPage, IQueryAttributable
{
    private List<LineItem> lineItems = [];
    /// <summary>
    /// The path to a file with an image to be scanned, either a temporary file from the camera 
    /// or a permanent one containing a browsed picture
    /// </summary>
    private string ImagePath = null;

    /// <summary>
    /// Normally null, but for debug builds it can be a stored analysis of an image from a previous call 
    /// It also may be the name of an image file being scanned so that an analysis of that image can be stored
    /// which means a debug build need not call the scanning web service again for the same picture.
    /// </summary>
    private ScannedBill scannedBill = null;
    private CancellationTokenSource tokenSource = null;
    public ScanPage() => InitializeComponent();
    protected override async void OnAppearing()
    {
        loading.IsVisible = true;
        ErrorMessage = "";
        tokenSource = new CancellationTokenSource();
        cancelButton.IsEnabled = true;

        if (!string.IsNullOrWhiteSpace(ImagePath))
        {
            lineItems = [];
            try
            {
                await Task.Run(DecodeLineItems);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error: " + ex.Message;
            }
        }
        if (lineItems is not null)
            OnPropertyChanged(nameof(LineItems));
        cancelButton.IsEnabled = false;
        loading.IsVisible = false;
    }

    protected override void OnDisappearing()
    {
        tokenSource?.Cancel(true);
        base.OnDisappearing();
    }

    /// <summary>
    /// Set this to generate fake line items for debug instead of scanning to get them
    /// </summary>
    private static readonly bool FakeLineItems = false;

    /// <summary>
    /// Push the image to the bill scan web service and use the textual result to
    /// populate the bill item list.
    /// </summary>
    private async Task DecodeLineItems()
    {

        if (FakeLineItems)
        {
            // Simulate an asynchronous call to analyze the image to save time and money
            await Task.Delay(5000); // simulate the time taken to do the analysis
            scannedBill = new ScannedBill()
            {
                OrderLines = new List<OrderLine>() {
                        { new OrderLine() { ItemName = "Simulated item 1", ItemCost = "$123.45" } },
                        { new OrderLine() { ItemName = "Simulated item 1", ItemCost = "$123.45" } },
                    }
            };
        }
        else if (scannedBill.OrderLines.Count == 0) // The normal case, probably as a result of taking a picture or scanning a new image
        {
            string sourceName = scannedBill.SourceName; // get the name of the image this came from, if there is one
            scannedBill = await CallWs.ImageToScannedBill(ImagePath, tokenSource.Token);
            if (scannedBill is not null)
            {
                if (scannedBill.ScansLeft == 0)
                    await Utilities.DisplayAlertAsync("Warning", "You have used your last scan");
                else if (scannedBill.ScansLeft == 1)
                    await Utilities.DisplayAlertAsync("Warning", "You have only one scan remaining");
                else if (scannedBill.ScansLeft < Billing.ScansWarningLevel)
                    await Utilities.DisplayAlertAsync("Warning", $"You have only {scannedBill.ScansLeft} scans remaining");
                // Store the ScannedBill object in case we can use it later
                if (string.IsNullOrEmpty(scannedBill.SourceName) && !string.IsNullOrEmpty(sourceName))
                    scannedBill.SourceName = sourceName;
                scannedBill.StoreToFile();
            }
        }
        // We should have a bill in scannedB, either loaded from a scan we just did or (for debugging locally)
        // from a file generated by a previous scan 

        if (scannedBill is not null)
        {
            lineItems = scannedBill.ToLineItems();
            OnPropertyChanged(nameof(LineItems));
        }
    }
    /// <summary>
    /// Line items scanned from the image in "ImagePath" passed in navigation as a URI value
    /// </summary>
    public ObservableCollection<LineItem> LineItems => [.. lineItems];

    public string ErrorMessage
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }
    private async void UpdateCurrentMeal(bool clearItems = false)
    {
        if (clearItems)
        {
            // Are we going to destroy information by deleting some existing items
            bool changed = Meal.CurrentMeal.LineItems.Any();
            Meal.CurrentMeal.LineItems.Clear();
            if (changed) // mark the bill as new and preserve the old one
                await Meal.CurrentMeal.MarkAsNewAsync("Scan");
        }
        foreach (var item in LineItems)
            Meal.CurrentMeal.LineItems.Add(item);

        // See if there is a scanned subtotal or tax value (if so, take the first)
        decimal ScannedSubTotal = 0, ScannedTax = 0;
        foreach (var item in scannedBill.FormElements)
        {
            if (ScannedSubTotal == 0 && item.FieldName.Equals("Subtotal") && decimal.TryParse(item.FieldValue, out decimal d))
            {
                ScannedSubTotal = d;
                if (ScannedTax > 0)
                    break;
            }
            else if (ScannedTax == 0 && item.FieldName.Equals("TotalTax") && decimal.TryParse(item.FieldValue, out decimal t))
            {
                ScannedTax = t;
                if (ScannedSubTotal > 0)
                    break;
            }
        }
        if (clearItems)
        {
            Meal.CurrentMeal.ScannedSubTotal = ScannedSubTotal;
            Meal.CurrentMeal.ScannedTax = ScannedTax;
        }
        else
        {
            if (ScannedSubTotal == 0)
                Meal.CurrentMeal.ScannedSubTotal = 0; // There's a good chance it's now wrong so just discard it
            else
                Meal.CurrentMeal.ScannedSubTotal += ScannedSubTotal;
            if (ScannedTax == 0)
                Meal.CurrentMeal.ScannedTax = 0; // There's a good chance it's now wrong so just discard it
            else
                Meal.CurrentMeal.ScannedTax += ScannedTax;
        }
        if (Meal.CurrentMeal.ScannedTax > 0)
        {
            decimal diff = ScannedTax - Meal.CurrentMeal.TaxWithoutDelta;
            if (Math.Abs(diff) <= 0.02M)
                Meal.CurrentMeal.TaxDelta = diff;
        }
        Shell.Current.Navigation.RemovePage(this);
        await App.GoToHomeAsync();
    }
    private void OnAddItemList(object sender, EventArgs e) => UpdateCurrentMeal(clearItems: false);

    private void OnReplaceItemList(object sender, EventArgs e) => UpdateCurrentMeal(clearItems: true);

    private void OnCancel(object sender, EventArgs e)
    {
        tokenSource.Cancel(true);
        cancelButton.IsEnabled = false;
    }
    #region IQueryAttributable Implementation
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        scannedBill = query.TryGetValue("ScannedBill", out var scannedBillObject)
            ? scannedBillObject as ScannedBill : new ScannedBill();
        ImagePath = query.TryGetValue("ImagePath", out var imagePathObject)
            ? imagePathObject as string : null;
    }
    #endregion
}
