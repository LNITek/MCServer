# MCServer
[![Release](https://img.shields.io/badge/PreRelease-0.3-blue.svg)](https://github.com/LNITek/MCServer/releases/latest)
![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightblue.svg)
![Platform](https://img.shields.io/badge/Platform-Docker%20-skyblue.svg)

[![RepoRanker](https://reporanker.com/badge/LNITek/MCServer)](https://reporanker.com/repos/LNITek/MCServer)

`MCServer` is a self-hosted web UI for managing Minecraft servers. The host itself is server-type agnostic — game support comes from plugins.

> ***Note!*** This software is optimised for private home servers. For public servers, use it at your own risk.

> ***Warning!*** This project is in development. Code is messy. Featurs my or my not work as expected, or at all.
---

Check the feature tables down below or find the [Documentation](https://github.com/LNITek/MCServer/wiki) for more info.
<br/>
The [ChangeLog](ChangeLog.md) contains info around changes, fixes and new features.
<br/>
Have an issue or feature in mind don't hesitate to post them [here](https://github.com/LNITek/MCServer/issues).


## Supported Platforms

| OS | Support | Installer |
|---|---|---|
| Windows | :heavy_check_mark: | :heavy_check_mark: |
| Linux | :heavy_check_mark: | :x: |
| Docker | :heavy_check_mark: | :heavy_check_mark: |

## Windows

> Windows and its installer is curently under production testing.

> Version 0.3.* is incompatible with previus versions (> 0.2.*). To upgrade first uninstall the old version manually, then install the fresh new version. Settings do not migrate.


### Requirements:
*  **Runtime**  — [ASP.NET Core 10 (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)

### Installation

1. Install the ASP.NET Core 10 x64 runtime (link above).
2. Run `MCServer.msi` from the [latest release](https://github.com/LNITek/MCServer/releases/latest) (installs to `C:\Program Files\MCServer\` with Start Menu shortcuts).
3. Open a new console and run `MCServer --version` (or `MCServer --help`) to verify the CLI.
4. Start **MCServer** and open `http://localhost:5000`.

Run this command to allow BDS software access though the firewall.
```
Get-ChildItem "$env:APPDATA\MCServer\Servers" -Recurse -Filter bedrock_server.exe |
  ForEach-Object { New-NetFirewallRule -DisplayName "Bedrock Server ($($_.Directory.Name))" -Direction Inbound -Program $_.FullName -Action Allow }
```

### Build
```
dotnet build MCServer.Installer.wixproj -c Release
```
The installer file is under `MCServer.Installer/bin/Release/en-US/MCServer.msi`

## Docker

The image stores all data (servers, settings, logs, plugins) under `/data`, declared as a volume.
Run with a named volume (recommended) or a bind mount to keep data across container recreations:
```
docker build -t mcserver .
docker run -d --name mcserver -p 8080:8080 -v mcserver-data:/data mcserver
```
The data location inside the container can be changed with the `MCS_DATA` environment variable.
The target directory must be mounted and writable by the container's non-root user:
```
docker run -d --name mcserver -p 8080:8080 -e MCS_DATA=/opt/mcs -v /opt/mcs:/opt/mcs mcserver
```
The image installs an `MCServer` launcher in `PATH`; it is used as the container entrypoint and for one-off commands:
```
docker exec -it mcserver MCServer --version
docker run --rm -v mcserver-data:/data mcserver --help
```

Or with Docker Compose (`docker compose up -d`):

```yaml
services:
  mcserver:
    image: mcserver:latest
    container_name: mcserver
    restart: unless-stopped
    ports:
      - "8080:8080"   # http UI
      # - "8081:8081" # https UI
    environment:
      MCS_DATA: /data
    volumes:
      - mcserver:/data

volumes:
  mcserver:
```

## Security

Log in with `Admin` / `Admin`. Change the credentials immediately under Settings after installation.
> Forgot your login? Type: `--reset-admin` in the terminal (resets to `Admin` / `Admin`).

The auth system is not tested against any bad actors. I would keep the web UI local only.

The project is AI assisted. Not all code written by AI has been audited.

## Platform Features

| Feature | Status |
|---|---|
| App updater / Version checking | :x: |
| Login & user auth | :heavy_check_mark: |
| Multi user support | :x: |
| Plugin system | :heavy_check_mark: |
| Plugin version control | :x: |
| Plugin Store | :x: |
| Multi server support | :heavy_check_mark: |
| Console display & commands | :heavy_check_mark: |
| Schedule system | :heavy_check_mark: |
| Power management | :heavy_check_mark: |
| Documentation viewer | :heavy_check_mark: |
| Server folder viewer | :heavy_check_mark: |
| Server hardware resource viewer | :heavy_check_mark: |
| Host system hardware resource viewer | :heavy_check_mark: |
| External hosting | :x: |
| Dynamic DNS manager | :x: |
| Port management | :x: |
| Port fowarding checker | :x: |

## Bundled Plugins

| Plugin | Description |
|---|---|
| [Bedrock Dedicated Server](MCServer.BDS/README.md) | Manage Mojang Bedrock servers |

## Commands

MCServer ships a command-line interface (CLI) in addition to the web UI. Commands run **before** the web host starts, and any arguments MCServer does not recognise (like `--urls`) are passed to the ASP.NET Core host unchanged.

Windows MSI installs add the installation folder to the system `PATH` (open a new console after install/upgrade). The Docker image installs `/usr/local/bin/MCServer` and uses it as the entrypoint.

Run `MCServer --help` for the complete, always-up-to-date list.

### How it works

* Most **read-only** commands work without the host running — they read the files under the data folder directly (server list, config paths, docs, schedules, logs).
* Commands that need a **running host** (`--run`, `--exec`, `--start`, `--stop`, `--restart`, `--server-status`) forward to the running instance over a **local named pipe** (`MCServer.Cli`, Unix socket / Windows named pipe, local user only). If MCServer is not running they print `MCServer is not running`.

### Paths and settings

| Command | Description |
|---|---|
| `--path-data` (alias `--data-dir`) | Print the data/root folder |
| `--path-servers` / `--path-plugins` / `--path-logs` | Print the servers, plugins and logs folders |
| `--path-settings` | Print the path to `settings.json` |
| `--show-settings` | Print `settings.json` content |
| `--edit-settings` | Open `settings.json` in `$EDITOR` (falls back to `nano` / `notepad`) |
| `--users` | List configured users and roles |
| `--health` | Sanity-check data folders, `settings.json` and write access |
| `--plugins` | List built-in and external plugins |
| `--logs [count]` | Print today's log file (last `<count>` lines, default 50) |
| `--reset-admin` | Reset the Admin login to `Admin` / `Admin` |
| `--version` | Print the MCServer version |

Example — edit the settings file in nano:

```bash
nano "$(MCServer --path-settings)"
```

### Server discovery and inspection

| Command | Description |
|---|---|
| `--list-servers` (alias `--servers`) | List servers, folders, types and auto-start state |
| `--server-status [name]` (alias `--status`) | Live status with a running host; `Unknown` otherwise |
| `--server-path <server>` | Print a server's folder |
| `--server-config <server> [--file name]` | Print the path of a config file (default `server.properties`) |
| `--show-config <server> [--file name]` | Print a config file |
| `--doc <server> [slug]` | List registered docs (`readme`, `notes`, `releases`) or print one |
| `--list-files <server> [pattern]` | List a server's files, optionally filtered by `*` |
| `--schedules [server]` | Show schedules: trigger, status, last run, next run |

`--server-config` and `--show-config` only accept paths **inside** the server folder; absolute paths and `..` are rejected.

### Running commands on a server

| Command | Description |
|---|---|
| `--run <server> <command...>` | Send a command to a running server (e.g. `--run test list`) |
| `--exec <server> <command...>` | Run an MCServer command (e.g. `--exec test Backup -world MyWorld`) |
| `--start <server>` / `--stop <server>` / `--restart <server>` | Forward a power action to the running host |

If the host is not running, these commands print `MCServer is not running`. In the running host's terminal you can also type `servers`, `status`, `schedules`, `run test say hello`, `start test`, `stop test`, `restart test` and `help`.

### Creating and deleting servers

```bash
# Downloads the newest Bedrock server files (requires network access)
MCServer --create-server --name "My World" --type BDS --auto-start

# Create the folder and registry entry only (no download)
MCServer --create-server --name "My World" --no-download

# Deletes the server and everything under its folder; asks for the server ID
MCServer --delete-server "My World"
MCServer --delete-server "My World" --yes   # skip the confirmation
```

`--delete-server` refuses to delete a running server. Deleting is permanent.

### Output and exit codes

Add `--json` to machine-readable commands for JSON output (`--list-servers`, `--server-status`, `--schedules`, `--plugins`, `--users`).

| Exit code | Meaning |
|---|---|
| `0` | Success |
| `1` | Runtime failure (host not running, IO error, server not found, …) |
| `2` | Usage error (unknown option, missing argument) |

### Examples

```bash
MCServer --list-servers
MCServer --show-config test --file server.properties
MCServer --doc test readme
MCServer --schedules --json
nano "$(MCServer --path-settings)"
MCServer --run test say 'Server restarting soon'
MCServer --exec test Backup -world MyWorld
```

