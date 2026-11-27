# AslHelp.Engines

[![AslHelp.Engines on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Engines?label=AslHelp.Engines)](https://www.nuget.org/packages/AslHelp.Engines)
[![AslHelp.Engines.Unreal on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Engines.Unreal?label=AslHelp.Engines.Unreal)](https://www.nuget.org/packages/AslHelp.Engines.Unreal)

This repo contains the libraries that read game engines from memory and the tools we use to test them. `fixtures` builds a small game on a real engine version with known values in its memory. We call that a fixture player. `measure` reads those players and writes a report.

## Libraries

`AslHelp.Engines.Unreal` reads names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. You pick the storage shape and pass in its layouts. For properties you use `ObjectPropertyReader` up to 4.24 and `FieldPropertyReader` from 4.25 on. The readers return `Result<T>` and the name tables aren't thread-safe.

`UnrealProfile.Parse` reads an `unreal3` or `unreal4` profile document from `just measure --profiles` (see [Profiles](#profiles)). `UnrealGame.Attach` finds the globals of that profile in the game and picks the readers for its shapes:

```cs
var profile = UnrealProfile.Parse(File.ReadAllText("unreal4-4.18.3-win-x64-shipping.json")).Unwrap();
var game = UnrealGame.Attach(memory, module, profile).Unwrap();

var data = game.GWorld.Member("AuthorityGameMode").Member("FixtureData");
var hp = data.Member<int>("Hp").CreateWatcher(ticks);
var x = data.Member("Coord").Member<int>("X").CreateWatcher(ticks);
```

`ticks` is the `ITickCounter` of your host. `GWorld` is a path that starts at the world. A path doesn't read until its watcher does. Each hop looks the member up on the real class of the object, so `FixtureData` shows up on the game mode even though `AuthorityGameMode` is declared as `AGameModeBase`. `Member(name)` goes through an object pointer or into a struct, picked by the kind of the property. `Member<T>(name)` ends the path on a value. Attach doesn't wait for a world. A null object on the way, like a world that isn't loaded yet, fails the read and the watcher follows its `FailAction`. With the default `Clear` it shows the default of `T`.

| Property | `T` and what a chain watcher gives |
|---|---|
| `Int8Property`, `Int16Property`, `IntProperty`, `Int64Property` | `sbyte`, `short`, `int`, `long` |
| `ByteProperty` (also `TEnumAsByte`), `UInt16Property`, `UInt32Property`, `UInt64Property` | `byte`, `ushort`, `uint`, `ulong` |
| `FloatProperty`, `DoubleProperty`, `LargeWorldCoordinatesRealProperty` | `float`, `double`, `double` |
| `BoolProperty` (also bitfields) | `bool` |
| `EnumProperty` | the type of its underlying property, like `byte` |
| `NameProperty`, `StrProperty` | `string`, a name with its number like `Actor_4` |
| `ObjectProperty`, `ClassProperty`, `ComponentProperty`, `ObjectPtrProperty`, `ClassPtrProperty` | `Address`, a path goes on through it |
| `WeakObjectProperty` (4.11 and later) | `Address` or 0 when the object is gone. A path goes on through it |
| `StructProperty` | no value, a path goes on into it |
| `StructProperty` with only numbers of 1 class, like `FVector` | an array of that number, like `float[]` (`double[]` on 5.x) |
| `ArrayProperty` | an array of the element type, like `int[]` |
| `SetProperty` | `HashSet<T>` of the element type |
| `MapProperty` | `Dictionary<TKey, TValue>` |

After a collection, an index or key goes into 1 element and `Count` reads the number of elements:

```cs
var second = data.Member("IntArray").Element<int>(1).CreateWatcher(ticks);
var two = data.Member("Dict").Element<int>("two").CreateWatcher(ticks);
var shieldCount = data.Member("Items")[1].Member<int>("Count").CreateWatcher(ticks);
var items = data.Member("Items").Count.CreateWatcher(ticks);
```

- an index is an `int` or `long`, a key is an `int`, `long` or `string` and `Element<T>` also takes a `bool` key
- a string key matches name and string keys, an integer matches any integer or enum key
- `Element<bool>(key)` on a set reads whether the set has the key
- an array or map of mixed structs doesn't read as 1 value, so index it
- a missing key or an index past the count fails the read

Any other kind fails with the property and its class, like `TextProperty` or `SoftObjectProperty`. A `T` that doesn't match the kind fails when the watcher reads. A string with more than 512 characters fails. A whole array with more than 1024 elements fails too. A whole read or a key lookup fails on a set or map with more than 1024 slots, used or free. A watcher of a collection keeps its instance while the elements don't change. A profile without the offsets of a kind still attaches and only reads of that kind fail with the missing offsets. Unreal Engine 3 documents don't have the set, map and weak pointer offsets, so those kinds fail there.

The path types are in `AslHelp.Engines.Unreal.Paths`.

A script writes the same path through `dynamic`. Its watcher is a `Watcher<object?>` whose value has the type from the table, picked from the property at read time. Before the 1st good read `Current` and `Old` are null. When another class has a member with that name and another kind, `Old` keeps the old type for 1 tick and `Changed` is true. A misspelled member fails when the watcher reads.

```cs
IWatcherSource hp = game.GWorldChain.AuthorityGameMode.FixtureData.Hp;
IWatcherSource x = game.GWorldChain.AuthorityGameMode.FixtureData.Coord.X;
IWatcherSource shield = game.GWorldChain.AuthorityGameMode.FixtureData.Items[1].ItemName;
```

Paths and chains can start at other objects too:

```cs
var engineHp = game.GEngine.Member("GameViewport").Member("World").Member("AuthorityGameMode").Member("FixtureData").Member<int>("Hp");

var item = game.FindPackage("/Script/Fixture").Unwrap().FindClass("FixtureItem").Unwrap().GetInstances().Unwrap()[0];
var itemName = item.Path.Member<string>("ItemName");
IWatcherSource count = item.Chain.Count;

var subsystem = game.FindSubsystem("MySubsystem").Unwrap();
var score = subsystem.Path.Member<int>("Score");
```

- `GEngine` and `GEngineChain` start at the engine object, like `GWorld` and `GWorldChain`. A profile needs `globals.GEngine`.
- `FindPackage` takes a path like `/Script/Engine` in any case. On Unreal Engine 3 it takes a bare name like `Engine`. `GetPackages` lists the packages.
- A package has `FindClass` and `GetClasses`. A class has `FindProperty` (through its bases), `GetProperties` (its own), `GetInstances` and `GetDerivedClasses`.
- `GetInstances` lists the live objects of exactly that class without the `Default__` object. It searches the object array like `FindClass`. An object's path fails once the game frees it.
- `FindSubsystem` gives a root at the 1st live object of a subsystem class. It needs a profile of 4.22 or later. A read finds the object again when it's missing or replaced.
- `World` doesn't have any reflected properties on Unreal Engine 3, so start at `GEngine` or at an object from `GetInstances` on that engine.

`ClassOf` returns the real class of an object and `FindClass` finds a loaded class by name. A miss reads the object array again, but only the objects in slots that changed. `UnrealGame` keeps each class with its properties and each package after the 1st read. It isn't thread-safe. `Read<T>` reads the value of 1 property of an object with the types from the table. `UnrealProperty.Accept` calls `VisitValue<T>`, `VisitEnum`, `VisitStruct`, `VisitCollection` or `VisitUnsupported` of your `PropertyVisitor<TResult>`.

`AslHelp.Engines` is what the engine libraries share. `MemberChain<THop>` lets a script write a path of members through `dynamic`, like `root.Player.Health`. Each name goes to the engine's `IMemberResolver<THop>`, which returns the hop for it or throws if the type can't have that member. An index like `root.Items[2]` goes to `ResolveIndex` the same way. The resolver also creates the watcher, so it can type the watcher after the member at the end or after the value it reads:

```cs
dynamic root = new MemberChain<Hop>(resolver, rootHop);
IWatcherSource health = root.Player.Health;

var watcher = health.CreateWatcher(ticks);
```

```
dotnet add package AslHelp.Engines
dotnet add package AslHelp.Engines.Unreal
```

## Fixture players

### Usage

```
just fixtures --help
just fixtures build <toolchain> --help
just fixtures build <toolchain> <version> --output <folder>
```

| Argument | Value |
|---|---|
| `<toolchain>` | The toolchain that builds the players, `unreal3` or `unreal4`. |
| `<version>` | The toolchain version, written the way its release lists it. |
| `--output` | The folder for the downloads, the install and the players. Required. |

`just fixtures` runs from your current folder, so a relative `--output` path starts there. If you don't use `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.

1 version can have several players. The `build` command installs the toolchain once, builds all the players in 1 pass and checks that each one starts. Each player ends up in `<output>/<toolchain>-<version>-<hash>` and the command prints those folders at the end, 1 per line. A download that stops picks up where it left off on the next run.

### Cache

The `build` command can keep each player in a WebDAV folder. That way each player only gets built once and other machines just download it. Set these environment variables to turn the cache on:

| Variable | Value |
|---|---|
| `FIXTURES_DAV_URL` | The https URL of the WebDAV folder, without a query or fragment. Plain http only works for `localhost`. |
| `FIXTURES_DAV_USER` | The WebDAV user |
| `FIXTURES_DAV_TOKEN` | The app password for that user. The tool never prints it. |

A player's key comes from the toolchain name and the toolchain's inputs, like the player project and the build scripts. `<hash>` is the first 16 hex characters of the key. Each toolchain also declares a `BuildVersion`. Bump it when you change a build step in a way the other inputs don't show and every player gets built again.

A build uses the player folder if it exists and the store copy if it doesn't. It only builds what's missing from both. The store keeps each player as `players/<toolchain>/<version>/<hash>.tar.zst` with a `<hash>.json` manifest next to it. The manifest lists every input of the key, so you can see why 2 keys are different. It's also in the player folder as `manifest.json`.

### Unreal Engine 3

We build Unreal Engine 3 players with UDK, which is the free Unreal Engine 3 release.

```
just fixtures build unreal3 2015-02 --output ../fixtures
just fixtures-all unreal3 ../fixtures
```

`just fixtures-all` builds every pinned version of the toolchain, 1 at a time. Its exit code is the number of versions that failed.

You need a Docker engine with Windows containers that your user can reach. The 1st build pulls `mcr.microsoft.com/windows/server:ltsc2025`, which is about 16 GB.

Pass the version the way the installer file name writes it, like `2015-02` or `2010-08-BETA`. `eng/fixtures/unreal3/releases.json` lists every pinned version. The build downloads the installer from the [`udkinstaller`](https://archive.org/details/udkinstaller) item into `<output>/downloads` and extracts it into `<output>/udk-<version>`, so nothing gets installed for the whole system. The compiler and the game server run in a container with that folder mounted. If a step finds that the extracted install is damaged, delete `<output>/udk-<version>` and build again.

Every pinned version from 2010-06-BETA on builds. The build refuses 2009-11-BETA because that engine needs PhysX installed on the machine.

#### Adding a UDK version

1. Copy the sha1 of `UDKInstall-<version>.exe` from https://archive.org/metadata/udkinstaller into `eng/fixtures/unreal3/releases.json`, keeping the versions in order.
2. Run the build command for it.

### Unreal Engine 4 and 5

```
just fixtures build unreal4 <version> --output <folder> [--arch x64|x86] [--config shipping|development]
```

Pass the version as `4.19`, `4.19.2` or `UE_4.19`. If you leave out `--arch` and `--config`, it builds every player of the version (see the table below).

We get the engines from Epic with [legendary](https://github.com/legendary-gl/legendary), pinned to 1 hotfix per minor version from 4.0 to 5.8. `eng/fixtures/unreal4/releases.json` lists them.

Before your 1st build:

1. Run `winget install astral-sh.uv`. uv gets Python 3.14 and legendary 0.21.1 on its own.
2. Run `just legendary auth` once. It opens the Epic login in your browser and legendary saves the login in `~/.config/legendary`.
3. Build the image. The players get built in a Windows image from `eng/fixtures/unreal4/image/Dockerfile` and the tool never builds it itself. Run a build and it fails with the command to run, like this:

```
docker build -t asl-help/unreal4-build:<tag> "<repo>\artifacts\bin\AslHelp.Engines.Fixtures.Cli\debug\build\unreal4\image"
```

The image needs Docker with Windows containers and about 40 GiB free under the Docker data root. Its tag is a hash of the image folder, so you need a new image whenever a file there changes.

Engines get installed to `<output>/engines/UE_<minor>`. All 37 engines take 563 GiB together and the biggest one is 5.3 at 35 GiB. A build needs the engine's size plus 40 GiB of free scratch space. When the drive runs low, the tool removes the scratch folders of failed builds and then the oldest finished engines. If long paths are off in Windows, keep `--output` at 28 characters or less.

The build runs the scripts in `eng/fixtures/unreal4/build` in containers of the image and writes each step's log to `<output>/unreal4-<version>-build/logs`. When a build fails it keeps that folder. The top of each script shows the `docker run` line for running the step by hand.

Each version's row in `releases.json` declares what's different for that version. "All 4" in the table means win-x64-shipping, win-x64-development, win-x86-shipping and win-x86-development. The 1st player in the list is the one that cooks.

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

#### Adding an Unreal Engine version

1. Add a row to `eng/fixtures/unreal4/releases.json` with `version`, `app` (like `UE_4.19`), `build`, the `sha1` of the manifest, `installBytes`, `installTags`, `players`, `editorFlags`, `packageFlags`, `pathPrepend` (or `null`) and `runtimes`. Add it to the table above too.
2. Run the build command for it.

## Measuring

```
just measure <engine> <players> --output <file> [--profiles <folder>] [--targets <file>] [--jobs <n>]
```

| Argument | Value |
|---|---|
| `<engine>` | The engine's subcommand, `unreal3` or `unreal4`. `just measure <engine> --help` lists its options. |
| `<players>` | The folder with the players from `just fixtures build`. |
| `--output` | The file to write the JSON report to. |
| `--profiles` | The folder to write 1 profile document per group to. Leave it out to skip them. |
| `--jobs`, `-j` | How many players to measure at once. The default is the engine's limit, which is 4 for `unreal4` and 1 for `unreal3`. Lower it if you run short on memory. |
| `--targets` | The file with the values to find. It replaces the built-in list, so you can change it without a rebuild. |

`unreal3` starts each player on your machine and reads its memory. `unreal4` only reads the PDB and the executable of each player, so it takes a few seconds per player. It also takes `--arch x64|x86` and `--config shipping|development`, which both default to every player.

If a player fails, its row gets the error and the run keeps going, but the command exits with 1 at the end.

### Report

The report lists 1 row per player under `players`:

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

- `globals` lists RVAs in the executable for each global. It's a list because several statics can point at the same object.
- `offsets` gives the offset of each member under its type, using the engine's real names.
- `unmeasured` gives the reason each global or member wasn't found.

A failed row has `error` instead of the maps.

After `players` comes `groups`. 2 versions are in the same group when the offsets the libraries in `src/` read are the same in both. `unreal4` groups also have a `key` like `win-x64-shipping` because groups don't mix architectures or configurations. Their `shapes` map shows the layout of the types that changed across versions.

```json
"groups": [
  {
    "versions": [
      "2010-09",
      "2013-07",
      "2015-02"
    ],
    "offsets": {
      "UObject": {
        "Outer": "0x28",
        "Name": "0x2C"
      }
    },
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
  }
]
```

Each group lists `routes` for the globals of its versions. A route finds a global with a signature search through the code of the executable and then reads the global from the operand at `offset`. `forward` uses the same format as the `forward` step of a `LocatorRoute` in AslHelp.Memory, with `?` for the bytes of address operands and branch targets. A route only counts for a version when every match in that version's image reaches the same RVA, which `reached` lists. `uncovered` gives the reason for each version that no route covers.

### Profiles

The run also writes 1 JSON file per group when you pass `--profiles`. Each file has what an engine library needs to attach to a game of that group. The file name is `<engine>-<1st version>-<key>.json`, like `unreal4-4.18.3-win-x64-shipping.json`. A group without a key gets `<engine>-<1st version>.json`, like `unreal3-2010-09.json`. Use an empty folder because the run doesn't delete the files of an older run. `globals` gives a locator per global in the JSON form that `MemoryLocator` in AslHelp.Memory reads. Each route of the report becomes 1 locator route, listed under the 1st version it covers.

```json
{
  "engine": "unreal4",
  "key": "win-x64-shipping",
  "versions": ["4.18.3", "4.19.2"],
  "shapes": { "FNameEntry": "indirect" },
  "offsets": { "UObjectBase": { "ClassPrivate": "0x10" } },
  "globals": {
    "GWorld": {
      "4.18.3": [{ "forward": "48 89 05 ???????? 0F 28 D6", "offset": 3 }, { "read": "global" }]
    }
  }
}
```

### Unreal Engine 3 targets

`eng/fixtures/unreal3/targets.json` lists the globals. The members aren't in the file because the search for each member is written in code.

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
| `nameArray` | `firstNames` | The array that starts with these names. The file needs exactly 1 of these. |
| `objectArray` | | The array whose objects store their own position in it. The file needs exactly 1 of these. |
| `objectSlot` | `class`, `object` | Every static that points at an object of `class` or of a class that derives from it. `object` is optional and is the object's name in the name array. |

### Unreal Engine 4 and 5 targets

`eng/fixtures/unreal4/targets.json` lists what to report. Every target can have `from` and `until`. They're minor versions like `4.25` and include both ends.

| `$type` | Keys | Finds |
|---|---|---|
| `global` | `constructedType`, `constructedFrom`, `constructedUntil` | The public symbol with the target's name. Versions from `constructedFrom` to `constructedUntil` don't have that public, so for those the measurer takes the static that calls to the `constructedType` constructor pass as `this`. |
| `functionStatic` | `function` | The 1st address outside the code that the function reads and that doesn't have a public symbol. |
| `constant` | | The value of the compile-time constant whose C++ name is the target, like `Scope::Value`. The report lists it as `Value` under `Scope` next to the offsets. |
| `layout` | `members` | The size as `sizeof` and each member of the type with the target's name. A member can have its own `from` and `until`. `oldName` with `oldNameUntil` is the member's name in the PDBs up to that minor. A member decides the groups unless it has `"needed": false`. The size only decides them with `"sizeNeeded": true` on the type. |

If a type or member is inside its range but the PDB doesn't have it, the row fails. A type only shows up in a PDB when a fixture compile unit includes the full type and uses it, so add it to `eng/fixtures/unreal4/Source/Fixture/FixtureTypes.cpp`.
