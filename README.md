# MD3 Zip · 项目验收与测试指南

> Material Design 3 风格的 7-Zip 外壳：Avalonia 11 桌面端 + 托管 `IExplorerCommand` +
> Windows 11 稀疏包（Sparse MSIX）右键菜单。压缩/解压全部委托给随包分发的 `7z.exe`。

---

## 1. 目录结构与编译产物

```
D:\wb
├── MD3Zip.sln                        解决方案（3 个项目）
├── Assets\7z\                        随包分发的 7-Zip 引擎
│   ├── 7z.exe                        7-Zip 23.01 (x64) 控制台
│   ├── 7z.dll
│   └── License.txt
├── samples\演示 归档.7z              验收用示例压缩包（4 文件 / 3 目录）
├── src
│   ├── MD3Zip.Core\                  业务核心：模型 + 7z 进程调度引擎
│   │   ├── Models\
│   │   │   ├── ArchiveEntry.cs       条目模型 + 路径规范化（阻断 .. 逃逸）
│   │   │   ├── ArchiveListing.cs     `7z l -slt` 解析 + 目录树索引 + 面包屑
│   │   │   ├── ArchiveProgress.cs    进度快照 + SevenZipStatus 语义枚举
│   │   │   └── Requests.cs           压缩/解压请求模型
│   │   ├── SevenZipLocator.cs        定位 7z.exe（随包 → 系统安装 → PATH），缺失只提示不复制
│   │   └── SevenZipService.cs        异步进程调度 / 进度解析 / 进程树终止 / 错误映射
│   ├── MD3Zip.App\                   Avalonia 11 MD3 桌面端（多窗口 + CLI 路由）
│   │   ├── Themes\Tokens.axaml       MD3 语义色令牌（Light/Dark 双主题字典）
│   │   ├── Themes\Controls.axaml     按钮 / TextBox / CheckBox / Slider / 进度条 / FAB 样式
│   │   ├── Themes\Icons.axaml        内联矢量图标（无额外图标包依赖）
│   │   ├── Views\                    MainWindow / CompressWindow / ExtractCardWindow
│   │   │                             PreviewWindow / PasswordWindow / MessageWindow
│   │   ├── ViewModels\               主窗口、压缩配置、迷你卡片、预览
│   │   ├── Services\                 主题管理 / 系统外壳调用 / 文件类型 / 对话框契约
│   │   ├── CliOptions.cs             CLI 参数解析（4 种启动模式）
│   │   └── Program.cs                入口（无参数 → 主窗口）
│   └── MD3Zip.ShellExtension\        Win11 COM `IExplorerCommand` 扩展
│       ├── Interop.cs                COM 互操作声明（严格按 vtable 顺序）
│       └── ExplorerCommand.cs        一级折叠菜单 + 3 个子命令 + 枚举器 + 启动器
└── packaging\
    ├── AppxManifest.xml              稀疏包清单（AllowExternalContent + COM + 右键菜单）
    ├── Assets\                       Logo44/150/300/620、StoreLogo
    ├── install_context_menu.ps1      一键部署（证书 / 打包 / 签名 / 注册）
    ├── uninstall_context_menu.ps1    一键卸载与证书清理
    └── tools\ComProbe\               右键菜单 COM 全链路诊断工具（独立 csproj）
```

### 核心编译产物

