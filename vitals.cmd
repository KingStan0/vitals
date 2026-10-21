@echo off
setlocal
set "ROOT=%~dp0"

set "EXE=%ROOT%dist\vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%src\Vitals\bin\Release\net8.0-windows\vitals.exe"
if not exist "%EXE%" set "EXE=%ROOT%src\Vitals\bin\Debug\net8.0-windows\vitals.exe"

if not exist "%EXE%" (
  echo vitals.exe was not found.
  echo Build it first:  pwsh -File scripts\publish.ps1
  exit /b 1
)

rem Already elevated? Run in this console so the TUI behaves normally.
net session >nul 2>&1
if not errorlevel 1 (
  "%EXE%" %*
  exit /b %errorlevel%
)

rem Otherwise relaunch through UAC. The dashboard needs admin for CPU and
rem motherboard sensors; scripted JSON output can call vitals.exe directly.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%EXE%' -ArgumentList '%*' -Verb RunAs"
