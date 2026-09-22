namespace ReaderDetect.Llrp;

/// <summary>TLV parameter type numbers used by the identify handshake.</summary>
public static class LlrpParameterType
{
  /// <summary>UTCTimestamp inside ReaderEventNotificationData.</summary>
  public const ushort UtcTimestamp = 128;

  /// <summary>Uptime inside ReaderEventNotificationData (readers without a clock).</summary>
  public const ushort Uptime = 129;

  /// <summary>GeneralDeviceCapabilities: vendor, model, firmware.</summary>
  public const ushort GeneralDeviceCapabilities = 137;

  /// <summary>Container in READER_EVENT_NOTIFICATION.</summary>
  public const ushort ReaderEventNotificationData = 246;

  /// <summary>Result of the connection attempt, sent by the reader on connect.</summary>
  public const ushort ConnectionAttemptEvent = 256;

  /// <summary>LLRPStatus in every response.</summary>
  public const ushort LlrpStatus = 287;
}
