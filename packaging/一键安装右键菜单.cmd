@echo off
setlocal
set "DIR=%~dp0"
if /i "%~1"=="dry" goto :dry
echo.
echo   MD3 Zip - 安装 Windows 11 右键菜单
echo   ------------------------------------------------
echo   即将请求管理员权限：注册稀疏包、导入本地证书、签名并重启资源管理器。
echo   请在弹出的 UAC 窗口中点击“是”。
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','%DIR%run_install.ps1'"
if errorlevel 1 (echo 提权被取消，安装未执行。) else (echo 已在管理员窗口中开始安装，日志: %DIR%install.log)
timeout /t 3 >nul
exit /b 0

:dry
echo 解析正常。
echo 提权命令: powershell -NoProfile -ExecutionPolicy Bypass -File %DIR%run_install.ps1
exit /b 0
