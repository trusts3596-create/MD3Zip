# MD3 Zip

Material Design 3 风格的 7-Zip 外壳，支持 Windows 11 现代右键菜单。界面支持中 / 英双语。
压缩解压全部交给内置 7-Zip 引擎（23.01），本项目不含任何压缩算法实现。

| | |
| --- | --- |
| 平台 | Windows 10 / 11（右键菜单为 Windows 11）x64 |
| 技术栈 | .NET 8 · Avalonia 11 · Material.Avalonia 3.14 |
| 引擎 | 内置 7-Zip 23.01（`Assets/7z`，`7z.exe` 调用） |
| 许可 | LGPL-2.1+，第三方条款见 [`LICENSE`](LICENSE) |

---

## 1. 安装

### 1.1 安装包（推荐）

`packaging/setup/MD3Zip-Setup-x64.exe`（约 89 MB，自包含 .NET 8，目标机无需预装运行时）

| 操作 | 方式 |
| --- | --- |
| 图形安装 | 双击安装包 → 选目录与组件 → 安装 |
| 静默安装 | `MD3Zip-Setup-x64.exe /S` 或加 `/D="D:\指定目录"` |
| 卸载 | 设置 → 应用 → MD3 Zip → 卸载（或安装目录 `uninstall.ps1`） |

默认装到 `%LOCALAPPDATA%\Programs\MD3 Zip`（当前用户，零 UAC）。可选组件「注册 Windows 11 右键菜单」会在勾选时单独弹一次管理员授权。

### 1.2 右键菜单（可选组件）

安装时勾选即可；事后补装在安装目录运行 `一键安装右键菜单.cmd`（管理员）。

原理：MSIX 稀疏包 + `IExplorerCommand` COM 扩展，由 dllhost 承载，崩溃不影响资源管理器。
仅 Windows 11 生效，出现在「显示更多选项」或现代菜单中，含三项：

> MD3 Zip → 解压到当前文件夹 / 智能解压 / 添加到压缩包…

卸载右键菜单：安装目录 `uninstall.ps1`，或 `Remove-AppxPackage MD3Zip.Shell`。

### 1.3 从源码构建

前置：.NET 8 SDK、Windows SDK（用于 MakeAppx/SignTool，右键菜单与安装包才需要）。

```powershell
# 编译
dotnet build MD3Zip.sln -c Release

# 直接运行（不注册右键菜单也可完整使用）
.\src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe [归档文件]

# 构建安装包（发布自包含负载 → 压缩 → 编译安装器 → 签名）
powershell -ExecutionPolicy Bypass -File packaging\build_setup.ps1
#   -SkipPublish 复用已有负载    -NoSign 跳过签名

# 注册右键菜单（管理员；首次会生成自签证书）
powershell -ExecutionPolicy Bypass -File packaging\install_context_menu.ps1
```

命令行入口：直接传归档路径浏览；`--extract-here` 解压到当前文件夹；`--smart-extract` 智能解压；
`--compress-dialog <文件...>` 打开压缩对话框；`--password <pwd>` 解密加密包。

---

## 2. 修改项指南

想改界面、行为或打包？改动集中在这几处。

### 2.1 界面与文案

| 想改什么 | 改哪里 | 注意 |
| --- | --- | --- |
| 按钮/标题等界面文字 | 对应 `Views/*.axaml` 里的 `{loc:T 中文}` | 中文原文即 key，改中文需同步改词典 |
| 英文翻译 | `src/MD3Zip.Core/Localization/en-US.json` | key=中文原文；缺失自动回退中文 |
| 新增一段可翻译文案 | XAML 用 `{loc:T 新文案}`，C# 用 `L10n.L("新文案")` | 同步往 `en-US.json` 加英译 |
| 主题色 / 深浅色 | `src/MD3Zip.App/Themes/Tokens.axaml` | MD3 语义令牌集中在这里 |
| 图标 | `src/MD3Zip.App/Themes/Icons.axaml` | 内联 StreamGeometry |
| 窗口尺寸/最小尺寸 | `Views/MainWindow.axaml` 顶部属性 | 当前 960×640，最小 680×480 |
| 默认语言 | 启动时读 `%LOCALAPPDATA%\MD3Zip\settings.json` | 也可从「更多」菜单切换，即时生效 |
| 翻译批量提取/替换 | `scripts/extract_strings.py` / `apply_translation.py` | 改版后重跑即可更新词典 |

