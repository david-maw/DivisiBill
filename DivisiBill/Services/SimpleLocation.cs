using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Services;

public class SimpleLocation : IEquatable<SimpleLocation>
{
    private double latitude = 0;
    private double longitude = 0;
    private int accuracy = Distances.Inaccurate;

    private static readonly XmlSerializer xmlSerializer = new XmlSerializer(typeof(SimpleLocation));

    public SimpleLocation() { } // for XML deserialization
    public SimpleLocation(Location location)
    {
        if (location is not null)
        {
            this.accuracy = location.AccuracyOrDefault();
            this.latitude = Utilities.Adjusted(location.Latitude, accuracy);
            this.longitude = Utilities.Adjusted(location.Longitude, accuracy);
        }
    }
    public bool Equals(SimpleLocation other)
    {
        return latitude == other.latitude && longitude == other.longitude && accuracy == other.accuracy;
    }

    public static implicit operator Location(SimpleLocation simpleLocation) => new Location(simpleLocation.Latitude, simpleLocation.Longitude) { Accuracy = simpleLocation.Accuracy };

    public string ToXml()
    {
        MemoryStream stream = new MemoryStream();
        ToStream(stream);
        stream.Position = 0;
        StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public bool ToStream(Stream stream)
    {
        try
        {
            using (StreamWriter sw = new(stream, System.Text.Encoding.UTF8, -1, true))
            using (var xmlwriter = XmlWriter.Create(sw, new XmlWriterSettings() { OmitXmlDeclaration = true }))
            {
                var namespaces = new XmlSerializerNamespaces();
                namespaces.Add(string.Empty, string.Empty);
                xmlSerializer.Serialize(xmlwriter, this, namespaces);
            }
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
    public double Latitude { get => latitude; set => latitude = value; }
    [XmlAttribute, DefaultValue(0.0)]
    public double Longitude { get => longitude; set => longitude = value; }
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
}
