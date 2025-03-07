using CommunityToolkit.Maui.Alerts;
using DivisiBill.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace DivisiBill.Views;

public partial class MapPage : ContentPage
{
    private Location originalVenueLocation;
    private readonly Pin pin = new() { Type = PinType.Place }; // No location or name yet

    public MapPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        await App.StartMonitoringLocation();
        base.OnAppearing();
        Location mapCenter;
        originalVenueLocation = VenueLocation; // Use to restore location if the user asks
        if (VenueLocation.IsAccurate())
        {
            mapCenter = VenueLocation;
            MovePin();
        }
        else
            mapCenter = App.UseLocation ? App.MyLocation : null;
        if (mapCenter is not null)
        {
            var mapSpan = new MapSpan(mapCenter, 0.01, 0.01);
            await Task.Delay(200); // Without this the MoveToRegion is ignored 
            map.MoveToRegion(mapSpan);
        }
        VenueLocationHasChanged = false;
        App.MyLocationChanged += App_MyLocationChanged;
    }

    protected override async void OnDisappearing()
    {
        App.MyLocationChanged -= App_MyLocationChanged;
        base.OnDisappearing();
        await App.StopMonitoringLocation();
    }

    private void App_MyLocationChanged(object sender, EventArgs e) => VenueDistance = App.GetDistanceTo(VenueLocation);

    /// <summary>
    /// Takes a number and returns the nearest 'simpler' one. A simpler number has all zeros, except the first digit
    /// </summary>
    /// <param name="d"></param>
    /// <returns>Simpler number</returns>
    private static double Simplified(double d)
    {
        if (d <= 0) return d;

        double digits = Math.Floor(Math.Log10(d));
        double exponent = Math.Pow(10, digits);
        double mantissa = d / exponent;
        mantissa = Math.Round(mantissa);
        return mantissa * exponent;
    }
    private async void OnMapClicked(object _, MapClickedEventArgs e)
    {
        // A round number based on there being 20 distinct points across the screen
        double fingerWidth = Simplified(map.VisibleRegion.Radius.Meters / 10.0);
        e.Location.Accuracy = fingerWidth;
        VenueLocation = e.Location;
        MovePin();
        if (fingerWidth > Distances.AccuracyLimit)
            await Toast.Make("Location is not accurate, zoom in").Show();
    }

    private void MovePin()
    {
        map.Pins.Remove(pin); // it might not be in the map if the location was previously unknown, but this just won't do anything then
        map.MapElements.Remove(AccuracyCircle);
        if (VenueLocation.IsAccurate())
        {
            // Place the pin
            pin.Location = VenueLocation;
            map.Pins.Add(pin);
            // Draw accuracy circle around the pin
            AccuracyCircle.Center = VenueLocation;
            AccuracyCircle.Radius = Distance.FromMeters(VenueLocation.Accuracy.GetValueOrDefault());
            map.MapElements.Add(AccuracyCircle);
        }
    }

    public bool VenueLocationHasChanged = false;

    // BindingContext
    public string VenueName
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref field, value);
            pin.Label = VenueName;
        }
    }
    public Location VenueLocation
    {
        get;
        set
        {
            if ((value is null && field is not null) || value.GetDistanceTo(field) > 0)
            {
                field = value;
                VenueDistance = App.GetDistanceTo(field);
                MovePin();
                VenueLocationHasChanged = true;
                OnPropertyChanged();
            }
        }
    }
    public int VenueDistance
    {
        get;
        set => SetProperty(ref field, value);
    } = Distances.Unknown;

    public bool MapIsShowingUser => App.UseLocation;

    #region Commands
    public ICommand RestoreCommand => new Command(async () =>
    {
        VenueLocation = originalVenueLocation;
        if (VenueLocation is not null)
        {
            var mapSpan = new MapSpan(VenueLocation, 0.01, 0.01);
            if (mapSpan is not null)
            {
                await Task.Delay(200); // Without this the MoveToRegion is ignored 
                map.MoveToRegion(mapSpan);
            }
        }
    });
    public ICommand MapTypeCommand => new Command(() =>
    {
        map.MapType = map.MapType == MapType.Street ? MapType.Satellite : MapType.Street;
    });
    public ICommand ClearLocationCommand => new Command(() =>
    {
        VenueLocation = null;
    });
    #endregion

    protected bool SetProperty<T>(ref T backingStore, T value,
    Action onChanged = null,
    [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        onChanged?.Invoke();
        OnPropertyChanged(propertyName);
        return true;
    }

}