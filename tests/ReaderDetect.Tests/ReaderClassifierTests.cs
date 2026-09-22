using System.Net;
using ReaderDetect.Llrp;
using ReaderDetect.Network;
using ReaderDetect.Scanning;

namespace ReaderDetect.Tests;

public class ReaderClassifierTests
{
  private static readonly IPAddress Ip = IPAddress.Parse("192.168.68.139");
  private static readonly GeneralDeviceCapabilities R220 = new(2, true, true, 25882, 2001001, "7.6.3.240");

  private static CandidateEvidence Evidence(
    DiscoverySources sources = DiscoverySources.PortSweep,
    LlrpProbeResult? llrp = null,
    string? mac = null,
    string? reverse = null,
    HttpFingerprint? http = null,
    string? mdnsHost = null,
    IReadOnlyDictionary<string, string>? txt = null,
    string? wsdHost = null,
    string? confirmed = null) =>
    new(Ip, sources, llrp, mac is null ? null : MacFormat.Parse(mac), reverse, http, mdnsHost, txt, wsdHost, confirmed, null);

  [Fact]
  public void CapabilitiesGiveConfirmed()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.Free, ConnectionAttemptStatus.Success, R220, null),
      mac: "00:16:25:12:59:43",
      mdnsHost: "SpeedwayR-12-59-43.local"));

    Assert.NotNull(info);
    Assert.Equal(Confidence.Confirmed, info.Confidence);
    Assert.Equal(ReaderVendor.Impinj, info.Vendor);
    Assert.Equal("Speedway R220", info.Model);
    Assert.Equal("7.6.3.240", info.Firmware);
    Assert.Equal(LlrpStatus.Free, info.LlrpStatus);
    Assert.Equal("SpeedwayR-12-59-43", info.Hostname);
    Assert.Equal("SpeedwayR-12-59-43", info.ExpectedHostname);
    Assert.Equal(2001001u, info.ModelCode);
    Assert.Null(info.Note);
    Assert.Equal("http://192.168.68.139/", info.WebUrl);
  }

  [Fact]
  public void BusyReaderWithOuiIsLikelyImpinjInUse()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.InUse, ConnectionAttemptStatus.FailedClientInitiatedConnectionExists, null, null),
      mac: "00:16:25:12:59:43"));

    Assert.NotNull(info);
    Assert.Equal(Confidence.Likely, info.Confidence);
    Assert.Equal(ReaderVendor.Impinj, info.Vendor);
    Assert.Equal(LlrpStatus.InUse, info.LlrpStatus);
    Assert.Equal("LLRP in use by another client", info.Note);
    Assert.Null(info.Model);
  }

  [Fact]
  public void HandshakeWithoutAnyHintIsLikelyUnknownVendor()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.InUse, ConnectionAttemptStatus.FailedOther, null, null)));
    Assert.NotNull(info);
    Assert.Equal(Confidence.Likely, info.Confidence);
    Assert.Equal(ReaderVendor.Unknown, info.Vendor);
  }

  [Fact]
  public void MdnsAdvertisementAloneIsLikelyAndTxtPenGivesVendor()
  {
    var info = ReaderClassifier.Classify(Evidence(
      sources: DiscoverySources.Mdns,
      llrp: new LlrpProbeResult(LlrpStatus.Unknown, null, null, "connect timeout"),
      mdnsHost: "SpeedwayR-12-59-43.local",
      txt: new Dictionary<string, string> { ["pen"] = "25882", ["vendor-version"] = "octane-7.6" }));

    Assert.NotNull(info);
    Assert.Equal(Confidence.Likely, info.Confidence);
    Assert.Equal(ReaderVendor.Impinj, info.Vendor);
    Assert.Equal(25882u, info.ManufacturerPen);
    Assert.Equal(LlrpStatus.Unknown, info.LlrpStatus);
  }

  [Fact]
  public void OuiOnlyIsPossible()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.NoLlrp, null, null, "not an LLRP header"),
      mac: "84:24:8D:FB:6C:10"));
    Assert.NotNull(info);
    Assert.Equal(Confidence.Possible, info.Confidence);
    Assert.Equal(ReaderVendor.Zebra, info.Vendor);
    Assert.Equal(LlrpStatus.NoLlrp, info.LlrpStatus);
    Assert.Equal("FX7500FB6C10", info.ExpectedHostname);
  }

  [Fact]
  public void ZebraHostnameFromWsDiscoveryGivesVendorAndModel()
  {
    var info = ReaderClassifier.Classify(Evidence(
      sources: DiscoverySources.WsDiscovery,
      llrp: new LlrpProbeResult(LlrpStatus.InUse, ConnectionAttemptStatus.FailedClientInitiatedConnectionExists, null, null),
      wsdHost: "FX9600FB6C10"));
    Assert.NotNull(info);
    Assert.Equal(ReaderVendor.Zebra, info.Vendor);
    Assert.Equal("FX9600", info.Model);
    Assert.Equal("FX9600FB6C10", info.Hostname);
  }

  [Fact]
  public void PcAnsweringWsDiscoveryIsDropped()
  {
    var info = ReaderClassifier.Classify(Evidence(
      sources: DiscoverySources.WsDiscovery,
      llrp: new LlrpProbeResult(LlrpStatus.Unknown, null, null, "connect failed: ConnectionRefused"),
      wsdHost: "DESKTOP-ABC123",
      reverse: "desktop-abc123.lan"));
    Assert.Null(info);
  }

  [Fact]
  public void RandomServiceOnTheLlrpPortIsDropped()
  {
    Assert.Null(ReaderClassifier.Classify(Evidence(llrp: new LlrpProbeResult(LlrpStatus.NoLlrp, null, null, "not an LLRP header"))));
  }

  [Fact]
  public void HttpBannerRaisesABusyReaderToImpinj()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.InUse, ConnectionAttemptStatus.FailedClientInitiatedConnectionExists, null, null),
      http: new HttpFingerprint(401, "thttpd/2.29", "realm=\".\"")));
    Assert.NotNull(info);
    Assert.Equal(ReaderVendor.Impinj, info.Vendor);
  }

  [Fact]
  public void ConfirmedExpectedHostnameIsUsedWhenNothingElseNamesIt()
  {
    var info = ReaderClassifier.Classify(Evidence(
      llrp: new LlrpProbeResult(LlrpStatus.Free, ConnectionAttemptStatus.Success, R220, null),
      mac: "00:16:25:12:59:43",
      confirmed: "SpeedwayR-12-59-43"));
    Assert.Equal("SpeedwayR-12-59-43", info!.Hostname);
  }
}
