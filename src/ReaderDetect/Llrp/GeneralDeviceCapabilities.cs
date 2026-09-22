namespace ReaderDetect.Llrp;

/// <summary>The identity part of a reader's GeneralDeviceCapabilities parameter.</summary>
/// <param name="MaxAntennas">Antenna ports the reader supports.</param>
/// <param name="CanSetAntennaProperties">Whether antenna properties are configurable.</param>
/// <param name="HasUtcClock">Whether the reader has a UTC clock.</param>
/// <param name="ManufacturerPen">IANA private enterprise number of the manufacturer.</param>
/// <param name="ModelCode">Vendor-specific model number.</param>
/// <param name="FirmwareVersion">Firmware version string as reported.</param>
public sealed record GeneralDeviceCapabilities(
  ushort MaxAntennas,
  bool CanSetAntennaProperties,
  bool HasUtcClock,
  uint ManufacturerPen,
  uint ModelCode,
  string FirmwareVersion);