### 2.2 功能行为

| 想改什么 | 改哪里 | 说明 |
| --- | --- | --- |
| 7z 参数 / 进度解析 | `src/MD3Zip.Core/SevenZipService.cs` | 全部命令行调用集中于此 |
| 压缩格式与校验 | `src/MD3Zip.Core/Models/Requests.cs` | 新增格式需在此扩展枚举 |
| 解压覆盖策略 | `ExtractionRequest.OverwriteMode` | `-aoa/-aos/-aou` 三态，UI 默认覆盖 |
| 主窗口命令 | `ViewModels/MainViewModel.cs` | 「更多」菜单项对应各 Command |
| 压缩对话框选项 | `ViewModels/CompressViewModel.cs` + `Views/CompressWindow.axaml` | 格式/密码/分卷/删源 |
| 密码弹窗行为 | `Views/PasswordWindow.axaml.cs` | 含验证→报错→重试闭环 |
| 右键菜单三项动作 | `src/MD3Zip.ShellExtension/ExplorerCommand.cs` | 解压/智能解压/添加到压缩包 |
| 菜单项注册范围 | `packaging/AppxManifest.xml` | 仅 `*`/`Directory`/`Directory\Background`，**勿重复注册后缀** |
| 升级 7-Zip 引擎 | 替换 `Assets/7z/7z.exe + 7z.dll` | 不依赖具体版本，替换即生效 |

### 2.3 打包与安装器

| 想改什么 | 改哪里 | 说明 |
| --- | --- | --- |
| 安装包构建流程 | `packaging/build_setup.ps1` | 发布→补资源→签名→压缩→编译安装器 |
| 安装向导界面 | `src/MD3Zip.Setup/SetupForm.cs` | 布局用 `TableLayoutPanel`，勿用绝对坐标 |
| 安装/卸载逻辑 | `src/MD3Zip.Setup/Installer.cs` | 当前用户安装，写 HKCU 卸载项 |
| 自动化修剪 | `src/MD3Zip.App/MD3Zip.App.csproj` 的 `TrimmerRootAssembly` | **必须含 `MD3Zip.App` 与 Avalonia 全家**，否则绑定静默失败 |
| 签名证书 | `packaging/install_context_menu.ps1` 首次运行自动生成 | 私钥不入库（见 `.gitignore`） |

---

## 3. 项目结构

```
MD3Zip.sln
src/
├── MD3Zip.Core/             7z 引擎封装：压缩/解压/列表/测试/删除/追加/校验和、
│                            密码探测、分卷、覆盖策略；Localization/ 中英词典
├── MD3Zip.App/              Avalonia 主界面：浏览器、智能解压、迷你卡片、
│                            压缩/密码对话框、预览、深浅色、双语
├── MD3Zip.ShellExtension/   Windows 11 右键菜单（IExplorerCommand COM）
└── MD3Zip.Setup/            WinForms 单文件安装器
packaging/
├── AppxManifest.xml         稀疏包清单（右键菜单注册）
├── build_setup.ps1          构建安装包
├── install_context_menu.ps1 部署右键菜单（证书/打包/签名/注册）
├── uninstall_context_menu.ps1
├── 一键安装右键菜单.cmd      提权启动器（GBK+CRLF）
└── tools/ComProbe|CoreSmoke COM 全链路诊断 / Core 冒烟测试
scripts/                     i18n 提取与批量替换脚本
Assets/7z/                   内置 7-Zip 23.01 引擎（含 License.txt）
samples/                     演示压缩包（encrypted-demo.7z 密码 1234）
```

