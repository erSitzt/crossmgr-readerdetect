using System.Net;
using System.Net.Sockets;
using ReaderDetect.Network;

namespace ReaderDetect.Mdns;

/// <summary>
/// Browses for one service type. Queries go out from an ephemeral port on each
/// interface with the QU bit set, so responders answer unicast to that port;
/// this sidesteps port 5353, which the OS's own responder owns on Windows and
/// macOS. A best-effort multicast listener on 5353 catches responders that
/// answer multicast anyway.
/// </summary>
public sealed class MdnsBrowser
{
  private static readonly IPAddress MulticastGroup = IPAddress.Parse("224.0.0.251");
  private const int MulticastPort = 5353;
  private static readonly int[] QueryDelaysMs = [0, 400, 1000];

  private readonly Action<string>? _log;
  private readonly List<string> _warnings = [];

  /// <summary>Creates a browser.</summary>
  public MdnsBrowser(Action<string>? log = null)
  {
    _log = log;
  }

  /// <summary>Problems worth telling the user after a browse (e.g. the 5353 listener was refused).</summary>
  public IReadOnlyList<string> Warnings => _warnings;

  /// <summary>
  /// Collects instances of <paramref name="serviceType"/> for <paramref name="duration"/>.
  /// <paramref name="onFound"/> fires the first time each instance is complete enough to use.
  /// </summary>
  public async Task<IReadOnlyList<MdnsService>> BrowseAsync(
    string serviceType,
    IReadOnlyList<NetworkInterfaceInfo> interfaces,
    TimeSpan duration,
    Action<MdnsService>? onFound = null,
    CancellationToken ct = default)
  {
    _warnings.Clear();
    var packets = new List<(DnsMessage Message, IPAddress From, NetworkInterfaceInfo? Nic)>();
    var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sockets = new List<(Socket Socket, NetworkInterfaceInfo? Nic)>();
    var query = DnsCodec.BuildQuery(serviceType, DnsRecordType.Ptr, unicastResponse: true);

    foreach (var nic in interfaces)
    {
      try
      {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(nic.Address, 0));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
        sockets.Add((socket, nic));
      }
      catch (SocketException ex)
      {
        _warnings.Add($"mDNS: cannot query on {nic.Name} ({ex.SocketErrorCode})");
      }
    }

    try
    {
      var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
      listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      listener.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
      foreach (var nic in interfaces)
      {
        try
        {
          listener.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastGroup, nic.Address));
        }
        catch (SocketException ex)
        {
          _log?.Invoke($"mdns: membership on {nic.Name} refused ({ex.SocketErrorCode})");
        }
      }

      sockets.Add((listener, null));
    }
    catch (SocketException ex)
    {
      // Not fatal: the unicast replies to the query sockets are the primary path.
      _warnings.Add($"mDNS: multicast listener on port {MulticastPort} unavailable ({ex.SocketErrorCode}); relying on unicast replies");
    }

    if (sockets.Count == 0) return [];

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(duration);
    var token = cts.Token;

    var sender = Task.Run(async () =>
    {
      var elapsed = 0;
      foreach (var delay in QueryDelaysMs)
      {
        if (delay > elapsed)
        {
          await Task.Delay(delay - elapsed, token).ConfigureAwait(false);
          elapsed = delay;
        }

        foreach (var (socket, nic) in sockets)
        {
          if (nic is null) continue;
          try
          {
            await socket.SendToAsync(query, SocketFlags.None, new IPEndPoint(MulticastGroup, MulticastPort), token).ConfigureAwait(false);
          }
          catch (SocketException ex)
          {
            _log?.Invoke($"mdns: send on {nic.Name} failed ({ex.SocketErrorCode})");
          }
        }
      }
    }, token);

    var receivers = sockets.Select(entry => Task.Run(async () =>
    {
      var buffer = new byte[9000];
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

        if (result.RemoteEndPoint is not IPEndPoint from || !DnsCodec.TryParse(buffer.AsSpan(0, result.ReceivedBytes), out var message) || !message.IsResponse)
        {
          continue;
        }

        var nic = entry.Nic ?? interfaces.FirstOrDefault(n => n.Subnet.Contains(from.Address));
        List<MdnsService> fresh;
        lock (packets)
        {
          packets.Add((message, from.Address, nic));
          fresh = Assemble(serviceType, packets).Where(s => reported.Add(s.InstanceName)).ToList();
        }

        foreach (var service in fresh)
        {
          _log?.Invoke($"mdns: {service.InstanceName} at {string.Join(",", service.Addresses)} port {service.Port}");
          onFound?.Invoke(service);
        }
      }
    }, token)).ToList();

    try
    {
      await Task.WhenAll([sender, .. receivers]).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      // The browse window simply ended.
    }
    finally
    {
      foreach (var (socket, _) in sockets) socket.Dispose();
    }

    ct.ThrowIfCancellationRequested();
    lock (packets)
    {
      return Assemble(serviceType, packets);
    }
  }

  /// <summary>
  /// Joins PTR → SRV → A records into services. Pure, so it can be tested with
  /// hand-built packets; also handles responders that put the records in
  /// separate packets or leave the A record out.
  /// </summary>
  public static IReadOnlyList<MdnsService> Assemble(
    string serviceType,
    IEnumerable<(DnsMessage Message, IPAddress From, NetworkInterfaceInfo? Nic)> packets)
  {
    var type = serviceType.TrimEnd('.');
    var ptrs = new List<(string Instance, IPAddress From, NetworkInterfaceInfo? Nic)>();
    var srvs = new Dictionary<string, SrvRecord>(StringComparer.OrdinalIgnoreCase);
    var txts = new Dictionary<string, TxtRecord>(StringComparer.OrdinalIgnoreCase);
    var addresses = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);

    foreach (var (message, from, nic) in packets)
    {
      foreach (var record in message.Records)
      {
        switch (record)
        {
          case PtrRecord ptr when string.Equals(ptr.Name, type, StringComparison.OrdinalIgnoreCase):
            ptrs.Add((ptr.Target, from, nic));
            break;
          case SrvRecord srv:
            srvs.TryAdd(srv.Name, srv);
            break;
          case TxtRecord txt:
            txts.TryAdd(txt.Name, txt);
            break;
          case ARecord a:
            if (!addresses.TryGetValue(a.Name, out var list)) addresses[a.Name] = list = [];
            if (!list.Contains(a.Address)) list.Add(a.Address);
            break;
        }
      }
    }

    var services = new List<MdnsService>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (instance, from, nic) in ptrs)
    {
      if (!seen.Add(instance)) continue;
      srvs.TryGetValue(instance, out var srv);
      txts.TryGetValue(instance, out var txt);
      var host = srv?.Target;
      var ips = host is not null && addresses.TryGetValue(host, out var found) && found.Count > 0 ? found : [from];
      var label = instance.EndsWith("." + type, StringComparison.OrdinalIgnoreCase) ? instance[..^(type.Length + 1)] : instance;
      services.Add(new MdnsService(label, type, host, srv?.Port ?? 0, ips,
        txt?.Pairs ?? new Dictionary<string, string>(), from, nic));
    }

    return services;
  }
}
