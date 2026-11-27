# =====================================================================
# MD3 Zip · Windows 11 右键菜单一键部署脚本
# ---------------------------------------------------------------------
# 作用：
#   1. 发布主程序 + Shell 扩展到 packaging\publish（稀疏包的 ExternalLocation）
#   2. 生成并信任本地自签名代码签名证书（导出到 packaging\certs）
#   3. 用 MakeAppx 打包稀疏 MSIX，用 SignTool 签名主程序 / COM 宿主 / 稀疏包
#   4. Add-AppxPackage -ExternalLocation 完成系统级注册
#   5. 重启资源管理器让右键菜单立即生效
#
# 需要：管理员权限；.NET 8 SDK；网络（首次自动下载 Windows SDK 打包工具）
# =====================================================================
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$NoTimestamp,
    [switch]$NoRestart,
    [string]$CertPassword = "MD3Zip!2026",
    [string]$Publisher = "CN=MD3Zip Dev",
    [string]$SdkBuildToolsVersion = "10.0.28000.2957"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------
# 0. 管理员权限检查（未提升则自动请求提权）
# ---------------------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "需要管理员权限，正在请求提权…" -ForegroundColor Yellow
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs
    return
}

$packagingDir = $PSScriptRoot
$root = Split-Path -Parent $packagingDir
$workDir = Join-Path $packagingDir "publish"
$publishDir = Join-Path $workDir "app"
$stageDir = Join-Path $workDir "stage"
$certDir = Join-Path $packagingDir "certs"
$msixPath = Join-Path $workDir "MD3Zip.Shell.msix"
$packageName = "MD3Zip.Shell"

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Resolve-DotnetPath {
    # 优先使用工作区内置的完整 SDK：本机 PATH 上的 dotnet 可能只有运行时、没有 SDK。
    $local = Join-Path $root ".tools\dotnet\dotnet.exe"
    if ((Test-Path $local) -and (Test-Path (Join-Path $root ".tools\dotnet\sdk"))) { return $local }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        $sdks = & $command.Source --list-sdks 2>$null
        if ($sdks) { return $command.Source }
    }

    if (Test-Path $local) { return $local }

    throw "未找到可用的 .NET SDK（dotnet）。请安装 .NET 8 SDK 后重试。"
}

function Resolve-SdkTool([string]$name) {
    $candidates = New-Object System.Collections.Generic.List[string]

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        Get-ChildItem $kitsRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { $candidates.Add((Join-Path $_.FullName "x64\$name.exe")) }
    }

    $candidates.Add((Join-Path $packagingDir "tools\x64\$name.exe"))

    $restoreRoot = Join-Path $packagingDir "tools\packages\microsoft.windows.sdk.buildtools"
    if (Test-Path $restoreRoot) {
        Get-ChildItem $restoreRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                $binRoot = Join-Path $_.FullName "bin"
                if (Test-Path $binRoot) {
                    Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue |
                        Sort-Object Name -Descending |
                        ForEach-Object { $candidates.Add((Join-Path $_.FullName "x64\$name.exe")) }
                }
            }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

function Ensure-SdkTools([string]$dotnetPath) {
    $makeappx = Resolve-SdkTool "makeappx"
    $signtool = Resolve-SdkTool "signtool"
    if ($makeappx -and $signtool) {
        return @{ MakeAppx = $makeappx; SignTool = $signtool }
    }

    Write-Step "未检测到 Windows SDK 打包工具，正在通过 NuGet 自动获取"
    $toolsDir = Join-Path $packagingDir "tools"
    if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir | Out-Null }

    $projectPath = Join-Path $toolsDir "sdkbuildtools.csproj"
    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>NU1701</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="$SdkBuildToolsVersion" />
  </ItemGroup>
</Project>
"@
    Set-Content -Path $projectPath -Value $projectXml -Encoding UTF8
    & $dotnetPath restore $projectPath --packages (Join-Path $toolsDir "packages") | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "自动获取 Windows SDK 打包工具失败。请安装 Windows SDK 后重试。"
    }

    $makeappx = Resolve-SdkTool "makeappx"
    $signtool = Resolve-SdkTool "signtool"
    if (-not $makeappx -or -not $signtool) {
        throw "仍然找不到 makeappx.exe / signtool.exe，请安装 Windows 10/11 SDK。"
    }

    return @{ MakeAppx = $makeappx; SignTool = $signtool }
}

