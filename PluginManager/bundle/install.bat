@echo off
rem Installs PluginManager so AutoCAD 2025/2026 loads it automatically on startup.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\PluginManager.bundle"
xcopy /E /I /Y "%~dp0PluginManager.bundle" "%DEST%" >nul
if errorlevel 1 (
  echo Install failed. Close AutoCAD and try again.
) else (
  echo Installed to %DEST%
  echo Start AutoCAD and type PMLIST to check it is loaded.
)
pause
