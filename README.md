# crossmgr-readerdetect

Finds the RFID readers on the local network that the timing setup uses — Impinj
Speedway R220/R420 (and other Impinj LLRP readers) and Zebra FX7500/FX9600 —
and can switch an Impinj reader between DHCP and a static address.

It exists because the timing app only listens for reads; somebody still has to
find out what address the reader got from the router this morning.

## What you get

| Executable | What it is |
|---|---|
| `readerdetect.exe` | Console tool: `scan`, `interfaces`, `probe`, `config …`. Prints a table or `--json`. |
| `readerdetect-gui.exe` | One window: tick interfaces, Scan, right-click a reader to copy its IP, open its web UI or change its network settings. |

Both are self-contained single-file Windows x64 builds; nothing needs to be
installed on the timing laptop. Unsigned, so SmartScreen will ask once.

## Using the console tool

```
readerdetect                      scan every Ethernet/Wi-Fi interface
readerdetect scan --json          same, machine-readable
readerdetect interfaces           list the interfaces it would scan
readerdetect probe 192.168.68.139 identify one address
readerdetect config show   192.168.68.139
readerdetect config static 192.168.68.139 --address 192.168.68.200 --mask 255.255.255.0 --gateway 192.168.68.1
readerdetect config dhcp   192.168.68.200
readerdetect config reboot 192.168.68.200
```

A scan of a /24 takes about two seconds. Example:

```
IP              Hostname            Vendor  Model          Firmware   MAC                LLRP  Found via   Confidence
--------------  ------------------  ------  -------------  ---------  -----------------  ----  ----------  ----------
192.168.68.139  SpeedwayR-12-59-43  Impinj  Speedway R220  7.6.3.240  00:16:25:12:59:43  free  mdns+sweep  confirmed
```

`LLRP` says whether the reader's LLRP port is free or already taken by another
client (normally: the timing bridge is connected). `Confidence` is *confirmed*
when the reader reported its capabilities over LLRP, *likely* when it answered
LLRP or advertised itself but could not be read, *possible* when only its MAC
prefix or hostname looks like a reader.

