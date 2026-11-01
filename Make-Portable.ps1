# Builds the portable zip (myseq-portable.zip): the same files and folders the installer puts in
# Program Files\MySEQ, ready to unpack anywhere and run without installing.
# Usage: Make-Portable.ps1 <client dir> <32-bit server dir> <64-bit server dir> [<zip path>]
#   client dir          folder holding the built MySEQ.exe
#   32-bit server dir   folder holding the built server.exe
#   64-bit server dir   folder holding the built serverx64.exe
# Run it from the repository root. The files are the ones myseq.xml installs; keep the two in step.
param(
	[Parameter(Mandatory = $true, Position = 0)][string]$ClientDir,
	[Parameter(Mandatory = $true, Position = 1)][string]$Server32Dir,
	[Parameter(Mandatory = $true, Position = 2)][string]$Server64Dir,
	[Parameter(Position = 3)][string]$Zip = 'myseq-portable.zip'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Each entry is "source path" -> "path inside the folder".
$files = [ordered]@{
	"$ClientDir\MySEQ.exe"        = 'MySEQ.exe'
	"$Server64Dir\serverx64.exe"  = 'serverx64.exe'
	"$Server32Dir\server.exe"     = 'server.exe'
	'server\myseqserver.ini'      = 'myseqserver.ini'
	'server\config.ini'           = 'config.ini'
	'LICENSE'                     = 'LICENSE-MySEQ.txt'
	'server\gpl-3.0.txt'          = 'gpl-3.0.txt'
	'client\license.txt'          = 'license.txt'
	'client\mapconvert.vbs'       = 'maps\mapconvert.vbs'
	'client\cfg\Classes.txt'      = 'cfg\Classes.txt'
	'client\cfg\Races.txt'        = 'cfg\Races.txt'
	'client\cfg\BodyTypes.txt'    = 'cfg\BodyTypes.txt'
	'client\cfg\RGB.txt'          = 'cfg\RGB.txt'
	'client\cfg\GroundItems.ini'  = 'cfg\GroundItems.ini'
	'client\cfg\Zones.ini'        = 'cfg\Zones.ini'
}
$folders = 'maps', 'logs', 'timers', 'filters'

$stage = Join-Path ([IO.Path]::GetTempPath()) ('myseq-portable-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $stage 'MySEQ'
try {
	New-Item -ItemType Directory -Path $root | Out-Null
	foreach ($folder in $folders) { New-Item -ItemType Directory -Path (Join-Path $root $folder) | Out-Null }

	foreach ($entry in $files.GetEnumerator()) {
		if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) { throw "Missing file: $($entry.Key)" }
		$target = Join-Path $root $entry.Value
		New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
		Copy-Item -LiteralPath $entry.Key -Destination $target
	}

	@'
MySEQ without the installer
===========================

Unpack this folder anywhere you can write to (not Program Files), then:

1. Start EverQuest.
2. Run serverx64.exe (64-bit EverQuest) or server.exe (32-bit). It asks for administrator rights because it reads
   the game's memory. It reads myseqserver.ini and config.ini from this folder.
3. Run MySEQ.exe and connect it to the server.

Run the server with "debug" after the name (serverx64.exe debug) to see what it reads. To remove MySEQ, delete this
folder; the client's settings are kept in %APPDATA%\MySEQ.

https://github.com/Teichoui/myseq
'@ | Set-Content -Path (Join-Path $root 'README-portable.txt') -Encoding ASCII

	$zipPath = if ([IO.Path]::IsPathRooted($Zip)) { $Zip } else { Join-Path (Get-Location) $Zip }
	if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
	[IO.Compression.ZipFile]::CreateFromDirectory($root, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
	Write-Host "Built $zipPath"
}
finally {
	if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
