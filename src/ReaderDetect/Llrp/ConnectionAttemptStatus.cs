namespace ReaderDetect.Llrp;

/// <summary>Status field of the ConnectionAttemptEvent a reader sends right after a client connects.</summary>
public enum ConnectionAttemptStatus : ushort
{
  /// <summary>We own the LLRP connection now.</summary>
  Success = 0,

  /// <summary>The reader is configured to connect out to a client itself and already did.</summary>
  FailedReaderInitiatedConnectionExists = 1,

  /// <summary>Another client (typically the timing bridge) is connected.</summary>
  FailedClientInitiatedConnectionExists = 2,

  /// <summary>Some other reason.</summary>
  FailedOther = 3,

  /// <summary>Sent to the existing client when someone else tried to connect.</summary>
  AnotherConnectionAttempted = 4,
}
