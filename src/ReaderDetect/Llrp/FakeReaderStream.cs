namespace ReaderDetect.Llrp;

/// <summary>
/// A scripted duplex stream that plays the reader's side of the handshake, so
/// the probe logic can be tested without a reader. Bytes written by the probe
/// are recorded per write call and handed to <c>reply</c>, whose result (if
/// any) becomes readable. It ships in the library, like the fakes in
/// crossmgr-lora, so the host application's tests can use it too.
/// </summary>
public sealed class FakeReaderStream : Stream
{
  private readonly Func<byte[], byte[]?>? _reply;
  private readonly bool _closeAfterInitial;
  private readonly Queue<byte[]> _pending = new();
  private readonly SemaphoreSlim _available = new(0);
  private readonly object _gate = new();
  private ReadOnlyMemory<byte> _current;
  private bool _closed;

  /// <summary>Creates the stream.</summary>
  /// <param name="initial">Bytes the "reader" sends before the client writes anything.</param>
  /// <param name="reply">Maps each message the client writes to the reader's reply, or null for silence.</param>
  /// <param name="closeAfterInitial">Whether the reader hangs up once the initial bytes are consumed (the "in use" behaviour).</param>
  public FakeReaderStream(byte[] initial, Func<byte[], byte[]?>? reply = null, bool closeAfterInitial = false)
  {
    _reply = reply;
    _closeAfterInitial = closeAfterInitial;
    if (initial.Length > 0) Enqueue(initial);
    if (closeAfterInitial) MarkClosed();
  }

  /// <summary>Every message the client wrote, in order.</summary>
  public List<byte[]> Written { get; } = [];

  /// <inheritdoc/>
  public override bool CanRead => true;

  /// <inheritdoc/>
  public override bool CanSeek => false;

  /// <inheritdoc/>
  public override bool CanWrite => true;

  /// <inheritdoc/>
  public override long Length => throw new NotSupportedException();

  /// <inheritdoc/>
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  /// <summary>Makes the reader hang up: pending bytes stay readable, then reads return 0.</summary>
  public void MarkClosed()
  {
    lock (_gate)
    {
      _closed = true;
    }

    _available.Release();
  }

  /// <inheritdoc/>
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    while (true)
    {
      lock (_gate)
      {
        if (_current.IsEmpty && _pending.Count > 0) _current = _pending.Dequeue();
        if (!_current.IsEmpty)
        {
          var count = Math.Min(buffer.Length, _current.Length);
          _current[..count].CopyTo(buffer);
          _current = _current[count..];
          return count;
        }

        if (_closed) return 0;
      }

      await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count) =>
    ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  /// <inheritdoc/>
  public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
  {
    Write(buffer.Span);
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc/>
  public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

  /// <inheritdoc/>
  public override void Write(ReadOnlySpan<byte> buffer)
  {
    var message = buffer.ToArray();
    Written.Add(message);
    var reply = _reply?.Invoke(message);
    if (reply is { Length: > 0 }) Enqueue(reply);
  }

  /// <inheritdoc/>
  public override void Flush()
  {
  }

  /// <inheritdoc/>
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  /// <inheritdoc/>
  public override void SetLength(long value) => throw new NotSupportedException();

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    if (disposing) _available.Dispose();
    base.Dispose(disposing);
  }

  private void Enqueue(byte[] bytes)
  {
    lock (_gate)
    {
      _pending.Enqueue(bytes);
    }

    _available.Release();
  }
}
