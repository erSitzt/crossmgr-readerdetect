using System.Net;
using System.Net.Sockets;
using System.Text;
using ReaderDetect.Network;

namespace ReaderDetect.WsDiscovery;

/// <summary>
/// Multicasts a Probe on each interface and collects the unicast ProbeMatch
/// replies. Windows PCs, printers and cameras answer too; the classifier
/// drops them later because nothing else about them looks like a reader.
/// </summary>
public sealed class WsDiscoveryProbe
{
  private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.255.250");
  private const int MulticastPort = 3702;
  private static readonly int[] ProbeDelaysMs = [0, 500];

  private readonly Action<string>? _log;
  private readonly List<string> _warnings = [];

  /// <summary>Creates a probe.</summary>
  public WsDiscoveryProbe(Action<string>? log = null)
  {
    _log = log;
  }

  /// <summary>Problems worth telling the user afterwards.</summary>
  public IReadOnlyList<string> Warnings => _warnings;

  /// <summary>Probes for <paramref name="duration"/>; <paramref name="onFound"/> fires once per responder.</summary>
  public async Task<IReadOnlyList<WsDiscoveryMatch>> ProbeAsync(
    IReadOnlyList<NetworkInterfaceInfo> interfaces,
    TimeSpan duration,
    Action<WsDiscoveryMatch>? onFound = null,
    CancellationToken ct = default)
  {
    _warnings.Clear();
    var sockets = new List<(Socket Socket, NetworkInterfaceInfo Nic)>();
    foreach (var nic in interfaces)
    {
      try
      {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(nic.Address, 0));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
        sockets.Add((socket, nic));
      }
      catch (SocketException ex)
      {
        _warnings.Add($"WS-Discovery: cannot probe on {nic.Name} ({ex.SocketErrorCode})");
      }
    }

    if (sockets.Count == 0) return [];

    var results = new List<WsDiscoveryMatch>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(duration);
    var token = cts.Token;

    var sender = Task.Run(async () =>
    {
      var elapsed = 0;
      foreach (var delay in ProbeDelaysMs)
      {
        if (delay > elapsed)
        {
          await Task.Delay(delay - elapsed, token).ConfigureAwait(false);
          elapsed = delay;
        }

        foreach (var (socket, nic) in sockets)
        {
          foreach (var oasis in new[] { false, true })
          {
            var probe = Encoding.UTF8.GetBytes(WsDiscoveryCodec.BuildProbe(Guid.NewGuid(), oasis));
            try
            {
              await socket.SendToAsync(probe, SocketFlags.None, new IPEndPoint(MulticastGroup, MulticastPort), token).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
              _log?.Invoke($"wsd: send on {nic.Name} failed ({ex.SocketErrorCode})");
            }
          }
        }
      }
    }, token);

    var receivers = sockets.Select(entry => Task.Run(async () =>
    {
      var buffer = new byte[65536];
      while (!token.IsCancellationRequested)
      {
        SocketReceiveFromResult result;
        try
        {
          result = await entry.Socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
          break;
        }

        if (result.RemoteEndPoint is not IPEndPoint from) continue;
        var xml = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
        foreach (var match in WsDiscoveryCodec.ParseProbeMatches(xml, from.Address, entry.Nic))
        {
          bool fresh;
          lock (results)
          {
            fresh = seen.Add($"{from.Address}|{match.EndpointAddress}");
            if (fresh) results.Add(match);
          }

          if (!fresh) continue;
          _log?.Invoke($"wsd: {from.Address} {match.HostFromXAddrs ?? match.EndpointAddress ?? "?"} [{string.Join(" ", match.Types)}]");
          onFound?.Invoke(match);
        }
      }
    }, token)).ToList();

    try
    {
      await Task.WhenAll([sender, .. receivers]).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      // The probe window ended.
    }
    finally
    {
      foreach (var (socket, _) in sockets) socket.Dispose();
    }

    ct.ThrowIfCancellationRequested();
    lock (results)
    {
      return [.. results];
    }
  }
}
