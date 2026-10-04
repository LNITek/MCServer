# MCServer.BDS

`MCServer.BDS` is the built-in `MCServer` plugin for Mojang's Bedrock Dedicated Server (BDS). It is installed automatically with `MCServer` — see the [main readme](../README.md#installation).

> ***Note!*** This plugin is ***NOT*** Mojang official software. It is created independently by **LNI.Tek**.
`The Base Software (Bedrock Dedicated Server)` is official Mojang software and can be downloaded at [Mojang's Website](https://www.minecraft.net/en-us/download/server/bedrock).

> ***Note!*** By downloading and using this software you do agree to the Minecraft [End User License Agreement](https://minecraft.net/eula) and [Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839).

Any issue or feature for the `Base Software (Bedrock Dedicated Server)` can be reported here: [Issues](https://bugs.mojang.com/projects/BDS/issues/BDS) and [Features](https://feedback.minecraft.net/).

## Feature Support

| Feature | Status | Notes |
|---|---|---|
| Auto Minecraft (BDS) updater | :heavy_check_mark: | downloads newest server files |
| Custom console commands | :heavy_check_mark: | Look under command section |
| Server properties editor | :heavy_check_mark: | |
| Allowlist / permissions editors | :heavy_check_mark: | |
| Player management | :heavy_check_mark: | |
| Player banning system | :heavy_check_mark: | Auto detects banned players and kicks them from the server |
| Player logging | :heavy_check_mark: | |
| Packet rate limit editor | :heavy_check_mark: | |
| Resource / behavior packs management | :heavy_check_mark: | import & export |
| World trim | :heavy_check_mark: | per-dimension keep-areas |
| World export & import | :heavy_check_mark: | |
| World backup system | :heavy_check_mark: | |
| Multi world support | :heavy_check_mark: | Can change the active world |
| Profanity whitelist editor | :x: | |
| Server version control | :x: | |
| World version control | :x: | |
| Server scripting | :x: | |
| World map viewer | :x: | |

## Commands

Type these in the server console page. `help` lists the registered commands and `help <command>` shows its arguments. Anything else is sent to the Bedrock server itself.

| Command | Description | Arguments |
|---|---|---|
| `Backup` | Backs up a world to a `.tar.gz` archive. | `-world <name>` — world folder name, defaults to the active world |
| `Update` | Downloads and installs the newest BDS version. | — |
| `Trim` | Deletes chunks outside a keep-area for one dimension. The server must be stopped; a backup is made first unless `-nobackup` is given. | See trim arguments below |
| `Start` | Starts the server if it is not running yet. | — |
| `Restart` | Restarts the server. | `-delay <seconds>` — delay before restarting, default `10` |
| `Stop` | Stops the server. | `-delay <seconds>` — delay before stopping, default `10` |

### Trim arguments

`Trim -world <name> -dimension <name> -mode <rect|radius> ...`

| Argument | Description |
|---|---|
| `-world <name>` | World folder name, defaults to the active world |
| `-dimension <name>` | `Overworld`, `Nether` or `End`, defaults to `Overworld` |
| `-mode <rect\|radius>` | Keep-area shape. Auto-detected when omitted: `Radius` if `-cx`/`-cz`/`-radius` are given without rect bounds, otherwise `Rect` |
| `-minx -minz -maxx -maxz <int>` | Rect mode: keep-area chunk coordinates (all four required) |
| `-cx -cz -radius <int>` | Radius mode: center chunk and keep radius in chunks (all three required) |
| `-circular` | Radius mode: keep a circle instead of a square |
| `-dryrun` | Count only, delete nothing |
| `-nobackup` | Skip the pre-trim backup |

Example: `Trim -dimension Nether -cx 0 -cz 0 -radius 32 -dryrun`