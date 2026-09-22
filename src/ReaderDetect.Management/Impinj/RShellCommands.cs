using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ReaderDetect.Network;

namespace ReaderDetect.Management.Impinj;

/// <summary>
/// The RShell command strings, built here so their exact spelling is tested
/// and arguments are validated before anything is sent to a reader.
/// </summary>
public static partial class RShellCommands
{
  /// <summary>Current network configuration and state.</summary>
  public const string ShowNetworkSummary = "show network summary";

  /// <summary>Switch to DHCP; takes effect immediately.</summary>
  public const string IpDynamic = "config network ip dynamic";

  /// <summary>Reboot the reader.</summary>
  public const string Reboot = "reboot";

  [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
  private static partial Regex HostnamePattern();

  /// <summary>
  /// Set a static address; takes effect immediately. The broadcast address is
  /// left out so the reader derives it from the mask, as the manual allows.
  /// </summary>
  public static string IpStatic(IPAddress ip, IPAddress mask, IPAddress? gateway)
  {
    RequireIpv4(ip, nameof(ip));
    RequireIpv4(mask, nameof(mask));
    if (!IpSubnet.IsValidMask(mask)) throw new ArgumentException($"{mask} is not a valid subnet mask", nameof(mask));
    if (gateway is null) return $"config network ip static {ip} {mask}";
    RequireIpv4(gateway, nameof(gateway));
    return $"config network ip static {ip} {mask} {gateway}";
  }

  /// <summary>Set the hostname (a DHCP-supplied name still wins while on DHCP).</summary>
  public static string Hostname(string hostname)
  {
    if (!HostnamePattern().IsMatch(hostname)) throw new ArgumentException($"'{hostname}' is not a valid hostname", nameof(hostname));
    return $"config network hostname {hostname}";
  }

  /// <summary>Add a static DNS server.</summary>
  public static string DnsAdd(IPAddress server)
  {
    RequireIpv4(server, nameof(server));
    return $"config network dns add {server}";
  }

  private static void RequireIpv4(IPAddress address, string name)
  {
    if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("IPv4 address required", name);
  }
}
