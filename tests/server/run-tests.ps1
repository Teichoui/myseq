# Builds and runs the server tests with the Visual Studio C++ tools.
#
#   .\run-tests.ps1                         # test the server sources in this repo
#   .\run-tests.ps1 -ServerDir C:\old\server -OutDir C:\temp\old   # run against another copy
#
# NetUtilTests is skipped when the source tree has no NetUtil.h (an older copy).

param(
	[string]$ServerDir = (Join-Path $PSScriptRoot "..\..\server"),
	[string]$OutDir = (Join-Path $env:TEMP "myseq-server-tests"),
	[string]$VsDevCmd = ""		# default: found with vswhere
)

# Find the C++ build environment of the newest Visual Studio unless a path was given.
if (-not $VsDevCmd) {
	$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
	if (Test-Path $vswhere) {
		$vsRoot = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
		if ($vsRoot) { $VsDevCmd = Join-Path $vsRoot "Common7\Tools\VsDevCmd.bat" }
	}
}
if (-not $VsDevCmd -or -not (Test-Path $VsDevCmd)) {
	Write-Host "Could not find VsDevCmd.bat. Install Visual Studio with the C++ tools, or pass -VsDevCmd <path>." -ForegroundColor Red
	exit 2
}

$ServerDir = (Resolve-Path $ServerDir).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$common = "/nologo /EHsc /std:c++17 /W3 /D_CRT_SECURE_NO_WARNINGS /D_WINSOCK_DEPRECATED_NO_WARNINGS /I`"$ServerDir`""

function Build-Test($name, $sources, $extra) {
	$objDir = Join-Path $OutDir "$name-obj"
	New-Item -ItemType Directory -Force $objDir | Out-Null
	$exe = Join-Path $OutDir "$name.exe"
	$cmd = "`"$VsDevCmd`" -arch=x64 -no_logo && cl $common $extra /Fo`"$objDir\\`" /Fd`"$objDir\\`" /Fe`"$exe`" $sources"
	$log = cmd /c $cmd 2>&1
	if ($LASTEXITCODE -ne 0) {
		Write-Host "BUILD FAILED: $name" -ForegroundColor Red
		$log | Select-String "error|fatal" | ForEach-Object { Write-Host $_.Line }
		return $null
	}
	return $exe
}

function Run-Test($title, $exe) {
	Write-Host "`n=== $title ===" -ForegroundColor Cyan
	if (-not $exe) { return 1 }
	# Run inside the VS environment so the AddressSanitizer runtime DLL can be found.
	cmd /c "`"$VsDevCmd`" -arch=x64 -no_logo && `"$exe`"" | Out-Host
	return $LASTEXITCODE
}

$results = @{}
$tests = $PSScriptRoot

if (Test-Path (Join-Path $ServerDir "NetUtil.h")) {
	$exe = Build-Test "NetUtilTests" "`"$tests\NetUtilTests.cpp`"" ""
	$results["NetUtilTests"] = Run-Test "NetUtil: sockets and walk guard" $exe
} else {
	Write-Host "`n=== NetUtilTests skipped (no NetUtil.h in $ServerDir) ===" -ForegroundColor Yellow
}

$srcs = "`"$tests\RawBufferTests.cpp`" `"$ServerDir\Spawn.cpp`" `"$ServerDir\Item.cpp`" `"$ServerDir\World.cpp`""
$exe = Build-Test "RawBufferTests" $srcs "/fsanitize=address /Zi /MD"
$results["RawBufferTests"] = Run-Test "Spawn raw buffer (AddressSanitizer)" $exe

$srcs = "`"$tests\MemReaderLeakTest.cpp`" `"$ServerDir\MemReader.cpp`" `"$ServerDir\IniReader.cpp`""
$exe = Build-Test "MemReaderLeakTest" $srcs "psapi.lib ws2_32.lib advapi32.lib user32.lib"
$results["MemReaderLeakTest"] = Run-Test "MemReader handle leak" $exe

Write-Host "`n--- summary ---"
$results.GetEnumerator() | Sort-Object Name | ForEach-Object {
	$state = if ($_.Value -eq 0) { "PASS" } else { "FAIL" }
	Write-Host ("{0,-20} {1}" -f $_.Key, $state)
}
if ($results.Values | Where-Object { $_ -ne 0 }) { exit 1 }
