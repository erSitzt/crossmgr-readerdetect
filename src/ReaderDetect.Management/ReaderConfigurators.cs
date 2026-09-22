using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Management.Impinj;
using ReaderDetect.Network;

namespace ReaderDetect.Management;

/// <summary>Picks the configurator for a reader and verifies a reader came back after a change.</summary>
public static class ReaderConfigurators
{
  /// <summary>The configurator for a vendor.</summary>
  /// <exception cref="ConfigurationException">No management path exists for the vendor.</exception>
  public static IReaderConfigurator For(ReaderVendor vendor, Action<string>? log = null) => vendor switch
  {
    ReaderVendor.Impinj => ImpinjConfigurator.CreateDefault(log),
    _ => throw new ConfigurationException(ConfigurationFailure.Unsupported, $"network setup is not supported for vendor {vendor}"),
  };

  /// <summary>Identifies the reader at <paramref name="ip"/> (LLRP, mDNS name, MAC prefix) to pick its vendor.</summary>
  public static async Task<ReaderInfo> DetectAsync(IPAddress ip, ReaderScanner scanner, CancellationToken ct = default) =>
    await scanner.ProbeAsync(ip, ct).ConfigureAwait(false);

  /// <summary>
  /// Waits for the reader to show up again after a change: at <paramref name="expected"/>
  /// when a static address was set, otherwise anywhere on the scanned subnets
  /// with the same <paramref name="mac"/>. When a reboot was requested the
  /// reader keeps answering at <paramref name="previous"/> for a few seconds
  /// first, so the wait starts only once that address has gone quiet.
  /// </summary>
  public static async Task<ReaderInfo?> WaitForReaderAsync(
    IPAddress? expected,
    PhysicalAddress? mac,
    ReaderScanner scanner,
    TimeSpan timeout,
    Action<string>? progress = null,
    CancellationToken ct = default,
    IPAddress? previous = null,
    bool rebooting = false)
  {
    var deadline = DateTime.UtcNow + timeout;
    if (rebooting && previous is not null)
    {
      var goneBy = DateTime.UtcNow + TimeSpan.FromSeconds(45);
      while (DateTime.UtcNow < goneBy && await PortSweeper.IsOpenAsync(previous, scanner.Options.LlrpPort, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false))
      {
        progress?.Invoke($"waiting for {previous} to go down for the reboot");
        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
      }
    }

    while (DateTime.UtcNow < deadline)
    {
      ct.ThrowIfCancellationRequested();
      if (expected is not null)
      {
        progress?.Invoke($"waiting for {expected}");
        if (await PortSweeper.IsOpenAsync(expected, scanner.Options.LlrpPort, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false))
        {
          var info = await scanner.ProbeAsync(expected, ct).ConfigureAwait(false);
          if (info.Confidence >= Confidence.Likely) return info;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
      }
      else
      {
        progress?.Invoke("rescanning for the reader's new address");
        var result = await scanner.ScanAsync(null, ct).ConfigureAwait(false);
        var match = result.Readers.FirstOrDefault(r => mac is not null && r.Mac is not null && r.Mac.Equals(mac))
                    ?? (mac is null && result.Readers.Count == 1 ? result.Readers[0] : null);
        if (match is not null) return match;
        await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
      }
    }

    return null;
  }
}
