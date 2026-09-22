using System.Net;
using ReaderDetect.Management;
using ReaderDetect.Network;

namespace ReaderDetect.Cli;

/// <summary>
/// <c>config show|dhcp|static|reboot &lt;ip&gt;</c>. Changing a reader's address is
/// the one thing this tool does that can make a reader unreachable, so every
/// change is shown first, confirmed, and followed by a wait for the reader to
/// come back at its new address.
/// </summary>
internal static class ConfigCommands
{
  private const string PasswordEnv = "READERDETECT_PASSWORD";

  public static int Run(string[] args)
  {
    var positional = Args.Positional(args, Program.ValueOptions);
    if (positional.Count < 3 || !IPAddress.TryParse(positional[2], out var ip))
    {
      Console.Error.WriteLine("usage: readerdetect config show|dhcp|static|reboot <ip> [options]");
      return 2;
    }

    var sub = positional[1].ToLowerInvariant();
    if (sub is not ("show" or "dhcp" or "static" or "reboot"))
    {
      Console.Error.WriteLine($"unknown config command '{positional[1]}'");
      return 2;
    }

    var options = Program.OptionsFrom(args, out var problems);
    if (problems.Count > 0)
    {
      problems.ForEach(Console.Error.WriteLine);
      return 2;
    }

    var log = options.Log;
    var json = Args.Flag(args, "--json");
    var scanner = new ReaderScanner(options);

    try
    {
      var (vendor, info) = ResolveVendor(args, ip, scanner);
      var credentials = ResolveCredentials(args, vendor);
      var configurator = ReaderConfigurators.For(vendor, log);
      if (!json && info is not null)
      {
        Console.Error.WriteLine($"{ip}: {info.Vendor} {info.Model ?? ""} {info.Hostname ?? ""} ({info.Confidence})".Replace("  ", " ", StringComparison.Ordinal));
      }

      return sub switch
      {
        "show" => Show(ip, configurator, credentials, json),
        "reboot" => Reboot(args, ip, configurator, credentials),
        _ => Apply(args, sub, ip, configurator, credentials, scanner, info),
      };
    }
    catch (ConfigurationException ex)
    {
      Console.Error.WriteLine($"error: {ex.Message}");
      if (ex.Kind == ConfigurationFailure.AuthFailed)
      {
        Console.Error.WriteLine("hint: pass --user/--password (or set READERDETECT_PASSWORD) if the reader's login was changed; new Zebra firmware forces a new admin password on first login");
      }

      return ex.Kind switch
      {
        ConfigurationFailure.AuthFailed => 4,
        ConfigurationFailure.Throttled or ConfigurationFailure.Unsupported => 5,
        _ => 2,
      };
    }
  }

  private static (ReaderVendor Vendor, ReaderInfo? Info) ResolveVendor(string[] args, IPAddress ip, ReaderScanner scanner)
  {
    var text = Args.Option(args, "--vendor");
    if (text is not null)
    {
      if (!Enum.TryParse<ReaderVendor>(text, ignoreCase: true, out var forced) || forced == ReaderVendor.Unknown)
      {
        throw new ConfigurationException(ConfigurationFailure.Unsupported, $"--vendor must be impinj or zebra, not '{text}'");
      }

      return (forced, null);
    }

    var info = ReaderConfigurators.DetectAsync(ip, scanner).GetAwaiter().GetResult();
    if (info.Vendor == ReaderVendor.Unknown)
    {
      throw new ConfigurationException(ConfigurationFailure.Unsupported,
        $"could not identify the reader at {ip} ({info.Note ?? "no LLRP answer"}); pass --vendor impinj|zebra to force it");
    }

    return (info.Vendor, info);
  }

  private static ReaderCredentials ResolveCredentials(string[] args, ReaderVendor vendor)
  {
    var defaults = ReaderCredentials.DefaultFor(vendor);
    var user = Args.Option(args, "--user") ?? defaults.Username;
    var password = Args.Option(args, "--password") ?? Environment.GetEnvironmentVariable(PasswordEnv) ?? defaults.Password;
    return new ReaderCredentials(user, password);
  }

  private static int Show(IPAddress ip, IReaderConfigurator configurator, ReaderCredentials credentials, bool json)
  {
    var settings = configurator.GetNetworkAsync(ip, credentials).GetAwaiter().GetResult();
    if (json)
    {
      Console.WriteLine(JsonOutput.Serialize(settings));
      return 0;
    }

    Print(settings);
    return 0;
  }

  private static void Print(NetworkSettings settings)
  {
    Console.WriteLine($"  mode      {(settings.Dhcp ? "DHCP" : "static")}");
    Console.WriteLine($"  address   {settings.Ip?.ToString() ?? "-"}");
    Console.WriteLine($"  mask      {settings.Mask?.ToString() ?? "-"}");
    Console.WriteLine($"  gateway   {settings.Gateway?.ToString() ?? "-"}");
    if (settings.Dns.Count > 0) Console.WriteLine($"  dns       {string.Join(", ", settings.Dns)}");
    Console.WriteLine($"  hostname  {settings.Hostname ?? "-"}");
    Console.WriteLine($"  mac       {(settings.Mac is null ? "-" : MacFormat.Colon(settings.Mac))}");
    if (settings.Raw.Count > 0)
    {
      Console.WriteLine("  raw:");
      foreach (var (key, value) in settings.Raw) Console.WriteLine($"    {key} = {value}");
    }
  }

