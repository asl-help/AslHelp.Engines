# AslHelp.Engines

AslHelp.Engines builds the fixture players that the engine libraries' live tests read from. A fixture player is a small game made with a real engine version, with known values in its memory.

## Usage

```
just fixtures --help
```

`just fixtures` runs the command line from the current folder. Without `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.
