namespace ReaderDetect.Llrp;

/// <summary>The 10-byte LLRP message header.</summary>
/// <param name="Version">Protocol version (1 for LLRP 1.0.1 and 1.1).</param>
/// <param name="Type">Message type.</param>
/// <param name="Length">Total message length including this header.</param>
/// <param name="MessageId">Client-chosen id echoed in responses.</param>
public readonly record struct LlrpHeader(byte Version, LlrpMessageType Type, uint Length, uint MessageId)
{
  /// <summary>Length of the payload that follows the header.</summary>
  public int PayloadLength => (int)Length - LlrpCodec.HeaderLength;
}
