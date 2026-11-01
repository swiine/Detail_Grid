@echo off
rem Removes CivilInteractive for the current Windows user. Close Civil 3D first.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\CivilInteractive.bundle"
if exist "%DEST%" rmdir /S /Q "%DEST%"
if exist "%DEST%" (
  echo Could not remove it. Is Civil 3D still open?
) else (
  echo CivilInteractive removed. Your name and settings stay in %APPDATA%\CivilInteractive.
)
pause
