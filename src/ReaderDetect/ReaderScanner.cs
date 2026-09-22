using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using ReaderDetect.Llrp;
using ReaderDetect.Network;
using ReaderDetect.Scanning;
using ReaderDetect.Vendors;

namespace ReaderDetect;

/// <summary>
/// Runs the whole discovery: passive advertisements and an active sweep feed
/// candidates into an LLRP identify queue; every candidate is then enriched
/// (MAC, names, web banner) and classified. Findings stream out through the
/// progress callback so a UI can fill its table while the sweep is still running.
/// </summary>
public sealed class ReaderScanner
{
  private readonly IArpResolver _arp;

  /// <summary>Creates a scanner; the ARP resolver defaults to the platform one.</summary>
  public ReaderScanner(ScanOptions? options = null, IArpResolver? arp = null)
  {
    Options = options ?? new ScanOptions();
    _arp = arp ?? ArpResolver.CreateForCurrentPlatform();
  }

  /// <summary>The options this scanner runs with.</summary>
  public ScanOptions Options { get; }

  /// <summary>The local interfaces a scan would cover.</summary>
  public static IReadOnlyList<NetworkInterfaceInfo> Interfaces(bool includeVirtual = false) =>
    NetworkInterfaces.Enumerate(includeVirtual);

  /// <summary>Scans the selected interfaces and returns every reader found.</summary>
  public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
  {
    var stopwatch = Stopwatch.StartNew();
    var warnings = new List<string>();
    var log = Options.Log;

    progress?.Report(new ScanProgress(ScanPhase.Interfaces, 0, 0, 0, "enumerating interfaces", null));
    var nics = Options.Interfaces ?? NetworkInterfaces.Enumerate(Options.IncludeVirtualInterfaces);
    if (nics.Count == 0)
    {
      warnings.Add("no usable network interface (up, Ethernet/Wi-Fi, with an IPv4 address)");
      return new ScanResult([], nics, warnings, stopwatch.Elapsed);
    }

    var targets = Targets(nics, warnings);
    var hosts = targets.SelectMany(t => t.Hosts).ToList();
    log?.Invoke($"scan: {nics.Count} interface(s), {targets.Count} subnet(s), {hosts.Count} host(s) to sweep");

    var candidates = new ConcurrentDictionary<IPAddress, Candidate>();
    var queue = Channel.CreateUnbounded<Candidate>();
    var found = new ConcurrentDictionary<IPAddress, ReaderInfo>();

    void Enqueue(Candidate candidate)
    {
      if (candidate.TryMarkQueued()) queue.Writer.TryWrite(candidate);
    }

    Candidate CandidateFor(IPAddress ip, DiscoverySources source)
    {
      var candidate = candidates.GetOrAdd(ip, static address => new Candidate(address));
      candidate.AddSource(source, NicFor(nics, ip));
      return candidate;
    }

    var prober = Task.Run(() => Parallel.ForEachAsync(
      queue.Reader.ReadAllAsync(ct),
      new ParallelOptions { MaxDegreeOfParallelism = Options.ProbeParallelism, CancellationToken = ct },
      async (candidate, token) =>
      {
        await EnrichAsync(candidate, token).ConfigureAwait(false);
        var info = ReaderClassifier.Classify(candidate.Snapshot());
        if (info is null)
        {
          log?.Invoke($"{candidate.Ip}: not a reader");
          return;
        }

        found[info.Ip] = info;
        progress?.Report(new ScanProgress(ScanPhase.LlrpProbe, 0, 0, found.Count, $"{info.Ip} {info.Vendor} {info.Model}", info));
      }), ct);

    var discovery = new List<Task>();
    if (Options.EnablePortSweep && hosts.Count > 0)
    {
      progress?.Report(new ScanProgress(ScanPhase.PortSweep, 0, hosts.Count, 0, "sweeping", null));
      var sweeper = new PortSweeper(Options.SweepParallelism, Options.ConnectTimeout);
      var scanned = new Progress<int>(done =>
        progress?.Report(new ScanProgress(ScanPhase.PortSweep, done, hosts.Count, found.Count, "sweeping", null)));
      discovery.Add(sweeper.SweepAsync(hosts, Options.LlrpPort,
        onOpen: ip =>
        {
          log?.Invoke($"{ip}: port {Options.LlrpPort} open");
          Enqueue(CandidateFor(ip, DiscoverySources.PortSweep));
        },
        scanned: scanned,
        ct: ct));
    }

    try
    {
      await Task.WhenAll(discovery).ConfigureAwait(false);
    }
    finally
    {
      queue.Writer.TryComplete();
    }

    await prober.ConfigureAwait(false);

    // A candidate may have gained evidence after it was classified (e.g. an
    // advertisement that arrived late), so classify everything once more.
    var readers = candidates.Values
      .Select(c => ReaderClassifier.Classify(c.Snapshot()))
      .Where(r => r is not null)
      .Select(r => r!)
      .OrderBy(r => nics.ToList().IndexOf(r.Interface!))
      .ThenBy(r => r.Ip, Comparer<IPAddress>.Create((a, b) => a.GetAddressBytes().AsSpan().SequenceCompareTo(b.GetAddressBytes())))
      .ToList();

    progress?.Report(new ScanProgress(ScanPhase.Done, 1, 1, readers.Count, "done", null));
    return new ScanResult(readers, nics, warnings, stopwatch.Elapsed);
  }

