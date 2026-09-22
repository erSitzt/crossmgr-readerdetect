using System.Net;
using ReaderDetect.Network;

namespace ReaderDetect.WsDiscovery;

/// <summary>One ProbeMatch from a WS-Discovery responder.</summary>
/// <param name="Sender">Address the reply came from; the candidate address.</param>
/// <param name="EndpointAddress">The endpoint reference (usually a urn:uuid).</param>
/// <param name="Types">Advertised type QNames.</param>
/// <param name="Scopes">Advertised scopes.</param>
/// <param name="XAddrs">Transport addresses, e.g. <c>http://FX7500ED7A5B:80/…</c>.</param>
/// <param name="Interface">Local interface the reply arrived on.</param>
public sealed record WsDiscoveryMatch(
  IPAddress Sender,
  string? EndpointAddress,
  IReadOnlyList<string> Types,
  IReadOnlyList<string> Scopes,
  IReadOnlyList<string> XAddrs,
  NetworkInterfaceInfo? Interface)
{
  /// <summary>The hostname in the first XAddr that uses a name rather than an IP literal; Zebra readers put their hostname there.</summary>
  public string? HostFromXAddrs
  {
    get
    {
      foreach (var xaddr in XAddrs)
      {
        if (!Uri.TryCreate(xaddr, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns || uri.Host.Length == 0) continue;
        // Uri lower-cases the host; the reader's own spelling (FX7500ED7A5B) is what users see on the label.
        var start = xaddr.IndexOf("://", StringComparison.Ordinal) + 3;
        var end = xaddr.IndexOfAny([':', '/', '?', '#'], start);
        var original = end < 0 ? xaddr[start..] : xaddr[start..end];
        return string.Equals(original, uri.Host, StringComparison.OrdinalIgnoreCase) ? original : uri.Host;
      }

      return null;
    }
  }
}
