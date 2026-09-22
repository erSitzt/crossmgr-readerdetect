using System.Net;
using System.Net.NetworkInformation;

namespace ReaderDetect.Network;

/// <summary>One IPv4 address on one local network interface: the unit a sweep runs over.</summary>
/// <param name="Id">OS interface id.</param>
/// <param name="Name">Short name (<c>en0</c>, <c>Ethernet 2</c>).</param>
/// <param name="Description">Adapter description (driver/model name on Windows).</param>
/// <param name="Type">Interface type.</param>
/// <param name="Address">The IPv4 address.</param>
/// <param name="Mask">Its subnet mask.</param>
/// <param name="PrefixLength">The mask as a prefix length.</param>
/// <param name="Mac">The adapter's MAC, if any.</param>
/// <param name="IsVirtual">Whether the name/type looks like a VPN, VM or container adapter.</param>
/// <param name="Index">Interface index, used to pick the multicast egress interface.</param>
public sealed record NetworkInterfaceInfo(
  string Id,
  string Name,
  string Description,
  NetworkInterfaceType Type,
  IPAddress Address,
  IPAddress Mask,
  int PrefixLength,
  PhysicalAddress? Mac,
  bool IsVirtual,
  int Index)
{
  /// <summary>The subnet the address lives in.</summary>
  public IpSubnet Subnet => new(Address, PrefixLength);

  /// <summary>Short kind label for tables.</summary>
  public string Kind => Type switch
  {
    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
    NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
      or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet => "Ethernet",
    _ => Type.ToString(),
  } + (IsVirtual ? " (virtual)" : "");

  /// <summary>True when <paramref name="selector"/> names this interface by name, id, or address.</summary>
  public bool Matches(string selector) =>
    string.Equals(selector, Name, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(selector, Id, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(selector, Address.ToString(), StringComparison.Ordinal) ||
    string.Equals(selector, Description, StringComparison.OrdinalIgnoreCase);

  /// <inheritdoc/>
  public override string ToString() => $"{Name}  {Address}/{PrefixLength}  {Kind}";
}
