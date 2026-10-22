[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'dist'
}

$dotnet = $null
$command = Get-Command dotnet -ErrorAction SilentlyContinue
if ($command) {
    $dotnet = $command.Source
}
if (-not $dotnet) {
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $candidate) {
        $dotnet = $candidate
    }
}
if (-not $dotnet) {
    throw 'The .NET 10 SDK was not found. Install it from https://dot.net and try again.'
}

Write-Host "publishing vitals ($Configuration, $Runtime) -> $Output"
& $dotnet publish (Join-Path $root 'src\Vitals\Vitals.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $root 'README.md') $Output -Force
Copy-Item (Join-Path $root 'CHANGELOG.md') $Output -Force
Copy-Item (Join-Path $root 'LICENSE') $Output -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $Output -Force
Copy-Item (Join-Path $root 'licenses') $Output -Recurse -Force
Copy-Item (Join-Path $root 'assets') $Output -Recurse -Force
Copy-Item (Join-Path $root 'config.example.json') $Output -Force -ErrorAction SilentlyContinue

$docs = Join-Path $Output 'docs'
New-Item -ItemType Directory -Path (Join-Path $docs 'schema') -Force | Out-Null
Copy-Item (Join-Path $root 'docs\troubleshooting.md') $docs -Force
Copy-Item (Join-Path $root 'docs\schema\snapshot.schema.json') (Join-Path $docs 'schema') -Force

$exe = Join-Path $Output 'vitals.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "done: $exe ($size MB)"
Write-Host "run : $exe snapshot --json"
