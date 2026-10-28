@echo off
rem Installs DoomChat for the current Windows user: CivDOOM (the game) and DoomChat (its lobby chat) together.
rem Close Civil 3D first.
set "PLUGINS=%APPDATA%\Autodesk\ApplicationPlugins"
set "DEST=%PLUGINS%\DoomChat.bundle"
rem An older CivDOOM-only install would load the game twice - replace it.
if exist "%PLUGINS%\CivDoom.bundle" rmdir /S /Q "%PLUGINS%\CivDoom.bundle"
if exist "%DEST%" rmdir /S /Q "%DEST%"
xcopy /E /I /Y "%~dp0DoomChat.bundle" "%DEST%" >nul
if errorlevel 1 (
  echo Install failed. Is Civil 3D still open?
) else (
  echo DoomChat and CivDOOM installed to %DEST%
  echo Start Civil 3D: type DOOMCHAT for the lobby, CIVDOOMHOST to start a game.
)
pause
