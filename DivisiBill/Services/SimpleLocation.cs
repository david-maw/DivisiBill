using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Services;

public class SimpleLocation : IEquatable<SimpleLocation>
{
    private int accuracy = Distances.Inaccurate;

    private static readonly XmlSerializer xmlSerializer = new(typeof(SimpleLocation));

    public SimpleLocation() { } // for XML deserialization
    public SimpleLocation(Location location)
    {
        if (location is not null)
        {
            this.accuracy = location.AccuracyOrDefault();
            this.Latitude = Utilities.Adjusted(location.Latitude, accuracy);
            this.Longitude = Utilities.Adjusted(location.Longitude, accuracy);
        }
    }
    public bool Equals(SimpleLocation other) => Latitude == other.Latitude && Longitude == other.Longitude && accuracy == other.accuracy;

    public static implicit operator Location(SimpleLocation simpleLocation) => new(simpleLocation.Latitude, simpleLocation.Longitude) { Accuracy = simpleLocation.Accuracy };

    public string ToXml()
    {
        MemoryStream stream = new();
        ToStream(stream);
        stream.Position = 0;
        StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public bool ToStream(Stream stream)
    {
        try
        {
            using StreamWriter sw = new(stream, System.Text.Encoding.UTF8, -1, true);
            using var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { OmitXmlDeclaration = true });
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            xmlSerializer.Serialize(xmlWriter, this, namespaces);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    public static SimpleLocation FromStream(Stream stream)
    {
        try
        {
            return (SimpleLocation)xmlSerializer.Deserialize(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }


    [XmlAttribute, DefaultValue(0.0)]
    public double Latitude { get; set; } = 0;
    [XmlAttribute, DefaultValue(0.0)]
    public double Longitude { get; set; } = 0;
    [XmlAttribute, DefaultValue(Distances.Inaccurate)]
    public int Accuracy
    {
        get => accuracy; set
        {
            accuracy = value == 0 ? Distances.Inaccurate : value;
            isLocationValid = accuracy < Distances.AccuracyLimit;
        }
    }
    private bool isLocationValid;
    [XmlIgnore]
    public bool IsLocationValid
    {
        get => Accuracy <= Distances.AccuracyLimit;
        set
        {
            if (value != IsLocationValid)
            {
                if (!value)
                {
                    // Reset these because they are persisted
                    Latitude = 0.0;
                    Longitude = 0.0;
                    Accuracy = Distances.Inaccurate;
                }
            }
        }
    }
    public override bool Equals(object obj) => obj is SimpleLocation simpleLocation && Equals(simpleLocation);

    public override int GetHashCode() => base.GetHashCode();
}
