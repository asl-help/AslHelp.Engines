# AslHelp.Engines

AslHelp.Engines builds the fixture players that the engine libraries' live tests read from. A fixture player is a small game made with a real engine version, with known values in its memory.

## Usage

```
just fixtures --help
just fixtures build <toolchain> <version> --output <folder>
```

| Argument | Value |
|---|---|
| `<toolchain>` | The toolchain to build the player with. |
| `<version>` | The version of the toolchain, as its release lists it. |
| `--output` | The folder for the downloads, the install and the player. Required. |

`just fixtures` runs the command line from the current folder, so a relative `--output` folder is read from there. Without `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.

The `build` command installs the toolchain at that version into the `--output` folder, builds the player in it and checks that the player starts. Then it moves the install to `<output>/<toolchain>-<version>-<hash>` and prints that folder last.

It prints a line as each step starts. The installer download and the download and upload of a player archive show a progress bar with the size, the speed and the time left, which is cleared when the transfer ends. When the output is not a terminal, such as a log in CI, a transfer prints its name and its percentage instead, once when it starts, at 25, 50 and 75 percent and from 95 percent on.

A failed step prints its error and exits with code 1. A download or an upload fails its step when connecting to the server takes more than 30 seconds. It also fails when the server sends no response within 1 minute or the transfer moves no bytes for 1 minute. After the last byte of an upload, the server gets 5 minutes to respond, because it stores the file first. The message has the URL or the store path.

A toolchain download keeps the bytes it has, so the next run resumes it. A failed download from the store starts over on the next run. Run 1 build at a time per `--output` folder, because 2 runs into the same folder write the same files.

## Cache

The `build` command keeps each player in a WebDAV folder, so a player is built only once and other machines download it. These environment variables turn the cache on:

| Variable | Value |
|---|---|
| `FIXTURES_DAV_URL` | The https URL of the WebDAV folder, with no query or fragment. A plain http URL works only for `localhost`. |
| `FIXTURES_DAV_USER` | The WebDAV user |
| `FIXTURES_DAV_TOKEN` | The app password of that user. It is never printed. |

With none of them set, the build stays local and prints a line that lists them. With only some of them set, the build fails and lists the missing ones.

The key of a player covers the toolchain name, the files of the player project that the build uses and the inputs the toolchain adds. These inputs are what goes into the build, not the code of the tool. A player is built once per key. Other machines and later commits download it.

The section of each toolchain lists its inputs. The version in a path is the one the toolchain pins, not the one you typed. `<hash>` is the first 16 hex chars of the key. On disk the player sits in the folder `<output>/<toolchain>-<version>-<hash>`. In the store it sits at `players/<toolchain>/<version>/<hash>.tar.zst`, with its manifest `<hash>.json` beside it.

The manifest lists what a player was built from, so you can see why 2 keys differ. It sits in the player folder as `manifest.json` too. It lists the full key as `player-` and all 64 hex chars of its hash, the toolchain, the version and every input in the order the key hashed them. The project folder shows up as 1 content hash, not as a list of files. A build never reads a manifest, so a missing one never fails a build.

A build checks these in order:

1. If `<output>/<toolchain>-<version>-<hash>` exists, it is the player. Nothing is sent over the network.
2. If the store has `players/<toolchain>/<version>/<hash>.tar.zst`, the build downloads it to `<output>/<toolchain>-<version>-<hash>.tar.zst`, unpacks it into `<output>/<toolchain>-<version>-<hash>` and deletes the archive. The toolchain is not installed.
3. Otherwise it installs the toolchain, builds and checks the player, writes `manifest.json` into the install and moves the install to `<output>/<toolchain>-<version>-<hash>`. Then it packs the folder into `<output>/<toolchain>-<version>-<hash>.tar.zst`, uploads that to `players/<toolchain>/<version>/<hash>.tar.zst`, uploads the manifest to `<hash>.json` beside it and deletes the archive.

The archive goes up before the manifest, so a manifest in the store always has its archive. A failed upload keeps the player folder and the archive. The next run finds the folder and does not upload again, so upload the archive and the manifest by hand or delete the folder to build again.

A damaged or cut short `players/<toolchain>/<version>/<hash>.tar.zst` fails the build with that path. The build deletes its download. Delete the file from the store and build again, so the next run builds and uploads the player.

## Unreal Engine 3

Unreal Engine 3 players are built with UDK, the free Unreal Engine 3 release.

```
dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- build unreal3 2015-02 --output ../fixtures
just fixtures-all ../fixtures
```

`just fixtures-all` builds every pinned version 1 at a time and prints the exit code, the seconds and the last output line of each. It exits with the number of failed versions, so a full run exits with 1 because the build refuses 2009-11-BETA.

The build needs a Docker engine that runs Windows containers and that your user can reach. The 1st build pulls `mcr.microsoft.com/windows/server:ltsc2025`, which is about 16 GB. We use that image because it's the smallest one with `dsound.dll` and `dinput8.dll`.

1. Pass the version as in the installer file name. Upper or lower case both work. `eng/tools/AslHelp.Engines.Fixtures.Cli/Unreal3/installers.json` lists every pinned version, its sha1 and the date it was last checked.
2. The build downloads `UDKInstall-<version>.exe` from the [`udkinstaller`](https://archive.org/details/udkinstaller) item into `<output>/downloads`, checks its sha1 and extracts the zip inside it into `<output>/udk-<version>`, so the UDK setup program never runs.
3. It reads the import tables of `Binaries/Win32/UDK.exe` and of the DLLs it loads from that folder. It copies the DirectX and Visual C++ 2010 DLLs they import from the install's own `Binaries/Redist/UE3Redist.exe` (`Binaries/Windows/UE3Redist.exe` before 2011-05) into `Binaries/Win32`, each DirectX DLL from the 1st x86 cabinet of the redist that lists it. The Visual C++ 2005 and 2008 DLLs come from Windows. Nothing is installed for the whole system.
4. Versions up to 2011-07 target CLR 2, which Windows Server doesn't ship, so it adds a startup section with CLR 4 to `Binaries/Win32/UDK.exe.config` and `Binaries/Win32/UDK.com.config`.
5. It copies `test/fixtures/unreal3/Classes` to `Development/Src/Fixture/Classes`, turns `Fixture` on in `UDKGame/Config/DefaultEngineUDK.ini` and deletes `UDKGame/Config/UDKEngine.ini`.
6. It runs `Binaries/Win32/UDK.com make -unattended -nullrhi` in a container, stops it after 10 minutes and checks `UDKGame/Script/Fixture.u`.
7. It runs `Binaries/Win32/UDK.exe server <map>?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH` in a container with the 1st map under `UDKGame/Content/Maps` and waits up to 2 minutes for `ScriptLog: Fixture ready` in `UDKGame/Logs/Launch.log`. After a pass it deletes the log, because the log has the machine name, the user and the paths of the build.
8. The player ends up in `<output>/unreal3-<version>-<hash>` with the install layout and `manifest.json` inside. `udk-<version>` is the install only until then.

The key of a UDK player covers the pinned version, the installer sha1, the make and server command lines, the ini edits and a build version. The runtime DLLs are no key input, because the key is built before the install exists. The installer sha1 pins the files they are read from. Bump `BuildVersion` in `Udk` when a build step changes in a way none of the other inputs show, so every player is built again.

The compiler and the server run in a Windows container with the install mounted, so a build fails when the player needs something that only the build machine has. The `-nullrhi` switch turns rendering off, because a container has no display. The build gives your user full control of the install before each start, because the files a container writes belong to the administrator of the container. A version with `checked` null may use an installer layout that the tool does not read. A step that finds files missing or damaged in the extracted install needs a fresh extraction: delete `<output>/udk-<version>` and run the build again.

Every pinned version from 2010-06-BETA on builds. The build refuses 2009-11-BETA right after the extraction, because that engine stops at startup until the PhysX installer has run on the machine.

### Adding a UDK version

1. Copy the sha1 of `UDKInstall-<version>.exe` from https://archive.org/metadata/udkinstaller into `installers.json` in version order, with `checked` null.
2. Run the build command for it.
3. Set `checked` to the date of that run.
