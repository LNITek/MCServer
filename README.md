# MCServer

`MCServer` Is Server Software For Minecraft Bedrock Using Mojang's Bedrock Dedicated Server Software (BDS) As A Base.

> ***Note!*** This Software Is Optomised For Private Servers. Use It For A Public Server At Your Own Risk.

> ***Note!*** This Software Is ***Not*** Mojang Oficial Software. It Is Created Independently By **LNI.Tek**.
`The Base Software (Bedrock Dedicated Server)` Is Oficial Mojang Software And Is Download From [Mojang's Website](https://www.minecraft.net/en-us/download/server/bedrock).

> ***Note!*** By Downloading And Using This Software You Do Agree To The Minecraft [End User License Agreement](https://minecraft.net/eula) and [Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839).

---

Check The `Content Tables` Down Below Or Look At The [Documentasion](https://github.com/LNITek/MCServer/wiki) To See More Info.
<br/>
The [Change Log](https://github.com/LNITek/MCServer/blob/main/MCServer/ChangeLag.md) Contains Info Around Changes, Fixes And New Features.
<br/>
Have An Issue Or Feature In Mind Visit: [Issues And Features](https://github.com/LNITek/MCServer/issues).
<br/>
Any Issue Or New Feature For The `Base Software (Bedrock Dedicated Server)`: [Issues](https://bugs.mojang.com/projects/BDS/issues/BDS) And [Features](https://feedback.minecraft.net/).

## Installation
You Can Download `MCServer` [Here](https://github.com/LNITek/MCServer/releases) Or On My [Website](https://lnitek.com/Projects/zkEunhiFIqy4h1FVttEm).

## Suport
| OS									| Suported	| Version	| Project	|
| -----									| -----		| -----		| -----		|
| Windows UI (7, 10, 11)				| [x]		| 1.0.0		| [Win](https://github.com/LNITek/MCServer/blob/main/MCServer) |
| Windows Server UI (2016 - 2019)		| [x]		| 1.0.0		| [Win](https://github.com/LNITek/MCServer/blob/main/MCServer) |
| Windows Server Console (2016 - 2019)	| [ ]		| 1.0.0		| [Lite](https://github.com/LNITek/MCServer/blob/main/MCServer%20Lite) |
| Linux UI ()							| [ ]		| 0.0.0		|			|
| Linux Server UI ()					| [ ]		| 0.0.0		|			|
| Linux Server Console ()				| [ ]		| 0.0.0		|			|

## Features
| Feature								| Windows UI | Windows Server UI | Windows Server Console | Linux UI | Linux Server UI | Linux Server Console |
| -----									| -----		 | -----			 | -----				  | -----	 | ----- 		   | -----				  |
| MCServer Updater						| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| BDS Manager							| [x] 1.1 | [x] 1.1 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| System Manager						| [x] 1.1 | [x] 1.1 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| World Backuper						| [x] 1.1 | [x] 1.1 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Auto Updater							| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Properties Editor						| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Alowlist Editor						| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Permissions Editor					| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Dynamic DNS Manager					| [x] 1.0 | [x] 1.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| Resource / Behavior Packs Manager		| [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| World Trim							| [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |
| World Export							| [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 | [ ] 0.0 |

## Command -Help
| Command	| Parameters				| Description																	| Exampel	|
| -----		| -----						| -----																			| -----		|
| !			| [None]					| Emergency Stop And Shutdown													| !		    |
| exit		| (number: Shutdown Mode)	| Stops The Server And Shuts Down Your System									| exit 1	|
| start		| [None]					| Starts The Server If Not Running.												| start		|
| restart	| [None]					| Restarts The Server.															| restart	|
| stop		| [None]					| Stops The Server.																| stop		|
| backup	| [None]					| Start Backing Up Cuurent Running World.										| backup	|
| [Default] | [Unkown]					| Anything Else Will Pass Thru To `The Base Software (Bedrock Dedicated Server)`| backup	|