[CmdletBinding()]
param(
    [string]$Exe = '',
    [ValidateRange(5, 3600)]
    [int]$Seconds = 60,
    [ValidateSet('headless', 'tui')]
    [string]$Mode = 'headless'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Exe)) {
    # Prefer a source build in a checkout; dist may be an older release. Pass
    # -Exe explicitly when soaking a packaged build.
    $candidates = @(
        (Join-Path $root 'src\Vitals\bin\Release\net10.0-windows\vitals.exe'),
        (Join-Path $root 'src\Vitals\bin\Debug\net10.0-windows\vitals.exe'),
        (Join-Path $root 'dist\vitals.exe')
    )
    $Exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Exe -or -not (Test-Path $Exe)) {
    throw 'vitals.exe not found. Build it with scripts\publish.ps1 or pass -Exe.'
}

Write-Host "soaking $Exe"

$logical = [Environment]::ProcessorCount
$output = [System.IO.Path]::GetTempFileName()

try {
    if ($Mode -eq 'headless') {
        $arguments = @(
            'watch', '--json', '--no-driver',
            '--interval', '1000',
            '--duration', "${Seconds}s"
        )
        $process = Start-Process -FilePath $Exe -ArgumentList $arguments `
            -RedirectStandardOutput $output -NoNewWindow -PassThru
    }
    else {
        $arguments = @('--interval', '1000')
        $process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
    }

    # Windows PowerShell's Start-Process -PassThru does not keep the native
    # handle, so ExitCode is $null after the process exits. Touching Handle
    # retains it and makes the exit-code check below reliable.
    $null = $process.Handle

    $started = [DateTime]::UtcNow
    $deadline = $started.AddSeconds($Seconds + 10)
    $cpuSeconds = 0.0
    $workingSet = [long]0
    $samples = 0

    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
        $workingSet += $process.WorkingSet64
        $samples++
        Start-Sleep -Seconds 1
    }

    $forced = $false
    if (-not $process.HasExited) {
        $forced = $true
        Stop-Process -Id $process.Id -Force
    }

    $process.WaitForExit()
    $process.Refresh()
    if ($process.TotalProcessorTime.TotalSeconds -gt $cpuSeconds) {
        $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
    }

    $elapsed = ([DateTime]::UtcNow - $started).TotalSeconds
    if ($elapsed -le 0) {
        $elapsed = 1
    }

    $oneCore = $cpuSeconds / $elapsed * 100
    $machine = $oneCore / $logical
    $averageWorkingSetMb = if ($samples -gt 0) {
        [math]::Round(($workingSet / $samples) / 1MB, 1)
    }
    else {
        0
    }

    Write-Host ("mode        : {0}, {1} s, {2} logical CPUs" -f $Mode, $Seconds, $logical)
    Write-Host ("cpu time    : {0:N2} s" -f $cpuSeconds)
    Write-Host ("cpu average : {0:N2}% of one core, {1:N3}% of the machine" -f $oneCore, $machine)
    Write-Host ("working set : {0:N1} MB average" -f $averageWorkingSetMb)

    if ($Mode -eq 'headless') {
        if ($forced) {
            throw "watch did not finish within $($Seconds + 10) seconds"
        }

        if ($process.ExitCode -ne 0) {
            throw "watch --json failed with exit code $($process.ExitCode)"
        }

        $lines = @(Get-Content -LiteralPath $output -Encoding UTF8 -ErrorAction SilentlyContinue).Count
        Write-Host ("ndjson lines: {0}" -f $lines)
        if ($lines -lt 1) {
            throw 'watch --json produced no output'
        }
    }
    else {
        Write-Host 'note        : TUI run was stopped with Stop-Process, so shutdown cost is not measured'
    }
}
finally {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
