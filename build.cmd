@echo off
setlocal
rem ---------------------------------------------------------------
rem ClaudeGauge - build script
rem Uses only the C# compiler that ships with Windows (.NET Framework 4.8).
rem No NuGet, no extra SDK required.
rem ---------------------------------------------------------------

set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "OUTDIR=%ROOT%dist"
set "OUTEXE=%OUTDIR%\ClaudeGauge.exe"

if not exist "%CSC%" (
  echo [ERROR] C# compiler not found: %CSC%
  exit /b 1
)

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo Building...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /codepage:65001 ^
  /out:"%OUTEXE%" ^
  /win32manifest:"%ROOT%app.manifest" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  "%ROOT%src\*.cs"

if errorlevel 1 (
  echo [ERROR] Build failed
  exit /b 1
)

echo Done: %OUTEXE%
endlocal
