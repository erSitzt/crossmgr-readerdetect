using System.Buffers.Binary;
using System.Net;
using System.Text;
using ReaderDetect.Mdns;

namespace ReaderDetect.Tests;

public class DnsCodecTests
{
  /// <summary>Appends DNS wire data and remembers offsets, so tests can build compression pointers deliberately.</summary>
  private sealed class PacketBuilder
  {
    private readonly List<byte> _bytes = [];

    public int Count => _bytes.Count;

    public PacketBuilder Header(ushort answers)
    {
      U16(0).U16(0x8400).U16(0).U16(answers).U16(0).U16(0);
      return this;
    }

    public PacketBuilder U16(ushort value)
    {
      Span<byte> b = stackalloc byte[2];
      BinaryPrimitives.WriteUInt16BigEndian(b, value);
      _bytes.AddRange(b.ToArray());
      return this;
    }

    public PacketBuilder U32(uint value)
    {
      Span<byte> b = stackalloc byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(b, value);
      _bytes.AddRange(b.ToArray());
      return this;
    }

    public PacketBuilder Label(string label)
    {
      var utf8 = Encoding.UTF8.GetBytes(label);
      _bytes.Add((byte)utf8.Length);
      _bytes.AddRange(utf8);
      return this;
    }

    public PacketBuilder End()
    {
      _bytes.Add(0);
      return this;
    }

    public PacketBuilder Pointer(int offset)
    {
      _bytes.Add((byte)(0xC0 | (offset >> 8)));
      _bytes.Add((byte)(offset & 0xFF));
      return this;
    }

    public PacketBuilder Bytes(params byte[] bytes)
    {
      _bytes.AddRange(bytes);
      return this;
    }

    public PacketBuilder Txt(params string[] strings)
    {
      foreach (var s in strings)
      {
        var utf8 = Encoding.UTF8.GetBytes(s);
        _bytes.Add((byte)utf8.Length);
        _bytes.AddRange(utf8);
      }

      return this;
    }

    public byte[] ToArray() => [.. _bytes];
  }

  /// <summary>Mirrors the reply the R220 actually sends: PTR, TXT, SRV, AAAA, A in one packet with pointers.</summary>
  private static byte[] SpeedwayReply()
  {
    var p = new PacketBuilder().Header(5);

    // PTR _llrp._tcp.local -> SpeedwayR-12-59-43._llrp._tcp.local
    var serviceName = p.Count;
    p.Label("_llrp");
    var tcp = p.Count;
    p.Label("_tcp");
    var local = p.Count;
    p.Label("local").End();
    p.U16(12).U16(0x8001).U32(4500).U16(21);
    var instance = p.Count;
    p.Label("SpeedwayR-12-59-43").Pointer(serviceName);

    // TXT
    p.Pointer(instance).U16(16).U16(0x8001).U32(4500);
    var txt = new PacketBuilder().Txt("version=1.0", "interface=reader", "security=none", "pen=25882", "vendor-version=octane-7.6").ToArray();
    p.U16((ushort)txt.Length).Bytes(txt);

    // SRV -> SpeedwayR-12-59-43.local:5084
    p.Pointer(instance).U16(33).U16(0x8001).U32(120).U16(6 + 19 + 2).U16(0).U16(0).U16(5084);
    var host = p.Count;
    p.Label("SpeedwayR-12-59-43").Pointer(local);

    // AAAA (ignored by the assembler)
    p.Pointer(host).U16(28).U16(0x8001).U32(120).U16(16).Bytes(new byte[16]);

    // A
    p.Pointer(host).U16(1).U16(0x8001).U32(120).U16(4).Bytes(192, 168, 68, 139);
    _ = tcp;
    return p.ToArray();
  }

  [Fact]
  public void QueryBytesAreExact()
  {
    var query = DnsCodec.BuildQuery("_llrp._tcp.local", DnsRecordType.Ptr, unicastResponse: true);
    Assert.Equal(
      "000000000001000000000000" + "055f6c6c7270" + "045f746370" + "056c6f63616c" + "00" + "000c" + "8001",
      TestBytes.ToHex(query));
    var multicast = DnsCodec.BuildQuery("_llrp._tcp.local", DnsRecordType.Ptr, unicastResponse: false);
    Assert.Equal("0001", TestBytes.ToHex(multicast)[^4..]);
  }

