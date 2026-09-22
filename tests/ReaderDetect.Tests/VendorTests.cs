using ReaderDetect.Network;
using ReaderDetect.Vendors;

namespace ReaderDetect.Tests;

public class VendorTests
{
  [Theory]
  [InlineData(25882u, ReaderVendor.Impinj)]
  [InlineData(161u, ReaderVendor.Zebra)]
  [InlineData(1u, ReaderVendor.Unknown)]
  public void PenMapsToVendor(uint pen, ReaderVendor vendor) => Assert.Equal(vendor, ReaderModels.VendorFromPen(pen));

  [Fact]
  public void KnownImpinjModelsAreNamed()
  {
    Assert.Equal("Speedway R220", ReaderModels.Describe(ReaderVendor.Impinj, 2001001));
    Assert.Equal("Speedway R420", ReaderModels.Describe(ReaderVendor.Impinj, 2001002));
    Assert.Equal("R700", ReaderModels.Describe(ReaderVendor.Impinj, 2001052));
  }

  [Fact]
  public void UnknownModelKeepsTheRawCode()
  {
    Assert.Null(ReaderModels.ModelName(ReaderVendor.Impinj, 2001099));
    Assert.Equal("model 2001099", ReaderModels.Describe(ReaderVendor.Impinj, 2001099));
    Assert.Equal("model 42", ReaderModels.Describe(ReaderVendor.Zebra, 42));
  }

  [Theory]
  [InlineData("00:16:25:12:59:43", ReaderVendor.Impinj)]
  [InlineData("84:24:8D:FB:6C:10", ReaderVendor.Zebra)]
  [InlineData("00:23:68:3B:A6:3A", ReaderVendor.Zebra)]
  public void OuiLookupFindsReaderVendors(string mac, ReaderVendor vendor) =>
    Assert.Equal(vendor, OuiTable.Lookup(MacFormat.Parse(mac)));

  [Fact]
  public void OuiLookupIsNullForOthers()
  {
    Assert.Null(OuiTable.Lookup(MacFormat.Parse("a4:83:e7:11:22:33")));
    Assert.Null(OuiTable.Lookup(null));
    Assert.Null(OuiTable.VendorName(null));
    Assert.Equal("Impinj", OuiTable.VendorName(MacFormat.Parse("00:16:25:00:00:00")));
  }

  [Fact]
  public void ExpectedHostnamesFollowTheVendor()
  {
    Assert.Equal(["SpeedwayR-12-59-43"], HostnameHints.ExpectedHostnames(MacFormat.Parse("00:16:25:12:59:43")));
    Assert.Equal(["FX7500FB6C10", "FX9600FB6C10"], HostnameHints.ExpectedHostnames(MacFormat.Parse("84:24:8D:FB:6C:10")));
    Assert.Equal(3, HostnameHints.ExpectedHostnames(MacFormat.Parse("a4:83:e7:11:22:33")).Count);
  }

  [Theory]
  [InlineData("SpeedwayR-12-59-43", ReaderVendor.Impinj, null)]
  [InlineData("SpeedwayR-12-59-43.local.", ReaderVendor.Impinj, null)]
  [InlineData("impinj-r700", ReaderVendor.Impinj, null)]
  [InlineData("FX9600ABC123", ReaderVendor.Zebra, "FX9600")]
  [InlineData("fx7500ed7a5b.local", ReaderVendor.Zebra, "FX7500")]
  [InlineData("FXR90AB12CD", ReaderVendor.Zebra, "FXR90")]
  [InlineData("DESKTOP-ABC123", null, null)]
  [InlineData("", null, null)]
  public void HostnameGivesVendorAndModel(string hostname, ReaderVendor? vendor, string? model)
  {
    Assert.Equal(vendor, HostnameHints.VendorFromHostname(hostname));
    Assert.Equal(model, HostnameHints.ModelFromHostname(hostname));
  }
}
