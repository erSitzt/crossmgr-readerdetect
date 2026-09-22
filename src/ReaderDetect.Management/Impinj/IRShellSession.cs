namespace ReaderDetect.Management.Impinj;

/// <summary>One authenticated RShell session; each command runs on its own exec channel.</summary>
public interface IRShellSession : IAsyncDisposable
{
  /// <summary>Runs one command and returns everything it printed.</summary>
  /// <exception cref="ConfigurationException">With <see cref="ConfigurationFailure.ConnectionDropped"/> when the reader hung up mid-command.</exception>
  Task<string> RunAsync(string command, CancellationToken ct = default);
}