| 产物 | 路径 |
| --- | --- |
| 主程序 | `src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe` |
| 主程序输出目录（含 Avalonia 依赖与 `Assets\7z\`） | `src\MD3Zip.App\bin\Release\net8.0\` |
| 核心库 | `src\MD3Zip.App\bin\Release\net8.0\MD3Zip.Core.dll` |
| COM 宿主 | `src\MD3Zip.ShellExtension\bin\Release\net8.0-windows\win-x64\MD3Zip.ShellExtension.comhost.dll` |

### 重新编译

本机原先没有 .NET SDK，已在工作区内置 8.0.428（`D:\wb\.tools\dotnet`）。
若你已自行安装 SDK，把下面的路径换成 `dotnet` 即可。

```powershell
D:\wb\.tools\dotnet\dotnet.exe build D:\wb\MD3Zip.sln -c Release
```

当前状态：**3 个项目全部成功，0 警告 0 错误**。

---

## 2. 不注册右键菜单的测试指令

所有命令都在 `D:\wb` 下执行（路径含空格请保留引号）。

### 模式 1：完整压缩包浏览器

```powershell
src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe "D:\wb\samples\演示 归档.7z"
```

可验证：

1. 面包屑 `根目录 > 文档` 逐级导航，左上箭头返回上级；
2. 双击文件夹进入、双击文件弹出预览（文本/图片内置预览，其他类型可“用默认程序打开”）；
3. 勾选若干条目 → 顶部出现“解压选中项”，仅解压选中内容；
4. 搜索框实时过滤（文件名，跨目录）；
5. 从资源管理器拖文件/文件夹到窗口：单个压缩包 → 打开；其他 → 直接进入压缩配置；
6. 右上角月亮图标切换深/浅色，所有颜色来自 MD3 令牌（无硬编码黑白灰）；
7. 右下角 Extended FAB “新建压缩”；
8. 顶部“解压全部”选择目标目录后解压，底部显示 Linear 进度与取消按钮。

### 模式 2：解压到当前文件夹（迷你悬浮卡片）

```powershell
src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe --extract-here "D:\wb\samples\演示 归档.7z"
```

- 不加载主窗口，直接从屏幕右下角弹出无边框卡片；
- 显示文件名、Linear 进度（`-bsp1` 实时百分比 + 当前文件）、取消按钮；
- 完成后变为对勾 + “解压完成”，**1.5 秒后自动退出**。

> 演示包根目录含 3 个条目，`--extract-here` 会按原始结构直接解压到压缩包所在目录。

### 模式 3：智能解压

```powershell
src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe --smart-extract "D:\wb\samples\演示 归档.7z"
```

决策规则（可在卡片底部文案里看到判定原因）：

- **情况 A**：根目录只有唯一一个文件夹 → 直接解压到当前目录（避免 `a\a\...` 套娃）；
- **情况 B**：根目录有多个散乱条目 → 自动新建“压缩包同名文件夹”再解压。

推荐用两个包对比：`samples\演示 归档.7z`（情况 B），以及自己压一个“只有一个根文件夹”的 7z（情况 A）。

### 模式 4：压缩配置对话框

```powershell
src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe --compress-dialog "D:\wb\samples\演示 归档.7z" "D:\wb\Assets\7z\License.txt"
```

可验证：

- 归档格式 **MD3 分段按钮**（7z / zip / tar），切换后输出扩展名同步；
- 压缩等级 **MD3 滑块**（`-mx1 / -mx3 / -mx5 / -mx7 / -mx9`，吸附到 5 档）；
- “高级选项”折叠面板：密码 + 确认密码、加密文件名（`-mhe=on`）、分卷（`100m/500m/1g/自定义`）；
- 点击“开始压缩”→ 平滑切换为进度视图 → 完成后自动关闭窗口；
- 默认输出目录 = 源文件所在目录（右键场景不再落到桌面）。

### 加密压缩包（密码输入闭环）

```
src\MD3Zip.App\bin\Release\net8.0\MD3Zip.App.exe "D://wb//samples//encrypted-demo.7z"
```

示例包密码是 **1234**。预期行为：

1. 立刻弹出 MD3 圆角密码框（锁图标、提示文案、右侧眼睛图标可切换明文/密文）；
2. 输错密码 → **弹窗不关闭**，输入框下方出现红色「密码错误，请重试。」，可继续输入；
3. 输入正确 → 弹窗关闭、归档打开，概要行出现「· 加密」标记；
4. 迷你模式同样生效：`--extract-here` / `--smart-extract` 遇到加密包会先弹密码框，验证通过后继续解压并在完成后自动退出。

### 其他可选参数

| 参数 | 说明 |
| --- | --- |
| `--password <pwd>` | 预置密码，命中则直接解锁；密码错误时仍会弹出密码框要求重试 |
| 直接传单个压缩包路径 | 等同模式 1，启动即打开 |

---

## 3. Windows 11 右键菜单的部署与验收

> 当前状态：**已在本机完成部署并端到端实测通过**。包
> `MD3Zip.Shell_1.0.0.0_x64__ps1av0evqpwje` 已注册（`Status=Ok`），
> `ExternalLocation = D:\wb\packaging\publish\app`，证书 `CN=MD3Zip Dev` 已在本地受信任根。
> 实测：COM 激活 `S_OK` → 3 个子命令枚举 → 点击「添加到压缩包…」→ 弹出「新建压缩包」窗口。
> 本节既是复现步骤，也是换机/重装时的操作手册；重新执行需**管理员身份**（导入证书要提权）。

### 前置条件

- Windows 11 Build 22000+（现代右键菜单）；
- 已安装 .NET 8 **Desktop Runtime**（或把发布改成 self-contained，见第 5 节）；
- 首次运行需要网络：脚本会自动通过 NuGet 拉取 `Microsoft.Windows.SDK.BuildTools`（内含 `makeappx.exe` / `signtool.exe`）。

### 步骤

1. 关闭所有 `MD3Zip.App.exe` 进程（避免文件被占用导致发布失败）；
2. 以**管理员身份**打开 Windows PowerShell；
3. 执行：

```powershell
cd D:\wb\packaging
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\install_context_menu.ps1
```

脚本会依次完成（每步都有中文输出）：

1. `dotnet publish` 主程序到 `packaging\publish\app`（稀疏包的 ExternalLocation），并编译 Shell 扩展、拷贝
   `.dll / .comhost.dll / .deps.json / .runtimeconfig.json`；
2. `New-SelfSignedCertificate` 生成代码签名证书 `CN=MD3Zip Dev`，导出到 `packaging\certs\MD3ZipDev.cer / .pfx`；
3. 把 `.cer` 导入 `Cert:\LocalMachine\Root` 与 `TrustedPeople`（包签名校验需要）；
4. `MakeAppx pack /nv` 生成稀疏包 `packaging\publish\MD3Zip.Shell.msix`
   （exe 与 COM DLL 都在包外，`/nv` 是稀疏包的必需参数，不再先试严格校验）；
5. `SignTool sign` 签名 **ExternalLocation 内所有 `MD3Zip.*.exe/.dll`** 与稀疏包；
   脚本随后逐一自检签名，任何未签名文件都会直接报错（带 DigiCert 时间戳，失败自动降级为不带时间戳）；
6. `Add-AppxPackage -Path ...msix -ExternalLocation <发布目录>` 完成注册；
7. 重启资源管理器使菜单立即出现。

4. 验证：在资源管理器里右键**任意文件 / 文件夹 / 文件夹空白处**，一级菜单出现带图标的
   **“MD3 Zip”** 折叠项，悬停展开：

   - 解压到当前文件夹 → `MD3Zip.App.exe --extract-here "<文件>"`
   - 智能解压 → `MD3Zip.App.exe --smart-extract "<文件>"`
   - 添加到压缩包… → `MD3Zip.App.exe --compress-dialog "<文件1>" "<文件2>" ...`

5. 卸载：

```powershell
cd D:\wb\packaging
.\uninstall_context_menu.ps1
```

（会移除 MSIX 包、删除导入的证书与 `certs` 目录、重启资源管理器；加 `-KeepCertificate` 可保留证书。）

### 3.1 一键诊断（先跑这个，不需要重启 Explorer）

```powershell
cd D:\wb
.\.tools\dotnet\dotnet.exe run --project packaging\tools\ComProbe\ComProbe.csproj -c Release -- --invoke-compress
```

`ComProbe` 不依赖资源管理器，直接从 COM 运行时激活 CLSID，依次验证
`GetTitle / GetIcon / GetFlags / GetState / EnumSubCommands::Next / Invoke`，
最后检查 `MD3Zip.App.exe` 进程与调用日志。正常输出应包含：
`CoCreateInstance = 0x00000000`、`fetched=3`（3 个子命令）、`Invoke = 0x00000000`、进程数 ≥ 1。
带 `--invoke-compress` 时会真实弹出一次「新建压缩包」窗口用于确认 GUI 正常；
带 `--invoke-root` 则模拟「直接点一级菜单」，验证兜底动作是否可用。

### 3.2 调用日志位置与两轮「点击无反应」根因

主日志固定在包外真实路径 **`packaging\publish\app\logs\shell.log`**
（UTF-8，用 `Get-Content -Encoding UTF8` 读）。

> 关键坑：COM 服务器由 `dllhost.exe` 在 **MSIX 包上下文**中承载，`%LOCALAPPDATA%` 会被
> 虚拟化重定向到
> `...\AppData\Local\Packages\MD3Zip.Shell_ps1av0evqpwje\LocalCache\Local\MD3Zip\shell.log`。
> 早期版本只写 LocalAppData，导致在常规路径下永远找不到日志，一度被误判成「COM 从未激活」。

日志首行 `COM 环境` 会打印 `BaseDirectory`、`AppContext.BaseDirectory` 与 `主程序存在`。

**第一轮根因（已修）**：`AppContext.BaseDirectory` 在 dllhost 下为**空**，旧代码用它拼
`MD3Zip.App.exe` 路径必然找不到主程序并静默返回失败。现已改为
`typeof(AppLauncher).Assembly.Location` 所在目录，实测
`BaseDirectory=D:\wb\packaging\publish\app`、`主程序存在=True`。

**第二轮根因（已修）**：`IExplorerCommand::GetFlags()` 返回 **`EXPCMDFLAGS`**，
与 `GetState()` 的 **`EXPCMDSTATE`** 是两套不同枚举。旧代码把两者混用，还自造了
`ECS_HASSUBCOMMANDS = 0x4`——SDK 中 `ECF_HASSUBCOMMANDS = 0x1`，而 `0x4` 是
`ECF_HIDELABEL`（在另一套枚举里则是 `ECS_CHECKBOX`）。结果 Explorer 把「MD3 Zip」
渲染成**没有子菜单的普通命令**，点击只调用一级 `Invoke`（旧实现为空操作）。
日志证据：23:10–23:13 连续 5 次「点击一级菜单」而没有任何子命令 `Invoke`。

修复（两层，互为保险）：

1. `GetFlags` 返回 `ECF_HASSUBCOMMANDS = 0x1`，Explorer 才会展开折叠项；常量在
   `Interop.cs` 中拆成 `ExplorerCommandFlags`（EXPCMDFLAGS）与
   `ExplorerCommandState`（EXPCMDSTATE），避免再次串用。
2. 一级 `Invoke` 增加**兜底动作**：直接打开压缩对话框。正常展开子菜单时不会触发
   `Invoke`，不影响常规路径；但万一某些 Explorer 版本仍不展开，点一级项也能打开窗口。

> 决策取舍：备选方案是「把 3 个动作平铺成 3 个一级菜单项」，最直接但要改
> `AppxManifest.xml`、重新打包签名并以管理员重新注册包（改动大、不可逆成本高、
> 菜单变长）。当前方案只改 COM 实现、不动包身份，是更可逆的选择；
> 代价是仍依赖 Explorer 正确实现 `ECF_HASSUBCOMMANDS` 的展开行为，故加了兜底。

### 3.3 排错清单

| 现象 | 处理 |
| --- | --- |
| 一级菜单不出现 | 重启 `explorer.exe`；仍不行则注销/重新登录；先跑 3.1 确认 COM 是否可激活 |
| 菜单出现但点击无反应 | 查 `logs\shell.log` 的 `COM 环境` 行；`主程序存在=False` 即路径定位问题 |
| 只有「MD3 Zip」一项、没有子菜单 | 跑 `ComProbe` 看 `GetFlags` 是否为 `0x1`；改过 DLL 后必须**重启 `explorer.exe` 并结束旧 `dllhost`** 才会重建菜单 |
| `CoCreateInstance = 0x80040154` | 包未注册/被移除，重跑 `install_context_menu.ps1` |
| `CoCreateInstance = 0x80070005` | 签名/权限问题：确认 `.cer` 在 `LocalMachine\Root`、外部 PE 全部已签名 |
| `Add-AppxPackage` 报证书不受信任 | 确认 `.cer` 已导入 `LocalMachine\Root` 与 `TrustedPeople` |
| 报 `0x80073CF3` 依赖缺失 | 安装 .NET 8 Desktop Runtime 后重试 |
| 改完扩展代码后菜单失效 | **不要只复制 DLL 覆盖**——复制会抹掉签名。重跑一键脚本（含签名自检） |

只重新编译 ShellExtension 时包清单没变，**不必重新 `Add-AppxPackage`**：
复制新 DLL → 重新签名 → 结束加载旧 DLL 的 `dllhost.exe`，下次 COM 激活即加载新版本。

---

## 4. 实现要点（便于验收对照）

| 需求点 | 实现位置 |
| --- | --- |
| UTF-8 中文路径（`-sccUTF-8`） | `SevenZipService.RunAsync` 统一注入 |
| 实时进度（`-bsp1` + 正则） | `PumpAsync` 逐字符缓冲（处理 `\b` 退格与 `\r\n`），`TryParseProgress` 取“最后一个百分比” |
| 安全取消 | `CancellationToken` → `process.Kill(entireProcessTree: true)`，退出码 255 映射为 `Cancelled` |
| 错误语义化 | `Classify`：密码错误 / 磁盘不足 / 文件占用 / 找不到 / 非归档 |
| 密码探测 | 无密码时**强制传 `-p-`**，7z 立刻以退出码 2 失败而不会在控制台等密码；`Enter password` / `Wrong password` / `Can not open encrypted archive` 统一按「是否已提供密码」拆成 `PasswordRequired` 与 `WrongPassword` |
| 密码弹窗 | `PasswordWindow` 接收 `validator(password)`，非成功状态时窗口保持打开并显示红色错误提示，形成「验证→报错→重试」闭环；同时兜底 `RedirectStandardInput + Close` 防止任何交互式挂起 |
| 清单解析 | `ArchiveListing.Parse` 按 `----------` 切块、空行分记录，并补全未显式记录的中间目录 |
| 智能解压 | `SmartExtractAsync` 先列目录再决定 A/B，返回可展示的判定原因 |
| MD3 令牌 | `Themes/Tokens.axaml`：`Md3.Primary / OnSurface / SurfaceContainerHigh …`，Light+Dark 双字典 |
| 窗口质感 | 主窗口与压缩弹窗均用 `ExtendClientAreaToDecorationsHint` + `ExtendClientAreaChromeHints="NoChrome"`（保留系统缩放边框，去掉系统标题栏）；主窗口再叠加 `TransparencyLevelHint="Mica, AcrylicBlur, Blur"` + `Md3.MicaLayer` |
| 尺寸与缩放 | 主窗口 `CanResize` 默认 960×640、最小 680×480；压缩弹窗 620×720、最小 460×420。两窗口都由系统提供八向边缘/四角缩放（实测边缘拖拽 1100→913 DIP、角落拖拽并受最小尺寸钳制） |
| 自适应布局 | 所有行高用 `*` / `Auto`：文件列表随窗口拉伸、文件名列 `*` 吃掉剩余宽度，底部状态栏与右下角 Extended FAB 固定贴边；列表底部预留 76px，避免 FAB 压住最后一行与勾选框 |
| 自绘窗口按钮 | `Button.caption`：46×32 固定容器，内部 10×10 / `StrokeThickness=1.2` 描边图标（最小化横线、最大化方框、还原双层方框、关闭叉），圆角 8 + 2px 内缩悬浮层，关闭悬停 `#E81123` 且图标转白，背景色 140ms 过渡 |
| 标题栏对齐 | 标题栏统一 40px 高，图标/标题、32×32 圆形主题按钮、三枚窗口按钮同一水平中线；拖拽热区判定 `e.Source` 是否落在 Button 内，避免吞掉按钮点击 |
| 圆角规范 | **Win11 原生标准**：外层窗口 8（与 DWM 圆角重合、无黑角），内容卡片 8–12、图标容器 10–12；按钮 20 / FAB 16 / 输入框 14 维持 MD3 组件形状 |
| 右键菜单 | `RootExplorerCommand.GetFlags` 返回 `ECF_HASSUBCOMMANDS=0x1`（`EXPCMDFLAGS`，**勿与 `EXPCMDSTATE` 混用**）；`EnumExplorerCommand` 通过 `Marshal.GetComInterfaceForObject` 返回 3 个动作的 CCW；一级 `Invoke` 另有兜底动作 |
| 稀疏包 | `uap10:AllowExternalContent` + `com:SurrogateServer`（dllhost 承载，崩溃不拖垮 Explorer） |

