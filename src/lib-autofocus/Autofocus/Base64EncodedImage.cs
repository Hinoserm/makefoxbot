using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autofocus;

public class Base64EncodedImage
{
    internal readonly byte[] _data;

    public ReadOnlyMemory<byte> Data => _data;

    public Base64EncodedImage(byte[] data)
    {
        _data = data;
    }

    public string Base64()
    {
        return Convert.ToBase64String(_data);
    }
}

internal class Base64EncodedImageConverter
    : JsonConverter<Base64EncodedImage>
{
    public override Base64EncodedImage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new Base64EncodedImage(reader.GetBytesFromBase64());
    }

    public override void Write(Utf8JsonWriter writer, Base64EncodedImage value, JsonSerializerOptions options)
    {
        writer.WriteBase64StringValue(value.Data.Span);
    }
}