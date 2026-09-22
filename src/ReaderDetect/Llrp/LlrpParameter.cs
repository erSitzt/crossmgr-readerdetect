namespace ReaderDetect.Llrp;

/// <summary>One TLV parameter: its type number and its value bytes (header excluded).</summary>
/// <param name="Type">Parameter type number.</param>
/// <param name="Value">Value bytes, which may contain nested parameters.</param>
public readonly record struct LlrpParameter(ushort Type, ReadOnlyMemory<byte> Value);
