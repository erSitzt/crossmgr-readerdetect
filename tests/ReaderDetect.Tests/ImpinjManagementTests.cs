using System.Net;
using ReaderDetect.Management;
using ReaderDetect.Management.Impinj;
using ReaderDetect.Network;

namespace ReaderDetect.Tests;

public class ImpinjManagementTests
{
  // Verbatim output of `show network summary` on the R220, Octane 7.6.3.240.
  private const string ShowNetworkSummary = """
    Status='0,Success'
    PrimaryInterface='eth:eth0'
    ActiveInterface='eth:eth0'
    Hostname='SpeedwayR-12-59-43'
    connectionStatus='Connected'
    ipAddressMode='Dynamic'
    ipAddress='192.168.68.139'
    ipMask='255.255.255.0'
    broadcastAddress='192.168.68.255'
    gatewayAddress='192.168.68.1'
    MACAddress='00:16:25:12:59:43'
    HTTPService='active'

    """;

  private static readonly IPAddress Ip = IPAddress.Parse("192.168.68.139");
  private static readonly ReaderCredentials Creds = ReaderCredentials.DefaultFor(ReaderVendor.Impinj);

  [Fact]
  public void ResponseParsesTheCapturedSummary()
  {
    var response = RShellResponse.Parse(ShowNetworkSummary);
    Assert.True(response.Success);
    Assert.Equal(0, response.StatusCode);
    Assert.Equal("Success", response.StatusText);
    Assert.Equal(11, response.Values.Count);
    Assert.Equal("Dynamic", response["ipAddressMode"]);
    Assert.Equal("00:16:25:12:59:43", response["MACAddress"]);
    Assert.Null(response["nope"]);
  }

  [Theory]
  [InlineData("Status='1,Invalid-Command'\r\n", 1, "Invalid-Command")]
  [InlineData("Status='3,Invalid-Parameter-Value'", 3, "Invalid-Parameter-Value")]
  [InlineData(" > show network summary\nStatus='0,Success'\n show network > ", 0, "Success")]
  [InlineData("garbage without any status", -1, "no Status line in response")]
  public void ResponseStatusLine(string text, int code, string status)
  {
    var response = RShellResponse.Parse(text);
    Assert.Equal(code, response.StatusCode);
    Assert.Equal(status, response.StatusText);
  }

  [Fact]
  public void CommandsAreSpelledExactly()
  {
    Assert.Equal("config network ip dynamic", RShellCommands.IpDynamic);
    Assert.Equal("config network ip static 192.168.20.116 255.255.255.0 192.168.20.1",
      RShellCommands.IpStatic(IPAddress.Parse("192.168.20.116"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("192.168.20.1")));
    Assert.Equal("config network ip static 192.168.20.116 255.255.255.0",
      RShellCommands.IpStatic(IPAddress.Parse("192.168.20.116"), IPAddress.Parse("255.255.255.0"), null));
    Assert.Equal("config network hostname finish-line", RShellCommands.Hostname("finish-line"));
    Assert.Equal("config network dns add 1.1.1.1", RShellCommands.DnsAdd(IPAddress.Parse("1.1.1.1")));
    Assert.Equal("show network summary", RShellCommands.ShowNetworkSummary);
  }

  [Fact]
  public void CommandsRejectBadArguments()
  {
    Assert.Throws<ArgumentException>(() => RShellCommands.IpStatic(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.0.255.0"), null));
    Assert.Throws<ArgumentException>(() => RShellCommands.IpStatic(IPAddress.IPv6Loopback, IPAddress.Parse("255.255.255.0"), null));
    Assert.Throws<ArgumentException>(() => RShellCommands.Hostname("bad name"));
    Assert.Throws<ArgumentException>(() => RShellCommands.Hostname("-lead"));
  }

  [Fact]
  public async Task GetMapsTheSummary()
  {
    var session = new FakeRShellSession(_ => ShowNetworkSummary);
    var settings = await new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session)).GetNetworkAsync(Ip, Creds);

    Assert.True(settings.Dhcp);
    Assert.Equal(Ip, settings.Ip);
    Assert.Equal(IPAddress.Parse("255.255.255.0"), settings.Mask);
    Assert.Equal(IPAddress.Parse("192.168.68.1"), settings.Gateway);
    Assert.Equal("SpeedwayR-12-59-43", settings.Hostname);
    Assert.Equal("00:16:25:12:59:43", MacFormat.Colon(settings.Mac!));
    Assert.Equal("active", settings.Raw["HTTPService"]);
    Assert.Equal(["show network summary"], session.Commands);
    Assert.True(session.Disposed);
  }

  [Fact]
  public async Task SetStaticSendsShowThenConfigAndReportsTheNewAddress()
  {
    var session = new FakeRShellSession(_ => ShowNetworkSummary.Replace("Dynamic", "Static", StringComparison.Ordinal));
    var configurator = new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session));
    var desired = NetworkSettings.ForStatic(IPAddress.Parse("192.168.68.200"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("192.168.68.1"));

    var result = await configurator.SetNetworkAsync(Ip, Creds, desired);

    Assert.True(result.Applied);
    Assert.False(result.ConnectionDropped);
    Assert.Equal(IPAddress.Parse("192.168.68.200"), result.ExpectedAddress);
    Assert.Equal(["show network summary", "config network ip static 192.168.68.200 255.255.255.0 192.168.68.1"], session.Commands);
    Assert.DoesNotContain(result.Log, line => line.Contains("impinj", StringComparison.Ordinal));
  }

