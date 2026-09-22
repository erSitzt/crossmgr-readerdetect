using System.Net;
using System.Xml;
using System.Xml.Linq;
using ReaderDetect.Network;

namespace ReaderDetect.WsDiscovery;

/// <summary>
/// Builds a typeless WS-Discovery Probe and reads ProbeMatch replies. Zebra FX
/// readers implement WS-Discovery (RDMP, ISO 24791-3); which namespace
/// revision their firmware speaks is not documented, so callers send both and
/// this parser ignores namespaces entirely.
/// </summary>
public static class WsDiscoveryCodec
{
  /// <summary>The Probe envelope. <paramref name="oasis2009"/> selects the OASIS 2009/01 namespaces over the 2005/04 draft ones.</summary>
  public static string BuildProbe(Guid messageId, bool oasis2009 = false)
  {
    var addressing = oasis2009 ? "http://www.w3.org/2005/08/addressing" : "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    var discovery = oasis2009 ? "http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01" : "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    var to = oasis2009 ? "urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01" : "urn:schemas-xmlsoap-org:ws:2005:04:discovery";
    return
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
      $"<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"{addressing}\" xmlns:d=\"{discovery}\">" +
      "<s:Header>" +
      $"<a:Action>{discovery}/Probe</a:Action>" +
      $"<a:MessageID>urn:uuid:{messageId}</a:MessageID>" +
      $"<a:To>{to}</a:To>" +
      "</s:Header>" +
      "<s:Body><d:Probe/></s:Body>" +
      "</s:Envelope>";
  }

  /// <summary>Every ProbeMatch in a reply; empty for anything that is not one.</summary>
  public static IReadOnlyList<WsDiscoveryMatch> ParseProbeMatches(string xml, IPAddress sender, NetworkInterfaceInfo? nic)
  {
    XDocument document;
    try
    {
      using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
      document = XDocument.Load(reader);
    }
    catch (XmlException)
    {
      return [];
    }

    var matches = new List<WsDiscoveryMatch>();
    foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "ProbeMatch"))
    {
      var endpoint = element.Elements().FirstOrDefault(e => e.Name.LocalName == "EndpointReference")?
        .Descendants().FirstOrDefault(e => e.Name.LocalName == "Address")?.Value.Trim();
      matches.Add(new WsDiscoveryMatch(
        sender,
        string.IsNullOrEmpty(endpoint) ? null : endpoint,
        Words(element, "Types"),
        Words(element, "Scopes"),
        Words(element, "XAddrs"),
        nic));
    }

    return matches;
  }

  private static IReadOnlyList<string> Words(XElement match, string localName) =>
    match.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value
      .Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries) ?? [];
}
