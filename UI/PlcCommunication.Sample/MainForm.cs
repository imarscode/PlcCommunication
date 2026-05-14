#pragma warning disable CS8618, CS0169, CS8632, CS8602, CS8600
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PlcCommunication.Core;
using PlcCommunication.Diagnostics;
using PlcCommunication.Protocols.Modbus;
using PlcCommunication.Protocols.Siemens;
using PlcCommunication.Protocols.Mitsubishi;
using PlcCommunication.Protocols.Omron;
using PlcCommunication.Protocols.AllenBradley;
using PlcCommunication.Simulation;

namespace PlcCommunication.Sample
{
    public partial class MainForm : Form
    {
        // ========== 模拟器 ==========
        private MitsubishiMcSimulator? _mitsubishiSim;
        private SiemensS7Simulator? _siemensSim;
        private ModbusTcpSimulator? _modbusSim;
        private bool _simRunning;

        // ========== 菜单 ==========
        private MenuStrip _menuStrip;
        private ToolStripMenuItem _menuSimulator;
        // ========== 连接控件 ==========
        private ComboBox _cmbProtocol;
        private TextBox _txtIp;
        private NumericUpDown _numPort;
        private Button _btnConnect;
        private Panel _pnlStatusDot;
        private Label _lblStatusText;
        private Panel _pnlParams;
        private Label _lblStationId; private NumericUpDown _numStationId;
        private Label _lblPlcType; private ComboBox _cmbPlcType;
        private Label _lblLocalTsap, _lblRemoteTsap;
        private TextBox _txtLocalTsap, _txtRemoteTsap;
        private Label _lblDstNode, _lblSrcNode;
        private NumericUpDown _numDstNode, _numSrcNode;
        private Label _lblUnitNo; private TextBox _txtUnitNo;
        private Label _lblSlot; private NumericUpDown _numSlot;

        // ========== 读写控件 ==========
        private TextBox _txtAddress;
        private NumericUpDown _numLength;
        private Button _btnRead, _btnWrite, _btnReadBool, _btnWriteBool;
        private TextBox _txtWriteValue;
        private ComboBox _cmbWriteType;
        private CheckBox _chkBoolValue;
        private Label _lblAddrHint;

        // ========== 结果控件 ==========
        private TextBox _txtHexResult;
        private Label _lblInt16, _lblUInt16, _lblInt32, _lblUInt32;
        private Label _lblInt64, _lblUInt64;
        private Label _lblFloat, _lblDouble, _lblBool, _lblString;
        private Label _lblElapsed;

        // ========== 日志控件 ==========
        private RichTextBox _rtbLog;
        private CheckBox _chkAutoScroll;

        // ========== 状态栏 ==========
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _lblStatusLeft, _lblStatusRight;

        // ========== TabControl ==========
        private TabControl _tabMain;

        // ========== 监视表控件 ==========
        private DataGridView _dgvMonitor;
        private Button _btnMonitorAdd, _btnMonitorRemove, _btnMonitorReadOnce, _btnMonitorToggle;
        private NumericUpDown _numMonitorInterval;
        private System.Windows.Forms.Timer? _monitorTimer;
        private bool _monitorRunning;
        private int _monitorTickCount;

        // ========== 批量读写控件 ==========
        private TextBox _txtBatchAddresses;
        private TextBox _txtBatchResult;
        private Button _btnBatchRead, _btnBatchWrite, _btnBatchClear;
        private ComboBox _cmbBatchType;

        // ========== 书签控件 ==========
        private ListBox _lstBookmarks;
        private Button _btnBookmarkAdd, _btnBookmarkRemove, _btnBookmarkUse;
        private TextBox _txtBookmarkName;

        // ========== 设备与状态 ==========
        private IReadWriteNet? _device;
        private NetworkDeviceBase? _deviceBase;
        private bool _connected;

        // ========== 配置持久化 ==========
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlcCommunication", "config.json");

        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            LogManager.AddSink(e => { if (!_rtbLog.IsDisposed) _rtbLog.Invoke(() => AppendLog(e)); });
            btnState();
            LoadConfig();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Text = "PlcCommunication - PLC 通信调试工具 v2.0";
            Size = new Size(1200, 820);
            MinimumSize = new Size(960, 640);
            BackColor = Color.FromArgb(240, 242, 247);
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            
            // 创建菜单
            _menuStrip = new MenuStrip();
            _menuSimulator = new ToolStripMenuItem("模拟器");
            _menuSimulator.DropDownItems.Add("启动三菱MC模拟器 (端口5006)", null, (s, e) => ToggleSimulator("mitsubishi"));
            _menuSimulator.DropDownItems.Add("启动西门子S7模拟器 (端口102)", null, (s, e) => ToggleSimulator("siemens"));
            _menuSimulator.DropDownItems.Add("启动Modbus TCP模拟器 (端口502)", null, (s, e) => ToggleSimulator("modbus"));
            _menuSimulator.DropDownItems.Add(new ToolStripSeparator());
            _menuSimulator.DropDownItems.Add("停止所有模拟器", null, (s, e) => StopAllSimulators());
            _menuStrip.Items.Add(_menuSimulator);
            Controls.Add(_menuStrip);
            MainMenuStrip = _menuStrip;
            
            ResumeLayout(false);
            PerformLayout();
        }

        // =====================================================================
        // UI 构建
        // =====================================================================

