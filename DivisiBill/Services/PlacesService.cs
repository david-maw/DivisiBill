#nullable enable

using System.Text.Json;

namespace DivisiBill.Services;

public class PlacesService
{
    private readonly HttpClient httpClient;

    public PlacesService()
    {
        httpClient = new HttpClient();
    }

    static readonly JsonSerializerOptions caseInsensitive = new() { PropertyNameCaseInsensitive = true };
    public async Task<IEnumerable<string>?> GetNearestRestaurantsAsync(double lat, double lon, string apiKey)
    {
        string url =
            $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
            $"?location={lat},{lon}" +
            $"&radius=50" + // meters
            $"&type=restaurant" +
            $"&key={apiKey}";

        var json = await httpClient.GetStringAsync(url);

        var data = JsonSerializer.Deserialize<PlacesResponse>(json, caseInsensitive);

        return data?.Results?.Where(r => !string.IsNullOrEmpty(r.Name))?.Select(r => r.Name)!;
    }
}

public class PlacesResponse
{
    public List<PlaceResult>? Results { get; set; }
}

public class PlaceResult
{
    public string? Name { get; set; }
}