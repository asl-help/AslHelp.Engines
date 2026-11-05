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
  - `TempPaths` has the `.partial` suffix of a file or folder that is still being written and the quiet deletes that clean one up.
  - `IToolchain` installs an engine release at a version, builds players in it, checks that they start and adds the inputs of its build to the player key with `AddKeyInputs`, which returns the version as the toolchain pins it.
- `eng/CheckEncoding.cs` checks the encoding and the line endings of every tracked file. CI and `just check` run it as a file-based app with `dotnet eng/CheckEncoding.cs`.
- The tests for a project sit in `test/<Project>.Tests`.
- A new project goes into `src/` when it ships and into `eng/tools/` when it only builds or tests the repo. It also goes into `AslHelp.Engines.slnx`.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Text that users read, such as messages, step lines and command help, sits in the `SR.resx` of its project. The code reads it through the generated `SR` class and fills placeholders with `SR.Format`. `Directory.Build.targets` generates the class in design-time builds too, so `dotnet format` compiles it.
- `readme.md` and this file change in the same commit as the code they describe.
- Tests never read the machine's `FIXTURES_DAV_*` variables. A `CommandLine` test passes a store or clears the variables first.
- Commit subjects are lowercase and imperative, with no body and no trailers.
