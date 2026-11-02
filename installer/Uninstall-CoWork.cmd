@echo off
rem Removes CoWork for the current Windows user. Close Civil 3D first.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\CoWork.bundle"
if exist "%DEST%" rmdir /S /Q "%DEST%"
if exist "%DEST%" (
  echo Could not remove it. Is Civil 3D still open?
) else (
  echo CoWork removed. Your name and settings stay in %APPDATA%\CoWork.
)
pause
