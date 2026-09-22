using System.Net;
using ReaderDetect.Network;

namespace ReaderDetect.Tests;

public class IpSubnetTests
{
  [Fact]
  public void ParsesAndNormalises()
  {
    var subnet = IpSubnet.Parse("192.168.68.127/24");
    Assert.Equal("192.168.68.0/24", subnet.ToString());
    Assert.Equal(IPAddress.Parse("255.255.255.0"), subnet.Mask);
    Assert.Equal(IPAddress.Parse("192.168.68.255"), subnet.Broadcast);
    Assert.Equal(254, subnet.HostCount);
  }

  [Theory]
  [InlineData("192.168.1.0")]
  [InlineData("192.168.1.0/33")]
  [InlineData("fe80::1/64")]
  [InlineData("nope/24")]
  [InlineData("")]
  public void RejectsBadCidr(string text)
  {
    Assert.False(IpSubnet.TryParse(text, out _));
    Assert.Throws<FormatException>(() => IpSubnet.Parse(text));
  }

  [Theory]
  [InlineData("255.255.255.0", 24)]
  [InlineData("255.255.0.0", 16)]
  [InlineData("255.255.255.252", 30)]
  [InlineData("0.0.0.0", 0)]
  [InlineData("255.255.255.255", 32)]
  public void MaskToPrefix(string mask, int prefix)
  {
    Assert.Equal(prefix, IpSubnet.PrefixFromMask(IPAddress.Parse(mask)));
    Assert.True(IpSubnet.IsValidMask(IPAddress.Parse(mask)));
  }

  [Fact]
  public void NonContiguousMaskIsRejected()
  {
    Assert.False(IpSubnet.IsValidMask(IPAddress.Parse("255.0.255.0")));
    Assert.Throws<ArgumentException>(() => IpSubnet.PrefixFromMask(IPAddress.Parse("255.0.255.0")));
  }

  [Fact]
  public void Contains()
  {
    var subnet = IpSubnet.FromAddress(IPAddress.Parse("192.168.68.127"), IPAddress.Parse("255.255.255.0"));
    Assert.True(subnet.Contains(IPAddress.Parse("192.168.68.139")));
    Assert.True(subnet.Contains(IPAddress.Parse("192.168.68.0")));
    Assert.False(subnet.Contains(IPAddress.Parse("192.168.69.1")));
    Assert.False(subnet.Contains(IPAddress.IPv6Loopback));
  }

  [Fact]
  public void SmallSubnetListsAllUsableHosts()
  {
    var hosts = IpSubnet.Parse("192.168.68.0/24").Hosts(1024, IPAddress.Parse("192.168.68.127"), out var capped).ToList();
    Assert.False(capped);
    Assert.Equal(254, hosts.Count);
    Assert.Equal(IPAddress.Parse("192.168.68.1"), hosts[0]);
    Assert.Equal(IPAddress.Parse("192.168.68.254"), hosts[^1]);
  }

  [Fact]
  public void Slash30AndSlash31()
  {
    Assert.Equal(["10.0.0.1", "10.0.0.2"], IpSubnet.Parse("10.0.0.0/30").Hosts(1024, null, out _).Select(h => h.ToString()));
    Assert.Equal(["10.0.0.0", "10.0.0.1"], IpSubnet.Parse("10.0.0.0/31").Hosts(1024, null, out _).Select(h => h.ToString()));
    Assert.Equal(["10.0.0.7"], IpSubnet.Parse("10.0.0.7/32").Hosts(1024, null, out _).Select(h => h.ToString()));
  }

  [Fact]
  public void LargeSubnetIsCappedToTheBlockAroundTheAnchor()
  {
    var hosts = IpSubnet.Parse("192.168.0.0/16").Hosts(1024, IPAddress.Parse("192.168.68.127"), out var capped).ToList();
    Assert.True(capped);
    Assert.Equal(1022, hosts.Count);
    // 192.168.68.127 lies in the /22 block 192.168.68.0 - 192.168.71.255.
    Assert.Equal(IPAddress.Parse("192.168.68.1"), hosts[0]);
    Assert.Equal(IPAddress.Parse("192.168.71.254"), hosts[^1]);
  }

  [Fact]
  public void LargeSubnetWithoutAnchorStartsAtTheNetwork()
  {
    var hosts = IpSubnet.Parse("10.0.0.0/8").Hosts(256, null, out var capped).ToList();
    Assert.True(capped);
    Assert.Equal(254, hosts.Count);
    Assert.Equal(IPAddress.Parse("10.0.0.1"), hosts[0]);
  }
}
