[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,
    [string]$ExpectedVersion = '',
    [switch]$RequireSchema
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 otherwise decodes native UTF-8 output with the OEM
# code page, which can corrupt sensor names and JSON strings.
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
}
catch {
    # Redirected hosts may not expose a settable console encoding.
}

if (-not (Test-Path $Exe)) {
    throw "executable not found: $Exe"
}

$version = ((& $Exe --version) -join "`n").Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^vitals \d+\.\d+\.\d+') {
    throw "unexpected --version output: $version"
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $expected = $ExpectedVersion.TrimStart('v', 'V')
    if ($version -ne "vitals $expected") {
        throw "expected version vitals $expected, got: $version"
    }
}

$json = (& $Exe snapshot --json --no-driver) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "snapshot --json failed with exit code $LASTEXITCODE"
}

try {
    $snapshot = $json | ConvertFrom-Json
}
catch {
    throw "snapshot did not emit valid JSON: $($_.Exception.Message)"
}

# Test-Json -SchemaFile exists in PowerShell 6.1+. Windows PowerShell 5.1 can
# still run the smoke test; CI uses pwsh and performs the full schema check.
$testJson = Get-Command Test-Json -ErrorAction SilentlyContinue
if ($testJson -and $testJson.Parameters.ContainsKey('SchemaFile')) {
    $schemaPath = Join-Path $PSScriptRoot '..\docs\schema\snapshot.schema.json'
    if (-not (Test-Path $schemaPath)) {
        throw "snapshot schema not found: $schemaPath"
    }

    try {
        $valid = $json | Test-Json -SchemaFile $schemaPath -ErrorAction Stop
    }
    catch {
        throw "snapshot schema validation failed: $($_.Exception.Message)"
    }

    if (-not $valid) {
        throw 'snapshot does not match docs/schema/snapshot.schema.json'
    }
}
elseif ($RequireSchema) {
    throw 'Test-Json -SchemaFile is required but is not available in this PowerShell host'
}

if ($null -eq $snapshot.cpuLoadPercent) {
    throw 'snapshot is missing cpuLoadPercent'
}

if ($null -eq $snapshot.status) {
    throw 'snapshot is missing status'
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

if ($null -eq $snapshot.cores -or $null -eq $snapshot.volumes -or $null -eq $snapshot.temps) {
    throw 'snapshot is missing required arrays'
}

Write-Host ("smoke ok: {0}, {1} readings, {2} processes" -f $version, $snapshot.readings.Count, @($snapshot.processes).Count)
