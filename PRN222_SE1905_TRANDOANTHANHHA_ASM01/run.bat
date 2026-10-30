@echo off
dotnet run --project "%~dp0src\SelfStorageManagementSystem.Web" %*
exit /b %errorlevel%
