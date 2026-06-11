@echo off
REM Build script for Jellych Jellyfin Plugin - Windows

setlocal enabledelayedexpansion

if not exist "Jellyfin.Plugin.Jellych.sln" (
  echo Error: Run this script from the repository root
  exit /b 1
)

echo Building Jellych Jellyfin Plugin...
echo.

dotnet clean Jellyfin.Plugin.Jellych.sln -c Release
if %ERRORLEVEL% neq 0 goto error

dotnet restore Jellyfin.Plugin.Jellych.sln
if %ERRORLEVEL% neq 0 goto error

dotnet build Jellyfin.Plugin.Jellych.sln -c Release --no-restore
if %ERRORLEVEL% neq 0 goto error

echo.
echo Build succeeded!
echo.

goto end

:error
echo.
echo ERROR: Build failed!
echo.
exit /b 1

:end
if %ERRORLEVEL% neq 0 goto error

echo.
echo Build succeeded!
echo.
echo Plugin DLL: src\bin\Release\net8.0\Jellych.WebhookPlugin.dll
echo.

exit /b 0

:error
echo.
echo Build failed!
exit /b 1
