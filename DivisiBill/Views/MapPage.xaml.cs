using CommunityToolkit.Maui.Alerts;
using DivisiBill.Controls;
using DivisiBill.Models;
using DivisiBill.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;



#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
#endif
namespace DivisiBill.Views;

/// <summary>
/// This page is used whenever it is necessary to specify a map location, either when editing
/// a venue or specifying a fake location in debug mode. It allows the user to select a location on a map
/// and gives an indication of the location name and accuracy.
/// </summary>
[QueryProperty(nameof(MapSettings), "MapSettings")]
public partial class MapPage : ContentPage
{
    private Location originalVenueLocation; // Use to restore location if the user asks
    private readonly Pin pin = new() { Type = PinType.Place }; // No location or name yet
    private Microsoft.Maui.Controls.Maps.Map map; // Map control created in code-behind
    private bool VenueLocationHasChanged { get; set; } = false;

    public MapPage()
    {
        InitializeComponent();
        if (googleMapsWebView.IsVisible)
            googleMapsWebView.HandlerChanged += OnHandlerChanged;
        else
        {
            // Create a map and use it to replace the WebView on non-Windows platforms - it is used on
            // Windows because the native map control is not supported, so we use a WebView with Google Maps instead.
            // On other platforms we can just use the native map control, which has better performance.

            // Create the Map control
            map = new Microsoft.Maui.Controls.Maps.Map();

            // Assign event handler for map clicks to allow the user to select a location
            map.MapClicked += OnMapClicked;

            // Set the map to occupy the same row as the (invisible)WebView, so it will be in the same place in the layout
            ColumnLayout.SetSameRow(map, true);

            // Bind IsShowingUser property
            map.SetBinding(Microsoft.Maui.Controls.Maps.Map.IsShowingUserProperty,
                new Binding(nameof(MapIsShowingUser), mode: BindingMode.OneTime));

            // Insert the map after the WebView)
            (_, int index) = rootLayout.Children.FindItemAndIndex((view) => view == googleMapsWebView);
            rootLayout.Children.Insert(index + 1, map);
        }
    }

    /// <summary>
    /// Used to specify the name and location of a venue, or to specify a fake location in debug mode.
    /// This is input to the MapPage, which allows the user to select change the location on a map or clear it.
    /// </summary>
    public MapSettings MapSettings { get; set; }
    protected override async void OnAppearing()
    {
        await App.StartMonitoringLocation();
        base.OnAppearing();
        App.MyLocationChanged += App_MyLocationChanged;
    }
    protected override async void OnDisappearing()
    {
        App.MyLocationChanged -= App_MyLocationChanged;
        await App.StopMonitoringLocation();
        base.OnDisappearing();
    }
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ArgumentNullException.ThrowIfNull(MapSettings);
        VenueName = MapSettings.VenueName;
        originalVenueLocation = VenueLocation = MapSettings.VenueLocation;
        VenueLocationHasChanged = false;

        Location mapCenter = VenueLocation ?? (App.UseLocation ? App.MyLocation : null) ?? Venue.MiddleOfNowhere;

