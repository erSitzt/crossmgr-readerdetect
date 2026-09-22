using System.Net;

namespace ReaderDetect.Management;

/// <summary>Reads and changes a reader's network settings through its vendor management interface.</summary>
public interface IReaderConfigurator
{
  /// <summary>The vendor this configurator talks to.</summary>
  ReaderVendor Vendor { get; }

  /// <summary>Reads the current settings.</summary>
  Task<NetworkSettings> GetNetworkAsync(IPAddress ip, ReaderCredentials credentials, CancellationToken ct = default);

  /// <summary>Applies <paramref name="desired"/>; only the fields that are set are changed.</summary>
  Task<ApplyResult> SetNetworkAsync(IPAddress ip, ReaderCredentials credentials, NetworkSettings desired, CancellationToken ct = default);

  /// <summary>Reboots the reader.</summary>
  Task RebootAsync(IPAddress ip, ReaderCredentials credentials, CancellationToken ct = default);

  /// <summary>The commands or requests <see cref="SetNetworkAsync"/> would send, for confirmation dialogs and dry runs.</summary>
  IReadOnlyList<string> Plan(NetworkSettings desired);
}
