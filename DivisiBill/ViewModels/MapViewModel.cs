using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using Microsoft.Maui.Maps;
using System.Text.Json.Serialization;

namespace DivisiBill.ViewModels;

[QueryProperty(nameof(MapSettings), "MapSettings")]
public partial class MapViewModel : ObservableObject
{
    // Used to store the original venue location so we can restore it if the user clicks the "Restore" button   
    private Location? originalVenueLocation;

    // Events to notify the view when the location or map type changes so it can handle non-MVVM map features.
    public delegate void LocationChangedEventHandler(object? sender, Location? newLocation);
    public event LocationChangedEventHandler? LocationChanged;
    public event EventHandler? RestoreRequested;
    public event EventHandler<MapType>? MapTypeRequested;
    public event EventHandler? ClearLocationRequested;

    public MapViewModel()
    {
        App.MyLocationChanged += (_, _) => { if (VenueLocation is not null) VenueDistance = App.GetDistanceTo(VenueLocation); };
    }

    /// <summary>
    /// Passed in as a query parameter when navigating to the map page. Contains the initial venue location
    /// and name, and is updated with the new location when the user selects a location and navigates back.
    /// This allows us to return the selected location back to the caller without needing a more complex 
    /// messaging system or shared state. We update the ViewModel properties directly from the MapSettings
    /// when it is set, so that the UI is initialized with the correct values.
    /// </summary>
    public MapSettings? MapSettings
    {
        get => field;
        set
        {
            if (field != value)
            {
                ArgumentNullException.ThrowIfNull(value);
                VenueName = value.VenueName;
                originalVenueLocation = VenueLocation = value.VenueLocation;
                VenueLocationHasChanged = false;
                field = value;
            }
        }
    } = null;

