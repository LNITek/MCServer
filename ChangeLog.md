# V0.3.3

* Added: Command-line interface for paths, settings inspection, server discovery, per-server docs/config/schedules, live server commands, progress UI for downloads, and host single-instance enforcement.
* Added: `--create-server` / `--delete-server` with shared UI/CLI safety checks and pipe forwarding.
* Added: `MCServer` command is installed in PATH by the Docker image (entrypoint changes) and the Windows MSI installer (PATH update).
* Added: Local named pipe (`MCServer.Cli`) so one-off commands talk to a running host.
* Added: README `Commands` section documenting flags, exit codes and examples.
* Added: `--help` output is generated from the registered CLI options, so new flags appear automatically.
* Fixed: CLI now resolves the repo-local `Data` folder and old relative `Path` entries from settings.json instead of the caller's working directory.
* Fixed: Reworked the schedule system to work properly.

# V0.3.2

* Fixed: Windows installer installing properly.

# V0.3.1

* Fixed: Crash relating to console.
* Fixed: Added command field to schedules.
* Fixed: Schedules not reading & writing correctly.
* Fixed: Custom server properties using the same value for all fields.
* Fixed: Player permissions now save properly.
* Fixed: World import & other import/export functions.
* Added: Host logging with Serilog
* Added: Apache V2 License.
* Added: README file to BDS.
* Added: `help` command for server console.

# V0.3.0

* Changed: Full rewrite from a WPF desktop app to a Blazor Server web UI on .NET 10. Manage the server from any browser.
* Changed: Reformatted file structure (MCServer / MCServer.Plugin / MCServer.BDS / MCServer.Installer).
* Added: Login system, single Admin account (default Admin/Admin, reset via `reset-auth` in terminal).
* Added: Multi-server / multi-world support with dashboard.
* Added: Plugin system with built-in Bedrock plugin plus external plugins folder.
* Added: Support to edit Packet Rate Limit Config file.
* Added: World Trim per dimension (rectangle or radius keep-area, dry-run scan, pre-trim backup).
* Added: Settings stored in the data folder (%AppData%\MCServer), safe across upgrades.
* Removed: Dynamic DNS manager (no replacement yet).
