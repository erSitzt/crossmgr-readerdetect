namespace ReaderDetect.Management.Impinj;

/// <summary>A scripted session for tests: answers from a lookup and can hang up after a given command.</summary>
public sealed class FakeRShellSession : IRShellSession
{
  private readonly Func<string, string> _responder;
  private readonly int? _dropAfterCommand;

  /// <summary>Creates the fake.</summary>
  /// <param name="responder">Maps a command to its printed answer.</param>
  /// <param name="dropAfterCommand">1-based index of the command after which the "connection" drops, or null.</param>
  public FakeRShellSession(Func<string, string> responder, int? dropAfterCommand = null)
  {
    _responder = responder;
    _dropAfterCommand = dropAfterCommand;
  }

  /// <summary>Commands run so far, in order.</summary>
  public List<string> Commands { get; } = [];

  /// <summary>Whether <see cref="DisposeAsync"/> was called.</summary>
  public bool Disposed { get; private set; }

  /// <inheritdoc/>
  public Task<string> RunAsync(string command, CancellationToken ct = default)
  {
    Commands.Add(command);
    if (_dropAfterCommand is { } n && Commands.Count >= n)
    {
      throw new ConfigurationException(ConfigurationFailure.ConnectionDropped, "connection dropped");
    }

    return Task.FromResult(_responder(command));
  }

  /// <inheritdoc/>
  public ValueTask DisposeAsync()
  {
    Disposed = true;
    return ValueTask.CompletedTask;
  }
}
