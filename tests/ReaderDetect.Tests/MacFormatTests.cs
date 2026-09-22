using ReaderDetect.Network;

namespace ReaderDetect.Tests;

public class MacFormatTests
{
  [Theory]
  [InlineData("00:16:25:12:59:43")]
  [InlineData("00-16-25-12-59-43")]
  [InlineData("001625125943")]
  [InlineData("0:16:25:12:59:43")]
  [InlineData(" 00:16:25:12:59:43 ")]
  public void ParsesCommonSpellings(string text)
  {
    Assert.Equal("00:16:25:12:59:43", MacFormat.Colon(MacFormat.Parse(text)));
  }

  [Theory]
  [InlineData("")]
  [InlineData("00:16:25:12:59")]
  [InlineData("00:16:25:12:59:zz")]
  [InlineData("0016251259")]
  [InlineData("192.168.68.139")]
  public void RejectsGarbage(string text)
  {
    Assert.False(MacFormat.TryParse(text, out _));
    Assert.Throws<FormatException>(() => MacFormat.Parse(text));
  }

  [Fact]
  public void DerivedPieces()
  {
    var mac = MacFormat.Parse("00:16:25:12:59:43");
    Assert.Equal("00:16:25", MacFormat.Oui(mac));
    Assert.Equal("12-59-43", MacFormat.LastThreeOctetsHyphen(mac));
    Assert.Equal("125943", MacFormat.LastSixHex(mac));
  }
}
