using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using ReaderDetect.Network;

namespace ReaderDetect.Vendors;

/// <summary>
/// Readers ship with a hostname derived from their MAC address, which makes the
/// hostname both a vendor fingerprint and something we can predict before we
/// have talked to the reader at all.
/// </summary>
public static partial class HostnameHints
{
  [GeneratedRegex("^FX(7400|7500|9500|9600|R90)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ZebraPattern();

  [GeneratedRegex("^(SpeedwayR|impinj)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ImpinjPattern();

  /// <summary>
  /// The factory hostnames a reader with this MAC would have: Impinj uses
  /// <c>SpeedwayR-12-59-43</c>, Zebra <c>FX7500125943</c> / <c>FX9600125943</c>.
  /// When the MAC prefix identifies the vendor only that vendor's names are returned.
  /// </summary>
  public static IReadOnlyList<string> ExpectedHostnames(PhysicalAddress mac)
  {
    var impinj = $"SpeedwayR-{MacFormat.LastThreeOctetsHyphen(mac)}";
    var zebra = new[] { $"FX7500{MacFormat.LastSixHex(mac)}", $"FX9600{MacFormat.LastSixHex(mac)}" };
    return OuiTable.Lookup(mac) switch
    {
      ReaderVendor.Impinj => [impinj],
      ReaderVendor.Zebra => zebra,
      _ => [impinj, .. zebra],
    };
  }

  /// <summary>Vendor guessed from a hostname, or null when it matches no known pattern.</summary>
  public static ReaderVendor? VendorFromHostname(string? hostname)
  {
    if (string.IsNullOrWhiteSpace(hostname)) return null;
    var name = StripLocal(hostname);
    if (ImpinjPattern().IsMatch(name)) return ReaderVendor.Impinj;
    if (ZebraPattern().IsMatch(name)) return ReaderVendor.Zebra;
    return null;
  }

  /// <summary>
  /// Model family from a hostname: Zebra names start with the model (<c>FX9600…</c>);
  /// Impinj names only say <c>SpeedwayR</c>, which does not distinguish R220 from R420, so null.
  /// </summary>
  public static string? ModelFromHostname(string? hostname)
  {
    if (string.IsNullOrWhiteSpace(hostname)) return null;
    var match = ZebraPattern().Match(StripLocal(hostname));
    return match.Success ? "FX" + match.Groups[1].Value.ToUpperInvariant() : null;
  }

  /// <summary>Removes a trailing <c>.local</c> (and trailing dot) from an mDNS name.</summary>
  public static string StripLocal(string hostname)
  {
    var name = hostname.Trim().TrimEnd('.');
    return name.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? name[..^6] : name;
  }
}
