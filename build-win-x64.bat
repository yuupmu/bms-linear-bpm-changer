@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET 10 SDK is not installed.
  echo Download: https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)

echo Building a self-contained Windows x64 executable...
dotnet publish "src\BmsLinearBpmChanger.WinForms\BmsLinearBpmChanger.WinForms.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishTrimmed=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "dist\win-x64"

if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. See the messages above.
  pause
  exit /b 1
)

echo.
echo Build complete:
echo %~dp0dist\win-x64\BmsLinearBpmChanger.exe
pause
