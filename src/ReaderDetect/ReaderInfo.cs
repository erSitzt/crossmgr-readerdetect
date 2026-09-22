using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Network;

namespace ReaderDetect;

/// <summary>Everything the scan learned about one reader.</summary>
/// <param name="Ip">Its IPv4 address.</param>
/// <param name="Mac">Its MAC, when ARP or an advertisement gave it.</param>
/// <param name="Hostname">Its hostname (mDNS, WS-Discovery or reverse DNS), without <c>.local</c>.</param>
/// <param name="Vendor">Manufacturer.</param>
/// <param name="Model">Model name, or the raw model code when unknown.</param>
/// <param name="Firmware">Firmware version, when the reader reported it.</param>
/// <param name="LlrpStatus">Whether the LLRP port is free, in use, or not LLRP at all.</param>
/// <param name="Sources">Which discovery mechanisms saw it.</param>
/// <param name="Confidence">How sure we are it is a reader.</param>
public sealed record ReaderInfo(
  IPAddress Ip,
  PhysicalAddress? Mac,
  string? Hostname,
  ReaderVendor Vendor,
  string? Model,
  string? Firmware,
  LlrpStatus LlrpStatus,
  DiscoverySources Sources,
  Confidence Confidence)
{
  /// <summary>IANA private enterprise number from the LLRP capabilities or the mDNS TXT record.</summary>
  public uint? ManufacturerPen { get; init; }

  /// <summary>Vendor model code from the LLRP capabilities.</summary>
  public uint? ModelCode { get; init; }

  /// <summary>The factory hostname this MAC would have; a hint for the user when the real hostname is unknown.</summary>
  public string? ExpectedHostname { get; init; }

  /// <summary>The local interface the reader was found through.</summary>
  public NetworkInterfaceInfo? Interface { get; init; }

  /// <summary>Human-readable remark, e.g. that another client holds the LLRP connection.</summary>
  public string? Note { get; init; }

  /// <summary>The reader's web interface.</summary>
  public string WebUrl => $"http://{Ip}/";

  /// <summary>MAC as <c>00:16:25:12:59:43</c>, or empty.</summary>
  public string MacText => Mac is null ? string.Empty : MacFormat.Colon(Mac);
}
