# V0.3.1
Fixed: Crash relating to console.
Fixed: Added command field to schedules.
Fixed: Schedules not reading & writing corectly.
Fixed: Custom server properties using the same value for all fields.
Fixed: Player permitions now save properly.
Fixed: World import & other import / export functions.

Added: Host logging with Serilog
Added: Apache V2 License.
Added: Readme File to BDS.
Added: `help` command for server console.

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
