@echo off
setlocal enabledelayedexpansion
set "HERE=%~dp0"
set "OUT=%HERE%NexusBoard.exe"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "PF86=%ProgramFiles(x86)%"
set "ROS="
set "CSC="
set "LV="

if exist "%FW%\csc.exe" set "CSC=%FW%\csc.exe"

set "VSWHERE=%PF86%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not defined ROS if exist "%VSWHERE%" (
  "%VSWHERE%" -latest -prerelease -products * -find "MSBuild\**\Bin\Roslyn\csc.exe" >"%TEMP%\nexusboard-vswhere.txt" 2>nul
  for /f "usebackq delims=" %%i in ("%TEMP%\nexusboard-vswhere.txt") do if not defined ROS set "ROS=%%i"
  del "%TEMP%\nexusboard-vswhere.txt" >nul 2>&1
)
for %%v in (2022 2019 2017) do if not defined ROS for /d %%d in ("%PF86%\Microsoft Visual Studio\%%v\*") do if not defined ROS if exist "%%d\MSBuild\Current\Bin\Roslyn\csc.exe" set "ROS=%%d\MSBuild\Current\Bin\Roslyn\csc.exe"
for %%v in (2022 2019 2017) do if not defined ROS for /d %%d in ("%ProgramFiles%\Microsoft Visual Studio\%%v\*") do if not defined ROS if exist "%%d\MSBuild\Current\Bin\Roslyn\csc.exe" set "ROS=%%d\MSBuild\Current\Bin\Roslyn\csc.exe"
if not defined ROS for /f "delims=" %%i in ('where csc.exe 2^>nul') do if not defined ROS set "ROS=%%i"

if not defined CSC if defined ROS (
  set "CSC=%ROS%"
  set "LV=/langversion:7.3"
)

if not defined CSC (
  echo BUILD FAILED: csc.exe not found
  echo   tried: %FW%\csc.exe
  echo   tried: vswhere -find "MSBuild\**\Bin\Roslyn\csc.exe"
  echo   tried: csc.exe from PATH
  exit /b 1
)

set "REFS=/r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Drawing.dll" /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Web.Extensions.dll""
if not exist "%FW%\System.dll" set "REFS="

echo csc: %CSC%
call :compile
if not errorlevel 1 goto ok

if not defined ROS goto fail
if /i "%CSC%"=="%ROS%" goto fail
echo retry with Roslyn: %ROS%
set "CSC=%ROS%"
set "LV=/langversion:7.3"
call :compile
if errorlevel 1 goto fail

:ok
echo BUILD OK %OUT%
exit /b 0

:fail
echo BUILD FAILED
exit /b 1

:compile
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ %LV% %REFS% /out:"%OUT%" ^
 "%HERE%src\NexusBoard.cs" "%HERE%src\BoardCli.cs"
exit /b %errorlevel%
