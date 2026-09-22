using ReaderDetect.Network;

namespace ReaderDetect;

/// <summary>Everything a scan can be told. Defaults are tuned for a /24 LAN at a race track.</summary>
public sealed class ScanOptions
{
  /// <summary>Interfaces to scan; null means every eligible one.</summary>
  public IReadOnlyList<NetworkInterfaceInfo>? Interfaces { get; set; }

  /// <summary>Subnets to sweep instead of the interfaces' own; empty means use the interfaces.</summary>
  public IReadOnlyList<IpSubnet> ExplicitSubnets { get; set; } = [];

  /// <summary>Also scan VPN/VM/container adapters.</summary>
  public bool IncludeVirtualInterfaces { get; set; }

  /// <summary>Listen for mDNS <c>_llrp._tcp</c> advertisements (Impinj).</summary>
  public bool EnableMdns { get; set; } = true;

  /// <summary>Send a WS-Discovery probe (Zebra).</summary>
  public bool EnableWsDiscovery { get; set; } = true;

  /// <summary>TCP-sweep the LLRP port across the subnet.</summary>
  public bool EnablePortSweep { get; set; } = true;

  /// <summary>Resolve MAC addresses.</summary>
  public bool EnableArp { get; set; } = true;

  /// <summary>Look up reverse DNS names.</summary>
  public bool EnableReverseDns { get; set; } = true;

  /// <summary>Fetch the web banner when LLRP could not identify the reader.</summary>
  public bool EnableHttpFingerprint { get; set; } = true;

  /// <summary>LLRP port; readers listen on 5084.</summary>
  public int LlrpPort { get; set; } = 5084;

  /// <summary>Sweep at most this many hosts per subnet (a /22); larger subnets are capped around the interface address.</summary>
  public int MaxHostsPerSubnet { get; set; } = 1024;

  /// <summary>Concurrent connect attempts in the sweep.</summary>
  public int SweepParallelism { get; set; } = 256;

  /// <summary>Concurrent LLRP identify probes.</summary>
  public int ProbeParallelism { get; set; } = 16;

  /// <summary>Per-host connect timeout in the sweep.</summary>
  public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(600);

  /// <summary>How long to collect mDNS answers.</summary>
  public TimeSpan MdnsDuration { get; set; } = TimeSpan.FromSeconds(2);

  /// <summary>How long to collect WS-Discovery answers.</summary>
  public TimeSpan WsDiscoveryDuration { get; set; } = TimeSpan.FromSeconds(2);

  /// <summary>Whole-handshake timeout for one LLRP probe.</summary>
  public TimeSpan LlrpProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);

  /// <summary>Reverse DNS timeout.</summary>
  public TimeSpan ReverseDnsTimeout { get; set; } = TimeSpan.FromSeconds(1);

  /// <summary>HTTP banner timeout.</summary>
  public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(1);

  /// <summary>Verbose diagnostics sink.</summary>
  public Action<string>? Log { get; set; }
}
