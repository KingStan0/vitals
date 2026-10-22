[CmdletBinding()]
param(
    [string]$Version = 'dev',
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'vitals-win-x64'
$cleanVersion = $Version.TrimStart('v', 'V')
$zip = Join-Path $dist "vitals-$cleanVersion-$Runtime.zip"

if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}

& (Join-Path $PSScriptRoot 'publish.ps1') `
    -Runtime $Runtime `
    -Configuration $Configuration `
    -Output $stage

& (Join-Path $PSScriptRoot 'smoke.ps1') -Exe (Join-Path $stage 'vitals.exe')

Copy-Item (Join-Path $root 'vitals.cmd') $stage -Force

if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $bytes = $sha.ComputeHash($stream)
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }

    return ([System.BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
}

$hash = Get-Sha256 $zip
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii -NoNewline

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "package: $zip ($size MB)"
Write-Host "sha256 : $hash"
Write-Host "version: $Version"
