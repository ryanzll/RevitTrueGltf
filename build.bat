@echo off
setlocal enabledelayedexpansion

:: Default version. Can be overridden via command-line arg (e.g., build.bat v1.0.1)
set ZIP_VERSION=v0.1.0
if not "%~1"=="" set ZIP_VERSION=%~1

set SOLUTION_PATH=src\RevitTrueGltf.sln
set OUT_ROOT=bin

echo ================================================
echo       Building RevitTrueGltf Release %ZIP_VERSION%
echo ================================================

:: Iterate over all supported Revit versions
for %%V in (2020 2021 2022 2023 2024 2025 2026) do (
    set REVIT_VERSION=%%V
    set CONFIG=Release%%V
    
    echo.
    echo [1/3] Building for Revit !REVIT_VERSION! ^(!CONFIG!^)...
    dotnet build "!SOLUTION_PATH!" --configuration !CONFIG!
    
    set OUT_DIR=!OUT_ROOT!\!REVIT_VERSION!
    
    if exist "!OUT_DIR!" (
        echo [2/3] Cleaning up unnecessary Revit API DLLs...
        
        :: Remove proprietary Revit DLLs to reduce package size and avoid conflicts
        if exist "!OUT_DIR!\RevitAPI.dll" del /f /q "!OUT_DIR!\RevitAPI.dll"
        if exist "!OUT_DIR!\RevitAPIUI.dll" del /f /q "!OUT_DIR!\RevitAPIUI.dll"
        if exist "!OUT_DIR!\UIFramework.dll" del /f /q "!OUT_DIR!\UIFramework.dll"
        if exist "!OUT_DIR!\AdWindows.dll" del /f /q "!OUT_DIR!\AdWindows.dll"
        
        set ZIP_NAME=RevitTrueGltf_%ZIP_VERSION%_Revit!REVIT_VERSION!.zip
        set ZIP_PATH=!OUT_ROOT!\!ZIP_NAME!
        
        if exist "!ZIP_PATH!" del /f /q "!ZIP_PATH!"
        
        echo [3/3] Packaging to !ZIP_NAME!...
        :: Call built-in PowerShell to compress the folder without third-party tools like WinRAR
        powershell -NoProfile -Command "Compress-Archive -Path '!OUT_DIR!\*' -DestinationPath '!ZIP_PATH!' -Force"
        
        echo       Done.
    ) else (
        echo Error: Output directory !OUT_DIR! not found! Build might have failed.
    )
)

echo.
echo ================================================
echo   All versions built and packaged successfully!  
echo   Release ZIP files are located in the 'bin' folder.
echo ================================================
pause
