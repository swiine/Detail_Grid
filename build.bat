@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 8 SDK is not installed.
    echo Download it from https://dotnet.microsoft.com/download/dotnet/8.0  ^(choose "SDK x64"^)
    echo then run this file again.
    pause
    exit /b 1
)

echo Building Block Layer Changer...
dotnet build "BlockLayerChanger\BlockLayerChanger.csproj" -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED - see the errors above.
    pause
    exit /b 1
)

if not exist dist mkdir dist
copy /y "BlockLayerChanger\bin\Release\net8.0-windows\BlockLayerChanger.dll" "dist\BlockLayerChanger.dll" >nul

echo.
echo Done: %~dp0dist\BlockLayerChanger.dll
echo In Civil 3D run NETLOAD, pick that file, then type BLC.
pause
