[CmdletBinding()]
param(
    [int]$Iterations = 20,
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Exe)) {
    $candidates = @(
        (Join-Path $root 'dist\vitals.exe'),
        (Join-Path $root 'src\Vitals\bin\Release\net8.0-windows\vitals.exe'),
        (Join-Path $root 'src\Vitals\bin\Debug\net8.0-windows\vitals.exe')
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

Write-Host "benchmarking $Exe"
Write-Host "iterations: $Iterations (one full snapshot, including process list warm-up)"

$times = New-Object System.Collections.Generic.List[double]
for ($i = 0; $i -lt $Iterations; $i++) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $Exe snapshot --json 2>$null | Out-Null
    $watch.Stop()
    $times.Add($watch.Elapsed.TotalMilliseconds)
}

$sorted = $times | Sort-Object
$average = ($times | Measure-Object -Average).Average
$min = $sorted[0]
$max = $sorted[$sorted.Count - 1]
$p95Index = [math]::Min($sorted.Count - 1, [math]::Floor($sorted.Count * 0.95))

"min    {0,8:0.0} ms" -f $min
"avg    {0,8:0.0} ms" -f $average
"p95    {0,8:0.0} ms" -f $sorted[$p95Index]
"max    {0,8:0.0} ms" -f $max
