[CmdletBinding()]
param(
    [int]$Iterations = 20,
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Exe)) {
    # Prefer a source build in a checkout; dist may be an older release. Pass
    # -Exe explicitly when benchmarking a packaged build.
    $candidates = @(
        (Join-Path $root 'src\Vitals\bin\Release\net10.0-windows\vitals.exe'),
        (Join-Path $root 'src\Vitals\bin\Debug\net10.0-windows\vitals.exe'),
        (Join-Path $root 'dist\vitals.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $Exe = $candidate
            break
        }
    }
}

if (-not $Exe -or -not (Test-Path $Exe)) {
    throw 'vitals.exe not found. Run scripts\publish.ps1 first.'
}

if ($Iterations -lt 1) {
    throw 'Iterations must be at least 1.'
}

Write-Host "benchmarking $Exe"
Write-Host "iterations: $Iterations (one full snapshot, including process list warm-up)"

$times = New-Object System.Collections.Generic.List[double]
for ($i = 0; $i -lt $Iterations; $i++) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $Exe snapshot --json 2>$null | Out-Null
    $exitCode = $LASTEXITCODE
    $watch.Stop()
    if ($exitCode -ne 0) {
        throw "snapshot failed with exit code $exitCode"
    }

    $times.Add($watch.Elapsed.TotalMilliseconds)
}

$sorted = @($times | Sort-Object)
$average = ($times | Measure-Object -Average).Average
$min = $sorted[0]
$max = $sorted[$sorted.Count - 1]
$p95Index = [math]::Max(0, [math]::Min($sorted.Count - 1, [math]::Ceiling($sorted.Count * 0.95) - 1))

"min    {0,8:0.0} ms" -f $min
"avg    {0,8:0.0} ms" -f $average
"p95    {0,8:0.0} ms" -f $sorted[$p95Index]
"max    {0,8:0.0} ms" -f $max
