param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$Sha256
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$iso = 'C:\setup\dl\vs2013.iso'
$extracted = 'C:\setup\dl\iso'
$log = 'C:\setup\dl\vs2013.log'

& C:\setup\fetch.ps1 -Url $Url -Sha256 $Sha256 -Out $iso

New-Item -ItemType Directory -Force $extracted | Out-Null
tar.exe -xf $iso -C $extracted

if ($LASTEXITCODE -ne 0) {
    throw "tar exit: $LASTEXITCODE"
}

$process = Start-Process "$extracted\vs_community.exe" -ArgumentList "/AdminFile C:\setup\vs2013-admin.xml /Quiet /NoRestart /NoRefresh /NoWeb /Log $log" -Wait -PassThru

if ($process.ExitCode -notin 0, 3010) {
    Get-Content $log -Tail 40 -ErrorAction SilentlyContinue
    throw "installer exit: $($process.ExitCode)"
}

# Read under Continue, because cl.exe prints its banner to stderr and a redirected stderr line throws under Stop.
$ErrorActionPreference = 'Continue'
$cl = & 'C:\Program Files (x86)\Microsoft Visual Studio 12.0\VC\bin\amd64\cl.exe' 2>&1 | Out-String -Width 200
$ErrorActionPreference = 'Stop'

if ($cl -notlike '*Version 18.00*') {
    throw "cl.exe: $cl"
}

& C:\setup\cleanup.ps1
