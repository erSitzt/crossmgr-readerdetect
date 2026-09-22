using System.Buffers.Binary;
using System.Text;

namespace ReaderDetect.Llrp;

/// <summary>
/// Just enough of the LLRP wire format to identify a reader: build three
/// client messages and pick the vendor/model/firmware and connection status
/// out of two reader messages. No IO in here, so every byte can be tested
/// against captures from a real reader.
/// </summary>
public static class LlrpCodec
{
  /// <summary>Size of the message header.</summary>
  public const int HeaderLength = 10;

  /// <summary>Sanity cap on message length; a reader never sends more than a few KB to us.</summary>
  public const uint MaxMessageLength = 1024 * 1024;

  private const ushort Version = 1;

  /// <summary>Builds a message: header (version 1, type, total length, id) followed by the payload.</summary>
  public static byte[] Encode(LlrpMessageType type, uint messageId, ReadOnlySpan<byte> payload = default)
  {
    var buffer = new byte[HeaderLength + payload.Length];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)((Version << 10) | (ushort)type));
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(2), (uint)buffer.Length);
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(6), messageId);
    payload.CopyTo(buffer.AsSpan(HeaderLength));
    return buffer;
  }

  /// <summary>GET_READER_CAPABILITIES. RequestedData 1 = general device capabilities only, which is all we read.</summary>
  public static byte[] GetReaderCapabilities(uint messageId, byte requestedData = 1) =>
    Encode(LlrpMessageType.GetReaderCapabilities, messageId, [requestedData]);

  /// <summary>CLOSE_CONNECTION, so the reader frees the slot for the real client immediately.</summary>
  public static byte[] CloseConnection(uint messageId) => Encode(LlrpMessageType.CloseConnection, messageId);

  /// <summary>KEEPALIVE_ACK.</summary>
  public static byte[] KeepaliveAck(uint messageId) => Encode(LlrpMessageType.KeepaliveAck, messageId);

  /// <summary>
  /// Parses a header. Returns false for fewer than 10 bytes, a protocol version
  /// other than 1, reserved bits set, or an implausible length; any of those
  /// means the thing on the other end is not an LLRP reader.
  /// </summary>
  public static bool TryParseHeader(ReadOnlySpan<byte> bytes, out LlrpHeader header)
  {
    header = default;
    if (bytes.Length < HeaderLength) return false;
    var word = BinaryPrimitives.ReadUInt16BigEndian(bytes);
    var reserved = word >> 13;
    var version = (byte)((word >> 10) & 0x7);
    var type = (ushort)(word & 0x3FF);
    var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[2..]);
    var id = BinaryPrimitives.ReadUInt32BigEndian(bytes[6..]);
    if (reserved != 0 || version != Version || length < HeaderLength || length > MaxMessageLength) return false;
    header = new LlrpHeader(version, (LlrpMessageType)type, length, id);
    return true;
  }

  /// <summary>
  /// Splits a byte range into its top-level TLV parameters. A TV parameter
  /// (high bit set) ends the walk: none occur at the levels we look at, and
  /// decoding them needs a per-type length table we do not carry.
  /// </summary>
  public static List<LlrpParameter> ParseParameters(ReadOnlyMemory<byte> data)
  {
    var result = new List<LlrpParameter>();
    var span = data.Span;
    var offset = 0;
    while (offset + 4 <= span.Length)
    {
      if ((span[offset] & 0x80) != 0) break;
      var type = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(span[offset..]) & 0x3FF);
      var length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 2)..]);
      if (length < 4 || offset + length > span.Length) break;
      result.Add(new LlrpParameter(type, data.Slice(offset + 4, length - 4)));
      offset += length;
    }

    return result;
  }

  /// <summary>
  /// The ConnectionAttemptEvent status inside a READER_EVENT_NOTIFICATION payload,
  /// or null when this notification is about something else.
  /// </summary>
  public static ConnectionAttemptStatus? ParseConnectionAttempt(ReadOnlyMemory<byte> readerEventPayload)
  {
    foreach (var container in ParseParameters(readerEventPayload))
    {
      if (container.Type != LlrpParameterType.ReaderEventNotificationData) continue;
      foreach (var inner in ParseParameters(container.Value))
      {
        if (inner.Type == LlrpParameterType.ConnectionAttemptEvent && inner.Value.Length >= 2)
        {
          return (ConnectionAttemptStatus)BinaryPrimitives.ReadUInt16BigEndian(inner.Value.Span);
        }
      }
    }

    return null;
  }

  /// <summary>The UTC timestamp (microseconds) inside a READER_EVENT_NOTIFICATION payload, if present.</summary>
  public static ulong? ParseUtcTimestamp(ReadOnlyMemory<byte> readerEventPayload)
  {
    foreach (var container in ParseParameters(readerEventPayload))
    {
      if (container.Type != LlrpParameterType.ReaderEventNotificationData) continue;
      foreach (var inner in ParseParameters(container.Value))
      {
        if (inner.Type == LlrpParameterType.UtcTimestamp && inner.Value.Length >= 8)
        {
          return BinaryPrimitives.ReadUInt64BigEndian(inner.Value.Span);
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Reads LLRPStatus and GeneralDeviceCapabilities out of a
  /// GET_READER_CAPABILITIES_RESPONSE payload. Returns false with an error
  /// text when the status is not success or the capabilities are missing/truncated.
  /// </summary>
  public static bool TryParseCapabilities(
    ReadOnlyMemory<byte> payload,
    out GeneralDeviceCapabilities? capabilities,
    out ushort llrpStatusCode,
    out string? error)
  {
    capabilities = null;
    llrpStatusCode = 0;
    error = null;

    LlrpParameter? general = null;
    foreach (var parameter in ParseParameters(payload))
    {
      switch (parameter.Type)
      {
        case LlrpParameterType.LlrpStatus when parameter.Value.Length >= 4:
          llrpStatusCode = BinaryPrimitives.ReadUInt16BigEndian(parameter.Value.Span);
          var descriptionLength = BinaryPrimitives.ReadUInt16BigEndian(parameter.Value.Span[2..]);
          if (llrpStatusCode != 0)
          {
            var description = descriptionLength > 0 && parameter.Value.Length >= 4 + descriptionLength
              ? Encoding.UTF8.GetString(parameter.Value.Span.Slice(4, descriptionLength))
              : string.Empty;
            error = $"LLRPStatus {llrpStatusCode}: {description}".TrimEnd(':', ' ');
          }

          break;
        case LlrpParameterType.GeneralDeviceCapabilities:
          general = parameter;
          break;
      }
    }

    if (error is not null) return false;
    if (general is null)
    {
      error = "no GeneralDeviceCapabilities in response";
      return false;
    }

    var value = general.Value.Value.Span;
    if (value.Length < 14)
    {
      error = "GeneralDeviceCapabilities truncated";
      return false;
    }

    var firmwareLength = BinaryPrimitives.ReadUInt16BigEndian(value[12..]);
    if (value.Length < 14 + firmwareLength)
    {
      error = "GeneralDeviceCapabilities firmware string truncated";
      return false;
    }

    var flags = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
    capabilities = new GeneralDeviceCapabilities(
      MaxAntennas: BinaryPrimitives.ReadUInt16BigEndian(value),
      CanSetAntennaProperties: (flags & 0x8000) != 0,
      HasUtcClock: (flags & 0x4000) != 0,
      ManufacturerPen: BinaryPrimitives.ReadUInt32BigEndian(value[4..]),
      ModelCode: BinaryPrimitives.ReadUInt32BigEndian(value[8..]),
      FirmwareVersion: Encoding.UTF8.GetString(value.Slice(14, firmwareLength)));
    return true;
  }
}
