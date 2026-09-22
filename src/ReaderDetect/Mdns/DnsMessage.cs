namespace ReaderDetect.Mdns;

/// <summary>A parsed mDNS packet: questions are skipped, all resource records kept in order.</summary>
/// <param name="Id">Transaction id (0 in mDNS).</param>
/// <param name="IsResponse">QR bit.</param>
/// <param name="Records">Answer, authority and additional records, in that order.</param>
public sealed record DnsMessage(ushort Id, bool IsResponse, IReadOnlyList<DnsRecord> Records);
