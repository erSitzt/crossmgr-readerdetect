using System.Net;
using ReaderDetect.Network;

namespace ReaderDetect.Mdns;

/// <summary>One advertised service instance, assembled from PTR/SRV/A/TXT records.</summary>
/// <param name="InstanceName">The instance label, e.g. <c>SpeedwayR-12-59-43</c>.</param>
/// <param name="ServiceType">The service type, e.g. <c>_llrp._tcp.local</c>.</param>
/// <param name="Host">Target host from SRV, e.g. <c>SpeedwayR-12-59-43.local</c>.</param>
/// <param name="Port">Port from SRV, 0 when there was no SRV.</param>
/// <param name="Addresses">IPv4 addresses of the host; falls back to the responder's address.</param>
/// <param name="Txt">TXT key/value pairs.</param>
/// <param name="Responder">Source address of the packet that carried the PTR.</param>
/// <param name="Interface">Local interface the packet arrived on, when known.</param>
public sealed record MdnsService(
  string InstanceName,
  string ServiceType,
  string? Host,
  ushort Port,
  IReadOnlyList<IPAddress> Addresses,
  IReadOnlyDictionary<string, string> Txt,
  IPAddress Responder,
  NetworkInterfaceInfo? Interface);
