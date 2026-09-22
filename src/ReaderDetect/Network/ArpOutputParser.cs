using System.Net;
using System.Net.NetworkInformation;

namespace ReaderDetect.Network;

/// <summary>Pure parsers for the text the system ARP tools print.</summary>
public static class ArpOutputParser
{
  /// <summary>
  /// macOS/BSD: <c>? (192.168.68.139) at 0:16:25:12:59:43 on en0 ifscope [ethernet]</c>;
  /// an unresolved entry says <c>(incomplete)</c>.
  /// </summary>
  public static PhysicalAddress? ParseMacOs(string output, IPAddress ip)
  {
    var needle = $"({ip})";
    foreach (var line in output.Split('\n'))
    {
      if (!line.Contains(needle, StringComparison.Ordinal)) continue;
      var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var at = Array.IndexOf(tokens, "at");
      if (at < 0 || at + 1 >= tokens.Length) continue;
      return MacFormat.TryParse(tokens[at + 1], out var mac) ? mac : null;
    }

    return null;
  }

  /// <summary>Linux <c>ip neigh</c>: <c>192.168.68.139 dev eth0 lladdr 00:16:25:12:59:43 REACHABLE</c>.</summary>
  public static PhysicalAddress? ParseLinux(string output, IPAddress ip)
  {
    foreach (var line in output.Split('\n'))
    {
      var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (tokens.Length == 0 || tokens[0] != ip.ToString()) continue;
      var lladdr = Array.IndexOf(tokens, "lladdr");
      if (lladdr < 0 || lladdr + 1 >= tokens.Length) continue;
      return MacFormat.TryParse(tokens[lladdr + 1], out var mac) ? mac : null;
    }

    return null;
  }
}
