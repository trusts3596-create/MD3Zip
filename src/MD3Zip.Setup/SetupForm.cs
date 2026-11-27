using MD3Zip.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MD3Zip.Setup;

/// <summary>
/// 三步向导：欢迎（选择目录/组件）→ 安装进度 → 完成。
/// 布局全部交给 TableLayoutPanel + AutoSize，避免高 DPI 下绝对坐标重叠。
/// 所有单列容器必须显式声明 Percent=100% 列宽：默认的 AutoSize 列会按最宽
/// 子控件（长副标题）撑出容器，导致右侧内容连同按钮一起被裁切。
/// </summary>
internal sealed class SetupForm : Form
{
    private static readonly Color Surface = Color.FromArgb(0x1D, 0x1B, 0x20);
    private static readonly Color SurfaceHigh = Color.FromArgb(0x2B, 0x29, 0x30);
    private static readonly Color OnSurface = Color.FromArgb(0xE6, 0xE0, 0xE9);
    private static readonly Color OnSurfaceVariant = Color.FromArgb(0xCA, 0xC4, 0xD0);
    private static readonly Color Primary = Color.FromArgb(0xD0, 0xBC, 0xFF);
    private static readonly Color OnPrimary = Color.FromArgb(0x21, 0x00, 0x5D);
    private static readonly Color Outline = Color.FromArgb(0x49, 0x45, 0x4F);
    private static readonly Color ErrorTint = Color.FromArgb(0xF2, 0xB8, 0xB5);

    /// <summary>内容区最大宽度（逻辑像素），比 ClientSize - 左右 padding 略小，保证长文本换行。</summary>
    private const int ContentWidth = 700;

    private readonly Panel _pageHost = new();
    private readonly Panel _welcomePage = new();
    private readonly Panel _progressPage = new();
    private readonly Panel _donePage = new();
    private readonly TextBox _pathBox = new();
    private readonly CheckBox _desktopBox = new();
    private readonly CheckBox _contextBox = new();
    private readonly CheckBox _launchBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _stageLabel = new();
    private readonly Label _doneTitle = new();
    private readonly Label _doneDetail = new();
    private readonly Button _primary = new();
    private readonly Button _secondary = new();

    private readonly string _initialDirectory;
    private string _installedDirectory = string.Empty;
    private int _page;
    private bool _finished;