### 4.1 与 7-Zip 的功能对照

| 7-Zip 能力 | 本项目 | 入口 / 实现 |
| --- | --- | --- |
| 打开 / 浏览归档、目录面包屑、搜索 | ✅ | 主窗口 |
| 多格式解压（7z/zip/rar/tar/gz/bz2/xz/iso…） | ✅ | 7z 引擎自动识别 |
| 解压全部 / 解压选中条目 | ✅ | 工具栏 + 勾选 |
| 解压到指定目录 | ✅ | 解压前选择目标文件夹 |
| 覆盖策略（覆盖 / 跳过 / 重命名） | ✅ 引擎层 | `ExtractionOverwriteMode` → `-aoa/-aos/-aou`；UI 默认覆盖 |
| 拖入归档即浏览 | ✅ | 主窗口拖放 |
| 压缩为 7z / zip / tar | ✅ | 压缩对话框 |
| 压缩为 gz / bz2 / xz | ✅ **新增** | 单文件格式，自动校验并沿用源文件名 |
| 压缩级别 1–9 | ✅ | 滑块 + 档位说明 |
| 密码 + AES-256 | ✅ | 高级选项 |
| 加密文件名（`-mhe=on`） | ✅ | 仅 7z |
| 分卷（100MB/500MB/1GB/自定义） | ✅ | 高级选项 |
| 压缩后删除源文件 | ✅ **新增** | 高级选项 → 「压缩完成后删除源文件」 |
| 测试归档完整性 | ✅ **新增** | 更多 → 测试压缩包完整性（`7z t`） |
| 向现有归档追加文件 | ✅ **新增** | 更多 → 向压缩包添加文件（`7z u`） |
| 删除归档内条目 | ✅ **新增** | 勾选后 更多 → 删除选中条目（`7z d`） |
| 校验和 CRC32 / SHA-1 / SHA-256 | ✅ **新增** | 更多 → 计算校验和（`7z h`） |
| 密码探测与「验证→报错→重试」 | ✅ | 加密包自动弹窗 |
| Win11 现代右键菜单 | ✅ | 稀疏包 + `IExplorerCommand` |
| 文件类型关联 / 「打开方式」 | ❌ 未做 | 需 `uap:FileTypeAssociation`，非本期范围 |
| 更新模式细分（添加并替换/更新/刷新/同步） | ⚠️ 部分 | 使用 `7z u` 默认「添加并替换」 |
| 归档内重命名 | ❌ | 7-Zip 本身也不支持，需删除后重新添加 |
| 邮件压缩 / 基准测试 / 语言包 | ❌ 未做 | 非核心场景 |

