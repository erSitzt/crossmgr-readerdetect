using System.Net;

namespace ReaderDetect.Network;

/// <summary>What the web interface said in its first response.</summary>
/// <param name="StatusCode">HTTP status.</param>
/// <param name="Server">Server header.</param>
/// <param name="Realm">Basic-auth realm, if it asked for credentials.</param>
public sealed record HttpFingerprint(int StatusCode, string? Server, string? Realm);

/// <summary>
/// A low-weight vendor hint from port 80. Impinj Speedway readers answer with
/// thttpd and a Basic-auth challenge; it only matters when LLRP is busy and
/// we could not read the capabilities.
/// </summary>
public static class HttpFingerprinter
{
  private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
  {
    Timeout = Timeout.InfiniteTimeSpan,
  };

  /// <summary>GETs <c>http://ip/</c>; null when nothing answered in time.</summary>
  public static async Task<HttpFingerprint?> FetchAsync(IPAddress ip, TimeSpan timeout, CancellationToken ct)
  {
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{ip}/");
      using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
      var server = response.Headers.TryGetValues("Server", out var servers) ? string.Join(" ", servers) : null;
      var realm = response.Headers.WwwAuthenticate
        .Select(h => h.Parameter)
        .FirstOrDefault(p => p is not null && p.Contains("realm", StringComparison.OrdinalIgnoreCase));
      return new HttpFingerprint((int)response.StatusCode, server, realm);
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
    {
      return null;
    }
  }

  /// <summary>Vendor suggested by the banner, or null.</summary>
  public static ReaderVendor? VendorHint(HttpFingerprint? fingerprint)
  {
    if (fingerprint is null) return null;
    if (fingerprint.Server?.Contains("thttpd", StringComparison.OrdinalIgnoreCase) == true &&
        fingerprint.StatusCode == (int)HttpStatusCode.Unauthorized)
    {
      return ReaderVendor.Impinj;
    }

    return null;
  }
}
