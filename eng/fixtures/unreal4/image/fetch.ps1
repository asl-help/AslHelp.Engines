param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$Sha256,
    [Parameter(Mandatory)][string]$Out
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Invoke-WebRequest -Uri $Url -OutFile $Out -UseBasicParsing

$actual = (Get-FileHash $Out -Algorithm SHA256).Hash

if ($actual -ne $Sha256) {
    throw "sha256 of ${Out}: expected $Sha256, got $actual"
}
