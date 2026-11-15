# AslHelp.Engines

AslHelp.Engines has the libraries that read game engines from memory and the tools that test them. `fixtures` builds a small game with a real engine version, called a fixture player, with known values in its memory. `measure` runs those players and writes a report.

## Libraries

`AslHelp.Engines.Unreal` reads names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. You pick the storage shape and pass in its layouts. For properties you use `ObjectPropertyReader` up to 4.24 and `FieldPropertyReader` from 4.25 on. The readers return `Result<T>`. When a read fails, its message has the address, the read that failed and the layout field you should check. `ClassOf`, `OuterOf` and `SuperOf` return `Address.Zero` when the pointer they read is 0. `IObjectArray.Read` also returns it for an empty position. Reading `Address.Zero` itself with an `ObjectReader` fails. The name tables aren't thread-safe. The library isn't on NuGet because its API isn't stable.

## Fixture players

### Usage

```
just fixtures --help
just fixtures build <toolchain> --help
just fixtures build <toolchain> <version> --output <folder>
```

| Argument | Value |
|---|---|
| `<toolchain>` | The toolchain that builds the players. |
| `<version>` | The version of the toolchain, as its release lists it. |
| `--output` | The folder for the downloads, the install and the players. Required. |

Each toolchain can add its own options. Run `build <toolchain> --help` to see them.

