# MD3 Zip

Material Design 3 风格的 **7-Zip 图形外壳**（Windows 11 桌面端）。压缩、解压、校验全部委托随包分发的 7-Zip 23.01 引擎；界面基于 Avalonia 11 + MD3 语义色令牌；右键菜单是 Windows 11 现代折叠项；单文件安装包免运行时、零 UAC。

## 界面特性

- MD3 语义色令牌驱动的**深/浅双主题**，整套界面即时切换；中英双语即时切换（中文原文作 key）
- 主窗口 Mica 质感；全部窗口采用 **Windows 11 原生 8px 圆角**（与 DWM 裁剪重合，无黑角），内容卡片 8–12px 克制圆角
- 胶囊式分段格式选择、离散刻度压缩等级滑块、迷你解压悬浮卡片、MD3 密码闭环弹窗（验证→报错→重试）

## 功能清单

| 能力 | 说明 |
| --- | --- |
| 压缩 | 7z / zip / tar / gz / bz2 / xz；密码 AES-256、加密文件名（仅 7z）、分卷、压缩后删除源 |
| 解压 | 解压全部 / 选中 / 到当前文件夹 / 智能解压（自动判定套娃结构）；覆盖策略引擎层支持 |
| 浏览 | 压缩包内容浏览、面包屑导航、跨目录搜索、内置文本/图片预览 |
| 维护 | 测试归档完整性、向归档追加、删除条目 |
| 校验 | CRC-32 / CRC-64 / SHA-1 / SHA-256 / 全量，支持粘贴预期值比对（匹配绿 / 不匹配红） |
| 右键菜单 | Win11 现代菜单三项：解压到当前 / 智能解压 / 添加到压缩包… |

## 下载安装

| 版本 | 附件 | SHA-256 |
| --- | --- | --- |
| v1.1.1 | [Releases](https://github.com/trusts3596-create/MD3Zip/releases/tag/v1.1.1) 页 `MD3Zip-Setup-x64.exe`（89 MB，x64） | `81355b09970c0cb377f4798b57c2220c10f02a065db7b0d78d5c9f2e95d5ad37` |

双击图形向导，或静默安装：`MD3Zip-Setup-x64.exe /S [/D="目标目录"]`。默认装到 `%LOCALAPPDATA%\Programs\MD3 Zip`，全程无需管理员。

## 构建

```powershell
dotnet build MD3Zip.sln -c Release                                  # Core / App / ShellExtension
dotnet build src\MD3Zip.Setup\MD3Zip.Setup.csproj -c Release       # 安装器不在 sln 内，需单独构建
powershell -ExecutionPolicy Bypass -File packaging\build_setup.ps1 # 生成安装包（publish→签名→打包→签名）
```

- 环境：.NET 8 SDK；**Avalonia 11.3.23 / Material.Avalonia 3.14.0 版本锁死**（3.15+ 要求 Avalonia 12，勿升级）
- 修剪约束：`MD3Zip.App` 与全部 Avalonia/Material 程序集必须保留在 `TrimmerRootAssembly`，否则 `{Binding}` 静默失效导致 UI 重叠
- 冒烟测试：`dotnet run --project packaging\tools\CoreSmoke -c Release`（11 项应全 PASS）

## Win11 右键菜单注册

以**管理员身份** PowerShell 执行（首次需联网拉取 SDK BuildTools）：

```powershell
cd packaging
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\install_context_menu.ps1        # 证书 → 稀疏包打包 → 签名 → 注册 → 重启资源管理器
```

验证：右键任意文件/文件夹/空白处，一级菜单出现「MD3 Zip」折叠项。卸载：`.\uninstall_context_menu.ps1`。

排障速查：

| 现象 | 处置 |
| --- | --- |
| 菜单不出现 | 重启 `explorer.exe`；跑 `dotnet run --project packaging\tools\ComProbe -c Release` 诊断 COM |
| 点击无反应 | 看 `packaging\publish\app\logs\shell.log` 的 `COM 环境` 行（`主程序存在=False` 即路径问题） |
| 弹出旧 UI | `publish\app` 是右键实际执行副本，发版后必须重新 publish（framework-dependent 需 `-p:PublishTrimmed=false`）并重签 5 个 PE |
| 证书不受信任 | 确认 `MD3ZipDev.cer` 已导入 `LocalMachine\Root` 与 `TrustedPeople` |

## 目录结构

```
src\MD3Zip.Core\           引擎封装 + 中英词典（Localization\en-US.json）
src\MD3Zip.App\            Avalonia 主程序（CLI 路由四种启动模式）
src\MD3Zip.ShellExtension\ Win11 IExplorerCommand COM 扩展
src\MD3Zip.Setup\          WinForms 单文件安装器
packaging\                 稀疏包清单 / 出包与右键脚本 / 诊断工具
Assets\7z\                 随包分发的 7-Zip 23.01 x64 引擎
samples\                   验收用示例压缩包
```

## 已知限制

自签名证书仅本机有效（换机需重跑注册脚本）；右键菜单文本固定中文；主题选择不持久化；仅支持 Windows x64；解压覆盖策略 UI 固定「覆盖」。

## 许可

项目代码 MIT；随包分发 7-Zip 23.01（Igor Pavlov），unRAR 等上游声明见 [LICENSE](LICENSE)。
