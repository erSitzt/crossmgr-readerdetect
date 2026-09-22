using ReaderDetect.Network;

namespace ReaderDetect;

/// <summary>What a scan found.</summary>
/// <param name="Readers">Readers, ordered by interface then address.</param>
/// <param name="Interfaces">Interfaces that were scanned.</param>
/// <param name="Warnings">Things the user should know (capped subnets, mDNS listener refused, …).</param>
/// <param name="Elapsed">Wall time.</param>
public sealed record ScanResult(
  IReadOnlyList<ReaderInfo> Readers,
  IReadOnlyList<NetworkInterfaceInfo> Interfaces,
  IReadOnlyList<string> Warnings,
  TimeSpan Elapsed);
