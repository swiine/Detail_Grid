@echo off
rem Removes DoomChat and CivDOOM for the current Windows user. Close Civil 3D first.
set "PLUGINS=%APPDATA%\Autodesk\ApplicationPlugins"
if exist "%PLUGINS%\DoomChat.bundle" rmdir /S /Q "%PLUGINS%\DoomChat.bundle"
if exist "%PLUGINS%\CivDoom.bundle" rmdir /S /Q "%PLUGINS%\CivDoom.bundle"
if exist "%PLUGINS%\DoomChat.bundle" (
  echo Couldn't remove everything. Is Civil 3D still open?
) else (
  echo DoomChat and CivDOOM removed. Your name and folder settings stay in %APPDATA%\DoomChat.
)
pause
