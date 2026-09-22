using ReaderDetect.Llrp;

namespace ReaderDetect.Tests;

public class LlrpProbeTests
{
  private static readonly byte[] NotificationSuccess =
    TestBytes.Hex("043f0000002000000000" + "00f600160080000c00065c150675cca3010000060000");

  private static readonly byte[] NotificationInUse =
    LlrpCodec.Encode(LlrpMessageType.ReaderEventNotification, 0, TestBytes.Hex("00f6000a 0100 0006 0002"));

  private static readonly byte[] CapabilitiesResponse = LlrpCodec.Encode(
    LlrpMessageType.GetReaderCapabilitiesResponse,
    1,
    TestBytes.Hex("011f 0008 0000 0000" + "0089 001b 0002 c000 0000651a 001e8869 0009 372e362e332e323430"));

  private static byte[]? ReaderReplies(byte[] written)
  {
    Assert.True(LlrpCodec.TryParseHeader(written, out var header));
    return header.Type switch
    {
      LlrpMessageType.GetReaderCapabilities => CapabilitiesResponse,
      LlrpMessageType.CloseConnection => LlrpCodec.Encode(LlrpMessageType.CloseConnectionResponse, header.MessageId),
      _ => null,
    };
  }

  [Fact]
  public async Task FreeReaderReportsCapabilitiesAndIsClosedCleanly()
  {
    var stream = new FakeReaderStream(NotificationSuccess, ReaderReplies);
    var result = await new LlrpProbe(TimeSpan.FromSeconds(2)).ProbeAsync(stream);

    Assert.Equal(LlrpStatus.Free, result.Status);
    Assert.Equal(ConnectionAttemptStatus.Success, result.ConnectionAttempt);
    Assert.Null(result.Error);
    Assert.NotNull(result.Capabilities);
    Assert.Equal(25882u, result.Capabilities.ManufacturerPen);
    Assert.Equal(2001001u, result.Capabilities.ModelCode);
    Assert.Equal("7.6.3.240", result.Capabilities.FirmwareVersion);

    Assert.Equal(2, stream.Written.Count);
    Assert.Equal(LlrpCodec.GetReaderCapabilities(1), stream.Written[0]);
    Assert.Equal(LlrpCodec.CloseConnection(2), stream.Written[1]);
  }

  [Fact]
  public async Task BusyReaderIsReportedInUseWithoutSendingAnything()
  {
    var stream = new FakeReaderStream(NotificationInUse, closeAfterInitial: true);
    var result = await new LlrpProbe(TimeSpan.FromSeconds(2)).ProbeAsync(stream);

    Assert.Equal(LlrpStatus.InUse, result.Status);
    Assert.Equal(ConnectionAttemptStatus.FailedClientInitiatedConnectionExists, result.ConnectionAttempt);
    Assert.Null(result.Capabilities);
    Assert.Empty(stream.Written);
  }

  [Fact]
  public async Task NonLlrpServerIsNoLlrp()
  {
    var stream = new FakeReaderStream("HTTP/1.1 401 Unauthorized\r\n\r\n"u8.ToArray(), closeAfterInitial: true);
    var result = await new LlrpProbe(TimeSpan.FromSeconds(2)).ProbeAsync(stream);
    Assert.Equal(LlrpStatus.NoLlrp, result.Status);
    Assert.Empty(stream.Written);
  }

  [Fact]
  public async Task ImmediateHangupIsNoLlrp()
  {
    var stream = new FakeReaderStream([], closeAfterInitial: true);
    var result = await new LlrpProbe(TimeSpan.FromSeconds(2)).ProbeAsync(stream);
    Assert.Equal(LlrpStatus.NoLlrp, result.Status);
  }

  [Fact]
  public async Task SilenceTimesOutAsNoLlrp()
  {
    var stream = new FakeReaderStream([]);
    var started = DateTime.UtcNow;
    var result = await new LlrpProbe(TimeSpan.FromMilliseconds(200)).ProbeAsync(stream);
    Assert.Equal(LlrpStatus.NoLlrp, result.Status);
    Assert.Contains("timeout", result.Error, StringComparison.Ordinal);
    Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
  }

  [Fact]
  public async Task KeepaliveBeforeTheNotificationIsAcknowledged()
  {
    var keepalive = LlrpCodec.Encode(LlrpMessageType.Keepalive, 77);
    var stream = new FakeReaderStream([.. keepalive, .. NotificationSuccess], ReaderReplies);
    var result = await new LlrpProbe(TimeSpan.FromSeconds(2)).ProbeAsync(stream);

    Assert.Equal(LlrpStatus.Free, result.Status);
    Assert.Equal(LlrpCodec.KeepaliveAck(77), stream.Written[0]);
    Assert.Equal(LlrpCodec.GetReaderCapabilities(1), stream.Written[1]);
  }

  [Fact]
  public async Task CallerCancellationPropagates()
  {
    var stream = new FakeReaderStream([]);
    using var cts = new CancellationTokenSource(50);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LlrpProbe(TimeSpan.FromSeconds(5)).ProbeAsync(stream, cts.Token));
  }
}