    public SetupForm(string? initialDirectory)
    {
        _initialDirectory = string.IsNullOrWhiteSpace(initialDirectory)
            ? Installer.DefaultDirectory
            : initialDirectory;

        BuildUi();
        ShowPage(0);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>让系统标题栏跟随深色界面（Win10 20H1+ 用属性 20，早期版本退回 19）。</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var dark = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
        }
    }

    private void BuildUi()
    {
        Text = L10n.L("MD3 Zip 安装程序");
        ClientSize = new Size(800, 650);
        MinimumSize = new Size(740, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Surface;
        ForeColor = OnSurface;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(32, 18, 32, 18),
            BackColor = Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var brand = new Label
        {
            Text = "MD3 Zip",
            Font = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold),
            ForeColor = Primary,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 0)
        };

        var subtitle = new Label
        {
            Text = L10n.L("Material Design 3 风格的 7-Zip 外壳 · Windows 11 右键菜单"),
            ForeColor = OnSurfaceVariant,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(2, 2, 0, 14)
        };

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = Surface;

        BuildWelcomePage();
        BuildProgressPage();
        BuildDonePage();
        _pageHost.Controls.AddRange(new Control[] { _welcomePage, _progressPage, _donePage });

        _primary.Text = L10n.L("安装");
        _primary.AutoSize = false;
        _primary.Size = new Size(132, 38);
        _primary.FlatStyle = FlatStyle.Flat;
        _primary.FlatAppearance.BorderSize = 0;
        _primary.BackColor = Primary;
        _primary.ForeColor = OnPrimary;
        _primary.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _primary.Margin = new Padding(10, 0, 0, 0);
        _primary.Click += OnPrimaryClick;

        _secondary.Text = L10n.L("取消");
        _secondary.AutoSize = false;
        _secondary.Size = new Size(96, 38);
        _secondary.FlatStyle = FlatStyle.Flat;
        _secondary.FlatAppearance.BorderColor = Outline;
        _secondary.BackColor = Surface;
        _secondary.ForeColor = OnSurface;
        _secondary.Margin = new Padding(0);
        _secondary.Click += OnSecondaryClick;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Surface,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        buttons.Controls.Add(_primary);
        buttons.Controls.Add(_secondary);

        root.Controls.Add(brand, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(_pageHost, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
    }

    private void BuildWelcomePage()
    {
        _welcomePage.Dock = DockStyle.Fill;
        _welcomePage.BackColor = Surface;
        _welcomePage.Visible = false;

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Surface
        };
        for (var i = 0; i < 6; i++)
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var intro = new Label
        {
            Text = L10n.L("安装包已内置 .NET 8 运行时，装完即可使用，无需另外安装依赖。"),
            ForeColor = OnSurfaceVariant,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, 14)
        };

        var pathLabel = new Label
        {
            Text = L10n.L("安装位置"),
            ForeColor = OnSurface,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        var pathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = false,
            Height = 36,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 14),
            BackColor = Surface
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Height = 30;
        _pathBox.Margin = new Padding(0, 3, 10, 3);
        _pathBox.Text = _initialDirectory;
        _pathBox.BackColor = SurfaceHigh;
        _pathBox.ForeColor = OnSurface;
        _pathBox.BorderStyle = BorderStyle.FixedSingle;

        var browse = new Button
        {
            Text = L10n.L("浏览…"),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = OnSurface,
            Margin = new Padding(0, 2, 0, 2)
        };
        browse.FlatAppearance.BorderColor = Outline;
        browse.Click += OnBrowseClick;

        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);

        _desktopBox.Text = L10n.L("创建桌面快捷方式");
        _desktopBox.Checked = true;
        _desktopBox.AutoSize = true;
        _desktopBox.ForeColor = OnSurface;
        _desktopBox.Margin = new Padding(0, 0, 0, 8);

        _contextBox.Text = L10n.L("注册 Windows 11 右键菜单（会弹出一次管理员授权）");
        _contextBox.Checked = true;
        _contextBox.AutoSize = true;
        _contextBox.ForeColor = OnSurface;
        _contextBox.Margin = new Padding(0, 0, 0, 16);

        var hint = new Label
        {
            Text = "安装到当前用户目录（无需管理员），约 105 MB。\n"
                 + L10n.L("右键菜单为可选组件，装好后也可在安装目录运行「一键安装右键菜单.cmd」重新注册。"),
            ForeColor = Color.FromArgb(0x93, 0x8F, 0x99),
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0)
        };

        page.Controls.Add(intro, 0, 0);
        page.Controls.Add(pathLabel, 0, 1);
        page.Controls.Add(pathRow, 0, 2);
        page.Controls.Add(_desktopBox, 0, 3);
        page.Controls.Add(_contextBox, 0, 4);
        page.Controls.Add(hint, 0, 5);

        _welcomePage.Controls.Add(page);
    }

    private void BuildProgressPage()
    {
        _progressPage.Dock = DockStyle.Fill;
        _progressPage.BackColor = Surface;
        _progressPage.Visible = false;

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Surface
        };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var title = new Label
        {
            Text = L10n.L("正在安装…"),
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = OnSurface,
            AutoSize = true,
            Margin = new Padding(0, 40, 0, 20)
        };

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 12;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Margin = new Padding(0, 0, 0, 12);

        _stageLabel.AutoSize = true;
        _stageLabel.ForeColor = OnSurfaceVariant;
        _stageLabel.Margin = new Padding(0);

        page.Controls.Add(title, 0, 0);
        page.Controls.Add(_progressBar, 0, 1);
        page.Controls.Add(_stageLabel, 0, 2);

        _progressPage.Controls.Add(page);
    }

    private void BuildDonePage()
    {
        _donePage.Dock = DockStyle.Fill;
        _donePage.BackColor = Surface;
        _donePage.Visible = false;

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Surface
        };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _doneTitle.Text = L10n.L("安装完成");
        _doneTitle.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        _doneTitle.ForeColor = Primary;
        _doneTitle.AutoSize = true;
        _doneTitle.Margin = new Padding(0, 40, 0, 16);

        _doneDetail.AutoSize = true;
        _doneDetail.MaximumSize = new Size(ContentWidth, 0);
        _doneDetail.ForeColor = OnSurfaceVariant;
        _doneDetail.Margin = new Padding(0, 0, 0, 20);

        _launchBox.Text = L10n.L("立即启动 MD3 Zip");
        _launchBox.Checked = true;
        _launchBox.AutoSize = true;
        _launchBox.ForeColor = OnSurface;
        _launchBox.Margin = new Padding(0);

        page.Controls.Add(_doneTitle, 0, 0);
        page.Controls.Add(_doneDetail, 0, 1);
        page.Controls.Add(_launchBox, 0, 2);

        _donePage.Controls.Add(page);
    }

    private void ShowPage(int index)
    {
        _page = index;
        _welcomePage.Visible = index == 0;
        _progressPage.Visible = index == 1;
        _donePage.Visible = index == 2;

        _secondary.Visible = index != 1;
        _secondary.Text = index == 2 ? L10n.L("关闭") : L10n.L("取消");
        _primary.Text = index switch
        {
            0 => L10n.L("安装"),
            2 => L10n.L("完成"),
            _ => L10n.L("请稍候…")
        };
        _primary.Enabled = index != 1;
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L10n.L("选择安装位置"),
            SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : Installer.DefaultDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.SelectedPath;
        }
    }

    private async void OnPrimaryClick(object? sender, EventArgs e)
    {
        if (_page == 2)
        {
            _finished = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_page != 0)
        {
            return;
        }

        var target = _pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(L10n.L("请填写安装位置。"), Installer.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法创建安装目录：{ex.Message}", Installer.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ShowPage(1);
        _progressBar.Value = 0;
        _stageLabel.Text = L10n.L("正在准备…");

        var options = new InstallOptions(target, _desktopBox.Checked, _contextBox.Checked);
        IProgress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            _stageLabel.Text = p.Stage;
        });

        try
        {
            // Progress<T>.Report 会把回调 Post 回 UI 线程，后台安装过程只做文件操作。
            await Task.Run(() => Installer.Install(options, p => progress.Report(p))).ConfigureAwait(true);

            _installedDirectory = target;
            _doneTitle.Text = L10n.L("安装完成");
            _doneTitle.ForeColor = Primary;
            _doneDetail.Text = $"已安装到：\n{target}\n\n"
                             + L10n.L("开始菜单已创建快捷方式")
                             + (_desktopBox.Checked ? L10n.L("，桌面快捷方式也已创建。") : "。")
                             + (_contextBox.Checked ? "\n右键任意文件或文件夹即可看到「MD3 Zip」。" : string.Empty);
            _launchBox.Visible = true;
            ShowPage(2);
        }
        catch (Exception ex)
        {
            _doneTitle.Text = L10n.L("安装失败");
            _doneTitle.ForeColor = ErrorTint;
            _doneDetail.Text = ex.Message;
            _launchBox.Visible = false;
            ShowPage(2);
        }
    }

    private void OnSecondaryClick(object? sender, EventArgs e)
    {
        if (_page == 1)
        {
            return;
        }

        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (!_finished || !_launchBox.Checked || string.IsNullOrEmpty(_installedDirectory))
        {
            return;
        }

        var exe = Path.Combine(_installedDirectory, Installer.AppExeName);
        if (!File.Exists(exe))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = _installedDirectory
            });
        }
        catch (Exception)
        {
            // 启动失败不影响安装结果。
        }
    }
}
