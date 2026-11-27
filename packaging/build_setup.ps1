# =====================================================================
# MD3 Zip · 生成 Windows 安装包（单文件 exe）
# ---------------------------------------------------------------------
# 流程：
#   1. self-contained 发布主程序（内置 .NET 8，目标机无需装运行时）
#   2. 补齐 Shell 扩展、MSIX 稀疏包、自签证书、右键菜单脚本
#   3. 签名所有 MD3Zip.* PE
#   4. 压缩为 payload.zip（内嵌进安装器）
#   5. 编译 WinForms 安装器（单文件 + 压缩）
#   6. 签名安装包
#
# 用法：powershell -ExecutionPolicy Bypass -File packaging\build_setup.ps1
#       -SkipPublish  复用已有 payload（只重打安装包）
#       -NoSign       跳过签名
# =====================================================================
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [string]$Configuration = "Release",
    [switch]$NoSign
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$setupDir = Join-Path $PSScriptRoot "setup"
$payload = Join-Path $setupDir "payload"
$payloadZip = Join-Path $setupDir "payload.zip"
$outDir = Join-Path $setupDir "out"
$finalExe = Join-Path $setupDir "MD3Zip-Setup-x64.exe"

$pfx = Join-Path $PSScriptRoot "certs\MD3ZipDev.pfx"
$pfxPassword = "MD3Zip!2026"
$signtool = Get-ChildItem (Join-Path $PSScriptRoot "tools\packages\microsoft.windows.sdk.buildtools") `
    -Recurse -Filter "signtool.exe" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\x64\*" } |
    Select-Object -First 1 -ExpandProperty FullName

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Sign-Files([string[]]$paths) {
    if ($NoSign -or -not $signtool -or -not (Test-Path $pfx)) {
        Write-Host "  跳过签名（-NoSign 或缺少 signtool/pfx）" -ForegroundColor Yellow
        return
    }

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) { continue }
        & $signtool sign /fd SHA256 /f $pfx /p $pfxPassword $path | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "签名失败: $path" }
    }
}

# ---------------------------------------------------------------------
# 1. 主程序（self-contained）
# ---------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step "发布主程序（self-contained，内置 .NET 8 运行时）"
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }

    & $dotnet publish (Join-Path $root "src\MD3Zip.App\MD3Zip.App.csproj") `
        -c $Configuration -r win-x64 --self-contained true -o $payload --nologo
    if ($LASTEXITCODE -ne 0) { throw "主程序发布失败。" }
}
elseif (-not (Test-Path $payload)) {
    throw "payload 不存在，请先不带 -SkipPublish 运行一次。"
}

$size = (Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("  负载文件: {0} 个 / {1:N1} MB" -f (Get-ChildItem $payload -Recurse -File).Count, ($size / 1MB))

# ---------------------------------------------------------------------
# 2. Shell 扩展与右键菜单资源
# ---------------------------------------------------------------------
Write-Step "补齐 Shell 扩展与右键菜单资源"

$shellOut = Join-Path $root "src\MD3Zip.ShellExtension\bin\$Configuration\net8.0-windows\win-x64"
if (-not (Test-Path (Join-Path $shellOut "MD3Zip.ShellExtension.comhost.dll"))) {
    & $dotnet build (Join-Path $root "src\MD3Zip.ShellExtension\MD3Zip.ShellExtension.csproj") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Shell 扩展编译失败。" }
}

foreach ($file in @(
    "MD3Zip.ShellExtension.dll",
    "MD3Zip.ShellExtension.comhost.dll",
    "MD3Zip.ShellExtension.deps.json",
    "MD3Zip.ShellExtension.runtimeconfig.json")) {
    $source = Join-Path $shellOut $file
    if (-not (Test-Path $source)) { throw "缺少 Shell 扩展产物: $file" }
    Copy-Item $source $payload -Force
}

$extras = @(
    [pscustomobject]@{ Src = (Join-Path $PSScriptRoot "publish\MD3Zip.Shell.msix"); Name = "MD3Zip.Shell.msix" },
    [pscustomobject]@{ Src = (Join-Path $PSScriptRoot "certs\MD3ZipDev.cer"); Name = "MD3ZipDev.cer" },
    [pscustomobject]@{ Src = (Join-Path $PSScriptRoot "一键安装右键菜单.cmd"); Name = "一键安装右键菜单.cmd" }
)
foreach ($extra in $extras) {
    if (Test-Path $extra.Src) {
        Copy-Item $extra.Src (Join-Path $payload $extra.Name) -Force
        Write-Host "  + $($extra.Name)"
    }
    else {
        Write-Host "  ! 缺少 $($extra.Name)" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------
# 3. 签名负载中的程序文件
# ---------------------------------------------------------------------
Write-Step "签名程序文件"
$payloadPe = Get-ChildItem $payload -File |
    Where-Object { $_.Name -like "MD3Zip.*" -and $_.Extension -in @(".exe", ".dll") } |
    Select-Object -ExpandProperty FullName
Sign-Files $payloadPe
Write-Host ("  已处理 {0} 个文件" -f $payloadPe.Count)

# ---------------------------------------------------------------------
# 4. 压缩负载
# ---------------------------------------------------------------------
Write-Step "压缩负载"
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payload, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("  payload.zip: {0:N1} MB" -f ((Get-Item $payloadZip).Length / 1MB))

# ---------------------------------------------------------------------
# 5. 编译安装器
# ---------------------------------------------------------------------
Write-Step "编译安装程序（单文件 + 内嵌负载）"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

& $dotnet publish (Join-Path $root "src\MD3Zip.Setup\MD3Zip.Setup.csproj") -c $Configuration -o $outDir --nologo
if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败。" }

$built = Join-Path $outDir "MD3Zip.Setup.exe"
if (-not (Test-Path $built)) { throw "未找到 MD3Zip.Setup.exe。" }
Copy-Item $built $finalExe -Force

# ---------------------------------------------------------------------
# 6. 签名安装包
# ---------------------------------------------------------------------
Write-Step "签名安装包"
Sign-Files @($finalExe)

Write-Host ""
Write-Host "安装包已生成：" -ForegroundColor Green
Write-Host ("  {0}" -f $finalExe) -ForegroundColor Green
Write-Host ("  大小: {0:N1} MB" -f ((Get-Item $finalExe).Length / 1MB))
Write-Host ""
Write-Host "验证：" -ForegroundColor Gray
Write-Host "  .\MD3Zip-Setup-x64.exe            图形安装向导"
Write-Host "  .\MD3Zip-Setup-x64.exe /S         静默安装"
Write-Host "  `"$outDir\MD3Zip.Setup.exe`" /S /D=`"C:\Temp\MD3Zip`"" -ForegroundColor Gray
