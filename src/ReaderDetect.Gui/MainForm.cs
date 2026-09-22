using System.ComponentModel;
using System.Diagnostics;
using ReaderDetect.Network;

namespace ReaderDetect.Gui;

/// <summary>
/// One window: pick interfaces, scan, see readers, copy an address or open
/// the web UI. All discovery logic lives in the library; this form only binds.
/// </summary>
internal sealed class MainForm : Form
{
  private readonly CheckedListBox _interfaces = new() { CheckOnClick = true, IntegralHeight = false, Dock = DockStyle.Fill };
  private readonly CheckBox _includeVirtual = new() { Text = "Include VPN/virtual adapters", AutoSize = true };
  private readonly TextBox _subnet = new() { PlaceholderText = "extra subnet, e.g. 192.168.1.0/24", Width = 220 };
  private readonly Button _scan = new() { Text = "Scan", Width = 90 };
  private readonly Button _cancel = new() { Text = "Cancel", Width = 90, Enabled = false };
  private readonly DataGridView _grid = new()
  {
    Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
    SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoGenerateColumns = false,
    RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
  };
  private readonly BindingList<ReaderRow> _rows = [];
  private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
  private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
  private CancellationTokenSource? _cts;

  public MainForm()
  {
    Text = "Reader Detect";
    Width = 1100;
    Height = 620;
    MinimumSize = new Size(700, 400);

    foreach (var (name, property) in new[]
             {
               ("IP", nameof(ReaderRow.Ip)), ("Hostname", nameof(ReaderRow.Hostname)), ("Vendor", nameof(ReaderRow.Vendor)),
               ("Model", nameof(ReaderRow.Model)), ("Firmware", nameof(ReaderRow.Firmware)), ("MAC", nameof(ReaderRow.Mac)),
               ("LLRP", nameof(ReaderRow.Llrp)), ("Found via", nameof(ReaderRow.FoundVia)), ("Confidence", nameof(ReaderRow.Confidence)),
               ("Note", nameof(ReaderRow.Note)),
             })
    {
      _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, DataPropertyName = property });
    }

    _grid.DataSource = _rows;
    _grid.ContextMenuStrip = BuildMenu();
    _grid.CellDoubleClick += (_, _) => CopyIp();

    var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
    top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    top.Controls.Add(_interfaces, 0, 0);
    var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
    buttons.Controls.AddRange([_scan, _cancel, _includeVirtual, _subnet]);
    top.Controls.Add(buttons, 1, 0);

    var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    layout.Controls.Add(top, 0, 0);
    layout.Controls.Add(_grid, 0, 1);
    layout.Controls.Add(_progress, 0, 2);
    layout.Controls.Add(_status, 0, 3);
    Controls.Add(layout);

    _scan.Click += async (_, _) => await ScanAsync();
    _cancel.Click += (_, _) => _cts?.Cancel();
    _includeVirtual.CheckedChanged += (_, _) => LoadInterfaces();
    Load += (_, _) => LoadInterfaces();
  }

  private ContextMenuStrip BuildMenu()
  {
    var menu = new ContextMenuStrip();
    menu.Items.Add("Copy IP", null, (_, _) => CopyIp());
    menu.Items.Add("Open web UI", null, (_, _) => OpenWeb());
    menu.Items.Add("Re-probe", null, async (_, _) => await ReprobeAsync());
    menu.Items.Add("Network setup…", null, (_, _) => NetworkSetup());
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add("Copy table as text", null, (_, _) => CopyTable());
    return menu;
  }

  private void LoadInterfaces()
  {
    _interfaces.Items.Clear();
    foreach (var nic in ReaderScanner.Interfaces(_includeVirtual.Checked)) _interfaces.Items.Add(nic, isChecked: !nic.IsVirtual);
    _status.Text = _interfaces.Items.Count == 0 ? "No usable network interface found." : $"{_interfaces.Items.Count} interface(s).";
  }

  private ScanOptions BuildOptions()
  {
    var options = new ScanOptions
    {
      Interfaces = _interfaces.CheckedItems.Cast<NetworkInterfaceInfo>().ToList(),
      IncludeVirtualInterfaces = _includeVirtual.Checked,
    };
    if (IpSubnet.TryParse(_subnet.Text, out var subnet)) options.ExplicitSubnets = [subnet];
    return options;
  }

  private ReaderRow? Selected => _grid.CurrentRow?.DataBoundItem as ReaderRow;

  private async Task ScanAsync()
  {
    if (_cts is not null) return;
    var options = BuildOptions();
    if (options.Interfaces!.Count == 0)
    {
      MessageBox.Show(this, "Tick at least one interface.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    _cts = new CancellationTokenSource();
    _scan.Enabled = false;
    _cancel.Enabled = true;
    _rows.Clear();
    _progress.Style = ProgressBarStyle.Marquee;
    var progress = new Progress<ScanProgress>(OnProgress);
    try
    {
      var scanner = new ReaderScanner(options);
      var token = _cts.Token;
      var result = await Task.Run(() => scanner.ScanAsync(progress, token), token);
      foreach (var reader in result.Readers) Upsert(reader);
      _status.Text = $"{result.Readers.Count} reader(s) in {result.Elapsed.TotalSeconds:F1} s" +
                     (result.Warnings.Count > 0 ? "  |  " + string.Join("; ", result.Warnings) : "");
    }
    catch (OperationCanceledException)
    {
      _status.Text = "Scan cancelled.";
    }
    catch (Exception ex)
    {
      _status.Text = "Scan failed.";
      MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      _progress.Style = ProgressBarStyle.Continuous;
      _progress.Value = 0;
      _scan.Enabled = true;
      _cancel.Enabled = false;
      _cts.Dispose();
      _cts = null;
    }
  }

  private void OnProgress(ScanProgress tick)
  {
    if (tick.Reader is not null) Upsert(tick.Reader);
    if (tick.Total > 0)
    {
      _progress.Style = ProgressBarStyle.Continuous;
      _progress.Maximum = tick.Total;
      _progress.Value = Math.Min(tick.Done, tick.Total);
    }

    _status.Text = tick.Phase switch
    {
      ScanPhase.PortSweep => $"Sweeping {tick.Done}/{tick.Total}, found {tick.Found}",
      ScanPhase.Done => _status.Text,
      _ => $"{tick.Message}, found {tick.Found}",
    };
  }

  private void Upsert(ReaderInfo info)
  {
    var existing = _rows.FirstOrDefault(r => r.Info.Ip.Equals(info.Ip));
    if (existing is null)
    {
      _rows.Add(new ReaderRow(info));
    }
    else
    {
      existing.Update(info);
      _rows.ResetItem(_rows.IndexOf(existing));
    }
  }

  private void CopyIp()
  {
    if (Selected is { } row) Clipboard.SetText(row.Ip);
  }

  private void OpenWeb()
  {
    if (Selected is { } row) Process.Start(new ProcessStartInfo(row.Info.WebUrl) { UseShellExecute = true });
  }

  private void CopyTable()
  {
    var lines = _rows.Select(r => string.Join("\t", r.Ip, r.Hostname, r.Vendor, r.Model, r.Firmware, r.Mac, r.Llrp, r.FoundVia, r.Confidence));
    Clipboard.SetText(string.Join(Environment.NewLine, lines));
  }

  private async Task ReprobeAsync()
  {
    if (Selected is not { } row) return;
    _status.Text = $"Probing {row.Ip}…";
    try
    {
      var info = await Task.Run(() => new ReaderScanner(BuildOptions()).ProbeAsync(row.Info.Ip));
      Upsert(info);
      _status.Text = $"{info.Ip}: {info.Vendor} {info.Model} ({info.Confidence})";
    }
    catch (Exception ex)
    {
      _status.Text = $"Probe failed: {ex.Message}";
    }
  }

  private void NetworkSetup()
  {
    if (Selected is not { } row) return;
    using var dialog = new NetworkSetupDialog(row.Info, BuildOptions());
    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is { } updated) Upsert(updated);
  }
}
