namespace ReaderDetect;

/// <summary>What the LLRP port on a host told us.</summary>
public enum LlrpStatus
{
  /// <summary>The LLRP port was not probed (or the probe could not run).</summary>
  Unknown,

  /// <summary>Something accepted the TCP connection but did not speak LLRP.</summary>
  NoLlrp,

  /// <summary>The reader accepted our connection; nobody else is connected.</summary>
  Free,

  /// <summary>A reader answered, but another client already holds the LLRP connection (typically the timing bridge).</summary>
  InUse,
}
