@echo off
echo Building Windows Dual Audio Manager v1.0.0 Release...

:: Clean previous builds
dotnet clean -c Release

:: Restore packages
dotnet restore

:: Build release version
dotnet build -c Release

:: Create output directory for installer if it doesn't exist
if not exist "installer" mkdir installer

echo.
echo Build complete. Release files are located in:
echo %CD%\bin\Release\net6.0-windows\

:: Instructions for creating installer with Inno Setup
echo.
echo To create the installer:
echo 1. Download and install Inno Setup from https://jrsoftware.org/isdl.php
echo 2. Open InstallerScript.iss with Inno Setup
echo 3. Click Build -^> Compile

echo.
echo Required files for distribution:
echo - All files in the bin\Release\net6.0-windows directory
echo.
