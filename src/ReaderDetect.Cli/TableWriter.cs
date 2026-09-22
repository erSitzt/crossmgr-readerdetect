namespace ReaderDetect.Cli;

/// <summary>Fixed-width text tables; wide enough for a terminal, plain enough to paste into a chat.</summary>
internal static class TableWriter
{
  public static void Write(TextWriter output, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
  {
    var widths = headers.Select(h => h.Length).ToArray();
    foreach (var row in rows)
    {
      for (var i = 0; i < widths.Length && i < row.Count; i++) widths[i] = Math.Max(widths[i], row[i].Length);
    }

    output.WriteLine(Line(headers, widths));
    output.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
    foreach (var row in rows) output.WriteLine(Line(row, widths));
  }

  private static string Line(IReadOnlyList<string> cells, int[] widths) =>
    string.Join("  ", widths.Select((w, i) => (i < cells.Count ? cells[i] : string.Empty).PadRight(w))).TrimEnd();
}
