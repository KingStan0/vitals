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
rem work. Only the interactive dashboard needs elevation.
if /I "%~1"=="snapshot" goto run
if /I "%~1"=="watch" goto run
if /I "%~1"=="export" goto run
if /I "%~1"=="sensors" goto run
if /I "%~1"=="-h" goto run
if /I "%~1"=="--help" goto run
if /I "%~1"=="--version" goto run

rem fltmc requires administrator rights and does not depend on the Server service.
fltmc >nul 2>&1
if not errorlevel 1 goto run

set "VITALS_EXE=%EXE%"
set "VITALS_ARGS=%*"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = @{ FilePath = $env:VITALS_EXE; Verb = 'RunAs'; ErrorAction = 'Stop' }; if ($env:VITALS_ARGS) { $p.ArgumentList = $env:VITALS_ARGS }; Start-Process @p } catch { Write-Host 'vitals: elevation was cancelled.'; exit 1 }"
exit /b %errorlevel%

:run
"%EXE%" %*
exit /b %errorlevel%
