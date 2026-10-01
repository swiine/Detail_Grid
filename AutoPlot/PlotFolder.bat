@echo off
rem Drag a folder onto this file, or run:  PlotFolder.bat "P:\Job123\Sheets"
rem For more options (page setup override, subfolders...) run Plot-Folder.ps1 directly.
setlocal
set "FOLDER=%~1"
if "%FOLDER%"=="" set /p "FOLDER=Folder of drawings to plot: "
set "FOLDER=%FOLDER:"=%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Plot-Folder.ps1" -Folder "%FOLDER%"
pause
