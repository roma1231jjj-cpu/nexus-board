@echo off
setlocal
set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
set FW=C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~dp0NexusBoard.exe

%CSC% /nologo /target:winexe /platform:anycpu /optimize+ /langversion:7.3 ^
 /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Drawing.dll" ^
 /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Web.Extensions.dll" ^
 /out:"%OUT%" "%~dp0src\NexusBoard.cs" "%~dp0src\BoardCli.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK %OUT%
