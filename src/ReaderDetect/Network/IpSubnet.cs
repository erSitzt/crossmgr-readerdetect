using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ReaderDetect.Network;

/// <summary>
/// An IPv4 network in CIDR form. Only IPv4: the readers this tool cares about
/// are managed over IPv4 and a sweep of an IPv6 prefix is not a thing.
/// </summary>
public readonly record struct IpSubnet
{
  /// <summary>Creates a subnet; the address is normalised to the network address.</summary>
  /// <exception cref="ArgumentException">Not IPv4, or the prefix is out of range.</exception>
  public IpSubnet(IPAddress network, int prefixLength)
  {
    if (network.AddressFamily != AddressFamily.InterNetwork)
    {
      throw new ArgumentException("only IPv4 subnets are supported", nameof(network));
    }

    if (prefixLength is < 0 or > 32)
    {
      throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength, "prefix must be 0..32");
    }

    PrefixLength = prefixLength;
    NetworkValue = ToUInt32(network) & MaskValue(prefixLength);
  }

  /// <summary>Prefix length, 0..32.</summary>
  public int PrefixLength { get; }

  private uint NetworkValue { get; }

  /// <summary>The network address, e.g. 192.168.68.0.</summary>
  public IPAddress Network => FromUInt32(NetworkValue);

  /// <summary>The subnet mask, e.g. 255.255.255.0.</summary>
  public IPAddress Mask => FromUInt32(MaskValue(PrefixLength));

  /// <summary>The broadcast address, e.g. 192.168.68.255.</summary>
  public IPAddress Broadcast => FromUInt32(NetworkValue | ~MaskValue(PrefixLength));

  /// <summary>Number of usable host addresses (network and broadcast excluded below /31).</summary>
  public long HostCount => PrefixLength switch
  {
    32 => 1,
    31 => 2,
    _ => (1L << (32 - PrefixLength)) - 2,
  };

  /// <summary>Parses <c>192.168.68.0/24</c>; a host address like <c>192.168.68.5/24</c> is accepted and normalised.</summary>
  /// <exception cref="FormatException">Not a valid IPv4 CIDR.</exception>
  public static IpSubnet Parse(string cidr) =>
    TryParse(cidr, out var subnet) ? subnet : throw new FormatException($"'{cidr}' is not an IPv4 CIDR like 192.168.1.0/24");

  /// <summary>Non-throwing variant of <see cref="Parse"/>.</summary>
  public static bool TryParse(string? cidr, out IpSubnet subnet)
  {
    subnet = default;
    if (string.IsNullOrWhiteSpace(cidr)) return false;
    var slash = cidr.IndexOf('/');
    if (slash < 0) return false;
    if (!IPAddress.TryParse(cidr.AsSpan(0, slash).Trim(), out var address) ||
        address.AddressFamily != AddressFamily.InterNetwork ||
        !int.TryParse(cidr.AsSpan(slash + 1).Trim(), out var prefix) || prefix is < 0 or > 32)
    {
      return false;
    }

    subnet = new IpSubnet(address, prefix);
    return true;
  }

  /// <summary>Builds the subnet an interface lives in from its address and mask.</summary>
  public static IpSubnet FromAddress(IPAddress address, IPAddress mask) => new(address, PrefixFromMask(mask));

  /// <summary>Converts a dotted mask to a prefix length.</summary>
  /// <exception cref="ArgumentException">The mask is not contiguous ones followed by zeros.</exception>
  public static int PrefixFromMask(IPAddress mask)
  {
    if (mask.AddressFamily != AddressFamily.InterNetwork)
    {
      throw new ArgumentException("only IPv4 masks are supported", nameof(mask));
    }

    var value = ToUInt32(mask);
    var prefix = 0;
    while (prefix < 32 && (value & (0x8000_0000u >> prefix)) != 0) prefix++;
    if (value != MaskValue(prefix))
    {
      throw new ArgumentException($"{mask} is not a contiguous subnet mask", nameof(mask));
    }

    return prefix;
  }

  /// <summary>True when the mask is contiguous ones followed by zeros.</summary>
  public static bool IsValidMask(IPAddress mask)
  {
    try
    {
      PrefixFromMask(mask);
      return true;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  /// <summary>True when the address is inside this subnet (network and broadcast included).</summary>
  public bool Contains(IPAddress address) =>
    address.AddressFamily == AddressFamily.InterNetwork &&
    (ToUInt32(address) & MaskValue(PrefixLength)) == NetworkValue;

  /// <summary>
  /// The host addresses to sweep. A subnet larger than <paramref name="maxHosts"/>
  /// is not swept whole: a /16 is 65k connection attempts and almost always a
  /// misconfigured mask rather than a real network. Instead the aligned block
  /// of <paramref name="maxHosts"/> addresses around <paramref name="anchor"/>
  /// (normally the interface's own address) is returned and <paramref name="capped"/> is set.
  /// </summary>
  public IEnumerable<IPAddress> Hosts(int maxHosts, IPAddress? anchor, out bool capped)
  {
    if (maxHosts < 2) throw new ArgumentOutOfRangeException(nameof(maxHosts));
    capped = HostCount > maxHosts;
    if (!capped)
    {
      return Range(PrefixLength, NetworkValue);
    }

    // Largest power-of-two block that fits in maxHosts (+2 for network/broadcast).
    var blockPrefix = 32;
    while (blockPrefix > PrefixLength && (1L << (32 - blockPrefix)) - 2 < maxHosts) blockPrefix--;
    if ((1L << (32 - blockPrefix)) - 2 > maxHosts) blockPrefix++;

    var anchorValue = anchor is { AddressFamily: AddressFamily.InterNetwork } && Contains(anchor)
      ? ToUInt32(anchor)
      : NetworkValue;
    return Range(blockPrefix, anchorValue & MaskValue(blockPrefix));
  }

  /// <inheritdoc/>
  public override string ToString() => $"{Network}/{PrefixLength}";

  private static IEnumerable<IPAddress> Range(int prefix, uint network)
  {
    var mask = MaskValue(prefix);
    var first = prefix >= 31 ? network : network + 1;
    var last = prefix >= 31 ? (network | ~mask) : (network | ~mask) - 1;
    for (var value = (long)first; value <= last; value++)
    {
      yield return FromUInt32((uint)value);
    }
  }

  private static uint MaskValue(int prefix) => prefix == 0 ? 0u : 0xFFFF_FFFFu << (32 - prefix);

  private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

  private static IPAddress FromUInt32(uint value)
  {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    return new IPAddress(bytes);
  }
}
