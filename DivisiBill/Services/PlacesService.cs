#nullable enable

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DivisiBill.Services;

public class PlacesService
{
    private HttpClient httpClient;

    public PlacesService()
    {
        httpClient = new HttpClient();
    }

    public async Task<IEnumerable<PlaceResult>> GetNearestRestaurantsAsync(double lat, double lon, string apiKey, int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 20); // API has limits on max results, adjust as needed
        Location venueLocation = new(lat, lon) { Accuracy = 1.0 };
        string url = "https://places.googleapis.com/v1/places:searchNearby";
        httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", apiKey);
        httpClient.DefaultRequestHeaders.Add("X-Goog-FieldMask", "places.displayName,places.location");

        var request = new
        {
            includedTypes = new[] { "restaurant" },   // or any place type(s)
            rankPreference = "DISTANCE",
            maxResultCount = maxResults,
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude = lat, longitude = lon },
                    radius = 200.0 // search radius in meters
                }
            }
        };

        try
        {
            HttpResponseMessage response = await CallWs.CallUncertainWebServiceAsync((CancellationTokenSource cts) => httpClient.PostAsJsonAsync(url, request, cts.Token));
            // Need a new HttpClient for the next call, otherwise we get a "Bad Request" error on the next call to PostAsJsonAsync.
            httpClient.Dispose();
            httpClient = new HttpClient();


            if (response is not null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                // Detect the weird failure which just returns an OK result but no data
                if (string.IsNullOrEmpty(json))
                { // This is a failure, return a NotFound status
                    Utilities.DebugMsg("GetNearestRestaurantsAsync returned OK but no data, returning empty list");
                    return [];
                }
                else
                {
                    var data = JsonSerializer.Deserialize<PlacesResult>(json);
                    
                    if (data is null)
                    {
                        Utilities.RecordMsg("No data returned fetching places");
                        return [];
                    }
                    if (data.Places is null)
                    {
                        Utilities.RecordMsg($"No results returned fetching places");
                        return [];
                    }

                    var placeList = data.Places.Where(r => !string.IsNullOrEmpty(r.Name));
                    foreach (var place in placeList)
                    {
                        if (place.GoogleLocation is not null)
                            place.Distance = venueLocation.GetDistanceTo(new Location(place.GoogleLocation.Latitude, place.GoogleLocation.Longitude) { Accuracy = 1.0 });
                    }
                    return placeList;
                }
            }
            else if (response is null)
                Utilities.DebugMsg("GetNearestRestaurantsAsync failed, no task returned");
            else
                Utilities.DebugMsg("GetNearestRestaurantsAsync failed, status code = " + response.StatusCode);
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("GetNearestRestaurantsAsync failed, exception = " + ex);
        }
        return [];
    }
}

public class PlacesResult
{
    [JsonPropertyName("places")]
    public List<PlaceResult>? Places { get; set; }
}

public class PlaceResult
{
    [JsonPropertyName("location")]
    public GoogleLocation? GoogleLocation { get; set; }

    [JsonPropertyName("displayName")]
    public DisplayName? DisplayName { get; set; }

    public string? Name => DisplayName?.Text;

    /// <summary>
    /// Distance in meters, calculated on the client side based on the location of the place and the user's current location
    /// </summary>
    public int Distance { get; set; } = Distances.Inaccurate; 
 
}

public class GoogleLocation
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}

public class DisplayName
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }
}
