@echo off
rem Builds the MySEQ installer (myseq.msi). Needs the WiX Toolset 3.x (candle.exe and light.exe) on the PATH.
rem Usage: Make.bat <client dir> <32-bit server dir> <64-bit server dir>
rem   client dir          folder holding the built MySEQ.exe
rem   32-bit server dir   folder holding the built server.exe
rem   64-bit server dir   folder holding the built serverx64.exe
rem Run it from the repository root. The version comes from client\AssemblyInfo.cs.

if "%~3"=="" (
    echo Usage: Make.bat ^<client dir^> ^<32-bit server dir^> ^<64-bit server dir^>
    exit /b 1
)

rem Drop a trailing backslash: in -dName="dir\" the backslash would escape the closing quote.
set "CLIENTDIR=%~1"
set "SERVER32DIR=%~2"
set "SERVER64DIR=%~3"
if "%CLIENTDIR:~-1%"=="\" set "CLIENTDIR=%CLIENTDIR:~0,-1%"
if "%SERVER32DIR:~-1%"=="\" set "SERVER32DIR=%SERVER32DIR:~0,-1%"
if "%SERVER64DIR:~-1%"=="\" set "SERVER64DIR=%SERVER64DIR:~0,-1%"

set VERSION=
for /f tokens^=2^ delims^=^" %%a in ('findstr /b /c:"[assembly: AssemblyVersion" client\AssemblyInfo.cs') do set VERSION=%%a
if "%VERSION%"=="" (
    echo Could not read the version from client\AssemblyInfo.cs
    exit /b 1
)

echo Building MySEQ Open %VERSION%
candle.exe -nologo -dVersion=%VERSION% -dClientDir="%CLIENTDIR%" -dServer32Dir="%SERVER32DIR%" -dServer64Dir="%SERVER64DIR%" myseq.xml || exit /b 1
light.exe -nologo -ext WixUIExtension -cultures:en-us myseq.wixobj -out myseq.msi || exit /b 1
