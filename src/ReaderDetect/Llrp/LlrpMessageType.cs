namespace ReaderDetect.Llrp;

/// <summary>The few LLRP message types the identify handshake needs.</summary>
public enum LlrpMessageType : ushort
{
  /// <summary>Client asks for the reader's capabilities.</summary>
  GetReaderCapabilities = 1,

  /// <summary>Reader acknowledges CLOSE_CONNECTION.</summary>
  CloseConnectionResponse = 4,

  /// <summary>Reader answers GET_READER_CAPABILITIES.</summary>
  GetReaderCapabilitiesResponse = 11,

  /// <summary>Client closes the session cleanly.</summary>
  CloseConnection = 14,

  /// <summary>Reader keepalive; must be acknowledged.</summary>
  Keepalive = 62,

  /// <summary>Reader event, including the connection-attempt result sent right after connect.</summary>
  ReaderEventNotification = 63,

  /// <summary>Client acknowledges a keepalive.</summary>
  KeepaliveAck = 72,
}
