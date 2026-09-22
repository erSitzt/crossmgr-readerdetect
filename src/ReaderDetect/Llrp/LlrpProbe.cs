using System.Net;
using System.Net.Sockets;

namespace ReaderDetect.Llrp;

/// <summary>
/// The identify handshake: connect, read the reader's connection-attempt
/// event, and if we got the slot ask for capabilities and close again. It
/// holds the LLRP connection for roughly 100 ms, sends nothing that changes
/// reader state, and backs off immediately when another client is connected.
/// </summary>
public sealed class LlrpProbe
{
  private static readonly TimeSpan CloseGrace = TimeSpan.FromMilliseconds(500);

  private readonly TimeSpan _timeout;
  private readonly Action<string>? _log;

  /// <summary>Creates a probe. The timeout bounds the whole handshake, connect included.</summary>
  public LlrpProbe(TimeSpan? timeout = null, Action<string>? log = null)
  {
    _timeout = timeout ?? TimeSpan.FromSeconds(3);
    _log = log;
  }

  /// <summary>Connects to <paramref name="ip"/>:<paramref name="port"/> and runs the handshake.</summary>
  public async Task<LlrpProbeResult> ProbeAsync(IPAddress ip, int port = 5084, CancellationToken ct = default)
  {
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(_timeout);
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    try
    {
      await socket.ConnectAsync(new IPEndPoint(ip, port), cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return new LlrpProbeResult(LlrpStatus.Unknown, null, null, "connect timeout");
    }
    catch (SocketException ex)
    {
      return new LlrpProbeResult(LlrpStatus.Unknown, null, null, $"connect failed: {ex.SocketErrorCode}");
    }

    await using var stream = new NetworkStream(socket, ownsSocket: false);
    return await ProbeAsync(stream, cts.Token, ct).ConfigureAwait(false);
  }

  /// <summary>Runs the handshake over an already-open duplex stream (testable with <see cref="FakeReaderStream"/>).</summary>
  public async Task<LlrpProbeResult> ProbeAsync(Stream stream, CancellationToken ct = default)
  {
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(_timeout);
    return await ProbeAsync(stream, cts.Token, ct).ConfigureAwait(false);
  }

  private async Task<LlrpProbeResult> ProbeAsync(Stream stream, CancellationToken token, CancellationToken caller)
  {
    try
    {
      ConnectionAttemptStatus? attempt = null;
      while (attempt is null)
      {
        var message = await ReadMessageAsync(stream, token).ConfigureAwait(false);
        if (message is null) return NoLlrp("not an LLRP header");
        switch (message.Value.Header.Type)
        {
          case LlrpMessageType.ReaderEventNotification:
            attempt = LlrpCodec.ParseConnectionAttempt(message.Value.Payload);
            break;
          case LlrpMessageType.Keepalive:
            await stream.WriteAsync(LlrpCodec.KeepaliveAck(message.Value.Header.MessageId), token).ConfigureAwait(false);
            break;
        }
      }

      _log?.Invoke($"llrp: connection attempt {attempt}");
      if (attempt != ConnectionAttemptStatus.Success)
      {
        // The reader closes on us now; do not send anything into a dying connection.
        return new LlrpProbeResult(LlrpStatus.InUse, attempt, null, null);
      }

      await stream.WriteAsync(LlrpCodec.GetReaderCapabilities(1), token).ConfigureAwait(false);
      GeneralDeviceCapabilities? capabilities = null;
      string? error = null;
      while (true)
      {
        var message = await ReadMessageAsync(stream, token).ConfigureAwait(false);
        if (message is null)
        {
          error = "reader stopped speaking LLRP mid-handshake";
          break;
        }

        if (message.Value.Header.Type == LlrpMessageType.GetReaderCapabilitiesResponse)
        {
          LlrpCodec.TryParseCapabilities(message.Value.Payload, out capabilities, out _, out error);
          break;
        }

        if (message.Value.Header.Type == LlrpMessageType.Keepalive)
        {
          await stream.WriteAsync(LlrpCodec.KeepaliveAck(message.Value.Header.MessageId), token).ConfigureAwait(false);
        }
      }

      await CloseQuietlyAsync(stream, token).ConfigureAwait(false);
      return new LlrpProbeResult(LlrpStatus.Free, attempt, capabilities, error);
    }
    catch (OperationCanceledException) when (!caller.IsCancellationRequested)
    {
      return NoLlrp("timeout waiting for LLRP handshake");
    }
    catch (EndOfStreamException)
    {
      return NoLlrp("connection closed during handshake");
    }
    catch (IOException ex)
    {
      return NoLlrp(ex.Message);
    }
  }

  private async Task CloseQuietlyAsync(Stream stream, CancellationToken token)
  {
    try
    {
      await stream.WriteAsync(LlrpCodec.CloseConnection(2), token).ConfigureAwait(false);
      using var grace = CancellationTokenSource.CreateLinkedTokenSource(token);
      grace.CancelAfter(CloseGrace);
      while (true)
      {
        var message = await ReadMessageAsync(stream, grace.Token).ConfigureAwait(false);
        if (message is null || message.Value.Header.Type == LlrpMessageType.CloseConnectionResponse) break;
      }
    }
    catch (Exception ex) when (ex is OperationCanceledException or IOException)
    {
      // The reader may simply drop the socket; we asked for nothing more.
      _log?.Invoke($"llrp: close not acknowledged ({ex.GetType().Name})");
    }
  }

  private static async Task<(LlrpHeader Header, ReadOnlyMemory<byte> Payload)?> ReadMessageAsync(Stream stream, CancellationToken token)
  {
    var headerBytes = new byte[LlrpCodec.HeaderLength];
    await stream.ReadExactlyAsync(headerBytes, token).ConfigureAwait(false);
    if (!LlrpCodec.TryParseHeader(headerBytes, out var header)) return null;
    var payload = new byte[header.PayloadLength];
    await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
    return (header, payload);
  }

  private LlrpProbeResult NoLlrp(string reason)
  {
    _log?.Invoke($"llrp: {reason}");
    return new LlrpProbeResult(LlrpStatus.NoLlrp, null, null, reason);
  }
}
