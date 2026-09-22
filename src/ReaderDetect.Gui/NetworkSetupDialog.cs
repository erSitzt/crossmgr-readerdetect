using System.Net;
using ReaderDetect.Management;
using ReaderDetect.Network;

namespace ReaderDetect.Gui;

/// <summary>
/// Read and change one reader's IP settings. Credentials start at the vendor
/// default and live only in this dialog; nothing is written to disk.
/// </summary>
internal sealed class NetworkSetupDialog : Form
{
  private readonly ReaderInfo _reader;
  private readonly ScanOptions _scanOptions;
  private readonly TextBox _user = new() { Width = 160 };
  private readonly TextBox _password = new() { Width = 160, UseSystemPasswordChar = true };
  private readonly CheckBox _showPassword = new() { Text = "show", AutoSize = true };
  private readonly Button _read = new() { Text = "Read current", AutoSize = true };
  private readonly TextBox _current = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 420, Height = 140 };
  private readonly RadioButton _dhcp = new() { Text = "DHCP (address from the router)", AutoSize = true, Checked = true };
  private readonly RadioButton _static = new() { Text = "Static address", AutoSize = true };
  private readonly TextBox _address = new() { Width = 160 };
  private readonly TextBox _mask = new() { Width = 160, Text = "255.255.255.0" };
  private readonly TextBox _gateway = new() { Width = 160 };
  private readonly TextBox _hostname = new() { Width = 160, PlaceholderText = "(keep)" };
  private readonly Button _apply = new() { Text = "Apply", AutoSize = true };
  private readonly Button _reboot = new() { Text = "Reboot reader", AutoSize = true };
  private readonly Button _close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
  private readonly Label _status = new() { AutoSize = false, Width = 520, Height = 40, TextAlign = ContentAlignment.MiddleLeft };
  private IReaderConfigurator? _configurator;

  public NetworkSetupDialog(ReaderInfo reader, ScanOptions scanOptions)
  {
    _reader = reader;
    _scanOptions = scanOptions;
    Text = $"Network setup — {reader.Ip} {reader.Vendor} {reader.Model}";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = false;
    MinimizeBox = false;
    StartPosition = FormStartPosition.CenterParent;
    ClientSize = new Size(560, 560);
    CancelButton = _close;

    var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    void Row(string label, Control control)
    {
      var row = layout.RowCount++;
      layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);
      layout.Controls.Add(control, 1, row);
    }

    Row("Reader", new Label { Text = $"{reader.Vendor} {reader.Model}  {reader.Ip}  {reader.MacText}  {reader.Hostname}", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
    Row("Username", _user);
    var passwordRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
    passwordRow.Controls.AddRange([_password, _showPassword]);
    Row("Password", passwordRow);
    Row("", _read);
    Row("Current", _current);
    var mode = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    mode.Controls.AddRange([_dhcp, _static]);
    Row("New mode", mode);
    Row("Address", _address);
    Row("Mask", _mask);
    Row("Gateway", _gateway);
    Row("Hostname", _hostname);
    var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
    buttons.Controls.AddRange([_apply, _reboot, _close]);
    Row("", buttons);
    Row("", _status);
    Controls.Add(layout);

    _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
    _dhcp.CheckedChanged += (_, _) => ToggleStatic();
    _read.Click += async (_, _) => await ReadAsync();
    _apply.Click += async (_, _) => await ApplyAsync();
    _reboot.Click += async (_, _) => await RebootAsync();

    try
    {
      var defaults = ReaderCredentials.DefaultFor(reader.Vendor);
      _user.Text = defaults.Username;
      _password.Text = defaults.Password;
      _configurator = ReaderConfigurators.For(reader.Vendor);
      _status.Text = "Credentials are the factory default; change them if the reader's login was changed.";
    }
    catch (ConfigurationException ex)
    {
      _status.Text = ex.Message;
      _read.Enabled = _apply.Enabled = _reboot.Enabled = false;
    }

    ToggleStatic();
  }

  /// <summary>The reader as seen after a successful change, for the main grid.</summary>
  public ReaderInfo? Result { get; private set; }

  private ReaderCredentials Credentials => new(_user.Text.Trim(), _password.Text);

  private void ToggleStatic()
  {
    var isStatic = _static.Checked;
    _address.Enabled = _mask.Enabled = _gateway.Enabled = isStatic;
    if (isStatic && _address.Text.Length == 0) _address.Text = _reader.Ip.ToString();
  }

  private void Busy(bool busy)
  {
    _read.Enabled = _apply.Enabled = _reboot.Enabled = !busy;
    UseWaitCursor = busy;
  }

  private async Task ReadAsync()
  {
    if (_configurator is null) return;
    Busy(true);
    _status.Text = "Reading…";
    try
    {
      var settings = await Task.Run(() => _configurator.GetNetworkAsync(_reader.Ip, Credentials));
      _current.Text = Describe(settings);
      if (settings.Dhcp) _dhcp.Checked = true;
      else _static.Checked = true;
      if (settings.Ip is not null) _address.Text = settings.Ip.ToString();
      if (settings.Mask is not null) _mask.Text = settings.Mask.ToString();
      if (settings.Gateway is not null) _gateway.Text = settings.Gateway.ToString();
      _status.Text = "Current settings read.";
    }
    catch (Exception ex)
    {
      _status.Text = Explain(ex);
    }
    finally
    {
      Busy(false);
    }
  }

  private NetworkSettings? Desired()
  {
    var hostname = _hostname.Text.Trim().Length == 0 ? null : _hostname.Text.Trim();
    if (_dhcp.Checked) return NetworkSettings.ForDhcp(hostname);
    if (!IPAddress.TryParse(_address.Text.Trim(), out var ip) || !IPAddress.TryParse(_mask.Text.Trim(), out var mask))
    {
      _status.Text = "Address and mask must be IPv4 addresses.";
      return null;
    }

    IPAddress? gateway = null;
    if (_gateway.Text.Trim().Length > 0 && !IPAddress.TryParse(_gateway.Text.Trim(), out gateway))
    {
      _status.Text = "Gateway must be an IPv4 address (or empty).";
      return null;
    }

    try
    {
      return NetworkSettings.ForStatic(ip, mask, gateway, null, hostname);
    }
    catch (ArgumentException ex)
    {
      _status.Text = ex.Message;
      return null;
    }
  }

  private async Task ApplyAsync()
  {
    if (_configurator is null || Desired() is not { } desired) return;
    var plan = string.Join(Environment.NewLine, _configurator.Plan(desired));
    var warning = desired.Ip is { } target && !(_scanOptions.Interfaces ?? NetworkInterfaces.Enumerate()).Any(n => n.Subnet.Contains(target))
      ? Environment.NewLine + Environment.NewLine + $"Warning: {target} is outside this PC's subnets; the reader will not be reachable from here afterwards."
      : "";
    var answer = MessageBox.Show(this, $"Set {_reader.Ip} to {desired.Describe()}?{Environment.NewLine}{Environment.NewLine}{plan}{warning}",
      "Apply network settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;

    Busy(true);
    try
    {
      _status.Text = "Applying…";
      var result = await Task.Run(() => _configurator.SetNetworkAsync(_reader.Ip, Credentials, desired));
      _status.Text = result.ConnectionDropped ? "Applied; waiting for the reader at its new address…" : "Applied; waiting for the reader…";
      var scanner = new ReaderScanner(_scanOptions);
      var timeout = TimeSpan.FromSeconds(result.RebootRequested ? 90 : result.ExpectedAddress is null ? 60 : 30);
      var seen = await Task.Run(() => ReaderConfigurators.WaitForReaderAsync(result.ExpectedAddress, _reader.Mac, scanner, timeout,
        previous: _reader.Ip, rebooting: result.RebootRequested));
      if (seen is null)
      {
        _status.Text = result.ExpectedAddress is null
          ? "Applied, but the reader has not shown up yet. Scan again in a moment."
          : $"Applied, but nothing answers at {result.ExpectedAddress} yet. Scan again in a moment.";
        return;
      }

      Result = seen;
      _status.Text = $"Reader is back at {seen.Ip}.";
      DialogResult = DialogResult.OK;
    }
    catch (Exception ex)
    {
      _status.Text = Explain(ex);
    }
    finally
    {
      Busy(false);
    }
  }

  private async Task RebootAsync()
  {
    if (_configurator is null) return;
    if (MessageBox.Show(this, $"Reboot the reader at {_reader.Ip}?", "Reboot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
    Busy(true);
    try
    {
      await Task.Run(() => _configurator.RebootAsync(_reader.Ip, Credentials));
      _status.Text = "Reboot requested.";
    }
    catch (Exception ex)
    {
      _status.Text = Explain(ex);
    }
    finally
    {
      Busy(false);
    }
  }

  private static string Describe(NetworkSettings s)
  {
    var lines = new List<string>
    {
      $"mode: {(s.Dhcp ? "DHCP" : "static")}",
      $"address: {s.Ip}   mask: {s.Mask}   gateway: {s.Gateway}",
      $"hostname: {s.Hostname}   mac: {(s.Mac is null ? "" : MacFormat.Colon(s.Mac))}",
    };
    lines.AddRange(s.Raw.Select(kv => $"  {kv.Key} = {kv.Value}"));
    return string.Join(Environment.NewLine, lines);
  }

  private static string Explain(Exception ex) => ex switch
  {
    ConfigurationException { Kind: ConfigurationFailure.AuthFailed } => ex.Message + " Enter the reader's current login above.",
    _ => ex.Message,
  };
}