> 取舍说明：覆盖策略在 Core 已支持三态，但 UI 目前固定「覆盖」，避免每次解压都多一次弹窗；
> 需要时可再加一个「解压设置」下拉。追加/删除会直接改写归档，删除前有确认框。

### 4.2 中英双语界面

- 以**中文原文作 key**，英文词典 `src\MD3Zip.Core\Localization\en-US.json`（279 条）映射；缺词条自动回退中文。
- XAML 用 `{loc:T 中文}` 绑定，C# 用 `L10n.L("中文")`；切换语言通过 `LocalizationService` 的 `Item[]` 通知**即时刷新**，无需重启。
- 入口：主窗口 → 更多 → 底部「English / 中文」；选择写入 `%LOCALAPPDATA%\MD3Zip\settings.json`。
- 首次启动按系统 UI 区域自动选择（中文系统 → 中文，其他 → 英文）；安装向导同样按系统区域显示。
- 覆盖范围：主窗口 / 压缩对话框 / 密码框 / 预览 / 消息框 / 迷你卡片 / Core 错误消息 / 安装向导。右键菜单文本在 MSIX 清单中固定为中文，需改清单并重新注册包才能换语言。

### 4.3 Windows 安装包（单文件 exe）

**产物**：`packaging\setup\MD3Zip-Setup-x64.exe` —— 89.0 MB，x64，自签名（`Valid`）。
它 = WinForms 安装向导本体（内置 .NET 8 运行时）+ 以资源形式嵌入的 `payload.zip`（26.1 MB）。
`payload.zip` 展开后为 61.6 MB / 108 个文件（已修剪）：主程序（self-contained，目标机**无需**预装 .NET）、
`Assets\7z\7z.exe + 7z.dll`、Shell 扩展四个产物、`MD3Zip.Shell.msix`、`MD3ZipDev.cer`
与「一键安装右键菜单.cmd」。

