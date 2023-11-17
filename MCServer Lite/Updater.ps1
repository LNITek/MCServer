# FILE DIRECTORY TO BEDROCK SERVER:
$Settings = Get-Content "$PSScriptRoot\Settings.txt" #"C:\Users\Egbert\Documents\Visual Studio\Visual C#\MCServer\bin\Debug\net6.0-windows\Settings.txt"

foreach($Set in $Settings){
    $Prop = $Set.Split('=')
    if( $Prop[0] -eq "ServerPath"){
        $gameDir = $Prop[1]
		break
    }
}

cd $gameDir

[Net.ServicePointManager]::SecurityProtocol = "tls12, tls11, tls"

# BUGFIX: HAD TO INVOKE-WEBREQEUEST WITH A DIFFERENT CALL TO FIX A PROBLEM WITH IT NOT WORKING ON FIRST RUN
try
{
	$requestResult = Invoke-WebRequest -Uri 'https://www.minecraft.net/en-us/download/server/bedrock' -TimeoutSec 1
}
catch
{
	# NO ACTION, JUST SILENCE ERROR
} 

# START WEB REQUEST SESSION
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$session.UserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36'
$InvokeWebRequestSplatt = @{
    UseBasicParsing = $true
    Uri             = 'https://www.minecraft.net/en-us/download/server/bedrock'
    WebSession      = $session
	TimeoutSec		= 10
    Headers         = @{
        "accept"          = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8"
        "accept-encoding" = "gzip, deflate, br"
        "accept-language" = "en-US,en;q=0.8"
    }
}

# GET DATA FROM WEB
try
{
	$requestResult = Invoke-WebRequest @InvokeWebRequestSplatt
}
catch
{
	# IF ERROR, CAN'T PROCEED, SO EXIT SCRIPT
	echo "WEB REQUEST ERROR"
	Start-Sleep -Seconds 3
	exit
} 

# PARSE DOWNLOAD LINK AND FILE NAME
$serverurl = $requestResult.Links | select href | where {$_.href -like "https://minecraft.azureedge.net/bin-win/bedrock-server*"}
$url = $serverurl.href
$filename = $url.Replace("https://minecraft.azureedge.net/bin-win/","")
$url = "$url"
$output = "$gameDir\BACKUP\$filename" 

write-output "NEWEST UPDATE AVAILABLE: $filename"

# CHECK IF FILE ALREADY DOWNLOADED
if(!(Test-Path -Path $output -PathType Leaf))
{ 
	# STOP SERVER
	if(get-process -name bedrock_server -ErrorAction SilentlyContinue)
	{
		echo "STOPPING SERVICE..."
		Stop-Process -name "bedrock_server" 
	}

	# DO A BACKUP OF CONFIG 
	if(!(Test-Path -Path "BACKUP"))
	{
		New-Item -ItemType Directory -Name BACKUP 
	}
	
	if(Test-Path -Path "server.properties" -PathType Leaf)
	{
		echo "BACKING UP server.properties..."
		Copy-Item -Path "server.properties" -Destination BACKUP 
	}
	else # NO CONFIG FILE MEANS NO VALID SERVER INSTALLED, SOMETHING WENT WRONG...
	{
		echo "NO server.properties FOUND... ERROR"
		Start-Sleep -Seconds 3
		exit
	}
	
	if(Test-Path -Path "allowlist.json" -PathType Leaf)
	{
		echo "BACKING UP allowlist.json..."
		Copy-Item -Path "allowlist.json" -Destination BACKUP
	}
	
	if(Test-Path -Path "permissions.json" -PathType Leaf)
	{
		echo "BACKING UP permissions.json..."
		Copy-Item -Path "permissions.json" -Destination BACKUP 
	}

	# DOWNLOAD UPDATED SERVER .ZIP FILE
	Write-Output "DOWNLOADING $filename..."
	$start_time = Get-Date 
	Invoke-WebRequest -Uri $url -OutFile $output 

	# UNZIP
	echo "UPDATING SERVER FILES..."
	Expand-Archive -LiteralPath $output -DestinationPath $gameDir -Force 

	# RECOVER BACKUP OF CONFIG 
	echo "RESTORING server.properties..."
	Copy-Item -Path ".\BACKUP\server.properties" -Destination .\ 
	
	if(Test-Path -Path "allowlist.json" -PathType Leaf)
	{
		echo "RESTORING allowlist.json..."
		Copy-Item -Path ".\BACKUP\allowlist.json" -Destination .\ 
	}
	
	if(Test-Path -Path "permissions.json" -PathType Leaf)
	{
		echo "RESTORING permissions.json..."
		Copy-Item -Path ".\BACKUP\permissions.json" -Destination .\ 
	}
} 
else
{
	echo "UPDATE ALREADY INSTALLED..."
}

Start-Sleep -Seconds 3
exit