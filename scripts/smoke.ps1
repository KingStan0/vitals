[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Exe)) {
    throw "executable not found: $Exe"
}

$version = & $Exe --version
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^vitals \d+\.\d+\.\d+') {
    throw "unexpected --version output: $version"
}

$json = & $Exe snapshot --json --no-driver
if ($LASTEXITCODE -ne 0) {
    throw "snapshot --json failed with exit code $LASTEXITCODE"
}

try {
    $snapshot = $json | ConvertFrom-Json
}
catch {
    throw "snapshot did not emit valid JSON: $($_.Exception.Message)"
}

if ($null -eq $snapshot.cpuLoadPercent) {
    throw 'snapshot is missing cpuLoadPercent'
}

if ($snapshot.status.lhmAvailable -ne $false) {
    throw '--no-driver should report lhmAvailable=false'
}

if ($snapshot.status.appVersion -ne $version.Replace('vitals ', '')) {
    throw "version mismatch: status=$($snapshot.status.appVersion) cli=$version"
}

if (-not $snapshot.readings -or $snapshot.readings.Count -lt 1) {
    throw 'snapshot has no readings'
}

if ($null -eq $snapshot.gpus) {
    throw 'snapshot is missing the gpus array'
}

Write-Host ("smoke ok: {0}, {1} readings, {2} processes" -f $version, $snapshot.readings.Count, @($snapshot.processes).Count)
