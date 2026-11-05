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

# Build the player of every pinned UDK version into the folder, 1 at a time, and print each result.
[no-cd]
[script("pwsh", "-NoLogo", "-NoProfile")]
fixtures-all output:
    $installers = Get-Content -Raw '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Fixtures.Cli/Unreal3/installers.json' | ConvertFrom-Json
    $failed = 0
    foreach ($version in $installers.version) {
        $started = Get-Date
        $lines = dotnet run --project '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Fixtures.Cli' -- build unreal3 $version --output '{{ output }}' 2>&1
        $code = $LASTEXITCODE
        if ($code -ne 0) { $failed++ }
        "$version exit=$code seconds=$([int]((Get-Date) - $started).TotalSeconds) $($lines | Select-Object -Last 1)"
    }
    exit $failed
