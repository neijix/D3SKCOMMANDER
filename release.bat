@echo off
rem Double-click it to build every package into dist\ with release.sh: the gate, the three
rem archives, the Windows setup when NSIS is installed, SHA256SUMS. Opens dist\ when it worked
rem and waits for a key, so the window stays readable. Options go to release.sh (--no-check,
rem --no-setup). Needs Git for Windows (its bash runs release.sh) and the .NET 10 SDK.
rem macOS and Linux: release.command. D3SK_UNATTENDED=1: no wait, no folder opened (scripted runs).
setlocal
cd /d "%~dp0"
set "GITBASH="
if exist "%ProgramFiles%\Git\bin\bash.exe" set "GITBASH=%ProgramFiles%\Git\bin\bash.exe"
if not defined GITBASH if exist "%LOCALAPPDATA%\Programs\Git\bin\bash.exe" set "GITBASH=%LOCALAPPDATA%\Programs\Git\bin\bash.exe"
rem Else beside git.exe on PATH (...\Git\cmd\git.exe -> ...\Git\bin\bash.exe); not WSL's bash.
if not defined GITBASH for /f "delims=" %%G in ('where git.exe 2^>nul') do if not defined GITBASH if exist "%%~dpG..\bin\bash.exe" set "GITBASH=%%~dpG..\bin\bash.exe"
if defined GITBASH goto :run
echo ERROR: Git Bash not found. Install Git for Windows: https://git-scm.com/download/win
set "CODE=1"
goto :end

:run
"%GITBASH%" ./release.sh %*
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" goto :failed
echo Packages are in %CD%\dist
if not defined D3SK_UNATTENDED start "" "%CD%\dist"
goto :end

:failed
echo release.sh failed (exit code %CODE%): see the messages above.

:end
if not defined D3SK_UNATTENDED pause
exit /b %CODE%
