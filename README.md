# MCServer

`MCServer` Is Server Software For Minecraft Bedrock Using Mojang's Bedrock Dedicated Server Software (BDS) As A Base.

> ***Note!*** This Software Is Optomised For Private Home Servers. Use It For A Public Server At Your Own Risk.

> ***Note!*** This Software Is ***Not*** Mojang Oficial Software. It Is Created Independently By **LNI.Tek**.
`The Base Software (Bedrock Dedicated Server)` Is Oficial Mojang Software And Is Download From [Mojang's Website](https://www.minecraft.net/en-us/download/server/bedrock).

> ***Note!*** By Downloading And Using This Software You Do Agree To The Minecrafts [End User License Agreement](https://minecraft.net/eula) and [Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839).

---

Check The `Content Tables` Down Below Or Look At The [Documentasion](https://github.com/LNITek/MCServer/wiki) To See More Info.
<br/>
The [Change Log](https://github.com/LNITek/MCServer/blob/main/MCServer/ChangeLog.md) Contains Info Around Changes, Fixes And New Features.
<br/>
Have An Issue Or Feature In Mind Visit: [Issues And Features](https://github.com/LNITek/MCServer/issues).
<br/>
Any Issue Or New Feature For The `Base Software (Bedrock Dedicated Server)`: [Issues](https://bugs.mojang.com/projects/BDS/issues/BDS) And [Features](https://feedback.minecraft.net/).

## Installation
You Can Download `MCServer` [Here](https://github.com/LNITek/MCServer/releases) Or On My [Website](https://lnitek.com/Projects/zkEunhiFIqy4h1FVttEm).

## Suported Platforms

| OS		| GUI	| Console	|
| -----		| -----	| -----		|
| Windows (7+) | :heavy_check_mark: (V0.2.0) | :wavy_dash:* (V0.0.0) |
| Linux () | :x: | :x: |

> *Console Version For The Windows Platform Is Not Up To Date! Commands and or other functions may or may not work as descibed.

## Features
| Feature								| Windows UI |
| -----									| -----		 |
| Auto Minecraft Updater				| :heavy_check_mark: |
| Console Display						| :heavy_check_mark: |
| Fast Controles						| :heavy_check_mark: |
| Easy To Use Commands					| :heavy_check_mark: |
| Auto Updater							| :x: |
| server.properties File Editor			| :heavy_check_mark: |
| Alowlist File Editor					| :heavy_check_mark: |
| Permissions File Editor				| :heavy_check_mark: |
| Dynamic DNS Manager					| :wavy_dash: Feature is there, but there are no services included. |
| Resource / Behavior Packs Manager		| :x: |
| World Trim							| :x: |
| World Export							| :x: |
| World Backup System					| :heavy_check_mark: |
| Schedule System						| :heavy_check_mark: |
| Power Management						| :heavy_check_mark: |
| Player Tracker						| :x: |

## Command -Help
| Command	| Parameters				| Description																	| Exampel	|
| -----		| -----						| -----																			| -----		|
| !			| [None]					| Stops the server, Exits the app and Power off (Shutdown) in 10 Seconds		| !		    |
| Backup	| [None]					| Backups The Active World While The Server Is Running.							| backup	|	
| Start		| [None]					| Starts The Server If It's Not Running Yet.									| start		|
| Restart	| [None]					| Restarts The Server.															| restart	|
| Stop		| `Delay`: ***number*** = The Delay Before Stoping In Seconds. | Stops The Server.							| stop 10	|						
| Exit		| `Delay`: ***number*** = The Delay Before Stoping In Seconds. | Stops The Server And Exits The App.		| exit 10	|					
| Power		| `Delay`: ***number*** = The Delay Before Stoping In Seconds. | Stops The Server And Power On/Off OS.		| power 10 1|
| [Default] | [Unkown]					| Anything Else Will Pass Thru To `The Base Software (Bedrock Dedicated Server)`|			|
| ***Command Interface Only*** ||||
| [None] ||||
| **To Be Added** ||||
| Help | `Command`: text = The command to help with | Gives a helping hand and describes the whats and hows.			| help power |
| version | [none] | Will display the minecraft and this software versions | version |