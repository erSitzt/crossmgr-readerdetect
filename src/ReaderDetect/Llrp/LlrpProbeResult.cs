namespace ReaderDetect.Llrp;

/// <summary>Outcome of one identify handshake.</summary>
/// <param name="Status">What the port turned out to be.</param>
/// <param name="ConnectionAttempt">The reader's connection-attempt status, when a reader answered at all.</param>
/// <param name="Capabilities">Vendor/model/firmware, when the reader let us read them.</param>
/// <param name="Error">Why the probe stopped short, for the verbose log.</param>
public sealed record LlrpProbeResult(
  LlrpStatus Status,
  ConnectionAttemptStatus? ConnectionAttempt,
  GeneralDeviceCapabilities? Capabilities,
  string? Error);
