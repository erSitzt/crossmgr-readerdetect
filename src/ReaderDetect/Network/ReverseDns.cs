using System.Net;
using System.Net.Sockets;

namespace ReaderDetect.Network;

/// <summary>Reverse DNS with a real timeout; <see cref="Dns.GetHostEntryAsync(IPAddress)"/> has none.</summary>
public static class ReverseDns
{
  /// <summary>The PTR name for <paramref name="ip"/>, or null when there is none or the resolver is slow.</summary>
  public static async Task<string?> LookupAsync(IPAddress ip, TimeSpan timeout, CancellationToken ct)
  {
    try
    {
      var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(timeout, ct).ConfigureAwait(false);
      var name = entry.HostName;
      return string.IsNullOrWhiteSpace(name) || name == ip.ToString() ? null : name;
    }
    catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
    {
      return null;
    }
  }

  /// <summary>
  /// Forward-resolves a name through the OS resolver (which speaks mDNS for
  /// <c>.local</c> on Windows 10+ and macOS) and says whether it points at <paramref name="ip"/>.
  /// </summary>
  public static async Task<bool> ResolvesToAsync(string hostname, IPAddress ip, TimeSpan timeout, CancellationToken ct)
  {
    try
    {
      var addresses = await Dns.GetHostAddressesAsync(hostname, ct).WaitAsync(timeout, ct).ConfigureAwait(false);
      return addresses.Any(a => a.Equals(ip));
    }
    catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
    {
      return false;
    }
  }
}
