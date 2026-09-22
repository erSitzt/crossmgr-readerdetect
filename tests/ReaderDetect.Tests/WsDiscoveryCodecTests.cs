using System.Net;
using ReaderDetect.WsDiscovery;

namespace ReaderDetect.Tests;

public class WsDiscoveryCodecTests
{
  private static readonly IPAddress Sender = IPAddress.Parse("192.168.68.60");

  private const string FxStyleMatch2005 = """
    <?xml version="1.0" encoding="utf-8"?>
    <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                   xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                   xmlns:wsd="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                   xmlns:rdmp="urn:iso:24791-3:rdmp">
      <soap:Header>
        <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</wsa:Action>
        <wsa:RelatesTo>urn:uuid:11111111-2222-3333-4444-555555555555</wsa:RelatesTo>
      </soap:Header>
      <soap:Body>
        <wsd:ProbeMatches>
          <wsd:ProbeMatch>
            <wsa:EndpointReference><wsa:Address>urn:uuid:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</wsa:Address></wsa:EndpointReference>
            <wsd:Types>rdmp:Reader</wsd:Types>
            <wsd:Scopes>ldap:///ou=readers</wsd:Scopes>
            <wsd:XAddrs>http://FX7500ED7A5B:80/rdmp https://FX7500ED7A5B:443/rdmp</wsd:XAddrs>
            <wsd:MetadataVersion>1</wsd:MetadataVersion>
          </wsd:ProbeMatch>
        </wsd:ProbeMatches>
      </soap:Body>
    </soap:Envelope>
    """;

  private const string WindowsPcMatch2009 = """
    <?xml version="1.0" encoding="utf-8"?>
    <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://www.w3.org/2005/08/addressing" xmlns:wsd="http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01" xmlns:wsdp="http://docs.oasis-open.org/ws-dd/ns/devprof/2009/01">
      <soap:Header><wsa:Action>http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01/ProbeMatches</wsa:Action></soap:Header>
      <soap:Body>
        <wsd:ProbeMatches>
          <wsd:ProbeMatch>
            <wsa:EndpointReference><wsa:Address>urn:uuid:12345678-0000-0000-0000-000000000001</wsa:Address></wsa:EndpointReference>
            <wsd:Types>wsdp:Device pub:Computer</wsd:Types>
            <wsd:XAddrs>http://192.168.68.60:5357/12345678-0000-0000-0000-000000000001</wsd:XAddrs>
            <wsd:MetadataVersion>3</wsd:MetadataVersion>
          </wsd:ProbeMatch>
          <wsd:ProbeMatch>
            <wsa:EndpointReference><wsa:Address>urn:uuid:12345678-0000-0000-0000-000000000002</wsa:Address></wsa:EndpointReference>
            <wsd:Types>wsdp:Device</wsd:Types>
            <wsd:XAddrs>http://printer.lan:80/</wsd:XAddrs>
          </wsd:ProbeMatch>
        </wsd:ProbeMatches>
      </soap:Body>
    </soap:Envelope>
    """;

  [Fact]
  public void ProbeCarriesTheRequiredHeaders()
  {
    var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    var probe2005 = WsDiscoveryCodec.BuildProbe(id);
    Assert.Contains("<a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>", probe2005, StringComparison.Ordinal);
    Assert.Contains("<a:MessageID>urn:uuid:11111111-2222-3333-4444-555555555555</a:MessageID>", probe2005, StringComparison.Ordinal);
    Assert.Contains("<a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>", probe2005, StringComparison.Ordinal);
    Assert.Contains("<d:Probe/>", probe2005, StringComparison.Ordinal);

    var probe2009 = WsDiscoveryCodec.BuildProbe(id, oasis2009: true);
    Assert.Contains("http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01/Probe", probe2009, StringComparison.Ordinal);
    Assert.Contains("urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01", probe2009, StringComparison.Ordinal);

    // Both must be well-formed XML.
    Assert.Empty(WsDiscoveryCodec.ParseProbeMatches(probe2005, Sender, null));
    Assert.Empty(WsDiscoveryCodec.ParseProbeMatches(probe2009, Sender, null));
  }

  [Fact]
  public void FxStyleMatchYieldsTheHostname()
  {
    var match = Assert.Single(WsDiscoveryCodec.ParseProbeMatches(FxStyleMatch2005, Sender, null));
    Assert.Equal(Sender, match.Sender);
    Assert.Equal("urn:uuid:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", match.EndpointAddress);
    Assert.Equal(["rdmp:Reader"], match.Types);
    Assert.Equal(["ldap:///ou=readers"], match.Scopes);
    Assert.Equal(2, match.XAddrs.Count);
    Assert.Equal("FX7500ED7A5B", match.HostFromXAddrs);
  }

  [Fact]
  public void OasisMatchesParseAndIpLiteralsGiveNoHostname()
  {
    var matches = WsDiscoveryCodec.ParseProbeMatches(WindowsPcMatch2009, Sender, null);
    Assert.Equal(2, matches.Count);
    Assert.Null(matches[0].HostFromXAddrs);
    Assert.Equal(["wsdp:Device", "pub:Computer"], matches[0].Types);
    Assert.Equal("printer.lan", matches[1].HostFromXAddrs);
  }

  [Theory]
  [InlineData("")]
  [InlineData("not xml at all")]
  [InlineData("<a><b></a>")]
  [InlineData("<?xml version=\"1.0\"?><Envelope><Body><Hello/></Body></Envelope>")]
  public void GarbageYieldsNothing(string xml) => Assert.Empty(WsDiscoveryCodec.ParseProbeMatches(xml, Sender, null));
}
