@echo off
setlocal

rem ================== SETTINGS - CHANGE THESE ==================
rem Folder containing the drawings to plot (every .dwg in it is plotted)
set "FOLDER=P:\2026\2616\261651\Design Info\260923 - Addendum 3 - Appendix 6 CAD, Arborist Lakemba, Flood, survey, heritage\south-west-metro-active-transport-link (4)\A36 Surveys\0\0"

rem Office plotter files (.pc3 and its .pmp)
set "PC3DIR=A:\Civil\AutoCAD\Global\Australia\NSW\_default\Printers"
set "PLOTTER=TTW_stdState_Printer_A1_PDF.pc3"
set "PMP=TTW_stdState_Printer_A1_PDF.pmp"

rem Paper size exactly as shown in the Plot dialog.
rem Leave blank to use the plotter's default paper size.
set "PAPER="

rem Plot style table (use . for none)
set "CTB=monochrome.ctb"

rem Landscape or Portrait
set "ORIENT=Landscape"

rem Path to AutoCAD's core console (change the year to match your AutoCAD)
set "ACAD=C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"
rem =============================================================

if not exist "%ACAD%" (
  echo Could not find AutoCAD at: %ACAD%
  echo Change the ACAD line at the top of this file.
  pause
  exit /b 1
)
if not exist "%PC3DIR%\%PLOTTER%" (
  echo Could not find plotter: %PC3DIR%\%PLOTTER%
  pause
  exit /b 1
)

rem Copy the office plotter into your local AutoCAD Plotters folder(s)
rem so AutoCAD can find it by name.
for /d /r "%APPDATA%\Autodesk" %%d in (Plotters) do (
  if exist "%%d\" (
    copy /y "%PC3DIR%\%PLOTTER%" "%%d\" >nul
    if exist "%PC3DIR%\%PMP%" if exist "%%d\PMP Files\" copy /y "%PC3DIR%\%PMP%" "%%d\PMP Files\" >nul
  )
)

rem Build the AutoCAD plot script
set "SCR=%TEMP%\plot_folder.scr"
> "%SCR%" echo _.-PLOT
>> "%SCR%" echo Y
>> "%SCR%" echo.
>> "%SCR%" echo %PLOTTER%
if defined PAPER >> "%SCR%" echo %PAPER%
if not defined PAPER >> "%SCR%" echo.
>> "%SCR%" echo M
>> "%SCR%" echo %ORIENT%
>> "%SCR%" echo N
>> "%SCR%" echo E
>> "%SCR%" echo F
>> "%SCR%" echo C
>> "%SCR%" echo Y
>> "%SCR%" echo %CTB%
>> "%SCR%" echo Y
>> "%SCR%" echo N
>> "%SCR%" echo N
>> "%SCR%" echo N
>> "%SCR%" echo.
>> "%SCR%" echo N
>> "%SCR%" echo Y

set COUNT=0
for %%f in ("%FOLDER%\*.dwg") do (
  echo Plotting %%~nxf ...
  "%ACAD%" /i "%%~ff" /s "%SCR%" /l en-US >nul
  set /a COUNT+=1
)

echo.
echo Done - %COUNT% drawing(s) sent to %PLOTTER%
pause
