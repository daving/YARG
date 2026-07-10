@echo off
setlocal
cd /d "%~dp0"
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; $dialog.Description = 'Choose your YARG source folder'; if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $dialog.SelectedPath }"`) do set "YARG_SOURCE=%%I"

if "%YARG_SOURCE%"=="" (
    echo No folder selected.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-gamenight-yarg.ps1" -YargSourcePath "%YARG_SOURCE%"
set EXITCODE=%ERRORLEVEL%
echo.
if "%EXITCODE%"=="0" (
    echo Integration installed. You can now run BUILD-GAMENIGHT-YARG.bat inside that YARG source folder.
) else (
    echo Install failed.
)
echo.
pause
exit /b %EXITCODE%
