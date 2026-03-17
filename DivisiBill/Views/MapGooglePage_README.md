# Google Maps Alternative Implementation

## Overview
`MapPage.googleMapsWebView` is an alternative map implementation that uses the Google Maps API embedded in a WebView instead of the .NET MAUI Maps control. This provides a solution when the Maps control cannot be used (basically on Windows - so for debugging primarily).

## Features (the same as with the MapPage)
- **Location Selection**: Click on the map to select a location
- **Accuracy Circle**: Visual representation of location accuracy with a red circle
- **Map Type Toggle**: Switch between road map and satellite views
- **Restore Location**: Restore the original location with the restore command
- **Clear Location**: Clear the selected location entirely
- **User Location Tracking**: Optionally shows your current location (if enabled)
- **Distance Display**: Shows distance to the selected venue

## Usage
Instead of using `Map`, use `googleMapsWebView` as necessary:

## Important Configuration
The Google Maps API key needs to be set in the HTML generation. It uses a value derived from an environment variable DIVISIBILL_MAPS_KEY:
```
$"https://maps.googleapis.com/maps/api/js?key={Generated.BuildInfo.DivisiBillMapsKey}&callback=initMap"
```

### To make it work:
1. Get a Google Maps API key from [Google Cloud Console](https://console.cloud.google.com/)
2. Set DIVISIBILL_MAPS_KEY with your actual API key
3. Ensure the API key has these services enabled:
   - Maps JavaScript API

## Differences from Map
| Feature | Map | Google Maps |
|---------|---------|---------------|
| Control Type | MAUI Maps | WebView |
| Accuracy Circle | Built-in | CSS Circle |
| Map Provider | Platform default | Google Maps |
| Performance | Native | Web-based |
| Configuration | Requires API key | Requires API key |

## Architecture
The implementation uses:
- **WebView** to host Google Maps
- **HTML/CSS/JavaScript** for map rendering and interaction
- **EvaluateJavaScriptAsync** to trigger JS functions from C#
- **Binding** for MVVM data binding

## Map Interactions
- **Click to Select**: Click any location on the map to make it the current location
- **Accuracy Circle**: Shows approximate selection accuracy based on zoom level

## JavaScript Functions
The embedded HTML provides these JavaScript functions:
- `initMap()` - Initialize the map
- `updateMarker(lat, lng, accuracy)` - Place marker (pin) and accuracy circle
- `changeMapType(mapType)` - Switch between road map and satellite views
- `clearMarker()` - Remove marker and circle

## Performance Considerations
- First load has a network request to Google's CDN
- Subsequent map interactions use cached resources
- JavaScript execution is asynchronous via `EvaluateJavaScriptAsync`

## Troubleshooting
- **Map not appearing**: Verify Google Maps API key is set correctly
- **API error**: Ensure Maps JavaScript API is enabled in Google Cloud Console
- **Click not working**: Check browser console for JavaScript errors (use browser dev tools)
- **Accuracy circle not visible**: Verify CSS fillColor and fillOpacity values in HTML