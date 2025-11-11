@echo off
echo ======================================
echo Portway API Test Runner
echo ======================================

REM Navigate to the test project directory
cd /d %~dp0

echo Running tests with detailed output...
dotnet test PortwayApi.Tests.csproj -v n

echo.
echo Test run complete.
echo.

pause