---

## 4. 与 7-Zip 的功能对照

| 7-Zip 能力 | 本项目 | 入口 |
| --- | --- | --- |
| 打开/浏览归档、目录导航、搜索 | ✅ | 主窗口 |
| 多格式解压（7z/zip/rar/tar/gz/bz2/xz/iso…） | ✅ | 7z 引擎自动识别 |
| 解压全部 / 解压选中 / 解压到指定目录 | ✅ | 工具栏 + 勾选 |
| 压缩 7z/zip/tar/gz/bz2/xz | ✅ | 压缩对话框 |
| 压缩级别 1–9、密码 AES-256、加密文件名、分卷 | ✅ | 高级选项 |
| 压缩后删除源文件 | ✅ | 高级选项 |
| 测试完整性（`7z t`） | ✅ | 更多 → 测试压缩包完整性 |
| 追加（`7z u`）/ 删除条目（`7z d`） | ✅ | 更多 → 添加/删除 |
| 校验和 CRC32/SHA-1/SHA-256（`7z h`） | ✅ | 更多 → 计算校验和 |
| 密码探测与「验证→报错→重试」 | ✅ | 加密包自动弹窗 |
| Win11 现代右键菜单 | ✅ | 稀疏包 + COM |
| 中英双语 | ✅ | 更多 → English/中文 |
| 文件类型关联 / 归档内重命名 / 邮件压缩 / 基准测试 | ❌ | 非核心或 7-Zip 本身不支持 |

---

## 5. 排错

| 现象 | 处理 |
| --- | --- |
| 右键菜单不出现 | 重启 `explorer.exe` 或注销重登；运行 `一键安装右键菜单.cmd` 重新注册 |
| 菜单出现但点了没反应 | 跑 `packaging\tools\ComProbe`（`-- --invoke-compress` 可模拟点击），看 `publish\app\logs\shell.log` |
| 出现两个「MD3 Zip」菜单 | 清单重复注册了归档后缀 ItemType，只保留 `*`/`Directory`/`Directory\Background` 三个 |
| `Add-AppxPackage` 报证书不受信任 | 执行 `install_context_menu.ps1` 重新生成并导入证书 |
| 报 `0x80073CF3` 依赖缺失 | 安装 .NET 8 Desktop Runtime 后重试 |
| 界面文字全空白 / 空态重叠 | 修剪配置问题：`MD3Zip.App` 与 Avalonia 必须在 `TrimmerRootAssembly` 中 |
| 提示找不到 7z.exe | 确认 `Assets/7z/7z.exe + 7z.dll` 存在，或安装 7-Zip 后重启程序 |

---

## 6. 许可与第三方声明

本项目按 **LGPL-2.1+** 发布（详见 [`LICENSE`](LICENSE)）。

`Assets/7z/` 分发 7-Zip 23.01 二进制，**遵从上游守则**：
- 7-Zip Copyright (C) 1999-2023 Igor Pavlov，主许可 GNU LGPL；
- `7z.dll` 内含 Apple LZFSE 代码（BSD 3-clause）与 unRAR 代码（附带限制）；
- 二进制分发须随附 `Assets/7z/License.txt`（已随仓库分发）；
- **unRAR 声明（上游要求保留）**：RAR 解压引擎源自 unRAR，版权归 Alexander Roshal。
  不得使用该代码重新实现专有的 RAR 压缩算法；本项目仅通过 7-Zip 提供 RAR 解压，
  不实现也不允许据本项目代码开发 RAR（WinRAR）兼容的归档工具。

其余依赖（Avalonia、Material.Avalonia、.NET 8）为 MIT，构建时经 NuGet 拉取。