        private void BuildUI()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 第0行：标题
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 99, 235) };
            header.Controls.Add(new Label
            {
                Text = "⚡ PlcCommunication - PLC 通信调试工具 v2.0",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 8),
                AutoSize = true
            });
            // 右侧作者信息
            var lblAuthor = new Label
            {
                Text = "作者：ljh | QQ：3010812967 | Email: usmars@qq.com | MIT License",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(200, 220, 255),
                Location = new Point(380, 14),
                AutoSize = true
            };
            header.Controls.Add(lblAuthor);
            layout.Controls.Add(header, 0, 0);

            // 第1行：连接
            var connPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10, 6, 10, 4) };

            // 第一行：协议+IP+端口+连接
            var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, WrapContents = false };
            row1.Controls.Add(MkLabel("协议：", 40));
            _cmbProtocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Size = new Size(130, 24) };
            _cmbProtocol.Items.AddRange(new[] { "Modbus TCP", "Modbus RTU", "西门子 S7", "三菱 MC", "欧姆龙 FINS", "欧姆龙 HostLink", "罗克韦尔 CIP" });
            _cmbProtocol.SelectedIndexChanged += OnProtocolChanged;
            row1.Controls.Add(_cmbProtocol);
            row1.Controls.Add(MkLabel("IP：", 28, 10));
            _txtIp = new TextBox { Text = "127.0.0.1", Size = new Size(112, 24), BorderStyle = BorderStyle.FixedSingle };
            row1.Controls.Add(_txtIp);
            row1.Controls.Add(MkLabel("端口：", 38, 6));
            _numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 502, Size = new Size(66, 24), BorderStyle = BorderStyle.FixedSingle };
            row1.Controls.Add(_numPort);
            _btnConnect = new Button
            {
                Text = "连接", BackColor = Color.FromArgb(22, 163, 74), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }, Size = new Size(74, 26), Cursor = Cursors.Hand, Margin = new Padding(10, 2, 0, 0)
            };
            _btnConnect.Click += BtnConnect_Click;
            row1.Controls.Add(_btnConnect);
            _pnlStatusDot = new Panel { Size = new Size(12, 12), BackColor = Color.Gray, Margin = new Padding(8, 6, 0, 0) };
            _pnlStatusDot.Paint += StatusDot_Paint;
            row1.Controls.Add(_pnlStatusDot);
            _lblStatusText = new Label { Text = "未连接", ForeColor = Color.FromArgb(100, 116, 139), Size = new Size(60, 22), TextAlign = ContentAlignment.MiddleLeft };
            row1.Controls.Add(_lblStatusText);
            connPanel.Controls.Add(row1);

            // 第二行：协议参数
            _pnlParams = new Panel { Dock = DockStyle.Top, Height = 28 };
            BuildParams();
            connPanel.Controls.Add(_pnlParams);

            // 第三行：地址提示
            _lblAddrHint = new Label
            {
                Dock = DockStyle.Bottom, Height = 20,
                ForeColor = Color.FromArgb(59, 130, 246),
                Font = new Font("Segoe UI", 8F),
                Text = "💡 地址格式：0, 1, 2 ... (寄存器地址)"
            };
            connPanel.Controls.Add(_lblAddrHint);

            layout.Controls.Add(connPanel, 0, 1);

            // 第2行：Tab内容区
            _tabMain = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };
            _tabMain.Padding = new Point(12, 4);

            var tabReadWrite = new TabPage("📝 读写操作") { BackColor = Color.White };
            var tabMonitor = new TabPage("📊 数据监视") { BackColor = Color.White };
            var tabBatch = new TabPage("📋 批量读写") { BackColor = Color.White };
            var tabBookmark = new TabPage("⭐ 地址书签") { BackColor = Color.White };
            var tabLog = new TabPage("📜 日志输出") { BackColor = Color.FromArgb(15, 23, 42) };

            BuildReadWriteTab(tabReadWrite);
            BuildMonitorTab(tabMonitor);
            BuildBatchTab(tabBatch);
            BuildBookmarkTab(tabBookmark);
            BuildLogTab(tabLog);

            _tabMain.TabPages.AddRange(new[] { tabReadWrite, tabMonitor, tabBatch, tabBookmark, tabLog });
            layout.Controls.Add(_tabMain, 0, 2);

            Controls.Add(layout);

            // 状态栏
            _statusStrip = new StatusStrip { BackColor = Color.FromArgb(248, 250, 252), ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 8.5F), SizingGrip = false };
            _lblStatusLeft = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _lblStatusRight = new ToolStripStatusLabel("PLC 通信调试工具 v2.0");
            _statusStrip.Items.Add(_lblStatusLeft);
            _statusStrip.Items.Add(_lblStatusRight);
            Controls.Add(_statusStrip);

            _cmbProtocol.SelectedIndex = 0;
            KeyDown += OnFormKeyDown;
        }

        private static Label MkLabel(string text, int width, int leftMargin = 0) => new Label
        {
            Text = text, ForeColor = Color.FromArgb(100, 116, 139),
            Size = new Size(width, 22), TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(leftMargin, 0, 0, 0)
        };

        // =====================================================================
        // Tab: 读写操作
        // =====================================================================

        private void BuildReadWriteTab(TabPage tab)
        {
            var cp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(4) };
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));

            // 左侧：读写操作
            var leftGb = new GroupBox { Dock = DockStyle.Fill, Text = " 读写操作 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var leftPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

            int y = 6;
            leftPnl.Controls.Add(new Label { Text = "地址：", Location = new Point(4, y + 4), AutoSize = true });
            _txtAddress = new TextBox { Text = "0", Location = new Point(52, y), Size = new Size(140, 24), BorderStyle = BorderStyle.FixedSingle };
            leftPnl.Controls.Add(_txtAddress);

            leftPnl.Controls.Add(new Label { Text = "长度：", Location = new Point(204, y + 4), AutoSize = true });
            _numLength = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 4, Location = new Point(244, y), Size = new Size(66, 24), BorderStyle = BorderStyle.FixedSingle };
            leftPnl.Controls.Add(_numLength);

            _btnRead = new Button
            {
                Text = "📖 读取", Location = new Point(324, y - 2), Size = new Size(78, 28),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand
            };
            _btnRead.Click += BtnRead_Click;
            leftPnl.Controls.Add(_btnRead);

            _btnReadBool = new Button
            {
                Text = "🔵 Bool", Location = new Point(410, y - 2), Size = new Size(72, 28),
                FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Color.FromArgb(203, 213, 225), BorderSize = 1 }, Cursor = Cursors.Hand
            };
            _btnReadBool.Click += BtnReadBool_Click;
            leftPnl.Controls.Add(_btnReadBool);

            y += 38;
            leftPnl.Controls.Add(new Panel { Location = new Point(2, y), Width = 480, Height = 1, BackColor = Color.FromArgb(226, 232, 240) });

            y += 10;
            leftPnl.Controls.Add(new Label { Text = "写入值：", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(4, y + 4), AutoSize = true });
            y += 22;
            _txtWriteValue = new TextBox { Text = "00 01 00 02", Location = new Point(4, y), Size = new Size(140, 24), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9F) };
            leftPnl.Controls.Add(_txtWriteValue);

            _cmbWriteType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Location = new Point(152, y), Size = new Size(90, 24) };
            _cmbWriteType.Items.AddRange(new[] { "十六进制", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String", "Bool" });
            _cmbWriteType.SelectedIndex = 0;
            _cmbWriteType.SelectedIndexChanged += OnWriteTypeChanged;
            leftPnl.Controls.Add(_cmbWriteType);

            _chkBoolValue = new CheckBox { Text = "True", Checked = true, Visible = false, Location = new Point(152, y), AutoSize = true };
            _chkBoolValue.CheckedChanged += OnBoolValueChanged;
            leftPnl.Controls.Add(_chkBoolValue);

            y += 32;
            _btnWrite = new Button
            {
                Text = "✏️ 写入", Location = new Point(4, y), Size = new Size(78, 28),
                BackColor = Color.FromArgb(220, 38, 38), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand
            };
            _btnWrite.Click += BtnWrite_Click;
            leftPnl.Controls.Add(_btnWrite);

            _btnWriteBool = new Button
            {
                Text = "🔵 Bool", Location = new Point(90, y), Size = new Size(72, 28),
                FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Color.FromArgb(203, 213, 225), BorderSize = 1 }, Cursor = Cursors.Hand
            };
            _btnWriteBool.Click += BtnWriteBool_Click;
            leftPnl.Controls.Add(_btnWriteBool);

            // 快捷地址按钮
            y += 38;
            leftPnl.Controls.Add(new Label { Text = "快捷地址：", ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(4, y + 4), AutoSize = true });
            y += 20;
            string[] quickAddr = { "D0", "D100", "M0", "DB1.DBW0", "40001", "0" };
            for (int i = 0; i < quickAddr.Length; i++)
            {
                var qb = new Button
                {
                    Text = quickAddr[i], Location = new Point(4 + i * 68, y), Size = new Size(62, 22),
                    FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 8F),
                    FlatAppearance = { BorderColor = Color.FromArgb(203, 213, 225), BorderSize = 1 },
                    Cursor = Cursors.Hand, Tag = quickAddr[i]
                };
                qb.Click += (s, e) => { if (s is Button b) _txtAddress.Text = b.Tag?.ToString() ?? ""; };
                leftPnl.Controls.Add(qb);
            }

            y += 30;
            leftPnl.Controls.Add(new Label
            {
                Text = "快捷键：Ctrl+R 读取 | Ctrl+W 写入 | Ctrl+D 断开",
                ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI", 7.5F),
                Location = new Point(4, y), AutoSize = true
            });

            leftGb.Controls.Add(leftPnl);
            cp.Controls.Add(leftGb, 0, 0);

            // 右侧：读取结果
            var rightGb = new GroupBox { Dock = DockStyle.Fill, Text = " 读取结果 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var rightPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

            y = 6;
            rightPnl.Controls.Add(new Label { Text = "十六进制：", Location = new Point(4, y + 2), AutoSize = true });
            _txtHexResult = new TextBox
            {
                ReadOnly = true, Location = new Point(4, y + 18), Width = 520, Height = 24,
                Font = new Font("Consolas", 10F), BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            rightPnl.Controls.Add(_txtHexResult);

            y += 50;
            int cw = 108, g = 4;
            _lblInt16 = VLabel("Int16", 4, y, cw);
            _lblUInt16 = VLabel("UInt16", 4 + cw + g, y, cw);
            _lblInt32 = VLabel("Int32", 4 + (cw + g) * 2, y, cw);
            _lblUInt32 = VLabel("UInt32", 4 + (cw + g) * 3, y, cw);
            rightPnl.Controls.AddRange(new[] { _lblInt16, _lblUInt16, _lblInt32, _lblUInt32 });

            y += 22;
            _lblInt64 = VLabel("Int64", 4, y, cw);
            _lblUInt64 = VLabel("UInt64", 4 + cw + g, y, cw);
            _lblFloat = VLabel("Float", 4 + (cw + g) * 2, y, cw);
            _lblDouble = VLabel("Double", 4 + (cw + g) * 3, y, cw);
            rightPnl.Controls.AddRange(new[] { _lblInt64, _lblUInt64, _lblFloat, _lblDouble });

            y += 22;
            _lblBool = VLabel("Bool", 4, y, cw);
            _lblString = VLabel("String", 4 + cw + g, y, cw + 40);
            _lblElapsed = new Label { Text = "耗时：--", ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 8.5F), Location = new Point(4 + (cw + g) * 2, y + 2), AutoSize = true };
            rightPnl.Controls.AddRange(new[] { _lblBool, _lblString, _lblElapsed });

            // 字节可视化
            y += 30;
            rightPnl.Controls.Add(new Label { Text = "字节视图：", Location = new Point(4, y), AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139) });
            y += 18;
            var lblByteHint = new Label
            {
                Text = "(读取数据后在此显示每个字节的偏移和值)",
                Location = new Point(4, y), AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI", 8F)
            };
            rightPnl.Controls.Add(lblByteHint);

            rightGb.Controls.Add(rightPnl);
            cp.Controls.Add(rightGb, 1, 0);

            tab.Controls.Add(cp);
        }

        // =====================================================================
        // Tab: 数据监视
        // =====================================================================

        private void BuildMonitorTab(TabPage tab)
        {
            var mainPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            // 工具栏
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, BackColor = Color.White };

            toolbar.Controls.Add(MkLabel("轮询间隔：", 68));
            _numMonitorInterval = new NumericUpDown { Minimum = 100, Maximum = 60000, Value = 1000, Increment = 100, Size = new Size(72, 24), BorderStyle = BorderStyle.FixedSingle };
            toolbar.Controls.Add(_numMonitorInterval);
            toolbar.Controls.Add(MkLabel("ms", 24));

            _btnMonitorAdd = new Button { Text = "➕ 添加", Size = new Size(70, 26), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(203, 213, 225) }, Cursor = Cursors.Hand };
            _btnMonitorAdd.Click += BtnMonitorAdd_Click;
            toolbar.Controls.Add(_btnMonitorAdd);

            _btnMonitorRemove = new Button { Text = "➖ 移除", Size = new Size(70, 26), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(203, 213, 225) }, Cursor = Cursors.Hand };
            _btnMonitorRemove.Click += BtnMonitorRemove_Click;
            toolbar.Controls.Add(_btnMonitorRemove);

            _btnMonitorReadOnce = new Button { Text = "📊 单次读取", Size = new Size(88, 26), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand };
            _btnMonitorReadOnce.Click += BtnMonitorReadOnce_Click;
            toolbar.Controls.Add(_btnMonitorReadOnce);

            _btnMonitorToggle = new Button { Text = "▶ 开始监视", Size = new Size(92, 26), BackColor = Color.FromArgb(22, 163, 74), ForeColor = Color.White, Font = new Font("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand };
            _btnMonitorToggle.Click += BtnMonitorToggle_Click;
            toolbar.Controls.Add(_btnMonitorToggle);

            mainPnl.Controls.Add(toolbar);

            // DataGridView
            _dgvMonitor = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(226, 232, 240),
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                Font = new Font("Segoe UI", 9F),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _dgvMonitor.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };

            _dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", FillWeight = 20, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.FromArgb(30, 41, 59) } });
            _dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "地址", FillWeight = 20, DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Consolas", 9F) } });
            _dgvMonitor.Columns.Add(new DataGridViewComboBoxColumn { Name = "DataType", HeaderText = "数据类型", FillWeight = 15, Items = { "Hex", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "Bool", "String" } });
            _dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn { Name = "Length", HeaderText = "长度", FillWeight = 8, DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Consolas", 9F) } });
            _dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "当前值", FillWeight = 25, DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Consolas", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(37, 99, 235), BackColor = Color.FromArgb(248, 250, 252) } });
            _dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastUpdate", HeaderText = "更新时间", FillWeight = 12, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 8F) } });

            // 默认行
            _dgvMonitor.Rows.Add("温度", "D100", "Float", "2", "--", "--");
            _dgvMonitor.Rows.Add("压力", "D102", "Float", "2", "--", "--");
            _dgvMonitor.Rows.Add("运行状态", "M0", "Bool", "1", "--", "--");

            mainPnl.Controls.Add(_dgvMonitor);
            tab.Controls.Add(mainPnl);
        }

        // =====================================================================
        // Tab: 批量读写
        // =====================================================================

        private void BuildBatchTab(TabPage tab)
        {
            var cp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(4) };
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // 左侧：批量读取
            var readGb = new GroupBox { Dock = DockStyle.Fill, Text = " 批量读取 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var readPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            readPnl.Controls.Add(new Label { Text = "每行一个地址，格式：地址[ 长度]", ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(4, 4), AutoSize = true });
            _txtBatchAddresses = new TextBox
            {
                Location = new Point(4, 22), Size = new Size(440, 180),
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.FixedSingle,
                Text = "D100 2\nD102 2\nM0 1"
            };
            readPnl.Controls.Add(_txtBatchAddresses);

            _cmbBatchType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(4, 210), Size = new Size(90, 24), FlatStyle = FlatStyle.Flat };
            _cmbBatchType.Items.AddRange(new[] { "Hex", "Int16", "UInt16", "Int32", "Float", "Double", "Bool", "String" });
            _cmbBatchType.SelectedIndex = 0;
            readPnl.Controls.Add(_cmbBatchType);

            _btnBatchRead = new Button
            {
                Text = "📖 批量读取", Location = new Point(104, 208), Size = new Size(100, 28),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand
            };
            _btnBatchRead.Click += BtnBatchRead_Click;
            readPnl.Controls.Add(_btnBatchRead);

            _btnBatchClear = new Button { Text = "清空", Location = new Point(214, 208), Size = new Size(52, 28), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(203, 213, 225) }, Cursor = Cursors.Hand };
            _btnBatchClear.Click += (s, e) => _txtBatchResult.Clear();
            readPnl.Controls.Add(_btnBatchClear);

            readGb.Controls.Add(readPnl);
            cp.Controls.Add(readGb, 0, 0);

            // 右侧：批量读取结果
            var resultGb = new GroupBox { Dock = DockStyle.Fill, Text = " 批量结果 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtBatchResult = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9.5F), BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.None, ReadOnly = true,
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            resultGb.Controls.Add(_txtBatchResult);
            cp.Controls.Add(resultGb, 1, 0);

            tab.Controls.Add(cp);
        }

        // =====================================================================
        // Tab: 地址书签
        // =====================================================================

        private void BuildBookmarkTab(TabPage tab)
        {
            var cp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(4) };
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            cp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

            // 左侧：书签列表
            var listGb = new GroupBox { Dock = DockStyle.Fill, Text = " 已保存书签 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var listPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            _lstBookmarks = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            listPnl.Controls.Add(_lstBookmarks);

            // 按钮行
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, WrapContents = true };

            _txtBookmarkName = new TextBox { Size = new Size(120, 24), BorderStyle = BorderStyle.FixedSingle, Text = "书签名称" };
            btnRow.Controls.Add(_txtBookmarkName);

            _btnBookmarkAdd = new Button { Text = "➕ 添加当前", Size = new Size(82, 26), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(203, 213, 225) }, Cursor = Cursors.Hand };
            _btnBookmarkAdd.Click += BtnBookmarkAdd_Click;
            btnRow.Controls.Add(_btnBookmarkAdd);

            _btnBookmarkRemove = new Button { Text = "🗑️ 删除", Size = new Size(66, 26), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(239, 68, 68) }, Cursor = Cursors.Hand };
            _btnBookmarkRemove.Click += BtnBookmarkRemove_Click;
            btnRow.Controls.Add(_btnBookmarkRemove);

            _btnBookmarkUse = new Button { Text = "📖 使用", Size = new Size(66, 26), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand };
            _btnBookmarkUse.Click += BtnBookmarkUse_Click;
            btnRow.Controls.Add(_btnBookmarkUse);

            listPnl.Controls.Add(btnRow);
            listGb.Controls.Add(listPnl);
            cp.Controls.Add(listGb, 0, 0);

            // 右侧：说明
            var helpGb = new GroupBox { Dock = DockStyle.Fill, Text = " 地址格式参考 ", Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var helpPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };

            var helpText = new RichTextBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F), ReadOnly = true,
                Rtf = BuildAddressHelpRtf()
            };
            helpPnl.Controls.Add(helpText);
            helpGb.Controls.Add(helpPnl);
            cp.Controls.Add(helpGb, 1, 0);

            tab.Controls.Add(cp);
        }

        private static string BuildAddressHelpRtf()
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"{\rtf1\ansi");
            sb.AppendLine(@"{\colortbl;\red37\green99\blue235;\red30\green41\blue59;\red100\green116\blue139;}");
            sb.AppendLine(@"\cf1\b\f0 Modbus TCP/RTU\cf2\b0\par");
            sb.AppendLine(@"\cf3 地址：0, 1, 2... (寄存器偏移)\par");
            sb.AppendLine(@"线圈：地址0~65535\par");
            sb.AppendLine(@"保持寄存器：40001~49999\par");
            sb.AppendLine(@"\par");
            sb.AppendLine(@"\cf1\b 西门子 S7\cf2\b0\par");
            sb.AppendLine(@"\cf3 DB1.DBW0 — DB块字\par");
            sb.AppendLine(@"DB1.DBD0 — DB块双字\par");
            sb.AppendLine(@"DB1.DBX0.0 — DB块位\par");
            sb.AppendLine(@"MW0, MD0, M0.0 — M区\par");
            sb.AppendLine(@"IW0, QW0 — I/Q区\par");
            sb.AppendLine(@"\par");
            sb.AppendLine(@"\cf1\b 三菱 MC\cf2\b0\par");
            sb.AppendLine(@"\cf3 D0, D100 — 数据寄存器\par");
            sb.AppendLine(@"M0, M100 — 内部继电器\par");
            sb.AppendLine(@"X0, Y0 — 输入/输出(16进制)\par");
            sb.AppendLine(@"M10.3 — 位偏移\par");
            sb.AppendLine(@"\par");
            sb.AppendLine(@"\cf1\b 欧姆龙 FINS\cf2\b0\par");
            sb.AppendLine(@"\cf3 D100, DM100 — DM区\par");
            sb.AppendLine(@"CIO100 — CIO区\par");
            sb.AppendLine(@"W100, WR100 — 工作区\par");
            sb.AppendLine(@"H100, HR100 — 保持区\par");
            sb.AppendLine(@"D100.05 — 位寻址\par");
            sb.AppendLine(@"\par");
            sb.AppendLine(@"\cf1\b 罗克韦尔 CIP\cf2\b0\par");
            sb.AppendLine(@"\cf3 标签名直接使用\par");
            sb.AppendLine(@"例：MyTag, MyTag[0]\par");
            sb.AppendLine(@"MyTag.Member\par");
            sb.AppendLine(@"}");
            return sb.ToString();
        }

        // =====================================================================
        // Tab: 日志输出
        // =====================================================================

        private void BuildLogTab(TabPage tab)
        {
            var logPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4) };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 28 };
            var tb2 = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), WrapContents = false };
            tb2.Controls.Add(new Label { Text = "日志输出", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true });
            _chkAutoScroll = new CheckBox { Text = "自动滚动", Checked = true, ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true };
            tb2.Controls.Add(_chkAutoScroll);
            var btnClear = new Button { Text = "清空", FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(148, 163, 184), FlatAppearance = { BorderSize = 0 }, Size = new Size(48, 22), Cursor = Cursors.Hand };
            btnClear.Click += (s, e) => _rtbLog.Clear();
            tb2.Controls.Add(btnClear);
            var btnExport = new Button { Text = "📁 导出日志", FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(148, 163, 184), FlatAppearance = { BorderSize = 0 }, Size = new Size(86, 22), Cursor = Cursors.Hand };
            btnExport.Click += BtnExportLog_Click;
            tb2.Controls.Add(btnExport);
            toolbar.Controls.Add(tb2);
            logPnl.Controls.Add(toolbar);

            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9.5F),
                ReadOnly = true, WordWrap = false,
                BorderStyle = BorderStyle.None
            };
            logPnl.Controls.Add(_rtbLog);
            tab.Controls.Add(logPnl);
        }

        // =====================================================================
        // 协议参数
        // =====================================================================

        private void BuildParams()
        {
            int x = 0;
            _lblStationId = new Label { Text = "站号：", Location = new Point(x, 3), AutoSize = true }; x += 42;
            _numStationId = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 1, Location = new Point(x, -2), Size = new Size(56, 24), BorderStyle = BorderStyle.FixedSingle }; x += 62;
            _pnlParams.Controls.AddRange(new Control[] { _lblStationId, _numStationId });

            _lblPlcType = new Label { Text = "PLC 型号：", Location = new Point(x, 3), AutoSize = true }; x += 66;
            _cmbPlcType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Location = new Point(x, -3), Size = new Size(96, 24) };
            _cmbPlcType.Items.AddRange(Enum.GetNames(typeof(SiemensPLCS))); _cmbPlcType.SelectedIndex = 3; x += 102;
            _pnlParams.Controls.AddRange(new Control[] { _lblPlcType, _cmbPlcType });

            _lblLocalTsap = new Label { Text = "Local TSAP：", Location = new Point(x, 3), AutoSize = true }; x += 82;
            _txtLocalTsap = new TextBox { Text = "10.00", Location = new Point(x, -3), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle }; x += 60;
            _pnlParams.Controls.AddRange(new Control[] { _lblLocalTsap, _txtLocalTsap });

            _lblRemoteTsap = new Label { Text = "Remote TSAP：", Location = new Point(x, 3), AutoSize = true }; x += 92;
            _txtRemoteTsap = new TextBox { Text = "02.00", Location = new Point(x, -3), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle }; x += 60;
            _pnlParams.Controls.AddRange(new Control[] { _lblRemoteTsap, _txtRemoteTsap });

            _lblDstNode = new Label { Text = "目标节点：", Location = new Point(x, 3), AutoSize = true }; x += 66;
            _numDstNode = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 1, Location = new Point(x, -2), Size = new Size(56, 24), BorderStyle = BorderStyle.FixedSingle }; x += 62;
            _pnlParams.Controls.AddRange(new Control[] { _lblDstNode, _numDstNode });

            _lblSrcNode = new Label { Text = "源节点：", Location = new Point(x, 3), AutoSize = true }; x += 52;
            _numSrcNode = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 0, Location = new Point(x, -2), Size = new Size(56, 24), BorderStyle = BorderStyle.FixedSingle }; x += 62;
            _pnlParams.Controls.AddRange(new Control[] { _lblSrcNode, _numSrcNode });

            _lblUnitNo = new Label { Text = "单元号：", Location = new Point(x, 3), AutoSize = true }; x += 52;
            _txtUnitNo = new TextBox { Text = "00", Location = new Point(x, -3), Size = new Size(44, 24), BorderStyle = BorderStyle.FixedSingle }; x += 50;
            _pnlParams.Controls.AddRange(new Control[] { _lblUnitNo, _txtUnitNo });

            _lblSlot = new Label { Text = "槽号：", Location = new Point(x, 3), AutoSize = true }; x += 42;
            _numSlot = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 0, Location = new Point(x, -2), Size = new Size(56, 24), BorderStyle = BorderStyle.FixedSingle }; x += 62;
            _pnlParams.Controls.AddRange(new Control[] { _lblSlot, _numSlot });

            foreach (Control c in _pnlParams.Controls) c.Visible = false;
        }

        private void UpdateParams()
        {
            foreach (Control c in _pnlParams.Controls) c.Visible = false;
            
            // 切换协议时设置默认地址
            _txtAddress.Text = _cmbProtocol.SelectedIndex switch
            {
                0 or 1 => "0",
                2 => "DB1.DBW0",
                3 => "D0",
                4 => "D100",
                5 => "D100",
                6 => "MyTag",
                _ => "0"
            };
            
            _lblAddrHint.Text = _cmbProtocol.SelectedIndex switch
            {
                0 or 1 => "💡 Modbus 地址：0, 1, 2 ... (寄存器偏移) | 线圈: 0~65535 | 保持寄存器: 40001~49999",
                2 => "💡 S7 地址：DB1.DBW0 (字) | DB1.DBD0 (双字) | DB1.DBX0.0 (位) | MW0, IW0, QW0",
                3 => "💡 三菱MC 地址：D0 (数据寄存器) | M0 (继电器) | X0/Y0 (IO, 16进制) | M10.3 (位偏移)",
                4 => "💡 FINS 地址：D100/DM100 (DM区) | CIO100 | W100 | H100 | D100.05 (位寻址)",
                5 => "💡 HostLink 地址：D100, CIO100, W100, H100 等",
                6 => "💡 CIP 地址：直接使用标签名 (如 MyTag, MyTag[0], MyTag.Member)",
                _ => ""
            };

            switch (_cmbProtocol.SelectedIndex)
            {
                case 0: case 1: _lblStationId.Visible = _numStationId.Visible = true; _numPort.Value = 502; break;
                case 2: _lblPlcType.Visible = _cmbPlcType.Visible = _lblLocalTsap.Visible = _txtLocalTsap.Visible = _lblRemoteTsap.Visible = _txtRemoteTsap.Visible = true; _numPort.Value = 102; break;
                case 3: _numPort.Value = 5006; break;
                case 4: _lblDstNode.Visible = _numDstNode.Visible = _lblSrcNode.Visible = _numSrcNode.Visible = true; _numPort.Value = 9600; break;
                case 5: _lblUnitNo.Visible = _txtUnitNo.Visible = true; _numPort.Value = 9600; break;
                case 6: _lblSlot.Visible = _numSlot.Visible = true; _numPort.Value = 44818; break;
            }
        }

        // =====================================================================
        // 日志
        // =====================================================================

        private void AppendLog(TraceEventArgs e)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{ts}] [{e.Level}] {e.Message}";
            if (e.ElapsedMilliseconds > 0) line += $" ({e.ElapsedMilliseconds}ms)";
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = e.Level switch
            {
                TraceLevel.Verbose => Color.Gray,
                TraceLevel.Info => Color.FromArgb(52, 211, 153),
                TraceLevel.Warning => Color.FromArgb(251, 191, 36),
                TraceLevel.Error => Color.FromArgb(248, 113, 113),
                TraceLevel.Fatal => Color.Red,
                _ => Color.LightGray
            };
            _rtbLog.AppendText(line + "\n");
            if (_chkAutoScroll.Checked) { _rtbLog.SelectionStart = _rtbLog.TextLength; _rtbLog.ScrollToCaret(); }
        }

        private void LogInfo(string m) => Log(new TraceEventArgs(TraceLevel.Info, m));
        private void LogError(string m) => Log(new TraceEventArgs(TraceLevel.Error, m));
        private void LogWarn(string m) => Log(new TraceEventArgs(TraceLevel.Warning, m));
        private void Log(TraceEventArgs e) { if (!_rtbLog.IsDisposed) _rtbLog.Invoke(() => AppendLog(e)); }

        private void BtnExportLog_Click(object? s, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "文本文件|*.txt|所有文件|*.*", FileName = $"plc_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try { File.WriteAllText(dlg.FileName, _rtbLog.Text, Encoding.UTF8); LogInfo($"日志已导出: {dlg.FileName}"); }
                catch (Exception ex) { LogError($"导出失败: {ex.Message}"); }
            }
        }

        // =====================================================================
        // 连接
        // =====================================================================

        private void OnProtocolChanged(object? s, EventArgs e) => UpdateParams();
        private void StatusDot_Paint(object? s, PaintEventArgs e) { var p = (Panel)s!; using var b = new SolidBrush(p.BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillEllipse(b, 1, 1, p.Width - 2, p.Height - 2); }
        private void OnWriteTypeChanged(object? s, EventArgs e) => UpdateWriteUI();
        private void OnBoolValueChanged(object? s, EventArgs e) => _chkBoolValue.Text = _chkBoolValue.Checked ? "True" : "False";

        private void OnFormKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.R) _btnRead.PerformClick();
            if (e.Control && e.KeyCode == Keys.W) _btnWrite.PerformClick();
            if (e.Control && e.KeyCode == Keys.D) { if (_connected) _btnConnect.PerformClick(); }
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_connected) { await Disconnect(); return; }
            _btnConnect.Enabled = false;
            SetStatus("orange", "连接中...");
            try
            {
                var r = await Connect();
                if (r.IsSuccess)
                {
                    _connected = true;
                    SetStatus("green", "已连接");
                    _btnConnect.Text = "断开";
                    _btnConnect.BackColor = Color.FromArgb(220, 38, 38);
                    _lblStatusLeft.Text = $"已连接  {_txtIp.Text}:{_numPort.Value}";
                    LogInfo($"已连接到 {_txtIp.Text}:{_numPort.Value}");
                    SaveConfig();
                }
                else { SetStatus("red", "连接失败"); LogError($"连接失败: {r.Message}"); }
            }
            catch (Exception ex) { SetStatus("red", "连接异常"); LogError($"连接异常: {ex.Message}"); }
            _btnConnect.Enabled = true;
            btnState();
        }

        private async Task<OperateResult> Connect()
        {
            string ip = _txtIp.Text.Trim();
            int port = (int)_numPort.Value;
            await Disconnect();
            _device = _cmbProtocol.SelectedIndex switch
            {
                0 => new ModbusTcpNet(ip, port, (byte)_numStationId.Value),
                1 => new ModbusRtuNet(ip, port, (byte)_numStationId.Value),
                2 => new SiemensS7Net((SiemensPLCS)Enum.Parse(typeof(SiemensPLCS), _cmbPlcType.Text), ip, port) { LocalTSAP = _txtLocalTsap.Text.Trim(), RemoteTSAP = _txtRemoteTsap.Text.Trim() },
                3 => new MitsubishiMcNet(ip, port),
                4 => new OmronFinsNet(ip, port) { DstNode = (byte)_numDstNode.Value, SrcNode = (byte)_numSrcNode.Value },
                5 => new OmronHostLinkNet(ip, port) { UnitNumber = _txtUnitNo.Text.Trim() },
                6 => new AllenBradleyCipNet(ip, port) { Slot = (byte)_numSlot.Value },
                _ => throw new InvalidOperationException()
            };
            if (_device is NetworkDeviceBase dev)
            {
                _deviceBase = dev; dev.EnableTrace = true; dev.ConnectTimeout = 5000; dev.SendTimeout = 3000; dev.ReceiveTimeout = 5000;
                LogManager.Subscribe(dev);
            }
            return await _device.ConnectAsync();
        }

        private async Task Disconnect()
        {
            if (_monitorRunning) StopMonitor();
            if (_device != null)
            {
                if (_deviceBase != null) LogManager.Unsubscribe(_deviceBase);
                await _device.DisconnectAsync(); _device = null; _deviceBase = null;
            }
            _connected = false; _btnConnect.Text = "连接"; _btnConnect.BackColor = Color.FromArgb(22, 163, 74);
            SetStatus("gray", "未连接"); _lblStatusLeft.Text = "就绪"; btnState();
        }

        private void SetStatus(string color, string text)
        {
            _lblStatusText.Text = text;
            _pnlStatusDot.BackColor = color switch { "green" => Color.FromArgb(22, 163, 74), "red" => Color.FromArgb(220, 38, 38), "orange" => Color.FromArgb(245, 158, 11), _ => Color.Gray };
            _pnlStatusDot.Invalidate();
        }

        // =====================================================================
        // 读写操作
        // =====================================================================

        private async void BtnRead_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            string addr = _txtAddress.Text.Trim();
            
            // 地址格式校验
            var addrError = ValidateAddress(addr);
            if (addrError != null) { LogWarn(addrError); return; }
            
            int len = (int)_numLength.Value;
            _btnRead.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await _device!.ReadAsync(addr, (ushort)len);
                sw.Stop();
                if (r.IsSuccess && r.Content != null) { ShowResult(r.Content, sw.ElapsedMilliseconds); LogInfo($"读取成功: {addr}, {len}B, {sw.ElapsedMilliseconds}ms"); }
                else { ClearResult(); LogError($"读取失败: {r.Message}"); }
            }
            catch (Exception ex) { ClearResult(); LogError($"读取异常: {ex.Message}"); }
            _btnRead.Enabled = true;
        }

        private async void BtnReadBool_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            string addr = _txtAddress.Text.Trim();
            _btnReadBool.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await _device!.ReadBoolAsync(addr); sw.Stop();
                if (r.IsSuccess) { _lblBool.Text = $"Bool: {r.Content}"; _lblElapsed.Text = $"耗时：{sw.ElapsedMilliseconds}ms"; LogInfo($"ReadBool: {addr} = {r.Content}, {sw.ElapsedMilliseconds}ms"); }
                else { _lblBool.Text = "Bool: --"; LogError($"ReadBool失败: {r.Message}"); }
            }
            catch (Exception ex) { LogError($"ReadBool异常: {ex.Message}"); }
            _btnReadBool.Enabled = true;
        }

        private async void BtnWrite_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            byte[]? data = ParseValue(); if (data == null) return;
            string addr = _txtAddress.Text.Trim();
            _btnWrite.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await _device!.WriteAsync(addr, data); sw.Stop();
                if (r.IsSuccess) LogInfo($"写入成功: {addr}, {data.Length}B, {sw.ElapsedMilliseconds}ms");
                else LogError($"写入失败: {r.Message}");
            }
            catch (Exception ex) { LogError($"写入异常: {ex.Message}"); }
            _btnWrite.Enabled = true;
        }

        private async void BtnWriteBool_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            string addr = _txtAddress.Text.Trim(); bool val = _chkBoolValue.Checked;
            _btnWriteBool.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await _device!.WriteAsync(addr, val); sw.Stop();
                if (r.IsSuccess) LogInfo($"WriteBool: {addr} = {val}, {sw.ElapsedMilliseconds}ms");
                else LogError($"WriteBool失败: {r.Message}");
            }
            catch (Exception ex) { LogError($"WriteBool异常: {ex.Message}"); }
            _btnWriteBool.Enabled = true;
        }

        // =====================================================================
        // 数据监视
        // =====================================================================

        private void BtnMonitorAdd_Click(object? s, EventArgs e)
        {
            string addr = _txtAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { LogWarn("请先输入地址"); return; }
            _dgvMonitor.Rows.Add($"监控项", addr, "Hex", "4", "--", "--");
        }

        private void BtnMonitorRemove_Click(object? s, EventArgs e)
        {
            if (_dgvMonitor.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in _dgvMonitor.SelectedRows)
                    if (!row.IsNewRow) _dgvMonitor.Rows.Remove(row);
            }
        }

        private async void BtnMonitorReadOnce_Click(object? s, EventArgs e)
        {
            if (!CheckConn()) return;
            await ReadAllMonitorRows();
        }

        private void BtnMonitorToggle_Click(object? s, EventArgs e)
        {
            if (_monitorRunning)
                StopMonitor();
            else
                StartMonitor();
        }

        private void StartMonitor()
        {
            if (!CheckConn()) return;
            _monitorRunning = true;
            _btnMonitorToggle.Text = "⏸ 停止监视";
            _btnMonitorToggle.BackColor = Color.FromArgb(220, 38, 38);
            _monitorTickCount = 0;

            int interval = (int)_numMonitorInterval.Value;
            _monitorTimer = new System.Windows.Forms.Timer { Interval = interval };
            _monitorTimer.Tick += async (s, e) =>
            {
                if (!_monitorRunning || !_connected) { StopMonitor(); return; }
                await ReadAllMonitorRows();
                _monitorTickCount++;
                _lblStatusRight.Text = $"监视中 #{_monitorTickCount} | {interval}ms";
            };
            _monitorTimer.Start();
            LogInfo($"监视已启动，间隔 {interval}ms");
        }

        private void StopMonitor()
        {
            _monitorRunning = false;
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _monitorTimer = null;
            _btnMonitorToggle.Text = "▶ 开始监视";
            _btnMonitorToggle.BackColor = Color.FromArgb(22, 163, 74);
            LogInfo("监视已停止");
        }

        private async Task ReadAllMonitorRows()
        {
            if (_device == null || _deviceBase == null) return;

            for (int i = 0; i < _dgvMonitor.Rows.Count; i++)
            {
                var row = _dgvMonitor.Rows[i];
                if (row.IsNewRow) continue;

                string? address = row.Cells["Address"].Value?.ToString();
                string? dataType = row.Cells["DataType"].Value?.ToString();
                string? lengthStr = row.Cells["Length"].Value?.ToString();

                if (string.IsNullOrEmpty(address)) continue;

                try
                {
                    ushort len = ushort.TryParse(lengthStr, out var l) ? l : (ushort)4;
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    if (dataType == "Bool")
                    {
                        var r = await _device.ReadBoolAsync(address);
                        sw.Stop();
                        row.Cells["Value"].Value = r.IsSuccess ? r.Content.ToString() : $"ERR: {r.Message}";
                    }
                    else
                    {
                        var r = await _device.ReadAsync(address, len);
                        sw.Stop();
                        if (r.IsSuccess && r.Content != null)
                        {
                            var t = _deviceBase.ByteTransform;
                            string value = dataType switch
                            {
                                "Int16" => t.TransInt16(r.Content, 0).ToString(),
                                "UInt16" => t.TransUInt16(r.Content, 0).ToString(),
                                "Int32" => t.TransInt32(r.Content, 0).ToString(),
                                "UInt32" => t.TransUInt32(r.Content, 0).ToString(),
                                "Float" => t.TransSingle(r.Content, 0).ToString("F4"),
                                "Double" => t.TransDouble(r.Content, 0).ToString("F6"),
                                "String" => Encoding.ASCII.GetString(r.Content).TrimEnd('\0'),
                                _ => string.Join(" ", r.Content.Select(b => b.ToString("X2")))
                            };
                            row.Cells["Value"].Value = value;
                        }
                        else
                        {
                            row.Cells["Value"].Value = $"ERR: {r.Message}";
                        }
                    }
                    row.Cells["LastUpdate"].Value = DateTime.Now.ToString("HH:mm:ss.fff");
                }
                catch (Exception ex)
                {
                    row.Cells["Value"].Value = $"EX: {ex.Message}";
                }
            }
        }

        // =====================================================================
        // 批量读写
        // =====================================================================

        private async void BtnBatchRead_Click(object? s, EventArgs e)
        {
            if (!CheckConn()) return;
            _btnBatchRead.Enabled = false;
            _txtBatchResult.Clear();

            var lines = _txtBatchAddresses.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int success = 0, fail = 0;

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string addr = parts[0];
                ushort len = parts.Length > 1 && ushort.TryParse(parts[1], out var l) ? l : (ushort)4;

                try
                {
                    string dataType = _cmbBatchType.SelectedItem?.ToString() ?? "Hex";

                    if (dataType == "Bool")
                    {
                        var r = await _device!.ReadBoolAsync(addr);
                        if (r.IsSuccess) { sb.AppendLine($"{addr,-16} = {r.Content}"); success++; }
                        else { sb.AppendLine($"{addr,-16} = ERR: {r.Message}"); fail++; }
                    }
                    else
                    {
                        var r = await _device!.ReadAsync(addr, len);
                        if (r.IsSuccess && r.Content != null && _deviceBase != null)
                        {
                            var t = _deviceBase.ByteTransform;
                            string value = dataType switch
                            {
                                "Int16" => t.TransInt16(r.Content, 0).ToString(),
                                "UInt16" => t.TransUInt16(r.Content, 0).ToString(),
                                "Int32" => t.TransInt32(r.Content, 0).ToString(),
                                "UInt32" => t.TransUInt32(r.Content, 0).ToString(),
                                "Float" => t.TransSingle(r.Content, 0).ToString("F4"),
                                "Double" => t.TransDouble(r.Content, 0).ToString("F6"),
                                "String" => Encoding.ASCII.GetString(r.Content).TrimEnd('\0'),
                                _ => string.Join(" ", r.Content.Select(b => b.ToString("X2")))
                            };
                            sb.AppendLine($"{addr,-16} = {value}");
                            success++;
                        }
                        else { sb.AppendLine($"{addr,-16} = ERR: {r.Message}"); fail++; }
                    }
                }
                catch (Exception ex) { sb.AppendLine($"{addr,-16} = EX: {ex.Message}"); fail++; }
            }

            sw.Stop();
            sb.AppendLine($"\n─────────────────────────────");
            sb.AppendLine($"完成: {success} 成功 / {fail} 失败 / {lines.Length} 总计 | 耗时: {sw.ElapsedMilliseconds}ms");
            _txtBatchResult.Text = sb.ToString();
            _btnBatchRead.Enabled = true;
        }

        // =====================================================================
        // 书签
        // =====================================================================

        private void BtnBookmarkAdd_Click(object? s, EventArgs e)
        {
            string name = _txtBookmarkName.Text.Trim();
            string addr = _txtAddress.Text.Trim();
            string len = _numLength.Value.ToString();
            if (string.IsNullOrEmpty(addr)) { LogWarn("请先输入地址"); return; }
            if (string.IsNullOrEmpty(name)) name = addr;

            string entry = $"{name}|{addr}|{len}|{_cmbProtocol.SelectedIndex}";
            if (!_lstBookmarks.Items.Contains(entry))
            {
                _lstBookmarks.Items.Add(entry);
                SaveBookmarks();
                LogInfo($"书签已添加: {name} ({addr})");
            }
        }

        private void BtnBookmarkRemove_Click(object? s, EventArgs e)
        {
            if (_lstBookmarks.SelectedIndex >= 0)
            {
                _lstBookmarks.Items.RemoveAt(_lstBookmarks.SelectedIndex);
                SaveBookmarks();
            }
        }

        private void BtnBookmarkUse_Click(object? s, EventArgs e)
        {
            if (_lstBookmarks.SelectedItem is string entry)
            {
                var parts = entry.Split('|');
                if (parts.Length >= 3)
                {
                    _txtAddress.Text = parts[1];
                    if (ushort.TryParse(parts[2], out var len)) _numLength.Value = len;
                    if (parts.Length >= 4 && int.TryParse(parts[3], out var proto)) _cmbProtocol.SelectedIndex = proto;
                    _tabMain.SelectedIndex = 0; // 切换到读写Tab
                }
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var bookmarks = new List<string>();
                foreach (var item in _lstBookmarks.Items) bookmarks.Add(item?.ToString() ?? "");
                File.WriteAllText(Path.Combine(dir!, "bookmarks.txt"), string.Join("\n", bookmarks), Encoding.UTF8);
            }
            catch { }
        }

        private void LoadBookmarks()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                var path = Path.Combine(dir!, "bookmarks.txt");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                        if (!string.IsNullOrWhiteSpace(line))
                            _lstBookmarks.Items.Add(line);
                }
            }
            catch { }
        }

        // =====================================================================
        // 配置保存/加载
        // =====================================================================

        private void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var cfg = new Dictionary<string, string>
                {
                    ["Protocol"] = _cmbProtocol.SelectedIndex.ToString(),
                    ["Ip"] = _txtIp.Text,
                    ["Port"] = _numPort.Value.ToString(),
                    ["StationId"] = _numStationId.Value.ToString(),
                    ["PlcType"] = _cmbPlcType.SelectedIndex.ToString(),
                    ["LocalTsap"] = _txtLocalTsap.Text,
                    ["RemoteTsap"] = _txtRemoteTsap.Text,
                    ["DstNode"] = _numDstNode.Value.ToString(),
                    ["SrcNode"] = _numSrcNode.Value.ToString(),
                    ["UnitNo"] = _txtUnitNo.Text,
                    ["Slot"] = _numSlot.Value.ToString()
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (cfg == null) return;
                if (cfg.TryGetValue("Protocol", out var p) && int.TryParse(p, out var pi)) _cmbProtocol.SelectedIndex = pi;
                if (cfg.TryGetValue("Ip", out var ip)) _txtIp.Text = ip;
                if (cfg.TryGetValue("Port", out var port) && int.TryParse(port, out var pv)) _numPort.Value = pv;
                if (cfg.TryGetValue("StationId", out var sid) && int.TryParse(sid, out var sv)) _numStationId.Value = sv;
                if (cfg.TryGetValue("PlcType", out var pt) && int.TryParse(pt, out var ptv)) _cmbPlcType.SelectedIndex = ptv;
                if (cfg.TryGetValue("LocalTsap", out var lt)) _txtLocalTsap.Text = lt;
                if (cfg.TryGetValue("RemoteTsap", out var rt)) _txtRemoteTsap.Text = rt;
                if (cfg.TryGetValue("DstNode", out var dn) && int.TryParse(dn, out var dnv)) _numDstNode.Value = dnv;
                if (cfg.TryGetValue("SrcNode", out var sn) && int.TryParse(sn, out var snv)) _numSrcNode.Value = snv;
                if (cfg.TryGetValue("UnitNo", out var un)) _txtUnitNo.Text = un;
                if (cfg.TryGetValue("Slot", out var sl) && int.TryParse(sl, out var slv)) _numSlot.Value = slv;
            }
            catch { }

            LoadBookmarks();
        }

        // =====================================================================
        // 辅助
        // =====================================================================

        private bool CheckConn()
        {
            if (!_connected || _device == null) { LogWarn("请先连接设备"); return false; }
            return true;
        }

        private void btnState() => _btnRead.Enabled = _btnWrite.Enabled = _btnReadBool.Enabled = _btnWriteBool.Enabled = _connected;

        private void UpdateWriteUI()
        {
            int type = _cmbWriteType.SelectedIndex;
            bool isBool = type == 10;
            _txtWriteValue.Visible = !isBool; _chkBoolValue.Visible = isBool;
            if (!isBool) _txtWriteValue.Text = type switch
            {
                0 => "00 01 00 02",
                1 => "1234",
                2 => "65535",
                3 => "123456",
                4 => "4000000000",
                5 => "123456789",
                6 => "18446744073709551615",
                7 => "3.14",
                8 => "3.14159265358979",
                9 => "Hello PLC",
                _ => _txtWriteValue.Text
            };
            else _chkBoolValue.Checked = true;
        }

        private byte[]? ParseValue()
        {
            int type = _cmbWriteType.SelectedIndex; string text = _txtWriteValue.Text.Trim();
            try
            {
                if (_deviceBase == null) { LogError("未连接设备"); return null; }
                var t = _deviceBase.ByteTransform;
                return type switch
                {
                    0 => text.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => Convert.ToByte(p, 16)).ToArray(),
                    1 => t.GetBytes(short.Parse(text)),
                    2 => t.GetBytes(ushort.Parse(text)),
                    3 => t.GetBytes(int.Parse(text)),
                    4 => t.GetBytes(uint.Parse(text)),
                    5 => t.GetBytes(long.Parse(text)),
                    6 => t.GetBytes(ulong.Parse(text)),
                    7 => t.GetBytes(float.Parse(text)),
                    8 => t.GetBytes(double.Parse(text)),
                    9 => Encoding.ASCII.GetBytes(text),
                    _ => null
                };
            }
            catch (FormatException) { LogError("数值格式错误"); return null; }
            catch (Exception ex) { LogError($"解析错误: {ex.Message}"); return null; }
        }

        private void ShowResult(byte[] data, long ms)
        {
            _txtHexResult.Text = string.Join(" ", data.Select(b => b.ToString("X2")));
            _lblStatusRight.Text = $"最后: {ms}ms  {data.Length}B";
            _lblElapsed.Text = $"耗时：{ms}ms";
            if (data.Length == 0 || _deviceBase == null) return;
            var t = _deviceBase.ByteTransform;
            try { _lblInt16.Text = data.Length >= 2 ? $"Int16: {t.TransInt16(data, 0)}" : "Int16: --"; } catch { _lblInt16.Text = "Int16: err"; }
            try { _lblUInt16.Text = data.Length >= 2 ? $"UInt16: {t.TransUInt16(data, 0)}" : "UInt16: --"; } catch { _lblUInt16.Text = "UInt16: err"; }
            try { _lblInt32.Text = data.Length >= 4 ? $"Int32: {t.TransInt32(data, 0)}" : "Int32: --"; } catch { _lblInt32.Text = "Int32: err"; }
            try { _lblUInt32.Text = data.Length >= 4 ? $"UInt32: {t.TransUInt32(data, 0)}" : "UInt32: --"; } catch { _lblUInt32.Text = "UInt32: err"; }
            try { _lblInt64.Text = data.Length >= 8 ? $"Int64: {t.TransInt64(data, 0)}" : "Int64: --"; } catch { _lblInt64.Text = "Int64: err"; }
            try { _lblUInt64.Text = data.Length >= 8 ? $"UInt64: {t.TransUInt64(data, 0)}" : "UInt64: --"; } catch { _lblUInt64.Text = "UInt64: err"; }
            try { _lblFloat.Text = data.Length >= 4 ? $"Float: {t.TransSingle(data, 0):F6}" : "Float: --"; } catch { _lblFloat.Text = "Float: err"; }
            try { _lblDouble.Text = data.Length >= 8 ? $"Double: {t.TransDouble(data, 0):F6}" : "Double: --"; } catch { _lblDouble.Text = "Double: err"; }
            try { _lblBool.Text = $"Bool: {(data[0] != 0)}"; } catch { _lblBool.Text = "Bool: err"; }
            try { _lblString.Text = data.Length > 0 ? $"String: {Encoding.ASCII.GetString(data).TrimEnd('\0')}" : "String: --"; } catch { _lblString.Text = "String: err"; }
        }

        private void ClearResult()
        {
            _txtHexResult.Clear();
            _lblElapsed.Text = "耗时：--";
            foreach (var l in new[] { _lblInt16, _lblUInt16, _lblInt32, _lblUInt32, _lblInt64, _lblUInt64, _lblFloat, _lblDouble, _lblBool, _lblString })
                l.Text = l.Text.Split(':')[0] + ": --";
        }

        private static Label VLabel(string n, int x, int y, int w) => new Label
        { Text = $"{n}: --", Location = new Point(x, y), Size = new Size(w, 20), TextAlign = ContentAlignment.MiddleLeft };

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_monitorRunning) StopMonitor();
            if (_device != null)
            {
                if (_deviceBase != null) LogManager.Unsubscribe(_deviceBase);
                await _device.DisconnectAsync();
                if (_device is IDisposable d) d.Dispose();
            }
            StopAllSimulators();
            SaveConfig();
            base.OnFormClosing(e);
        }

        // =====================================================================
        // 地址格式校验
        // =====================================================================

        /// <summary>校验地址格式，返回 null 表示有效，否则返回错误信息。</summary>
        private string? ValidateAddress(string addr)
        {
            if (string.IsNullOrWhiteSpace(addr)) return "地址不能为空";
            return _cmbProtocol.SelectedIndex switch
            {
                0 or 1 => null, // Modbus 地址格式简单，不做校验
                2 => ValidateS7Address(addr),
                3 => ValidateMitsubishiAddress(addr),
                4 or 5 => ValidateOmronAddress(addr),
                6 => null, // CIP 使用标签名，不做校验
                _ => null
            };
        }

        private static string? ValidateS7Address(string addr)
        {
            addr = addr.ToUpperInvariant();
            if (addr.StartsWith("DB"))
            {
                if (!addr.Contains('.')) return "DB 地址格式：DB1.DBW0, DB1.DBD0, DB1.DBX0.0";
                return null;
            }
            if (addr.Length >= 2)
            {
                char area = addr[0];
                char type = addr[1];
                if ((area == 'M' || area == 'I' || area == 'Q') &&
                    (type == 'B' || type == 'W' || type == 'D' || type == 'X' || char.IsDigit(type)))
                    return null;
            }
            if ((addr.StartsWith('T') || addr.StartsWith('C')) && addr.Length > 1)
                return null;
            return "S7 地址格式示例：DB1.DBW0, MW0, I0.0, QW0, T0, C0";
        }

        private static string? ValidateMitsubishiAddress(string addr)
        {
            addr = addr.ToUpperInvariant();
            if (addr.Length == 0) return "地址不能为空";
            char area = addr[0];
            if (area == 'D' || area == 'M' || area == 'X' || area == 'Y' ||
                area == 'B' || area == 'W' || area == 'R' || area == 'S' ||
                area == 'T' || area == 'C' || area == 'Z')
            {
                if (addr.Length == 1) return $"地址 {area} 后需要跟数字，如 {area}0";
                return null;
            }
            return "三菱地址格式：D0, M0, X0, Y0";
        }

        private static string? ValidateOmronAddress(string addr)
        {
            addr = addr.ToUpperInvariant();
            if (addr.Length == 0) return "地址不能为空";
            if (addr.StartsWith("CIO") || addr.StartsWith("DM") || addr.StartsWith("WR") ||
                addr.StartsWith("HR") || addr.StartsWith("AR") || addr.StartsWith("EM"))
                return null;
            char area = addr[0];
            if (area == 'D' || area == 'W' || area == 'H' || area == 'A' || area == 'T' || area == 'C' || area == 'E')
            {
                if (addr.Length == 1) return $"地址 {area} 后需要跟数字";
                return null;
            }
            return "欧姆龙地址格式：D100, CIO100, W100, H100";
        }

        // =====================================================================
        // 模拟器控制
        // =====================================================================

        private void ToggleSimulator(string type)
        {
            try
            {
                switch (type)
                {
                    case "mitsubishi":
                        if (_mitsubishiSim?.IsRunning == true)
                        {
                            _mitsubishiSim.Stop();
                            _mitsubishiSim.Dispose();
                            _mitsubishiSim = null;
                            LogInfo("三菱MC模拟器已停止");
                        }
                        else
                        {
                            _mitsubishiSim?.Dispose();
                            _mitsubishiSim = new MitsubishiMcSimulator(5006);
                            _mitsubishiSim.Start();
                            LogInfo("三菱MC模拟器已启动，端口5006");
                            _txtIp.Text = "127.0.0.1";
                            _numPort.Value = 5006;
                            _cmbProtocol.SelectedIndex = 3; // 三菱MC
                        }
                        break;

                    case "siemens":
                        if (_siemensSim?.IsRunning == true)
                        {
                            _siemensSim.Stop();
                            _siemensSim.Dispose();
                            _siemensSim = null;
                            LogInfo("西门子S7模拟器已停止");
                        }
                        else
                        {
                            _siemensSim?.Dispose();
                            _siemensSim = new SiemensS7Simulator(102);
                            _siemensSim.Start();
                            LogInfo("西门子S7模拟器已启动，端口102");
                            _txtIp.Text = "127.0.0.1";
                            _numPort.Value = 102;
                            _cmbProtocol.SelectedIndex = 2; // 西门子S7
                        }
                        break;

                    case "modbus":
                        if (_modbusSim?.IsRunning == true)
                        {
                            _modbusSim.Stop();
                            _modbusSim.Dispose();
                            _modbusSim = null;
                            LogInfo("Modbus TCP模拟器已停止");
                        }
                        else
                        {
                            _modbusSim?.Dispose();
                            _modbusSim = new ModbusTcpSimulator(502);
                            _modbusSim.Start();
                            LogInfo("Modbus TCP模拟器已启动，端口502");
                            _txtIp.Text = "127.0.0.1";
                            _numPort.Value = 502;
                            _cmbProtocol.SelectedIndex = 0; // Modbus TCP
                        }
                        break;
                }
                UpdateSimulatorMenu();
            }
            catch (Exception ex)
            {
                LogError($"模拟器启动失败: {ex.Message}");
            }
        }

        private void StopAllSimulators()
        {
            _mitsubishiSim?.Stop();
            _mitsubishiSim?.Dispose();
            _mitsubishiSim = null;

            _siemensSim?.Stop();
            _siemensSim?.Dispose();
            _siemensSim = null;

            _modbusSim?.Stop();
            _modbusSim?.Dispose();
            _modbusSim = null;

            UpdateSimulatorMenu();
            LogInfo("所有模拟器已停止");
        }

        private void UpdateSimulatorMenu()
        {
            if (_menuSimulator.DropDownItems.Count < 5) return;
            
            ((ToolStripMenuItem)_menuSimulator.DropDownItems[0]).Text = 
                _mitsubishiSim?.IsRunning == true ? "✓ 停止三菱MC模拟器" : "启动三菱MC模拟器 (端口5006)";
            ((ToolStripMenuItem)_menuSimulator.DropDownItems[1]).Text = 
                _siemensSim?.IsRunning == true ? "✓ 停止西门子S7模拟器" : "启动西门子S7模拟器 (端口102)";
            ((ToolStripMenuItem)_menuSimulator.DropDownItems[2]).Text = 
                _modbusSim?.IsRunning == true ? "✓ 停止Modbus TCP模拟器" : "启动Modbus TCP模拟器 (端口502)";
        }
    }
}
