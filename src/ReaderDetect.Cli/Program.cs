using System.Net;
using ReaderDetect.Llrp;
using ReaderDetect.Vendors;

namespace ReaderDetect.Cli;

/// <summary>
/// Command-line front end. It exists so the discovery can be scripted and so
/// a volunteer can paste one line of output into a chat when something at the
/// track does not work.
/// </summary>
internal static class Program
{
  private static int Main(string[] args)
  {
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help") return Usage();
    var verbose = Args.Flag(args, "--verbose");
    Action<string>? log = verbose ? line => Console.Error.WriteLine(line) : null;

    try
    {
      switch (args[0].ToLowerInvariant())
      {
        case "probe":
          return Probe(args, log);
        default:
          Console.Error.WriteLine($"unknown command '{args[0]}'");
          return Usage();
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"failed: {ex.Message}");
      if (verbose) Console.Error.WriteLine(ex);
      return 2;
    }
  }

  private static int Probe(string[] args, Action<string>? log)
  {
    var positional = Args.Positional(args, "--timeout-ms");
    if (positional.Count < 2 || !IPAddress.TryParse(positional[1], out var ip))
    {
      Console.Error.WriteLine("probe takes an IPv4 address");
      return 2;
    }

    var timeout = int.TryParse(Args.Option(args, "--timeout-ms"), out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(3);
    var result = new LlrpProbe(timeout, log).ProbeAsync(ip).GetAwaiter().GetResult();
    Console.WriteLine($"{ip}: llrp {result.Status}" + (result.ConnectionAttempt is { } a ? $" ({a})" : ""));
    if (result.Capabilities is { } caps)
    {
      var vendor = ReaderModels.VendorFromPen(caps.ManufacturerPen);
      Console.WriteLine($"  vendor   {vendor} (PEN {caps.ManufacturerPen})");
      Console.WriteLine($"  model    {ReaderModels.Describe(vendor, caps.ModelCode)} (code {caps.ModelCode})");
      Console.WriteLine($"  firmware {caps.FirmwareVersion}");
      Console.WriteLine($"  antennas {caps.MaxAntennas}");
    }

    if (result.Error is not null) Console.WriteLine($"  note     {result.Error}");
    return result.ConnectionAttempt is not null ? 0 : 1;
  }

  private static int Usage()
  {
    Console.WriteLine("""
      readerdetect - find Impinj Speedway and Zebra FX RFID readers on the local network

      usage:
        readerdetect probe <ip> [--timeout-ms <n>] [--verbose]   identify one reader over LLRP
        readerdetect help
      """);
    return 2;
  }
}
