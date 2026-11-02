@echo off
rem Installs CoWork for the current Windows user. Close Civil 3D first.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\CoWork.bundle"
if exist "%DEST%" rmdir /S /Q "%DEST%"
rem Remove the earlier test version of this tool (it was called CivilInteractive) so only CoWork loads.
if exist "%APPDATA%\Autodesk\ApplicationPlugins\CivilInteractive.bundle" rmdir /S /Q "%APPDATA%\Autodesk\ApplicationPlugins\CivilInteractive.bundle"
xcopy /E /I /Y "%~dp0CoWork.bundle" "%DEST%" >nul
if errorlevel 1 (
  echo Install failed. Is Civil 3D still open?
) else (
  echo CoWork installed to %DEST%
  echo Start Civil 3D and type CWCHAT, or click the CW icon in the status bar.
)
pause
