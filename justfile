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
