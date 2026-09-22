using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ReaderDetect.Management.Impinj;

/// <summary>
/// RShell over SSH with SSH.NET. Speedway readers take the password through
/// keyboard-interactive, so both that and plain password auth are offered.
/// Host keys are accepted as they come: readers generate their own and nobody
/// pins them. One connection serves all commands of an operation, because the
/// reader throttles logins after a burst of them.
/// </summary>
public sealed class SshNetRShellSession : IRShellSession
{
  private readonly SshClient _client;
  private readonly Action<string>? _log;

  private SshNetRShellSession(SshClient client, Action<string>? log)
  {
    _client = client;
    _log = log;
  }

  /// <summary>Connects and authenticates.</summary>
  /// <exception cref="ConfigurationException">Auth failure, throttling (no answer within <paramref name="timeout"/>), or an unreachable port.</exception>
  public static async Task<SshNetRShellSession> OpenAsync(
    IPAddress ip,
    ReaderCredentials credentials,
    TimeSpan timeout,
    Action<string>? log = null,
    CancellationToken ct = default)
  {
    var keyboard = new KeyboardInteractiveAuthenticationMethod(credentials.Username);
    keyboard.AuthenticationPrompt += (_, e) =>
    {
      foreach (var prompt in e.Prompts) prompt.Response = credentials.Password;
    };
    var info = new ConnectionInfo(ip.ToString(), 22, credentials.Username,
      new PasswordAuthenticationMethod(credentials.Username, credentials.Password), keyboard)
    {
      Timeout = timeout,
    };
    var client = new SshClient(info);
    client.HostKeyReceived += (_, e) => e.CanTrust = true;

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    try
    {
      log?.Invoke($"ssh: connecting to {ip} as {credentials.Username}");
      await client.ConnectAsync(cts.Token).ConfigureAwait(false);
      log?.Invoke("ssh: authenticated");
      return new SshNetRShellSession(client, log);
    }
    catch (SshAuthenticationException ex)
    {
      client.Dispose();
      throw new ConfigurationException(ConfigurationFailure.AuthFailed, $"the reader rejected the login for '{credentials.Username}'", ex);
    }
    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
    {
      client.Dispose();
      throw new ConfigurationException(ConfigurationFailure.Throttled,
        $"the reader did not finish SSH authentication within {timeout.TotalSeconds:F0} s; Impinj readers throttle repeated logins, wait a few minutes and try again", ex);
    }
    catch (Exception ex) when (ex is SocketException or SshConnectionException or SshOperationTimeoutException)
    {
      client.Dispose();
      throw new ConfigurationException(ConfigurationFailure.NotReachable, $"no SSH service reachable at {ip}:22 ({ex.Message})", ex);
    }
  }

  /// <inheritdoc/>
  public async Task<string> RunAsync(string command, CancellationToken ct = default)
  {
    _log?.Invoke($"rshell> {command}");
    try
    {
      using var ssh = _client.CreateCommand(command);
      await ssh.ExecuteAsync(ct).ConfigureAwait(false);
      var output = ssh.Result;
      if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(ssh.Error)) output = ssh.Error;
      foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _log?.Invoke($"rshell  {line.TrimEnd()}");
      return output;
    }
    catch (Exception ex) when (ex is SshConnectionException or SocketException or ObjectDisposedException or SshOperationTimeoutException)
    {
      throw new ConfigurationException(ConfigurationFailure.ConnectionDropped, $"the SSH connection dropped while running '{command}'", ex);
    }
  }

  /// <inheritdoc/>
  public ValueTask DisposeAsync()
  {
    try
    {
      if (_client.IsConnected) _client.Disconnect();
    }
    catch (Exception ex) when (ex is SshException or SocketException or ObjectDisposedException)
    {
      // Already gone; nothing to clean up.
    }

    _client.Dispose();
    return ValueTask.CompletedTask;
  }
}
