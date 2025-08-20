@echo off
chcp 65001 > nul
echo +----------------------------------------+
echo |     MinimalSProxy Token Generator      |
echo +----------------------------------------+

REM Check if username was provided as argument
IF "%~1"=="" (
    REM No username provided, run in interactive mode
    TokenGenerator.exe
) ELSE (
    REM Username provided as argument
    TokenGenerator.exe %1
)