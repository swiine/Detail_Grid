@echo off
rem Drag a drawing folder onto this file to split it.
if "%~1"=="" (
    echo Drag the drawing folder onto this file.
    pause
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0SplitDrawings.ps1" -Source "%~1"
pause
