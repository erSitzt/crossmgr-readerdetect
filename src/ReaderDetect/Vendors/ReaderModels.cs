using System.Globalization;

namespace ReaderDetect.Vendors;

/// <summary>
/// Maps the numbers a reader reports in its LLRP GeneralDeviceCapabilities
/// (IANA private enterprise number and vendor model code) to names.
/// </summary>
public static class ReaderModels
{
  /// <summary>IANA private enterprise number of Impinj.</summary>
  public const uint ImpinjPen = 25882;

  /// <summary>IANA private enterprise number of Motorola Solutions, still reported by Zebra FX readers.</summary>
  public const uint ZebraPen = 161;

  // Impinj Octane LLRP model codes. Verified on a real R220 (2001001); the rest
  // are from the Octane LLRP documentation.
  private static readonly Dictionary<uint, string> ImpinjModels = new()
  {
    [2001001] = "Speedway R220",
    [2001002] = "Speedway R420",
    [2001003] = "xPortal",
    [2001004] = "xArray WM",
    [2001007] = "xArray",
    [2001009] = "Speedway R120",
    [2001052] = "R700",
  };

  /// <summary>Vendor for a private enterprise number.</summary>
  public static ReaderVendor VendorFromPen(uint pen) => pen switch
  {
    ImpinjPen => ReaderVendor.Impinj,
    ZebraPen => ReaderVendor.Zebra,
    _ => ReaderVendor.Unknown,
  };

  /// <summary>The model name for a vendor/model code, or null when the code is not in the table.</summary>
  public static string? ModelName(ReaderVendor vendor, uint modelCode) =>
    vendor == ReaderVendor.Impinj && ImpinjModels.TryGetValue(modelCode, out var name) ? name : null;

  /// <summary>Model name, or a readable fallback like <c>model 2001099</c> so the raw code is never lost.</summary>
  public static string Describe(ReaderVendor vendor, uint modelCode) =>
    ModelName(vendor, modelCode) ?? $"model {modelCode.ToString(CultureInfo.InvariantCulture)}";
}