`just fixtures` runs the command line from the current folder, so a relative `--output` folder is read from there. Without `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.

1 version can have several players. The `build` command installs the toolchain once into the `--output` folder, builds all the players in 1 pass and checks that each one starts. Then it moves each player to `<output>/<toolchain>-<version>-<hash>` and prints the player folders at the end, 1 per line. If a check fails, it exits with code 1 but keeps the players it already stored.

It prints a line as each step starts. The installer download and the download and upload of a player archive show a progress bar with the size, the speed and the time left, which is cleared when the transfer ends. When the output is not a terminal, such as a log in CI, a transfer prints its name and its percentage instead, once when it starts, at 25, 50 and 75 percent and from 95 percent on.

A failed step prints its error and exits with code 1. A download or an upload fails its step when connecting to the server takes more than 30 seconds. It also fails when the server sends no response within 1 minute or the transfer moves no bytes for 1 minute. After the last byte of an upload, the server gets 5 minutes to respond, because it stores the file first. The message has the URL or the store path.

If a toolchain download stops, the next run picks it up where it left off. A run keeps `.fixtures.lock` open in the `--output` folder, so a 2nd run into the same folder fails right away.

### Cache

The `build` command keeps each player in a WebDAV folder, so a player is built only once and other machines download it. These environment variables turn the cache on:

| Variable | Value |
|---|---|
| `FIXTURES_DAV_URL` | The https URL of the WebDAV folder, with no query or fragment. A plain http URL works only for `localhost`. |
| `FIXTURES_DAV_USER` | The WebDAV user |
| `FIXTURES_DAV_TOKEN` | The app password of that user. It is never printed. |

With none of them set, the build stays local and prints a line that lists them. With only some of them set, the build fails and lists the missing ones.

The key of a player covers the toolchain name and the inputs the toolchain adds, like the player project as the build uses it. The key covers what goes into the build, not the code of the tool. A player is built once per key and other machines download it.

The section of each toolchain lists its inputs. In a folder name, `<hash>` is the first 16 hex characters of the key and `<version>` is the version the toolchain pins for that player, not the one you typed. Players go in `<output>/<toolchain>-<version>-<hash>` on disk and in `players/<toolchain>/<version>/<hash>.tar.zst` in the store, with a `<hash>.json` manifest next to the archive.

The manifest lists every input of the key, so you can see why 2 keys are different. You'll also find it in the player folder as `manifest.json`. It has the full key (`player-` plus all 64 hex characters of the hash), the toolchain, the version and the inputs in the order the key hashed them. The project shows up as 1 content hash, not as a list of files. A build never reads a manifest, so a missing one never fails a build.

For each player, the build checks these in order:

1. If `<output>/<toolchain>-<version>-<hash>` already exists, that's the player.
2. If the store has the player, the build downloads and unpacks it. If unpacking fails, it treats the store copy as missing.
3. Otherwise it installs the toolchain once, builds all the missing players in 1 pass and checks each one. It moves each player to `<output>/<toolchain>-<version>-<hash>` and uploads its archive, then its manifest. Last it deletes the folders of the same version that have a different hash.

If an upload fails, the build keeps the archive next to the player folder and the next run uploads it again.

### Unreal Engine 3

Unreal Engine 3 players are built with UDK, the free Unreal Engine 3 release.

```
dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- build unreal3 2015-02 --output ../fixtures
just fixtures-all unreal3 ../fixtures
```

`just fixtures-all` builds every pinned version of the toolchain, 1 at a time. It shows each build's output while it runs, then a line with the exit code, the time in seconds and the last output line. Its exit code is the number of versions that failed.

The build needs a Docker engine that runs Windows containers and that your user can reach. The 1st build pulls `mcr.microsoft.com/windows/server:ltsc2025`, which is about 16 GB. We use that image because it's the smallest one with `dsound.dll` and `dinput8.dll`.

1. Pass the version the way the installer file name writes it. Upper or lower case both work. `eng/fixtures/unreal3/releases.json` lists every pinned version with its sha1.
2. The build downloads `UDKInstall-<version>.exe` from the [`udkinstaller`](https://archive.org/details/udkinstaller) item into `<output>/downloads`, checks its sha1 and extracts the zip inside it into `<output>/udk-<version>`, so the UDK setup program never runs.
3. It reads the import tables of `Binaries/Win32/UDK.exe` and of the DLLs it loads from that folder. It copies the DirectX and Visual C++ 2010 DLLs they import from the install's own `Binaries/Redist/UE3Redist.exe` (`Binaries/Windows/UE3Redist.exe` before 2011-05) into `Binaries/Win32`, each DirectX DLL from the 1st x86 cabinet of the redist that lists it. The Visual C++ 2005 and 2008 DLLs come from Windows. Nothing is installed for the whole system.
4. Versions up to 2011-07 target CLR 2, which Windows Server doesn't ship, so it adds a startup section with CLR 4 to `Binaries/Win32/UDK.exe.config` and `Binaries/Win32/UDK.com.config`.
5. It copies `eng/fixtures/unreal3/Classes` to `Development/Src/Fixture/Classes`, turns on `Fixture` in `UDKGame/Config/DefaultEngineUDK.ini` and deletes `UDKGame/Config/UDKEngine.ini`.
6. It runs `Binaries/Win32/UDK.com make -unattended -nullrhi` in a container, stops it after 10 minutes and checks `UDKGame/Script/Fixture.u`.
7. It runs `Binaries/Win32/UDK.exe server <map>?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH` in a container with the 1st map under `UDKGame/Content/Maps` and waits up to 2 minutes for `ScriptLog: Fixture ready` in `UDKGame/Logs/Launch.log`. After a pass it deletes the log, because the log has the machine name, the user and the paths of the build.
8. The player ends up in `<output>/unreal3-<version>-<hash>` with the install layout and `manifest.json` inside. `udk-<version>` is the install only until then.

The key of a UDK player covers the pinned version, the project folder as it is on disk, the installer sha1, the make and server command lines, the ini edits and a build version. The runtime DLLs aren't a key input because the key is built before the install exists. The installer sha1 pins the files they come from. Bump `BuildVersion` in `Udk` when a build step changes in a way the other inputs don't show, so every player is built again.

The compiler and the server run in a Windows container with the install mounted, so a build fails when the player needs something that only the build machine has. The `-nullrhi` switch turns rendering off, because a container has no display. The build gives your user full control of the install before each start, because the files a container writes belong to the administrator of the container. A step that finds files missing or damaged in the extracted install needs a fresh extraction: delete `<output>/udk-<version>` and run the build again.

Every pinned version from 2010-06-BETA on builds. The build refuses 2009-11-BETA right after the extraction, because that engine stops at startup until the PhysX installer has run on the machine.

#### Adding a UDK version

1. Copy the sha1 of `UDKInstall-<version>.exe` from https://archive.org/metadata/udkinstaller into `eng/fixtures/unreal3/releases.json`, keeping the versions in order.
2. Run the build command for it.

### Unreal Engine 4 and 5

```
just fixtures build unreal4 <version> --output <folder> [--arch x64|x86] [--config shipping|development]
```

Pass the version as `4.19`, `4.19.2` or `UE_4.19`. If you leave out `--arch` and `--config`, it builds every player in the row (see the table below).

We get the engines from Epic with [legendary](https://github.com/legendary-gl/legendary), pinned to 1 hotfix per minor version from 4.0 to 5.8. `eng/fixtures/unreal4/releases.json` lists them.

Before your 1st build:

1. Run `winget install astral-sh.uv`. uv gets Python 3.14 and legendary 0.21.1 on its own.
2. Run `just legendary auth` once. It opens the Epic login in your browser and legendary saves the login in `~/.config/legendary`.

Engines get installed to `<output>/engines/UE_<minor>`, minus the tagged files like `editor_symbols`. If an install gets cut off, it picks up where it stopped and every file gets a hash check. A download fails if no chunk finishes for 10 minutes. If long paths are off in Windows, keep `--output` at 28 characters or less.

All 37 engines take 563 GiB together and the biggest one is 5.3 at 35 GiB. A build needs the engine's size plus 40 GiB of free scratch space. When the drive runs low, the tool removes the scratch folders of failed builds first and then the oldest finished engines. If that still isn't enough, the build fails.

The players get built in a Windows image from `eng/fixtures/unreal4/image/Dockerfile`. The image is `mcr.microsoft.com/windows/server:ltsc2025` pinned by digest, plus DirectX June 2010, VS2017 15.9, the .NET 4.8 dev pack, VS2022 17.14, VS2013 Update 5 and the VC++ 14.50 x64 runtime. Its tag is a hash of that folder, so you need a new image whenever a file there changes. The tool never builds the image itself. If the image is missing, the build fails and prints the command with the folder's tag, like this:

```
docker build -t asl-help/unreal4-build:<tag> "<repo>\artifacts\bin\AslHelp.Engines.Fixtures.Cli\debug\build\unreal4\image"
```

Building the image needs Docker with Windows containers and about 40 GiB free under the Docker data root. Docker caches each layer, so if the image build stops, the next try continues from there.

Here's what a build does:

1. Installs the engine (see above).
2. Deletes `<output>/unreal4-<version>-build` and renders the project for the version into `project` in that folder.
3. Runs the scripts in `eng/fixtures/unreal4/build` in containers of the image, with the engine, `project`, `out`, `logs` and the scripts mounted. `editor.bat` builds the editor target. Then `package.bat` builds each player, the 1st one with `-cook` and the rest with `-skipcook`. Versions 4.0 to 4.18 and 4.25 to 4.27 run `runtimes.bat` first. Each step writes its log to `logs/<step>.log` and gets 90 minutes.
4. Moves each archive to `players/<tag>`, copies in the DLLs its exe imports that Windows doesn't ship, writes `fixture.json` and copies in the logs of its steps.
5. Starts each exe with `-nullrhi -nosound` in a clean `mcr.microsoft.com/windows/server:ltsc2025` container and waits up to 3 minutes for the fixture to write `FixtureReady.txt` next to the exe. Then it deletes that file and `Fixture/Saved`.
6. Moves each player to `<output>/unreal4-<version>-<tag>-<hash>`.

When every script passes it deletes `project` and `out`. When a script fails it keeps them so you can look at what the step saw. The top of each script has the `docker run` line for running it by hand.

Each version's row in `releases.json` has what's different for that version. "All 4" in the table means win-x64-shipping, win-x64-development, win-x86-shipping and win-x86-development. The 1st player in the list is the one that cooks.

| Versions | `players` | `editorFlags` | `packageFlags` | `pathPrepend` | `runtimes` | Toolset |
|---|---|---|---|---|---|---|
| 4.0 to 4.7 | win-x64-development, win-x86-shipping | `-rocket` | `-rocket -nobootstrapexe` | | image | VS2013 12.0 |
| 4.8 to 4.9 | all 4 | `-rocket` | `-rocket` | | image | VS2013 12.0 |
| 4.10 | all 4 | `-rocket` | `-rocket` | | image | VS2015 14.0 |
| 4.11 to 4.13 | all 4 | `-waitmutex` | | | image | VS2015 14.0 |
| 4.14 | all 4 | `-waitmutex` | | | image | VS2015 14.0 |
| 4.15 to 4.18 | all 4 | `-waitmutex` | | VS2017 14.16 `HostX64\x64` | image | VS2017 14.16 |
| 4.19 | all 4 | `-waitmutex` | | VS2017 14.16 `HostX64\x64` | engine | VS2017 14.16 |
| 4.20 to 4.21 | all 4 | `-waitmutex` | | | engine | VS2022 14.29 |
| 4.22 to 4.23 | all 4 | `-waitmutex` | | VS2017 14.16 `HostX64\x64` | engine | VS2017 14.16 |
| 4.24 | all 4 | `-waitmutex` | | | engine | VS2017 14.16 |
| 4.25 | all 4 | `-waitmutex` | | | image | VS2022 14.44 |
| 4.26 to 4.27 | all 4 | `-waitmutex` | | | image | VS2022 14.44 |
| 5.0 to 5.2 | win-x64-shipping, win-x64-development | `-waitmutex` | | | engine | VS2022 14.29 |
| 5.3 | win-x64-shipping, win-x64-development | `-waitmutex -CompilerVersion=14.38.33130` | `-skipbuildeditor -ubtargs=-CompilerVersion=14.38.33130` | | engine | VS2022 14.38 |
| 5.4 to 5.6 | win-x64-shipping, win-x64-development | `-waitmutex` | | | engine | VS2022 14.38 |
| 5.7 to 5.8 | win-x64-shipping, win-x64-development | `-waitmutex` | | | engine | VS2022 14.44 |

- `image`: the VC 2013 CRT, the newest VS2022 CRT and the DirectX June 2010 DLLs from the image.
- `engine`: `Engine/Binaries/ThirdParty/AppLocalDependencies/<Win64|Win32>`.
- Toolset: the VS and MSVC version that UBT picks from the image. It's also the linker version of the shipping exe.
- 5.3 sets its toolset on the command line because UBT 5.3 refuses the 14.29 toolset it would pick otherwise.

A player's key comes from the player id, the manifest sha1, the image tag, the project as it renders for the version, the step scripts, `editorFlags`, `packageFlags`, `pathPrepend`, `runtimes` and `BuildVersion` in `Unreal`. If a change sits behind a version check that leaves it out for some version, the key of that version stays the same.

#### Adding a version

1. Add a row to `eng/fixtures/unreal4/releases.json` with `version`, `app` (like `UE_4.19`), `build`, the `sha1` of the manifest, `installBytes`, `installTags`, `players`, `editorFlags`, `packageFlags`, `pathPrepend` (or `null`) and `runtimes`. Add it to the table above too.
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

The command runs each player on the current machine in version order until its fixture game is ready, reads the player's memory and writes 1 row per player into the report. It logs each step to the console with the time and the level, with the steps of a player indented under its start line. Errors go to stderr.

```
2026-11-06 14:03:12 info: measure[8] players: 26 in 'E:\players'
2026-11-06 14:03:12 info: measure[1] [2010-09] (4/26)
2026-11-06 14:03:31 info: measure[101]   => server ready (19.0 s)
2026-11-06 14:03:44 info: measure[2]   => done (32.1 s): 4 globals, 15 offsets, 0 not found
2026-11-06 14:21:07 info: measure[9] versions with the same offsets:
2026-11-06 14:21:07 info: measure[10]   => [2010-08-BETA, 2010-09, 2010-10-BETA]
2026-11-06 14:21:19 info: measure[11]     => GWorld: forward 8B 3D ???????? 45, offset 2, versions 3/3
2026-11-06 14:21:19 warn: measure[13]     => GEngine: no route, uncovered [2010-09]
2026-11-06 14:21:19 info: measure[15]     => routes (12.3 s)
2026-11-06 14:21:19 info: measure[10]   => [2015-02]
2026-11-06 14:21:20 info: measure[15]     => routes (0.8 s)
```

A player that fails gets its error in its row and the run goes on, so the command exits with 1 at the end. The error lists each step that failed down to the memory read. The command deletes the log of each player afterward. A canceled run writes no report. A bad targets file fails before the 1st player starts.

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

The report has a `groups` list after `players`. A group has its `versions` and their `offsets` in the same form as a row. 2 versions share a group when they have the same types, members and values, even with another version between them. The comparison ignores the globals. A member in `unmeasured` is missing from `offsets`, so its row stays apart from rows that have that member.

A failed row and a row with empty `offsets` do not join a group. Every other row is in exactly 1 group, so a version with its own offsets forms a group of 1. The groups are in order of their 1st version and the versions of a group are in the order of the run. The command logs 1 line per group after the last player.

```json
"groups": [
  {
    "versions": [
      "2010-08-BETA",
      "2010-09",
      "2011-03-BETA2"
    ],
    "offsets": {
      "UObject": {
        "Outer": "0x28",
        "Name": "0x2C"
      }
    }
  }
]
```

A group also has `routes`, with an entry for each global of its versions. A route is a signature search through the code of the executable and then a read of the global from the operand at `offset`. The command reads the executable file of each version once after the players have ended, so the search needs no running game.

A route covers a version when every match in the code sections of that version decodes to an address operand at `offset` and all of them reach 1 RVA from the list of the global in that version. `reached` has that RVA per version, because the list can have statics that change between runs. The command picks the route that covers the most versions, then with the fewest instructions and bytes. It repeats that for the versions left, so a global can have several routes. It tries at most 64 candidates per instruction count, because each one costs a search through the file of every version. `uncovered` has the reason for each version that no route covers and is missing when every version is covered. A file that fails to read gets its version into `uncovered` and the run goes on.

```json
"routes": {
  "GWorld": {
    "routes": [
      {
        "forward": "8B 3D ???????? 45",
        "offset": 2,
        "versions": [
          "2010-09",
          "2015-02"
        ],
        "reached": {
          "2010-09": "0x29D6984",
          "2015-02": "0x2A658D0"
        }
      }
    ],
    "uncovered": {
      "2013-07": "No signature of the group reaches 1 RVA of the global."
    }
  }
}
```

`forward` has the same spelling as the `forward` step of a `LocatorRoute` in AslHelp.Memory, with `?` for the bytes of the address operands and the branch targets. When linear decoding misses the load of a version, its route can also have `??` for the ModRM byte of the load.

### Unreal Engine 3 targets

`eng/fixtures/unreal3/targets.json` lists the globals as an object. Each key is the name of a global and each value has a `$type`. The members aren't in the file because the search for each member is written in code.

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

The measurement reads the sections of `UDK.exe` without code from the running player and finds the 2 arrays in them by their contents. The classes `Class`, `State`, `Struct`, `Field` and `Object` then lead to the offsets because their names and super chain are the same in every build. The properties of `Object` contain the offsets found so far, which gives the offsets of the property members. A row fails when the name array, the object array, the name, class or outer offset or a class of that chain is not found or when 2 arrays with different data fit and neither is the longer one. It also fails when a first name or the object array does not read. Every other member goes to `unmeasured`.

The measurement then reads `IntValue` of the spawned `FixtureData` object through the offsets it found and the row fails when the value is not 1337. The row also fails when the class, the object or the field is not found or when `UStruct.Children`, `UField.Next` or `UProperty.Offset` is in `unmeasured`, because the check needs them. A `--targets` file does not turn the check off, because the class belongs to the fixture game.
