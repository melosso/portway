@echo off
setlocal

REM Change this path if you move the tool
set "TOOL_DIR=%~dp0"

REM Debug: Show tool directory
echo TOOL_DIR is "%TOOL_DIR%"

REM Check if EXE exists
if not exist "%TOOL_DIR%Encrypt.exe" (
    echo Error: "%TOOL_DIR%Encrypt.exe" not found. Please build the project first.
    pause
    exit /b 1
)

echo.
echo ==========================================
echo   Portway Encrypt/Decrypt Tool Launcher
echo ==========================================
echo.
echo Select a mode:
echo   1. Encrypt all environment files
echo   2. Decrypt all environment files
echo   3. Verify encryption status
echo   4. Encrypt (custom Environments path)
echo   5. Decrypt (custom Environments path)
echo   6. Verify (custom Environments path)
echo   0. Exit
echo.

set /p mode=Enter your choice (0-6): 

if "%mode%"=="1" (
    "%TOOL_DIR%Encrypt.exe" --encrypt
    pause
    goto :eof
)
if "%mode%"=="2" (
    "%TOOL_DIR%Encrypt.exe" --decrypt
    pause
    goto :eof
)
if "%mode%"=="3" (
    "%TOOL_DIR%Encrypt.exe" --verify
    pause
    goto :eof
)
if "%mode%"=="4" (
    set /p envdir=Enter full path to Environments folder: 
    "%TOOL_DIR%Encrypt.exe" --encrypt --envdir "%envdir%"
    pause
    goto :eof
)
if "%mode%"=="5" (
    set /p envdir=Enter full path to Environments folder: 
    "%TOOL_DIR%Encrypt.exe" --decrypt --envdir "%envdir%"
    pause
    goto :eof
)
if "%mode%"=="6" (
    set /p envdir=Enter full path to Environments folder: 
    "%TOOL_DIR%Encrypt.exe" --verify --envdir "%envdir%"
    pause
    goto :eof
)
if "%mode%"=="0" (
    echo Exiting.
    goto :eof
)

echo Invalid choice.
pause