Write-Host "MD3 Zip 右键菜单部署" -ForegroundColor Magenta
Write-Host "工作目录: $packagingDir"

$dotnet = Resolve-DotnetPath
Write-Host "dotnet: $dotnet"

# ---------------------------------------------------------------------
# 1. 发布主程序与 Shell 扩展
# ---------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Step "发布主程序 (win-x64, framework-dependent)"
    if (-not (Test-Path $publishDir)) { New-Item -ItemType Directory -Path $publishDir -Force | Out-Null }

    & $dotnet publish (Join-Path $root "src\MD3Zip.App\MD3Zip.App.csproj") `
        -c Release -r win-x64 --self-contained false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "主程序发布失败。" }

    Write-Step "编译 Shell 扩展 (COM host)"
    & $dotnet build (Join-Path $root "src\MD3Zip.ShellExtension\MD3Zip.ShellExtension.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Shell 扩展编译失败。" }

    $shellOutput = Join-Path $root "src\MD3Zip.ShellExtension\bin\Release\net8.0-windows\win-x64"
    foreach ($file in @(
        "MD3Zip.ShellExtension.dll",
        "MD3Zip.ShellExtension.comhost.dll",
        "MD3Zip.ShellExtension.deps.json",
        "MD3Zip.ShellExtension.runtimeconfig.json")) {
        $source = Join-Path $shellOutput $file
        if (-not (Test-Path $source)) { throw "缺少 Shell 扩展产物: $file" }
        Copy-Item $source $publishDir -Force
    }

    # 稀疏包的 ExternalLocation 里必须能找到清单声明的可执行文件
    if (-not (Test-Path (Join-Path $publishDir "MD3Zip.App.exe"))) {
        throw "发布目录缺少 MD3Zip.App.exe。"
    }
    if (-not (Test-Path (Join-Path $publishDir "Assets\7z\7z.exe"))) {
        Write-Host "警告: 发布目录没有 Assets\7z\7z.exe，压缩功能将不可用。" -ForegroundColor Yellow
    }
}
else {
    Write-Step "跳过构建，使用已有发布目录: $publishDir"
    if (-not (Test-Path (Join-Path $publishDir "MD3Zip.App.exe"))) {
        throw "发布目录不存在或不完整，请先不带 -SkipBuild 运行。"
    }
}

# ---------------------------------------------------------------------
# 2. 自签名证书
# ---------------------------------------------------------------------
Write-Step "准备本地自签名代码签名证书"
if (-not (Test-Path $certDir)) { New-Item -ItemType Directory -Path $certDir | Out-Null }

$cerPath = Join-Path $certDir "MD3ZipDev.cer"
$pfxPath = Join-Path $certDir "MD3ZipDev.pfx"

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    Write-Host "生成新证书: $Publisher"
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Publisher `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(3)

    if (-not $certificate) { throw "创建自签名证书失败。" }
}
else {
    Write-Host "复用已有证书: $($certificate.Thumbprint)"
}

Export-Certificate -Cert $certificate -FilePath $cerPath -Force | Out-Null
$securePassword = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword -Force | Out-Null

Write-Host "导入受信任根证书颁发机构与 TrustedPeople…"
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
Set-Content -Path (Join-Path $certDir "thumbprint.txt") -Value $certificate.Thumbprint -Encoding ASCII

# ---------------------------------------------------------------------
# 3. 打包稀疏 MSIX
# ---------------------------------------------------------------------
Write-Step "准备稀疏包内容"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null
Copy-Item (Join-Path $packagingDir "AppxManifest.xml") $stageDir -Force
Copy-Item (Join-Path $packagingDir "Assets") $stageDir -Recurse -Force

$tools = Ensure-SdkTools $dotnet
Write-Host "MakeAppx: $($tools.MakeAppx)"
Write-Host "SignTool: $($tools.SignTool)"

Write-Step "生成 $packageName.msix（稀疏包）"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

