using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Network;

namespace ReaderDetect.Management;

/// <summary>A reader's IPv4 settings, both as read and as desired.</summary>
/// <param name="Dhcp">Address by DHCP (true) or static (false).</param>
/// <param name="Ip">Static address (or the current one when reading).</param>
/// <param name="Mask">Subnet mask.</param>
/// <param name="Gateway">Default gateway.</param>
/// <param name="Dns">DNS servers.</param>
/// <param name="Hostname">Hostname.</param>
/// <param name="Mac">MAC address as the reader reports it (read only).</param>
/// <param name="Raw">Every vendor key/value exactly as read, for display and for echoing back unchanged fields.</param>
public sealed record NetworkSettings(
  bool Dhcp,
  IPAddress? Ip,
  IPAddress? Mask,
  IPAddress? Gateway,
  IReadOnlyList<IPAddress> Dns,
  string? Hostname,
  PhysicalAddress? Mac,
  IReadOnlyDictionary<string, string> Raw)
{
  /// <summary>Desired settings: DHCP, optionally with a new hostname.</summary>
  public static NetworkSettings ForDhcp(string? hostname = null) =>
    new(true, null, null, null, [], hostname, null, new Dictionary<string, string>());

  /// <summary>Desired settings: a static address.</summary>
  /// <exception cref="ArgumentException">The mask is not contiguous or the gateway is outside the subnet.</exception>
  public static NetworkSettings ForStatic(IPAddress ip, IPAddress mask, IPAddress? gateway, IReadOnlyList<IPAddress>? dns = null, string? hostname = null)
  {
    if (!IpSubnet.IsValidMask(mask)) throw new ArgumentException($"{mask} is not a valid subnet mask", nameof(mask));
    var subnet = IpSubnet.FromAddress(ip, mask);
    if (gateway is not null && !subnet.Contains(gateway))
    {
      throw new ArgumentException($"gateway {gateway} is not inside {subnet}", nameof(gateway));
    }

    return new NetworkSettings(false, ip, mask, gateway, dns ?? [], hostname, null, new Dictionary<string, string>());
  }

  /// <summary>One line for confirmations and logs, e.g. <c>static 192.168.68.200/255.255.255.0 gateway 192.168.68.1</c>.</summary>
  public string Describe()
  {
    var text = Dhcp ? "DHCP" : $"static {Ip}/{Mask}" + (Gateway is null ? "" : $" gateway {Gateway}");
    if (Dns.Count > 0) text += $" dns {string.Join(",", Dns)}";
    if (Hostname is not null) text += $" hostname {Hostname}";
    return text;
  }
}
