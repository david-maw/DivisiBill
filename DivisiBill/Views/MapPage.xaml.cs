using CommunityToolkit.Maui.Alerts;
using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;

#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
#endif
namespace DivisiBill.Views;

/// <summary>
/// This page is used whenever it is necessary to specify a map location, either when editing
/// a venue or specifying a fake location in debug mode. It allows the user to select a location on a map
/// and gives an indication of the location name and accuracy.It uses a WebView with Google Maps on Windows 
/// because the native map control is not supported, and the native map control on other platforms because
/// it has better performance. The page subscribes to location updates when it appears and unsubscribes
/// when it disappears so it can show a current distance to the target location. Because neither map 
/// implementation is especially MVVM compliantIt also handles various events from the ViewModel to update
/// the map view accordingly.
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly Pin nativePin = new() { Type = PinType.Place }; // No location or name yet
    private readonly MapViewModel viewModel; // Our ViewModel, which is the BindingContext of the page
    public MapPage()
    {
        InitializeComponent();
        BindingContext = viewModel = new MapViewModel();

        // Subscribe to ViewModel events
        viewModel.LocationChanged += ViewModel_LocationChanged;
        viewModel.RestoreRequested += ViewModel_RestoreRequested;
        viewModel.MapTypeRequested += ViewModel_MapTypeRequested;
        viewModel.ClearLocationRequested += ViewModel_ClearLocationRequested;

#if WINDOWS
        /// Handle changes to the WebView handler to set up message communication between JavaScript
        /// and the view model using WebView2.
        googleMapsWebView.HandlerChanged += (sender, e) =>
        {
            if (sender is WebView wv && wv.Handler?.PlatformView is WebView2 wv2)
            {
                // Wait for WebView2 to finish initializing
                wv2.CoreWebView2Initialized += async (sender, _) =>
                {
                    var wv2 = (WebView2)sender;
                    CoreWebView2 core = wv2.CoreWebView2;

                    // Listen for JS -> C# messages
                    core.WebMessageReceived += (s, msg) =>
                    {
                        var text = msg.TryGetWebMessageAsString();
                        viewModel.HandleGoogleMapMessage(text);
                    };
                };
            }
        };
#endif
    }
    #region Page Appearing/Disappearing
    /// <summary>
    /// When the page appears subscribe to location updates and start location monitoring.
    /// </summary>
    /// <remarks>This method subscribes to location change events and registers for location monitoring each time
    /// the page becomes visible.</remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Utilities.IsWinUI)
        {
            App.MyLocationChanged += App_MyLocationChanged;
            await App.StartMonitoringLocation();
        }
    }

    /// <summary>
    /// Undoes <see cref="OnAppearing"/> when the page is about to disappear from view.
    /// </summary>
    /// <remarks>This method unsubscribes from location change events and unregisters for location monitoring to release
    /// resources and prevent unnecessary updates when the page is not visible. It is called automatically by the
    /// framework when the page is being removed from the navigation stack or otherwise hidden.</remarks>
    protected override async void OnDisappearing()
    {
        if (Utilities.IsWinUI)
        {
            App.MyLocationChanged -= App_MyLocationChanged;
            await App.StopMonitoringLocation();
        }
        base.OnDisappearing();
    }
    #endregion
    #region Navigation to and From This Page
    /// <summary>
    /// Handles logic to be executed when the page is navigated to, initializing map settings and updating the map view
    /// as needed.
    /// </summary>
    /// <remarks>This method ensures that the map and its related settings are properly initialized when the
    /// page becomes active. It updates the pin label to reflect the current venue name and configures the map view
    /// based on the application's state and available location data.</remarks>
    /// <param name="args">The event data that contains information about the navigation event.</param>
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        // We cannot do anything useful unless we have a MapSettings object
        ArgumentNullException.ThrowIfNull(viewModel?.MapSettings);

        // On Windows we have to wait until the WebView is fully initialized before we can do anything with it,
        // but on other platforms we can initialize the map immediately. Where possible use a runtime check of the platform
        // rather than compile-time directives so code is always checked for syntax. We still have to use compile-time
        // directives to avoid referencing WebView2 types on platforms where they are not supported.
        if (Utilities.IsWinUI) // The windows code using Google maps in a web page
        {
#if WINDOWS
            if (googleMapsWebView.Handler?.PlatformView is WebView2 wv2)
                await wv2.EnsureCoreWebView2Async();
#endif
            viewModel.InitializeGoogleMap();
        }
        else
        {
            // Set initial pin label, the venue name does not change in this page
            nativePin.Label = viewModel.VenueName;
            // Center the map on the venue location if we have one
            Location mapCenter = viewModel.VenueLocation ?? (App.UseLocation ? App.MyLocation : null) ?? Venue.MiddleOfNowhere;
            // Move the map pin to the venue location if we know it 
            if (viewModel.VenueLocation?.IsAccurate() ?? false)
                MoveNativePin();
            // Calculate how big a sensibly scaled map should be, then size the map view appropriately
            MapSpan mapSpan = new(mapCenter, 0.01, 0.01);
            await Task.Delay(200); // Without this the MoveToRegion is ignored 
            nativeMap.MoveToRegion(mapSpan);
        }
    }

    /// <summary>
    /// Invoked when the navigation framework is about to navigate away from this page, notifies the ViewModel
    /// that we are done.
    /// </summary>
    /// <param name="args">The event data that contains information about the navigation request.</param>
    protected override void OnNavigatingFrom(NavigatingFromEventArgs args)
    {
        base.OnNavigatingFrom(args);
        viewModel.OnNavigatedFrom();
    }
    #endregion
    #region My Location Changes
    /// <summary>
    /// Handles changes to the application's current location and updates the Google map display accordingly.
    /// </summary>
    /// <remarks>This method updates the Google map view to reflect the application's current location when location
    /// services are enabled and the application is running in a WinUI environment.</remarks>
    /// <param name="sender">The source of the event, typically the object that triggered the location change.</param>
    /// <param name="e">An EventArgs object that contains no event data.</param>
    private async void App_MyLocationChanged(object? sender, EventArgs e)
    {
        if (Utilities.IsWinUI && App.UseLocation)
            await googleMapsWebView.EvaluateJavaScriptAsync(App.MyLocation is null
                ? "clearCurrentLocation();"
                : $"showCurrentLocation({App.MyLocation.Latitude:F5}, {App.MyLocation.Longitude:F5}, {App.MyLocation.Accuracy ?? 0:F0});");
    }
    #endregion
    #region Native Map Venue Location Changes
    /// <summary>
    /// Handles the native map click event by updating the venue location based on the clicked position.
    /// </summary>
    /// <remarks>If the map is zoomed out and the location accuracy is insufficient, a notification is
    /// displayed to prompt the user to zoom in for a more precise selection.</remarks>
    /// <param name="_">An unused sender parameter required by the event handler signature.</param>
    /// <param name="e">The event data containing information about the map click, including the clicked
    /// location.</param>
    private async void OnNativeMapClicked(object? _, MapClickedEventArgs e)
    {
        if (nativeMap?.VisibleRegion is null)
            return; // This should never happen, but just in case, don't do anything if we don't have a visible region to work with
        // A round number based on there being 20 distinct points across the screen
        double fingerWidth = Utilities.Simplified(nativeMap.VisibleRegion.Radius.Meters / 10.0);
        e.Location.Accuracy = fingerWidth;
        viewModel.VenueLocation = e.Location;
        if (fingerWidth > Distances.AccuracyLimit)
            await Toast.Make("Location is not accurate, zoom in").Show();
    }

    /// <summary>
    /// Updates the native map to reflect the current venue location by repositioning the pin and accuracy circle based
    /// on the latest location data.
    /// </summary>
    /// <remarks>If the venue location is accurate, the method places the pin at the specified location and
    /// draws an accuracy circle around it. If the location is not accurate, any existing pin and accuracy circle are
    /// removed from the map. This method does not throw an exception if the pin or accuracy circle is not present on
    /// the map.</remarks>
    private void MoveNativePin()
    {
        if (nativeMap is null)
            return; // Just in case, should never happen)
        nativeMap.Pins.Remove(nativePin); // it might not be in the map if the location was previously unknown, but this just won't do anything then
        nativeMap.MapElements.Remove(nativeAccuracyCircle);
        if (viewModel.VenueLocation?.IsAccurate() ?? false)
        {
            // Place the pin
            nativePin.Location = viewModel.VenueLocation;
            nativePin.Label = viewModel.VenueName;
            nativeMap.Pins.Add(nativePin);
            // Draw accuracy circle around the pin
            nativeAccuracyCircle.Center = viewModel.VenueLocation;
            nativeAccuracyCircle.Radius = Distance.FromMeters(viewModel.VenueLocation.Accuracy.GetValueOrDefault());
            nativeMap.MapElements.Add(nativeAccuracyCircle);
        }
    }
    #endregion
    #region ViewModel Event Handlers
    /// <summary>
    /// Notifies the native map to update the pin location when the view model's venue location changes.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="location">The new location data associated with the event.</param>
    private void ViewModel_LocationChanged(object? sender, Location? location)
    {
        if (!Utilities.IsWinUI)
            MoveNativePin();
        // On Windows the map is updated directly from the ViewModel via JavaScript,
        // so we don't need to do anything here.
    }

    /// <summary>
    /// Handles the restore request event to reset the map view to its default state or a predefined location.
    /// </summary>
    /// <remarks>On WinUI platforms, this method invokes a JavaScript function to restore the map. On other
    /// platforms, it resets the map view to the venue location if available.</remarks>
    /// <param name="sender">The source of the event that triggered the restore request.</param>
    /// <param name="e">An object that contains the event data.</param>
    private async void ViewModel_RestoreRequested(object? sender, EventArgs e)
    {
        if (Utilities.IsWinUI)
            await googleMapsWebView.EvaluateJavaScriptAsync("mapRestore()");
        else if (viewModel.VenueLocation is not null)
        {
            MapSpan mapSpan = new(viewModel.VenueLocation, 0.01, 0.01);
            if (mapSpan is not null)
            {
                await Task.Delay(200); // Without this the MoveToRegion is ignored 
                nativeMap.MoveToRegion(mapSpan);
            }
        }
    }

    /// <summary>
    /// Handles a request to change the map type in the view model.
    /// </summary>
    /// <remarks>This method updates the map display based on the requested map type when running in a WinUI
    /// environment, binding to the MapType property is used for the native map so there's nothing to do here.</remarks>
    /// <param name="sender">The source of the event that triggered the map type change.</param>
    /// <param name="mapType">The map type to apply. Specifies the desired map display mode.</param>
    private async void ViewModel_MapTypeRequested(object? sender, MapType mapType)
    {
        if (Utilities.IsWinUI)
        {
            string mapTypeString = mapType == MapType.Satellite ? "satellite" : "roadmap";
            await googleMapsWebView.EvaluateJavaScriptAsync($"changeMapType('{mapTypeString}')");
        }
    }

    /// <summary>
    /// Handles a request to clear the location marker from the map view.
    /// </summary>
    /// <remarks>This method is invoked in response to a command to remove a marker
    /// from the map. It is only needed when running in a WinUI environment, native maps
    /// can handle this using data binding.</remarks>
    /// <param name="sender">The source of the event that triggered the request.</param>
    /// <param name="e">An EventArgs object that contains the event data.</param>
    private async void ViewModel_ClearLocationRequested(object? sender, EventArgs e)
    {
        if (Utilities.IsWinUI)
            await googleMapsWebView.EvaluateJavaScriptAsync("clearMarker()");
    }
    #endregion
}