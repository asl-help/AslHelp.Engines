set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

# List the recipes.
default:
    @just --list

# Build every project.
build:
    dotnet build

# Run every test, 1 test project at a time, because the player tests of both take the machine-wide lock.
test:
    dotnet test -m:1

# Format every file in place.
format:
    dotnet format --severity info

# Check the encoding and the line endings of every tracked file, as CI does.
encoding:
    dotnet eng/CheckEncoding.cs

# Run the checks CI runs: the encoding check, the format check, the build and the tests.
check: encoding
    dotnet format --severity info --verify-no-changes
    dotnet build --no-incremental
    dotnet test --no-build -m:1

# Run the fixtures command line with the arguments as typed, from the current folder.
[no-cd]
[positional-arguments]
[script("pwsh", "-NoLogo", "-NoProfile")]
fixtures *args:
    dotnet run --project '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Fixtures.Cli' -- @args
    # Exit with the code of the run or with 1 when dotnet did not start, because the script exits 0 after a failed command.
    exit ($LASTEXITCODE ?? 1)

# Build the players of every pinned version of the toolchain into the folder, 1 version at a time, stream the output and print each result.
[no-cd]
[script("pwsh", "-NoLogo", "-NoProfile")]
fixtures-all toolchain output:
    $releases = Get-Content -Raw '{{ justfile_directory() }}/eng/fixtures/{{ toolchain }}/releases.json' -ErrorAction Stop | ConvertFrom-Json
    $failed = 0
    foreach ($version in $releases.version) {
        $started = Get-Date
        # Show each line as it comes, because a failure prints its reason over several lines, and keep the lines for the result.
        dotnet run --project '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Fixtures.Cli' -- build {{ toolchain }} $version --output '{{ output }}' 2>&1 | ForEach-Object { "$_" } | Tee-Object -Variable lines | Out-Host
        $code = $LASTEXITCODE
        if ($code -ne 0) { $failed++ }
        "$version exit=$code seconds=$([int]((Get-Date) - $started).TotalSeconds) $($lines | Select-Object -Last 1)"
    }
    exit $failed

# Run the pinned legendary with the arguments as typed, such as `just legendary auth`.
[no-cd]
[positional-arguments]
[script("pwsh", "-NoLogo", "-NoProfile")]
legendary *args:
    if (-not (Get-Command uv -ErrorAction Ignore)) { [Console]::Error.WriteLine("'uv' wasn't found. Install uv with 'winget install astral-sh.uv'."); exit 1 }
    # Use the pins of UnrealEngine.cs, because the login and the install have to run the same legendary. A test compares them.
    uv run --no-project --python 3.14 --with legendary-gl==0.21.1 legendary @args
    # Exit with the code of the run or with 1 when uv did not start, because the script exits 0 after a failed command.
    exit ($LASTEXITCODE ?? 1)

# Run the measuring command line with the arguments as typed, from the current folder.
[no-cd]
[positional-arguments]
[script("pwsh", "-NoLogo", "-NoProfile")]
measure *args:
    dotnet run --project '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Measuring.Cli' -- @args
    # Exit with the code of the run or with 1 when dotnet did not start, because the script exits 0 after a failed command.
    exit ($LASTEXITCODE ?? 1)
