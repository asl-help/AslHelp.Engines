# AslHelp.Engines

AslHelp.Engines has the libraries that read game engines from memory and the tools that test them. `fixtures` builds a small game with a real engine version, called a fixture player, with known values in its memory. `measure` runs those players and writes a report.

## Libraries

`AslHelp.Engines.Unreal` reads names, objects and structs from Unreal Engine 3 games given their layouts. We don't publish it to NuGet, because its API can still change.

## Fixture players

### Usage

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

### Cache

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

### Unreal Engine 3

Unreal Engine 3 players are built with UDK, the free Unreal Engine 3 release.

```
dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- build unreal3 2015-02 --output ../fixtures
just fixtures-all ../fixtures
```

`just fixtures-all` builds every pinned version 1 at a time and prints the exit code, the seconds and the last output line of each. It exits with the number of failed versions, so a full run exits with 1 because the build refuses 2009-11-BETA.

The build needs a Docker engine that runs Windows containers and that your user can reach. The 1st build pulls `mcr.microsoft.com/windows/server:ltsc2025`, which is about 16 GB. We use that image because it's the smallest one with `dsound.dll` and `dinput8.dll`.

1. Pass the version as in the installer file name. Upper or lower case both work. `eng/tools/AslHelp.Engines.Fixtures.Cli/Unreal3/installers.json` lists every pinned version and its sha1.
2. The build downloads `UDKInstall-<version>.exe` from the [`udkinstaller`](https://archive.org/details/udkinstaller) item into `<output>/downloads`, checks its sha1 and extracts the zip inside it into `<output>/udk-<version>`, so the UDK setup program never runs.
3. It reads the import tables of `Binaries/Win32/UDK.exe` and of the DLLs it loads from that folder. It copies the DirectX and Visual C++ 2010 DLLs they import from the install's own `Binaries/Redist/UE3Redist.exe` (`Binaries/Windows/UE3Redist.exe` before 2011-05) into `Binaries/Win32`, each DirectX DLL from the 1st x86 cabinet of the redist that lists it. The Visual C++ 2005 and 2008 DLLs come from Windows. Nothing is installed for the whole system.
4. Versions up to 2011-07 target CLR 2, which Windows Server doesn't ship, so it adds a startup section with CLR 4 to `Binaries/Win32/UDK.exe.config` and `Binaries/Win32/UDK.com.config`.
5. It copies `test/fixtures/unreal3/Classes` to `Development/Src/Fixture/Classes`, turns `Fixture` on in `UDKGame/Config/DefaultEngineUDK.ini` and deletes `UDKGame/Config/UDKEngine.ini`.
6. It runs `Binaries/Win32/UDK.com make -unattended -nullrhi` in a container, stops it after 10 minutes and checks `UDKGame/Script/Fixture.u`.
7. It runs `Binaries/Win32/UDK.exe server <map>?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH` in a container with the 1st map under `UDKGame/Content/Maps` and waits up to 2 minutes for `ScriptLog: Fixture ready` in `UDKGame/Logs/Launch.log`. After a pass it deletes the log, because the log has the machine name, the user and the paths of the build.
8. The player ends up in `<output>/unreal3-<version>-<hash>` with the install layout and `manifest.json` inside. `udk-<version>` is the install only until then.

The key of a UDK player covers the pinned version, the installer sha1, the make and server command lines, the ini edits and a build version. The runtime DLLs are no key input, because the key is built before the install exists. The installer sha1 pins the files they are read from. Bump `BuildVersion` in `Udk` when a build step changes in a way none of the other inputs show, so every player is built again.

The compiler and the server run in a Windows container with the install mounted, so a build fails when the player needs something that only the build machine has. The `-nullrhi` switch turns rendering off, because a container has no display. The build gives your user full control of the install before each start, because the files a container writes belong to the administrator of the container. A step that finds files missing or damaged in the extracted install needs a fresh extraction: delete `<output>/udk-<version>` and run the build again.

Every pinned version from 2010-06-BETA on builds. The build refuses 2009-11-BETA right after the extraction, because that engine stops at startup until the PhysX installer has run on the machine.

#### Adding a UDK version

1. Copy the sha1 of `UDKInstall-<version>.exe` from https://archive.org/metadata/udkinstaller into `installers.json` in version order.
2. Run the build command for it.

## Measuring

```
just measure <engine> <players> --output <file> [--targets <file>]
```

| Argument | Value |
|---|---|
| `<engine>` | The engine of the players, such as `unreal3`. |
| `<players>` | The folder with the `<engine>-<version>-<hash>` players that `just fixtures build` made. |
| `--output` | The file for the JSON report. |
| `--targets` | The file with the globals to find. It replaces the list built into the command, so a change needs no rebuild. |

The command runs each player on the current machine in version order until its fixture game is ready, reads the player's memory and writes 1 row per player into the report. It logs each step to the console with the time and the level. Errors go to stderr:

```
2026-11-06 14:03:12 info: measure[1] Measuring 2010-09.
2026-11-06 14:03:31 info: measure[101] Server ready after 19.0 s.
```
 A player that fails gets its error in its row and the run goes on, so the command exits with 1 at the end. It deletes the log of each player afterward. A canceled run writes no report. A bad targets file fails before the 1st player starts.

A row has the version and 3 maps:

- `globals` has a list of RVAs in the executable for each global, because several statics can point at 1 object.
- `offsets` has the offset of each member under its type, with the real names of the engine.
- `unmeasured` has the reason for each global or member that was not found. The row still counts as measured.

A failed row has `error` and none of the maps. Numbers are hex strings:

```json
{
  "version": "2015-02",
  "globals": {
    "GWorld": [
      "0x29D6984",
      "0x29E6244",
      "0x2A658D0"
    ]
  },
  "offsets": {
    "UObject": {
      "Outer": "0x28",
      "Name": "0x2C"
    }
  },
  "unmeasured": {}
}
```

### Unreal Engine 3 targets

`eng/tools/AslHelp.Engines.Measuring.Cli/Unreal3/targets.json` lists the globals as an object, with the name of the global as the key and a `$type` in each value. The members are not in the file, because the search for each member is code.

```json
{
  "GWorld": {
    "$type": "objectSlot",
    "class": "World",
    "object": "TheWorld"
  }
}
```

| `$type` | Keys | Finds |
|---|---|---|
| `nameArray` | `firstNames` | The array whose first entries are these names. The file needs exactly 1 of this type. |
| `objectArray` | | The array whose objects contain their own position. The file needs exactly 1 of this type. |
| `objectSlot` | `class`, `object` | Every static that points at an object of `class` or of a class deriving from it. `object` is optional and is the name of the object in the name array. |

The measurement reads the sections of `UDK.exe` without code from the running player and finds the 2 arrays in them by their contents. The classes `Class`, `State`, `Struct`, `Field` and `Object` then lead to the offsets because their names and super chain are the same in every build. The properties of `Object` contain the offsets found so far, which gives the offsets of the property members. A row fails when the name array, the object array, the name, class or outer offset or a class of that chain is not found or when 2 arrays with different data fit and neither is the longer one. Every other member goes to `unmeasured`.

The measurement then reads `IntValue` of the spawned `FixtureData` object through the offsets it found and the row fails when the value is not 1337. The row also fails when the class, the object or the field is not found or when `UStruct.Children`, `UField.Next` or `UProperty.Offset` is in `unmeasured`, because the check needs them. A `--targets` file does not turn the check off, because the class belongs to the fixture game.