    /// <summary>
    /// We are exiting the map page, so if the venue location has changed, we need to update the MapSettings
    /// to return the new location to the caller.
    /// </summary>
    public void OnNavigatedFrom()
    {
        if (VenueLocationHasChanged && MapSettings != null)
        {
            MapSettings.VenueLocationHasChanged = true;
            MapSettings.VenueLocation = VenueLocation;
        }
    }
    #region Properties
    // This flag is used to track whether the venue location has been changed by the user.
    // If it has, then when we navigate away from the map page, we need to update the MapSettings
    // with the new location so it can be returned to the caller. If it hasn't, then we can leave
    // the MapSettings unchanged to avoid unnecessary updates and potential side effects.
    private bool VenueLocationHasChanged
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ClearLocationCommand.NotifyCanExecuteChanged();
                RestoreCommand.NotifyCanExecuteChanged();
            }
        }
    }


    // The name of the venue is displayed in the UI but does not affect any map functionality,
    // so it does not need to be updated when the location changes.
    // It is only set from the MapSettings when the page is initialized.
    public string VenueName { get; private set; } = string.Empty;

    // The venue location is the core piece of data that this ViewModel manages.
    // When it changes, we need to update the distance and for a native map notify the view so
    // it can update the map marker and accuracy circle.
    [ObservableProperty]
    public partial Location? VenueLocation { get; set; }
    partial void OnVenueLocationChanged(Location? value)
    {
        if (value is null)
        {
            VenueDistance = Distances.Unknown;
            return;
        }
        VenueDistance = App.GetDistanceTo(value);
        VenueLocationHasChanged = true;
        // Notify the view that the location has changed so it can update the map marker and accuracy
        // circle for native maps. For Google Maps, the marker is updated directly from the JavaScript,
        // so the view will not be listening.
        LocationChanged?.Invoke(this, value);
    }

    // The current map type (street or satellite) is stored in the ViewModel so it can be toggled from the UI.
    [ObservableProperty]
    public partial MapType CurrentMapType { get; set; } = MapType.Street;
    partial void OnCurrentMapTypeChanged(MapType value)
    {
        // Notify the view that the map type has changed so it can update the Google map if necessary.
        MapTypeRequested?.Invoke(this, value);
    }

    // The distance to the venue is stored in the ViewModel so it can be displayed in the UI.
    [ObservableProperty]
    public partial int VenueDistance { get; set; } = Distances.Unknown;

    // This property is used to determine whether to show the user's current location on the map.
    public bool MapIsShowingUser => App.UseLocation;
    #endregion
    #region Commands
    /// <summary>
    /// Restores the venue location to its original value and resets the change state.
    /// </summary>
    /// <remarks>This method also raises the RestoreRequested event to notify the view that a restore
    /// operation has occurred.</remarks>
    [RelayCommand(CanExecute = nameof(VenueLocationHasChanged))]
    private void Restore()
    {
        VenueLocation = originalVenueLocation;
        VenueLocationHasChanged = false;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Toggles the current map type between street and satellite views.
    /// </summary>
    [RelayCommand]
    private void ChangeMapType()
    {
        CurrentMapType = CurrentMapType == MapType.Street ? MapType.Satellite : MapType.Street;
    }

    bool CanClearLocation() => VenueLocation != null;

    /// <summary>
    /// Clears the current venue location and raises a request to notify the view of the change.
    /// </summary>
    /// <remarks>This method sets the VenueLocation property to null and triggers the ClearLocationRequested
    /// event. Use this method to reset the location state and notify the view (and theoretically any subscribers)
    /// that the location has been cleared.</remarks>
    [RelayCommand(CanExecute = nameof(CanClearLocation))]
    private void ClearLocation()
    {
        VenueLocation = null;
        ClearLocationRequested?.Invoke(this, EventArgs.Empty);
    }
    #endregion
    #region Google Map Generation
    // The WebViewSource for the Google Map is stored in the ViewModel so it can be initialized with
    // the correct HTML+JavaScript when the page loads <see cref="GenerateGoogleMapHtml"/>.
    [ObservableProperty]
    public partial WebViewSource? GoogleMapSource { get; set; } = null;

    // Initializes the Google Map by generating the HTML+JavaScript with the correct initial center,
    // zoom, and marker based on the venue location.
    public void InitializeGoogleMap()
    {
        if (Utilities.IsWinUI && MapSettings != null)
        {
            double zoom = 15; // Default zoom level for accurate location
            Location? mapCenter = VenueLocation?.IsAccurate() == true ? VenueLocation : App.MyLocation;
            if (mapCenter is null)
                return;
            GoogleMapSource = new HtmlWebViewSource
            {
                Html = GenerateGoogleMapHtml(mapCenter.Latitude, mapCenter.Longitude, zoom)
            };
        }
    }

    /// <summary>
    /// Generates an HTML document containing an interactive Google Map centered at the specified coordinates and zoom
    /// level.
    /// </summary>
    /// <remarks>The generated HTML includes embedded JavaScript to support marker placement, accuracy
    /// visualization, and current location display. The map allows users to select locations and interact with the map,
    /// and is intended for use in a web view or browser control. A valid Google Maps API key is required for the map to
    /// function.</remarks>
    /// <param name="centerLatitude">The latitude, in decimal degrees, at which to center the map.</param>
    /// <param name="centerLongitude">The longitude, in decimal degrees, at which to center the map.</param>
    /// <param name="zoom">The initial zoom level for the map. Valid values typically range from 0 (world view) 
    /// to 21 (street level).</param>
    /// <returns>A string containing the complete HTML markup for a web page that displays a Google Map with interactive
    /// features.</returns>
    public string GenerateGoogleMapHtml(double centerLatitude, double centerLongitude, double zoom)
    {
        bool hasAccurateLocation = VenueLocation?.IsAccurate() == true;
        string initialMarkerHtml = hasAccurateLocation
            ? $"updateMarker({VenueLocation?.Latitude:F5}, {VenueLocation?.Longitude:F5}, {VenueLocation?.Accuracy:F0});"
            : "";
        string restoreMarkerHtml = hasAccurateLocation ? initialMarkerHtml : "clearMarker();";
        string showCurrentLocationHtml = App.UseLocation && App.MyLocation is not null
            ? $"showCurrentLocation({App.MyLocation.Latitude:F5}, {App.MyLocation.Longitude:F5}, {App.MyLocation?.Accuracy ?? 0:F0});"
            : "";

        // HTML with embedded JavaScript to display Google Maps and handle user interaction
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
                gmp-map {
                    height: 100%;
                    width: 100%;
                }
            </style>
        </head>
        <body>
            <gmp-map map-id="DEMO_MAP_ID"></gmp-map>
            <script>
                // Map state variables
                let map;
                let marker = null;       // User-selected location marker
                let circle = null;       // Accuracy radius circle around marker
                let currentLocationMarker = null;
                let currentLat = {{centerLatitude:F5}};
                let currentLng = {{centerLongitude:F5}};
                let currentZoom = {{zoom:F0}};

                // Initializes the Google Map and sets up click listeners for location selection
                // Called by Google once the map has loaded because of the "callback=initMap" parameter in the script URL
                async function initMap() {
                    const mapElement = document.querySelector('gmp-map');

                    mapElement.center = { lat: currentLat, lng: currentLng };
                    mapElement.zoom = Math.max(0, Math.min(21, currentZoom));
                    mapElement.mapTypeControl = false;
                    mapElement.fullscreenControl = false;
                    mapElement.streetViewControl = false;

                    map = await mapElement.innerMap;

                    // Handle user clicks on the map to select a new location
                    map.addListener('click', function (event) {
                        const lat = event.latLng.lat();
                        const lng = event.latLng.lng();

                        const bounds = map.getBounds();
                        if (bounds) {
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
                    {{showCurrentLocationHtml}}
                }

                // Updates the marker position and displays an accuracy circle around it (used when user selects a new location)
                function updateMarker(lat, lng, accuracy) {
                    // Remove existing marker and accuracy circle
                    if (marker) marker.setMap(null);
                    if (circle) circle.setMap(null);

                    // Add new marker at selected location
                    marker = new google.maps.marker.AdvancedMarkerElement({
                            position: { lat: lat, lng: lng },
                            map: map,
                            title: '{{VenueName}}'
                        });

                    // Add semi-transparent circle to visualize location accuracy radius
                    circle = new google.maps.Circle({
                        map: map,
                        center: { lat: lat, lng: lng },
                        radius: accuracy,
                        fillColor: '#FF0000',
                        fillOpacity: 0.1,
                        strokeWeight: 0,
                        editable: false,
                        clickable: false
                    });
                }

                // Toggles between satellite and road map view (called when user clicks "Change Map Type" button in C# code)
                function changeMapType(mapType) {
                    if (map)
                        map.setMapTypeId(mapType);
                }

                // Removes the marker and accuracy circle from the map (used when user clicks "Clear Location" button in C# code)
                function clearMarker() {
                    if (marker) marker.setMap(null);
                    if (circle) circle.setMap(null);
                    marker = null;
                    circle = null;
                }
        
                // Restore initial map state (used when user clicks "Restore" button in C# code)
                // Note that this should not restore the current location because it may have changed
                function mapRestore() {
                    map.setZoom({{zoom:F0}});
                    map.setCenter({ lat:{{centerLatitude:F5}}, lng:{{centerLongitude:F5}} });
                    {{restoreMarkerHtml}}
                }

                // Displays the current user position with a blue marker (distinct from the selected location marker)
                let currentLocationCircle = null;
                function showCurrentLocation(lat, lng, accuracy) {
                    // Remove existing current location marker and circle if any
                    if (currentLocationMarker) currentLocationMarker.setMap(null);
                    if (currentLocationCircle) currentLocationCircle.setMap(null);

                    // Add new marker for current location
                    const bluePin = document.createElement('div');
                    bluePin.style.width = '10px';
                    bluePin.style.height = '10px';
                    bluePin.style.borderRadius = '50%';
                    bluePin.style.backgroundColor = '#4285F4';
                    bluePin.style.border = '2px solid white';
                    bluePin.style.boxShadow = '0 2px 6px rgba(0,0,0,0.3)';
                    bluePin.style.transform = 'translate(0, 50%)';
                    bluePin.style.position = 'relative';

                    currentLocationMarker = new google.maps.marker.AdvancedMarkerElement({
                        position: { lat: lat, lng: lng },
                        map: map,
                        title: 'Current Location',
                        content: bluePin,
                        gmpClickable: false // Prevent clicks on the current location marker from triggering map click events
                    });

                    // Add blue semi-transparent circle to visualize current location accuracy radius
                    if (accuracy && accuracy > 0) {
                        currentLocationCircle = new google.maps.Circle({
                            map: map,
                            center: { lat: lat, lng: lng },
                            radius: accuracy,
                            fillColor: '#4285F4',
                            fillOpacity: 0.15,
                            strokeColor: '#4285F4',
                            strokeOpacity: 0.3,
                            strokeWeight: 1,
                            editable: false,
                            clickable: false
                        });
                    }
                }

                // Removes the current location marker and circle from the map
                function clearCurrentLocation() {
                    if (currentLocationMarker) currentLocationMarker.setMap(null);
                    if (currentLocationCircle) currentLocationCircle.setMap(null);
                    currentLocationMarker = null;
                    currentLocationCircle = null;
                }
                </script>
            <script src='https://maps.googleapis.com/maps/api/js?key={{Generated.BuildInfo.DivisiBillMapsKey}}&callback=initMap&libraries=marker&v=beta' async defer></script>
        </body>
        </html>
        """;
        return html;
    }
    #endregion
    #region Google Map Messages
#if WINDOWS
    class GoogleMapMessage
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;
        [JsonPropertyName("lat")]
        public double Latitude { get; set; }
        [JsonPropertyName("lng")]
        public double Longitude { get; set; }
        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }
    }

    public async void HandleGoogleMapMessage(string jsonString)
    {

        var msg = System.Text.Json.JsonSerializer.Deserialize<GoogleMapMessage>(jsonString);
        if (msg is not null && msg.Event == "NewLocation" && msg.Latitude != 0 && msg.Longitude != 0 && msg.Accuracy != 0)
        {
            double roundedAccuracy = Utilities.Simplified(msg.Accuracy);
            if (roundedAccuracy > Distances.AccuracyLimit)
                await Utilities.DisplayAlertAsync("Location is not accurate",
                    "The location you selected has an accuracy of about " + (int)(roundedAccuracy / 1000)
                    + " km, which is too high to be useful. Please zoom in and select a more accurate location.", "OK");
            else
                VenueLocation = new Location(msg?.Latitude ?? 0, msg?.Longitude ?? 0) { Accuracy = roundedAccuracy };
        }
    }
#endif 
    #endregion
}