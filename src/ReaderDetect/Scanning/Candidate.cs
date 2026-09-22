using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Llrp;
using ReaderDetect.Network;

namespace ReaderDetect.Scanning;

/// <summary>
/// Mutable, lock-protected evidence for one host. Discovery mechanisms report
/// concurrently and a host can be found by several of them, so this is where
/// their findings meet before the classifier sees a snapshot.
/// </summary>
internal sealed class Candidate
{
  private readonly object _gate = new();
  private DiscoverySources _sources;
  private LlrpProbeResult? _llrp;
  private PhysicalAddress? _mac;
  private string? _reverseName;
  private HttpFingerprint? _http;
  private string? _mdnsHost;
  private IReadOnlyDictionary<string, string>? _mdnsTxt;
  private string? _wsdHost;
  private string? _confirmedExpectedHostname;
  private NetworkInterfaceInfo? _interface;
  private bool _queued;

  public Candidate(IPAddress ip)
  {
    Ip = ip;
  }

  public IPAddress Ip { get; }

  /// <summary>Marks the candidate as queued for probing; true only the first time.</summary>
  public bool TryMarkQueued()
  {
    lock (_gate)
    {
      if (_queued) return false;
      _queued = true;
      return true;
    }
  }

  public void AddSource(DiscoverySources source, NetworkInterfaceInfo? nic)
  {
    lock (_gate)
    {
      _sources |= source;
      _interface ??= nic;
    }
  }

  public void SetMdns(string? host, IReadOnlyDictionary<string, string>? txt, PhysicalAddress? mac)
  {
    lock (_gate)
    {
      _sources |= DiscoverySources.Mdns;
      _mdnsHost ??= host;
      _mdnsTxt ??= txt;
      _mac ??= mac;
    }
  }

  public void SetWsDiscovery(string? host)
  {
    lock (_gate)
    {
      _sources |= DiscoverySources.WsDiscovery;
      _wsdHost ??= host;
    }
  }

  public void SetLlrp(LlrpProbeResult result)
  {
    lock (_gate)
    {
      _llrp = result;
    }
  }

  public void SetMac(PhysicalAddress? mac)
  {
    lock (_gate)
    {
      if (mac is null) return;
      _mac ??= mac;
      _sources |= DiscoverySources.Arp;
    }
  }

  public void SetReverseName(string? name)
  {
    lock (_gate)
    {
      if (name is null) return;
      _reverseName = name;
      _sources |= DiscoverySources.ReverseDns;
    }
  }

  public void SetHttp(HttpFingerprint? fingerprint)
  {
    lock (_gate)
    {
      if (fingerprint is null) return;
      _http = fingerprint;
      _sources |= DiscoverySources.Http;
    }
  }

  public void SetConfirmedExpectedHostname(string hostname)
  {
    lock (_gate)
    {
      _confirmedExpectedHostname = hostname;
    }
  }

  public PhysicalAddress? Mac
  {
    get
    {
      lock (_gate)
      {
        return _mac;
      }
    }
  }

  public string? MdnsHost
  {
    get
    {
      lock (_gate)
      {
        return _mdnsHost;
      }
    }
  }

  public NetworkInterfaceInfo? Interface
  {
    get
    {
      lock (_gate)
      {
        return _interface;
      }
    }
  }

  public CandidateEvidence Snapshot()
  {
    lock (_gate)
    {
      return new CandidateEvidence(Ip, _sources, _llrp, _mac, _reverseName, _http, _mdnsHost, _mdnsTxt, _wsdHost,
        _confirmedExpectedHostname, _interface);
    }
  }
}
