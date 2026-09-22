using System.Net;

namespace ReaderDetect.Management;

/// <summary>What happened when settings were applied.</summary>
/// <param name="Applied">The reader accepted the change.</param>
/// <param name="ExpectedAddress">Where the reader should now be, or null when it will take a DHCP lease.</param>
/// <param name="ConnectionDropped">The management connection died as the change took effect; normal for an IP change.</param>
/// <param name="RebootRequested">A reboot was triggered for the change to take effect.</param>
/// <param name="Log">Commands sent and answers received, passwords excluded.</param>
public sealed record ApplyResult(
  bool Applied,
  IPAddress? ExpectedAddress,
  bool ConnectionDropped,
  bool RebootRequested,
  IReadOnlyList<string> Log);
