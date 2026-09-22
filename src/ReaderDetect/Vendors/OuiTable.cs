using System.Net.NetworkInformation;
using ReaderDetect.Network;

namespace ReaderDetect.Vendors;

/// <summary>
/// A deliberately tiny OUI table: only the prefixes that matter for telling a
/// reader from a laptop. The full IEEE list is 30k lines and would only add
/// noise; a MAC prefix is a hint anyway, the LLRP handshake is the proof.
/// </summary>
public static class OuiTable
{
  private static readonly Dictionary<string, (ReaderVendor Vendor, string Name)> Entries = new(StringComparer.OrdinalIgnoreCase)
  {
    ["00:16:25"] = (ReaderVendor.Impinj, "Impinj"),
    ["00:23:68"] = (ReaderVendor.Zebra, "Zebra Technologies"),
    ["00:15:70"] = (ReaderVendor.Zebra, "Zebra Technologies (Symbol)"),
    ["00:A0:F8"] = (ReaderVendor.Zebra, "Zebra Technologies (Symbol)"),
    ["00:17:23"] = (ReaderVendor.Zebra, "Zebra Technologies (Symbol)"),
    ["84:24:8D"] = (ReaderVendor.Zebra, "Zebra Technologies"),
    ["48:A4:93"] = (ReaderVendor.Zebra, "Zebra Technologies"),
    ["40:83:DE"] = (ReaderVendor.Zebra, "Zebra Technologies"),
    ["94:FB:29"] = (ReaderVendor.Zebra, "Zebra Technologies"),
    ["90:75:DE"] = (ReaderVendor.Zebra, "Zebra Technologies"),
  };

  /// <summary>The vendor a MAC prefix belongs to, or null when unknown.</summary>
  public static ReaderVendor? Lookup(PhysicalAddress? mac) =>
    mac is not null && Entries.TryGetValue(MacFormat.Oui(mac), out var entry) ? entry.Vendor : null;

  /// <summary>Human-readable manufacturer name for a MAC prefix, or null when unknown.</summary>
  public static string? VendorName(PhysicalAddress? mac) =>
    mac is not null && Entries.TryGetValue(MacFormat.Oui(mac), out var entry) ? entry.Name : null;
}
