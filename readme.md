# AslHelp.Engines

AslHelp.Engines builds the fixture players that the engine libraries' live tests read from. A fixture player is a small game made with a real engine version, with known values in its memory.

## Usage

```
just fixtures --help
just fixtures build <toolchain> <version> --output <folder>
```

| Argument | Value |
|---|---|
| `<toolchain>` | The toolchain to build the player with. |
| `<version>` | The version of the toolchain, as its release lists it. |
| `--output` | The folder for the downloads, the install and the player. Required. |

`just fixtures` runs the command line from the current folder, so a relative `--output` folder is read from there. Without `just`, run `dotnet run --project eng/tools/AslHelp.Engines.Fixtures.Cli -- <arguments>` from the repo root.

The `build` command installs the toolchain at that version into the `--output` folder, builds the player in it and checks that the player starts. Then it moves the install to `<output>/<toolchain>-<version>-<hash>` and prints that folder last.

It prints a line as each step starts. The download and upload of a player archive show a progress bar with the size, the speed and the time left, which is cleared when the transfer ends. When the output is not a terminal, such as a log in CI, a transfer prints its name and its percentage instead, once when it starts, at 25, 50 and 75 percent and from 95 percent on.

A failed step prints its error and exits with code 1. A download or an upload fails its step when connecting to the server takes more than 30 seconds. It also fails when the server sends no response within 1 minute or the transfer moves no bytes for 1 minute. After the last byte of an upload, the server gets 5 minutes to respond, because it stores the file first. The message has the URL or the store path.

A failed download from the store starts over on the next run. Run 1 build at a time per `--output` folder, because 2 runs into the same folder write the same files.

## Cache

The `build` command keeps each player in a WebDAV folder, so a player is built only once and other machines download it. These environment variables turn the cache on:

| Variable | Value |
|---|---|
| `FIXTURES_DAV_URL` | The https URL of the WebDAV folder, with no query or fragment. A plain http URL works only for `localhost`. |
| `FIXTURES_DAV_USER` | The WebDAV user |
| `FIXTURES_DAV_TOKEN` | The app password of that user. It is never printed. |

With none of them set, the build stays local and prints a line that lists them. With only some of them set, the build fails and lists the missing ones.

The key of a player covers the toolchain name, the files of the player project that the build uses and the inputs the toolchain adds. These inputs are what goes into the build, not the code of the tool. A player is built once per key. Other machines and later commits download it. The version in a path is the one the toolchain pins, not the one you typed. `<hash>` is the first 16 hex chars of the key. On disk the player sits in the folder `<output>/<toolchain>-<version>-<hash>`. In the store it sits at `players/<toolchain>/<version>/<hash>.tar.zst`, with its manifest `<hash>.json` beside it.

The manifest lists what a player was built from, so you can see why 2 keys differ. It sits in the player folder as `manifest.json` too. It lists the full key as `player-` and all 64 hex chars of its hash, the toolchain, the version and every input in the order the key hashed them. The project folder shows up as 1 content hash, not as a list of files. A build never reads a manifest, so a missing one never fails a build.

A build checks these in order:

1. If `<output>/<toolchain>-<version>-<hash>` exists, it is the player. Nothing is sent over the network.
2. If the store has `players/<toolchain>/<version>/<hash>.tar.zst`, the build downloads it to `<output>/<toolchain>-<version>-<hash>.tar.zst`, unpacks it into `<output>/<toolchain>-<version>-<hash>` and deletes the archive. The toolchain is not installed.
3. Otherwise it installs the toolchain, builds and checks the player, writes `manifest.json` into the install and moves the install to `<output>/<toolchain>-<version>-<hash>`. Then it packs the folder into `<output>/<toolchain>-<version>-<hash>.tar.zst`, uploads that to `players/<toolchain>/<version>/<hash>.tar.zst`, uploads the manifest to `<hash>.json` beside it and deletes the archive.

The archive goes up before the manifest, so a manifest in the store always has its archive. A failed upload keeps the player folder and the archive. The next run finds the folder and does not upload again, so upload the archive and the manifest by hand or delete the folder to build again.

A damaged or cut short `players/<toolchain>/<version>/<hash>.tar.zst` fails the build with that path. The build deletes its download. Delete the file from the store and build again, so the next run builds and uploads the player.
