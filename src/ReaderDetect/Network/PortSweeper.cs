using System.Net;
using System.Net.Sockets;

namespace ReaderDetect.Network;

/// <summary>
/// Tries a TCP connect to one port on many hosts at once. Absent hosts on a
/// LAN only fail after the OS gives up on ARP, so the per-host timeout, not
/// the OS, is what bounds a sweep: 254 hosts at 256-wide parallelism finish
/// in about one timeout.
/// </summary>
public sealed class PortSweeper
{
  private readonly int _maxParallel;
  private readonly TimeSpan _connectTimeout;

  /// <summary>Creates a sweeper.</summary>
  public PortSweeper(int maxParallel = 256, TimeSpan? connectTimeout = null)
  {
    _maxParallel = Math.Max(1, maxParallel);
    _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(600);
  }

  /// <summary>
  /// Connects to <paramref name="port"/> on every host. <paramref name="onOpen"/>
  /// fires as soon as a host accepts (from a pool thread), <paramref name="scanned"/>
  /// reports the running count of hosts tried.
  /// </summary>
  public async Task<IReadOnlyList<IPAddress>> SweepAsync(
    IEnumerable<IPAddress> hosts,
    int port,
    Action<IPAddress>? onOpen = null,
    IProgress<int>? scanned = null,
    CancellationToken ct = default)
  {
    var open = new List<IPAddress>();
    var count = 0;
    var options = new ParallelOptions { MaxDegreeOfParallelism = _maxParallel, CancellationToken = ct };
    await Parallel.ForEachAsync(hosts, options, async (host, token) =>
    {
      if (await IsOpenAsync(host, port, _connectTimeout, token).ConfigureAwait(false))
      {
        lock (open)
        {
          open.Add(host);
        }

        onOpen?.Invoke(host);
      }

      scanned?.Report(Interlocked.Increment(ref count));
    }).ConfigureAwait(false);

    return open;
  }

  /// <summary>One connect attempt; true when something accepted within the timeout.</summary>
  public static async Task<bool> IsOpenAsync(IPAddress host, int port, TimeSpan timeout, CancellationToken ct)
  {
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    try
    {
      await socket.ConnectAsync(new IPEndPoint(host, port), cts.Token).ConfigureAwait(false);
      return true;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return false;
    }
    catch (SocketException)
    {
      return false;
    }
  }
}