  /// <summary>Identifies and enriches a single address the user typed.</summary>
  public async Task<ReaderInfo> ProbeAsync(IPAddress ip, CancellationToken ct = default)
  {
    var candidate = new Candidate(ip);
    var nics = Options.Interfaces ?? NetworkInterfaces.Enumerate(Options.IncludeVirtualInterfaces);
    candidate.AddSource(DiscoverySources.Manual, NicFor(nics, ip));
    await EnrichAsync(candidate, ct).ConfigureAwait(false);
    var evidence = candidate.Snapshot();
    return ReaderClassifier.Classify(evidence) ?? new ReaderInfo(ip, evidence.Mac, evidence.ReverseName, ReaderVendor.Unknown, null, null,
      evidence.Llrp?.Status ?? LlrpStatus.Unknown, evidence.Sources, Confidence.Possible)
    {
      Interface = evidence.Interface,
      Note = evidence.Llrp?.Error ?? "no reader evidence",
    };
  }

  private async Task EnrichAsync(Candidate candidate, CancellationToken ct)
  {
    var ip = candidate.Ip;
    var log = Options.Log;
    var nic = candidate.Interface;

    var llrp = await new LlrpProbe(Options.LlrpProbeTimeout, log is null ? null : line => log($"{ip}: {line}"))
      .ProbeAsync(ip, Options.LlrpPort, ct).ConfigureAwait(false);
    candidate.SetLlrp(llrp);

    var tasks = new List<Task>();
    if (Options.EnableArp && candidate.Mac is null && nic is not null && nic.Subnet.Contains(ip))
    {
      tasks.Add(Task.Run(async () => candidate.SetMac(await _arp.ResolveAsync(ip, nic.Address, ct).ConfigureAwait(false)), ct));
    }

    if (Options.EnableReverseDns)
    {
      tasks.Add(Task.Run(async () => candidate.SetReverseName(await ReverseDns.LookupAsync(ip, Options.ReverseDnsTimeout, ct).ConfigureAwait(false)), ct));
    }

    if (Options.EnableHttpFingerprint && llrp.Capabilities is null)
    {
      tasks.Add(Task.Run(async () => candidate.SetHttp(await HttpFingerprinter.FetchAsync(ip, Options.HttpTimeout, ct).ConfigureAwait(false)), ct));
    }

    await Task.WhenAll(tasks).ConfigureAwait(false);

    // With the MAC known we can ask the OS resolver whether the factory
    // hostname points here, which confirms the name even without our own mDNS.
    if (candidate.Mac is { } mac && candidate.MdnsHost is null)
    {
      foreach (var expected in HostnameHints.ExpectedHostnames(mac))
      {
        if (await ReverseDns.ResolvesToAsync(expected + ".local", ip, Options.ReverseDnsTimeout, ct).ConfigureAwait(false))
        {
          candidate.SetConfirmedExpectedHostname(expected);
          break;
        }
      }
    }
  }

  private List<(IpSubnet Subnet, NetworkInterfaceInfo? Nic, List<IPAddress> Hosts)> Targets(
    IReadOnlyList<NetworkInterfaceInfo> nics,
    List<string> warnings)
  {
    var targets = new List<(IpSubnet, NetworkInterfaceInfo?, List<IPAddress>)>();
    var own = nics.Select(n => n.Address).ToHashSet();
    if (Options.ExplicitSubnets.Count > 0)
    {
      foreach (var subnet in Options.ExplicitSubnets)
      {
        var nic = nics.FirstOrDefault(n => subnet.Contains(n.Address));
        targets.Add((subnet, nic, HostsOf(subnet, nic?.Address, own, warnings)));
      }
    }
    else
    {
      foreach (var group in nics.GroupBy(n => n.Subnet))
      {
        var nic = group.First();
        targets.Add((group.Key, nic, HostsOf(group.Key, nic.Address, own, warnings)));
      }
    }

    return targets;
  }

  private List<IPAddress> HostsOf(IpSubnet subnet, IPAddress? anchor, HashSet<IPAddress> own, List<string> warnings)
  {
    var hosts = subnet.Hosts(Options.MaxHostsPerSubnet, anchor, out var capped).Where(h => !own.Contains(h)).ToList();
    if (capped)
    {
      warnings.Add($"{subnet} has {subnet.HostCount} hosts; sweeping only the {hosts.Count} around {anchor ?? subnet.Network} (use an explicit subnet to override)");
    }

    return hosts;
  }

  private static NetworkInterfaceInfo? NicFor(IReadOnlyList<NetworkInterfaceInfo> nics, IPAddress ip) =>
    nics.FirstOrDefault(n => n.Subnet.Contains(ip));
}
