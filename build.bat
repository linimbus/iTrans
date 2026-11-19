@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo   itrans build script
echo   Target: publish\Itrans.exe
echo ==========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found.
    echo         Please install .NET 10 SDK: https://dotnet.microsoft.com/download
    goto :failed
)

if not exist "src\Itrans\Itrans.csproj" (
    echo [ERROR] src\Itrans\Itrans.csproj was not found.
    echo         Please run this script from the project root.
    goto :failed
)

echo [1/1] Publishing self-contained single-file build...
echo.

dotnet publish "src\Itrans\Itrans.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo         If publish\Itrans.exe is locked, close the running Itrans app and retry.
    goto :failed
)

if not exist "publish\Itrans.exe" (
    echo [ERROR] publish\Itrans.exe was not generated.
    goto :failed
)

if exist "publish\Itrans.pdb" del /q "publish\Itrans.pdb"

echo.
echo [OK] Build finished.
echo      Output: %CD%\publish\Itrans.exe
echo.
set "EXIT_CODE=0"
goto :done

:failed
set "EXIT_CODE=1"

:done
if /i not "%~1"=="--nopause" pause
endlocal & exit /b %EXIT_CODE%
