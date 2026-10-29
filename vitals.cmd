@echo off
setlocal
set "ROOT=%~dp0"

rem Release zip: exe sits next to this launcher.
set "EXE=%ROOT%vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%dist\vitals-win-x64\vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%dist\vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%src\Vitals\bin\Release\net10.0-windows\vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%src\Vitals\bin\Debug\net10.0-windows\vitals.exe"

if not exist "%EXE%" (
  echo vitals.exe was not found.
  echo Build it first:  pwsh -File scripts\publish.ps1
  exit /b 1
)

rem Headless commands must stay in this console so pipes and file redirection
rem work. Scan every argument, not just the first, because options may precede
rem the command (for example: vitals.cmd --json snapshot).
rem Setup needs elevation, but the launcher should wait for it and return its
rem exit code so scripts can detect failures.
set "HEADLESS="
set "WAIT="
for %%A in (%*) do (
  if /I "%%~A"=="snapshot" set "HEADLESS=1"
  if /I "%%~A"=="watch" set "HEADLESS=1"
  if /I "%%~A"=="export" set "HEADLESS=1"
  if /I "%%~A"=="sensors" set "HEADLESS=1"
  if /I "%%~A"=="--render-once" set "HEADLESS=1"
  if /I "%%~A"=="--check" set "HEADLESS=1"
  if /I "%%~A"=="-h" set "HEADLESS=1"
  if /I "%%~A"=="--help" set "HEADLESS=1"
  if /I "%%~A"=="--version" set "HEADLESS=1"
  if /I "%%~A"=="setup" set "WAIT=1"
)
if defined HEADLESS goto run

rem fltmc requires administrator rights and does not depend on the Server service.
fltmc >nul 2>&1
if not errorlevel 1 goto run

set "VITALS_EXE=%EXE%"
set "VITALS_ARGS=%*"
set "VITALS_WAIT=%WAIT%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = @{ FilePath = $env:VITALS_EXE; Verb = 'RunAs'; ErrorAction = 'Stop' }; if ($env:VITALS_ARGS) { $p.ArgumentList = $env:VITALS_ARGS }; if ($env:VITALS_WAIT) { $p.Wait = $true; $p.PassThru = $true }; $proc = Start-Process @p; if ($env:VITALS_WAIT) { exit $proc.ExitCode } } catch { Write-Host 'vitals: elevation was cancelled.'; exit 1 }"
exit /b %errorlevel%

:run
"%EXE%" %*
exit /b %errorlevel%
