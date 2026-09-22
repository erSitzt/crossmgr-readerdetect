namespace ReaderDetect.Mdns;

/// <summary>DNS record types the mDNS browser understands.</summary>
public enum DnsRecordType : ushort
{
  /// <summary>IPv4 address.</summary>
  A = 1,

  /// <summary>Pointer (service type → instance).</summary>
  Ptr = 12,

  /// <summary>Text key/value pairs.</summary>
  Txt = 16,

  /// <summary>IPv6 address.</summary>
  Aaaa = 28,

  /// <summary>Service location (host and port).</summary>
  Srv = 33,
}
