# 提权后的安装包装脚本：调用主部署脚本并把所有输出写入 install.log
# 由 install_context_menu.ps1 的自动化入口使用（也可手动以管理员身份运行）。
$logPath = Join-Path $PSScriptRoot "install.log"
"==== MD3 Zip 安装开始 $(Get-Date -Format o) ====" | Out-File -FilePath $logPath -Encoding UTF8

try {
    & (Join-Path $PSScriptRoot "install_context_menu.ps1") *>> $logPath
    $code = $LASTEXITCODE
}
catch {
    $_ | Out-String | Out-File -FilePath $logPath -Append -Encoding UTF8
    $code = 1
}

"==== 退出码: $code ====" | Out-File -FilePath $logPath -Append -Encoding UTF8
