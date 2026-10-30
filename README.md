# MCServer
[![Release](https://img.shields.io/badge/PreRelease-0.3.0-blue.svg)](https://github.com/LNITek/MCServer/releases/latest)
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
3. Start **MCServer** and open `http://localhost:5000`.

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