Options: `--interface <name|ip>` (repeatable), `--subnet <cidr>` (sweep a
subnet other than the interface's own), `--no-sweep`, `--no-mdns`, `--no-wsd`,
`--include-virtual` (VPN/VM adapters are skipped by default), `--timeout-ms`,
`--max-hosts`, `--verbose`.

Exit codes: `scan`/`probe` 0 found, 1 nothing found, 2 usage or error.
`config` 0 applied and the reader was seen again, 3 applied but not seen yet,
4 login rejected, 5 unsupported reader or login throttled, 2 usage or error.

## Changing a reader's address

`config dhcp|static` shows what it is about to send, asks for confirmation
(`--yes` to skip, required when scripting), applies the change, reboots the
reader when the firmware asks for it, and then waits until the reader answers
again — at the new static address, or, after switching to DHCP, wherever it
shows up with the same MAC. `--dry-run` only prints the commands.

Credentials default to the vendor's factory login: Impinj `root`/`impinj`,
Zebra `admin`/`change`. If the login was changed, pass `--user`/`--password`,
or put the password in the `READERDETECT_PASSWORD` environment variable so it
stays out of the shell history. The GUI has the same fields in its
*Network setup…* dialog; it keeps them in memory only.

**Impinj** (Speedway, Octane firmware) is done over RShell on SSH:
`show network summary`, `config network ip dynamic`,
`config network ip static <ip> <mask> <gateway>`, `config network hostname`,
`reboot`. Octane 7.x answers `14,Success-Reboot-Required` and the address only
changes after the reboot; older firmware applies it immediately. Verified on an
R220 with Octane 7.6.3.240. Impinj readers throttle repeated SSH logins: after
a burst of them authentication silently hangs for several minutes. The tool
opens one SSH connection per operation and reports the hang as "throttled";
if you see that, wait a few minutes.

**Zebra FX**: discovery works (WS-Discovery, LLRP identify, MAC prefix,
`FX7500…`/`FX9600…` hostnames), but changing network settings is **not
implemented** in this version; `config` reports it as unsupported. Use the
reader's web console (`http://<ip>/`).

A warning is printed when the static address you chose is outside this PC's
subnets, because the reader will not be reachable from here afterwards.

## How detection works

Three sources run at once on every selected interface and feed one LLRP
identify queue:

1. **mDNS** — Impinj readers advertise `_llrp._tcp` (instance
   `SpeedwayR-xx-xx-xx`, TXT `pen=25882`). The query is sent from an
   ephemeral port with the unicast-response bit, so replies come back to us
   regardless of who owns port 5353 (Windows' own responder does). A
   best-effort multicast listener on 5353 is added when the OS allows it.
2. **WS-Discovery** — Zebra FX readers implement it; a typeless Probe is
   multicast to 239.255.255.250:3702 (both the 2005 and OASIS 2009
   namespaces). Windows PCs and printers answer too and are dropped later.
3. **TCP sweep** of port 5084 across the subnet (256 parallel connects,
   600 ms each; a /24 takes about a second; larger subnets are capped at
   1024 hosts around the interface address).

Every candidate then gets the LLRP identify handshake: connect, read the
reader's connection-attempt event, and if the slot is free ask for
`GET_READER_CAPABILITIES` (vendor PEN, model code, firmware) and close. It
holds the connection for about 100 ms and changes nothing. If another client
is connected the reader says so and the row shows *in use*. MAC (ARP),
reverse DNS, the factory hostname and the web banner fill the rest in.

Do not scan in the middle of a race: the 100 ms handshake could make the
bridge's reconnect attempt fail once if it happens at the same instant.

## Windows notes

- No admin rights are needed. The only inbound listener is the optional mDNS
  one on UDP 5353; if Windows Firewall asks and you decline, nothing important
  is lost.
- Wi-Fi networks with client isolation, or a network profile set to *Public*,
  can hide the broadcasts; the sweep still finds readers that are reachable
  at all.
- The reader's SSH host key is accepted as-is (readers generate their own).

## Releases

Download `readerdetect.exe` and `readerdetect-gui.exe` (or the zip) from the
repository's Releases page; `SHA256SUMS.txt` lists their checksums. To cut a
release, push a version tag — the workflow in `.github/workflows/release.yml`
tests, publishes both executables stamped with that version and attaches them:

```
git tag v0.2.0
git push origin v0.2.0
```

`readerdetect --version` prints the stamped version.

## Building

.NET 9 SDK (or newer; the projects target `net9.0`). On macOS/Linux use the
solution filter, which leaves out the Windows-only GUI:

```
dotnet build ReaderDetect.NoGui.slnf
dotnet test  ReaderDetect.NoGui.slnf
dotnet run --project src/ReaderDetect.Cli -- scan
```

Publishing (`./publish.ps1` on Windows, `./publish.sh` elsewhere) writes the
two single-file executables to `dist/cli/` and `dist/gui/`.

Layout: `src/ReaderDetect` is the discovery library (no dependencies, plain
`net9.0`, so the timing application can reference it as a project);
`src/ReaderDetect.Management` adds network setup (this is where SSH.NET
lives); `src/ReaderDetect.Cli` and `src/ReaderDetect.Gui` are the front ends;
`tests/ReaderDetect.Tests` runs without any hardware, using byte captures from
a real reader and scripted fakes.

## Integrating into the timing app

Add this repository as a git submodule and a `ProjectReference` to
`src/ReaderDetect/ReaderDetect.csproj` (and `ReaderDetect.Management.csproj`
if you want the setup dialog). `ReaderScanner.ScanAsync` streams results
through `IProgress<ScanProgress>`, so a WinForms grid can fill while the sweep
is still running — `src/ReaderDetect.Gui/MainForm.cs` shows the pattern.

## License

MIT, see `LICENSE`.
