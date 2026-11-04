# agents.md

## Commands

```
just build
just test
just format
just encoding
just check
```

`just check` runs the encoding check, the format check, the build and the tests, as CI does. Run it before a commit.

Every project targets `net11.0-windows`, because the players only run on Windows. Build and test on Windows.

## Layout

- `eng/tools/AslHelp.Engines.Fixtures` is the library. It builds the cache keys, lists the inputs each key hashed and starts the players, 1 per machine, inside a kill-on-close job object, with the Windows hard-error dialogs off so a missing static import ends the player with STATUS_DLL_NOT_FOUND.
- `eng/CheckEncoding.cs` checks the encoding and the line endings of every tracked file. CI and `just check` run it as a file-based app with `dotnet eng/CheckEncoding.cs`.
- The tests for a project sit in `test/<Project>.Tests`.
- A new project goes into `src/` when it ships and into `eng/tools/` when it only builds or tests the repo. It also goes into `AslHelp.Engines.slnx`.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- Text that users read, such as messages, sits in the `SR.resx` of its project. The code reads it through the generated `SR` class and fills placeholders with `SR.Format`. `Directory.Build.targets` generates the class in design-time builds too, so `dotnet format` compiles it.
- `readme.md` and this file change in the same commit as the code they describe.
- Commit subjects are lowercase and imperative, with no body and no trailers.
