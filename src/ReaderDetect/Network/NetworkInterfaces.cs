using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ReaderDetect.Network;

/// <summary>Finds the local IPv4 networks worth scanning.</summary>
public static class NetworkInterfaces
{
  // Adapters that are almost never where a reader lives. Sweeping a VPN /10
  // would take minutes and annoy an IT department; the user can opt in.
  private static readonly string[] VirtualMarkers =
  [
    "virtual", "vmware", "vmnet", "vbox", "virtualbox", "hyper-v", "vethernet", "tap", "tun", "utun",
    "wireguard", "tailscale", "zerotier", "docker", "bridge", "vpn", "awdl", "llw", "loopback", "pseudo", "npcap", "bluetooth",
  ];

  private static readonly HashSet<NetworkInterfaceType> Wired =
  [
    NetworkInterfaceType.Ethernet, NetworkInterfaceType.Ethernet3Megabit, NetworkInterfaceType.FastEthernetT,
    NetworkInterfaceType.FastEthernetFx, NetworkInterfaceType.GigabitEthernet, NetworkInterfaceType.Wireless80211,
  ];

  /// <summary>
  /// Every up, non-loopback Ethernet/Wi-Fi adapter with a routable IPv4 address.
  /// Link-local 169.254.x addresses are skipped: a reader on such a link is
  /// found through the sweep of the real subnet anyway, and a /16 sweep of it is not.
  /// </summary>
  public static IReadOnlyList<NetworkInterfaceInfo> Enumerate(bool includeVirtual = false)
  {
    var result = new List<NetworkInterfaceInfo>();
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (nic.OperationalStatus != OperationalStatus.Up) continue;
      if (!Wired.Contains(nic.NetworkInterfaceType)) continue;
      var isVirtual = LooksVirtual(nic.Name, nic.Description, nic.NetworkInterfaceType);
      if (isVirtual && !includeVirtual) continue;

      IPInterfaceProperties properties;
      try
      {
        properties = nic.GetIPProperties();
      }
      catch (NetworkInformationException)
      {
        continue;
      }

      PhysicalAddress? mac;
      try
      {
        var raw = nic.GetPhysicalAddress();
        mac = raw.GetAddressBytes().Length == 6 ? raw : null;
      }
      catch (NetworkInformationException)
      {
        mac = null;
      }

      foreach (var unicast in properties.UnicastAddresses)
      {
        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
        var bytes = unicast.Address.GetAddressBytes();
        if (bytes[0] == 169 && bytes[1] == 254) continue;
        if (!TryMask(unicast, out var mask, out var prefix)) continue;
        var index = 0;
        try
        {
          index = properties.GetIPv4Properties()?.Index ?? 0;
        }
        catch (NetworkInformationException)
        {
          // Some virtual adapters have no IPv4 properties block; index 0 means "unknown".
        }

        result.Add(new NetworkInterfaceInfo(nic.Id, nic.Name, nic.Description, nic.NetworkInterfaceType,
          unicast.Address, mask, prefix, mac, isVirtual, index));
      }
    }

    return result;
  }

  /// <summary>Heuristic: does this adapter look like a VPN, VM, container or other non-physical link?</summary>
  public static bool LooksVirtual(string name, string description, NetworkInterfaceType type)
  {
    if (type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp or NetworkInterfaceType.Loopback) return true;
    var text = (name + " " + description).ToLowerInvariant();
    return VirtualMarkers.Any(marker => ContainsToken(text, marker));
  }

  /// <summary>
  /// Picks interfaces by the selectors a user typed (<c>--interface en0</c>,
  /// <c>--interface 192.168.68.127</c>). Unknown selectors are reported so a
  /// typo does not silently scan nothing.
  /// </summary>
  public static IReadOnlyList<NetworkInterfaceInfo> Select(
    IEnumerable<NetworkInterfaceInfo> available,
    IEnumerable<string> selectors,
    out IReadOnlyList<string> unmatched)
  {
    var chosen = new List<NetworkInterfaceInfo>();
    var missing = new List<string>();
    var all = available.ToList();
    foreach (var selector in selectors)
    {
      var hits = all.Where(nic => nic.Matches(selector)).ToList();
      if (hits.Count == 0) missing.Add(selector);
      chosen.AddRange(hits.Where(h => !chosen.Contains(h)));
    }

    unmatched = missing;
    return chosen;
  }

  private static bool ContainsToken(string text, string marker)
  {
    var index = text.IndexOf(marker, StringComparison.Ordinal);
    while (index >= 0)
    {
      var before = index == 0 || !char.IsLetter(text[index - 1]);
      var afterIndex = index + marker.Length;
      var after = afterIndex >= text.Length || !char.IsLetter(text[afterIndex]);
      if (before && after) return true;
      index = text.IndexOf(marker, index + 1, StringComparison.Ordinal);
    }

    return false;
  }

  private static bool TryMask(UnicastIPAddressInformation unicast, out IPAddress mask, out int prefix)
  {
    mask = IPAddress.None;
    prefix = 0;
    try
    {
      mask = unicast.IPv4Mask;
      prefix = IpSubnet.PrefixFromMask(mask);
      return true;
    }
    catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException or NetworkInformationException)
    {
      try
      {
        prefix = unicast.PrefixLength;
        if (prefix is < 0 or > 32) return false;
        mask = new IpSubnet(unicast.Address, prefix).Mask;
        return true;
      }
      catch (PlatformNotSupportedException)
      {
        return false;
      }
    }
  }
}
