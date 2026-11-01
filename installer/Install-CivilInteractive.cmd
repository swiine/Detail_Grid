@echo off
rem Installs CivilInteractive for the current Windows user. Close Civil 3D first.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\CivilInteractive.bundle"
if exist "%DEST%" rmdir /S /Q "%DEST%"
xcopy /E /I /Y "%~dp0CivilInteractive.bundle" "%DEST%" >nul
if errorlevel 1 (
  echo Install failed. Is Civil 3D still open?
) else (
  echo CivilInteractive installed to %DEST%
  echo Start Civil 3D and type CICHAT, or click the CI icon in the status bar.
)
pause
