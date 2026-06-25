namespace DivisiBill.Services;
/// <summary>
/// Used to pass values to and return results from a <see cref="Views.MapPage"/> 
/// </summary>
public class MapSettings(string venueName, Location? venueLocation)
{
    /// <summary>
    /// Gets the name of the venue to be displayed, which may only be set at object creation
    /// </summary>
    public string VenueName { get; } = venueName;
    /// <summary>
    /// The <see cref="Location"/> of the venue identified by <see cref="VenueName"/>, or null if we do not know one
    /// </summary>
    public Location? VenueLocation { get; set; } = venueLocation;
    /// <summary>
    /// Indicates whether the <see cref="VenueLocation"/> has been changed, reset once the change is acknowledged
    /// </summary>
    public bool VenueLocationHasChanged { get; set; } = false;
}
