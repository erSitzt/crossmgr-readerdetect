using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Llrp;
using ReaderDetect.Network;

namespace ReaderDetect.Scanning;

/// <summary>An immutable snapshot of everything collected about one host, the classifier's only input.</summary>
internal sealed record CandidateEvidence(
  IPAddress Ip,
  DiscoverySources Sources,
  LlrpProbeResult? Llrp,
  PhysicalAddress? Mac,
  string? ReverseName,
  HttpFingerprint? Http,
  string? MdnsHost,
  IReadOnlyDictionary<string, string>? MdnsTxt,
  string? WsdHost,
  string? ConfirmedExpectedHostname,
  NetworkInterfaceInfo? Interface);
