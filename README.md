# MySEQ

MySEQ is a live map and spawn tracker for EverQuest. It has two parts:

- **Server** (`server/`): a C++ Win32 program that runs on the same PC as the EverQuest client, reads the game's memory, and sends what it finds over the network.
- **Client** (`client/`): a C# WinForms program that connects to the server and shows the zone map, spawn list, and mob alerts.

This is a fork of [RedGuides/myseq](https://github.com/RedGuides/myseq). The current client version is **2.6.0.1**. See [Changelog.md](Changelog.md) for older changes.

## Using it

1. Start EverQuest.
2. Start the server (`server.exe` for 32-bit or `serverx64.exe` for 64-bit EverQuest). It listens on port 5555 by default; change it in `myseqserver.ini`.
3. Start the client and connect it to the server's address and port.

The server reads game memory, so it only shows real data while EverQuest is running. The client can run on the same PC or on another one that can reach the server.

When you start the client, it copies itself to a randomly named `.bin` file and exits, and the copy is the one that keeps running. This is normal. Old copies are cleaned up on the next start.

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
### Tests

The client's non-UI logic (packet framing, launcher copy cleanup, Discord webhook, and so on) has a small test program:

```
cd tests\client
dotnet run -c Release
```

## Version numbers

The client version is set in one place, `client/AssemblyInfo.cs` (`AssemblyVersion` and `AssemblyFileVersion`). It is shown in the About dialog, the status bar, and the exe's file properties. To make a new build, change the last number. The installer reads it from there too.

## Contributing

Make changes on a new branch and open a pull request against `main`.

## License

The project is public domain software; see [LICENSE](LICENSE). The original server files carry GPL headers from the SEQ project; see `server/gpl-2.0.txt` and `server/gpl-3.0.txt`.
