@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "tests\CoreSmokeTests\CoreSmokeTests.csproj" -c Release
pause