  private static int Reboot(string[] args, IPAddress ip, IReaderConfigurator configurator, ReaderCredentials credentials)
  {
    if (!Confirm(args, $"Reboot the reader at {ip}?")) return 2;
    configurator.RebootAsync(ip, credentials).GetAwaiter().GetResult();
    Console.WriteLine("reboot requested");
    return 0;
  }

  private static int Apply(string[] args, string sub, IPAddress ip, IReaderConfigurator configurator, ReaderCredentials credentials, ReaderScanner scanner, ReaderInfo? info)
  {
    NetworkSettings desired;
    try
    {
      desired = sub == "dhcp" ? NetworkSettings.ForDhcp(Args.Option(args, "--hostname")) : StaticFrom(args);
    }
    catch (ArgumentException ex)
    {
      Console.Error.WriteLine(ex.Message);
      return 2;
    }

    if (desired.Ip is { } target)
    {
      var local = scanner.Options.Interfaces ?? NetworkInterfaces.Enumerate(scanner.Options.IncludeVirtualInterfaces);
      if (!local.Any(n => n.Subnet.Contains(target)))
      {
        Console.Error.WriteLine($"warning: {target} is outside this PC's subnets ({string.Join(", ", local.Select(n => n.Subnet))}); the reader will not be reachable from here afterwards");
      }
    }

    var plan = configurator.Plan(desired);
    Console.WriteLine($"{ip} -> {desired.Describe()}");
    foreach (var step in plan) Console.WriteLine($"  {step}");
    if (Args.Flag(args, "--dry-run"))
    {
      Console.WriteLine("dry run: nothing sent");
      return 0;
    }

    if (!Confirm(args, "Apply?")) return 2;

    var result = configurator.SetNetworkAsync(ip, credentials, desired).GetAwaiter().GetResult();
    foreach (var line in result.Log) Console.WriteLine($"  {line}");
    Console.WriteLine(result.ConnectionDropped ? "applied (connection dropped as the address changed)" : "applied");
    if (result.RebootRequested) Console.WriteLine("the reader is rebooting");
    if (Args.Flag(args, "--no-wait")) return 0;

    var timeout = TimeSpan.FromSeconds(result.RebootRequested ? 90 : result.ExpectedAddress is null ? 60 : 30);
    var seen = ReaderConfigurators.WaitForReaderAsync(result.ExpectedAddress, info?.Mac, scanner, timeout,
      progress: message => Console.Error.Write($"\r{message,-60}\r")).GetAwaiter().GetResult();
    Console.Error.Write($"\r{string.Empty,-60}\r");
    if (seen is null)
    {
      Console.WriteLine(result.ExpectedAddress is null
        ? "the reader has not shown up on the network yet; run 'readerdetect scan' in a moment"
        : $"nothing answers at {result.ExpectedAddress} yet; check the address and run 'readerdetect scan'");
      return 3;
    }

    Console.WriteLine($"reader is back at {seen.Ip} ({seen.Vendor} {seen.Model ?? ""} {seen.Hostname ?? ""})".Replace("  ", " ", StringComparison.Ordinal));
    return 0;
  }

  private static NetworkSettings StaticFrom(string[] args)
  {
    var address = Args.Option(args, "--address");
    var mask = Args.Option(args, "--mask");
    if (address is null || mask is null) throw new ArgumentException("config static needs --address <ip> and --mask <mask> (and usually --gateway <ip>)");
    if (!IPAddress.TryParse(address, out var ip)) throw new ArgumentException($"'{address}' is not an IPv4 address");
    if (!IPAddress.TryParse(mask, out var maskAddress)) throw new ArgumentException($"'{mask}' is not a subnet mask");
    IPAddress? gateway = null;
    if (Args.Option(args, "--gateway") is { } gw && !IPAddress.TryParse(gw, out gateway)) throw new ArgumentException($"'{gw}' is not an IPv4 address");
    var dns = new List<IPAddress>();
    foreach (var text in (Args.Option(args, "--dns") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      if (!IPAddress.TryParse(text, out var server)) throw new ArgumentException($"'{text}' is not a DNS server address");
      dns.Add(server);
    }

    return NetworkSettings.ForStatic(ip, maskAddress, gateway, dns, Args.Option(args, "--hostname"));
  }

  private static bool Confirm(string[] args, string question)
  {
    if (Args.Flag(args, "--yes")) return true;
    if (Console.IsInputRedirected)
    {
      Console.Error.WriteLine("refusing to change a reader without confirmation; pass --yes when scripting");
      return false;
    }

    Console.Write($"{question} [y/N] ");
    var answer = Console.ReadLine();
    if (answer is not null && answer.Trim().StartsWith('y')) return true;
    Console.WriteLine("aborted");
    return false;
  }
}
