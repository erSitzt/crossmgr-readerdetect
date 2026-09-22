using System.Net;

namespace ReaderDetect.Mdns;

/// <summary>A resource record from an mDNS packet.</summary>
/// <param name="Name">Owner name, without trailing dot.</param>
/// <param name="Type">Record type number.</param>
/// <param name="Ttl">Time to live in seconds.</param>
public abstract record DnsRecord(string Name, ushort Type, uint Ttl);

/// <summary>PTR: <paramref name="Target"/> is an instance of service <paramref name="Name"/>.</summary>
public sealed record PtrRecord(string Name, uint Ttl, string Target) : DnsRecord(Name, (ushort)DnsRecordType.Ptr, Ttl);

/// <summary>SRV: the service instance <paramref name="Name"/> runs on <paramref name="Target"/>:<paramref name="Port"/>.</summary>
public sealed record SrvRecord(string Name, uint Ttl, ushort Priority, ushort Weight, ushort Port, string Target)
  : DnsRecord(Name, (ushort)DnsRecordType.Srv, Ttl);

/// <summary>A: IPv4 address of <paramref name="Name"/>.</summary>
public sealed record ARecord(string Name, uint Ttl, IPAddress Address) : DnsRecord(Name, (ushort)DnsRecordType.A, Ttl);

/// <summary>AAAA: IPv6 address of <paramref name="Name"/>.</summary>
public sealed record AaaaRecord(string Name, uint Ttl, IPAddress Address) : DnsRecord(Name, (ushort)DnsRecordType.Aaaa, Ttl);

/// <summary>TXT: the strings, and the <c>key=value</c> pairs they hold.</summary>
public sealed record TxtRecord(string Name, uint Ttl, IReadOnlyList<string> Strings) : DnsRecord(Name, (ushort)DnsRecordType.Txt, Ttl)
{
  /// <summary>Strings of the form <c>key=value</c> as a dictionary (keys lower-cased; a bare key maps to empty).</summary>
  public IReadOnlyDictionary<string, string> Pairs
  {
    get
    {
      var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var text in Strings)
      {
        if (text.Length == 0) continue;
        var eq = text.IndexOf('=');
        var key = eq < 0 ? text : text[..eq];
        if (key.Length > 0 && !pairs.ContainsKey(key)) pairs[key] = eq < 0 ? string.Empty : text[(eq + 1)..];
      }

      return pairs;
    }
  }
}

/// <summary>Any other record type, kept only so the packet accounting stays right.</summary>
public sealed record UnknownRecord(string Name, ushort Type, uint Ttl, int DataLength) : DnsRecord(Name, Type, Ttl);
