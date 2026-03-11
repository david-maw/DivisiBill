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
        string url = "https://places.googleapis.com/v1/places:searchNearby";
        httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", apiKey);
        httpClient.DefaultRequestHeaders.Add("X-Goog-FieldMask", "places.displayName,places.location");

        var request = new
        {
            includedTypes = new[] { "restaurant" },   // or any place type(s)
            maxResultCount = maxResults,
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude = lat, longitude = lon },
                    radius = 200.0                    // search radius in meters
                }
            }
        };

        try
        {
            HttpResponseMessage placesResponse = await CallWs.CallUncertainWebServiceAsync((CancellationTokenSource cts) => httpClient.PostAsJsonAsync(url, request, cts.Token));
            // Need a new HttpClient for the next call, otherwise we get a "Bad Request" error on the next call to PostAsJsonAsync.
            httpClient.Dispose();
            httpClient = new HttpClient();


            if (placesResponse is not null && placesResponse.IsSuccessStatusCode)
            {
                var json = await placesResponse.Content.ReadAsStringAsync();
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

                    return data.Places.Where(r => !string.IsNullOrEmpty(r.DisplayName?.Text))!;
                }
            }
            else if (placesResponse is null)
                Utilities.DebugMsg("GetNearestRestaurantsAsync failed, no task returned");
            else
                Utilities.DebugMsg("GetNearestRestaurantsAsync failed, status code = " + placesResponse.StatusCode);
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
