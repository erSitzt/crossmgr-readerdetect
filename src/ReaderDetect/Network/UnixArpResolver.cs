using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace ReaderDetect.Network;

/// <summary>
/// ARP cache lookup for macOS and Linux development machines by shelling out
/// to the system tools. Only used for developing and testing on a Mac; the
/// shipped tool runs on Windows.
/// </summary>
public sealed class UnixArpResolver : IArpResolver
{
  /// <inheritdoc/>
  public async Task<PhysicalAddress?> ResolveAsync(IPAddress ip, IPAddress? sourceAddress, CancellationToken ct)
  {
    if (OperatingSystem.IsLinux())
    {
      var output = await RunAsync("ip", $"neigh show {ip}", ct).ConfigureAwait(false);
      if (output is not null) return ArpOutputParser.ParseLinux(output, ip);
    }

    var arp = await RunAsync("arp", $"-n {ip}", ct).ConfigureAwait(false);
    return arp is null ? null : ArpOutputParser.ParseMacOs(arp, ip);
  }

  private static async Task<string?> RunAsync(string file, string arguments, CancellationToken ct)
  {
    try
    {
      using var process = new Process
      {
        StartInfo = new ProcessStartInfo(file, arguments)
        {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
        },
      };
      process.Start();
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(TimeSpan.FromSeconds(3));
      var output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
      await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
      return output;
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
    {
      return null;
    }
  }
}
