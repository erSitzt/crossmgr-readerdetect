using System.Net;
using ReaderDetect.Network;

namespace ReaderDetect.Management.Impinj;

/// <summary>
/// Network setup for Impinj Speedway readers through RShell. IP changes take
/// effect the moment the command returns, so the session may die right after
/// a successful change; that is reported as applied with the connection dropped.
/// </summary>
public sealed class ImpinjConfigurator : IReaderConfigurator
{
  /// <summary>Opens a session for an address and login.</summary>
  public delegate Task<IRShellSession> SessionFactory(IPAddress ip, ReaderCredentials credentials, CancellationToken ct);

  private readonly SessionFactory _open;
  private readonly Action<string>? _log;

  /// <summary>Creates a configurator over any session source (the fake in tests, SSH in production).</summary>
  public ImpinjConfigurator(SessionFactory open, Action<string>? log = null)
  {
    _open = open;
    _log = log;
  }

  /// <summary>A configurator that talks SSH, with a 30 s login timeout.</summary>
  public static ImpinjConfigurator CreateDefault(Action<string>? log = null, TimeSpan? loginTimeout = null) =>
    new(async (ip, credentials, ct) => await SshNetRShellSession.OpenAsync(ip, credentials, loginTimeout ?? TimeSpan.FromSeconds(30), log, ct).ConfigureAwait(false), log);

  /// <inheritdoc/>
  public ReaderVendor Vendor => ReaderVendor.Impinj;

  /// <inheritdoc/>
  public async Task<NetworkSettings> GetNetworkAsync(IPAddress ip, ReaderCredentials credentials, CancellationToken ct = default)
  {
    await using var session = await _open(ip, credentials, ct).ConfigureAwait(false);
    return await ReadAsync(session, ct).ConfigureAwait(false);
  }

  /// <inheritdoc/>
  public IReadOnlyList<string> Plan(NetworkSettings desired)
  {
    var commands = new List<string>();
    if (desired.Hostname is not null) commands.Add(RShellCommands.Hostname(desired.Hostname));
    if (desired.Dhcp)
    {
      commands.Add(RShellCommands.IpDynamic);
    }
    else
    {
      if (desired.Ip is null || desired.Mask is null) throw new ArgumentException("a static configuration needs an address and a mask", nameof(desired));
      foreach (var dns in desired.Dns) commands.Add(RShellCommands.DnsAdd(dns));
      commands.Add(RShellCommands.IpStatic(desired.Ip, desired.Mask, desired.Gateway));
    }

    return commands;
  }

  /// <inheritdoc/>
  public async Task<ApplyResult> SetNetworkAsync(IPAddress ip, ReaderCredentials credentials, NetworkSettings desired, CancellationToken ct = default)
  {
    var commands = Plan(desired);
    var log = new List<string>();
    await using var session = await _open(ip, credentials, ct).ConfigureAwait(false);

    var current = await ReadAsync(session, ct).ConfigureAwait(false);
    log.Add($"before: {current.Describe()}");
    var dropped = false;
    foreach (var command in commands)
    {
      string output;
      try
      {
        output = await session.RunAsync(command, ct).ConfigureAwait(false);
      }
      catch (ConfigurationException ex) when (ex.Kind == ConfigurationFailure.ConnectionDropped && command == commands[^1])
      {
        // The address changed under us; the reader is now elsewhere.
        log.Add($"{command}: connection dropped (address changed)");
        dropped = true;
        break;
      }

      var response = RShellResponse.Parse(output);
      log.Add($"{command}: {response.StatusCode},{response.StatusText}");
      if (!response.Success)
      {
        throw new ConfigurationException(ConfigurationFailure.CommandFailed, $"'{command}' failed: {response.StatusCode},{response.StatusText}");
      }
    }

    _log?.Invoke($"impinj: applied {desired.Describe()} on {ip}");
    return new ApplyResult(true, desired.Dhcp ? null : desired.Ip, dropped, false, log);
  }

  /// <inheritdoc/>
  public async Task RebootAsync(IPAddress ip, ReaderCredentials credentials, CancellationToken ct = default)
  {
    await using var session = await _open(ip, credentials, ct).ConfigureAwait(false);
    try
    {
      var response = RShellResponse.Parse(await session.RunAsync(RShellCommands.Reboot, ct).ConfigureAwait(false));
      if (!response.Success && response.StatusCode >= 0)
      {
        throw new ConfigurationException(ConfigurationFailure.CommandFailed, $"reboot refused: {response.StatusCode},{response.StatusText}");
      }
    }
    catch (ConfigurationException ex) when (ex.Kind == ConfigurationFailure.ConnectionDropped)
    {
      // Rebooting readers do not say goodbye.
    }
  }

  private static async Task<NetworkSettings> ReadAsync(IRShellSession session, CancellationToken ct)
  {
    var response = RShellResponse.Parse(await session.RunAsync(RShellCommands.ShowNetworkSummary, ct).ConfigureAwait(false));
    if (!response.Success)
    {
      throw new ConfigurationException(ConfigurationFailure.CommandFailed, $"'{RShellCommands.ShowNetworkSummary}' failed: {response.StatusCode},{response.StatusText}");
    }

    return new NetworkSettings(
      Dhcp: !string.Equals(response["ipAddressMode"], "Static", StringComparison.OrdinalIgnoreCase),
      Ip: Address(response["ipAddress"]),
      Mask: Address(response["ipMask"]),
      Gateway: Address(response["gatewayAddress"]),
      Dns: [],
      Hostname: response["Hostname"],
      Mac: MacFormat.TryParse(response["MACAddress"], out var mac) ? mac : null,
      Raw: response.Values);
  }

  private static IPAddress? Address(string? text) =>
    text is not null && IPAddress.TryParse(text, out var address) && !address.Equals(IPAddress.Any) ? address : null;
}
