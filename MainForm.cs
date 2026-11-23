using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyTierMoonlightManager.Services;

namespace EasyTierMoonlightManager;

public sealed class MainForm : Form
{
    // ---------- 状态页 ----------
    private Label _banner;
    private Button _btnStart, _btnStop, _btnRestart, _btnRefresh;
    private Label _vState, _vVip, _vExit, _vNat, _vOnline, _vSun, _vPorts, _vTask;
    private DataGridView _gridPeers;
    private CheckBox _chkAuto;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };

    // ---------- 节点页 ----------
    private BindingList<NodeEntry> _nodeBinding;
    private DataGridView _gridNodes;
    private Label _lblNodeHint;
    private CancellationTokenSource _testCts;

    // ---------- 设置页 ----------
    private TextBox _txtNet, _txtSecret, _txtVip;
    private Label _vSunState, _vSunPorts;
    private CheckBox _chkWan;

    // ---------- 日志页 ----------
    private TextBox _txtLog;

    private ToolStripStatusLabel _status;
    private readonly bool _demo;
    private readonly int _demoTab;
    private TabControl _tabs;
    private bool _busy;

    public MainForm()
    {
        var argv = Environment.GetCommandLineArgs();
        _demo = argv.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var tabArg = argv.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
        _demoTab = tabArg != null && int.TryParse(tabArg[6..], out var ti) ? Math.Clamp(ti, 0, 3) : 0;
        if (!_demo) Paths.EnsureDirs();
        _nodeBinding = new BindingList<NodeEntry>(_demo ? DemoNodes() : NodeStore.Load());

        Text = _demo ? "EasyTier · Moonlight 远程串流管理器（演示）" : "EasyTier · Moonlight 远程串流管理器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(900, 620);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        BuildUi();
        _timer.Tick += async (_, _) => { if (_chkAuto.Checked) await RefreshAllAsync(silent: true); };
        _timer.Start();

        Shown += async (_, _) => { _tabs.SelectedIndex = _demo ? _demoTab : 0; await RefreshAllAsync(); };
        FormClosing += (_, _) => { _timer.Stop(); _testCts?.Cancel(); };
    }

    // ================================================================ UI 构建
    private void BuildUi()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        var tabs = _tabs;
        tabs.TabPages.Add(BuildStatusTab());
        tabs.TabPages.Add(BuildNodesTab());
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildLogTab());
        tabs.SelectedIndex = 0;

        _status = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.Add(_status);

        Controls.Add(tabs);
        Controls.Add(strip);
    }

    // ---------------------------------------------------------------- 状态页
    private TabPage BuildStatusTab()
    {
        var page = new TabPage("状态") { Padding = new Padding(10) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- 顶部：横幅 + 信息 + 按钮
        var top = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        _banner = new Label
        {
            Text = "正在读取状态…",
            Dock = DockStyle.Top,
            Height = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            BackColor = Color.FromArgb(240, 240, 240),
            ForeColor = Color.FromArgb(60, 60, 60)
        };

        var info = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 2, Height = 74, Padding = new Padding(4, 8, 4, 4) };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

        _vState = AddInfo(info, 0, 0, "EasyTier：");
        _vVip = AddInfo(info, 0, 2, "虚拟 IP：");
        _vExit = AddInfo(info, 1, 0, "公网出口：");
        _vNat = AddInfo(info, 1, 2, "NAT 类型：");
        _vSun = AddInfo(info, 2, 0, "Sunshine：");
        _vPorts = AddInfo(info, 2, 2, "串流端口：");
        _vOnline = AddInfo(info, 3, 0, "在线节点：");
        _vTask = AddInfo(info, 3, 2, "开机自启：");

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 330,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };

        _btnStart = MakeButton("启动", 88, Color.FromArgb(16, 124, 16));
        _btnStop = MakeButton("停止", 88, Color.FromArgb(196, 43, 28));
        _btnRestart = MakeButton("重启", 88, Color.FromArgb(0, 95, 184));
        buttons.Controls.AddRange(new Control[] { _btnRestart, _btnStop, _btnStart });

        _btnStart.Click += async (_, _) => await RunActionAsync("正在启动 EasyTier…", () => EasyTierService.StartCore(), wait: 4000);
        _btnStop.Click += async (_, _) => await RunActionAsync("正在停止 EasyTier…", () => EasyTierService.StopCore(), wait: 1500);
        _btnRestart.Click += async (_, _) => await RunActionAsync("正在重启 EasyTier…", () => EasyTierService.RestartCore(), wait: 5000);

        // Dock 顺序：先放 Right 的按钮，再放 Top 的横幅/信息
        top.Controls.Add(info);
        top.Controls.Add(_banner);
        top.Controls.Add(buttons);

        // ---- 中间：peer 表格
        _gridPeers = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 30
        };
        _gridPeers.Columns.Add(Col("host", "主机名", 26));
        _gridPeers.Columns.Add(Col("ip", "虚拟 IP", 14));
        _gridPeers.Columns.Add(Col("cost", "连接方式", 11));
        _gridPeers.Columns.Add(Col("lat", "延迟 ms", 10));
        _gridPeers.Columns.Add(Col("loss", "丢包", 8));
        _gridPeers.Columns.Add(Col("tunnel", "隧道", 10));
        _gridPeers.Columns.Add(Col("nat", "NAT", 13));
        _gridPeers.Columns.Add(Col("version", "版本", 12));

        // ---- 底部工具条
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(2, 8, 2, 2), WrapContents = false };
        _chkAuto = new CheckBox { Text = "自动刷新（3 秒）", Checked = true, AutoSize = true, Margin = new Padding(6, 6, 14, 0) };
        _btnRefresh = MakeButton("立即刷新", 96, Color.FromArgb(0, 95, 184));
        _btnRefresh.Click += async (_, _) => await RefreshAllAsync();
        var btnLog = MakeButton("打开日志", 96, Color.FromArgb(90, 90, 90));
        btnLog.Click += (_, _) => OpenPath(Paths.MainLog);
        var btnCfg = MakeButton("打开配置", 96, Color.FromArgb(90, 90, 90));
        btnCfg.Click += (_, _) => OpenPath(Paths.ConfigFile);
        tools.Controls.AddRange(new Control[] { _chkAuto, _btnRefresh, btnLog, btnCfg });

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_gridPeers, 0, 1);
        root.Controls.Add(tools, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    private static Label AddInfo(TableLayoutPanel tlp, int row, int col, string caption)
    {
        var cap = new Label { Text = caption, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Anchor = AnchorStyles.Left, Margin = new Padding(8, 4, 0, 0) };
        var val = new Label { Text = "—", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 4, 12, 0) };
        tlp.Controls.Add(cap, col, row);
        tlp.Controls.Add(val, col + 1, row);
        return val;
    }

    private static DataGridViewTextBoxColumn Col(string name, string header, float weight)
        => new() { Name = name, HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };

    private static Button MakeButton(string text, int width, Color color) => new()
    {
        Text = text,
        Width = width,
        Height = 32,
        FlatStyle = FlatStyle.Flat,
        BackColor = color,
        ForeColor = Color.White,
        Margin = new Padding(6, 0, 0, 0),
        Cursor = Cursors.Hand
    };

    // ---------------------------------------------------------------- 节点页
    private TabPage BuildNodesTab()
    {
        var page = new TabPage("节点库") { Padding = new Padding(10) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        var tsTestAll = SmallButton("① 一键测速（全部）", 150, Color.FromArgb(0, 95, 184));
        var tsTestSel = SmallButton("测试所选", 100, Color.FromArgb(0, 120, 140));
        var tsAdd = SmallButton("添加节点", 100, Color.FromArgb(90, 90, 90));
        var tsDel = SmallButton("删除", 70, Color.FromArgb(150, 80, 80));
        var tsApply = SmallButton("② 设为当前节点", 140, Color.FromArgb(16, 124, 16));
        var tsUpdate = SmallButton("从网络更新列表", 130, Color.FromArgb(120, 90, 0));
        var tsReset = SmallButton("重置内置", 90, Color.FromArgb(90, 90, 90));
        var tsSite = SmallButton("官方状态页", 100, Color.FromArgb(90, 90, 90));

        tsTestAll.Click += async (_, _) => await TestNodesAsync(all: true);
        tsTestSel.Click += async (_, _) => await TestNodesAsync(all: false);
        tsAdd.Click += (_, _) => AddNodeDialog();
        tsDel.Click += (_, _) => DeleteSelected();
        tsApply.Click += async (_, _) => await ApplySelectedNodesAsync();
        tsUpdate.Click += async (_, _) => await UpdateFromNetworkAsync();
        tsReset.Click += (_, _) => ResetBuiltIn();
        tsSite.Click += (_, _) => OpenUrl("https://uptime.easytier.cn/");

        bar.Controls.AddRange(new Control[] { tsTestAll, tsTestSel, tsAdd, tsDel, tsApply, tsUpdate, tsReset, tsSite });

        _gridNodes = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeight = 30
        };
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "名称", DataPropertyName = "Name", FillWeight = 30 });
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "节点地址", DataPropertyName = "Uri", FillWeight = 34 });
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "来源", DataPropertyName = "Source", FillWeight = 9 });
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "连通性", DataPropertyName = "ResultText", FillWeight = 9 });
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "延迟 ms", DataPropertyName = "LastLatency", FillWeight = 9 });
        _gridNodes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "测试时间", DataPropertyName = "LastTestedAt", FillWeight = 9 });
        _gridNodes.DataSource = _nodeBinding;
        _gridNodes.CellFormatting += (object s, DataGridViewCellFormattingEventArgs e) =>
        {
            if (e.RowIndex >= 0 && _gridNodes.Columns[e.ColumnIndex]?.HeaderText == "连通性")
            {
                var txt = e.Value?.ToString();
                e.CellStyle.ForeColor = txt == "可用" ? Color.FromArgb(16, 124, 16)
                    : txt == "连不上" ? Color.FromArgb(196, 43, 28)
                    : Color.FromArgb(110, 110, 110);
                e.CellStyle.Font = new Font(Font, FontStyle.Bold);
            }
        };

        _lblNodeHint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(4, 6, 4, 0),
            ForeColor = Color.FromArgb(110, 110, 110),
            Text = "先「一键测速」确认节点可用，再选中「设为当前节点」（可多选：多个节点互为备份）。" +
                   "切换会写入 config.toml 并重启 EasyTier；外部电脑必须使用同一批节点地址才能互通。"
        };

        root.Controls.Add(bar, 0, 0);
        root.Controls.Add(_gridNodes, 0, 1);
        root.Controls.Add(_lblNodeHint, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    private static Button SmallButton(string text, int width, Color color)
    {
        var b = MakeButton(text, width, color);
        b.Height = 30;
        b.Margin = new Padding(0, 0, 6, 6);
        return b;
    }

    // ---------------------------------------------------------------- 设置页
    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("设置") { Padding = new Padding(10), AutoScroll = true };

        // --- 网络
        var grpNet = new GroupBox { Text = "虚拟网络（保存后自动重启生效）", Dock = DockStyle.Top, Height = 168, Padding = new Padding(12, 8, 12, 8) };
        var netTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        netTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        netTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        netTable.Controls.Add(new Label { Text = "网络名称", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _txtNet = new TextBox { Dock = DockStyle.Fill };
        netTable.Controls.Add(_txtNet, 1, 0);

        netTable.Controls.Add(new Label { Text = "网络密钥", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _txtSecret = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = false };
        netTable.Controls.Add(_txtSecret, 1, 1);

        netTable.Controls.Add(new Label { Text = "本机虚拟 IP", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _txtVip = new TextBox { Dock = DockStyle.Fill };
        netTable.Controls.Add(_txtVip, 1, 2);

        var netBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var btnSave = MakeButton("保存并应用", 120, Color.FromArgb(16, 124, 16));
        btnSave.Click += async (_, _) => await SaveNetworkAsync();
        var btnReload = MakeButton("重新读取", 96, Color.FromArgb(90, 90, 90));
        btnReload.Click += (_, _) => LoadSettingsFromConfig();
        netBtns.Controls.AddRange(new Control[] { btnSave, btnReload });
        netTable.Controls.Add(netBtns, 1, 3);

        grpNet.Controls.Add(netTable);

        // --- Sunshine
        var grpSun = new GroupBox { Text = "Sunshine 串流服务", Dock = DockStyle.Top, Height = 186, Padding = new Padding(12, 8, 12, 8) };
        var sunTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        sunTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        sunTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        sunTable.Controls.Add(new Label { Text = "服务状态", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _vSunState = new Label { Text = "—", AutoSize = true, Anchor = AnchorStyles.Left };
        sunTable.Controls.Add(_vSunState, 1, 0);

        sunTable.Controls.Add(new Label { Text = "监听端口", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _vSunPorts = new Label { Text = "—", AutoSize = true, Anchor = AnchorStyles.Left };
        sunTable.Controls.Add(_vSunPorts, 1, 1);

        var sunBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _chkWan = new CheckBox
        {
            Text = "允许虚拟局域网访问 Web 控制台（远端配对必须）",
            AutoSize = true,
            Margin = new Padding(0, 8, 10, 0)
        };
        var btnWan = MakeButton("应用", 70, Color.FromArgb(0, 95, 184));
        btnWan.Click += async (_, _) => await ApplyWanAsync();
        var btnWeb = MakeButton("打开 Web 控制台", 130, Color.FromArgb(90, 90, 90));
        btnWeb.Click += (_, _) => OpenUrl("https://localhost:47990");
        sunBtns.Controls.AddRange(new Control[] { _chkWan, btnWan, btnWeb });
        sunTable.Controls.Add(sunBtns, 1, 2);

        grpSun.Controls.Add(sunTable);

        // --- 维护
        var grpMaint = new GroupBox { Text = "维护", Dock = DockStyle.Top, Height = 96, Padding = new Padding(12, 8, 12, 8) };
        var maintBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var btnOpenDir = MakeButton("打开配置目录", 120, Color.FromArgb(90, 90, 90));
        btnOpenDir.Click += (_, _) => OpenPath(Paths.Root);
        var btnOpenLogs = MakeButton("打开日志目录", 120, Color.FromArgb(90, 90, 90));
        btnOpenLogs.Click += (_, _) => OpenPath(Paths.LogDir);
        maintBar.Controls.AddRange(new Control[] { btnOpenDir, btnOpenLogs });

        grpMaint.Controls.Add(maintBar);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(grpNet, 0, 0);
        root.Controls.Add(grpSun, 0, 1);
        root.Controls.Add(grpMaint, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    // ---------------------------------------------------------------- 日志页
    private TabPage BuildLogTab()
    {
        var page = new TabPage("日志") { Padding = new Padding(10) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
        var btnRefreshLog = MakeButton("刷新", 80, Color.FromArgb(0, 95, 184));
        btnRefreshLog.Click += (_, _) => LoadLog();
        var btnOpenLog = MakeButton("用记事本打开", 130, Color.FromArgb(90, 90, 90));
        btnOpenLog.Click += (_, _) => OpenPath(Paths.MainLog);
        bar.Controls.AddRange(new Control[] { btnRefreshLog, btnOpenLog });

        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(250, 250, 250)
        };

        root.Controls.Add(bar, 0, 0);
        root.Controls.Add(_txtLog, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    // ================================================================ 刷新
    private sealed class Snap
    {
        public bool Running;
        public int Pid;
        public List<PeerInfo> Peers = new();
        public NodeSnapshot Node = new();

        public string SunState = "";
        public string Ports = "";
        public List<string> ConfiguredPeers = new();
        public bool Wan;
    }

    private async Task RefreshAllAsync(bool silent = false)
    {
        if (_demo)
        {
            ApplySnap(DemoSnap());
            if (!silent) SetStatus("演示模式：界面显示的是示例数据，不会连接或启动任何进程");
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            var snap = await Task.Run(() =>
            {
                var s = new Snap();
                s.Running = EasyTierService.IsCoreRunning();
                s.Pid = EasyTierService.CorePid();
                if (s.Running)
                {
                    s.Peers = EasyTierService.GetPeers();
                    s.Node = EasyTierService.GetNode();
                }

                s.SunState = EasyTierService.SunshineState();
                s.Wan = EasyTierService.SunshineAllowsWan();
                s.ConfiguredPeers = ConfigEditor.GetPeers(ConfigEditor.Read());
                s.Ports = string.Join("  ", new[] { 47984, 47989, 47990, 48010 }
                    .Select(p => $"{p}:{(EasyTierService.PortListening(p) ? "开" : "关")}"));
                return s;
            });

            ApplySnap(snap);
            if (!silent) SetStatus("状态已刷新 " + DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            SetStatus("刷新失败：" + ex.Message);
        }
        finally { _busy = false; }
    }

    private void ApplySnap(Snap s)
    {
        if (s.Running)
        {
            var pidText = s.Pid > 0 ? s.Pid.ToString() : "—";
            _banner.Text = $"●  EasyTier 运行中（PID {pidText}）    虚拟 IP {s.Node.VirtualIp}";
            _banner.BackColor = Color.FromArgb(223, 246, 221);
            _banner.ForeColor = Color.FromArgb(16, 100, 20);
        }
        else
        {
            _banner.Text = "●  EasyTier 已停止（远程串流不可用）";
            _banner.BackColor = Color.FromArgb(253, 231, 233);
            _banner.ForeColor = Color.FromArgb(150, 30, 30);
        }

        _vState.Text = s.Running ? "运行中" : "已停止";
        _vVip.Text = string.IsNullOrWhiteSpace(s.Node.VirtualIp) ? "—" : s.Node.VirtualIp;
        _vExit.Text = string.IsNullOrWhiteSpace(s.Node.PublicIp) ? "—" : s.Node.PublicIp;
        _vNat.Text = string.IsNullOrWhiteSpace(s.Node.NatType) ? "—" : s.Node.NatType;

        int online = s.Peers.Count(p => !p.IsSelf && !string.IsNullOrWhiteSpace(p.Hostname));
        _vOnline.Text = s.Running ? $"{online} 个已连接" : "—";
        _vTask.Text = "关闭（只能手动启动）";

        _vSun.Text = s.SunState;
        _vPorts.Text = s.Ports;

        // peer 表
        _gridPeers.Rows.Clear();
        foreach (var p in s.Peers)
        {
            var cost = p.Cost switch
            {
                "Local" => "本机",
                "p2p" => "P2P 直连",
                "relay" => "中继",
                _ => p.Cost
            };
            _gridPeers.Rows.Add(
                string.IsNullOrWhiteSpace(p.Hostname) ? "(公共节点)" : p.Hostname,
                p.Ipv4,
                cost,
                p.LatMs == "-" ? "—" : p.LatMs,
                p.LossRate == "-" ? "—" : p.LossRate,
                p.TunnelProto == "-" ? "—" : p.TunnelProto,
                p.NatType,
                p.Version);
        }

        if (_demo)
        {
            // 演示模式绝不读取真实配置，避免把网络名/密钥带进截图
            if (_txtNet.Text.Length == 0)
            {
                _txtNet.Text = "demo-network";
                _txtSecret.Text = "demo-secret";
                _txtVip.Text = "10.126.126.1";
            }
        }
        else if (_txtNet.Text == "" && _txtSecret.Text == "" && _txtVip.Text == "")
        {
            LoadSettingsFromConfig();
        }
        _vSunState.Text = s.SunState;
        _vSunPorts.Text = s.Ports;
        _chkWan.Checked = s.Wan;
    }

    private void LoadSettingsFromConfig()
    {
        var text = ConfigEditor.Read();
        _txtNet.Text = ConfigEditor.GetValue(text, "network_identity", "network_name");
        _txtSecret.Text = ConfigEditor.GetValue(text, "network_identity", "network_secret");
        _txtVip.Text = ConfigEditor.GetValue(text, null, "ipv4");
    }

    // ================================================================ 动作
    private async Task RunActionAsync(string status, Func<string> action, int wait)
    {
        if (GuardDemo()) return;
        SetBusy(true, status);
        try
        {
            var err = await Task.Run(action);
            if (!string.IsNullOrWhiteSpace(err)) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await Task.Delay(wait);
            await RefreshAllAsync();
        }
        finally { SetBusy(false); }
    }

    private async Task SaveNetworkAsync()
    {
        if (GuardDemo()) return;
        var net = _txtNet.Text.Trim();
        var sec = _txtSecret.Text.Trim();
        var vip = _txtVip.Text.Trim();
        if (net.Length == 0) { MessageBox.Show(this, "网络名称不能为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!Regex.IsMatch(vip, @"^\d+\.\d+\.\d+\.\d+(/\d+)?$"))
        {
            MessageBox.Show(this, "虚拟 IP 格式应为 10.126.126.1 这样。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "正在保存并重启 EasyTier…");
        try
        {
            var err = await Task.Run(() =>
            {
                var text = ConfigEditor.Read();
                text = ConfigEditor.SetValue(text, null, "ipv4", vip);
                text = ConfigEditor.SetValue(text, "network_identity", "network_name", net);
                text = ConfigEditor.SetValue(text, "network_identity", "network_secret", sec);
                File.WriteAllText(Paths.ConfigFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), ConfigEditor.Read(), Paths.Utf8NoBom);
                ConfigEditor.Write(text);
                return EasyTierService.RestartCore();
            });
            if (!string.IsNullOrWhiteSpace(err)) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await Task.Delay(5000);
            await RefreshAllAsync();
            SetStatus("网络设置已保存，EasyTier 已重启");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    private async Task ApplyWanAsync()
    {
        if (GuardDemo()) return;
        var want = _chkWan.Checked;
        SetBusy(true, "正在修改 Sunshine 配置并重启服务…");
        try
        {
            var err = await Task.Run(() => EasyTierService.SetSunshineWan(want));
            if (!string.IsNullOrWhiteSpace(err)) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else SetStatus("Sunshine 已重启，origin_web_ui_allowed = " + (want ? "wan" : "lan"));
            await RefreshAllAsync();
        }
        finally { SetBusy(false); }
    }

    // ---------------------------------------------------------------- 节点操作
    private async Task TestNodesAsync(bool all)
    {
        if (GuardDemo()) return;
        var targets = all
            ? _nodeBinding.ToList()
            : _gridNodes.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem as NodeEntry).Where(n => n != null).ToList();

        if (targets.Count == 0) { MessageBox.Show(this, "请先在列表里选中要测试的节点。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        _testCts = new CancellationTokenSource();
        SetBusy(true, "开始测试节点…");
        try
        {
            int i = 0;
            foreach (var node in targets)
            {
                i++;
                SetStatus($"正在测试 {i}/{targets.Count}：{node.Uri} …");
                var res = await EasyTierService.TestNodeAsync(node.Uri, _testCts.Token);
                node.LastResult = res.Ok ? "OK" : "FAIL";
                node.LastLatency = res.Ok ? (double.TryParse(res.Latency, out var d) && d > 0 ? res.Latency : "<1") : "";
                node.LastTestedAt = DateTime.Now.ToString("HH:mm:ss");
                _nodeBinding.ResetBindings();
            }
            NodeStore.Save(_nodeBinding);
            int ok = targets.Count(n => n.LastResult == "OK");
            SetStatus($"测试完成：{ok}/{targets.Count} 个节点可用");
        }
        finally
        {
            SetBusy(false);
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    private async Task ApplySelectedNodesAsync()
    {
        if (GuardDemo()) return;
        var picked = _gridNodes.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as NodeEntry).Where(n => n != null).ToList();
        if (picked.Count == 0) { MessageBox.Show(this, "请先选中要使用的节点（可按住 Ctrl 多选，多个节点互为备份）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var uris = picked.Select(n => n.Uri).ToList();
        var warn = picked.Any(n => n.LastResult == "FAIL") ? "\n\n注意：其中包含测试结果为「连不上」的节点。" : "";
        var ans = MessageBox.Show(this,
            "将把当前节点设置为：\n\n  " + string.Join("\n  ", uris) + warn +
            "\n\n写入 config.toml 并重启 EasyTier，确定吗？",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (ans != DialogResult.OK) return;

        SetBusy(true, "正在切换节点…");
        try
        {
            var err = await Task.Run(() =>
            {
                var old = ConfigEditor.Read();
                File.WriteAllText(Paths.ConfigFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), old, Paths.Utf8NoBom);
                ConfigEditor.Write(ConfigEditor.SetPeers(old, uris));
                var e1 = EasyTierService.RestartCore();
                Thread.Sleep(9000);
                if (!EasyTierService.IsCoreRunning())
                {
                    // 起不来就回滚，避免把远程通路弄断
                    var baks = Directory.GetFiles(Paths.Root, "config.toml.bak-*").OrderByDescending(f => f).ToList();
                    if (baks.Count > 0) File.Copy(baks[0], Paths.ConfigFile, true);
                    EasyTierService.RestartCore();
                    return "新节点配置启动失败，已回滚到上一份配置。请先「测试所选」确认节点可用。";
                }
                return e1;
            });
            if (!string.IsNullOrWhiteSpace(err)) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshAllAsync();
            SetStatus("节点已切换：" + string.Join(" , ", uris));
        }
        finally { SetBusy(false); }
    }

    private void AddNodeDialog()
    {
        using var dlg = new InputDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var uri = dlg.Uri.Trim();
        if (!Regex.IsMatch(uri, @"^(tcp|udp|quic|ws|wss|wg|faketcp)://"))
        {
            MessageBox.Show(this, "地址需要以 tcp:// udp:// quic:// 等开头。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_nodeBinding.Any(n => string.Equals(n.Uri, uri, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "这个地址已经在列表里了。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _nodeBinding.Add(new NodeEntry
        {
            Name = string.IsNullOrWhiteSpace(dlg.NodeName) ? uri : dlg.NodeName.Trim(),
            Uri = uri,
            Source = "手动"
        });
        NodeStore.Save(_nodeBinding);
        SetStatus("已添加节点：" + uri);
    }

    private void DeleteSelected()
    {
        var picked = _gridNodes.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as NodeEntry).Where(n => n != null && n.Source != "内置").ToList();
        if (picked.Count == 0)
        {
            MessageBox.Show(this, "请选中「手动/网络」来源的节点（内置节点不会被删除，可用「重置内置」恢复）。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var n in picked) _nodeBinding.Remove(n);
        NodeStore.Save(_nodeBinding);
        SetStatus($"已删除 {picked.Count} 个节点");
    }

    private void ResetBuiltIn()
    {
        var keepCustom = _nodeBinding.Where(n => n.Source != "内置").ToList();
        var merged = NodeStore.Defaults();
        foreach (var c in keepCustom) if (!merged.Any(m => m.Uri == c.Uri)) merged.Add(c);
        _nodeBinding = new BindingList<NodeEntry>(merged);
        _gridNodes.DataSource = _nodeBinding;
        NodeStore.Save(_nodeBinding);
        SetStatus("已恢复内置节点列表");
    }

    private async Task UpdateFromNetworkAsync()
    {
        if (GuardDemo()) return;
        SetBusy(true, "正在从网络获取节点列表…");
        try
        {
            string detail = "";
            var found = await Task.Run(() => FetchNodesFromWeb(out detail));
            if (found.Count == 0)
            {
                MessageBox.Show(this,
                    "没能从网络拿到节点地址。\n\n可能原因：官方状态页 uptime.easytier.cn 当前无法解析/被网络拦截，" +
                    "或页面结构调整。\n可以在社区帖里复制节点地址，用「添加节点」手动加。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("网络更新失败：" + detail);
                return;
            }

            int added = 0;
            foreach (var uri in found)
            {
                if (_nodeBinding.Any(n => string.Equals(n.Uri, uri, StringComparison.OrdinalIgnoreCase))) continue;
                _nodeBinding.Add(new NodeEntry { Name = uri.Replace("://", " · "), Uri = uri, Source = "网络" });
                added++;
            }
            NodeStore.Save(_nodeBinding);
            SetStatus($"从网络获取到 {found.Count} 个地址，新增 {added} 个");
            MessageBox.Show(this, $"获取到 {found.Count} 个节点地址，其中新增 {added} 个。\n\n建议先「一键测速」再切换。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { SetBusy(false); }
    }

    private static List<string> FetchNodesFromWeb(out string detail)
    {
        var urls = new[]
        {
            "https://uptime.easytier.cn/api/status-page/default",
            "https://uptime.easytier.cn/",
            "https://easytier.cn/guide/network/quick-networking.html"
        };
        var result = new List<string>();
        var errors = new List<string>();

        foreach (var url in urls)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 EasyTierManager");
                var body = http.GetStringAsync(url).GetAwaiter().GetResult();

                var matches = Regex.Matches(body, @"(?:tcp|udp|quic|wss?|wg|faketcp)://[A-Za-z0-9\.\-]+:\d{2,5}");
                foreach (Match m in matches)
                {
                    var v = m.Value.Trim();
                    if (!result.Contains(v, StringComparer.OrdinalIgnoreCase)) result.Add(v);
                }
                if (result.Count > 0) { detail = "来源 " + url; return result; }
                errors.Add(url + " 未发现节点");
            }
            catch (Exception ex) { errors.Add(url + " → " + ex.Message); }
        }
        detail = string.Join(" | ", errors);
        return result;
    }

    // ---------------------------------------------------------------- 杂项
    private void SetBusy(bool busy, string status = null)
    {
        _busy = busy;
        _btnStart.Enabled = !busy;
        _btnStop.Enabled = !busy;
        _btnRestart.Enabled = !busy;
        _btnRefresh.Enabled = !busy;
        _timer.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (!string.IsNullOrEmpty(status)) SetStatus(status);
    }

    private void SetStatus(string msg) => _status.Text = msg;

    private void LoadLog()
    {
        _txtLog.Text = EasyTierService.TailLog(400);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MessageBox.Show(this, "找不到：" + path, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus("打开失败：" + ex.Message); }
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("打开链接失败：" + ex.Message); }
    }

    // ================================================================ 演示模式（--demo，仅用于截图/演示）
    private bool GuardDemo()
    {
        if (!_demo) return false;
        MessageBox.Show(this, "当前是演示模式（--demo），不会执行任何实际操作。", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    private static List<NodeEntry> DemoNodes() => new()
    {
        new NodeEntry { Name = "国际公益节点 · 东京（TCP）", Uri = "tcp://relay.example.com:11010", Source = "内置", LastResult = "OK", LastLatency = "42.6", LastTestedAt = "10:12:03" },
        new NodeEntry { Name = "国际公益节点 · 东京（UDP）", Uri = "udp://relay.example.com:11010", Source = "内置", LastResult = "OK", LastLatency = "45.1", LastTestedAt = "10:12:07" },
        new NodeEntry { Name = "社区节点 · 新加坡", Uri = "tcp://sg.example.org:11010", Source = "网络", LastResult = "OK", LastLatency = "88.4", LastTestedAt = "10:12:21" },
        new NodeEntry { Name = "社区节点 · 法兰克福", Uri = "tcp://eu.example.net:11010", Source = "网络", LastResult = "FAIL", LastLatency = "", LastTestedAt = "10:12:35" },
        new NodeEntry { Name = "自建中继（VPS）", Uri = "tcp://my-vps.example.io:11010", Source = "手动", LastResult = "OK", LastLatency = "23.9", LastTestedAt = "10:12:49" },
    };

    private static Snap DemoSnap() => new()
    {
        Running = true,
        Pid = 4242,
        SunState = "运行中",
        Ports = "47984:开  47989:开  47990:开  48010:开",
        Wan = true,
        ConfiguredPeers = new List<string> { "tcp://relay.example.com:11010", "udp://relay.example.com:11010" },
        Node = new NodeSnapshot
        {
            VirtualIp = "10.126.126.1/24",
            Hostname = "DESKTOP-DEMO",
            PublicIp = "203.0.113.7",
            NatType = "PortRestricted"
        },
        Peers = new List<PeerInfo>
        {
            new() { Cidr = "10.126.126.1/24", Ipv4 = "10.126.126.1", Hostname = "DESKTOP-DEMO", Cost = "Local", LatMs = "-", LossRate = "-", TunnelProto = "-", NatType = "PortRestricted", Version = "2.6.4" },
            new() { Hostname = "PublicServer_TOKYO-RELAY", Cost = "p2p", LatMs = "42.63", LossRate = "0.0%", TunnelProto = "tcp,udp", NatType = "FullCone", Version = "2.6.4" },
            new() { Cidr = "10.126.126.2/24", Ipv4 = "10.126.126.2", Hostname = "MOONLIGHT-CLIENT", Cost = "p2p", LatMs = "18.31", LossRate = "0.0%", TunnelProto = "udp", NatType = "PortRestricted", Version = "2.6.4" },
        }
    };
}

/// <summary>简单的“名称 + 地址”输入框。</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _uri = new() { Dock = DockStyle.Fill, Text = "tcp://" };

    public string NodeName => _name.Text;
    public string Uri => _uri.Text;

    public InputDialog()
    {
        Text = "添加节点";
        ClientSize = new Size(460, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var tlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        tlp.Controls.Add(new Label { Text = "名称", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        tlp.Controls.Add(_name, 1, 0);
        tlp.Controls.Add(new Label { Text = "地址", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        tlp.Controls.Add(_uri, 1, 1);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "确定", Width = 84, Height = 30, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
        bar.Controls.AddRange(new Control[] { ok, cancel });
        tlp.Controls.Add(bar, 1, 2);

        Controls.Add(tlp);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
