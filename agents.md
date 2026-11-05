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

`just fixtures` runs the `fixtures` command line from the current folder and passes each argument through whole, such as `just fixtures --help`.

Every project targets `net11.0-windows`, because the players only run on Windows. Build and test on Windows.

## Layout

- `eng/tools/AslHelp.Engines.Fixtures` is the library. It builds the cache keys, lists the inputs each key hashed and starts the players, 1 per machine, inside a kill-on-close job object, with the Windows hard-error dialogs off so a missing static import ends the player with STATUS_DLL_NOT_FOUND.
- `eng/tools/AslHelp.Engines.Fixtures.Cli` is the `fixtures` command line. It has these parts:
  - `Archive` packs and unpacks the `.tar.zst` archives, each zstd frame with a content checksum.
  - `DavStore` is the WebDAV cache store. `FromEnvironmentIfSet` opens it from the `FIXTURES_DAV_*` variables and writes a line that lists them when none is set.
  - `ICacheStore` is the part a cache store plugs in, with a download and an upload. `DavStore` implements it and the tests use a fake store.
  - `TransferTimer` sends the requests of `DavStore` and reads and writes their bodies with time limits. A request fails when its server sends no response headers within 1 minute. After the last byte of an upload the limit is 5 minutes, because the server stores the file first. A body read or write fails when it moves no bytes for 1 minute. `DavStore` throws a `TimeoutException` with the store path and deletes its partial file.
  - `HttpHandlers` creates the handler of `DavStore`. The handler stops a connect attempt after 30 seconds and checks the certificate of the server for revocation online.
  - `TransferProgress` is how far a download or an upload has come: the bytes moved and the length of the file. `DavStore` reports it after each chunk.
  - `TempPaths` has the `.partial` suffix of a file or folder that is still being written and the quiet deletes that clean one up.
- `eng/CheckEncoding.cs` checks the encoding and the line endings of every tracked file. CI and `just check` run it as a file-based app with `dotnet eng/CheckEncoding.cs`.
- The tests for a project sit in `test/<Project>.Tests`.
- A new project goes into `src/` when it ships and into `eng/tools/` when it only builds or tests the repo. It also goes into `AslHelp.Engines.slnx`.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Text that users read, such as messages and command help, sits in the `SR.resx` of its project. The code reads it through the generated `SR` class and fills placeholders with `SR.Format`. `Directory.Build.targets` generates the class in design-time builds too, so `dotnet format` compiles it.
- `readme.md` and this file change in the same commit as the code they describe.
- Tests never read the machine's `FIXTURES_DAV_*` variables.
- Commit subjects are lowercase and imperative, with no body and no trailers.
