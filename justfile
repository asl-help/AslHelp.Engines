set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

# List the recipes.
default:
    @just --list

# Build every project.
build:
    dotnet build

# Run every test.
test:
    dotnet test

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
    dotnet test --no-build

# Run the fixtures command line with the arguments as typed, from the current folder.
[no-cd]
[positional-arguments]
[script("pwsh", "-NoLogo", "-NoProfile")]
fixtures *args:
    dotnet run --project '{{ justfile_directory() }}/eng/tools/AslHelp.Engines.Fixtures.Cli' -- @args
    # Exit with the code of the run or with 1 when dotnet did not start, because the script exits 0 after a failed command.
    exit ($LASTEXITCODE ?? 1)
