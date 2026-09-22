using System.Globalization;
using ReaderDetect.Llrp;
using ReaderDetect.Network;
using ReaderDetect.Vendors;

namespace ReaderDetect.Scanning;

/// <summary>
/// Turns evidence into a <see cref="ReaderInfo"/>, or nothing when the host
/// is just a PC that answered a broadcast. Pure, so every rule is a test case.
/// </summary>
internal static class ReaderClassifier
{
  public static ReaderInfo? Classify(CandidateEvidence e)
  {
    var caps = e.Llrp?.Capabilities;
    var attempt = e.Llrp?.ConnectionAttempt;

    var penVendor = caps is null ? (ReaderVendor?)null : ReaderModels.VendorFromPen(caps.ManufacturerPen);
    var txtPen = ParsePen(e.MdnsTxt);
    var txtVendor = txtPen is null ? (ReaderVendor?)null : ReaderModels.VendorFromPen(txtPen.Value);
    var hintVendor =
      NonUnknown(txtVendor) ??
      OuiTable.Lookup(e.Mac) ??
      HostnameHints.VendorFromHostname(e.MdnsHost) ??
      HostnameHints.VendorFromHostname(e.WsdHost) ??
      HostnameHints.VendorFromHostname(e.ReverseName) ??
      HttpFingerprinter.VendorHint(e.Http);
    var vendor = NonUnknown(penVendor) ?? hintVendor ?? ReaderVendor.Unknown;

    var hostname = Pick(e.MdnsHost, e.WsdHost, e.ReverseName, e.ConfirmedExpectedHostname);
    var model = caps is not null
      ? ReaderModels.Describe(vendor, caps.ModelCode)
      : HostnameHints.ModelFromHostname(e.MdnsHost) ?? HostnameHints.ModelFromHostname(e.WsdHost) ?? HostnameHints.ModelFromHostname(e.ReverseName);

    var llrpStatus = caps is not null || attempt == ConnectionAttemptStatus.Success
      ? LlrpStatus.Free
      : attempt is not null
        ? LlrpStatus.InUse
        : e.Llrp?.Status ?? LlrpStatus.Unknown;

    var mdnsAdvertised = e.Sources.HasFlag(DiscoverySources.Mdns);
    Confidence confidence;
    if (caps is not null) confidence = Confidence.Confirmed;
    else if (attempt is not null) confidence = Confidence.Likely;
    else if (mdnsAdvertised) confidence = Confidence.Likely;
    else if (hintVendor is not null) confidence = Confidence.Possible;
    else return null;

    var note = attempt switch
    {
      ConnectionAttemptStatus.FailedClientInitiatedConnectionExists => "LLRP in use by another client",
      ConnectionAttemptStatus.FailedReaderInitiatedConnectionExists => "reader-initiated LLRP mode; connected elsewhere",
      ConnectionAttemptStatus.FailedOther or ConnectionAttemptStatus.AnotherConnectionAttempted => $"LLRP refused: {attempt}",
      _ => null,
    };
    if (note is null && caps is null && e.Llrp?.Error is { } error && llrpStatus != LlrpStatus.Unknown) note = error;

    return new ReaderInfo(e.Ip, e.Mac, hostname, vendor, model, caps?.FirmwareVersion, llrpStatus, e.Sources, confidence)
    {
      ManufacturerPen = caps?.ManufacturerPen ?? txtPen,
      ModelCode = caps?.ModelCode,
      ExpectedHostname = e.Mac is null ? null : HostnameHints.ExpectedHostnames(e.Mac).FirstOrDefault(),
      Interface = e.Interface,
      Note = note,
    };
  }

  private static ReaderVendor? NonUnknown(ReaderVendor? vendor) => vendor is null or ReaderVendor.Unknown ? null : vendor;

  private static string? Pick(params string?[] names)
  {
    foreach (var name in names)
    {
      if (!string.IsNullOrWhiteSpace(name)) return HostnameHints.StripLocal(name);
    }

    return null;
  }

  private static uint? ParsePen(IReadOnlyDictionary<string, string>? txt) =>
    txt is not null && txt.TryGetValue("pen", out var text) &&
    uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pen)
      ? pen
      : null;
}