  [Fact]
  public async Task SetDhcpWithHostnameChangesTheNameFirst()
  {
    var session = new FakeRShellSession(_ => ShowNetworkSummary);
    var configurator = new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session));

    var result = await configurator.SetNetworkAsync(Ip, Creds, NetworkSettings.ForDhcp("finish-line"));

    Assert.True(result.Applied);
    Assert.Null(result.ExpectedAddress);
    Assert.Equal(["show network summary", "config network hostname finish-line", "config network ip dynamic"], session.Commands);
  }

  [Fact]
  public async Task RefusedCommandThrowsWithTheStatusText()
  {
    var session = new FakeRShellSession(c => c.StartsWith("show", StringComparison.Ordinal) ? ShowNetworkSummary : "Status='3,Invalid-Parameter-Value'\n");
    var configurator = new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session));

    var ex = await Assert.ThrowsAsync<ConfigurationException>(() => configurator.SetNetworkAsync(Ip, Creds, NetworkSettings.ForDhcp()));
    Assert.Equal(ConfigurationFailure.CommandFailed, ex.Kind);
    Assert.Contains("Invalid-Parameter-Value", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ConnectionDroppingOnTheLastCommandCountsAsApplied()
  {
    var session = new FakeRShellSession(_ => ShowNetworkSummary, dropAfterCommand: 2);
    var configurator = new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session));
    var desired = NetworkSettings.ForStatic(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.255.255.0"), null);

    var result = await configurator.SetNetworkAsync(Ip, Creds, desired);

    Assert.True(result.Applied);
    Assert.True(result.ConnectionDropped);
    Assert.Equal(IPAddress.Parse("10.0.0.5"), result.ExpectedAddress);
  }

  [Fact]
  public async Task ConnectionDroppingEarlyIsAnError()
  {
    var session = new FakeRShellSession(_ => ShowNetworkSummary, dropAfterCommand: 1);
    var configurator = new ImpinjConfigurator((_, _, _) => Task.FromResult<IRShellSession>(session));
    var ex = await Assert.ThrowsAsync<ConfigurationException>(() => configurator.SetNetworkAsync(Ip, Creds, NetworkSettings.ForDhcp()));
    Assert.Equal(ConfigurationFailure.ConnectionDropped, ex.Kind);
  }

  [Fact]
  public void PlanListsTheCommandsWithoutConnecting()
  {
    var configurator = new ImpinjConfigurator((_, _, _) => throw new InvalidOperationException("must not connect"));
    Assert.Equal(["config network ip dynamic"], configurator.Plan(NetworkSettings.ForDhcp()));
    Assert.Equal(
      ["config network dns add 1.1.1.1", "config network ip static 10.0.0.5 255.255.255.0 10.0.0.1"],
      configurator.Plan(NetworkSettings.ForStatic(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("10.0.0.1"), [IPAddress.Parse("1.1.1.1")])));
  }

  [Fact]
  public void StaticSettingsAreValidated()
  {
    Assert.Throws<ArgumentException>(() => NetworkSettings.ForStatic(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.0.255.0"), null));
    Assert.Throws<ArgumentException>(() => NetworkSettings.ForStatic(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("10.0.1.1")));
    Assert.Equal("static 10.0.0.5/255.255.255.0 gateway 10.0.0.1 hostname r1",
      NetworkSettings.ForStatic(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("10.0.0.1"), null, "r1").Describe());
    Assert.Equal("DHCP", NetworkSettings.ForDhcp().Describe());
  }

  [Fact]
  public void CredentialsDefaults()
  {
    Assert.Equal(new ReaderCredentials("root", "impinj"), ReaderCredentials.DefaultFor(ReaderVendor.Impinj));
    Assert.Equal(new ReaderCredentials("admin", "change"), ReaderCredentials.DefaultFor(ReaderVendor.Zebra));
    Assert.True(ReaderCredentials.DefaultFor(ReaderVendor.Impinj).IsDefaultFor(ReaderVendor.Impinj));
    Assert.False(new ReaderCredentials("root", "secret").IsDefaultFor(ReaderVendor.Impinj));
    Assert.Equal(ConfigurationFailure.Unsupported, Assert.Throws<ConfigurationException>(() => ReaderCredentials.DefaultFor(ReaderVendor.Unknown)).Kind);
    Assert.DoesNotContain("impinj", ReaderCredentials.DefaultFor(ReaderVendor.Impinj).ToString(), StringComparison.Ordinal);
  }
}
