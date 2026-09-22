using System.Globalization;
using System.Net.NetworkInformation;

namespace ReaderDetect.Network;

/// <summary>
/// MAC address text helpers. <see cref="PhysicalAddress.ToString"/> gives bare
/// upper-case hex, which is neither what people read nor what readers print
/// on their labels, so everything user-facing goes through here.
/// </summary>
public static class MacFormat
{
  /// <summary>Formats as <c>00:16:25:12:59:43</c>.</summary>
  public static string Colon(PhysicalAddress mac) =>
    string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

  /// <summary>The first three octets, <c>00:16:25</c>, which identify the manufacturer.</summary>
  public static string Oui(PhysicalAddress mac) =>
    string.Join(":", mac.GetAddressBytes().Take(3).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

  /// <summary>The last three octets hyphenated, <c>12-59-43</c>, as Impinj uses in its default hostname.</summary>
  public static string LastThreeOctetsHyphen(PhysicalAddress mac) =>
    string.Join("-", mac.GetAddressBytes().Skip(3).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

  /// <summary>The last six hex digits, <c>125943</c>, as Zebra uses in its default hostname.</summary>
  public static string LastSixHex(PhysicalAddress mac) =>
    string.Concat(mac.GetAddressBytes().Skip(3).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

  /// <summary>
  /// Parses <c>00:16:25:12:59:43</c>, <c>00-16-25-12-59-43</c>, <c>001625125943</c> and the
  /// macOS <c>arp</c> spelling without leading zeros, <c>0:16:25:12:59:43</c>.
  /// </summary>
  /// <exception cref="FormatException">The text is not a 48-bit MAC address.</exception>
  public static PhysicalAddress Parse(string text) =>
    TryParse(text, out var mac) ? mac : throw new FormatException($"'{text}' is not a MAC address");

  /// <summary>Non-throwing variant of <see cref="Parse"/>.</summary>
  public static bool TryParse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PhysicalAddress? mac)
  {
    mac = null;
    if (string.IsNullOrWhiteSpace(text)) return false;
    var trimmed = text.Trim();
    string[] parts = trimmed.Contains(':') ? trimmed.Split(':')
      : trimmed.Contains('-') ? trimmed.Split('-')
      : trimmed.Length == 12 ? Enumerable.Range(0, 6).Select(i => trimmed.Substring(i * 2, 2)).ToArray()
      : [];
    if (parts.Length != 6) return false;

    var bytes = new byte[6];
    for (var i = 0; i < 6; i++)
    {
      if (parts[i].Length is < 1 or > 2 ||
          !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
      {
        return false;
      }
    }

    mac = new PhysicalAddress(bytes);
    return true;
  }
}
