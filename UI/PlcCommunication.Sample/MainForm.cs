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
    /// <summary>
    /// PLC 通信调试工具 v2.1 - 优化布局版本
    /// 作者：ljh QQ: 3010812967
    /// </summary>
    public class MainForm : Form
    {
        #region 控件声明
        
        // 连接控件
        private ComboBox _cmbProtocol;
        private TextBox _txtIp;
        private NumericUpDown _numPort;
        private Button _btnConnect;
        private Panel _pnlStatusDot;
        private Label _lblStatusText;
        private Panel _pnlParams;
        
        // 参数控件
        private Label _lblStationId; private NumericUpDown _numStationId;
        private Label _lblPlcType; private ComboBox _cmbPlcType;
        private Label _lblLocalTsap, _lblRemoteTsap;
        private TextBox _txtLocalTsap, _txtRemoteTsap;
        private Label _lblDstNode, _lblSrcNode;
        private NumericUpDown _numDstNode, _numSrcNode;
        private Label _lblUnitNo; private TextBox _txtUnitNo;
        private Label _lblSlot; private NumericUpDown _numSlot;

        // 读写控件
        private TextBox _txtAddress;
        private NumericUpDown _numLength;
        private Button _btnRead, _btnWrite, _btnReadBool, _btnWriteBool;
        private TextBox _txtWriteValue;
        private ComboBox _cmbWriteType;
        private CheckBox _chkBoolValue;
        private Label _lblAddrHint;

        // 结果控件
        private TextBox _txtHexResult;
        private Label _lblInt16, _lblUInt16, _lblInt32, _lblUInt32;
        private Label _lblInt64, _lblUInt64;
        private Label _lblFloat, _lblDouble, _lblBool, _lblString;
        private Label _lblElapsed;

        // 日志控件（右侧面板）
        private RichTextBox _rtbLog;
        private CheckBox _chkAutoScroll;
        private Button _btnClearLog, _btnExportLog;

        // 监视控件
        private DataGridView _dgvMonitor;
        private Button _btnMonitorAdd, _btnMonitorRemove, _btnMonitorReadOnce, _btnMonitorToggle;
        private NumericUpDown _numMonitorInterval;
        private System.Windows.Forms.Timer? _monitorTimer;
        private bool _monitorRunning;

        // 书签控件
        private ListBox _lstBookmarks;
        private Button _btnBookmarkAdd, _btnBookmarkRemove, _btnBookmarkUse;
        private TextBox _txtBookmarkName;

        // 状态栏
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _lblStatusLeft, _lblStatusRight;

        // 设备与状态
        private IReadWriteNet? _device;
        private NetworkDeviceBase? _deviceBase;
        private bool _connected;

        // 模拟器
        private MitsubishiMcSimulator? _mitsubishiSim;
        private SiemensS7Simulator? _siemensSim;
        private ModbusTcpSimulator? _modbusSim;

        // 菜单
        private MenuStrip _menuStrip;
        private ToolStripMenuItem _menuSimulator;

        // 配置路径
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlcCommunication", "config.json");

        #endregion

        #region 构造函数

        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            LogManager.AddSink(LogHandler);
            LoadConfig();
            btnState();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            
            // 窗体设置 - 适应更多屏幕尺寸
            Text = "PlcCommunication - PLC 通信调试工具 v2.1";
            Size = new Size(1100, 700);
            MinimumSize = new Size(900, 550);
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            WindowState = FormWindowState.Maximized; // 默认最大化
            
            // 创建菜单
            BuildMenu();
            
            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildMenu()
        {
            _menuStrip = new MenuStrip { BackColor = Color.White };
            
            // 文件菜单
            var menuFile = new ToolStripMenuItem("文件");
            menuFile.DropDownItems.Add("导出日志", null, (s, e) => ExportLog());
            menuFile.DropDownItems.Add(new ToolStripSeparator());
            menuFile.DropDownItems.Add("退出", null, (s, e) => Close());
            
            // 模拟器菜单
            _menuSimulator = new ToolStripMenuItem("模拟器");
            _menuSimulator.DropDownItems.Add("三菱MC模拟器 (端口5006)", null, (s, e) => ToggleSimulator("mitsubishi"));
            _menuSimulator.DropDownItems.Add("西门子S7模拟器 (端口102)", null, (s, e) => ToggleSimulator("siemens"));
            _menuSimulator.DropDownItems.Add("Modbus TCP模拟器 (端口502)", null, (s, e) => ToggleSimulator("modbus"));
            _menuSimulator.DropDownItems.Add(new ToolStripSeparator());
            _menuSimulator.DropDownItems.Add("停止所有模拟器", null, (s, e) => StopAllSimulators());
            
            // 帮助菜单
            var menuHelp = new ToolStripMenuItem("帮助");
            menuHelp.DropDownItems.Add("关于", null, (s, e) => MessageBox.Show(
                "PlcCommunication - PLC 通信调试工具 v2.1\n\n" +
                "作者：ljh\n" +
                "QQ：3010812967\n" +
                "Email: usmars@qq.com\n\n" +
                "MIT License - 免费开源可商用",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information));
            
            _menuStrip.Items.AddRange(new[] { menuFile, _menuSimulator, menuHelp });
            Controls.Add(_menuStrip);
            MainMenuStrip = _menuStrip;
        }

        #endregion

        #region UI 构建

        private void BuildUI()
        {
            // 主布局：左侧操作区 + 右侧日志区
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); // 连接区
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 内容区
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));  // 左侧操作
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));  // 右侧日志

            // ========== 第0行：连接区 ==========
            var connPanel = BuildConnectionPanel();
            mainLayout.Controls.Add(connPanel, 0, 0);
            
            // 右侧日志头部
            var logHeader = BuildLogHeader();
            mainLayout.Controls.Add(logHeader, 1, 0);

            // ========== 第1行：内容区 ==========
            // 左侧：读写操作
            var leftPanel = BuildLeftPanel();
            mainLayout.Controls.Add(leftPanel, 0, 1);
            
            // 右侧：日志
            var logPanel = BuildLogPanel();
            mainLayout.Controls.Add(logPanel, 1, 1);

            Controls.Add(mainLayout);

            // 状态栏
            _statusStrip = new StatusStrip
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5F),
                SizingGrip = false
            };
            _lblStatusLeft = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _lblStatusRight = new ToolStripStatusLabel("作者：ljh | QQ：3010812967 | MIT License");
            _statusStrip.Items.AddRange(new[] { _lblStatusLeft, _lblStatusRight });
            Controls.Add(_statusStrip);
        }

        private Panel BuildConnectionPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8)
            };

            // 第一行：协议 + IP + 端口 + 连接按钮
            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            row1.Controls.Add(CreateLabel("协议：", 42));
            _cmbProtocol = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(120, 26),
                FlatStyle = FlatStyle.Flat
            };
            _cmbProtocol.Items.AddRange(new[] { "Modbus TCP", "Modbus RTU", "西门子 S7", "三菱 MC", "欧姆龙 FINS", "欧姆龙 HostLink", "罗克韦尔 CIP" });
            _cmbProtocol.SelectedIndex = 0;
            _cmbProtocol.SelectedIndexChanged += OnProtocolChanged;
            row1.Controls.Add(_cmbProtocol);

            row1.Controls.Add(CreateLabel("IP：", 28, 8));
            _txtIp = new TextBox
            {
                Text = "127.0.0.1",
                Size = new Size(110, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            row1.Controls.Add(_txtIp);

            row1.Controls.Add(CreateLabel("端口：", 36, 6));
            _numPort = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 65535,
                Value = 502,
                Size = new Size(64, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            row1.Controls.Add(_numPort);

            _btnConnect = new Button
            {
                Text = "连接",
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(70, 28),
                Cursor = Cursors.Hand,
                Margin = new Padding(12, 0, 0, 0)
            };
            _btnConnect.FlatAppearance.BorderSize = 0;
            _btnConnect.Click += BtnConnect_Click;
            row1.Controls.Add(_btnConnect);

            // 状态指示器
            _pnlStatusDot = new Panel
            {
                Size = new Size(14, 14),
                BackColor = Color.Gray,
                Margin = new Padding(10, 7, 4, 0)
            };
            _pnlStatusDot.Paint += StatusDot_Paint;
            row1.Controls.Add(_pnlStatusDot);

            _lblStatusText = new Label
            {
                Text = "未连接",
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };
            row1.Controls.Add(_lblStatusText);

            panel.Controls.Add(row1);

            // 第二行：协议参数
            _pnlParams = new Panel { Dock = DockStyle.Top, Height = 30 };
            BuildParams();
            panel.Controls.Add(_pnlParams);

            // 第三行：地址提示
            _lblAddrHint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = Color.FromArgb(59, 130, 246),
                Font = new Font("Segoe UI", 8F),
                Text = "💡 Modbus 地址：0, 1, 2 ... (寄存器偏移)"
            };
            panel.Controls.Add(_lblAddrHint);

            return panel;
        }

        private void BuildParams()
        {
            int x = 0;
            
            _lblStationId = CreateLabel("站号：", 42, 0, 3);
            _numStationId = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 1, Location = new Point(x + 42, -2), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblStationId, _numStationId });
            x += 100;

            _lblPlcType = CreateLabel("PLC型号：", 60, x, 3);
            _cmbPlcType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Location = new Point(x + 60, -3), Size = new Size(90, 24) };
            _cmbPlcType.Items.AddRange(Enum.GetNames(typeof(SiemensPLCS)));
            _cmbPlcType.SelectedIndex = 3;
            _pnlParams.Controls.AddRange(new Control[] { _lblPlcType, _cmbPlcType });
            x += 154;

            _lblLocalTsap = CreateLabel("Local：", 48, x, 3);
            _txtLocalTsap = new TextBox { Text = "10.00", Location = new Point(x + 48, -3), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblLocalTsap, _txtLocalTsap });
            x += 106;

            _lblRemoteTsap = CreateLabel("Remote：", 54, x, 3);
            _txtRemoteTsap = new TextBox { Text = "02.00", Location = new Point(x + 54, -3), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblRemoteTsap, _txtRemoteTsap });
            x += 112;

            _lblDstNode = CreateLabel("目标节点：", 60, x, 3);
            _numDstNode = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 1, Location = new Point(x + 60, -2), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblDstNode, _numDstNode });
            x += 118;

            _lblSrcNode = CreateLabel("源节点：", 48, x, 3);
            _numSrcNode = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 0, Location = new Point(x + 48, -2), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblSrcNode, _numSrcNode });
            x += 106;

            _lblUnitNo = CreateLabel("单元号：", 48, x, 3);
            _txtUnitNo = new TextBox { Text = "00", Location = new Point(x + 48, -3), Size = new Size(40, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblUnitNo, _txtUnitNo });
            x += 92;

            _lblSlot = CreateLabel("槽号：", 36, x, 3);
            _numSlot = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 0, Location = new Point(x + 36, -2), Size = new Size(54, 24), BorderStyle = BorderStyle.FixedSingle };
            _pnlParams.Controls.AddRange(new Control[] { _lblSlot, _numSlot });

            foreach (Control c in _pnlParams.Controls) c.Visible = false;
        }

        private Panel BuildLogHeader()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(12, 8, 12, 8)
            };

            var title = new Label
            {
                Text = "📜 实时日志",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Dock = DockStyle.Left,
                AutoSize = true
            };
            panel.Controls.Add(title);

            return panel;
        }

        private Panel BuildLeftPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8)
            };

            // 创建 TabControl
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                ItemSize = new Size(100, 28),
                Padding = new Point(8, 4)
            };

            var tabReadWrite = new TabPage("📝 读写操作");
            var tabMonitor = new TabPage("📊 数据监视");
            var tabBookmark = new TabPage("⭐ 地址书签");

            BuildReadWriteTab(tabReadWrite);
            BuildMonitorTab(tabMonitor);
            BuildBookmarkTab(tabBookmark);

            tabs.TabPages.AddRange(new[] { tabReadWrite, tabMonitor, tabBookmark });
            panel.Controls.Add(tabs);

            return panel;
        }

        private Panel BuildLogPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(0, 0, 0, 0)
            };

            // 工具栏
            var toolbar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(30, 41, 59)
            };

            _chkAutoScroll = new CheckBox
            {
                Text = "自动滚动",
                Checked = true,
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(8, 8),
                AutoSize = true
            };
            toolbar.Controls.Add(_chkAutoScroll);

            _btnClearLog = new Button
            {
                Text = "清空",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(100, 4),
                Size = new Size(50, 24),
                Cursor = Cursors.Hand
            };
            _btnClearLog.FlatAppearance.BorderSize = 0;
            _btnClearLog.Click += (s, e) => _rtbLog.Clear();
            toolbar.Controls.Add(_btnClearLog);

            _btnExportLog = new Button
            {
                Text = "导出",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(158, 4),
                Size = new Size(50, 24),
                Cursor = Cursors.Hand
            };
            _btnExportLog.FlatAppearance.BorderSize = 0;
            _btnExportLog.Click += (s, e) => ExportLog();
            toolbar.Controls.Add(_btnExportLog);

            panel.Controls.Add(toolbar);

            // 日志文本框
            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9F),
                ReadOnly = true,
                WordWrap = false,
                BorderStyle = BorderStyle.None
            };
            panel.Controls.Add(_rtbLog);

            return panel;
        }

        private void BuildReadWriteTab(TabPage tab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));  // 操作区
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); // 结果区
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 写入区

            // ========== 操作区 ==========
            var opPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false
            };

            row1.Controls.Add(CreateLabel("地址：", 42));
            _txtAddress = new TextBox
            {
                Text = "0",
                Size = new Size(120, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            row1.Controls.Add(_txtAddress);

            row1.Controls.Add(CreateLabel("长度：", 42, 8));
            _numLength = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 65535,
                Value = 2,
                Size = new Size(64, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            row1.Controls.Add(_numLength);

            _btnRead = CreateButton("读取", Color.FromArgb(59, 130, 246), 70);
            _btnRead.Click += BtnRead_Click;
            row1.Controls.Add(_btnRead);

            _btnReadBool = CreateButton("读位", Color.FromArgb(139, 92, 246), 70);
            _btnReadBool.Click += BtnReadBool_Click;
            row1.Controls.Add(_btnReadBool);

            opPanel.Controls.Add(row1);

            var row2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0)
            };

            _cmbWriteType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(80, 26)
            };
            _cmbWriteType.Items.AddRange(new[] { "Hex", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String", "Bool" });
            _cmbWriteType.SelectedIndex = 0;
            _cmbWriteType.SelectedIndexChanged += OnWriteTypeChanged;
            row2.Controls.Add(_cmbWriteType);

            _txtWriteValue = new TextBox
            {
                Size = new Size(180, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            row2.Controls.Add(_txtWriteValue);

            _chkBoolValue = new CheckBox
            {
                Text = "False",
                ForeColor = Color.FromArgb(100, 116, 139),
                Visible = false,
                AutoSize = true
            };
            _chkBoolValue.CheckedChanged += (s, e) => _chkBoolValue.Text = _chkBoolValue.Checked ? "True" : "False";
            row2.Controls.Add(_chkBoolValue);

            _btnWrite = CreateButton("写入", Color.FromArgb(234, 88, 12), 70);
            _btnWrite.Click += BtnWrite_Click;
            row2.Controls.Add(_btnWrite);

            _btnWriteBool = CreateButton("写位", Color.FromArgb(234, 88, 12), 70);
            _btnWriteBool.Click += BtnWriteBool_Click;
            row2.Controls.Add(_btnWriteBool);

            opPanel.Controls.Add(row2);

            layout.Controls.Add(opPanel, 0, 0);

            // ========== 结果区 ==========
            var resultPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(8)
            };

            _txtHexResult = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F),
                ReadOnly = true
            };
            resultPanel.Controls.Add(_txtHexResult);

            var resultsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            _lblInt16 = CreateResultLabel("Int16: --");
            _lblUInt16 = CreateResultLabel("UInt16: --");
            _lblInt32 = CreateResultLabel("Int32: --");
            _lblUInt32 = CreateResultLabel("UInt32: --");
            _lblInt64 = CreateResultLabel("Int64: --");
            _lblUInt64 = CreateResultLabel("UInt64: --");
            _lblFloat = CreateResultLabel("Float: --");
            _lblDouble = CreateResultLabel("Double: --");
            _lblBool = CreateResultLabel("Bool: --");
            _lblString = CreateResultLabel("String: --");
            _lblElapsed = CreateResultLabel("耗时: --");

            resultsGrid.Controls.AddRange(new Control[] {
                _lblInt16, _lblUInt16, _lblInt32, _lblUInt32,
                _lblInt64, _lblUInt64, _lblFloat, _lblDouble,
                _lblBool, _lblString, _lblElapsed
            });

            resultPanel.Controls.Add(resultsGrid);
            layout.Controls.Add(resultPanel, 0, 1);

            // ========== 写入区 ==========
            var writePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8)
            };

            var writeHint = new Label
            {
                Text = "💡 提示：选择数据类型后输入值，点击写入按钮",
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Top,
                AutoSize = true
            };
            writePanel.Controls.Add(writeHint);

            layout.Controls.Add(writePanel, 0, 2);

            tab.Controls.Add(layout);
        }

        private void BuildMonitorTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            // 工具栏
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false
            };

            toolbar.Controls.Add(CreateLabel("间隔：", 42));
            _numMonitorInterval = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 60000,
                Value = 1000,
                Increment = 100,
                Size = new Size(70, 26)
            };
            toolbar.Controls.Add(_numMonitorInterval);
            toolbar.Controls.Add(CreateLabel("ms", 24));

            _btnMonitorToggle = CreateButton("▶ 开始监视", Color.FromArgb(34, 197, 94), 100);
            _btnMonitorToggle.Click += BtnMonitorToggle_Click;
            toolbar.Controls.Add(_btnMonitorToggle);

            _btnMonitorReadOnce = CreateButton("读取一次", Color.FromArgb(59, 130, 246), 80);
            _btnMonitorReadOnce.Click += BtnMonitorReadOnce_Click;
            toolbar.Controls.Add(_btnMonitorReadOnce);

            _btnMonitorAdd = CreateButton("添加", Color.FromArgb(100, 116, 139), 60);
            _btnMonitorAdd.Click += BtnMonitorAdd_Click;
            toolbar.Controls.Add(_btnMonitorAdd);

            _btnMonitorRemove = CreateButton("移除", Color.FromArgb(239, 68, 68), 60);
            _btnMonitorRemove.Click += BtnMonitorRemove_Click;
            toolbar.Controls.Add(_btnMonitorRemove);

            panel.Controls.Add(toolbar);

            // 数据网格
            _dgvMonitor = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Font = new Font("Segoe UI", 9F)
            };
            _dgvMonitor.Columns.AddRange(new[] {
                new DataGridViewTextBoxColumn { HeaderText = "名称", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "地址", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "类型", Width = 60 },
                new DataGridViewTextBoxColumn { HeaderText = "长度", Width = 50 },
                new DataGridViewTextBoxColumn { HeaderText = "值", Width = 120 },
                new DataGridViewTextBoxColumn { HeaderText = "时间", Width = 100 }
            });
            panel.Controls.Add(_dgvMonitor);

            tab.Controls.Add(panel);
        }

        private void BuildBookmarkTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            // 工具栏
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false
            };

            toolbar.Controls.Add(CreateLabel("名称：", 42));
            _txtBookmarkName = new TextBox { Size = new Size(120, 26), BorderStyle = BorderStyle.FixedSingle };
            toolbar.Controls.Add(_txtBookmarkName);

            _btnBookmarkAdd = CreateButton("添加当前", Color.FromArgb(34, 197, 94), 80);
            _btnBookmarkAdd.Click += BtnBookmarkAdd_Click;
            toolbar.Controls.Add(_btnBookmarkAdd);

            _btnBookmarkRemove = CreateButton("删除", Color.FromArgb(239, 68, 68), 60);
            _btnBookmarkRemove.Click += BtnBookmarkRemove_Click;
            toolbar.Controls.Add(_btnBookmarkRemove);

            _btnBookmarkUse = CreateButton("使用", Color.FromArgb(59, 130, 246), 60);
            _btnBookmarkUse.Click += BtnBookmarkUse_Click;
            toolbar.Controls.Add(_btnBookmarkUse);

            panel.Controls.Add(toolbar);

            // 列表
            _lstBookmarks = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
            panel.Controls.Add(_lstBookmarks);

            tab.Controls.Add(panel);
        }

        #endregion

        #region 辅助方法

        private static Label CreateLabel(string text, int width, int x = 0, int y = 0)
        {
            return new Label
            {
                Text = text,
                Width = width,
                Location = new Point(x, y),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        private static Button CreateButton(string text, Color backColor, int width)
        {
            return new Button
            {
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 28),
                Cursor = Cursors.Hand
            };
        }

        private static Label CreateResultLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Consolas", 9F),
                ForeColor = Color.FromArgb(51, 65, 85),
                Padding = new Padding(4),
                AutoSize = true
            };
        }

        #endregion

        #region 事件处理

        private void OnProtocolChanged(object? sender, EventArgs e) => UpdateParams();

        private void UpdateParams()
        {
            foreach (Control c in _pnlParams.Controls) c.Visible = false;

            _txtAddress.Text = _cmbProtocol.SelectedIndex switch
            {
                0 or 1 => "0",
                2 => "MW0",
                3 => "D0",
                4 or 5 => "D100",
                6 => "MyTag",
                _ => "0"
            };

            _lblAddrHint.Text = _cmbProtocol.SelectedIndex switch
            {
                0 or 1 => "💡 Modbus 地址：0, 1, 2 ... (寄存器偏移) | 线圈: 0~65535 | 保持寄存器: 40001~49999",
                2 => "💡 S7 地址：DB1.DBW0 (字) | DB1.DBD0 (双字) | DB1.DBX0.0 (位) | MW0, IW0, QW0",
                3 => "💡 三菱MC 地址：D0 (数据寄存器) | M0 (继电器) | X0/Y0 (IO, 16进制)",
                4 or 5 => "💡 FINS 地址：D100/DM100 (DM区) | CIO100 | W100 | H100",
                6 => "💡 CIP 地址：直接使用标签名 (如 MyTag, MyTag[0])",
                _ => ""
            };

            switch (_cmbProtocol.SelectedIndex)
            {
                case 0 or 1:
                    _lblStationId.Visible = _numStationId.Visible = true;
                    _numPort.Value = 502;
                    break;
                case 2:
                    _lblPlcType.Visible = _cmbPlcType.Visible = true;
                    _lblLocalTsap.Visible = _txtLocalTsap.Visible = true;
                    _lblRemoteTsap.Visible = _txtRemoteTsap.Visible = true;
                    _numPort.Value = 102;
                    break;
                case 3:
                    _numPort.Value = 5006;
                    break;
                case 4:
                    _lblDstNode.Visible = _numDstNode.Visible = true;
                    _lblSrcNode.Visible = _numSrcNode.Visible = true;
                    _numPort.Value = 9600;
                    break;
                case 5:
                    _lblUnitNo.Visible = _txtUnitNo.Visible = true;
                    _numPort.Value = 9600;
                    break;
                case 6:
                    _lblSlot.Visible = _numSlot.Visible = true;
                    _numPort.Value = 44818;
                    break;
            }
        }

        private void OnWriteTypeChanged(object? sender, EventArgs e)
        {
            bool isBool = _cmbWriteType.SelectedIndex == 10;
            _txtWriteValue.Visible = !isBool;
            _chkBoolValue.Visible = isBool;
        }

        private void StatusDot_Paint(object? sender, PaintEventArgs e)
        {
            var p = (Panel)sender!;
            using var b = new SolidBrush(p.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(b, 1, 1, p.Width - 2, p.Height - 2);
        }

        #endregion

        #region 连接

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_connected)
            {
                await Disconnect();
                return;
            }

            _btnConnect.Enabled = false;
            SetStatus("orange", "连接中...");

            try
            {
                var result = await Connect();
                if (result.IsSuccess)
                {
                    _connected = true;
                    SetStatus("green", "已连接");
                    _btnConnect.Text = "断开";
                    _btnConnect.BackColor = Color.FromArgb(239, 68, 68);
                    LogInfo($"已连接到 {_txtIp.Text}:{_numPort.Value}");
                }
                else
                {
                    SetStatus("red", "连接失败");
                    LogError($"连接失败: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                SetStatus("red", "连接异常");
                LogError($"连接异常: {ex.Message}");
            }

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
                2 => new SiemensS7Net((SiemensPLCS)Enum.Parse(typeof(SiemensPLCS), _cmbPlcType.Text), ip, port)
                {
                    LocalTSAP = _txtLocalTsap.Text.Trim(),
                    RemoteTSAP = _txtRemoteTsap.Text.Trim()
                },
                3 => new MitsubishiMcNet(ip, port),
                4 => new OmronFinsNet(ip, port)
                {
                    DstNode = (byte)_numDstNode.Value,
                    SrcNode = (byte)_numSrcNode.Value
                },
                5 => new OmronHostLinkNet(ip, port) { UnitNumber = _txtUnitNo.Text.Trim() },
                6 => new AllenBradleyCipNet(ip, port) { Slot = (byte)_numSlot.Value },
                _ => throw new InvalidOperationException()
            };

            if (_device is NetworkDeviceBase dev)
            {
                _deviceBase = dev;
                dev.EnableTrace = true;
                dev.ConnectTimeout = 5000;
                dev.SendTimeout = 3000;
                dev.ReceiveTimeout = 5000;
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
                await _device.DisconnectAsync();
                _device = null;
                _deviceBase = null;
            }

            _connected = false;
            _btnConnect.Text = "连接";
            _btnConnect.BackColor = Color.FromArgb(34, 197, 94);
            SetStatus("gray", "未连接");
            btnState();
        }

        private void SetStatus(string color, string text)
        {
            _lblStatusText.Text = text;
            _pnlStatusDot.BackColor = color switch
            {
                "green" => Color.FromArgb(34, 197, 94),
                "red" => Color.FromArgb(239, 68, 68),
                "orange" => Color.FromArgb(245, 158, 11),
                _ => Color.Gray
            };
            _pnlStatusDot.Invalidate();
        }

        private void btnState()
        {
            _btnRead.Enabled = _connected;
            _btnWrite.Enabled = _connected;
            _btnReadBool.Enabled = _connected;
            _btnWriteBool.Enabled = _connected;
        }

        #endregion

        #region 读写操作

        private async void BtnRead_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            string addr = _txtAddress.Text.Trim();
            int len = (int)_numLength.Value;

            _btnRead.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _device!.ReadAsync(addr, (ushort)len);
                sw.Stop();

                if (result.IsSuccess && result.Content != null)
                {
                    ShowResult(result.Content, sw.ElapsedMilliseconds);
                    LogInfo($"读取成功: {addr}, {len}B, {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    ClearResult();
                    LogError($"读取失败: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                ClearResult();
                LogError($"读取异常: {ex.Message}");
            }
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
                var result = await _device!.ReadBoolAsync(addr);
                sw.Stop();

                if (result.IsSuccess)
                {
                    _lblBool.Text = $"Bool: {result.Content}";
                    _lblElapsed.Text = $"耗时: {sw.ElapsedMilliseconds}ms";
                    LogInfo($"ReadBool: {addr} = {result.Content}, {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    _lblBool.Text = "Bool: --";
                    LogError($"ReadBool失败: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                LogError($"ReadBool异常: {ex.Message}");
            }
            _btnReadBool.Enabled = true;
        }

        private async void BtnWrite_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            byte[]? data = ParseValue();
            if (data == null) return;

            string addr = _txtAddress.Text.Trim();
            _btnWrite.Enabled = false;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _device!.WriteAsync(addr, data);
                sw.Stop();

                if (result.IsSuccess)
                    LogInfo($"写入成功: {addr}, {data.Length}B, {sw.ElapsedMilliseconds}ms");
                else
                    LogError($"写入失败: {result.Message}");
            }
            catch (Exception ex)
            {
                LogError($"写入异常: {ex.Message}");
            }
            _btnWrite.Enabled = true;
        }

        private async void BtnWriteBool_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            string addr = _txtAddress.Text.Trim();
            bool val = _chkBoolValue.Checked;

            _btnWriteBool.Enabled = false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _device!.WriteAsync(addr, val);
                sw.Stop();

                if (result.IsSuccess)
                    LogInfo($"WriteBool: {addr} = {val}, {sw.ElapsedMilliseconds}ms");
                else
                    LogError($"WriteBool失败: {result.Message}");
            }
            catch (Exception ex)
            {
                LogError($"WriteBool异常: {ex.Message}");
            }
            _btnWriteBool.Enabled = true;
        }

        private void ShowResult(byte[] data, long ms)
        {
            _txtHexResult.Text = string.Join(" ", data.Select(b => b.ToString("X2")));
            _lblStatusRight.Text = $"最后: {ms}ms  {data.Length}B";
            _lblElapsed.Text = $"耗时: {ms}ms";

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
            _lblElapsed.Text = "耗时: --";
            foreach (var l in new[] { _lblInt16, _lblUInt16, _lblInt32, _lblUInt32, _lblInt64, _lblUInt64, _lblFloat, _lblDouble, _lblBool, _lblString })
                l.Text = l.Text.Split(':')[0] + ": --";
        }

        private byte[]? ParseValue()
        {
            try
            {
                return _cmbWriteType.SelectedIndex switch
                {
                    0 => PlcCommunication.Utilities.SoftBasic.HexStringToBytes(_txtWriteValue.Text),
                    1 => _deviceBase?.ByteTransform.GetBytes(short.Parse(_txtWriteValue.Text)),
                    2 => _deviceBase?.ByteTransform.GetBytes(ushort.Parse(_txtWriteValue.Text)),
                    3 => _deviceBase?.ByteTransform.GetBytes(int.Parse(_txtWriteValue.Text)),
                    4 => _deviceBase?.ByteTransform.GetBytes(uint.Parse(_txtWriteValue.Text)),
                    5 => _deviceBase?.ByteTransform.GetBytes(long.Parse(_txtWriteValue.Text)),
                    6 => _deviceBase?.ByteTransform.GetBytes(ulong.Parse(_txtWriteValue.Text)),
                    7 => _deviceBase?.ByteTransform.GetBytes(float.Parse(_txtWriteValue.Text)),
                    8 => _deviceBase?.ByteTransform.GetBytes(double.Parse(_txtWriteValue.Text)),
                    9 => Encoding.ASCII.GetBytes(_txtWriteValue.Text),
                    10 => _chkBoolValue.Checked ? new byte[] { 0x01 } : new byte[] { 0x00 },
                    _ => null
                };
            }
            catch (FormatException) { LogError("数值格式错误"); return null; }
            catch (Exception ex) { LogError($"解析错误: {ex.Message}"); return null; }
        }

        private bool CheckConn()
        {
            if (!_connected)
            {
                LogWarn("请先连接设备");
                return false;
            }
            return true;
        }

        #endregion

        #region 监视

        private void BtnMonitorToggle_Click(object? sender, EventArgs e)
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
            _btnMonitorToggle.BackColor = Color.FromArgb(239, 68, 68);

            int interval = (int)_numMonitorInterval.Value;
            _monitorTimer = new System.Windows.Forms.Timer { Interval = interval };
            _monitorTimer.Tick += async (s, e) => await ReadAllMonitorRows();
            _monitorTimer.Start();
        }

        private void StopMonitor()
        {
            _monitorRunning = false;
            _btnMonitorToggle.Text = "▶ 开始监视";
            _btnMonitorToggle.BackColor = Color.FromArgb(34, 197, 94);

            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _monitorTimer = null;
        }

        private async Task ReadAllMonitorRows()
        {
            if (_device == null || _dgvMonitor == null || _dgvMonitor.IsDisposed) return;

            foreach (DataGridViewRow row in _dgvMonitor.Rows)
            {
                if (row.IsNewRow) continue;

                string? addr = row.Cells[1].Value?.ToString();
                if (string.IsNullOrEmpty(addr)) continue;

                var result = await _device.ReadAsync(addr, 2);
                if (result.IsSuccess && result.Content != null)
                {
                    row.Cells[4].Value = PlcCommunication.Utilities.SoftBasic.BytesToHexString(result.Content);
                    row.Cells[5].Value = DateTime.Now.ToString("HH:mm:ss.fff");
                }
            }
        }

        private async void BtnMonitorReadOnce_Click(object? sender, EventArgs e)
        {
            if (!CheckConn()) return;
            await ReadAllMonitorRows();
        }

        private void BtnMonitorAdd_Click(object? sender, EventArgs e)
        {
            string addr = _txtAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr))
            {
                LogWarn("请先输入地址");
                return;
            }
            _dgvMonitor.Rows.Add("监控项", addr, "Hex", "2", "--", "--");
        }

        private void BtnMonitorRemove_Click(object? sender, EventArgs e)
        {
            if (_dgvMonitor.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in _dgvMonitor.SelectedRows)
                    if (!row.IsNewRow) _dgvMonitor.Rows.Remove(row);
            }
        }

        #endregion

        #region 书签

        private void BtnBookmarkAdd_Click(object? sender, EventArgs e)
        {
            string name = string.IsNullOrWhiteSpace(_txtBookmarkName.Text)
                ? _txtAddress.Text
                : _txtBookmarkName.Text;

            _lstBookmarks.Items.Add($"{name}|{_txtAddress.Text}|{_numLength.Value}");
            LogInfo($"添加书签: {name}");
        }

        private void BtnBookmarkRemove_Click(object? sender, EventArgs e)
        {
            if (_lstBookmarks.SelectedIndex >= 0)
                _lstBookmarks.Items.RemoveAt(_lstBookmarks.SelectedIndex);
        }

        private void BtnBookmarkUse_Click(object? sender, EventArgs e)
        {
            if (_lstBookmarks.SelectedIndex < 0) return;

            string? item = _lstBookmarks.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(item)) return;

            var parts = item.Split('|');
            if (parts.Length >= 3)
            {
                _txtAddress.Text = parts[1];
                _numLength.Value = decimal.TryParse(parts[2], out var len) ? len : 2;
            }
        }

        #endregion

        #region 日志

        private void LogHandler(TraceEventArgs e)
        {
            if (_rtbLog.IsDisposed) return;
            _rtbLog.Invoke(() => AppendLog(e));
        }

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
                _ => Color.LightGray
            };
            _rtbLog.AppendText(line + "\n");

            if (_chkAutoScroll.Checked)
            {
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.ScrollToCaret();
            }
        }

        private void LogInfo(string m) => Log(new TraceEventArgs(TraceLevel.Info, m));
        private void LogError(string m) => Log(new TraceEventArgs(TraceLevel.Error, m));
        private void LogWarn(string m) => Log(new TraceEventArgs(TraceLevel.Warning, m));

        private void Log(TraceEventArgs e)
        {
            if (!_rtbLog.IsDisposed)
                _rtbLog.Invoke(() => AppendLog(e));
        }

        private void ExportLog()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"plc_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, _rtbLog.Text, Encoding.UTF8);
                    LogInfo($"日志已导出: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    LogError($"导出失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region 模拟器

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
                            _cmbProtocol.SelectedIndex = 3;
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
                            _cmbProtocol.SelectedIndex = 2;
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
                            _cmbProtocol.SelectedIndex = 0;
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
                _mitsubishiSim?.IsRunning == true ? "✓ 停止三菱MC模拟器" : "三菱MC模拟器 (端口5006)";
            ((ToolStripMenuItem)_menuSimulator.DropDownItems[1]).Text =
                _siemensSim?.IsRunning == true ? "✓ 停止西门子S7模拟器" : "西门子S7模拟器 (端口102)";
            ((ToolStripMenuItem)_menuSimulator.DropDownItems[2]).Text =
                _modbusSim?.IsRunning == true ? "✓ 停止Modbus TCP模拟器" : "Modbus TCP模拟器 (端口502)";
        }

        #endregion

        #region 配置

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (cfg != null)
                    {
                        if (cfg.TryGetValue("protocol", out var p) && int.TryParse(p?.ToString(), out int proto))
                            _cmbProtocol.SelectedIndex = proto;
                        if (cfg.TryGetValue("ip", out var ip))
                            _txtIp.Text = ip?.ToString() ?? "127.0.0.1";
                        if (cfg.TryGetValue("port", out var port) && int.TryParse(port?.ToString(), out int pt))
                            _numPort.Value = pt;
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var cfg = new Dictionary<string, object>
                {
                    ["protocol"] = _cmbProtocol.SelectedIndex,
                    ["ip"] = _txtIp.Text,
                    ["port"] = _numPort.Value
                };
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        #endregion

        #region 窗体关闭

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

        #endregion
    }
}