  [Fact]
  public void SpeedwayReplyParsesWithCompression()
  {
    Assert.True(DnsCodec.TryParse(SpeedwayReply(), out var message));
    Assert.True(message.IsResponse);
    Assert.Equal(5, message.Records.Count);

    var ptr = Assert.IsType<PtrRecord>(message.Records[0]);
    Assert.Equal("_llrp._tcp.local", ptr.Name);
    Assert.Equal("SpeedwayR-12-59-43._llrp._tcp.local", ptr.Target);

    var txt = Assert.IsType<TxtRecord>(message.Records[1]);
    Assert.Equal("SpeedwayR-12-59-43._llrp._tcp.local", txt.Name);
    Assert.Equal("25882", txt.Pairs["pen"]);
    Assert.Equal("octane-7.6", txt.Pairs["vendor-version"]);

    var srv = Assert.IsType<SrvRecord>(message.Records[2]);
    Assert.Equal(5084, srv.Port);
    Assert.Equal("SpeedwayR-12-59-43.local", srv.Target);

    Assert.IsType<AaaaRecord>(message.Records[3]);
    var a = Assert.IsType<ARecord>(message.Records[4]);
    Assert.Equal("SpeedwayR-12-59-43.local", a.Name);
    Assert.Equal(IPAddress.Parse("192.168.68.139"), a.Address);
  }

  [Fact]
  public void ForwardOrSelfPointerIsRejected()
  {
    var p = new PacketBuilder().Header(1);
    var start = p.Count;
    p.Pointer(start);   // points at itself
    p.U16(1).U16(1).U32(1).U16(4).Bytes(1, 2, 3, 4);
    Assert.False(DnsCodec.TryParse(p.ToArray(), out _));
  }

  [Fact]
  public void TruncatedRecordIsRejected()
  {
    var p = new PacketBuilder().Header(1).Label("a").End().U16(1).U16(1).U32(1).U16(4).Bytes(1, 2);
    Assert.False(DnsCodec.TryParse(p.ToArray(), out _));
    Assert.False(DnsCodec.TryParse(new byte[5], out _));
  }

  [Fact]
  public void AssembleJoinsTheRecords()
  {
    Assert.True(DnsCodec.TryParse(SpeedwayReply(), out var message));
    var from = IPAddress.Parse("192.168.68.139");
    var services = MdnsBrowser.Assemble("_llrp._tcp.local", [(message, from, null)]);

    var service = Assert.Single(services);
    Assert.Equal("SpeedwayR-12-59-43", service.InstanceName);
    Assert.Equal("_llrp._tcp.local", service.ServiceType);
    Assert.Equal("SpeedwayR-12-59-43.local", service.Host);
    Assert.Equal(5084, service.Port);
    Assert.Equal([from], service.Addresses);
    Assert.Equal("25882", service.Txt["pen"]);
  }

  [Fact]
  public void AssembleFallsBackToTheResponderAndDeduplicates()
  {
    var ptrOnly = new DnsMessage(0, true, [new PtrRecord("_llrp._tcp.local", 4500, "FX9600FB6C10._llrp._tcp.local")]);
    var from = IPAddress.Parse("192.168.68.50");
    var services = MdnsBrowser.Assemble("_llrp._tcp.local", [(ptrOnly, from, null), (ptrOnly, from, null)]);

    var service = Assert.Single(services);
    Assert.Equal("FX9600FB6C10", service.InstanceName);
    Assert.Null(service.Host);
    Assert.Equal(0, service.Port);
    Assert.Equal([from], service.Addresses);
    Assert.Empty(service.Txt);
  }

  [Fact]
  public void AssembleIgnoresOtherServiceTypes()
  {
    var other = new DnsMessage(0, true, [new PtrRecord("_http._tcp.local", 4500, "printer._http._tcp.local")]);
    Assert.Empty(MdnsBrowser.Assemble("_llrp._tcp.local", [(other, IPAddress.Loopback, null)]));
  }
}
