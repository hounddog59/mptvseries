@echo off
rem Installs the MP-TVSeries.dll you just built into MediaPortal (see tools\Install-to-MediaPortal.ps1).
rem Double-click it; Windows will ask for permission because MediaPortal is in Program Files.

net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Install-to-MediaPortal.ps1"
