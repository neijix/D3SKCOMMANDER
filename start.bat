@echo off
rem Build (release, incremental) and start d3skcommander. Extra arguments go to the app.
cd /d "%~dp0"
cargo build --release || exit /b 1
"target\release\d3skcommander.exe" %*
