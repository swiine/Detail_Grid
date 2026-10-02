@echo off
rem Double-click to build. Output: out\TTWLinemarking.dll
rem Uses Civil 3D's own Autodesk DLLs if AutoCAD 2026 is installed in the default folder,
rem otherwise downloads Autodesk's AutoCAD.NET reference package from nuget.org.
setlocal
cd /d "%~dp0"

set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=C:\dotnet-sdk\dotnet-sdk-8.0.424-win-x64\dotnet.exe"

"%DOTNET%" build "src\TTWLinemarking\TTWLinemarking.csproj" -c Release -o out %*
if errorlevel 1 (
  echo.
  echo BUILD FAILED - send the errors above.
  pause
  exit /b 1
)

echo.
echo Built: %~dp0out\TTWLinemarking.dll
pause
