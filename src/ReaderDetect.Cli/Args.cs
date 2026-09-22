namespace ReaderDetect.Cli;

/// <summary>Tiny option helpers; the CLI is small enough that a parser library would be more code than this.</summary>
internal static class Args
{
  /// <summary>Value after <paramref name="name"/>, or null when absent.</summary>
  public static string? Option(string[] args, string name)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    }

    return null;
  }

  /// <summary>All values given after repeated occurrences of <paramref name="name"/>.</summary>
  public static List<string> Options(string[] args, string name)
  {
    var values = new List<string>();
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) values.Add(args[i + 1]);
    }

    return values;
  }

  /// <summary>True when the flag is present.</summary>
  public static bool Flag(string[] args, string name) =>
    args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

  /// <summary>Positional arguments: everything that is neither an option name nor an option value.</summary>
  public static List<string> Positional(string[] args, params string[] optionsWithValues)
  {
    var result = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
      if (args[i].StartsWith("--", StringComparison.Ordinal))
      {
        if (optionsWithValues.Contains(args[i], StringComparer.OrdinalIgnoreCase)) i++;
        continue;
      }

      result.Add(args[i]);
    }

    return result;
  }
}
