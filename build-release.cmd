@echo off
setlocal
cd /d "%~dp0"
dotnet publish ".\WeatherHueWallpaper.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ".\publish"
if errorlevel 1 exit /b %errorlevel%
echo Release written to "%CD%\publish"
