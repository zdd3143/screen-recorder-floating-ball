# 打包：把构建产物 + ffmpeg 收拢到 dist\屏录球\，并（尝试）创建桌面快捷方式
# 用法（在仓库任意位置）：  pwsh -File tools\build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\ScreenRecorder\ScreenRecorder.csproj'
$dist = Join-Path $root 'dist\屏录球'

$env:DOTNET_CLI_HOME = Join-Path $root '.cli-home'
$env:DOTNET_NOLOGO = '1'

Write-Host '==> 构建 Release' -ForegroundColor Cyan
dotnet build $proj -c Release -v q
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

$bin = Join-Path $root 'src\ScreenRecorder\bin\Release\net10.0-windows'
Write-Host '==> 收拢到 dist' -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $bin '*') $dist -Recurse -Force
Copy-Item (Join-Path $root 'tools\ffmpeg\ffmpeg.exe') $dist -Force
Copy-Item (Join-Path $root 'README.md') $dist -Force

Write-Host '==> 创建桌面快捷方式' -ForegroundColor Cyan
$exe = Join-Path $dist '屏录球.exe'
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$lnk = Join-Path $desktop '屏录球.lnk'
try {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($lnk)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $dist
    $sc.Description = '屏录球 - 点击悬浮球录制屏幕与声音'
    $sc.IconLocation = "$exe,0"
    $sc.Save()
    Write-Host "    已创建：$lnk" -ForegroundColor Green
} catch {
    Write-Warning "创建快捷方式失败：$($_.Exception.Message)"
    Write-Host '    可先运行一次程序，程序会自动创建；或右键托盘菜单选择「创建桌面快捷方式」。'
}

Write-Host ''
Write-Host "完成。程序目录：$dist" -ForegroundColor Green
Write-Host "可执行文件：$exe"
