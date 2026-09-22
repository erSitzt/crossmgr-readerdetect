namespace ReaderDetect;

/// <summary>Which discovery mechanisms contributed evidence for a host.</summary>
[Flags]
public enum DiscoverySources
{
  /// <summary>No source.</summary>
  None = 0,

  /// <summary>An mDNS (Bonjour) <c>_llrp._tcp</c> advertisement.</summary>
  Mdns = 1,

  /// <summary>A WS-Discovery ProbeMatch.</summary>
  WsDiscovery = 2,

  /// <summary>The TCP sweep found the LLRP port open.</summary>
  PortSweep = 4,

  /// <summary>The MAC address was resolved through ARP.</summary>
  Arp = 8,

  /// <summary>A reverse DNS name was found.</summary>
  ReverseDns = 16,

  /// <summary>The web interface answered with a recognisable banner.</summary>
  Http = 32,

  /// <summary>The address was given by the user (probe command).</summary>
  Manual = 64,
}
