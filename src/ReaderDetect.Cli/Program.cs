using System.Net;
using ReaderDetect.Network;

namespace ReaderDetect.Cli;

/// <summary>
/// Command-line front end. It exists so the discovery can be scripted and so
/// a volunteer can paste one line of output into a chat when something at the
/// track does not work.
/// </summary>
internal static class Program
{
  private static readonly string[] ValueOptions =
    ["--interface", "--subnet", "--timeout-ms", "--max-hosts", "--vendor", "--user", "--password", "--address", "--mask", "--gateway", "--dns", "--hostname"];

  private static int Main(string[] args)
  {
    if (args.Length == 0) return Scan(args);
    if (args[0] is "-h" or "--help" or "help") return Usage(0);
    var verbose = Args.Flag(args, "--verbose");

    try
    {
      return args[0].ToLowerInvariant() switch
      {
        "scan" => Scan(args),
        "interfaces" => Interfaces(args),
        "probe" => Probe(args),
        _ when args[0].StartsWith("--", StringComparison.Ordinal) => Scan(args),
        _ => Unknown(args[0]),
      };
    }
    catch (OperationCanceledException)
    {
      Console.Error.WriteLine("cancelled");
      return 2;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"failed: {ex.Message}");
      if (verbose) Console.Error.WriteLine(ex);
      return 2;
    }
  }

  private static int Unknown(string command)
  {
    Console.Error.WriteLine($"unknown command '{command}'");
    return Usage(2);
  }

  private static ScanOptions OptionsFrom(string[] args, out List<string> problems)
  {
    problems = [];
    var verbose = Args.Flag(args, "--verbose");
    var options = new ScanOptions
    {
      IncludeVirtualInterfaces = Args.Flag(args, "--include-virtual"),
      EnableMdns = !Args.Flag(args, "--no-mdns"),
      EnableWsDiscovery = !Args.Flag(args, "--no-wsd"),
      EnablePortSweep = !Args.Flag(args, "--no-sweep"),
      Log = verbose ? line => Console.Error.WriteLine(line) : null,
    };

    if (int.TryParse(Args.Option(args, "--timeout-ms"), out var ms) && ms > 0)
    {
      options.ConnectTimeout = TimeSpan.FromMilliseconds(ms);
      options.LlrpProbeTimeout = TimeSpan.FromMilliseconds(Math.Max(ms * 5, 1000));
    }

    if (int.TryParse(Args.Option(args, "--max-hosts"), out var maxHosts) && maxHosts >= 2) options.MaxHostsPerSubnet = maxHosts;

    var selectors = Args.Options(args, "--interface");
    if (selectors.Count > 0)
    {
      var available = NetworkInterfaces.Enumerate(includeVirtual: true);
      options.Interfaces = NetworkInterfaces.Select(available, selectors, out var unmatched);
      problems.AddRange(unmatched.Select(u => $"no interface matches '{u}'"));
    }

    var subnets = new List<IpSubnet>();
    foreach (var text in Args.Options(args, "--subnet"))
    {
      if (IpSubnet.TryParse(text, out var subnet)) subnets.Add(subnet);
      else problems.Add($"'{text}' is not a subnet like 192.168.1.0/24");
    }

    options.ExplicitSubnets = subnets;
    return options;
  }

  private static int Scan(string[] args)
  {
    var options = OptionsFrom(args, out var problems);
    if (problems.Count > 0)
    {
      problems.ForEach(Console.Error.WriteLine);
      return 2;
    }

    var json = Args.Flag(args, "--json");
    var showProgress = !json && !Console.IsErrorRedirected;
    var progress = new SyncProgress(tick =>
    {
      if (!showProgress) return;
      var text = tick.Phase switch
      {
        ScanPhase.PortSweep when tick.Total > 0 => $"sweep {tick.Done}/{tick.Total}  found {tick.Found}",
        ScanPhase.Done => string.Empty,
        _ => $"{tick.Message}  found {tick.Found}",
      };
      Console.Error.Write($"\r{text,-60}\r");
    });

    var result = new ReaderScanner(options).ScanAsync(progress).GetAwaiter().GetResult();
    if (showProgress) Console.Error.Write($"\r{string.Empty,-60}\r");

    if (json)
    {
      Console.WriteLine(JsonOutput.Serialize(result));
    }
    else
    {
      foreach (var warning in result.Warnings) Console.Error.WriteLine($"warning: {warning}");
      if (result.Readers.Count == 0)
      {
        Console.WriteLine($"no readers found on {string.Join(", ", result.Interfaces.Select(i => $"{i.Name} {i.Subnet}"))} ({result.Elapsed.TotalSeconds:F1} s)");
      }
      else
      {
        TableWriter.Write(Console.Out,
          ["IP", "Hostname", "Vendor", "Model", "Firmware", "MAC", "LLRP", "Found via", "Confidence"],
          result.Readers.Select(Row).ToList());
        foreach (var reader in result.Readers.Where(r => r.Note is not null))
        {
          Console.WriteLine($"{reader.Ip}: {reader.Note}");
        }

        Console.WriteLine($"{result.Readers.Count} reader(s) in {result.Elapsed.TotalSeconds:F1} s");
      }
    }

    return result.Readers.Any(r => r.Confidence >= Confidence.Likely) ? 0 : 1;
  }

  private static int Interfaces(string[] args)
  {
    var nics = NetworkInterfaces.Enumerate(Args.Flag(args, "--include-virtual"));
    if (Args.Flag(args, "--json"))
    {
      Console.WriteLine(JsonOutput.Serialize(nics));
      return 0;
    }

    TableWriter.Write(Console.Out,
      ["Name", "Address", "Subnet", "Kind", "MAC", "Description"],
      nics.Select(n => (IReadOnlyList<string>)[n.Name, n.Address.ToString(), n.Subnet.ToString(), n.Kind, n.Mac is null ? "" : MacFormat.Colon(n.Mac), n.Description]).ToList());
    return nics.Count > 0 ? 0 : 1;
  }

  private static int Probe(string[] args)
  {
    var positional = Args.Positional(args, ValueOptions);
    if (positional.Count < 2 || !IPAddress.TryParse(positional[1], out var ip))
    {
      Console.Error.WriteLine("probe takes an IPv4 address");
      return 2;
    }

    var options = OptionsFrom(args, out var problems);
    if (problems.Count > 0)
    {
      problems.ForEach(Console.Error.WriteLine);
      return 2;
    }

    var reader = new ReaderScanner(options).ProbeAsync(ip).GetAwaiter().GetResult();
    if (Args.Flag(args, "--json"))
    {
      Console.WriteLine(JsonOutput.Serialize(reader));
    }
    else
    {
      Console.WriteLine($"{reader.Ip}  {reader.Vendor}  {reader.Model ?? "-"}  {reader.Confidence}");
      Console.WriteLine($"  hostname  {reader.Hostname ?? "-"}" + (reader.ExpectedHostname is { } e && e != reader.Hostname ? $"  (factory name would be {e})" : ""));
      Console.WriteLine($"  mac       {(reader.Mac is null ? "-" : reader.MacText)}");
      Console.WriteLine($"  firmware  {reader.Firmware ?? "-"}");
      Console.WriteLine($"  llrp      {reader.LlrpStatus}");
      Console.WriteLine($"  web       {reader.WebUrl}");
      Console.WriteLine($"  via       {reader.Sources}");
      if (reader.Note is not null) Console.WriteLine($"  note      {reader.Note}");
    }

    return reader.Confidence >= Confidence.Likely ? 0 : 1;
  }

  private static IReadOnlyList<string> Row(ReaderInfo r) =>
  [
    r.Ip.ToString(),
    r.Hostname ?? "",
    r.Vendor.ToString(),
    r.Model ?? "",
    r.Firmware ?? "",
    r.MacText,
    r.LlrpStatus switch { LlrpStatus.Free => "free", LlrpStatus.InUse => "in use", LlrpStatus.NoLlrp => "none", _ => "?" },
    string.Join("+", Sources(r.Sources)),
    r.Confidence.ToString().ToLowerInvariant(),
  ];

  private static IEnumerable<string> Sources(DiscoverySources sources)
  {
    if (sources.HasFlag(DiscoverySources.Mdns)) yield return "mdns";
    if (sources.HasFlag(DiscoverySources.WsDiscovery)) yield return "wsd";
    if (sources.HasFlag(DiscoverySources.PortSweep)) yield return "sweep";
    if (sources.HasFlag(DiscoverySources.Manual)) yield return "manual";
  }

  private static int Usage(int code)
  {
    Console.WriteLine("""
      readerdetect - find Impinj Speedway and Zebra FX RFID readers on the local network

      usage:
        readerdetect [scan] [--interface <name|ip>]... [--subnet <cidr>]... [--no-sweep] [--no-mdns] [--no-wsd]
                     [--include-virtual] [--timeout-ms <n>] [--max-hosts <n>] [--json] [--verbose]
        readerdetect interfaces [--include-virtual] [--json]
        readerdetect probe <ip> [--json] [--verbose]
        readerdetect help

      exit codes: 0 reader(s) found, 1 none found, 2 usage or error
      """);
    return code;
  }

  /// <summary>Progress that runs the callback inline; the CLI has no UI thread to marshal to.</summary>
  private sealed class SyncProgress(Action<ScanProgress> handler) : IProgress<ScanProgress>
  {
    public void Report(ScanProgress value) => handler(value);
  }
}
