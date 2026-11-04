@echo off
rem Build (release, incremental) and start d3skcommander. Extra arguments go to the app.
cd /d "%~dp0"
dotnet run --project csharp\src\D3skCommander.App -c Release -- %*
