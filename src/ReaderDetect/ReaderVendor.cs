namespace ReaderDetect;

/// <summary>The reader manufacturers this tool knows how to identify and configure.</summary>
public enum ReaderVendor
{
  /// <summary>Not identified (or a vendor we have no fingerprints for).</summary>
  Unknown,

  /// <summary>Impinj Speedway / xArray / R700 family (IANA PEN 25882).</summary>
  Impinj,

  /// <summary>Zebra (formerly Motorola/Symbol) FX series (IANA PEN 161).</summary>
  Zebra,
}
