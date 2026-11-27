# =====================================================================
# MD3 Zip · 右键菜单卸载与清理脚本
# ---------------------------------------------------------------------
# 作用：
#   1. 卸载 MD3Zip.Shell 稀疏包（右键菜单随之消失）
#   2. 删除本次安装导入的本地自签名证书（根证书 / TrustedPeople / 个人）
#   3. 删除 packaging\certs 下的 pfx/cer 文件
#   4. 重启资源管理器
#
# 可选参数：
#   -KeepCertificate  保留证书（方便稍后重新安装）
#   -NoRestart        不重启资源管理器
# =====================================================================
[CmdletBinding()]
param(
    [switch]$KeepCertificate,
    [switch]$NoRestart,
    [string]$Publisher = "CN=MD3Zip Dev"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "需要管理员权限，正在请求提权…" -ForegroundColor Yellow
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs
    return
}

$packagingDir = $PSScriptRoot
$certDir = Join-Path $packagingDir "certs"
$packageName = "MD3Zip.Shell"

Write-Host "MD3 Zip 右键菜单卸载" -ForegroundColor Magenta

# ---------------------------------------------------------------------
# 1. 卸载稀疏包
# ---------------------------------------------------------------------
$packages = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if ($packages) {
    foreach ($package in $packages) {
        Write-Host "移除包: $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName
    }
}
else {
    Write-Host "未发现已安装的 $packageName 包。" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------
# 2. 清理证书
# ---------------------------------------------------------------------
if (-not $KeepCertificate) {
    $thumbprints = @()
    $thumbprintFile = Join-Path $certDir "thumbprint.txt"
    if (Test-Path $thumbprintFile) {
        $thumbprints += (Get-Content $thumbprintFile | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_.Trim() })
    }

    # 兜底：按主题名匹配
    foreach ($store in @("Cert:\LocalMachine\Root", "Cert:\LocalMachine\TrustedPeople", "Cert:\CurrentUser\My")) {
        $matched = Get-ChildItem $store -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq $Publisher }
        foreach ($certificate in $matched) {
            if ($thumbprints -notcontains $certificate.Thumbprint) {
                $thumbprints += $certificate.Thumbprint
            }
        }
    }

    foreach ($thumbprint in ($thumbprints | Select-Object -Unique)) {
        foreach ($store in @("Cert:\LocalMachine\Root", "Cert:\LocalMachine\TrustedPeople", "Cert:\CurrentUser\My")) {
            $path = Join-Path $store $thumbprint
            if (Test-Path $path) {
                Write-Host "删除证书 $thumbprint ($store)"
                Remove-Item $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (Test-Path $certDir) {
        Remove-Item $certDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除 $certDir"
    }
}
else {
    Write-Host "按要求保留证书。" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------
# 3. 刷新资源管理器
# ---------------------------------------------------------------------
if (-not $NoRestart) {
    Write-Host "重启资源管理器…"
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host ""
Write-Host "卸载完成。" -ForegroundColor Green
Write-Host "发布目录（含主程序与 7z 引擎）未被删除，可手动删除 packaging\publish。" -ForegroundColor Gray
