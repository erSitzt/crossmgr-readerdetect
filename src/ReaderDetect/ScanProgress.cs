namespace ReaderDetect;

/// <summary>Stages of a scan, in order.</summary>
public enum ScanPhase
{
  /// <summary>Enumerating local interfaces.</summary>
  Interfaces,

  /// <summary>mDNS and WS-Discovery are listening; the sweep is running.</summary>
  Discovery,

  /// <summary>TCP sweep progress.</summary>
  PortSweep,

  /// <summary>Identifying candidates over LLRP.</summary>
  LlrpProbe,

  /// <summary>ARP, DNS and HTTP enrichment.</summary>
  Enrich,

  /// <summary>Finished.</summary>
  Done,
}

/// <summary>A progress tick. When <paramref name="Reader"/> is set the row for that IP should be inserted or replaced.</summary>
/// <param name="Phase">Current phase.</param>
/// <param name="Done">Work units done in this phase.</param>
/// <param name="Total">Work units in this phase, or 0 when unknown.</param>
/// <param name="Found">Readers found so far.</param>
/// <param name="Message">Short status text.</param>
/// <param name="Reader">A reader whose row changed, if any.</param>
public readonly record struct ScanProgress(ScanPhase Phase, int Done, int Total, int Found, string? Message, ReaderInfo? Reader);
