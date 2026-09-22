using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace ReaderDetect.Mdns;

/// <summary>
/// Builds one-question mDNS queries and parses responses, including name
/// compression. Hand-written because the whole need is one PTR query and four
/// record types; a DNS library would be more code than this and would bring
/// the Windows port-5353 problems with it.
/// </summary>
public static class DnsCodec
{
  private const int HeaderLength = 12;
  private const int MaxPointerHops = 64;

  /// <summary>
  /// A query for <paramref name="name"/>. With <paramref name="unicastResponse"/>
  /// the QU bit asks responders to reply unicast to our port, which is how a
  /// query from an ephemeral port gets its answer regardless of who owns 5353.
  /// </summary>
  public static byte[] BuildQuery(string name, DnsRecordType type, bool unicastResponse, ushort id = 0)
  {
    var encoded = EncodeName(name);
    var packet = new byte[HeaderLength + encoded.Length + 4];
    BinaryPrimitives.WriteUInt16BigEndian(packet, id);
    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 1);
    encoded.CopyTo(packet, HeaderLength);
    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(HeaderLength + encoded.Length), (ushort)type);
    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(HeaderLength + encoded.Length + 2), (ushort)(unicastResponse ? 0x8001 : 0x0001));
    return packet;
  }

  /// <summary>Encodes a dotted name as length-prefixed labels with a terminating zero.</summary>
  public static byte[] EncodeName(string name)
  {
    var labels = name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
    var bytes = new List<byte>();
    foreach (var label in labels)
    {
      var utf8 = Encoding.UTF8.GetBytes(label);
      if (utf8.Length > 63) throw new ArgumentException($"label '{label}' is longer than 63 bytes", nameof(name));
      bytes.Add((byte)utf8.Length);
      bytes.AddRange(utf8);
    }

    bytes.Add(0);
    return [.. bytes];
  }

  /// <summary>Parses a packet; false when it is malformed (bad pointer, truncated record, …).</summary>
  public static bool TryParse(ReadOnlySpan<byte> packet, out DnsMessage message)
  {
    message = null!;
    if (packet.Length < HeaderLength) return false;
    var id = BinaryPrimitives.ReadUInt16BigEndian(packet);
    var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
    var questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
    var recordCount = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) +
                      BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) +
                      BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
    var offset = HeaderLength;

    for (var i = 0; i < questions; i++)
    {
      if (!TryReadName(packet, ref offset, out _)) return false;
      offset += 4;
      if (offset > packet.Length) return false;
    }

    var records = new List<DnsRecord>(recordCount);
    for (var i = 0; i < recordCount; i++)
    {
      if (!TryReadName(packet, ref offset, out var name)) return false;
      if (offset + 10 > packet.Length) return false;
      var type = BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);
      var ttl = BinaryPrimitives.ReadUInt32BigEndian(packet[(offset + 4)..]);
      var dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 8)..]);
      offset += 10;
      if (offset + dataLength > packet.Length) return false;
      var data = packet.Slice(offset, dataLength);
      var dataOffset = offset;
      offset += dataLength;

      switch ((DnsRecordType)type)
      {
        case DnsRecordType.Ptr:
        {
          var o = dataOffset;
          if (!TryReadName(packet, ref o, out var target)) return false;
          records.Add(new PtrRecord(name, ttl, target));
          break;
        }

        case DnsRecordType.Srv:
        {
          if (dataLength < 7) return false;
          var o = dataOffset + 6;
          if (!TryReadName(packet, ref o, out var target)) return false;
          records.Add(new SrvRecord(name, ttl,
            BinaryPrimitives.ReadUInt16BigEndian(data),
            BinaryPrimitives.ReadUInt16BigEndian(data[2..]),
            BinaryPrimitives.ReadUInt16BigEndian(data[4..]),
            target));
          break;
        }

        case DnsRecordType.A when dataLength == 4:
          records.Add(new ARecord(name, ttl, new IPAddress(data)));
          break;

        case DnsRecordType.Aaaa when dataLength == 16:
          records.Add(new AaaaRecord(name, ttl, new IPAddress(data)));
          break;

        case DnsRecordType.Txt:
        {
          var strings = new List<string>();
          var o = 0;
          while (o < data.Length)
          {
            var length = data[o++];
            if (o + length > data.Length) return false;
            strings.Add(Encoding.UTF8.GetString(data.Slice(o, length)));
            o += length;
          }

          records.Add(new TxtRecord(name, ttl, strings));
          break;
        }

        default:
          records.Add(new UnknownRecord(name, type, ttl, dataLength));
          break;
      }
    }

    message = new DnsMessage(id, (flags & 0x8000) != 0, records);
    return true;
  }

  /// <summary>
  /// Reads a possibly-compressed name at <paramref name="offset"/>, advancing it
  /// past the name as it appears in the packet (two bytes for a pointer).
  /// Pointers must point backwards, which rules out loops.
  /// </summary>
  public static bool TryReadName(ReadOnlySpan<byte> packet, ref int offset, out string name)
  {
    name = string.Empty;
    var labels = new List<string>();
    var position = offset;
    var end = -1;
    var hops = 0;
    while (true)
    {
      if (position >= packet.Length) return false;
      var length = packet[position];
      if (length == 0)
      {
        position++;
        break;
      }

      if ((length & 0xC0) == 0xC0)
      {
        if (position + 1 >= packet.Length) return false;
        var pointer = ((length & 0x3F) << 8) | packet[position + 1];
        if (pointer >= position || ++hops > MaxPointerHops) return false;
        if (end < 0) end = position + 2;
        position = pointer;
        continue;
      }

      if ((length & 0xC0) != 0) return false;
      position++;
      if (position + length > packet.Length) return false;
      labels.Add(Encoding.UTF8.GetString(packet.Slice(position, length)));
      position += length;
    }

    offset = end < 0 ? position : end;
    name = string.Join('.', labels);
    return true;
  }
}
