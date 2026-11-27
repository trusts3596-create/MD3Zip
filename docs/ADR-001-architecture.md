# 架构决策记录（ADR）

本文件记录本项目几个"不好回头"的选择，以及被放弃的替代方案。每条决策都标注了代价。

---

## ADR-001：用「托管 COM host + 稀疏包」实现 Win11 现代右键菜单

**状态**：已采纳

**背景**
Win11 一级菜单只接受通过 `windows.fileExplorerContextMenus` 注册、并实现 `IExplorerCommand`
的 COM 组件。团队栈是 C#/.NET 8，不想为菜单单独维护一个 C++ DLL。

**决策**
`MD3Zip.ShellExtension` 用 `EnableComHosting` 产出 `*.comhost.dll`，在清单里以
`com:SurrogateServer`（dllhost 承载）+ `ThreadingModel="STA"` 注册；外层用稀疏包
（`uap10:AllowExternalContent`）把二进制留在 `packaging\publish\app`，包本体只有身份与注册信息。

**代价**
- 目标机必须有 .NET 8 Desktop Runtime（自包含发布可解，但体积从 ~10 MB 涨到 ~70 MB）。
- 托管对象进 dllhost 属于"能用但非官方主推"的路径，出问题时要靠 `Event Viewer` + COM 错误码定位。
- 子命令不单独声明 CLSID，而是用 `Marshal.GetComInterfaceForObject` 返回 CCW —— 少 3 个注册项，
  但要求子命令对象必须实现 `[ComImport]` 接口且不能跨 AppDomain（.NET 8 无 AppDomain，无影响）。

**放弃的替代方案**
| 方案 | 放弃原因 |
| --- | --- |
| 传统 `HKCU\Software\Classes\*\shell` 注册表 + `IContextMenu` | 只出现在"显示更多选项"旧菜单里，不满足 Win11 一级菜单要求 |
| 纯注册表注册 `IExplorerCommand`（非打包） | Win11 只信任带包身份的注册，非打包路径在 22H2 起被忽略 |
| C++ ATL DLL | 免运行时，但需要额外工具链与 32/64 双份构建，团队维护成本高 |

---

## ADR-002：压缩/解压全部外包给 `7z.exe` 进程

**状态**：已采纳

**背景**
需求明确要求复用 7-Zip 引擎、不重写算法。

**决策**
`SevenZipService` 用 `System.Diagnostics.Process` + `ArgumentList`（自动处理引号）异步调用
`7z.exe`；统一注入 `-sccUTF-8 -bsp1`，用逐字符缓冲解析进度。

**关键实现细节（都是踩过的坑）**
- 7z 用 `\b` 原地擦除进度、用 `\r` 回车；必须按"控制台缓冲区"语义处理，否则
  `\r\n` 会被拆成两次换行，把 `-slt` 的键值块切碎。
- `-slt` 的条目以**空行**分隔，输出缓冲必须保留空行，否则整份清单会塌成一条记录。
- 进度行的百分比可能因为擦除不彻底而粘连，解析时取"最后一个 `%` 前的数字"最稳。
- 取消必须 `Kill(entireProcessTree: true)`：7z 会派生线程/子进程，只杀主进程会留下锁文件。

**代价**
- 每次操作有进程启动开销（约 20–40 ms），对右键"秒开"可接受（卡片本身不阻塞）。
- 无法拿到 7z 的内存级错误细节，只能靠 stdout/stderr 文本 + 退出码做语义映射
  （已覆盖密码错误、磁盘不足、文件占用、找不到、非归档 5 类高频错误）。

**放弃的替代方案**
| 方案 | 放弃原因 |
| --- | --- |
| P/Invoke `7z.dll` 的 `CreateObject` COM 接口 | 需要手写大量 COM 声明与回调，跨版本 ABI 风险高 |
| 纯托管库（SharpCompress / SevenZipSharp） | 7z 格式支持不完整，且违背"不重写/不复刻引擎"的约束 |

---

## ADR-003：锁定 Material.Avalonia 3.14（Avalonia 11）+ 自建 MD3 令牌层

