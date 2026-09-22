using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ReaderDetect.Network;

/// <summary>
/// ARP via iphlpapi's SendARP. It needs no driver and no admin rights, unlike
/// raw sockets, and works because the sweep that ran just before has already
/// put the reader into the ARP cache.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsArpResolver : IArpResolver
{
  /// <inheritdoc/>
  public Task<PhysicalAddress?> ResolveAsync(IPAddress ip, IPAddress? sourceAddress, CancellationToken ct) =>
    Task.Run(() => Resolve(ip, sourceAddress), ct);

  private static PhysicalAddress? Resolve(IPAddress ip, IPAddress? sourceAddress)
  {
    // SendARP wants the address as inet_addr() returns it: the four bytes in memory order.
    var dest = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
    var src = sourceAddress is null ? 0u : BitConverter.ToUInt32(sourceAddress.GetAddressBytes(), 0);
    var mac = new byte[6];
    var length = (uint)mac.Length;
    var result = SendARP(dest, src, mac, ref length);
    return result == 0 && length == 6 ? new PhysicalAddress(mac) : null;
  }

  [DllImport("iphlpapi.dll", ExactSpelling = true)]
  private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddress, ref uint macAddressLength);
}
