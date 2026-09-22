using System.Net;
using System.Net.NetworkInformation;

namespace ReaderDetect.Network;

/// <summary>Resolves an on-link IPv4 address to its MAC address.</summary>
public interface IArpResolver
{
  /// <summary>
  /// The MAC for <paramref name="ip"/>, or null when it cannot be resolved.
  /// <paramref name="sourceAddress"/> is the local address on the same subnet,
  /// which Windows needs to pick the right adapter.
  /// </summary>
  Task<PhysicalAddress?> ResolveAsync(IPAddress ip, IPAddress? sourceAddress, CancellationToken ct);
}

/// <summary>Picks the resolver for the running OS.</summary>
public static class ArpResolver
{
  /// <summary>SendARP on Windows, the <c>arp</c>/<c>ip neigh</c> tools elsewhere.</summary>
  public static IArpResolver CreateForCurrentPlatform() =>
    OperatingSystem.IsWindows() ? new WindowsArpResolver() : new UnixArpResolver();
}
