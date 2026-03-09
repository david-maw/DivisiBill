#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DivisiBill.Services;

public class PlacesService
{
    private readonly HttpClient httpClient;

    public PlacesService()
    {
        httpClient = new HttpClient();
    }

    public async Task<IEnumerable<PlaceResult>> GetNearestRestaurantsAsync(double lat, double lon, string apiKey)
    {
        string url =
            $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
            $"?location={lat},{lon}" +
            $"&rankby=distance" +
            $"&type=restaurant" +
            $"&key={apiKey}";

        var json = await httpClient.GetStringAsync(url);

        var data = JsonSerializer.Deserialize<PlacesResponse>(json);

        if (data is null || data?.Status != "OK")
        {
            Utilities.RecordMsg($"Error fetching places: {data?.Status}");
            return [];
        }
        if (data.Results is null)
        {
            Utilities.RecordMsg($"No results returned fetching places");
            return [];
        }

        return data.Results.Where(r => !string.IsNullOrEmpty(r.Name))!;
    }
}

public class PlacesResponse
{
    [JsonPropertyName("html_attributions")]
    public List<string>? HtmlAttributions { get; set; }

    [JsonPropertyName("results")]
    public List<PlaceResult>? Results { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class PlaceResult
{
    [JsonPropertyName("business_status")]
    public string? BusinessStatus { get; set; }

    [JsonPropertyName("geometry")]
    public Geometry? Geometry { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icon_background_color")]
    public string? IconBackgroundColor { get; set; }

    [JsonPropertyName("icon_mask_base_uri")]
    public string? IconMaskBaseUri { get; set; }

    [JsonPropertyName("international_phone_number")]
    public string? InternationalPhoneNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("opening_hours")]
    public OpeningHours? OpeningHours { get; set; }

    [JsonPropertyName("photos")]
    public List<Photo>? Photos { get; set; }

    [JsonPropertyName("place_id")]
    public string? PlaceId { get; set; }

    [JsonPropertyName("plus_code")]
    public PlusCode? PlusCode { get; set; }

    [JsonPropertyName("price_level")]
    public int? PriceLevel { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("types")]
    public List<string>? Types { get; set; }

    [JsonPropertyName("user_ratings_total")]
    public int? UserRatingsTotal { get; set; }

    [JsonPropertyName("vicinity")]
    public string? Vicinity { get; set; }
}
public class Geometry
{
    [JsonPropertyName("location")]
    public LatLng? Location { get; set; }

    [JsonPropertyName("viewport")]
    public Viewport? Viewport { get; set; }
}

public class LatLng
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Long { get; set; }
}

public class Viewport
{
    [JsonPropertyName("northeast")]
    public LatLng? Northeast { get; set; }

    [JsonPropertyName("southwest")]
    public LatLng? Southwest { get; set; }
}

public class OpeningHours
{
    [JsonPropertyName("open_now")]
    public bool? OpenNow { get; set; }
}

public class Photo
{
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("html_attributions")]
    public List<string>? HtmlAttributions { get; set; }

    [JsonPropertyName("photo_reference")]
    public string? PhotoReference { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }
}

public class PlusCode
{
    [JsonPropertyName("compound_code")]
    public string? CompoundCode { get; set; }

    [JsonPropertyName("global_code")]
    public string? GlobalCode { get; set; }
}