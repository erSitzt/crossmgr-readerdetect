namespace ReaderDetect.Management;

/// <summary>Login for a reader's management interface.</summary>
/// <param name="Username">User name.</param>
/// <param name="Password">Password; never logged.</param>
public sealed record ReaderCredentials(string Username, string Password)
{
  /// <summary>
  /// The factory login: Impinj <c>root</c>/<c>impinj</c> (RShell over SSH),
  /// Zebra <c>admin</c>/<c>change</c> (web console). Newer Zebra firmware
  /// forces a password change on first login, so the Zebra default is a
  /// starting point rather than a promise.
  /// </summary>
  /// <exception cref="ConfigurationException">The vendor has no known management login.</exception>
  public static ReaderCredentials DefaultFor(ReaderVendor vendor) => vendor switch
  {
    ReaderVendor.Impinj => new ReaderCredentials("root", "impinj"),
    ReaderVendor.Zebra => new ReaderCredentials("admin", "change"),
    _ => throw new ConfigurationException(ConfigurationFailure.Unsupported, $"no management login is known for vendor {vendor}"),
  };

  /// <summary>True when these are the factory credentials for <paramref name="vendor"/>.</summary>
  public bool IsDefaultFor(ReaderVendor vendor) =>
    vendor is ReaderVendor.Impinj or ReaderVendor.Zebra && this == DefaultFor(vendor);

  /// <inheritdoc/>
  public override string ToString() => $"{Username}/*****";
}
