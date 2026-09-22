using System.Globalization;

namespace ReaderDetect.Management.Impinj;

/// <summary>
/// A parsed RShell answer. Every command answers with a <c>Status='code,text'</c>
/// line followed by <c>Key='value'</c> lines; code 0 is success.
/// </summary>
/// <param name="StatusCode">The numeric status; -1 when no status line was found.</param>
/// <param name="StatusText">The status text, e.g. <c>Success</c> or <c>Invalid-Command</c>.</param>
/// <param name="Values">The key/value lines in order of appearance.</param>
public sealed record RShellResponse(int StatusCode, string StatusText, IReadOnlyDictionary<string, string> Values)
{
  /// <summary>True for status code 0.</summary>
  public bool Success => StatusCode == 0;

  /// <summary>Parses the text an RShell command printed.</summary>
  public static RShellResponse Parse(string text)
  {
    var code = -1;
    var status = "no Status line in response";
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var seenStatus = false;
    foreach (var raw in text.Split('\n'))
    {
      var line = raw.Trim().TrimEnd('\r');
      var eq = line.IndexOf('=');
      if (eq <= 0) continue;
      var key = line[..eq].Trim();
      var value = line[(eq + 1)..].Trim();
      if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') value = value[1..^1];
      if (!seenStatus && key.Equals("Status", StringComparison.OrdinalIgnoreCase))
      {
        seenStatus = true;
        var comma = value.IndexOf(',');
        var codeText = comma < 0 ? value : value[..comma];
        code = int.TryParse(codeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        status = comma < 0 ? value : value[(comma + 1)..];
        continue;
      }

      values.TryAdd(key, value);
    }

    return new RShellResponse(code, status, values);
  }

  /// <summary>A value by key, or null.</summary>
  public string? this[string key] => Values.TryGetValue(key, out var value) ? value : null;
}
