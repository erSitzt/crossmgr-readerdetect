namespace ReaderDetect.Gui;

/// <summary>A grid row: plain strings so the DataGridView needs no converters.</summary>
internal sealed class ReaderRow
{
  public ReaderRow(ReaderInfo info)
  {
    Info = info;
  }

  public ReaderInfo Info { get; private set; }

  public string Ip => Info.Ip.ToString();

  public string Hostname => Info.Hostname ?? "";

  public string Vendor => Info.Vendor.ToString();

  public string Model => Info.Model ?? "";

  public string Firmware => Info.Firmware ?? "";

  public string Mac => Info.MacText;

  public string Llrp => Info.LlrpStatus switch
  {
    LlrpStatus.Free => "free",
    LlrpStatus.InUse => "in use",
    LlrpStatus.NoLlrp => "none",
    _ => "?",
  };

  public string FoundVia => string.Join("+", Sources());

  public string Confidence => Info.Confidence.ToString().ToLowerInvariant();

  public string Note => Info.Note ?? "";

  public void Update(ReaderInfo info) => Info = info;

  private IEnumerable<string> Sources()
  {
    if (Info.Sources.HasFlag(DiscoverySources.Mdns)) yield return "mdns";
    if (Info.Sources.HasFlag(DiscoverySources.WsDiscovery)) yield return "wsd";
    if (Info.Sources.HasFlag(DiscoverySources.PortSweep)) yield return "sweep";
    if (Info.Sources.HasFlag(DiscoverySources.Manual)) yield return "manual";
  }
}
