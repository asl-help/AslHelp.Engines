# agents.md

## Commands

```
just build
just test
just format
just encoding
just check
just fixtures <arguments>
just legendary <arguments>
just fixtures-all <toolchain> <folder>
just measure <engine> <players> --output <file> [--targets <file>] [--jobs <n>]
```

`just check` runs the encoding check, the format check, the build and the tests, the same as CI. Run it before every commit. If `just check` doesn't find the runtime, set `DOTNET_ROOT` to the .NET install, like `C:\Program Files\dotnet`.

`just fixtures` runs the `fixtures` command line from the current folder and passes each argument through as is, like `just fixtures build <toolchain> <version> --output <folder>` or `just fixtures build <toolchain> --help`. `just fixtures-all` builds every pinned version of the toolchain into the folder. `just legendary` runs the pinned legendary through uv, like `just legendary auth`.

The tool projects and their tests target `net11.0-windows`, because the players only run on Windows. The libraries in `src/` target `netstandard2.0` and their tests also run on `net4.8.1`. Build and test on Windows.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Files under `eng/fixtures` check out with CRLF on every machine, because the player cache key hashes their bytes.
- The route search reads files and never a running game, so a measurer only returns the path of the executable.
- Log lines of the `measure` command sit in `[LoggerMessage]` methods of `MeasureLog` and `Unreal3/UdkLog`, because Microsoft.Extensions.Logging takes their templates at compile time.
- Other text that users read, like messages, step lines and command help, goes in the `SR.resx` of its project. UDK text goes in `Unreal3/UdkSR.resx` and Unreal Engine 4 and 5 text in `Unreal4/UnrealSR.resx`. `Microsoft.CodeAnalysis.ResxSourceGenerator` generates the `SR`, `UdkSR` and `UnrealSR` classes. They get a property for each message and a `Format` method for each message with placeholders, like `SR.FormatFolderNotFound(path)`.
- `src/` and the `measure` tool return `Result` or `Result<T>` for expected failures. They throw for cancellation and programmer errors and `UdkPlayer` still throws for a player that does not start. A probing loop uses `UnwrapOr`, because a miss is normal there.
- `readme.md`, `changelog.md` and this file change in the same commit as the code they describe. `changelog.md` lists the changes to the libraries in `src/`, because the tools in `eng/tools/` do not ship.
- The player tests of both test projects take the machine-wide lock, so `just test`, `just check` and CI run the test projects 1 at a time. Run the tests while no build runs on the machine.
- Tests never read the machine's `FIXTURES_DAV_*` variables. A `CommandLine` test passes a store or clears the variables first.
- A test that builds real Unreal Engine 4 or 5 players only runs when `FIXTURES_UNREAL_BUILD` is a pinned version and `FIXTURES_UNREAL_BUILD_OUTPUT` is a folder with that engine in `engines`. The machine also needs the build image with the tag the tool prints.
- A test that needs uv and the legendary package only runs when `FIXTURES_UV_TESTS` is 1. CI doesn't set it, so run those tests locally before a PR that changes `eng/fixtures/unreal4/install_engine.py`.
- Engine installs stay in `<output>/engines`. Never put them in the repo, CI, CI logs or the store, because Epic serves them again.
- A person builds the build image with `docker build` from `eng/fixtures/<engine>/image`. The tools never build images, so each step stays readable and a person can run it by hand.
- Engine build tools only run in containers of the build image. The command lines are in the scripts in `eng/fixtures/unreal4/build`. The C# picks the scripts, the mounts and the variables from the row of `releases.json` and reads the exit codes and the logs. Anything that differs between versions is a field of the row.
- `~/.config/legendary` has the Epic login. Only legendary reads it, so never open or print `user.json`.
- A test that needs a real UDK installer or install only runs when `FIXTURES_UDK_INSTALLER` is the path of a downloaded `UDKInstall-<version>.exe` or `FIXTURES_UDK_INSTALL` is the path of an extracted install. The tests change that install. A test that needs a built player only runs when `FIXTURES_UDK_PLAYER` is the path of a player folder. A test that reads a real PDB only runs when `FIXTURES_UNREAL_PDB` is the path of the PDB of the 4.19 x64 Shipping player, with the executable next to it.
- `.vscode/settings.json` turns off the analysis of the UnrealScript extension, because the engine classes like `Actor` are in the UDK install and not in the repo. To get the analysis, add `Development/Src` of a UDK install to the workspace and set `unrealscript.analyzeDocuments` to `OnlyActive` in the workspace file.
- A file has 1 main type and any number of `file` types. A helper type stays nested only when it needs the private members of its outer type or sits in a member signature, because a `file` type cannot.
- Commit subjects are lowercase and imperative, with no body and no trailers.

## Layout

- `src/AslHelp.Engines.Unreal`: engine library. 1 type per storage shape, because shapes change between minor engine versions, not major ones.
- `eng/tools/AslHelp.Engines.Fixtures`: parts both command lines use, like cache keys, `Player`, the UDK install paths in `Unreal3/UdkInstall` and the player tags and ids in `Unreal4/UnrealPlayer`.
- `eng/tools/AslHelp.Engines.Fixtures.Cli`: `fixtures` command line. An engine plugs in through `IToolchain`, which adds its own command line options and lists the players of a version. `EmbeddedReleases` reads the `releases.json` of an engine with the shared checks and the row type of the engine adds its own checks through `IReleaseRow`. A store plugs in through `ICacheStore`. `Formats/` reads and writes archives, cabinets, self-extracting zips and PE imports. `Hosting/` runs players and build steps in containers and processes. `Transfer/` downloads files, talks to the WebDAV store and shows progress.
- `eng/tools/AslHelp.Engines.Measuring.Cli`: `measure` command line. An engine plugs in through `IEngineMeasurer`. `Routes/` is the route search, with no engine in it.
- `eng/fixtures/<engine>`: what 1 engine brings. `releases.json` and `targets.json` are the pin table and the measure targets. The `fixtures` command line imports `fixtures.props` and the `measure` command line imports `measure.props`, so a new engine adds 1 folder and 1 import line per command line.
- `eng/fixtures/unreal3`: UnrealScript classes of the Unreal Engine 3 player.
- `eng/fixtures/unreal4`: C++ project of the Unreal Engine 4 and 5 players, 1 tree for every minor from 4.0 to 5.8. `//@if >=4.14`, `//@else` and `//@endif` lines (`;@` in `.ini`) keep or drop the lines after them for the version being rendered. The operators are `>=`, `>`, `<`, `<=` and `==` on major.minor. `image/` has the Dockerfile of the build image and its scripts. `build/` has the step scripts the build runs in that image. `install_engine.py` installs an engine through legendary.
- `test/<Project>.Tests`: tests of a project.
- `eng/CheckEncoding.cs`: encoding and line ending check, same file in every repo.
- A new project goes into `src/` if it ships, `eng/tools/` if not, and into `AslHelp.Engines.slnx`.
- Engine code goes in a folder per engine, like `Unreal3/` or `Unreal4/` (that one is for Unreal Engine 4 and 5). Everything else uses neutral names without any engine in them.
