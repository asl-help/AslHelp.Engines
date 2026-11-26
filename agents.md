# agents.md

## Commands

| Command | What it does |
|---|---|
| `just build` | Builds every project. |
| `just check` | Runs the encoding check, the format check, the build and the tests, the same as CI. Run it before every commit. |
| `just encoding` | Checks the encoding and the line endings of every tracked file. |
| `just fixtures <arguments>` | Runs the `fixtures` command line from the current folder and passes each argument through as is, like `just fixtures build <toolchain> <version> --output <folder>` or `just fixtures build <toolchain> --help`. |
| `just fixtures-all <toolchain> <folder>` | Builds every pinned version of the toolchain into the folder, 1 version at a time. |
| `just format` | Formats every file in place. |
| `just legendary <arguments>` | Runs the pinned legendary through uv, like `just legendary auth`. |
| `just measure <engine> <players> --output <file> [--profiles <folder>] [--targets <file>] [--jobs <n>]` | Runs the `measure` command line from the current folder. |
| `just test` | Runs every test, 1 test project at a time, because the player tests take the machine-wide lock. |

If `just check` doesn't find the runtime, set `DOTNET_ROOT` to the .NET install, like `C:\Program Files\dotnet`.

The tool projects and their tests target `net11.0-windows` because the players only run on Windows. The libraries in `src/` target `netstandard2.0` and their tests also run on `net4.8.1`. Build and test on Windows.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Files under `eng/fixtures` check out with CRLF on every machine because the player cache key hashes their bytes.
- The route search reads files and never a running game, so a measurer only returns the path of the executable.
- Log lines of the `measure` command go in `[LoggerMessage]` methods of `MeasureLog`, `Unreal3/UdkLog` and `Unreal4/UnrealLog`, because Microsoft.Extensions.Logging needs their templates at compile time.
- Other text that users read, like messages, step lines and command help, goes in the `SR.resx` of its project. UDK text goes in `Unreal3/UdkSR.resx` and Unreal Engine 4 and 5 text in `Unreal4/UnrealSR.resx`. `Microsoft.CodeAnalysis.ResxSourceGenerator` generates the `SR`, `UdkSR` and `UnrealSR` classes. They get a property for each message and a `Format` method for each message with placeholders, like `SR.FormatFolderNotFound(path)`.
- `src/` and the `measure` tool return `Result` or `Result<T>` for expected failures. They throw for cancellation and programmer errors. `UdkPlayer` throws when a player doesn't start. A probing loop uses `UnwrapOr` because a miss is normal there.
- `readme.md`, `changelog.md` and this file change in the same commit as the code they describe. `changelog.md` only lists changes to the libraries in `src/` because the tools in `eng/tools/` don't ship.
- The player tests of both test projects take the machine-wide lock, so `just test`, `just check` and CI run the test projects 1 at a time. Don't run the tests while a build runs on the machine.
- Tests never read the machine's `FIXTURES_DAV_*` variables. A `CommandLine` test passes a store or clears the variables first.
- A test that needs a real UDK installer or install only runs when `FIXTURES_UDK_INSTALLER` is the path of a downloaded `UDKInstall-<version>.exe` or `FIXTURES_UDK_INSTALL` is the path of an extracted install. The tests change that install. A test that needs a built player only runs when `FIXTURES_UDK_PLAYER` is the path of a player folder. A test that reads a real PDB only runs when `FIXTURES_UNREAL_PDB` is the path of the PDB of the 4.19 x64 Shipping player, with the executable next to it. A test that measures a real player only runs when `FIXTURES_UNREAL_PLAYER` is the folder of that build, with `Fixture/Binaries/Win64` in it. The attach test of `AslHelp.Engines.Unreal` starts that player and also needs `FIXTURES_UNREAL_PROFILE`, the path of its profile document from `just measure --profiles`. It doesn't take the machine-wide lock, so don't run it while another player test runs.
- A test that builds real Unreal Engine 4 or 5 players only runs when `FIXTURES_UNREAL_BUILD` is a pinned version and `FIXTURES_UNREAL_BUILD_OUTPUT` is a folder with that engine in `engines`. The machine also needs the build image with the tag the tool prints.
- A test that needs uv and the legendary package only runs when `FIXTURES_UV_TESTS` is 1. CI doesn't set it, so run those tests locally before a PR that changes `eng/fixtures/unreal4/install_engine.py`.
- Engine installs stay in `<output>/engines`. Never put them in the repo, CI, CI logs or the store, because Epic serves them again.
- A person builds the build image with `docker build` from `eng/fixtures/<engine>/image`. The tools never build images, so each step stays readable and a person can run it by hand.
- Engine build tools only run in containers of the build image. The command lines are in the scripts in `eng/fixtures/unreal4/build`. The C# picks the scripts, the mounts and the variables from the row of `releases.json` and reads the exit codes and the logs. Anything that differs between versions is a field of the row.
- `~/.config/legendary` has the Epic login. Only legendary reads it, so never open or print `user.json`.
- `.vscode/settings.json` turns off the analysis of the UnrealScript extension, because the engine classes like `Actor` are in the UDK install and not in the repo. To get the analysis, add `Development/Src` of a UDK install to the workspace and set `unrealscript.analyzeDocuments` to `OnlyActive` in the workspace file.
- A file has 1 main type and any number of `file` types. A helper type only stays nested when it needs the private members of its outer type or shows up in a member signature, because a `file` type can't do either.
- Commit subjects are lowercase and imperative, with no body and no trailers.