# 稀疏包的 Application/@Executable 与 com:Class/@Path 都指向 ExternalLocation，
# stage 目录里只有清单与图标，严格校验必然报「文件不在包内」。
# 因此直接使用 /nv，避免先失败一次再回退，日志里也更干净。
& $tools.MakeAppx pack /d $stageDir /p $msixPath /o /nv
if ($LASTEXITCODE -ne 0) { throw "MakeAppx 打包失败。" }

# ---------------------------------------------------------------------
# 4. 签名
# ---------------------------------------------------------------------
Write-Step "签名主程序、COM 宿主与稀疏包"
$signArguments = @("sign", "/fd", "SHA256", "/f", $pfxPath, "/p", $CertPassword)
if (-not $NoTimestamp) {
    $signArguments += @("/tr", "http://timestamp.digicert.com", "/td", "SHA256")
}

# ExternalLocation 里所有本项目产出的 PE 都要签名。
# 特别注意：安装之后若再次编译并手工复制 comhost.dll / MD3Zip.ShellExtension.dll，
# 会覆盖掉签名，导致包外 COM 服务器处于「未签名」状态。用本脚本重新部署即可自动修复。
$targets = @(
    Get-ChildItem $publishDir -File |
        Where-Object { $_.Name -like "MD3Zip.*" -and $_.Extension -in @(".exe", ".dll") } |
        Select-Object -ExpandProperty FullName
)
$targets += $msixPath

if (-not (Test-Path (Join-Path $publishDir "MD3Zip.App.exe"))) { throw "发布目录缺少 MD3Zip.App.exe。" }

foreach ($target in $targets) {
    Write-Host "签名 $(Split-Path -Leaf $target)"
    & $tools.SignTool @signArguments $target
    if ($LASTEXITCODE -ne 0) {
        if ($NoTimestamp) { throw "签名失败: $target" }
        Write-Host "带时间戳签名失败，重试不带时间戳…" -ForegroundColor Yellow
        & $tools.SignTool sign /fd SHA256 /f $pfxPath /p $CertPassword $target
        if ($LASTEXITCODE -ne 0) { throw "签名失败: $target" }
    }
}

$unsigned = @(
    Get-ChildItem $publishDir -File |
        Where-Object { $_.Name -like "MD3Zip.*" -and $_.Extension -in @(".exe", ".dll") } |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne "Valid" } |
        Select-Object -ExpandProperty Name
)
if ($unsigned.Count -gt 0) {
    throw "以下外部文件签名无效，请重新执行本脚本: $($unsigned -join ', ')"
}
Write-Host "自检通过：ExternalLocation 内所有 MD3Zip 可执行文件均已签名。" -ForegroundColor Green

# ---------------------------------------------------------------------
# 5. 注册稀疏包
# ---------------------------------------------------------------------
Write-Step "注册稀疏包（ExternalLocation = $publishDir）"
$existing = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "移除旧版本: $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppxPackage -Path $msixPath -ExternalLocation $publishDir
if ($LASTEXITCODE -ne 0) { throw "Add-AppxPackage 注册失败。" }

$installed = Get-AppxPackage -Name $packageName
if (-not $installed) { throw "注册后未查询到包 $packageName。" }

# ---------------------------------------------------------------------
# 6. 刷新资源管理器
# ---------------------------------------------------------------------
if (-not $NoRestart) {
    Write-Step "重启资源管理器以加载新的右键菜单"
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host ""
Write-Host "部署完成。" -ForegroundColor Green
Write-Host "  包全名   : $($installed.PackageFullName)"
Write-Host "  外部位置 : $publishDir"
Write-Host "  证书指纹 : $($certificate.Thumbprint)"
Write-Host "  卸载脚本 : packaging\uninstall_context_menu.ps1"
Write-Host "  调用日志 : $publishDir\logs\shell.log"
Write-Host ""
Write-Host "提示：在资源管理器中右键任意文件/文件夹，应能看到 “MD3 Zip” 折叠项。" -ForegroundColor Gray
Write-Host "排障：dotnet run --project packaging\tools\ComProbe\ComProbe.csproj -c Release -- --invoke-compress" -ForegroundColor Gray
