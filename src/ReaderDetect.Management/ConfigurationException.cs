namespace ReaderDetect.Management;

/// <summary>Why a management operation could not be completed.</summary>
public enum ConfigurationFailure
{
  /// <summary>The reader rejected the credentials.</summary>
  AuthFailed,

  /// <summary>The reader is throttling logins (Impinj readers do this after a burst of SSH logins).</summary>
  Throttled,

  /// <summary>This vendor or model has no supported management path.</summary>
  Unsupported,

  /// <summary>The reader answered but refused the command.</summary>
  CommandFailed,

  /// <summary>Nothing answered on the management port.</summary>
  NotReachable,

  /// <summary>The connection dropped while a command was running.</summary>
  ConnectionDropped,
}

/// <summary>A management operation failed in a way the UI should explain.</summary>
public sealed class ConfigurationException : Exception
{
  /// <summary>Creates the exception.</summary>
  public ConfigurationException(ConfigurationFailure kind, string message, Exception? inner = null)
    : base(message, inner)
  {
    Kind = kind;
  }

  /// <summary>The failure category.</summary>
  public ConfigurationFailure Kind { get; }
}
