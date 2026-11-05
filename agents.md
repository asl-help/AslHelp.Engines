# agents.md

## Commands

```
just build
just test
just format
just encoding
just check
just fixtures <arguments>
```

`just check` runs the encoding check, the format check, the build and the tests, as CI does. Run it before a commit.

`just fixtures` runs the `fixtures` command line from the current folder and passes each argument through whole, such as `just fixtures build <toolchain> <version> --output <folder>`.

Every project targets `net11.0-windows`, because the players only run on Windows. Build and test on Windows.

## Layout

- `eng/tools/AslHelp.Engines.Fixtures` is the library. It builds the cache keys, lists the inputs each key hashed and starts the players, 1 per machine, inside a kill-on-close job object, with the Windows hard-error dialogs off so a missing static import ends the player with STATUS_DLL_NOT_FOUND.
- `eng/tools/AslHelp.Engines.Fixtures.Cli` is the `fixtures` command line. Its `build` command installs a toolchain, builds the player, checks it and caches it as `<output>/<toolchain>-<version>-<hash>` and `players/<toolchain>/<version>/<hash>.tar.zst`, with a `<hash>.json` manifest beside the archive. It has the parts its commands use:
  - `Archive` packs and unpacks the `.tar.zst` archives, each zstd frame with a content checksum. The `build` command uses it for the cached players.
  - `DavStore` is the WebDAV cache store that the `build` command uses. `FromEnvironmentIfSet` opens it from the `FIXTURES_DAV_*` variables and writes the line that the build stays local when none is set.
  - `ICacheStore` is the part a cache store plugs in, with a download and an upload. `DavStore` implements it and the tests use a fake store. The default `CommandLine.RunAsync` is the one place that picks the store, so `PlayerCache` and the `build` command use only `ICacheStore`.
  - `PlayerCache` restores a player from `<output>/<toolchain>-<version>-<hash>` or the store and stores a new build in both. `KeyOf` builds the key from the toolchain name, the project folder and the toolchain's inputs and returns a `PlayerKey`. It throws when the version a toolchain returns is not the `version` input it adds. `StoreAsync` writes `manifest.json` into the install before it moves the install to the player folder and uploads the manifest after the archive. A hit never reads a manifest.
  - `PlayerKey` is the key of a player with its toolchain, the pinned version and the inputs the key hashed. `PlayerKeyContext` writes it as the manifest JSON.
  - `Downloader` downloads a file pinned to a hash.
  - `TransferTimer` sends the requests of `Downloader` and `DavStore` and reads and writes their bodies with time limits. A request fails when its server sends no response headers within 1 minute. After the last byte of an upload the limit is 5 minutes, because the server stores the file first. A body read or write fails when it moves no bytes for 1 minute. `Downloader` and `DavStore` throw a `TimeoutException` with the URL or the store path. `Downloader` keeps its partial file for the next run. `DavStore` deletes its partial file.
  - `HttpHandlers` creates the handler of `Downloader` and `DavStore`. The handler stops a connect attempt after 30 seconds and checks the certificate of the server for revocation online.
  - `TransferProgress` is how far a download or an upload has come: the bytes moved and the length of the file. `Downloader` and `DavStore` report it after each chunk.
  - `TransferBars` shows a Spectre.Console progress bar for each transfer and clears it when the transfer ends. `TransferBars.Create` draws real bars on the console of this process and plain lines with no ANSI codes on any other writer, such as a test's `StringWriter`. `CommandLine` wraps the toolchain install in a bar and `PlayerCache` wraps the archive download and upload, so nothing else writes to the console while a bar is drawn.
  - `ArchiveOrgFile` is 1 file of an archive.org item with its pinned sha1 and its download URL.
  - `Unreal3/installers.json` lists the pinned UDK installers, 1 row per version with its sha1 and the date it was last checked. The CLI embeds it and `UdkInstallers` reads it once.
  - `SelfExtractingZip` reads the zip inside an installer without running it.
  - `ProcessRunner` runs a command line tool without a window and reads its output while the tool runs, so the output never reaches the console. A code other than 0 fails with the command line, the code and at most the last 10 lines of the output that are not blank.
  - `Cabinet` reads the file names of a Microsoft cabinet and unpacks its files with `expand.exe`.
  - `TempPaths` has the `.partial` suffix of a file or folder that is still being written and the quiet deletes that clean one up.
  - `Unreal3/` has the UDK toolchain, 1 part per step:
    - `Udk` is the toolchain. It calls the parts below and adds the key inputs in a fixed order.
    - `UdkInstallers` reads the pinned installers and finds the one of a version.
    - `UdkInstall` defines the install paths that the other parts share.
    - `UdkRuntimes` copies the runtime DLLs that `UDK.exe` imports from the install's redist. `DirectXRuntimes` unpacks the DirectX ones and `VisualCppRuntimes` the Visual C++ 2010 ones. `VisualCppRuntimes` also refuses the 2008 runtime.
    - `UdkProject` copies the fixture classes, edits the config files and compiles the package.
    - `UdkServer` starts the server and waits for the ready line in its log.
    - `UdkRedist` opens the install's `UE3Redist.exe`, finds the cabinet of a DLL and extracts entries.
    - `PeImports` reads the import DLL names of a PE file.
  - `IToolchain` installs an engine release at a version, builds players in it, checks that they start and adds the inputs of its build to the player key with `AddKeyInputs`, which returns the version as the toolchain pins it.
- `test/fixtures/unreal3` has the UnrealScript classes of the Unreal Engine 3 player. The CLI project copies them to its output as `projects/unreal3/Classes`.
- Engine-specific code sits in a folder per engine in the CLI project, such as `Unreal3/`, with 1 `IToolchain` each. Engine names appear only in the engine folders (`Unreal3/` in the CLI and its tests, `test/fixtures/unreal3`), in `Unreal3/UdkSR.resx`, in the toolchain list of `CommandLine`, in the CLI project items that copy or embed engine files and in the engine parts of this file and the readme. Everything else uses neutral names, with no engine in them.
- `eng/CheckEncoding.cs` checks the encoding and the line endings of every tracked file. CI and `just check` run it as a file-based app with `dotnet eng/CheckEncoding.cs`.
- The tests for a project sit in `test/<Project>.Tests`.
- A new project goes into `src/` when it ships and into `eng/tools/` when it only builds or tests the repo. It also goes into `AslHelp.Engines.slnx`.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Files under `test/fixtures` check out with CRLF on every machine, because the player cache key hashes their bytes.
- Text that users read, such as messages, step lines and command help, sits in the `SR.resx` of its project. The UDK text sits in `Unreal3/UdkSR.resx`. The code reads it through the generated `SR` and `UdkSR` classes and fills placeholders with `SR.Format`. `Directory.Build.targets` generates the classes in design-time builds too, so `dotnet format` compiles them.
- `readme.md` and this file change in the same commit as the code they describe.
- The player tests of both test projects take the machine-wide lock, so `just test`, `just check` and CI run the test projects 1 at a time. Run the tests while no build runs on the machine.
- Tests never read the machine's `FIXTURES_DAV_*` variables. A `CommandLine` test passes a store or clears the variables first.
- A test that needs a real UDK installer or install runs only when `FIXTURES_UDK_INSTALLER` is the path of a downloaded `UDKInstall-<version>.exe` or `FIXTURES_UDK_INSTALL` is the path of an extracted install. The tests change that install.
- Commit subjects are lowercase and imperative, with no body and no trailers.
