@echo off
setlocal
set DOTNET_ROLL_FORWARD=Major
dotnet run --project "%~dp0src\SelfStorageManagementSystem.Web" %*
exit /b %errorlevel%
