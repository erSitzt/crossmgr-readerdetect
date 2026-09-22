using ReaderDetect.Llrp;

namespace ReaderDetect.Tests;

public class LlrpCodecTests
{
  // Captured from a Speedway R220 (Octane 7.6.3.240) right after connect:
  // READER_EVENT_NOTIFICATION -> ReaderEventNotificationData -> UTCTimestamp + ConnectionAttemptEvent(Success).
  private const string CapturedNotificationPayload = "00f600160080000c00065c150675cca3010000060000";
  private const string CapturedNotificationHeader = "043f0000002000000000";

  // Hand-built GET_READER_CAPABILITIES_RESPONSE payload from the values the R220 reports:
  // LLRPStatus(0) + GeneralDeviceCapabilities(2 antennas, flags C000, PEN 25882, model 2001001, fw "7.6.3.240")
  // followed by one nested parameter (GPIOCapabilities) the parser must skip.
  private const string CapabilitiesPayload =
    "011f 0008 0000 0000" +
    "0089 001b 0002 c000 0000651a 001e8869 0009 372e362e332e323430" +
    "008d 0008 0004 0004";

  [Fact]
  public void GetReaderCapabilitiesIsTheExactElevenBytes()
  {
    Assert.Equal("0401" + "0000000b" + "00000001" + "01", TestBytes.ToHex(LlrpCodec.GetReaderCapabilities(1)));
  }

  [Fact]
  public void CloseConnectionIsHeaderOnly()
  {
    Assert.Equal("040e" + "0000000a" + "00000002", TestBytes.ToHex(LlrpCodec.CloseConnection(2)));
  }

  [Fact]
  public void HeaderRoundTrips()
  {
    var bytes = LlrpCodec.Encode(LlrpMessageType.KeepaliveAck, 0xDEADBEEF, [1, 2, 3]);
    Assert.True(LlrpCodec.TryParseHeader(bytes, out var header));
    Assert.Equal(1, header.Version);
    Assert.Equal(LlrpMessageType.KeepaliveAck, header.Type);
    Assert.Equal(13u, header.Length);
    Assert.Equal(3, header.PayloadLength);
    Assert.Equal(0xDEADBEEFu, header.MessageId);
  }

  [Theory]
  [InlineData("0401000000")]              // too short
  [InlineData("08010000000a00000001")]    // version 2
  [InlineData("e4010000000a00000001")]    // reserved bits set
  [InlineData("04010000000900000001")]    // length below header size
  [InlineData("0401ffffffff00000001")]    // absurd length
  [InlineData("485454502f312e3120343031")] // "HTTP/1.1 401"
  public void HeaderRejectsNonLlrp(string hex)
  {
    Assert.False(LlrpCodec.TryParseHeader(TestBytes.Hex(hex), out _));
  }

  [Fact]
  public void CapturedNotificationParsesAsSuccess()
  {
    var message = TestBytes.Hex(CapturedNotificationHeader + CapturedNotificationPayload);
    Assert.True(LlrpCodec.TryParseHeader(message, out var header));
    Assert.Equal(LlrpMessageType.ReaderEventNotification, header.Type);
    Assert.Equal(32u, header.Length);

    var payload = message.AsMemory(LlrpCodec.HeaderLength);
    Assert.Equal(ConnectionAttemptStatus.Success, LlrpCodec.ParseConnectionAttempt(payload));
    Assert.Equal(0x00065c150675cca3UL, LlrpCodec.ParseUtcTimestamp(payload));
  }

  [Fact]
  public void NotificationWithBusyStatusParsesAsInUse()
  {
    var payload = TestBytes.Hex("00f6000a 0100 0006 0002");
    Assert.Equal(ConnectionAttemptStatus.FailedClientInitiatedConnectionExists, LlrpCodec.ParseConnectionAttempt(payload));
  }

  [Fact]
  public void NotificationWithoutConnectionAttemptIsNull()
  {
    var payload = TestBytes.Hex("00f6 0010 0080 000c 00065c150675cca3");
    Assert.Null(LlrpCodec.ParseConnectionAttempt(payload));
  }

  [Fact]
  public void CapabilitiesParseTheR220Values()
  {
    Assert.True(LlrpCodec.TryParseCapabilities(TestBytes.Hex(CapabilitiesPayload), out var caps, out var status, out var error));
    Assert.Null(error);
    Assert.Equal(0, status);
    Assert.NotNull(caps);
    Assert.Equal(2, caps.MaxAntennas);
    Assert.True(caps.CanSetAntennaProperties);
    Assert.True(caps.HasUtcClock);
    Assert.Equal(25882u, caps.ManufacturerPen);
    Assert.Equal(2001001u, caps.ModelCode);
    Assert.Equal("7.6.3.240", caps.FirmwareVersion);
  }

  [Fact]
  public void CapabilitiesWithErrorStatusFail()
  {
    var payload = TestBytes.Hex("011f 000c 0065 0004 6f6f7073");
    Assert.False(LlrpCodec.TryParseCapabilities(payload, out var caps, out var status, out var error));
    Assert.Null(caps);
    Assert.Equal(101, status);
    Assert.Equal("LLRPStatus 101: oops", error);
  }

  [Fact]
  public void CapabilitiesMissingFail()
  {
    Assert.False(LlrpCodec.TryParseCapabilities(TestBytes.Hex("011f 0008 0000 0000"), out _, out _, out var error));
    Assert.Equal("no GeneralDeviceCapabilities in response", error);
  }

  [Fact]
  public void TruncatedFirmwareStringFails()
  {
    var payload = TestBytes.Hex("0089 0014 0002 c000 0000651a 001e8869 0009 372e36");
    Assert.False(LlrpCodec.TryParseCapabilities(payload, out _, out _, out var error));
    Assert.Contains("truncated", error, StringComparison.Ordinal);
  }

  [Fact]
  public void TvParameterStopsTheWalkWithoutThrowing()
  {
    var parameters = LlrpCodec.ParseParameters(TestBytes.Hex("011f 0008 0000 0000 8a 01 02"));
    Assert.Single(parameters);
    Assert.Equal(287, parameters[0].Type);
  }

  [Fact]
  public void BadLengthStopsTheWalk()
  {
    Assert.Empty(LlrpCodec.ParseParameters(TestBytes.Hex("011f 0002")));
    Assert.Empty(LlrpCodec.ParseParameters(TestBytes.Hex("011f 00ff 00")));
  }
}
