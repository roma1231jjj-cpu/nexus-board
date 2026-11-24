@echo off
setlocal
set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
set FW=C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~dp0..\NexusBoard-setup.exe
set PAYLOAD=%~dp0..\NexusBoard.exe

%CSC% /nologo /target:winexe /platform:anycpu /optimize+ /langversion:7.3 ^
 /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Drawing.dll" ^
 /r:"%FW%\System.Windows.Forms.dll" ^
 /resource:"%PAYLOAD%",NexusBoard.exe ^
 /out:"%OUT%" "%~dp0Setup.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK %OUT%
