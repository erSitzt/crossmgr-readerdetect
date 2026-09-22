namespace ReaderDetect.Tests;

/// <summary>Byte helpers for the wire-format tests.</summary>
internal static class TestBytes
{
  public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

  public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
