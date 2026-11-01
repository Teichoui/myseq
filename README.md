# MySEQ

MySEQ is a live map and spawn tracker for EverQuest. It has two parts:

- **Server** (`server/`): a C++ Win32 program that runs on the same PC as the EverQuest client, reads the game's memory, and sends what it finds over the network.
- **Client** (`client/`): a C# WinForms program that connects to the server and shows the zone map, spawn list, and mob alerts.

The current client version is **2.6.0.2**. See [Changelog.md](Changelog.md) for older changes.

## Using it

1. Start EverQuest.
2. Start the server (`server.exe` for 32-bit or `serverx64.exe` for 64-bit EverQuest). It listens on port 5555 by default; change it in `myseqserver.ini`.
3. Start the client and connect it to the server's address and port.

You can use it installed or not. A release has an installer (`MySEQ-<version>.msi`) and a portable zip (`MySEQ-<version>-portable.zip`) with the same files: unpack the zip anywhere you can write to (not Program Files) and run `MySEQ.exe` and the server from that folder. The client saves its settings in `%APPDATA%\MySEQ`, not in the folder, so delete that folder as well to remove every trace.

The server reads game memory, so it only shows real data while EverQuest is running. The client can run on the same PC or on another one that can reach the server.

After EverQuest is patched the memory offsets in `myseqserver.ini` often have to be found again. The server remembers the `eqgame.exe` timestamp it last read real data from (`VerifiedExeTime` in the ini file). If `eqgame.exe` has a different timestamp and the server can read no zone or character from it for about 45 seconds, the Patch line in the server window turns red and says `(EQ patched?)`. Use **Offset Finder** to find the new offsets. If the offsets still work after a patch, the line stays normal.

When you start the client, it copies itself to a randomly named `.bin` file and exits, and the copy is the one that keeps running. This is normal. Old copies are cleaned up on the next start.

### The map

- Mob names are placed so they do not overlap each other or sit under the spawn information window. Names that fall off the map are left where they are.
- When the client is not connected, or a zone has no map file, a short note on the map says so.
- **Update Delay** in the Options window sets how often the map redraws. The default of 200 ms is about 5 frames per second; a lower value is smoother (Windows timers step in about 15.6 ms, so 31 ms is about 30 frames per second).
- The Options window can be resized, and dark mode is in the View menu.

### Settings

The client saves its settings to a temporary file first and swaps it in when complete, keeping the previous copy as a `.bak` file next to it. If the main settings file is missing or cannot be read, the client loads the `.bak` copy instead and puts it back as the main file.

### Alerts

Mobs that match your hunt, caution, danger, or alert lists can play a beep, speak the mob's name, or play a wav file. Each alert type in the Options window has a **Test** button so you can hear your choice without waiting for a spawn.

## Building

### Client

The client targets .NET Framework 4.8 and is built for x86 and x64. Open `MySEQ.sln` in Visual Studio, or run MSBuild on `client/MySEQ.client.csproj`:

```
msbuild client\MySEQ.client.csproj /t:Restore
msbuild client\MySEQ.client.csproj /t:Rebuild /p:Configuration=Release
```

NuGet restores DockPanelSuite and Fody/Costura. The build stops with a clear error if the restore did not happen. Costura packs the DLLs into the exe.

### Server

Open `server/MySEQ.server.sln` in Visual Studio 2022 (toolset v143) and build the `Release` configuration for Win32 or x64.

### Installer

`Make.bat` builds `myseq.msi` from `myseq.xml`. It needs the [WiX Toolset 3.x](https://github.com/wixtoolset/wix3/releases) (`candle.exe` and `light.exe`) on the PATH. Build the client and both servers first, then run this from the repository root, giving the folder that holds each exe:

```
Make.bat <client dir> <32-bit server dir> <64-bit server dir>
```

The version comes from `client/AssemblyInfo.cs`. The installer needs .NET Framework 4.8 and administrator rights. It installs to `Program Files (x86)\MySEQ`, adds Start Menu shortcuts for the client and each server, and keeps edited `myseqserver.ini` and `config.ini` files when upgrading. The 64-bit server is not offered on 32-bit Windows.

### Portable zip

`Make-Portable.ps1` builds `myseq-portable.zip`, the same files and folders as the installer, from the same three folders. Run it from the repository root:

```
Make-Portable.ps1 <client dir> <32-bit server dir> <64-bit server dir>
```

`tests\portable\check-portable.ps1 myseq-portable.zip` checks the zip against `myseq.xml`, so a file added to the installer but not to the zip fails the build.

### Tests

The client's non-UI logic (packet framing, launcher copy cleanup, Discord webhook, and so on) has a small test program:

```
cd tests\client
dotnet run -c Release
```

The server's non-UI code (network helpers, raw buffers, the patch check, and a memory reader handle leak test) has test programs that `tests\server\run-tests.ps1` builds and runs. It finds Visual Studio with `vswhere`; pass `-VsDevCmd <path to VsDevCmd.bat>` to use a specific one.

```
tests\server\run-tests.ps1
```

### Continuous build

`.github/workflows/build.yml` runs on every pull request and on pushes to `main`. On a Windows runner it builds the client and both servers, runs the client and server tests, builds the installer, and tests that it installs, upgrades and uninstalls cleanly. The installer and its logs are uploaded as artifacts.

## Version numbers

The client version is set in one place, `client/AssemblyInfo.cs` (`AssemblyVersion` and `AssemblyFileVersion`). It is shown in the About dialog, the status bar, and the exe's file properties. To make a new build, change the last number. The installer reads it from there too.

## Releasing

To publish a release, change the version in `client/AssemblyInfo.cs` in a pull request and merge it. Then tag the merge with the same version and push the tag:

```
git checkout main
git pull
git tag v2.6.0.3
git push origin v2.6.0.3
```

`.github/workflows/release.yml` then checks that the tag matches the version in the source and is on `main`, builds the client and both servers, runs the tests, builds the installer and the portable zip, and publishes a GitHub release with `MySEQ-<version>.msi`, `MySEQ-<version>-portable.zip` and their SHA-256 checksums. Release notes are generated from the merged pull requests. A tag that does not match the version stops before anything is built.

## Contributing

Make changes on a new branch and open a pull request against `main`.

## License

The project is public domain software; see [LICENSE](LICENSE). The original server files carry GPL headers from the SEQ project; see `server/gpl-2.0.txt` and `server/gpl-3.0.txt`.
