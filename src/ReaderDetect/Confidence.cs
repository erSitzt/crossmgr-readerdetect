namespace ReaderDetect;

/// <summary>How sure we are that a host is an RFID reader.</summary>
public enum Confidence
{
  /// <summary>Only a hint (vendor MAC prefix or hostname pattern); nothing answered as a reader.</summary>
  Possible,

  /// <summary>An LLRP handshake or an LLRP service advertisement was seen, but capabilities could not be read.</summary>
  Likely,

  /// <summary>The reader reported its capabilities over LLRP.</summary>
  Confirmed,
}