**状态**：已采纳

**背景**
Material.Avalonia 3.15+ 已转向 Avalonia 12，而需求锁定 Avalonia 11.x。

**决策**
`Avalonia 11.3.23` + `Material.Avalonia 3.14.0`（最后一个依赖 `Avalonia >= 11.1.0` 的稳定版）。
Material 只作为"底层控件行为与涟漪"的来源；MD3 的语义色、形状、字阶全部由
`Themes/Tokens.axaml` 自建（Light/Dark 双 `ThemeDictionaries`），控件模板在
`Themes/Controls.axaml` 里重写关键部件（Button / TextBox / CheckBox / Slider / ProgressBar / FAB）。

**代价**
- Material 自带的是 M2 视觉，部分控件（如 ScrollBar）仍是 M2 风格，与 MD3 有细微不一致。
- TextBox 模板必须显式提供 `PART_TextPresenter`（Avalonia 11.3 的硬性要求），
  升级 Avalonia 大版本时需要重新核对模板部件名。
- 主题切换要同时驱动 `Application.RequestedThemeVariant` 与 Material 的 `BaseTheme`，
  否则会出现"Fluent 浅色 + Material 深色"的割裂。已封装在 `ThemeManager`。

**放弃的替代方案**
| 方案 | 放弃原因 |
| --- | --- |
| 升级到 Avalonia 12 + Material 3.22 | 与需求"11.x"冲突；且 Avalonia 12 的 API 变动未验证 |
| 只用 FluentTheme 手写全部样式 | 失去 Material 的涟漪/层级细节，工作量大且效果不一定更好 |
| 引入 Material.Icons.Avalonia | 2.4.3 实际依赖 Avalonia 12（还原期 NU1605），改用内联矢量图标 |

---

## ADR-004：不引入 MVVM 框架，手写 120 行基础设施

**状态**：已采纳

**决策**
`ObservableObject` + `RelayCommand` + `AsyncRelayCommand`（执行期自动禁用、异常集中回传），
共约 120 行，替代 CommunityToolkit.Mvvm。

**代价**
- 没有源生成器的属性模板，属性要手写 `SetProperty`（本项目 VM 属性数量可控）。
- 没有 `WeakReferenceMessenger`，跨窗口通信用事件 + 构造参数，规模变大时要补。

**收益**
依赖树只剩 Avalonia + Material 两项，还原失败面最小（本次确实被 Material.Icons 的版本链坑过一次）。

---

## ADR-005：主窗口「单窗口 + 视图切换」，右键入口「独立轻窗口」

**状态**：已采纳

**背景**
右键"解压到当前文件夹"要求点击后近乎无感；浏览器主窗口则需要完整 MD3 布局。

**决策**
`App.OnFrameworkInitializationCompleted` 按 CLI 模式决定**只创建哪个窗口**：
主窗口用 `ShutdownMode.OnExplicitShutdown` 管理生命周期；迷你卡片与压缩弹窗各自独立，
关闭即 `desktop.Shutdown()`。这样右键路径不会为 1100×740 的主窗口付出布局与渲染成本。

**代价**
- 4 个启动模式分散在 `CliOptions` + `App` 两处，新增模式要同步改两处。
- 主窗口内再弹模态窗（压缩/预览/密码/错误）时，需要在 `IDialogService` 上扩展方法，
  而不是直接 new Window —— 这是刻意的边界，保证 VM 不依赖具体视图。

---

## 未来演进（如果这个项目继续做）

1. **持久化**：主题选择、窗口尺寸、最近打开的压缩包 → `%LOCALAPPDATA%\MD3Zip\settings.json`。
2. **多选批量压缩进度**：把 FAB 的"新建压缩"扩展成任务队列，压缩与解压可并行。
3. **右键菜单精简**：如果实测 `ItemType` 出现重复项，收敛为单一 `*` + `Directory`。
4. **自包含发布**：去掉目标机 .NET 运行时依赖（需同步改签名脚本的发布参数）。
