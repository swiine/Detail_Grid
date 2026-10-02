@echo off
rem Double-click and pick the drawing folder, or drag the folder onto this file.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0SplitDrawings.ps1" -Source "%~1"
pause