## Layout

- `src/AslHelp.Engines`: parts every engine library shares, like member chains. It doesn't contain any engine code.
- `src/AslHelp.Engines.Unreal`: engine library. It has 1 type per storage shape because shapes change between minor engine versions, not major ones. `UnrealProfile` reads a profile document and maps each shape value to a `NameTableShape`, `ObjectArrayShape` or `PropertyShape`, which creates the reader of that shape. `UnrealGame.Attach` finds the globals and puts the readers together.
- `eng/tools/AslHelp.Engines.Fixtures`: parts both command lines use, like cache keys, `Player`, the UDK install paths in `Unreal3/UdkInstall` and the player tags and ids in `Unreal4/UnrealPlayer`.
- `eng/tools/AslHelp.Engines.Fixtures.Cli`: `fixtures` command line. An engine plugs in through `IToolchain`, which adds the engine's own command line options and lists the players of a version. `EmbeddedReleases` reads an engine's `releases.json` with the shared checks and the engine's row type adds its own checks through `IReleaseRow`. A store plugs in through `ICacheStore`. `Formats/` reads and writes archives, cabinets, self-extracting zips and PE imports. `Hosting/` runs players and build steps in containers and processes. `Transfer/` downloads files, talks to the WebDAV store and shows progress.
- `eng/tools/AslHelp.Engines.Measuring.Cli`: `measure` command line. `Program.cs` lists the engines and `CommandLine` doesn't mention any of them. An engine plugs in through `IEngineMeasurer`. It adds the engine's own command line options, picks the players, sets how many players run at once, gives a version its group key and picks the offsets that decide a group. `Routes/` is the route search and `Profiles/` writes the profile documents. Neither of them contains any engine code. `Pdb/` reads layouts, addresses and constants from a PDB and doesn't contain any engine code either. `Targets/` reads the targets file of any engine and leaves each engine's rules to that engine. `Files/` opens PE files and has the errors for a file that doesn't open. `Unreal3/` runs players and `Unreal4/` reads PDBs and executables. The `Errors/` folders of `Files/`, `Pdb/`, `Profiles/`, `Targets/`, `Unreal3/` and `Unreal4/` have 1 error type per concern, like `TargetsError` for a targets file that can't be read and `UdkTargetsError` for a file that breaks a UDK rule.
- `eng/fixtures/<engine>`: everything 1 engine brings. `releases.json` is the pin table and `targets.json` has the measure targets. The `fixtures` command line imports `fixtures.props` and the `measure` command line imports `measure.props`, so a new engine adds 1 folder and 1 import line per command line.
- `eng/fixtures/unreal3`: UnrealScript classes of the Unreal Engine 3 player.
- `eng/fixtures/unreal4`: C++ project of the Unreal Engine 4 and 5 players, 1 tree for every minor from 4.0 to 5.8. `//@if >=4.14`, `//@else` and `//@endif` lines (`;@` in `.ini`) keep or drop the lines after them for the version being rendered. The operators are `>=`, `>`, `<`, `<=` and `==` on major.minor. `image/` has the Dockerfile of the build image and its scripts. `build/` has the step scripts the build runs in that image. `install_engine.py` installs an engine through legendary.
- `test/<Project>.Tests`: tests of a project.
- `eng/CheckEncoding.cs`: encoding and line ending check, the same file in every repo.
- A new project goes into `src/` if it ships and `eng/tools/` if it doesn't. Either way it also goes into `AslHelp.Engines.slnx`.
- Engine code goes in a folder per engine, like `Unreal3/` or `Unreal4/` (that one is for Unreal Engine 4 and 5). Everything else uses neutral names without any engine in them.
