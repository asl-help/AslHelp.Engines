param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$Sha256,
    [Parameter(Mandatory)][string]$InstallPath,
    [Parameter(Mandatory)][string[]]$Components
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$bootstrapper = 'C:\setup\dl\vs_BuildTools.exe'
& C:\setup\fetch.ps1 -Url $Url -Sha256 $Sha256 -Out $bootstrapper

$arguments = @('--quiet', '--wait', '--norestart', '--nocache', '--installPath', $InstallPath)

foreach ($component in $Components) {
    $arguments += '--add'
    $arguments += $component
}

$process = Start-Process $bootstrapper -ArgumentList $arguments -Wait -PassThru

if ($process.ExitCode -notin 0, 3010) {
    Get-ChildItem $env:TEMP -Filter 'dd_*.log' | Sort-Object LastWriteTime | Select-Object -Last 3 | ForEach-Object {
        Write-Output "--- $($_.Name)"
        Get-Content $_.FullName -Tail 60
    }

    throw "installer exit: $($process.ExitCode)"
}

# Check state.json, because the installer drops a component the catalog doesn't have and still exits with 0.
$state = Get-ChildItem 'C:\ProgramData\Microsoft\VisualStudio\Packages\_Instances' -Directory |
    ForEach-Object { Get-Content (Join-Path $_.FullName 'state.json') -Raw | ConvertFrom-Json } |
    Where-Object { $_.installationPath -eq $InstallPath }

$selected = $state.selectedPackages | ForEach-Object { $_.id }
$dropped = $Components | Where-Object { $_ -notin $selected }

if ($dropped) {
    throw "components not installed: $($dropped -join ', ')"
}

& C:\setup\cleanup.ps1
