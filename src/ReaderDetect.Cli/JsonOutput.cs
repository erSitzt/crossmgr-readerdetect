using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReaderDetect.Network;

namespace ReaderDetect.Cli;

/// <summary>JSON output for scripts: camelCase, enums as names, addresses as strings.</summary>
internal static class JsonOutput
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(), new IpAddressConverter(), new MacConverter(), new SubnetConverter() },
  };

  public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

  private sealed class IpAddressConverter : JsonConverter<IPAddress>
  {
    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.GetString() is { } s ? IPAddress.Parse(s) : null;

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value.ToString());
  }

  private sealed class MacConverter : JsonConverter<PhysicalAddress>
  {
    public override PhysicalAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.GetString() is { } s ? MacFormat.Parse(s) : null;

    public override void Write(Utf8JsonWriter writer, PhysicalAddress value, JsonSerializerOptions options) =>
      writer.WriteStringValue(MacFormat.Colon(value));
  }

  private sealed class SubnetConverter : JsonConverter<IpSubnet>
  {
    public override IpSubnet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      IpSubnet.Parse(reader.GetString() ?? throw new JsonException());

    public override void Write(Utf8JsonWriter writer, IpSubnet value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value.ToString());
  }
}
