# agents.md

## Commands

```
just build
just test
just format
just encoding
just check
just fixtures <arguments>
just fixtures-all <folder>
just measure <engine> <players> --output <file> [--targets <file>]
```

`just check` runs the encoding check, the format check, the build and the tests, as CI does. Run it before a commit.

`just fixtures` runs the `fixtures` command line from the current folder and passes each argument through whole, such as `just fixtures build <toolchain> <version> --output <folder>`. `just fixtures-all` builds every pinned UDK version into the folder.

The tool projects and their tests target `net11.0-windows`, because the players only run on Windows. The libraries in `src/` target `netstandard2.0` and their tests also run on `net4.8.1`. Build and test on Windows.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Files under `test/fixtures` check out with CRLF on every machine, because the player cache key hashes their bytes.
- The route search reads files and never a running game, so a measurer only returns the path of the executable.
- Log lines of the `measure` command sit in `[LoggerMessage]` methods of `MeasureLog` and `Unreal3/UdkLog`, because Microsoft.Extensions.Logging takes their templates at compile time.
- Other text that users read, such as messages, step lines and command help, sits in the `SR.resx` of its project. The UDK text sits in `Unreal3/UdkSR.resx`. `Microsoft.CodeAnalysis.ResxSourceGenerator` generates the `SR` and `UdkSR` classes, with a property for each message and a `Format` method for each message with placeholders, such as `SR.FormatFolderNotFound(path)`.
- `src/` and the `measure` tool return `Result` or `Result<T>` for expected failures. They throw for cancellation and programmer errors and `UdkPlayer` still throws for a player that does not start. A probing loop uses `UnwrapOr`, because a miss is normal there.
- `readme.md`, `changelog.md` and this file change in the same commit as the code they describe. `changelog.md` lists the changes to the libraries in `src/`, because the tools in `eng/tools/` do not ship.
- The player tests of both test projects take the machine-wide lock, so `just test`, `just check` and CI run the test projects 1 at a time. Run the tests while no build runs on the machine.
- Tests never read the machine's `FIXTURES_DAV_*` variables. A `CommandLine` test passes a store or clears the variables first.
- A test that needs a real UDK installer or install runs only when `FIXTURES_UDK_INSTALLER` is the path of a downloaded `UDKInstall-<version>.exe` or `FIXTURES_UDK_INSTALL` is the path of an extracted install. The tests change that install. A test that needs a built player runs only when `FIXTURES_UDK_PLAYER` is the path of a player folder.
- A file has 1 main type and any number of `file` types. A helper type stays nested only when it needs the private members of its outer type or sits in a member signature, because a `file` type cannot.
- Commit subjects are lowercase and imperative, with no body and no trailers.

## Layout

- `src/AslHelp.Engines.Unreal`: engine library. 1 type per storage shape, because shapes change between minor engine versions, not major ones.
- `eng/tools/AslHelp.Engines.Fixtures`: parts both command lines use, such as cache keys and `Player`.
- `eng/tools/AslHelp.Engines.Fixtures.Cli`: `fixtures` command line. An engine plugs in through `IToolchain`, a store through `ICacheStore`.
- `eng/tools/AslHelp.Engines.Measuring.Cli`: `measure` command line. An engine plugs in through `IEngineMeasurer`. `Routes/` is the route search, with no engine in it.
- `test/fixtures/unreal3`: UnrealScript classes of the Unreal Engine 3 player.
- `test/<Project>.Tests`: tests of a project.
- `eng/CheckEncoding.cs`: encoding and line ending check, same file in every repo.
- A new project goes into `src/` if it ships, `eng/tools/` if not, and into `AslHelp.Engines.slnx`.
- Engine code sits in a folder per engine, such as `Unreal3/`. Everything else uses neutral names, with no engine in them.
