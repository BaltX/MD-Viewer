@echo off
setlocal

set OUT=%~dp0publish

if exist "%OUT%" (
    echo Cleaning %OUT%...
    rd /s /q "%OUT%"
)

echo Publishing MDViewer...
dotnet publish "%~dp0MDViewer.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "%OUT%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo PUBLISH FAILED
    pause
    exit /b 1
)

echo.
echo Done: %OUT%\MDViewer.exe
pause
