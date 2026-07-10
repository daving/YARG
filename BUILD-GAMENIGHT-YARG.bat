@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-gamenight-yarg.ps1"
set EXITCODE=%ERRORLEVEL%
echo.
if "%EXITCODE%"=="0" (
    echo Build finished successfully.
) else (
    echo Build did not finish successfully. Check unity-build.log and unity-compile.log in this folder.
)
echo.
pause
exit /b %EXITCODE%