**生成**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\build_setup.ps1
# -SkipPublish 复用已有 payload（只重打安装包）；-NoSign 跳过签名
```

脚本依次：publish 主程序 → 补齐 Shell / 证书 / MSIX 资源 → 签名负载内所有 `MD3Zip.*` PE
→ 压缩 `payload.zip` → 编译 `src\MD3Zip.Setup`（单文件）→ 签名安装包。
脚本必须以 **UTF-8 BOM** 保存，否则 PowerShell 5.1 会把中文按 ANSI 解析而报语法错。

**安装 / 卸载**

| 命令 | 行为 |
| --- | --- |
| 双击 `MD3Zip-Setup-x64.exe` | 三步向导：选目录与组件 → 进度 → 完成 |
| `... /S` | 静默安装到默认目录，不注册右键菜单 |
| `... /S /D="D:\Some\Dir"` | 静默安装到指定目录 |
| 设置 → 应用 → MD3 Zip → 卸载 | 走 `HKCU\...\Uninstall\MD3Zip` 登记的 `uninstall.ps1` |

默认目录 `%LOCALAPPDATA%\Programs\MD3 Zip`；安装会创建开始菜单 / 桌面快捷方式并写入卸载项。

**架构决策**

| 决策 | 理由 |
| --- | --- |
| 装到当前用户目录 + `HKCU` 卸载项，而非 `Program Files` + `HKLM` | 全程零 UAC，普通用户可直接装/卸；不污染系统目录 |
| 右键菜单做成**可选组件**，勾选时才单独提权导入证书并注册稀疏包 | 这是唯一需要管理员权限的步骤，与主程序安装解耦，失败不影响主程序可用 |
| 静默模式默认不注册右键菜单 | 避免无人值守时被 UAC 弹窗卡住；需要时在安装目录手动运行 `.cmd` |
| 卸载用几 KB 的 `uninstall.ps1`，不复制 106 MB 安装器 | 早期版本安装后占用 206 MB，改为脚本后降到 48.4 MB（当前 61.6 MB，增量来自校验功能与词典扩充） |
| 负载用 zip 内嵌而非 Inno Setup / NSIS / WiX | 本机无现成打包工具链，用 .NET SDK + WinForms 自实现，外部依赖最少 |
| 负载启用 `PublishTrimmed` 但把 `MD3Zip.App` / Avalonia / Material 全部列为 `TrimmerRootAssembly` | 修剪可把负载从 99.8 MB 降到 61.3 MB。**两个反面教材**：① 裁掉 Avalonia → 文本全空白；② 裁掉 `MD3Zip.App` 自身（连 `TrimMode=copyused` 也不行）→ `{Binding IsVisible}` 解析失败，空态/列表同时显示、UI 重叠。像素级对照：rooted 版亮像素 1307 = 不修剪基线 1307，逐像素一致 |

> WinForms 安装器本体不支持修剪（`NETSDK1175`），故 89.0 MB 中约 62.9 MB 是安装器自带运行时——这是无法再压的硬成本。

**实测**（本机，2026-11-28）：静默安装退出码 0 → 108 文件 / 61.6 MB、关键文件齐全 →
启动主程序标题「MD3 Zip」且响应正常 → 执行 `uninstall.ps1` 后安装目录、卸载项、
开始菜单项、桌面快捷方式全部移除。向导界面在 DPI 缩放下无重叠与截断。

---

## 5. 已知限制与注意事项

1. **自签名证书仅本机有效**：包与外部 PE 均用 `CN=MD3Zip Dev` 自签证书签名，证书已导入本机
   `LocalMachine\Root`。换机需重新执行 `install_context_menu.ps1` 生成并信任新证书，
   否则 `Add-AppxPackage` 会因签名不受信任而拒绝注册。
2. **托管 COM + dllhost**：`comhost.dll` 由 `SurrogateServer` 承载，目标机必须有 .NET 8 Desktop Runtime。
   若希望免运行时依赖，把发布命令改成 `--self-contained true`（发布体积会显著增大），并同步修改
   `install_context_menu.ps1` 中的 `dotnet publish` 参数。
3. **右键菜单重复项（已修复）**：早期清单为 `*` 再单独注册 `.7z/.zip/.rar` 等后缀 ItemType，
   但 Windows 11 **不会按 Verb Id 去重**——同一文件命中两个 ItemType 就会出现两个「MD3 Zip」。
   现清单只保留 `*` / `Directory` / `Directory\Background` 三类（`*` 已覆盖所有文件扩展名），
   系统内生效 ItemType 由 14 个减为 3 个，菜单只出现一项。
4. **自绘标题栏的取舍**：使用 `NoChrome` 后不再有系统提供的“贴靠布局（Snap Layouts）”浮层（鼠标悬停最大化按钮时不再弹出分屏选项），但换来了三枚按钮的尺寸/间距/悬停色完全可控；最大化/还原/拖拽/双击最大化均已自实现。
5. **Mica** 仅 Win11 生效，Win10 会回退到 Acrylic/纯色；迷你卡片与压缩弹窗使用
   `TransparencyLevelHint="Transparent"` 实现圆角浮层。
6. **主题不持久化**：每次启动默认深色（需求未要求记忆），切换只影响当前进程。
7. **预览/外开**：预览通过临时解压到 `%TEMP%\MD3Zip\preview\<guid>` 实现，读取后自动清理；
   “用默认程序打开”会保留该临时文件交给系统程序使用。
8. **7z 引擎**：随包为 7-Zip 23.01 x64（`Assets\7z`）。若想升级，替换 `7z.exe` + `7z.dll` 即可，
   接口不依赖具体版本（升级仍须走 `work\out\kernel-upgrade-playbook.md` 第三节 SHA-256 闸门）；
   若删除了该目录，程序**不会**再从 `C:\Program Files\7-Zip` 等处复制内核（该无校验旁路已移除），
   而是提示重新安装；本次运行仍按 `Locate()` 回落本机 7-Zip，并在界面显式标注该降级来源。
9. **本次工作区新增的工具**（可随时删除，不影响交付）：
   - `.tools\dotnet`：本机缺 SDK 时安装的 .NET 8.0.428（726 MB），删除后请改用系统 `dotnet`；
   - `.tools\shot`：本次用于 UI 自动验收的截图/点击小工具；
   - `.tools\make_icon.py`：图标生成脚本（ICO + 打包 PNG）；
   - `packaging\tools\ComProbe`：右键菜单 COM 全链路诊断工具（见 3.1，可独立运行）；
   - `packaging\tools\CoreSmoke`：Core 冒烟测试（压缩 / 测试 / 追加 / 删除 / 哈希 / 单文件格式）。

10. **「更多」浮层的配色**：Popup 的 `PopupRoot` 是独立 TopLevel，实测不继承窗口的
    `ThemeVariant`（`ThemeVariantScope` 在 Avalonia 11.3 上也不生效），会把 `Md3.*`
    解析成浅色值。因此浮层改用固定的深色令牌 `Md3.Overlay*`：深色主界面下观感正确，
    切到浅色主题时浮层仍保持深色（当作「强调层」处理）。
11. **安装包仅面向 Windows x64**：内置 win-x64 自包含运行时，不支持 x86 / ARM64；
    89.0 MB 主要来自安装器内置的 .NET 8 运行时与 7-Zip 引擎。右键菜单组件依赖自签证书与稀疏包，
    勾选安装时会单独弹一次 UAC，静默安装不包含该步骤（装完可在安装目录运行 `.cmd` 补注册）。

---

## 6. 验收自测清单

- [x] `dotnet build -c Release` 三项目 0 错误 0 警告
- [x] 中文路径 / 空格路径 压缩、解压、列表全通
- [x] 进度百分比实时上报（30 MB 随机数据压缩实测 20 次回调）
- [x] 取消操作返回「已取消」且进程树被终止
- [x] 错误密码返回结构化 `WrongPassword`
- [x] 智能解压 A/B 两种分支行为正确
- [x] 深/浅色切换整套界面同步
- [x] 压缩配置 → 进度 → 自动关闭（实测生成 `readme.7z`）
- [x] 迷你卡片右下角浮出、完成后自动退出
- [x] 双击文件预览（修复过覆盖层状态不刷新的缺陷）
- [x] 自绘窗口按钮：最小化/最大化/还原/关闭 + 关闭红底悬停（截图核对）
- [x] 打开加密压缩包不再卡死（`-p-` + stdin 关闭双保险）
- [x] 密码框内输错密码保持弹窗并红字提示，输对后成功打开（含「· 加密」标记）
- [x] 迷你解压卡片遇到加密包同样弹出密码框，通过后继续解压并自动退出
- [x] `--password <pwd>` 真正生效，加密包可直接解锁打开
- [x] 稀疏包 `MakeAppx` 校验通过（修正 `win32App` 不得声明 `EntryPoint`）
- [x] 稀疏包清单 XML 合法（14 个 Verb、1 个 COM 类）
- [x] 主窗口 960×640 / 最小 680×480、压缩弹窗最小 460×420 均可自由缩放，且最小尺寸有钳制
- [x] 缩放后列表、FAB、状态栏不遮挡不漂移（680×480 与 1200×852 两端截图核对）
- [x] 压缩弹窗展开高级选项后内容随窗口延展、溢出走滚动条，无截断
- [x] 管理员运行 `install_context_menu.ps1`（已执行：包 `MD3Zip.Shell_1.0.0.0_x64__ps1av0evqpwje` Status=Ok）
- [x] `ComProbe` COM 激活 `S_OK`，`GetTitle="MD3 Zip"`、`GetFlags=HASSUBCOMMANDS`、`Next` 返回 3 个子命令
- [x] 模拟点击「添加到压缩包…」：`Invoke=S_OK`，拉起 `MD3Zip.App.exe --compress-dialog`，窗口标题「新建压缩包」且响应正常
- [x] 修复 dllhost 下 `AppContext.BaseDirectory` 为空导致主程序路径失效的问题（改用 `Assembly.Location`）
- [x] 调用日志改写到包外 `packaging\publish\app\logs\shell.log`，避免被 MSIX 虚拟化重定向
- [x] `dotnet build MD3Zip.sln -c Release` 复验：0 错误 0 警告
- [x] ExternalLocation 内 5 个 `MD3Zip.*.exe/.dll` 全部重新签名，`Get-AuthenticodeSignature` 均为 `Valid`
- [x] 修正 `GetFlags` 枚举串用（`0x4` → `ECF_HASSUBCOMMANDS=0x1`），`ComProbe` 复验 `GetFlags=0x1` 且 3 个子命令可枚举
- [x] 一级菜单 `Invoke` 兜底：直接点「MD3 Zip」也能拉起「新建压缩包」（实测 pid 21272，Responding=True）
- [x] 部署后重启 `explorer.exe` 重建菜单缓存（新 pid 19784），并结束加载旧 DLL 的 `dllhost`
- [x] Core 冒烟测试 9 项全通过（压缩 / 测试 / 追加 / 删除 / SHA-256 / CRC32 / gzip / 单文件校验 / 覆盖策略）
- [x] 主窗口「更多」浮层：6 个入口，未打开归档时依赖 `HasArchive`/`HasSelection` 的项正确灰显
- [x] 压缩对话框：6 种格式（7z/zip/tar/gz/bz2/xz）单行正常、密码显隐眼睛、压缩后删除源、分卷
- [x] 稀疏包 `Status=Ok`，5 个外部 PE 重新签名后均为 `Valid`
- [x] Windows 安装包 `MD3Zip-Setup-x64.exe` 生成：89.0 MB（原 106.4 MB，-16.4%）、x64、自包含 .NET 8、签名 `Valid`
- [x] `build_setup.ps1` 支持 `-SkipPublish` / `-NoSign`，脚本以 UTF-8 BOM 保存（PowerShell 5.1 中文解析）
- [x] 静默安装 `/S /D=...` 退出码 0，100 文件 / 48.4 MB，卸载项与两处快捷方式齐全，无 `uninstall.exe` 冗余副本
- [x] 安装后主程序可启动（标题「MD3 Zip」）；卸载后目录 / 注册表 / 开始菜单 / 桌面全部清理
- [x] 安装向导三步界面在 DPI 缩放下无重叠、无截断（显式 `ColumnStyles` + 长文本 `MaximumSize` 换行）
- [x] 中英双语：`{loc:T}` / `L10n.L()` 覆盖 279 条文案，英文截图核对工具栏/搜索框/表头/空状态/按钮全英文
- [x] 更多菜单底部「English / 中文」切换入口，选中项动态反转，语言写入 `settings.json` 持久化
- [x] 安装包精简：负载 `PublishTrimmed` + `MD3Zip.App`/Avalonia 根程序集保留，负载 99.8 → 61.3 MB，CoreSmoke 9 项与界面渲染复验通过
- [x] 修复 UI 错位重叠：修剪曾裁掉 App 反射元数据致 `{Binding IsVisible}` 失效（空态/列表同时显示），rooted 后像素级回归基线
- [x] 修复「新建压缩」重复 3 处：收敛到 FAB 一处（顶栏与空态的副本已删除）
- [x] 修复右键菜单重复：清单 ItemType 由 14 减为 3（仅 `*`/`Directory`/`Directory\Background`），重新打包签名注册后菜单只出现一项
- [x] Win11 原生圆角统一：6 个窗口外层 28/24→8px（与 DWM 8px 裁剪重合、无黑角），内容卡片/图标容器收敛至 8–12px（2026-11-28 第五轮）
- [x] 压缩等级标签与 thumb 行程精确对齐（Grid `Auto/*/Auto/*/Auto` + 2px 内缩，实测首尾贴轨道端点、"标准"正对 thumb）
- [x] ChangeFormat 裸名截断残余清除（CompressViewModel.cs:612 不再二次剥离），zip↔7z 往返文件名 `3.1.0` 完整