        if (googleMapsWebView.IsVisible)
        { 
            await LoadGoogleMap(mapCenter, zoom: 15);
            if (App.UseLocation && App.MyLocation != null)
            {
                await Task.Delay(500); // Wait for the map to load
                await googleMapsWebView.EvaluateJavaScriptAsync(
                    $"showCurrentLocation({App.MyLocation.Latitude:F5}, {App.MyLocation.Longitude:F5});");
            }
        }
        else
        {
            if (VenueLocation.IsAccurate())
                MovePin();
            MapSpan mapSpan = new(mapCenter, 0.01, 0.01);
            await Task.Delay(200); // Without this the MoveToRegion is ignored 
            map.MoveToRegion(mapSpan);
        }
    }
    protected override void OnNavigatingFrom(NavigatingFromEventArgs args)
    {
        base.OnNavigatingFrom(args);
        if (VenueLocationHasChanged)
        {
            MapSettings.VenueLocationHasChanged = true;
            MapSettings.VenueLocation = VenueLocation;
        }
    }
    private void OnHandlerChanged(object sender, EventArgs e)
    {
#if WINDOWS
        if (googleMapsWebView.Handler?.PlatformView is WebView2 wv2)
        {
            // Wait for WebView2 to finish initializing
            wv2.CoreWebView2Initialized += async (sender, _) =>
            {
                var wv2 = (WebView2)sender;
                CoreWebView2 core = wv2.CoreWebView2;

                // Example: listen for JS -> C# messages
                core.WebMessageReceived += (s, msg) =>
                {
                    var text = msg.TryGetWebMessageAsString();
                    HandleGoogleMapMessage(text);
                    Utilities.DebugMsg("JS -> C#: " + text);
                };
            };
        }
#endif
    }

    private async void App_MyLocationChanged(object sender, EventArgs e)
    {
        VenueDistance = App.GetDistanceTo(VenueLocation);
        if (googleMapsWebView.IsVisible && App.UseLocation)
                await googleMapsWebView.EvaluateJavaScriptAsync(App.MyLocation is null 
                    ? "clearCurrentLocation();"
                    : $"showCurrentLocation({App.MyLocation.Latitude:F5}, {App.MyLocation.Longitude:F5});");
    }

    /// <summary>
    /// Takes a number and returns the nearest 'simpler' one. A simpler number has all zeros, except the first digit
    /// </summary>
    /// <param name="d"></param>
    /// <returns>Simpler number</returns>
    private static double Simplified(double d)
    {
        if (d <= 0)
            return d;

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

#if WINDOWS
    class GoogleMapMessage
    {
        [JsonPropertyName("event")]
        public string Event { get; set; }
        [JsonPropertyName("lat")]
        public double Latitude { get; set; }
        [JsonPropertyName("lng")]
        public double Longitude { get; set; }
        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }
    }

    private async void HandleGoogleMapMessage(string jsonString)
    {

        var msg = System.Text.Json.JsonSerializer.Deserialize<GoogleMapMessage>(jsonString);
        if (msg is not null && msg.Event == "NewLocation" && msg.Latitude != 0 && msg.Longitude != 0 && msg.Accuracy != 0)
        {
            double roundedAccuracy = Simplified(msg.Accuracy);
            if (roundedAccuracy > Distances.AccuracyLimit)
                await Utilities.DisplayAlertAsync("Location is not accurate",
                    "The location you selected has an accuracy of about " + (int)(roundedAccuracy / 1000)
                    + " km, which is too high to be useful. Please zoom in and select a more accurate location.", "OK");
            else
                VenueLocation = new Location(msg?.Latitude ?? 0, msg?.Longitude ?? 0) { Accuracy = roundedAccuracy };
        }
    }
