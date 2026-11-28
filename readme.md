# AslHelp.Engines

[![AslHelp.Engines on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Engines?label=AslHelp.Engines)](https://www.nuget.org/packages/AslHelp.Engines)
[![AslHelp.Engines.Unreal on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Engines.Unreal?label=AslHelp.Engines.Unreal)](https://www.nuget.org/packages/AslHelp.Engines.Unreal)

This repo contains the libraries that read game engines from memory and the tools we use to test them. `fixtures` builds a small game on a real engine version with known values in its memory. We call that a fixture player. `measure` reads those players and writes a report.

## Libraries

`AslHelp.Engines.Unreal` reads names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. You pick the storage shape and pass in its layouts. For properties you use `ObjectPropertyReader` up to 4.24 and `FieldPropertyReader` from 4.25 on. The readers return `Result<T>` and the name tables aren't thread-safe.

`UnrealGame.Attach(memory, module, ProfileGuessHandling.Allow)` reads the engine version from the file version of the executable, picks the profile document for it from the documents the library ships and finds the globals of that document in the game:

```cs
var game = UnrealGame.Attach(memory, module, ProfileGuessHandling.Allow).Unwrap();

if (game.Selection!.Warning is { } warning)
{
    log(warning);
}

var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
var hp = data.Member("Hp").Value<int>().Unwrap().CreateWatcher(ticks);
var x = data.Member("Coord").Member("X").Value<int>().Unwrap().CreateWatcher(ticks);
var anyHp = data.Member("Hp").Unwrap().CreateWatcher(ticks);
```

- the library ships 1 document per group of the measured players, 4.0 to 5.8 and UDK 2010-06 to 2015-02 (see [Profiles](#profiles))
- the pick takes the document that lists the version. A version without a patch, like a 5.8 stamp, takes the highest listed 5.8.x (a guess when the 5.8.x sit in 2 documents) and a listed 5.8 covers every 5.8.x. If none fits, it guesses the nearest patch of the same minor or else the highest lower version of the same major, like 5.8.4 for 5.9.0. Then `Selection.Warning` has both versions
- an Unreal Engine 3 game whose build isn't a measured UDK release reads as `3.<build>`, like 3.8225, so a guess takes the nearest lower UDK document. You can pass that form as the version too
- `ProfileGuessHandling.Disallow` doesn't guess and fails with the version a guess would take
- the key is `win-x64` or `win-x86` from the pointer size, then `development` when the exe name ends with `-Debug` or `-DebugGame` and `shipping` for any other name. UBT leaves the configuration out of the name of a development build (`Game.exe`), so a name without it is a guess. Pass `UnrealConfiguration.Development` for a development build
- after the pick, attach checks that name 0 of the name table reads `None`. A document that doesn't fit fails with the detected version and the picked version
- a game that's still starting can fail because its name table doesn't read yet. Attach again a little later
- the exes of 4.0 to 4.7 don't have a version stamp, so pass the version

To pass the version or the configuration or to add your own documents:

```cs
var catalog = UnrealProfile.CreateCatalog();
catalog.AddFile("my-game.json").Unwrap();

var target = UnrealTarget.Detect(memory, module, version: "4.27.2", configuration: UnrealConfiguration.Development).Unwrap();
var selection = catalog.Select(target, ProfileGuessHandling.Disallow).Unwrap();
var game = UnrealGame.Attach(memory, module, selection).Unwrap();
```

A document you add wins over a shipped document for the versions it lists. A key at the root that the engine doesn't read fails the document. So does an offset the engine doesn't read in a type where an optional offset is missing. `UnrealGame.Attach(memory, module, catalog, guessHandling)` detects the target itself. `UnrealProfile.Parse` reads 1 `unreal3` or `unreal4` document and `UnrealGame.Attach(memory, module, profile)` attaches with it as is, without the pick and the name check. `game.Profile` is the profile attach used.

In the 1st example:

- `ticks` is the `ITickCounter` of your host
- `GWorld` is a root and its `.Path` starts at the world
- each step returns a `Result`. A typo or a wrong `T` fails the step when the object is there
- when the object isn't there, like a world that's still loading, the step checks the class the property declares, like `GameModeBase` for `AuthorityGameMode`. The step fails on a key of the wrong type and when neither that class nor a loaded class derived from it has the member. It can pass once the game loads a derived class with the member, so try it again later. A member that only a derived class has works, but `IsChecked` stays false until a read gets there
- a failed step or read is a `MemberPathError`. `IsShapeMismatch` is true when the path doesn't fit the classes, like the typo the 1st read finds after an unchecked step. It's false for a read that can work later, like on a null object
- class and property names ignore case like the engine, but the exact name wins
- each read looks the member up on the real class of the object, so `FixtureData` shows up on the game mode even though `AuthorityGameMode` is declared as `AGameModeBase`
- `Member(name)` goes through an object pointer or into a struct, picked by the kind of the property
- `Value<T>()` gives a watcher of `T`. The path itself gives a `Watcher<object?>` with the value in the type from the table
- a failed read makes the watcher follow its `FailAction`. With the default `Clear` it shows the default of `T`
- `Read()` and `Read<T>()` read the value once without a watcher, like `data.Member("Hp").Read<int>()`. They read the whole path and don't touch the reads the watchers of a tick share

| Property | `T` and what a boxed watcher gives |
|---|---|
| `Int8Property`, `Int16Property`, `IntProperty`, `Int64Property` | `sbyte`, `short`, `int`, `long` |
| `ByteProperty` (also `TEnumAsByte`), `UInt16Property`, `UInt32Property`, `UInt64Property` | `byte`, `ushort`, `uint`, `ulong` |
| `FloatProperty`, `DoubleProperty`, `LargeWorldCoordinatesRealProperty` | `float`, `double`, `double` |
| `BoolProperty` (also bitfields) | `bool` |
| `EnumProperty` | the type of its underlying property, like `byte` |
| `NameProperty`, `StrProperty` | `string`, a name with its number like `Actor_4` |
| `ObjectProperty`, `ClassProperty`, `ComponentProperty`, `ObjectPtrProperty`, `ClassPtrProperty` | `Address`, a path goes on through it |
| `WeakObjectProperty` | `Address` or 0 when the object is gone. A path goes on through it. Before 4.11 the serial numbers come from `GSerialNumberBlocksForDebugVisualizersRoot` |
| `StructProperty` | `StructValue` with each member by name, like `pos["X"]`. A path also goes on into it, like `Member("X")` |
| `StructProperty` with only numbers of 1 class, like `FVector` | `StructValue` too. `Value<float[]>()` reads it as an array of that number (`double[]` on 5.x) |
| `FVector` and `FRotator` | `Value<Vector>()` and `Value<Rotator>()` read them with doubles on every version, like `pos.X` |
| `ArrayProperty` | an array of the element type, like `int[]`. An array of number structs reads as `float[][]` or `double[][]`, but 1 item reads as a `StructValue` |
| `SetProperty` | `HashSet<T>` of the element type |
| `MapProperty` | `Dictionary<TKey, TValue>` |

After a collection, an index or key goes into 1 element and `Count` reads the number of elements:

```cs
var second = data.Member("IntArray").Item(1).Value<int>().Unwrap().CreateWatcher(ticks);
var two = data.Member("Dict").Item("two").Value<int>().Unwrap().CreateWatcher(ticks);
var shieldCount = data.Member("Items").Item(1).Member("Count").Value<int>().Unwrap().CreateWatcher(ticks);
var items = data.Member("Items").Count().Value<int>().Unwrap().CreateWatcher(ticks);
```

- `Item` takes an integer, a `string` or a `bool` as a `PathKey`
- a string key matches name and string keys, an integer matches any integer or enum key
- `Item(key).Value<bool>()` on a set reads whether the set has the key
- on a set of objects or a map keyed by objects the key is the position among the used items, like `Item(1).Member("Hp")`. It moves when the game removes an item in front of it
- an array or map of mixed structs doesn't read as 1 value, so index it
- a missing key or an index past the count fails the read. A key of the wrong type, like a string on an array, fails the step
- `Count()` only works on a collection. `Member("Count")` reads a member called `Count` and fails on a collection

Any other kind fails with the property and its class, like `TextProperty` or `SoftObjectProperty`. A struct with a member of such a kind fails as a `StructValue`, but its other members still read through a step. A `StructValue` that changes allocates on that tick, so `Value<float[]>()` or a step to `X` is cheaper for a vector that moves all the time. A string with more than 512 characters fails. A whole array, set or map with more than 1024 elements fails too. A key lookup fails on a set or map with more than 1024 slots, used or free. Pass `new ValueBounds { MaxChars = 2048, MaxElements = 4096 }` to `UnrealGame.Attach` for other limits of up to 1048576. A watcher of a collection keeps its instance while the elements don't change. A profile without the offsets of a kind still attaches and only reads of that kind fail with the missing offsets. Unreal Engine 3 documents don't have the set, map and weak pointer offsets, so those kinds fail there.

The path types are in `AslHelp.Engines.Unreal.Paths`.

The boxed watcher of a path picks the type from the property at read time. Before the 1st good read `Current` and `Old` are null. When another class has a member with that name and another kind, `Old` keeps the old type for 1 tick and `Changed` is true. The path of a root alone reads the `Address` of its object.

Watchers with the same tick counter read the steps their paths share once per tick, also when each path is built again from the same root. 30 watchers under `GWorld.GameMode.Data` cost 39 reads per tick instead of 300, so give all watchers the same counter. `GWorld`, `GEngine` and `FindSubsystem` return 1 root each, but every object a lookup returns has its own root. When the game writes a pointer in the middle of a tick, all of them get it on the next tick. A read without a watcher, like a step check, always reads the whole path. The root keeps every path a step returned, so build paths once and not on each tick.

Every root gives its path through `.Path`:

```cs
var engineHp = game.GEngine.Path.Member("GameViewport").Member("World").Member("AuthorityGameMode").Member("FixtureData").Member("Hp");

var item = game.FindPackage("/Script/Fixture").Unwrap().FindClass("FixtureItem").Unwrap().GetInstances().Unwrap()[0];
var itemName = item.Path.Member("ItemName").Value<string>();

var subsystem = game.FindSubsystem("MySubsystem").Unwrap();
var score = subsystem.Path.Member("Score").Value<int>();
```

- `GEngine` starts at the engine object like `GWorld` at the world. A profile needs `globals.GEngine`.
- A `GWorld` or `GEngine` that isn't found doesn't fail attach, so the other one still reads. A read through it fails and searches the module again after 1 second.
- `FindPackage` takes a path like `/Script/Engine` in any case. On Unreal Engine 3 it takes a bare name like `Engine`. `GetPackages` lists the packages.
- A package has `FindClass` and `GetClasses`. A class has `FindProperty` (through its bases), `GetProperties` (its own), `GetInstances` and `GetDerivedClasses`.
- `GetInstances` lists the live objects of exactly that class without the `Default__` object. It searches the object array like `FindClass`. An object's path fails once the game frees it.
- `FindSubsystem` gives a root at the subsystem of a class in the subsystem map of the game instance, which it takes from `GEngine` or else from `GWorld`. It needs a profile of 4.22 or later. A read finds the subsystem again when it's missing or replaced.
- `World` doesn't have any reflected properties on Unreal Engine 3, so start at `GEngine` or at an object from `GetInstances` on that engine.

`ClassOf` returns the real class of an object and `FindClass` finds a loaded class by name. A miss reads the object array again, but only the objects in slots that changed. `UnrealGame` keeps each class with its properties and each package after the 1st read. It isn't thread-safe. `Read<T>` reads the value of 1 property of an object with the types from the table. `UnrealProperty.Accept` calls `VisitValue<T>`, `VisitEnum`, `VisitStruct`, `VisitCollection` or `VisitUnsupported` of your `PropertyVisitor<TResult>`.

`AslHelp.Engines` has the types an engine library implements. Code that only uses them doesn't depend on `AslHelp.Engines.Unreal`:

- `IMemberPath`: the steps `Member(name)`, `Item(key)`, `Count()` and `Value<T>()` return a `Result`. `Read()` and `Read<T>()` read the value once and `IsChecked` is false for a step the engine couldn't check yet. The path itself is an `IWatcherSource<object?>` and `ToString` gives it as written
- `MemberPathError`: the base of the path errors of an engine. `IsShapeMismatch` is true for a typo and false for a read that can work later
- `PathKey`: an index or key. `PathKey.TryCreate(object)` takes any integer, a `string` or a `bool`
- `StructValue`: a struct with its members by name, with value equality so `Changed` works
- `IWatcherSource` and `IWatcherSource<T>`: something a host can create a watcher from
- `TickMemo<T>`: keeps 1 value for 1 tick of 1 counter, so the watchers of a counter read the shared steps of paths once per tick

```cs
Result<IMemberPath> health = root.Path.Member("Player").Member("Health");
Watcher<object?> watcher = health.Unwrap().CreateWatcher(ticks);
```

The steps after the 1st one come from extension members on `Result<IMemberPath>`, which pass the 1st failure on.

`ProfileCatalog<TProfile>` picks profile documents for any engine. You pass the mapper and the version parser of your engine. The parser turns version text into the numbers of a `ProfileVersion`, so your engine decides the order, like a rank for a release letter. `ProfileReader` gives the mapper the parts every document has: `engine`, `key`, `versions`, `offsets` and `globals` as `MemoryLocator`s. Other root keys fail unless the mapper reads them with `ReadObject`. `Add`, `AddFile` and `AddEmbedded` add documents and `Select` picks 1 for a `ProfileTarget` with the rules above. The version can come from the file version of the module:

```cs
var catalog = new ProfileCatalog<MyProfile>(MyProfile.Map, MyVersion.Parse);
catalog.AddEmbedded(typeof(MyProfile).Assembly, "Profiles/").Unwrap();

var fileVersion = module.GetFileVersion().Unwrap();
var target = new ProfileTarget("my-engine", MyVersion.Parse(fileVersion.Text!)!, "x64");
var selection = catalog.Select(target, ProfileGuessHandling.Disallow).Unwrap();
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
| `--output`, `-o` | The folder for the downloads, the install and the players. Required. |

`just fixtures` runs from your current folder, so a relative `--output` path starts there. If you don't use `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.

1 version can have several players. The `build` command installs the toolchain once, builds all the players in 1 pass and checks that each one starts. Each player ends up in `<output>/<toolchain>-<version>-<hash>` and the command prints those folders at the end, 1 per line. A download that stops picks up where it left off on the next run.

Before each download and each extraction of an install, the `build` command makes sure the drive has room for that step, the build and 20 GiB on top. It reads each size when the step starts: a download from the server's response, an extraction from the file list of the installer and an engine from its manifest. When the drive doesn't have the room, the command removes what failed runs left behind, then the oldest installers in `<output>/downloads` and then the oldest finished engines in `<output>/engines`, until it fits. Player folders always stay. If removing all of that still wouldn't be enough, nothing gets removed and the command fails with the free and the needed space.

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

Pass the version as `4.19`, `4.19.2` or `UE_4.19`. If you leave out `--arch` and `--config`, it builds every player of the version (see the table below). Their short forms are `-a` and `-c`.

We get the engines from Epic with [legendary](https://github.com/legendary-gl/legendary), pinned to 1 hotfix per minor version from 4.0 to 5.8. `eng/fixtures/unreal4/releases.json` lists them.

Before your 1st build:

1. Run `winget install astral-sh.uv`. uv gets Python 3.14 and legendary 0.21.1 on its own.
2. Run `just legendary auth` once. It opens the Epic login in your browser and legendary saves the login in `~/.config/legendary`.
3. Build the image. The players get built in a Windows image from `eng/fixtures/unreal4/image/Dockerfile` and the tool never builds it itself. Run a build and it fails with the command to run, like this:

```
docker build -t asl-help/unreal4-build:<tag> "<repo>\artifacts\bin\AslHelp.Engines.Fixtures.Cli\debug\build\unreal4\image"
```

The image needs Docker with Windows containers and about 40 GiB free under the Docker data root. Its tag is a hash of the image folder, so you need a new image whenever a file there changes.

Engines get installed to `<output>/engines/UE_<minor>`. All 37 engines take 563 GiB together and the biggest one is 5.3 at 35 GiB. A build needs the engine's size plus 20 GiB of cook scratch, on top of the 20 GiB every build keeps free. The scratch folders of failed builds of other versions count as leftovers. If long paths are off in Windows, keep `--output` at 28 characters or less.

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

1. Add a row to `eng/fixtures/unreal4/releases.json` with `version`, `app` (like `UE_4.19`), `build`, the `sha1` of the manifest, `installTags`, `players`, `editorFlags`, `packageFlags`, `pathPrepend` (or `null`) and `runtimes`. Add it to the table above too.
2. Run the build command for it.

## Measuring

```
just measure <engine> <players> --output <file> [--profiles <folder>] [--targets <file>] [--jobs <n>] [--min-literals <n>]
```

| Argument | Value |
|---|---|
| `<engine>` | The engine's subcommand, `unreal3` or `unreal4`. `just measure <engine> --help` lists its options. |
| `<players>` | The folder with the players from `just fixtures build`. |
| `--output`, `-o` | The file to write the JSON report to. |
| `--profiles`, `-p` | The folder to write 1 profile document per group to. Leave it out to skip them. |
| `--jobs`, `-j` | How many players to measure at once. The default is the engine's limit, which is 4 for `unreal4` and 1 for `unreal3`. Lower it if you run short on memory. |
| `--min-literals`, `-m` | The fewest bytes a route keeps unmasked, 6 by default. A search at run time also reads the writable memory of the game, where a shorter pattern can match random data. |
| `--targets`, `-t` | The file with the values to find. It replaces the built-in list, so you can change it without a rebuild. |

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

Each group lists `routes` for the globals of its versions. A route finds a global with a signature search through the code of the executable and then reads the global from the operand at `offset`. `forward` uses the same format as the `forward` step of a `LocatorRoute` in AslHelp.Memory, with `?` for the bytes of address operands and branch targets. When no such route covers a version, the search also masks the register of the load, 1 nibble at a time: first the low nibble of the REX prefix, as in `4? 8B 0D`, then the high nibble of the ModRM byte, as in `4? 8B ?D`, and last the whole ModRM byte. A route only counts for a version when every match in that version's image reaches the same RVA, which `reached` lists. A match that doesn't load an address at `offset`, like `8B 4D 08` for `8B ?D ????????`, makes the route conflict, because the library reads the bytes at `offset` of every match as an address without decoding the instruction. `uncovered` gives the reason for each version that no route covers.

### Profiles

The run also writes 1 JSON file per group when you pass `--profiles`. Each file has what an engine library needs to attach to a game of that group. The file name is `<engine>-<1st version>-<key>.json`, like `unreal4-4.18.3-win-x64-shipping.json`. A group without a key gets `<engine>-<1st version>.json`, like `unreal3-2010-09.json`. Use an empty folder because the run doesn't delete the files of an older run. `globals` gives a locator per global in the JSON form that `MemoryLocator` in AslHelp.Memory reads. Each route of the report becomes 1 locator route, listed under the 1st version it covers. The library ships the documents of the measured players from `src/AslHelp.Engines.Unreal/Profiles`, copied there as the run wrote them.

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
