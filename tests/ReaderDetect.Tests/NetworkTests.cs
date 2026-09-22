using System.Net;
using System.Net.NetworkInformation;
using ReaderDetect.Network;

namespace ReaderDetect.Tests;

public class NetworkTests
{
  [Fact]
  public void MacOsArpLineParses()
  {
    const string output = "? (192.168.68.139) at 0:16:25:12:59:43 on en0 ifscope [ethernet]\n";
    var mac = ArpOutputParser.ParseMacOs(output, IPAddress.Parse("192.168.68.139"));
    Assert.NotNull(mac);
    Assert.Equal("00:16:25:12:59:43", MacFormat.Colon(mac));
    Assert.Null(ArpOutputParser.ParseMacOs(output, IPAddress.Parse("192.168.68.140")));
    Assert.Null(ArpOutputParser.ParseMacOs("? (192.168.68.140) at (incomplete) on en0 ifscope [ethernet]\n", IPAddress.Parse("192.168.68.140")));
  }

  [Fact]
  public void LinuxNeighbourLineParses()
  {
    const string output = "192.168.68.139 dev eth0 lladdr 00:16:25:12:59:43 REACHABLE\n192.168.68.1 dev eth0 FAILED\n";
    var mac = ArpOutputParser.ParseLinux(output, IPAddress.Parse("192.168.68.139"));
    Assert.NotNull(mac);
    Assert.Equal("00:16:25:12:59:43", MacFormat.Colon(mac));
    Assert.Null(ArpOutputParser.ParseLinux(output, IPAddress.Parse("192.168.68.1")));
  }

  [Theory]
  [InlineData("en0", "en0", NetworkInterfaceType.Wireless80211, false)]
  [InlineData("Ethernet", "Intel(R) Ethernet Connection I219-LM", NetworkInterfaceType.Ethernet, false)]
  [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, true)]
  [InlineData("Ethernet 3", "VMware Virtual Ethernet Adapter for VMnet8", NetworkInterfaceType.Ethernet, true)]
  [InlineData("Tailscale", "Tailscale Tunnel", NetworkInterfaceType.Ethernet, true)]
  [InlineData("utun4", "utun4", NetworkInterfaceType.Ethernet, true)]
  [InlineData("bridge0", "bridge0", NetworkInterfaceType.Ethernet, true)]
  [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet, false)]
  [InlineData("ppp0", "ppp0", NetworkInterfaceType.Ppp, true)]
  public void VirtualAdapterHeuristic(string name, string description, NetworkInterfaceType type, bool expected) =>
    Assert.Equal(expected, NetworkInterfaces.LooksVirtual(name, description, type));

  [Fact]
  public void SelectingInterfacesByNameOrAddress()
  {
    var en0 = Nic("en0", "192.168.68.127");
    var en5 = Nic("en5", "10.0.0.5");
    var chosen = NetworkInterfaces.Select([en0, en5], ["EN5", "192.168.68.127", "nope"], out var unmatched);
    Assert.Equal([en5, en0], chosen);
    Assert.Equal(["nope"], unmatched);
  }

  [Fact]
  public void InterfaceInfoDerivesSubnet()
  {
    var nic = Nic("en0", "192.168.68.127");
    Assert.Equal("192.168.68.0/24", nic.Subnet.ToString());
    Assert.Equal("en0  192.168.68.127/24  Ethernet", nic.ToString());
  }

  [Fact]
  public void HttpVendorHint()
  {
    Assert.Equal(ReaderVendor.Impinj, HttpFingerprinter.VendorHint(new HttpFingerprint(401, "thttpd/2.29 23May2018", "realm=\".\"")));
    Assert.Null(HttpFingerprinter.VendorHint(new HttpFingerprint(200, "nginx", null)));
    Assert.Null(HttpFingerprinter.VendorHint(null));
  }

  internal static NetworkInterfaceInfo Nic(string name, string address, int prefix = 24) =>
    new("id-" + name, name, name, NetworkInterfaceType.Ethernet, IPAddress.Parse(address),
      new IpSubnet(IPAddress.Parse(address), prefix).Mask, prefix, null, false, 1);
}