#endif

    private bool isSatelliteMap = false;

    private async Task LoadGoogleMap(Location mapCenter, double zoom)
    {
        string htmlContent = GenerateGoogleMapHtml(mapCenter.Latitude, mapCenter.Longitude, zoom);
        googleMapsWebView.Source = new HtmlWebViewSource { Html = htmlContent };
    }

    private string GenerateGoogleMapHtml(double latitude, double longitude, double zoom)
    {
        bool hasAccurateLocation = VenueLocation?.IsAccurate() == true;
        string initialMarkerHtml = hasAccurateLocation
            ? $"updateMarker({VenueLocation.Latitude:F5}, {VenueLocation.Longitude:F5}, {VenueLocation.Accuracy:F0});"
            : "";
        string restoreMarkerHtml = hasAccurateLocation ? initialMarkerHtml : "clearMarker();";

        // lang-independent HTML with embedded JavaScript to display Google Maps and handle user interaction
        string html = $$"""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8' />
            <meta name='viewport' content='width=device-width, initial-scale=1.0' />
            <title>Map</title>
            <style>
                html, body {
                    margin: 0;
                    padding: 0;
                    height: 100%;
                    width: 100%;
                }
                #map {
                    height: 100%;
                    width: 100%;
                }
            </style>
        </head>
        <body>
            <div id='map'></div>
            <script>
                // Map state variables
                let map;
                let marker = null;       // User-selected location marker
                let circle = null;       // Accuracy radius circle around marker
                let currentLocationMarker = null;
                let currentLat = {{latitude:F5}};
                let currentLng = {{longitude:F5}};
                let currentZoom = {{zoom:F0}};

                // Initializes the Google Map and sets up click listeners for location selection
                // Called by Google once the map has loaded because of the "callback=initMap" parameter in the script URL
                function initMap() 
                {
                    map = new google.maps.Map(document.getElementById('map'), 
                    {
                        center: { lat: currentLat, lng: currentLng },
                        zoom: Math.max(0, Math.min(21, currentZoom)),
                        mapTypeControl:false,
                        fullscreenControl: false,
                        streetViewControl: false
                    });

                    // Handle user clicks on the map to select a new location
                    map.addListener('click', function(event) 
                    {
                        const lat = event.latLng.lat();
                        const lng = event.latLng.lng();

                        const bounds = map.getBounds();
                        if (bounds) 
                        {
                            // Calculate accuracy in meters based on current zoom level
                            // Approximate accuracy as 20 pixels at current zoom (approximately finger tap precision)
                            const metersPerPixel = 156543.04 * Math.cos(map.getCenter().lat() * Math.PI / 180) / Math.pow(2, map.getZoom());
                            const accuracy = metersPerPixel * 20;

                            updateMarker(lat, lng, accuracy);
                            // Send selected location back to C# code
                            window.chrome.webview.postMessage(JSON.stringify({
                                event: "NewLocation",
                                lat: lat,
                                lng: lng,
                                accuracy: accuracy
                            }));
                        }
                    });
                    {{initialMarkerHtml}}
                }

                // Updates the marker position and displays an accuracy circle around it (used when user selects a new location)
                function updateMarker(lat, lng, accuracy) 
                {
                    currentLat = lat;
                    currentLng = lng;

                    // Remove existing marker and accuracy circle
                    if (marker) marker.setMap(null);
                    if (circle) circle.setMap(null);

                    // Add new marker at selected location
                    marker = new google.maps.Marker(
                    {
                        position: { lat: lat, lng: lng },
                        map: map,
                        title: 'Selected Location',
                        zIndex: 200
                    });

                    // Add semi-transparent circle to visualize location accuracy radius
                    circle = new google.maps.Circle(
                    {
                        map: map,
                        center: { lat: lat, lng: lng },
                        radius: accuracy,
                        fillColor: '#FF0000',
                        fillOpacity: 0.1,
                        strokeWeight: 0,
                        editable: false
                    });
                }

                // Toggles between satellite and road map view (called when user clicks "Change Map Type" button in C# code)
                function changeMapType(mapType)
                {
                    if (map)
                        map.setMapTypeId(mapType);
                }

                // Removes the marker and accuracy circle from the map (used when user clicks "Clear Location" button in C# code)
                function clearMarker() 
                {
                    if (marker) marker.setMap(null);
                    if (circle) circle.setMap(null);
                    marker = null;
                    circle = null;
                }
        
                // Restore initial map state (used when user clicks "Restore" button in C# code)
                function mapRestore() 
                {
                    map.setZoom({{zoom:F0}});
                    map.setCenter({ lat:{{latitude:F5}}, lng:{{longitude:F5}} });
                    {{restoreMarkerHtml}}
                }

                // Displays the current user position with a blue marker (distinct from the selected location marker)
                function showCurrentLocation(lat, lng)
                {
                    // Remove existing current location marker if any
                    if (currentLocationMarker) currentLocationMarker.setMap(null);
                    
                    // Add new marker for current location
                    currentLocationMarker = new google.maps.Marker(
                    {
                        position: { lat: lat, lng: lng },
                        map: map,
                        title: 'Current Location',
                        zIndex: 100,
                        icon: 'http://maps.google.com/mapfiles/ms/icons/blue-dot.png'  // Blue marker for current location
                    });
                }

                // Removes the current location marker from the map
                function clearCurrentLocation() 
                {
                    if (currentLocationMarker) currentLocationMarker.setMap(null);
                    currentLocationMarker = null;
                }
                </script>
            <script src='https://maps.googleapis.com/maps/api/js?key={{Generated.BuildInfo.DivisiBillMapsKey}}&callback=initMap' async defer></script>
        </body>
        </html>
        """;
        return html;
    }


    // BindingContext
    public string VenueName
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref field, value);
            pin.Label = value;
        }
    }
    public Location VenueLocation
    {
        get => field;
        set
        {
            if ((value is null && field is not null) || (value is not null && value.GetDistanceTo(field) > 0))
            {
                field = value;
                VenueDistance = App.GetDistanceTo(field);
                if (!Utilities.IsWinUI) MovePin();
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
        VenueLocationHasChanged = false;
        if (googleMapsWebView.IsVisible)
            await googleMapsWebView.EvaluateJavaScriptAsync("mapRestore()"); 
        else if (VenueLocation is not null)
        {
            MapSpan mapSpan = new(VenueLocation, 0.01, 0.01);
            if (mapSpan is not null)
            {
                await Task.Delay(200); // Without this the MoveToRegion is ignored 
                map.MoveToRegion(mapSpan);
            } 
	    }
    });
    public ICommand MapTypeCommand => new Command(async () =>
    {
        if (googleMapsWebView.IsVisible)
        {
            isSatelliteMap = !isSatelliteMap;
            string mapType = isSatelliteMap ? "satellite" : "roadmap";
            await googleMapsWebView.EvaluateJavaScriptAsync($"changeMapType('{mapType}')");
        }
        else
            map.MapType = map.MapType == MapType.Street ? MapType.Satellite : MapType.Street;
    });
    public ICommand ClearLocationCommand => new Command(async () =>
    {
        VenueLocation = null;
        if (googleMapsWebView.IsVisible)
            await googleMapsWebView.EvaluateJavaScriptAsync("clearMarker()");
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