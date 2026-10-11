@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo 正在检查屏录球是否还在运行...
tasklist /FI "IMAGENAME eq 屏录球.exe" 2>nul | find /I "屏录球.exe" >nul
if not errorlevel 1 (
  echo.
  echo [!] 屏录球正在运行，请先右键托盘图标退出，然后重新运行本脚本。
  pause
  exit /b 1
)
set SRC=src\ScreenRecorder\bin\Release\net10.0-windows
set DST=dist\屏录球
if not exist "%SRC%\屏录球.exe" (
  echo [!] 没找到编译产物，请先执行: dotnet build src\ScreenRecorder\ScreenRecorder.csproj -c Release
  pause
  exit /b 1
)
echo 正在更新到 %DST% ...
copy /Y "%SRC%\屏录球.exe" "%DST%\" >nul
copy /Y "%SRC%\屏录球.dll" "%DST%\" >nul
copy /Y "%SRC%\屏录球.deps.json" "%DST%\" >nul
copy /Y "%SRC%\屏录球.runtimeconfig.json" "%DST%\" >nul
echo 完成：%DST%\屏录球.exe
pause
