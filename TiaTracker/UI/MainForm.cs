using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaTracker.Core;

namespace TiaTracker.UI
{
    public class MainForm : Form
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private TextBox              _txtPath;
        private TextBox              _txtSearch;
        private Button               _btnBrowse, _btnRun, _btnSaveXml, _btnSaveMd, _btnTcp;
        private ProgressBar          _progress;
        private TreeView             _tree;
        private RichTextBox          _rtbDetail;
        private RichTextBox          _rtbLog;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripStatusLabel _lblStats;
        private Panel                _detailHeader;
        private Label                _lblDetailTitle;
        private Label                _lblDetailMeta;

        // ── State ─────────────────────────────────────────────────────────────
        private bool               _running;
        private bool               _lockWindowPos;   // impede resize/move durante ligação ao TIA
        private string             _xmlResult;
        private string             _mdResult;
        private TiaConnection      _conn;
        private List<BlockInfo>    _blocks    = new List<BlockInfo>();
        private List<TagTableInfo> _tagTables = new List<TagTableInfo>();
        private List<UdtInfo>      _udts      = new List<UdtInfo>();
        private List<HwDeviceInfo> _hwDevices = new List<HwDeviceInfo>();
        private Dictionary<string, List<ProjectReader.CallEdge>> _callGraph = new Dictionary<string, List<ProjectReader.CallEdge>>();

        // Tags especiais para nós da TreeView
        private class CallGraphTag  { public string Device; }
        private class HardwareTag   { public string Device; }

        // Ícones PNG embebidos
        private readonly Dictionary<string, Bitmap> _ico = new Dictionary<string, Bitmap>();

        // ── Win32 ─────────────────────────────────────────────────────────────
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        const int  WM_WINDOWPOSCHANGING = 0x0046;
        const uint SWP_NOSIZE           = 0x0001;
        const uint SWP_NOMOVE           = 0x0002;
        const uint SWP_NOZORDER         = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int    x, y, cx, cy;
            public uint   flags;
        }

        protected override void WndProc(ref Message m)
        {
            if (_lockWindowPos && m.Msg == WM_WINDOWPOSCHANGING)
            {
                var pos = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                pos.flags |= SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER;
                Marshal.StructureToPtr(pos, m.LParam, false);
            }
            base.WndProc(ref m);
        }

        // ── Theme — Antigravity Dark ───────────────────────────────────────────
        static readonly Color BG      = Color.FromArgb( 13,  17,  23);   // #0D1117 — fundo
        static readonly Color PANEL   = Color.FromArgb( 22,  27,  34);   // #161B22 — painéis
        static readonly Color SURFACE = Color.FromArgb( 33,  38,  50);   // #212632 — inputs/cards
        static readonly Color BORDER  = Color.FromArgb( 48,  54,  61);   // #30363D — bordas
        static readonly Color TREE_BG = Color.FromArgb( 22,  27,  34);   // #161B22
        static readonly Color LOG_BG  = Color.FromArgb( 13,  17,  23);   // #0D1117
        static readonly Color C_OB    = Color.FromArgb(121, 192, 255);   // #79C0FF  azul
        static readonly Color C_FB    = Color.FromArgb( 86, 211, 100);   // #56D364  verde
        static readonly Color C_FC    = Color.FromArgb(227, 179,  65);   // #E3B341  âmbar
        static readonly Color C_DB    = Color.FromArgb(247, 129, 102);   // #F78166  coral
        static readonly Color C_TAGS  = Color.FromArgb(121, 192, 255);   // #79C0FF  azul
        static readonly Color C_UDT   = Color.FromArgb(210, 168, 255);   // #D2A8FF  violeta
        static readonly Color C_NET   = Color.FromArgb( 86, 211, 100);   // #56D364  verde
        static readonly Color C_IFACE = Color.FromArgb(139, 148, 158);   // #8B949E  cinza médio
        static readonly Color C_TEXT  = Color.FromArgb(230, 237, 243);   // #E6EDF3  texto
        static readonly Color C_MUTED = Color.FromArgb(125, 133, 144);   // #7D8590  texto apagado
        static readonly Color C_OK    = Color.FromArgb( 86, 211, 100);   // #56D364  verde
        static readonly Color C_ERR   = Color.FromArgb(248,  81,  73);   // #F85149  vermelho
        static readonly Color C_GOLD  = Color.FromArgb(227, 179,  65);   // #E3B341  âmbar device
        static readonly Color C_GROUP = Color.FromArgb(139, 148, 158);   // #8B949E  cinza
        static readonly Color C_BLUE  = Color.FromArgb( 31, 111, 235);   // #1F6FEB  accent
        static readonly Color C_SEL   = Color.FromArgb( 31,  45,  63);   // #1F2D3F  seleção

        // ── Constructor ───────────────────────────────────────────────────────
        public MainForm() { BuildUI(); LoadIcons(); }

        private void LoadIcons()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            void Load(string key, string resource)
            {
                try
                {
                    using var s = asm.GetManifestResourceStream(resource);
                    if (s != null) _ico[key] = new Bitmap(s);
                }
                catch { }
            }
            const string base_ = "TiaTracker.Resources.Icons.";
            Load("OB",          base_ + "icon_ob.png");
            Load("FB",          base_ + "icon_fb.png");
            Load("FC",          base_ + "icon_fc.png");
            Load("DB",          base_ + "icon_db.png");
            Load("TAG",         base_ + "icon_tags.png");
            Load("UDT",         base_ + "icon_udt.png");
            Load("DEVICE",      base_ + "icon_device.png");
            Load("HARDWARE",    base_ + "icon_hardware.png");
            Load("FOLDER",      base_ + "icon_folder_FC_FB_DB.png");
            Load("FOLDER_TAGS", base_ + "icon_folder_tag_tables.png");
            Load("FOLDER_UDT",  base_ + "icon_folder_Udt.png");
        }

        // ── UI Construction ───────────────────────────────────────────────────
        private void BuildUI()
        {
            Text          = "Danilo Tracker  —  TIA Portal Project Reader";
            Size          = new Size(1400, 880);
            MinimumSize   = new Size(900, 580);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = C_TEXT;
            Font          = new Font("Segoe UI", 9f);
            Icon          = MakeIcon();

            // ── Header — barra escura simples estilo VS Code ──────────────────
            var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = PANEL };
            header.Paint += (s, e) =>
            {
                var g  = e.Graphics;
                var rc = header.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;
                using (var grad = new LinearGradientBrush(rc,
                    Color.FromArgb(28, 33, 50), PANEL, LinearGradientMode.Vertical))
                    g.FillRectangle(grad, rc);
                using (var pen = new Pen(C_BLUE, 1))
                    g.DrawLine(pen, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
            };

            var pnlHeaderLeft = new Panel { Dock = DockStyle.Left, Width = 50, BackColor = Color.Transparent };
            pnlHeaderLeft.Paint += (s, e) =>
            {
                var g  = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = pnlHeaderLeft.Width  / 2;
                int cy = pnlHeaderLeft.Height / 2;
                int r  = 16;
                var circleRect = new Rectangle(cx - r, cy - r, r * 2, r * 2);
                if (circleRect.Width <= 0 || circleRect.Height <= 0) return;
                using (var grad = new LinearGradientBrush(circleRect,
                    Color.FromArgb(31, 111, 235), Color.FromArgb(120, 72, 210),
                    LinearGradientMode.Vertical))
                    g.FillEllipse(grad, circleRect);
                using (var glow = new Pen(Color.FromArgb(60, 88, 166, 255), 1.5f))
                    g.DrawEllipse(glow, circleRect);
                using var f  = new Font("Segoe UI", 14f, FontStyle.Bold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var sb = new SolidBrush(Color.White);
                g.DrawString("D", f, sb, new RectangleF(circleRect.X, circleRect.Y, circleRect.Width, circleRect.Height), sf);
            };

            var lblTitle = new Label
            {
                Text      = "DANILO TRACKER",
                Dock      = DockStyle.Left,
                Width     = 200,
                Font      = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = C_TEXT,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            var lblSub = new Label
            {
                Text      = "TIA Portal V18  ·  Leitura offline de projeto PLC  ·  Exportação XML & Markdown",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = C_MUTED,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblSub);
            header.Controls.Add(lblTitle);
            header.Controls.Add(pnlHeaderLeft);

            // ── Toolbar ───────────────────────────────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = PANEL,
                Padding   = new Padding(10, 6, 10, 6)
            };

            var tbl = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 7,
                RowCount    = 1,
                Margin      = Padding.Empty,
                Padding     = Padding.Empty,
            };
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,  62));   // label
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));   // path
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,  38));   // ...
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));   // run
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));   // xml
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 162));   // IA
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));   // criar bloco

            var lblPath = new Label
            {
                Text      = "Projeto:",
                Dock      = DockStyle.Fill,
                ForeColor = C_MUTED,
                TextAlign = ContentAlignment.MiddleRight,
                Font      = new Font("Segoe UI", 8.5f)
            };

            _txtPath = new TextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = SURFACE,
                ForeColor   = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Consolas", 8.8f),
                Text        = @"C:\Users\Admin\Desktop\C#_PLC\C#_PLC.ap18",
                Margin      = new Padding(6, 0, 6, 0)
            };

            _btnBrowse  = MkBtn("···",               38, SURFACE);
            _btnRun     = MkBtn("▶  Conectar e Ler", 174, C_BLUE);
            _btnSaveXml = MkBtn("Salvar XML",         138, SURFACE);
            _btnSaveMd  = MkBtn("Exportar para IA",   158, SURFACE);
            _btnTcp     = MkBtn("⬡  Servidor TCP",    148, Color.FromArgb(20, 80, 100));

            _btnRun.Font        = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnRun.Margin      = new Padding(4, 0, 4, 0);
            _btnSaveXml.Enabled = false;
            _btnSaveMd.Enabled  = false;

            _btnBrowse.Click  += (s, e) => BrowseProject();
            _btnRun.Click     += async (s, e) => await RunAsync();
            _btnSaveXml.Click += (s, e) => SaveXml();
            _btnSaveMd.Click  += (s, e) => SaveMd();
            _btnTcp.Click     += (s, e) => OpenTcpServer();

            tbl.Controls.Add(lblPath,     0, 0);
            tbl.Controls.Add(_txtPath,    1, 0);
            tbl.Controls.Add(_btnBrowse,  2, 0);
            tbl.Controls.Add(_btnRun,     3, 0);
            tbl.Controls.Add(_btnSaveXml, 4, 0);
            tbl.Controls.Add(_btnSaveMd,  5, 0);
            tbl.Controls.Add(_btnTcp,     6, 0);
            toolbar.Controls.Add(tbl);

            _progress = new ProgressBar
            {
                Dock      = DockStyle.Top,
                Height    = 3,
                Style     = ProgressBarStyle.Continuous,
                Minimum   = 0,
                Maximum   = 100,
                Value     = 0,
                BackColor  = PANEL,
                ForeColor  = C_BLUE
            };
            _progress.HandleCreated += (s, e) => SetWindowTheme(_progress.Handle, "", "");

            // ── StatusStrip ───────────────────────────────────────────────────
            var strip = new StatusStrip { BackColor = Color.FromArgb(18, 22, 30), SizingGrip = false };
            _lblStatus = new ToolStripStatusLabel
            {
                Text      = "  Pronto. Selecione um projeto e clique em  ▶ Conectar e Ler.",
                ForeColor = C_TEXT,
                Spring    = true
            };
            _lblStats = new ToolStripStatusLabel { Text = "", ForeColor = C_BLUE, Spring = false };
            strip.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblStats });

            // ── Outer split ───────────────────────────────────────────────────
            var outerSplit = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = Orientation.Vertical,
                SplitterWidth = 1,
                BackColor     = BORDER,
            };

            // ── Left panel ────────────────────────────────────────────────────
            var treeTitle = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 28,
                BackColor = PANEL
            };
            treeTitle.Paint += (s, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(PANEL), treeTitle.ClientRectangle);
                using var f = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                using var b = new SolidBrush(C_MUTED);
                e.Graphics.DrawString("  EXPLORADOR", f, b, 2, 8);
            };

            _txtSearch = new TextBox
            {
                Dock        = DockStyle.Top,
                Height      = 26,
                BackColor   = SURFACE,
                ForeColor   = C_MUTED,
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 9f),
                Text        = "  Filtrar blocos...",
                Padding     = new Padding(4, 0, 0, 0)
            };
            _txtSearch.GotFocus += (s, e) =>
            {
                if (_txtSearch.Text.Contains("Filtrar")) { _txtSearch.Text = ""; _txtSearch.ForeColor = C_TEXT; }
            };
            _txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSearch.Text)) { _txtSearch.Text = "  Filtrar blocos..."; _txtSearch.ForeColor = C_MUTED; }
            };
            _txtSearch.TextChanged += (s, e) => FilterTree(_txtSearch.Text);

            var searchBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BORDER };

            _tree = new TreeView
            {
                Dock          = DockStyle.Fill,
                BackColor     = TREE_BG,
                ForeColor     = C_TEXT,
                Font          = new Font("Segoe UI", 9f),
                BorderStyle   = BorderStyle.None,
                ItemHeight    = 24,
                ShowLines     = false,
                ShowRootLines = false,
                ShowPlusMinus = true,
                FullRowSelect = true,
                HideSelection = false,
                Indent        = 18,
                DrawMode      = TreeViewDrawMode.OwnerDrawAll
            };
            _tree.DrawNode    += DrawTreeNode;
            _tree.AfterSelect += OnTreeSelect;

            // ── Context menu: botão direito na árvore ─────────────────────────
            var ctxMenu = new ContextMenuStrip { BackColor = PANEL, ForeColor = C_TEXT, ShowImageMargin = false };
            ctxMenu.Renderer = new DarkMenuRenderer();

            var ctxExportXml = new ToolStripMenuItem("Exportar XML do bloco...") { ForeColor = C_TEXT };
            ctxExportXml.Click += (s, e) => ExportSelectedBlockXml();
            ctxMenu.Items.Add(ctxExportXml);

            ctxMenu.Items.Add(new ToolStripSeparator());

            var ctxExportDevMd = new ToolStripMenuItem("📤  Exportar este PLC para IA (Markdown)...") { ForeColor = Color.FromArgb(220, 180, 80) };
            ctxExportDevMd.Click += (s, e) => ExportDeviceMd(_tree.SelectedNode?.Tag as string == "device" ? _tree.SelectedNode?.Text?.Trim() : null);
            ctxMenu.Items.Add(ctxExportDevMd);

            var ctxExportTagsMd = new ToolStripMenuItem("🏷  Exportar Tags deste PLC para IA (ficheiro único)...") { ForeColor = Color.FromArgb(130, 210, 130) };
            ctxExportTagsMd.Click += (s, e) =>
            {
                var node       = _tree.SelectedNode;
                var deviceName = node?.Parent?.Text?.Trim();
                if (!string.IsNullOrEmpty(deviceName)) ExportTagsMd(deviceName);
            };
            ctxMenu.Items.Add(ctxExportTagsMd);

            var ctxExportTagsMulti = new ToolStripMenuItem("📁  Exportar Tags deste PLC para IA (multi-ficheiro)...") { ForeColor = Color.FromArgb(100, 200, 255) };
            ctxExportTagsMulti.Click += (s, e) =>
            {
                var node       = _tree.SelectedNode;
                var deviceName = node?.Parent?.Text?.Trim();
                if (!string.IsNullOrEmpty(deviceName)) ExportTagsMultiMd(deviceName);
            };
            ctxMenu.Items.Add(ctxExportTagsMulti);

            _tree.ContextMenuStrip = ctxMenu;
            ctxMenu.Opening += (s, e) =>
            {
                var node = _tree.SelectedNode;
                ctxExportXml.Enabled      = node?.Tag is BlockInfo b && !string.IsNullOrEmpty(b.RawXml);
                ctxExportDevMd.Enabled    = node?.Tag as string == "device";
                ctxExportTagsMd.Enabled   = node?.Tag as string == "group:tags";
                ctxExportTagsMulti.Enabled = node?.Tag as string == "group:tags";
            };

            outerSplit.Panel1.Controls.Add(_tree);
            outerSplit.Panel1.Controls.Add(searchBorder);
            outerSplit.Panel1.Controls.Add(_txtSearch);
            outerSplit.Panel1.Controls.Add(treeTitle);
            outerSplit.Panel1.BackColor = TREE_BG;

            // ── Right panel ───────────────────────────────────────────────────
            var innerSplit = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = Orientation.Horizontal,
                SplitterWidth = 1,
                BackColor     = BORDER,
                FixedPanel    = FixedPanel.Panel2
            };

            // Detail header card
            _detailHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 48,
                BackColor = PANEL,
                Visible   = false
            };
            _detailHeader.Paint += (s, e) =>
            {
                var rc = _detailHeader.ClientRectangle;
                using var pen = new Pen(BORDER, 1);
                e.Graphics.DrawLine(pen, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
            };
            _lblDetailTitle = new Label
            {
                Location  = new Point(14, 8),
                Size      = new Size(800, 22),
                Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize  = false
            };
            _lblDetailMeta = new Label
            {
                Location  = new Point(14, 28),
                Size      = new Size(800, 16),
                Font      = new Font("Segoe UI", 8f),
                ForeColor = C_MUTED,
                BackColor = Color.Transparent,
                AutoSize  = false
            };
            _detailHeader.Controls.Add(_lblDetailTitle);
            _detailHeader.Controls.Add(_lblDetailMeta);

            _rtbDetail = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BG,
                ForeColor   = C_TEXT,
                Font        = new Font("Consolas", 9.5f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Both,
                WordWrap    = false
            };

            var detailPanel = new Panel { Dock = DockStyle.Fill };
            detailPanel.Controls.Add(_rtbDetail);
            detailPanel.Controls.Add(_detailHeader);
            innerSplit.Panel1.Controls.Add(detailPanel);

            // Log panel
            var logHeader = new Label
            {
                Text      = "  OUTPUT",
                Dock      = DockStyle.Top,
                Height    = 22,
                BackColor = PANEL,
                ForeColor = C_MUTED,
                Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _rtbLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = LOG_BG,
                ForeColor   = C_MUTED,
                Font        = new Font("Consolas", 8.5f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                WordWrap    = true
            };
            innerSplit.Panel2.Controls.Add(_rtbLog);
            innerSplit.Panel2.Controls.Add(logHeader);
            innerSplit.Panel2.BackColor = LOG_BG;

            outerSplit.Panel2.Controls.Add(innerSplit);

            Load += (s, e) =>
            {
                outerSplit.SplitterDistance = 300;
                innerSplit.SplitterDistance = Math.Max(120, innerSplit.Height - 130);
            };

            // ── Montar tudo ───────────────────────────────────────────────────
            Controls.Add(outerSplit);
            Controls.Add(strip);
            Controls.Add(_progress);
            Controls.Add(toolbar);
            Controls.Add(header);

            // Hint inicial
            DetailLine("\n  Selecione um projeto .ap18 e clique em  ▶ Conectar e Ler", C_GROUP);
            DetailLine("  Após a leitura, a estrutura aparece na árvore à esquerda.", C_GROUP);
            DetailLine("  Clique num bloco para ver interface e redes.", C_GROUP);
        }

        // ── Owner-draw TreeView ───────────────────────────────────────────────
        private void DrawTreeNode(object sender, DrawTreeNodeEventArgs e)
        {
            var node = e.Node;
            var g    = e.Graphics;
            var rc   = e.Bounds;
            if (rc.Width == 0) { e.DrawDefault = true; return; }

            bool selected = (e.State & TreeNodeStates.Selected) != 0;

            // fundo
            g.FillRectangle(new SolidBrush(selected ? C_SEL : TREE_BG),
                new Rectangle(0, rc.Y, _tree.Width, rc.Height));

            // borda esquerda na seleção
            if (selected)
            {
                using var pen = new Pen(C_BLUE, 2);
                g.DrawLine(pen, 0, rc.Y, 0, rc.Bottom);
            }

            // sinal +/- (expand/collapse)
            if (node.Nodes.Count > 0)
            {
                int midY   = rc.Y + rc.Height / 2;
                int arrowX = rc.X - 14;
                using var pen = new Pen(C_MUTED, 1);
                if (node.IsExpanded)
                {
                    g.DrawLine(pen, arrowX - 3, midY - 2, arrowX,     midY + 3);
                    g.DrawLine(pen, arrowX,     midY + 3, arrowX + 3, midY - 2);
                }
                else
                {
                    g.DrawLine(pen, arrowX - 2, midY - 3, arrowX + 3, midY);
                    g.DrawLine(pen, arrowX + 3, midY,     arrowX - 2, midY + 3);
                }
            }

            // ícone + offset de texto
            int iconX = rc.X + 2;
            int textX = rc.X + 24;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            if (node.Tag is BlockInfo bi)
            {
                var key = bi.Type == "OB" ? "OB" : bi.Type == "FB" ? "FB" : bi.Type == "FC" ? "FC" : "DB";
                if (_ico.TryGetValue(key, out var img))
                    DrawPng(g, img, iconX, rc.Y, rc.Height);
                else
                {
                    Color col = bi.Type == "OB" ? C_OB : bi.Type == "FB" ? C_FB : bi.Type == "FC" ? C_FC : C_DB;
                    DrawIconDocument(g, iconX, rc.Y, rc.Height, col, bi.Type);
                }
            }
            else if (node.Tag is TagTableInfo)
            {
                if (_ico.TryGetValue("TAG", out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                else DrawIconDocument(g, iconX, rc.Y, rc.Height, C_TAGS, "TAG");
            }
            else if (node.Tag is UdtInfo)
            {
                if (_ico.TryGetValue("UDT", out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                else DrawIconDocument(g, iconX, rc.Y, rc.Height, C_UDT, "UDT");
            }
            else if (node.Tag is HardwareTag)
            {
                if (_ico.TryGetValue("HARDWARE", out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                else DrawIconHardware(g, iconX, rc.Y, rc.Height, C_OB);
            }
            else if (node.Tag is CallGraphTag)
                DrawIconCallGraph(g, iconX, rc.Y, rc.Height, C_GOLD);
            else if (node.Tag is string kind)
            {
                if (kind == "device")
                {
                    if (_ico.TryGetValue("DEVICE", out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                    else DrawIconDevice(g, iconX, rc.Y, rc.Height);
                }
                else if (kind == "folder")
                {
                    if (_ico.TryGetValue("FOLDER", out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                    else DrawIconFolder(g, iconX, rc.Y, rc.Height, C_GOLD, node.IsExpanded);
                }
                else if (kind.StartsWith("group:"))
                {
                    var t = kind.Substring(6);
                    string fKey = t == "tags" ? "FOLDER_TAGS" : t == "udt" ? "FOLDER_UDT" : "FOLDER";
                    if (_ico.TryGetValue(fKey, out var img)) DrawPng(g, img, iconX, rc.Y, rc.Height);
                    else
                    {
                        Color col = t == "OB" ? C_OB : t == "FB" ? C_FB : t == "FC" ? C_FC :
                                    t == "DB" ? C_DB : t == "tags" ? C_TAGS : C_UDT;
                        DrawIconGroup(g, iconX, rc.Y, rc.Height, col);
                    }
                }
                else textX = rc.X + 4;
            }
            else textX = rc.X + 4;

            g.SmoothingMode = SmoothingMode.Default;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;

            // texto
            var textColor = node.ForeColor == Color.Empty ? C_TEXT : node.ForeColor;
            var font      = node.NodeFont ?? _tree.Font;
            TextRenderer.DrawText(g, node.Text, font,
                new Rectangle(textX, rc.Y, rc.Right - textX, rc.Height),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        // ── Ícones do TreeView (GDI+) ─────────────────────────────────────────

        /// <summary>Desenha um PNG centrado verticalmente no item da TreeView.</summary>
        private static void DrawPng(Graphics g, Bitmap img, int x, int y, int itemHeight)
        {
            int sz   = Math.Min(20, itemHeight - 4);
            int offY = y + (itemHeight - sz) / 2;
            g.DrawImage(img, x, offY, sz, sz);
        }

        /// <summary>Documento com borda colorida e etiqueta de tipo (OB/FB/FC/DB/TAG/UDT).</summary>
        private static void DrawIconDocument(Graphics g, int x, int y, int h, Color color, string label = null)
        {
            int midY = y + h / 2;
            int iw = 12, ih = 14, fold = 4;
            int ix = x, iy = midY - ih / 2;

            // corpo
            using var bodyBr = new SolidBrush(Color.FromArgb(50, 57, 76));
            g.FillRectangle(bodyBr, ix, iy, iw, ih);

            // canto dobrado
            using var foldBr = new SolidBrush(Color.FromArgb(72, 82, 105));
            g.FillPolygon(foldBr, new Point[] {
                new Point(ix + iw - fold, iy),
                new Point(ix + iw,        iy + fold),
                new Point(ix + iw - fold, iy + fold)
            });

            // borda esquerda colorida
            using var edgeBr = new SolidBrush(color);
            g.FillRectangle(edgeBr, ix, iy, 3, ih);

            // etiqueta de tipo
            if (!string.IsNullOrEmpty(label))
            {
                using var tf = new Font("Segoe UI", 4.5f, FontStyle.Bold);
                var clamp = Color.FromArgb(210, Math.Min(255, color.R + 30),
                                                Math.Min(255, color.G + 30),
                                                Math.Min(255, color.B + 30));
                TextRenderer.DrawText(g, label,
                    tf, new Rectangle(ix + 3, iy + 2, iw - 4, ih - 4), clamp,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>Pasta com estado aberta/fechada claramente distinto.</summary>
        private static void DrawIconFolder(Graphics g, int x, int y, int h, Color color, bool expanded)
        {
            int midY = y + h / 2;
            int iw = 14, ih = 10;
            int ix = x, iy = midY - ih / 2;

            Color tabColor  = Color.FromArgb((int)(color.R * 0.68f), (int)(color.G * 0.68f), (int)(color.B * 0.55f));
            Color bodyColor = expanded
                ? Color.FromArgb(Math.Min(255, color.R + 15), Math.Min(255, color.G + 12), (int)(color.B * 0.7f))
                : Color.FromArgb((int)(color.R * 0.88f), (int)(color.G * 0.82f), (int)(color.B * 0.55f));

            // aba com canto arredondado
            using var tabBr = new SolidBrush(tabColor);
            g.FillRectangle(tabBr, ix, iy, 7, 4);
            g.FillRectangle(tabBr, ix + 7, iy + 1, 3, 3); // canto da aba

            // corpo
            using var bodyBr = new SolidBrush(bodyColor);
            g.FillRectangle(bodyBr, ix, iy + 3, iw, ih - 3);

            if (expanded)
            {
                // linha brilhante no topo — indica aberto
                using var hlBr = new SolidBrush(Color.FromArgb(
                    Math.Min(255, color.R + 70), Math.Min(255, color.G + 60), Math.Min(255, color.B + 20)));
                g.FillRectangle(hlBr, ix, iy + 3, iw, 2);

                // pequenos "documentos" visíveis dentro
                using var itemBr = new SolidBrush(Color.FromArgb(180, tabColor));
                g.FillRectangle(itemBr, ix + 2, iy + 5, 3, 4);
                g.FillRectangle(itemBr, ix + 6, iy + 5, 3, 4);
                g.FillRectangle(itemBr, ix + 10, iy + 5, 3, 4);
            }
        }

        /// <summary>Ícone de dispositivo estilo S7-1500 — PS + CPU + módulos I/O.</summary>
        private static void DrawIconDevice(Graphics g, int x, int y, int h)
        {
            int midY = y + h / 2;
            int ix = x, iy = midY - 6;

            // trilho (rail) — linha fina na base
            using var railBr = new SolidBrush(Color.FromArgb(70, 80, 100));
            g.FillRectangle(railBr, ix, iy + 11, 16, 2);

            // PS (fonte de alimentação) — cinza escuro
            using var psBr = new SolidBrush(Color.FromArgb(55, 62, 80));
            g.FillRectangle(psBr, ix, iy, 3, 11);
            using var psEdge = new SolidBrush(Color.FromArgb(90, 100, 125));
            g.FillRectangle(psEdge, ix, iy, 1, 11);

            // CPU — levemente mais claro com LED verde
            using var cpuBr = new SolidBrush(Color.FromArgb(48, 58, 78));
            g.FillRectangle(cpuBr, ix + 4, iy, 5, 11);
            using var cpuBorder = new Pen(C_GOLD, 1f);
            g.DrawRectangle(cpuBorder, ix + 4, iy, 4, 10);
            // LED RUN (verde)
            using var ledBr = new SolidBrush(C_OK);
            g.FillRectangle(ledBr, ix + 5, iy + 2, 2, 2);
            // LED SF (vermelho, apagado)
            using var ledOffBr = new SolidBrush(Color.FromArgb(80, 60, 60));
            g.FillRectangle(ledOffBr, ix + 5, iy + 5, 2, 2);

            // Módulo DI — azul
            using var diBr = new SolidBrush(Color.FromArgb(30, 55, 85));
            g.FillRectangle(diBr, ix + 10, iy, 3, 11);
            using var diEdge = new SolidBrush(Color.FromArgb(50, 90, 140));
            g.FillRectangle(diEdge, ix + 10, iy, 1, 11);

            // Módulo DQ — verde escuro
            using var dqBr = new SolidBrush(Color.FromArgb(25, 65, 40));
            g.FillRectangle(dqBr, ix + 14, iy, 3, 11);
            using var dqEdge = new SolidBrush(Color.FromArgb(40, 100, 65));
            g.FillRectangle(dqEdge, ix + 14, iy, 1, 11);
        }

        /// <summary>Grupo de blocos — stack de documentos com cor do tipo.</summary>
        private static void DrawIconGroup(Graphics g, int x, int y, int h, Color color)
        {
            int midY = y + h / 2;
            int ix = x + 1;

            // documento traseiro (sombra)
            using var shadBr = new SolidBrush(Color.FromArgb(38, 45, 60));
            g.FillRectangle(shadBr, ix + 3, midY - 6, 9, 11);

            // documento frontal
            using var bodyBr = new SolidBrush(Color.FromArgb(52, 60, 80));
            g.FillRectangle(bodyBr, ix, midY - 5, 9, 11);

            // borda colorida esquerda
            using var edgeBr = new SolidBrush(color);
            g.FillRectangle(edgeBr, ix, midY - 5, 2, 11);

            // linha de conteúdo
            using var lineBr = new SolidBrush(Color.FromArgb(90, color.R, color.G, color.B));
            g.FillRectangle(lineBr, ix + 3, midY - 2, 5, 1);
            g.FillRectangle(lineBr, ix + 3, midY + 1, 5, 1);
        }

        /// <summary>Ícone de grafo de chamadas — 3 nós ligados.</summary>
        private static void DrawIconCallGraph(Graphics g, int x, int y, int h, Color color)
        {
            int midY = y + h / 2;
            int ix = x + 1;

            using var edgePen = new Pen(Color.FromArgb(140, color.R, color.G, color.B), 1f);
            using var nodeBr  = new SolidBrush(color);
            using var rootBr  = new SolidBrush(Color.FromArgb(220, color.R, color.G, color.B));

            // arestas
            g.DrawLine(edgePen, ix + 6, midY - 3, ix + 2,  midY + 2);
            g.DrawLine(edgePen, ix + 6, midY - 3, ix + 10, midY + 2);

            // nó raiz (maior)
            g.FillEllipse(rootBr, ix + 4, midY - 6, 5, 5);

            // nós filhos
            g.FillEllipse(nodeBr, ix,      midY + 2, 4, 4);
            g.FillEllipse(nodeBr, ix + 8,  midY + 2, 4, 4);
        }

        /// <summary>Ícone de hardware — placa de circuito estilizada.</summary>
        private static void DrawIconHardware(Graphics g, int x, int y, int h, Color color)
        {
            int midY = y + h / 2;
            int ix = x + 1;

            // placa base (verde PCB escuro)
            using var boardBr = new SolidBrush(Color.FromArgb(20, 50, 35));
            g.FillRectangle(boardBr, ix, midY - 5, 13, 10);

            // borda
            using var borderPen = new Pen(Color.FromArgb(40, 110, 70), 1f);
            g.DrawRectangle(borderPen, ix, midY - 5, 12, 9);

            // chip central
            using var chipBr = new SolidBrush(Color.FromArgb(45, 55, 48));
            g.FillRectangle(chipBr, ix + 3, midY - 3, 6, 6);
            using var chipBorder = new Pen(color, 1f);
            g.DrawRectangle(chipBorder, ix + 3, midY - 3, 5, 5);

            // pinos esquerda e direita
            using var pinPen = new Pen(Color.FromArgb(160, color.R, color.G, color.B), 1f);
            g.DrawLine(pinPen, ix,      midY - 2, ix + 3, midY - 2);
            g.DrawLine(pinPen, ix,      midY + 1, ix + 3, midY + 1);
            g.DrawLine(pinPen, ix + 8,  midY - 2, ix + 12, midY - 2);
            g.DrawLine(pinPen, ix + 8,  midY + 1, ix + 12, midY + 1);

            // LED no chip
            using var ledBr = new SolidBrush(color);
            g.FillRectangle(ledBr, ix + 5, midY - 1, 2, 2);
        }

        // ── Hover helper ──────────────────────────────────────────────────────
        private static void AddHover(Button b, Color hover, Color normal)
        {
            b.MouseEnter += (s, e) => b.BackColor = hover;
            b.MouseLeave += (s, e) => b.BackColor = normal;
        }

        // ── Icon gerado por código ─────────────────────────────────────────────
        private Icon MakeIcon()
        {
            try
            {
                var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.FromArgb(13, 17, 23));
                    using (var grad = new LinearGradientBrush(
                        new Rectangle(2, 2, 28, 28),
                        Color.FromArgb(31, 111, 235), Color.FromArgb(120, 72, 210),
                        LinearGradientMode.Vertical))
                        g.FillEllipse(grad, 2, 2, 28, 28);
                    // Letra "D" centrada
                    using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
                        TextRenderer.DrawText(g, "D", f, new Rectangle(2, 2, 28, 28), Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        private RoundButton MkBtn(string text, int width, Color? bg = null)
        {
            return new RoundButton(bg ?? SURFACE)
            {
                Text        = text,
                Dock        = DockStyle.Fill,
                MinimumSize = new Size(width, 26),
                ForeColor   = C_TEXT,
                Font        = Font,
                Margin      = Padding.Empty
            };
        }

        // ── Botão com cantos arredondados (custom paint) ───────────────────────
        private sealed class RoundButton : Button
        {
            private readonly Color _base;
            private readonly Color _hover;
            private bool _over;

            public RoundButton(Color baseColor)
            {
                _base  = baseColor;
                _hover = Color.FromArgb(
                    Math.Min(255, baseColor.R + 22),
                    Math.Min(255, baseColor.G + 22),
                    Math.Min(255, baseColor.B + 28));
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _over = true;  Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _over = false; Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode    = SmoothingMode.AntiAlias;
                g.PixelOffsetMode  = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // Pintar fundo do parent para cantos transparentes
                var parentBg = Parent?.BackColor ?? Color.FromArgb(22, 27, 34);
                using (var pb = new SolidBrush(parentBg))
                    g.FillRectangle(pb, ClientRectangle);

                Color btnColor = !Enabled
                    ? Color.FromArgb(28, 33, 45)
                    : _over ? _hover : _base;
                Color txtColor = !Enabled
                    ? Color.FromArgb(72, 79, 92)
                    : ForeColor;

                var rc = ClientRectangle;
                rc.Inflate(-1, -1);
                using (var path = RoundPath(rc, 5))
                {
                    using (var br = new SolidBrush(btnColor))
                        g.FillPath(br, path);

                    // Shimmer no topo
                    if (Enabled)
                    {
                        var sRect = new Rectangle(rc.X, rc.Y, rc.Width, Math.Min(rc.Height, 10));
                        using (var sh = new LinearGradientBrush(sRect,
                            Color.FromArgb(35, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                            LinearGradientMode.Vertical))
                        using (var sp = RoundPath(sRect, 5))
                            g.FillPath(sh, sp);
                    }

                    using (var pen = new Pen(Color.FromArgb(Enabled ? 55 : 25, 255, 255, 255), 1f))
                        g.DrawPath(pen, path);
                }

                TextRenderer.DrawText(g, Text, Font, ClientRectangle, txtColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            private static GraphicsPath RoundPath(Rectangle rc, int r)
            {
                int d = r * 2;
                var p = new GraphicsPath();
                p.AddArc(rc.X,          rc.Y,           d, d, 180, 90);
                p.AddArc(rc.Right - d,  rc.Y,           d, d, 270, 90);
                p.AddArc(rc.Right - d,  rc.Bottom - d,  d, d,   0, 90);
                p.AddArc(rc.X,          rc.Bottom - d,  d, d,  90, 90);
                p.CloseFigure();
                return p;
            }
        }

        // ── Browse ────────────────────────────────────────────────────────────
        private void BrowseProject()
        {
            using var dlg = new OpenFileDialog
            {
                Title            = "Selecionar Projeto TIA Portal",
                Filter           = "TIA Portal V18 (*.ap18)|*.ap18|TIA Portal V17 (*.ap17)|*.ap17|Todos|*.ap*",
                InitialDirectory = Path.GetDirectoryName(_txtPath.Text) ?? ""
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _txtPath.Text = dlg.FileName;
        }

        // ── Run ───────────────────────────────────────────────────────────────
        private async Task RunAsync()
        {
            if (_running) return;
            _running = true;
            _btnRun.Enabled     = false;
            _btnSaveXml.Enabled = false;
            _btnSaveMd.Enabled  = false;
            _xmlResult          = null;
            _mdResult           = null;
            _rtbDetail.Clear();
            _rtbLog.Clear();
            _tree.Nodes.Clear();
            _detailHeader.Visible = false;
            SetProgress(0);
            SetStatus("A preparar...", Color.Silver);
            SetStats("");

            var path = _txtPath.Text.Trim();

            // Bloquear resize/move da janela durante a ligação ao TIA Portal
            // (o TIA Portal abre com UI e o auto-accept usa mouse_event, o que pode corromper o nosso layout)
            _lockWindowPos = true;

            await Task.Run(() =>
            {
                Console.SetOut(new RtbWriter(this));
                try
                {
                    SetStatus("A conectar ao TIA Portal...", Color.Silver);
                    SetProgress(5);
                    _conn?.Dispose();
                    _conn = new TiaConnection(path);

                    if (!_conn.Connect())
                    {
                        SetStatus("Falha na ligação!", C_ERR);
                        SetProgress(0);
                        return;
                    }
                    SetProgress(20);

                    var reader = new ProjectReader(_conn.Project);

                    SetStatus("A ler blocos  (OB / FB / FC / DB)...", Color.Silver);
                    _blocks    = reader.ReadAllBlocks();
                    _callGraph = ProjectReader.BuildCallGraph(_blocks);
                    SetProgress(55);

                    SetStatus("A ler Tag Tables...", Color.Silver);
                    _tagTables = reader.ReadAllTagTables();
                    SetProgress(70);

                    SetStatus("A ler Tipos de Dados (UDTs)...", Color.Silver);
                    _udts = reader.ReadAllUDTs();
                    SetProgress(78);

                    SetStatus("A ler topologia de hardware...", Color.Silver);
                    _hwDevices = new HardwareReader(_conn.Project).ReadAll();
                    SetProgress(84);

                    SetStatus("A construir árvore...", Color.Silver);
                    Invoke((Action)BuildTree);
                    SetProgress(90);

                    _xmlResult = BuildXml(_blocks, _tagTables, _udts, path);
                    SetProgress(95);
                    _mdResult  = BuildMarkdown(_blocks, _tagTables, _udts, _hwDevices, path);
                    SetProgress(100);

                    int obC = _blocks.Count(b => b.Type == "OB");
                    int fbC = _blocks.Count(b => b.Type == "FB");
                    int fcC = _blocks.Count(b => b.Type == "FC");
                    int dbC = _blocks.Count(b => b.Type == "DB" || b.Type == "iDB");

                    SetStats($"Offline  ·  OB:{obC}  FB:{fbC}  FC:{fcC}  DB:{dbC}  Tags:{_tagTables.Count}  UDT:{_udts.Count}");
                    SetStatus("Leitura concluída!  Clique num bloco na árvore para ver os detalhes.", C_OK);
                    Invoke((Action)(() => { _btnSaveXml.Enabled = true; _btnSaveMd.Enabled = true; }));
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro: {ex.Message}", C_ERR);
                    LogLine($"❌  ERRO: {ex.Message}", C_ERR, true);
                    LogLine(ex.StackTrace ?? "", C_ERR);
                }
            });

            // Desbloquear + trazer janela para a frente
            _lockWindowPos = false;
            BringToFront();
            Activate();

            _running = false;
            _btnRun.Enabled = true;
            SetProgress(0);
        }

        // ── Build Tree ────────────────────────────────────────────────────────
        private void BuildTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            var devices = _blocks.Select(b => b.Device)
                .Concat(_tagTables.Select(t => t.Device))
                .Concat(_udts.Select(u => u.Device))
                .Distinct().OrderBy(d => d);

            foreach (var dev in devices)
            {
                var devNode = new TreeNode($" {dev}")
                {
                    ForeColor = C_TEXT,
                    NodeFont  = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Tag       = "device"
                };

                var devBlocks = _blocks.Where(b => b.Device == dev).ToList();

                // Verificar se existem pastas reais no projeto
                bool hasFolders = devBlocks.Any(b => !string.IsNullOrEmpty(b.FolderPath));

                if (hasFolders)
                {
                    // Mostrar estrutura real de pastas do TIA Portal
                    AddFolderTree(devNode, devBlocks);
                }
                else
                {
                    // Sem pastas — agrupar por tipo (comportamento anterior)
                    // Ordem fixa: OB → FB → FC → DB → iDB
                    AddBlockGroup(devNode, devBlocks, "OB",  "Organização  [OB]",  C_OB);
                    AddBlockGroup(devNode, devBlocks, "FB",  "Função  [FB]",       C_FB);
                    AddBlockGroup(devNode, devBlocks, "FC",  "Funções  [FC]",      C_FC);
                    AddBlockGroup(devNode, devBlocks, "DB",  "Dados  [DB]",        C_DB);
                    AddBlockGroup(devNode, devBlocks, "iDB", "Instance DBs",       C_DB);
                }

                // Tag Tables
                var devTags = _tagTables.Where(t => t.Device == dev).OrderBy(t => t.Name).ToList();
                if (devTags.Count > 0)
                {
                    var grp = MakeGroupNode($" Tag Tables  ({devTags.Count})", "tags");
                    foreach (var tt in devTags)
                        grp.Nodes.Add(new TreeNode($" {tt.Name}  ({tt.Tags.Count} tags)")
                            { ForeColor = C_TAGS, Tag = tt });
                    devNode.Nodes.Add(grp);
                }

                // UDTs
                var devUdts = _udts.Where(u => u.Device == dev).OrderBy(u => u.Name).ToList();
                if (devUdts.Count > 0)
                {
                    var grp = MakeGroupNode($" UDTs  ({devUdts.Count})", "udt");
                    foreach (var udt in devUdts)
                        grp.Nodes.Add(new TreeNode($" {udt.Name}")
                            { ForeColor = C_UDT, Tag = udt });
                    devNode.Nodes.Add(grp);
                }

                // Hardware
                var hwDev = _hwDevices.FirstOrDefault(h =>
                    string.Equals(h.Name, dev, StringComparison.OrdinalIgnoreCase));
                if (hwDev != null)
                {
                    var hwNode = new TreeNode($"  ⬡  Hardware  ({hwDev.Modules.Count} módulos)")
                    {
                        ForeColor = C_OB,
                        NodeFont  = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Tag       = new HardwareTag { Device = dev }
                    };
                    devNode.Nodes.Add(hwNode);
                }

                // Grafo de chamadas
                var cgNode = new TreeNode("  ⬡  Grafo de Chamadas")
                {
                    ForeColor = C_GOLD,
                    NodeFont  = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Tag       = new CallGraphTag { Device = dev }
                };
                devNode.Nodes.Add(cgNode);

                devNode.Expand();
                _tree.Nodes.Add(devNode);
            }

            // Colapsar grupos por defeito
            foreach (TreeNode dev in _tree.Nodes)
                foreach (TreeNode grp in dev.Nodes)
                    grp.Collapse();

            _tree.EndUpdate();
        }

        // ── Árvore de pastas real (estrutura do TIA Portal) ───────────────────
        private static int BlockTypeOrder(string t) =>
            t == "OB" ? 0 : t == "FB" ? 1 : t == "FC" ? 2 : 3;

        private void AddFolderTree(TreeNode parent, List<BlockInfo> blocks)
        {
            // Blocos na raiz (sem pasta)
            var rootBlocks = blocks.Where(b => string.IsNullOrEmpty(b.FolderPath))
                                   .OrderBy(b => BlockTypeOrder(b.Type)).ThenBy(b => b.Number);
            foreach (var b in rootBlocks)
                parent.Nodes.Add(MakeBlockLeaf(b));

            // Recolher todos os caminhos de pastas de 1º nível
            var topFolders = blocks
                .Where(b => !string.IsNullOrEmpty(b.FolderPath))
                .Select(b => b.FolderPath.Split('/')[0])
                .Distinct().OrderBy(f => f);

            foreach (var folder in topFolders)
            {
                var folderBlocks = blocks.Where(b => b.FolderPath == folder ||
                                                     b.FolderPath.StartsWith(folder + "/")).ToList();
                var folderNode = MakeFolderNode($"  {folder}  ({folderBlocks.Count})", folderBlocks.Count);
                AddFolderChildren(folderNode, folderBlocks, folder);
                parent.Nodes.Add(folderNode);
            }
        }

        private void AddFolderChildren(TreeNode parent, List<BlockInfo> blocks, string currentPath)
        {
            // Blocos directamente nesta pasta (OB → FB → FC → DB)
            var directBlocks = blocks
                .Where(b => b.FolderPath == currentPath)
                .OrderBy(b => BlockTypeOrder(b.Type)).ThenBy(b => b.Number);
            foreach (var b in directBlocks)
                parent.Nodes.Add(MakeBlockLeaf(b));

            // Sub-pastas
            var subFolders = blocks
                .Where(b => b.FolderPath.StartsWith(currentPath + "/"))
                .Select(b =>
                {
                    var remainder = b.FolderPath.Substring(currentPath.Length + 1);
                    return remainder.Split('/')[0];
                })
                .Distinct().OrderBy(f => f);

            foreach (var sub in subFolders)
            {
                var subPath   = currentPath + "/" + sub;
                var subBlocks = blocks.Where(b => b.FolderPath == subPath ||
                                                  b.FolderPath.StartsWith(subPath + "/")).ToList();
                var subNode   = MakeFolderNode($"  {sub}  ({subBlocks.Count})", subBlocks.Count);
                AddFolderChildren(subNode, subBlocks, subPath);
                parent.Nodes.Add(subNode);
            }
        }

        private TreeNode MakeBlockLeaf(BlockInfo b)
        {
            var col  = b.Type == "OB" ? C_OB : b.Type == "FB" ? C_FB : b.Type == "FC" ? C_FC : C_DB;
            var nets = b.Networks.Count > 0 ? $"  [{b.Networks.Count}]" : "";
            return new TreeNode($" {b.Type}{b.Number}  —  {b.Name}{nets}") { ForeColor = col, Tag = b };
        }

        private TreeNode MakeFolderNode(string text, int count) =>
            new TreeNode(text)
            {
                ForeColor = C_GOLD,
                NodeFont  = new Font("Segoe UI", 8.5f),
                Tag       = "folder"
            };

        private TreeNode MakeGroupNode(string text, string type = "DB") =>
            new TreeNode(text)
            {
                ForeColor = C_IFACE,
                NodeFont  = new Font("Segoe UI", 8.5f),
                Tag       = $"group:{type}"
            };

        private void AddBlockGroup(TreeNode parent, List<BlockInfo> blocks, string type, string label, Color color)
        {
            var list = blocks.Where(b => b.Type == type).OrderBy(b => b.Number).ToList();
            if (list.Count == 0) return;
            var grp = MakeGroupNode($" {label}  ({list.Count})", type);
            foreach (var b in list)
            {
                var nets = b.Networks.Count > 0 ? $"  [{b.Networks.Count}]" : "";
                grp.Nodes.Add(new TreeNode($" {type}{b.Number}  —  {b.Name}{nets}")
                    { ForeColor = color, Tag = b });
            }
            parent.Nodes.Add(grp);
        }

        // ── Exportar XML do bloco selecionado ────────────────────────────────
        private void ExportSelectedBlockXml()
        {
            if (!(_tree.SelectedNode?.Tag is BlockInfo b) || string.IsNullOrEmpty(b.RawXml))
            {
                MessageBox.Show("Selecione um bloco (FC/FB/OB/DB) na árvore.", "Exportar XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Title            = "Salvar XML do bloco",
                Filter           = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName         = $"{b.Type}{b.Number}_{BlockExporter.Sanitize(b.Name)}.xml",
                DefaultExt       = "xml",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                File.WriteAllText(dlg.FileName, b.RawXml, System.Text.Encoding.UTF8);
                _lblStatus.Text = $"XML exportado: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"XML salvo em:\n{dlg.FileName}", "Exportar XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Erro ao exportar XML: {ex.Message}";
                MessageBox.Show($"Erro ao salvar:\n{ex.Message}", "Exportar XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Tree Selection ────────────────────────────────────────────────────
        private void OnTreeSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag == null) return;
            _rtbDetail.Clear();
            switch (e.Node.Tag)
            {
                case BlockInfo b:
                    var folder = string.IsNullOrEmpty(b.FolderPath) ? "" : $"  ·  📁 {b.FolderPath}";
                    ShowDetailHeader(
                        $"[{b.Type}]  {b.Name}",
                        $"#{b.Number}  ·  {b.Language}  ·  {b.Device}  ·  {b.Networks.Count} redes{folder}",
                        b.Type == "OB" ? C_OB : b.Type == "FB" ? C_FB : b.Type == "FC" ? C_FC : C_DB);
                    RenderBlock(b);
                    break;
                case TagTableInfo t:
                    ShowDetailHeader($"[TAG TABLE]  {t.Name}", $"{t.Tags.Count} variáveis  ·  {t.Device}", C_TAGS);
                    RenderTagTable(t);
                    break;
                case UdtInfo u:
                    ShowDetailHeader($"[UDT]  {u.Name}", $"#{u.Number}  ·  {u.Members.Count} membros  ·  {u.Device}", C_UDT);
                    RenderUdt(u);
                    break;
                case HardwareTag hw:
                    var hwInfo = _hwDevices.FirstOrDefault(h =>
                        string.Equals(h.Name, hw.Device, StringComparison.OrdinalIgnoreCase));
                    var modCount = hwInfo?.Modules.Count ?? 0;
                    ShowDetailHeader("Topologia de Hardware", $"{hw.Device}  ·  {modCount} módulos", C_OB);
                    if (hwInfo != null) RenderHardware(hwInfo);
                    break;

                case CallGraphTag cg:
                    var devBlks = _blocks.Where(b => b.Device == cg.Device).ToList();
                    ShowDetailHeader("Grafo de Chamadas", $"{cg.Device}  ·  {devBlks.Count} blocos", C_GOLD);
                    RenderCallGraph(devBlks, _callGraph);
                    break;
            }
        }

        private void ShowDetailHeader(string title, string meta, Color accent)
        {
            _lblDetailTitle.Text      = title;
            _lblDetailTitle.ForeColor = accent;
            _lblDetailMeta.Text       = meta;
            _detailHeader.Visible     = true;
            _detailHeader.Invalidate();
        }

        // ── Render: Block ─────────────────────────────────────────────────────
        private void RenderBlock(BlockInfo b)
        {
            var col = b.Type == "OB" ? C_OB :
                      b.Type == "FB" ? C_FB :
                      b.Type == "FC" ? C_FC : C_DB;

            DetailLine($"\n  [{b.Type}]  {b.Name}", col, true, 13f);
            DetailLine($"  Número: {b.Number}    Linguagem: {b.Language}    Dispositivo: {b.Device}", C_IFACE);
            DetailLine("  " + new string('─', 90), C_GROUP);

            var secs = b.Interface.Where(s => s.Members.Count > 0).ToList();
            if (secs.Count > 0)
            {
                DetailLine("\n  INTERFACE", col, true);
                foreach (var sec in secs)
                {
                    DetailLine($"\n    ▸ {sec.Name}", C_IFACE, true);
                    RenderMembers(sec.Members, "      ");
                }
            }

            if (b.Networks.Count > 0)
            {
                var tagLookup   = BuildTagLookup(b.Device);
                var dbLookup    = BuildDbMemberLookup(b.Device);
                var dbBlockInfo = _blocks
                    .Where(x => (x.Type == "DB" || x.Type == "iDB") && x.Device == b.Device)
                    .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
                DetailLine($"\n  REDES  ({b.Networks.Count})", col, true);
                foreach (var net in b.Networks)
                {
                    var title = string.IsNullOrWhiteSpace(net.Title) ? "" : $"  —  {net.Title}";
                    DetailLine($"\n    ▸ Network {net.Index}  [{net.Language}]{title}", C_NET, true);
                    if (net.Lines.Count == 0)
                        DetailLine("      (vazio)", C_GROUP);
                    else
                        foreach (var line in net.Lines)
                            DetailLine($"      {line}", C_TEXT);

                    // Glossário de variáveis resolvidas da tag table
                    var resolved = net.UsedVariables
                        .Where(v => tagLookup.TryGetValue(v, out _))
                        .Select(v => (Name: v, Tag: tagLookup[v]))
                        .Where(x => !string.IsNullOrWhiteSpace(x.Tag.Address) ||
                                    !string.IsNullOrWhiteSpace(x.Tag.Comment))
                        .ToList();
                    if (resolved.Count > 0)
                    {
                        DetailLine("      ···", C_GROUP);
                        foreach (var (name, tag) in resolved)
                        {
                            var addr = string.IsNullOrWhiteSpace(tag.Address) ? "".PadRight(14) : tag.Address.PadRight(14);
                            var cmt  = string.IsNullOrWhiteSpace(tag.Comment) ? "" : $"// {tag.Comment}";
                            DetailLine($"      {name,-36}{addr}{cmt}", C_IFACE);
                        }
                    }

                    // Glossário de membros de DB
                    var resolvedDb = net.UsedDbMembers
                        .Where(k => dbLookup.TryGetValue(k, out _))
                        .Select(k => (Key: k, M: dbLookup[k]))
                        .ToList();
                    if (resolvedDb.Count > 0)
                    {
                        DetailLine("      ···  DB", C_GROUP);
                        foreach (var (key, m) in resolvedDb)
                        {
                            var dbName = key.Contains('.') ? key.Substring(0, key.IndexOf('.')) : key;
                            var dbRef  = dbBlockInfo.TryGetValue(dbName, out var dbi)
                                ? FormatDbAddress(dbi.Number, m).PadRight(18)
                                : "".PadRight(18);
                            var init = string.IsNullOrWhiteSpace(m.InitialValue) ? "" : $" = {m.InitialValue}";
                            var cmt  = string.IsNullOrWhiteSpace(m.Comment) ? "" : $"  // {m.Comment}";
                            DetailLine($"      {key,-44}{dbRef}{m.DataType,-14}{init}{cmt}", C_IFACE);
                        }
                    }
                }
            }
            else
                DetailLine("\n  (sem redes — bloco vazio ou DB)", C_GROUP);
        }

        // ── Render: Call Graph ───────────────────────────────────────────────
        // ── Render: Hardware ──────────────────────────────────────────────────
        private void RenderHardware(HwDeviceInfo hw)
        {
            // Cabeçalho de rede
            var net = hw.Network;
            if (!string.IsNullOrEmpty(net.IpAddress))
            {
                DetailLine($"\n  REDE", C_OB, true, 11f);
                DetailLine("  " + new string('─', 80), C_GROUP);
                DetailLine($"  IP Address   : {net.IpAddress}", C_TEXT);
                if (!string.IsNullOrEmpty(net.SubnetMask))    DetailLine($"  Máscara      : {net.SubnetMask}",    C_IFACE);
                if (!string.IsNullOrEmpty(net.RouterAddress)) DetailLine($"  Gateway      : {net.RouterAddress}", C_IFACE);
                if (!string.IsNullOrEmpty(net.ProfinetName))  DetailLine($"  PROFINET     : {net.ProfinetName}",  C_IFACE);
            }

            if (!string.IsNullOrEmpty(hw.OrderNumber))
            {
                DetailLine($"\n  CPU / REFERÊNCIA", C_OB, true, 11f);
                DetailLine("  " + new string('─', 80), C_GROUP);
                DetailLine($"  {hw.OrderNumber}", C_TEXT);
                if (!string.IsNullOrEmpty(hw.Comment)) DetailLine($"  {hw.Comment}", C_IFACE);
            }

            // Tabela de módulos
            if (hw.Modules.Count > 0)
            {
                DetailLine($"\n  MÓDULOS NO RACK  ({hw.Modules.Count})", C_OB, true, 11f);
                DetailLine("  " + new string('─', 80), C_GROUP);
                DetailLine($"  {"Slot",-5} {"Nome",-30} {"Referência",-25} {"Entradas",-12} {"Saídas",-12}", C_IFACE, true);
                DetailLine("  " + new string('─', 80), C_GROUP);

                foreach (var m in hw.Modules.OrderBy(x => x.Slot))
                {
                    var inp = string.IsNullOrEmpty(m.InputRange)  ? "—" : m.InputRange;
                    var out_ = string.IsNullOrEmpty(m.OutputRange) ? "—" : m.OutputRange;
                    var name = string.IsNullOrEmpty(m.Name) ? m.OrderNumber : m.Name;
                    DetailLine($"  {m.Slot,-5} {name,-30} {m.OrderNumber,-25} {inp,-12} {out_,-12}", C_TEXT);

                    if (!string.IsNullOrEmpty(m.Comment))
                        DetailLine($"        // {m.Comment}", C_MUTED);

                    foreach (var sub in m.SubModules.OrderBy(s => s.Slot))
                    {
                        var si = string.IsNullOrEmpty(sub.InputRange)  ? "—" : sub.InputRange;
                        var so = string.IsNullOrEmpty(sub.OutputRange) ? "—" : sub.OutputRange;
                        var sn = string.IsNullOrEmpty(sub.Name) ? sub.OrderNumber : sub.Name;
                        DetailLine($"  └ {sub.Slot,-3} {sn,-30} {sub.OrderNumber,-25} {si,-12} {so,-12}", C_IFACE);
                    }
                }
            }

            if (hw.Modules.Count == 0 && string.IsNullOrEmpty(net.IpAddress) && string.IsNullOrEmpty(hw.OrderNumber))
            {
                DetailLine("\n  (informação de hardware não disponível para este device)", C_GROUP);
                DetailLine("  O TIA Portal Openness API pode não expor todos os atributos de hardware offline.", C_MUTED);
            }
        }

        private void RenderCallGraph(
            List<BlockInfo> blocks,
            Dictionary<string, List<ProjectReader.CallEdge>> graph)
        {
            // Usar TODOS os blocos do projeto para o mapa (não só do device)
            var blockMap = _blocks
                .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var called = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in graph)
                foreach (var e in kv.Value)
                    called.Add(e.Name);

            DetailLine("\n  HIERARQUIA DE CHAMADAS", C_GOLD, true, 11f);
            DetailLine("  " + new string('─', 80), C_GROUP);

            var roots = blocks
                .Where(b => b.Type != "DB" && b.Type != "iDB" && (b.Type == "OB" || !called.Contains(b.Name)))
                .OrderBy(b => b.Type == "OB" ? 0 : 1)
                .ThenBy(b => b.Number);

            foreach (var root in roots)
            {
                DetailLine("", C_TEXT);
                var rootCol  = root.Type == "OB" ? C_OB : root.Type == "FB" ? C_FB : C_FC;
                var rootMeta = $"  · {root.Language} · {root.Networks.Count} redes";
                DetailLine($"  [{root.Type}{root.Number}]  {root.Name}", rootCol, true);
                DetailLine($"         {rootMeta}", C_IFACE);
                RenderCallNode(root.Name, graph, blockMap, 1,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            // Referência inversa
            DetailLine("\n\n  REFERÊNCIA INVERSA", C_GOLD, true, 11f);
            DetailLine("  " + new string('─', 80), C_GROUP);
            DetailLine($"  {"Bloco",-35} {"Tipo",-8} {"Linguagem",-6} {"Redes",-6}  Chamado por", C_IFACE, true);
            DetailLine("  " + new string('─', 80), C_GROUP);

            var anyShown = false;
            foreach (var block in blocks.Where(b => b.Type != "OB").OrderBy(b => b.Type).ThenBy(b => b.Name))
            {
                var callerEdges = graph
                    .Where(kv => kv.Value.Any(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(kv =>
                    {
                        var inst = kv.Value.FirstOrDefault(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase))?.Instance;
                        return inst != null ? $"{kv.Key} (via {inst})" : kv.Key;
                    })
                    .OrderBy(s => s)
                    .ToList();

                if (callerEdges.Count == 0) continue;
                anyShown = true;
                var col   = block.Type == "FB" ? C_FB : C_FC;
                var num   = $"{block.Type}{block.Number}";
                var nets  = block.Networks.Count.ToString();
                DetailLine($"  {block.Name,-35} {num,-8} {block.Language,-6} {nets,-6}  ← {string.Join(", ", callerEdges)}", col);
            }
            if (!anyShown)
                DetailLine("\n  (nenhuma chamada detectada)", C_GROUP);
        }

        private void RenderCallNode(
            string name,
            Dictionary<string, List<ProjectReader.CallEdge>> graph,
            Dictionary<string, BlockInfo> blockMap,
            int depth,
            HashSet<string> stack)
        {
            if (depth > 8) return;
            if (stack.Contains(name))
            {
                DetailLine($"{new string(' ', depth * 4 + 2)}↻ recursão detectada", C_ERR);
                return;
            }
            if (!graph.TryGetValue(name, out var calls) || calls.Count == 0) return;

            stack.Add(name);
            foreach (var edge in calls)
            {
                blockMap.TryGetValue(edge.Name, out var cb);
                var num    = cb != null ? $"{edge.Type}{cb.Number}" : edge.Type;
                var col    = edge.Type == "OB" ? C_OB : edge.Type == "FB" ? C_FB :
                             edge.Type == "FC" ? C_FC : C_DB;
                var indent = new string(' ', depth * 4 + 2);
                var lang   = cb != null ? $" · {cb.Language}" : "";
                var nets   = cb != null ? $" · {cb.Networks.Count} redes" : "";
                var inst   = edge.Instance != null ? $"  📦 {edge.Instance} [iDB]" : "";
                DetailLine($"{indent}→ [{num,-6}]  {edge.Name}{lang}{nets}{inst}", col);
                RenderCallNode(edge.Name, graph, blockMap, depth + 1, stack);
            }
            stack.Remove(name);
        }

        // ── Render: Tag Table ─────────────────────────────────────────────────
        private void RenderTagTable(TagTableInfo tt)
        {
            DetailLine($"\n  [TAG TABLE]  {tt.Name}", C_TAGS, true, 13f);
            DetailLine($"  {tt.Tags.Count} variáveis    Dispositivo: {tt.Device}", C_IFACE);
            DetailLine("  " + new string('─', 90), C_GROUP);

            if (tt.Tags.Count == 0) { DetailLine("\n  (tabela vazia)", C_GROUP); return; }

            DetailLine($"\n  {"Nome",-36} {"Tipo",-22} {"Endereço",-14} Comentário", C_IFACE, true);
            DetailLine("  " + new string('─', 90), C_GROUP);
            foreach (var tag in tt.Tags)
                DetailLine($"  {tag.Name,-36} {tag.DataType,-22} {tag.Address,-14} {tag.Comment?.Trim()}", C_TEXT);
        }

        // ── Render: UDT ───────────────────────────────────────────────────────
        private void RenderUdt(UdtInfo udt)
        {
            DetailLine($"\n  [UDT]  {udt.Name}", C_UDT, true, 13f);
            DetailLine($"  Número: {udt.Number}    {udt.Members.Count} membros    Dispositivo: {udt.Device}", C_IFACE);
            DetailLine("  " + new string('─', 90), C_GROUP);
            DetailLine("", C_TEXT);
            RenderMembers(udt.Members, "  ");
        }

        private void RenderMembers(List<MemberInfo> members, string indent, int depth = 0)
        {
            foreach (var m in members)
            {
                var init = string.IsNullOrWhiteSpace(m.InitialValue) ? "" : $" := {m.InitialValue}";
                var cmt  = string.IsNullOrWhiteSpace(m.Comment) ? "" : $"   // {m.Comment}";
                DetailLine($"{indent}{m.Name} : {m.DataType}{init}{cmt}", depth == 0 ? C_TEXT : C_IFACE);
                if (m.Members.Count > 0)
                    RenderMembers(m.Members, indent + "    ", depth + 1);
            }
        }

        // ── Filter Tree ───────────────────────────────────────────────────────
        private void FilterTree(string text)
        {
            if (_tree.Nodes.Count == 0) return;
            if (string.IsNullOrWhiteSpace(text) || text.Contains("Filtrar"))
            {
                BuildTree();
                return;
            }
            var lower = text.ToLowerInvariant();
            _tree.BeginUpdate();
            foreach (TreeNode dev in _tree.Nodes)
            {
                dev.Expand();
                foreach (TreeNode grp in dev.Nodes)
                {
                    bool any = grp.Nodes.Cast<TreeNode>().Any(n => n.Text.ToLowerInvariant().Contains(lower));
                    if (any) grp.Expand(); else grp.Collapse();
                }
            }
            _tree.EndUpdate();
        }

        // ── XML ───────────────────────────────────────────────────────────────
        private string BuildXml(List<BlockInfo> blocks, List<TagTableInfo> tagTables, List<UdtInfo> udts, string path)
        {
            var root = new XElement("PLCProject",
                new XAttribute("name",       Path.GetFileNameWithoutExtension(path)),
                new XAttribute("path",       path),
                new XAttribute("exportedAt", DateTime.UtcNow.ToString("o")),
                new XAttribute("blocks",     blocks.Count),
                new XAttribute("tagTables",  tagTables.Count),
                new XAttribute("udts",       udts.Count));

            var devMap = new Dictionary<string, XElement>();
            XElement GetDev(string name)
            {
                if (!devMap.TryGetValue(name, out var d))
                { d = new XElement("Device", new XAttribute("name", name)); devMap[name] = d; root.Add(d); }
                return d;
            }

            foreach (var b in blocks)
            {
                var dev  = GetDev(b.Device);
                var blks = dev.Element("Blocks") ?? new XElement("Blocks");
                if (dev.Element("Blocks") == null) dev.Add(blks);

                var bEl = new XElement("Block",
                    new XAttribute("type", b.Type), new XAttribute("name", b.Name),
                    new XAttribute("number", b.Number), new XAttribute("language", b.Language));

                var ifEl = new XElement("Interface");
                foreach (var sec in b.Interface.Where(s => s.Members.Count > 0))
                { var sEl = new XElement("Section", new XAttribute("name", sec.Name)); XmlMembers(sEl, sec.Members); ifEl.Add(sEl); }
                if (ifEl.HasElements) bEl.Add(ifEl);

                var netsEl = new XElement("Networks");
                foreach (var net in b.Networks)
                {
                    var nEl = new XElement("Network", new XAttribute("index", net.Index), new XAttribute("language", net.Language));
                    if (!string.IsNullOrWhiteSpace(net.Title)) nEl.Add(new XAttribute("title", net.Title));
                    foreach (var line in net.Lines) nEl.Add(new XElement("Line", line));
                    netsEl.Add(nEl);
                }
                if (netsEl.HasElements) bEl.Add(netsEl);
                blks.Add(bEl);
            }

            foreach (var tt in tagTables)
            {
                var dev = GetDev(tt.Device);
                var ttR = dev.Element("TagTables") ?? new XElement("TagTables");
                if (dev.Element("TagTables") == null) dev.Add(ttR);
                var ttEl = new XElement("TagTable", new XAttribute("name", tt.Name));
                foreach (var tag in tt.Tags)
                {
                    var tEl = new XElement("Tag", new XAttribute("name", tag.Name),
                        new XAttribute("type", tag.DataType), new XAttribute("address", tag.Address));
                    if (!string.IsNullOrWhiteSpace(tag.Comment)) tEl.Add(new XAttribute("comment", tag.Comment.Trim()));
                    ttEl.Add(tEl);
                }
                ttR.Add(ttEl);
            }

            foreach (var udt in udts)
            {
                var dev  = GetDev(udt.Device);
                var udtR = dev.Element("UDTs") ?? new XElement("UDTs");
                if (dev.Element("UDTs") == null) dev.Add(udtR);
                var uEl = new XElement("UDT", new XAttribute("name", udt.Name), new XAttribute("number", udt.Number));
                XmlMembers(uEl, udt.Members);
                udtR.Add(uEl);
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
        }

        private static void XmlMembers(XElement parent, List<MemberInfo> members)
        {
            foreach (var m in members)
            {
                var mEl = new XElement("Member", new XAttribute("name", m.Name), new XAttribute("type", m.DataType));
                if (!string.IsNullOrWhiteSpace(m.InitialValue)) mEl.Add(new XAttribute("init", m.InitialValue));
                if (!string.IsNullOrWhiteSpace(m.Comment)) mEl.Add(new XAttribute("comment", m.Comment));
                if (m.Members.Count > 0) XmlMembers(mEl, m.Members);
                parent.Add(mEl);
            }
        }

        // ── Markdown para IA ──────────────────────────────────────────────────
        private string BuildMarkdown(List<BlockInfo> blocks, List<TagTableInfo> tagTables, List<UdtInfo> udts, string path, string installationName = null)
            => BuildMarkdown(blocks, tagTables, udts, _hwDevices, path, installationName);

        private string BuildMarkdown(List<BlockInfo> blocks, List<TagTableInfo> tagTables, List<UdtInfo> udts, List<HwDeviceInfo> hwDevices, string path, string installationName = null)
        {
            var sb = new StringBuilder();
            var projName    = Path.GetFileNameWithoutExtension(path);
            var displayName = !string.IsNullOrWhiteSpace(installationName) ? installationName : projName;

            int obC = blocks.Count(b => b.Type == "OB");
            int fbC = blocks.Count(b => b.Type == "FB");
            int fcC = blocks.Count(b => b.Type == "FC");
            int dbC = blocks.Count(b => b.Type == "DB" || b.Type == "iDB");

            // ── Cabeçalho ─────────────────────────────────────────────────────
            sb.AppendLine($"# Base de Conhecimento PLC — {displayName}");
            sb.AppendLine();
            sb.AppendLine($"> **Instalação:** {displayName}  ");
            sb.AppendLine($"> **Projeto TIA Portal:** `{projName}`  ");
            sb.AppendLine($"> **Exportado em:** {DateTime.Now:yyyy-MM-dd HH:mm}  ");
            sb.AppendLine($"> **Resumo:** {obC} OBs · {fbC} FBs · {fcC} FCs · {dbC} DBs · {tagTables.Count} Tag Tables · {udts.Count} UDTs");
            sb.AppendLine();
            sb.AppendLine($"> **Contexto para IA:** Este documento descreve o programa PLC da instalação **{displayName}**.");
            sb.AppendLine($"> Ao responder qualquer questão, usa sempre dois níveis de explicação:");
            sb.AppendLine($"> - **Técnico** (para eletricistas e programadores PLC): endereços exatos, condições lógicas, temporizadores, blocos de função");
            sb.AppendLine($"> - **Operador** (linguagem simples): o que acontece fisicamente, qual equipamento atua, o que o operador vê");
            sb.AppendLine($">");
            sb.AppendLine($"> Glossário rápido: `%I` = entrada digital (sensor/botão), `%Q` = saída digital (motor/válvula/luz),");
            sb.AppendLine($"> `%M` = memória interna, `%IW`/`%QW` = sinal analógico, `DB` = bloco de dados, `FB` = bloco de função, `OB1` = ciclo principal.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            var devices = blocks.Select(b => b.Device)
                .Concat(tagTables.Select(t => t.Device))
                .Concat(udts.Select(u => u.Device))
                .Distinct().OrderBy(d => d);

            foreach (var dev in devices)
            {
                sb.AppendLine($"## Dispositivo: {dev}");
                sb.AppendLine();

                // ── Hardware Topology ─────────────────────────────────────────
                var hwDev = hwDevices?.FirstOrDefault(h =>
                    string.Equals(h.Name, dev, StringComparison.OrdinalIgnoreCase));
                if (hwDev != null)
                    MdHardware(sb, hwDev);

                // ── Tag Tables ────────────────────────────────────────────────
                var devTags = tagTables.Where(t => t.Device == dev).ToList();
                if (devTags.Count > 0)
                {
                    sb.AppendLine("### Tabelas de Tags (I/O e Memória)");
                    sb.AppendLine();
                    sb.AppendLine("> As tags representam entradas, saídas e memórias do PLC. "
                                + "Endereços `%I` = entrada digital, `%Q` = saída digital, `%M` = memória, `%IW`/`%QW` = word analógica.");
                    sb.AppendLine();
                    foreach (var tt in devTags)
                    {
                        sb.AppendLine($"#### Tag Table: {tt.Name}");
                        sb.AppendLine();
                        if (tt.Tags.Count == 0)
                        {
                            sb.AppendLine("_(tabela vazia)_");
                        }
                        else
                        {
                            sb.AppendLine("| Variável | Tipo | Endereço | Comentário |");
                            sb.AppendLine("|----------|------|----------|------------|");
                            foreach (var tag in tt.Tags)
                            {
                                var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "⚠ _sem descrição_" : tag.Comment.Trim();
                                sb.AppendLine($"| `{Esc(tag.Name)}` | {Esc(tag.DataType)} | `{Esc(tag.Address)}` | {Esc(cmt)} |");
                            }
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                // ── UDTs ──────────────────────────────────────────────────────
                var devUdts = udts.Where(u => u.Device == dev).ToList();
                if (devUdts.Count > 0)
                {
                    sb.AppendLine("### Tipos de Dados Definidos pelo Utilizador (UDTs)");
                    sb.AppendLine();
                    sb.AppendLine("> UDTs são estruturas personalizadas reutilizáveis em blocos FB/FC/DB.");
                    sb.AppendLine();
                    foreach (var udt in devUdts.OrderBy(u => u.Name))
                    {
                        sb.AppendLine($"#### UDT: {udt.Name}");
                        sb.AppendLine();
                        sb.AppendLine("```");
                        MdMembers(sb, udt.Members, "");
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                // ── Blocos por tipo ───────────────────────────────────────────
                var devBlocks = blocks.Where(b => b.Device == dev)
                    .OrderBy(b => b.Type == "OB" ? 0 : b.Type == "FB" ? 1 : b.Type == "FC" ? 2 : 3)
                    .ThenBy(b => b.Number);

                string lastType = "";
                foreach (var b in devBlocks)
                {
                    // Cabeçalho de grupo ao mudar de tipo
                    if (b.Type != lastType)
                    {
                        lastType = b.Type;
                        var groupTitle = b.Type == "OB"  ? "Blocos de Organização (OB) — Ciclos e Interrupções" :
                                         b.Type == "FB"  ? "Blocos de Função (FB) — Lógica com memória (instância)" :
                                         b.Type == "FC"  ? "Funções (FC) — Lógica sem memória" :
                                                           "Blocos de Dados (DB) — Armazenamento";
                        var groupDesc = b.Type == "OB"  ? "OBs são chamados automaticamente pelo sistema operativo do PLC (ciclo, arranque, interrupções)." :
                                        b.Type == "FB"  ? "FBs mantêm estado entre ciclos através de um bloco de dados de instância (iDB)." :
                                        b.Type == "FC"  ? "FCs não têm memória própria; recebem e devolvem valores por parâmetro." :
                                                          "DBs guardam dados persistentes. Global DBs são partilhados; Instance DBs pertencem a um FB.";
                        sb.AppendLine($"### {groupTitle}");
                        sb.AppendLine();
                        sb.AppendLine($"> {groupDesc}");
                        sb.AppendLine();
                    }

                    MdBlock(sb, b);
                }

                // ── Falhas e Alarmes ──────────────────────────────────────────
                MdFaultIndex(sb, blocks.Where(b => b.Device == dev).ToList(),
                             tagTables.Where(t => t.Device == dev).ToList());

                // ── I/O Disponível ────────────────────────────────────────────
                MdAvailableIO(sb, tagTables.Where(t => t.Device == dev).ToList());

                // ── Referências Cruzadas de Tags ──────────────────────────────
                MdTagCrossRef(sb, blocks.Where(b => b.Device == dev).ToList(),
                              tagTables.Where(t => t.Device == dev).ToList());

                // ── Tags Sem Descrição ────────────────────────────────────────
                MdTagsWithoutDescription(sb, tagTables.Where(t => t.Device == dev).ToList());

                sb.AppendLine("---");
                sb.AppendLine();
            }

            // ── Grafo de chamadas ──────────────────────────────────────────────
            if (blocks.Count > 0)
            {
                sb.AppendLine("## Grafo de Chamadas");
                sb.AppendLine();
                sb.AppendLine("> Mostra quem chama quem. OBs são os pontos de entrada do programa (chamados automaticamente pelo SO do PLC).");
                sb.AppendLine();

                var cg       = ProjectReader.BuildCallGraph(blocks);
                var blockMap = blocks
                    .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var called   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cg)
                    foreach (var e in kv.Value)
                        called.Add(e.Name);

                var roots = blocks
                    .Where(b => b.Type != "DB" && b.Type != "iDB" && (b.Type == "OB" || !called.Contains(b.Name)))
                    .OrderBy(b => b.Type == "OB" ? 0 : 1)
                    .ThenBy(b => b.Number);

                sb.AppendLine("```");
                foreach (var root in roots)
                {
                    sb.AppendLine($"[{root.Type}{root.Number}]  {root.Name}");
                    MdCallNode(sb, root.Name, cg, blockMap, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    sb.AppendLine();
                }
                sb.AppendLine("```");
                sb.AppendLine();

                // Referência inversa em tabela
                sb.AppendLine("### Referência inversa");
                sb.AppendLine();
                sb.AppendLine("| Bloco | Tipo | Linguagem | Redes | Chamado por |");
                sb.AppendLine("|-------|------|-----------|-------|-------------|");
                foreach (var block in blocks.Where(b => b.Type != "OB").OrderBy(b => b.Name))
                {
                    var callers = cg
                        .Where(kv => kv.Value.Any(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase)))
                        .Select(kv =>
                        {
                            var inst = kv.Value.FirstOrDefault(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase))?.Instance;
                            return inst != null ? $"{kv.Key} (via {inst})" : kv.Key;
                        })
                        .OrderBy(s => s).ToList();
                    if (callers.Count == 0) continue;
                    sb.AppendLine($"| `{block.Name}` | {block.Type}{block.Number} | {block.Language} | {block.Networks.Count} | {string.Join(", ", callers)} |");
                }
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void MdCallNode(
            StringBuilder sb, string name,
            Dictionary<string, List<ProjectReader.CallEdge>> graph,
            Dictionary<string, BlockInfo> blockMap,
            int depth, HashSet<string> stack)
        {
            if (depth > 8 || stack.Contains(name)) return;
            if (!graph.TryGetValue(name, out var calls) || calls.Count == 0) return;
            stack.Add(name);
            foreach (var edge in calls)
            {
                blockMap.TryGetValue(edge.Name, out var cb);
                var num  = cb != null ? $"{edge.Type}{cb.Number}" : edge.Type;
                var lang = cb != null ? $" · {cb.Language}" : "";
                var nets = cb != null ? $" · {cb.Networks.Count} redes" : "";
                var inst = edge.Instance != null ? $"  [iDB: {edge.Instance}]" : "";
                sb.AppendLine($"{new string(' ', depth * 2)}→ [{num}]  {edge.Name}{lang}{nets}{inst}");
                MdCallNode(sb, edge.Name, graph, blockMap, depth + 1, stack);
            }
            stack.Remove(name);
        }

        private void MdBlock(StringBuilder sb, BlockInfo b)
        {
            var tagLookup   = BuildTagLookup(b.Device);
            var dbLookup    = BuildDbMemberLookup(b.Device);
            var dbBlockInfo = _blocks
                .Where(x => (x.Type == "DB" || x.Type == "iDB") && x.Device == b.Device)
                .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
            var lang = b.Language;
            sb.AppendLine($"#### {b.Type}{b.Number} — {b.Name}");
            sb.AppendLine();
            sb.AppendLine($"| Campo | Valor |");
            sb.AppendLine($"|-------|-------|");
            sb.AppendLine($"| Tipo | {b.Type} |");
            sb.AppendLine($"| Número | {b.Number} |");
            sb.AppendLine($"| Linguagem | {lang} |");
            sb.AppendLine($"| Dispositivo | {b.Device} |");
            if (!string.IsNullOrEmpty(b.FolderPath))
                sb.AppendLine($"| Pasta | `{b.FolderPath}` |");
            sb.AppendLine();

            // Descrição do bloco
            if (!string.IsNullOrWhiteSpace(b.Comment))
            {
                sb.AppendLine($"> **Descrição:** {b.Comment}");
                sb.AppendLine();
            }

            // Interface
            var secs = b.Interface.Where(s => s.Members.Count > 0).ToList();
            if (secs.Count > 0)
            {
                sb.AppendLine("**Interface:**");
                sb.AppendLine();
                foreach (var sec in secs)
                {
                    var secDesc = sec.Name switch
                    {
                        "Input"    => "Entradas (valores recebidos pelo bloco)",
                        "Output"   => "Saídas (valores devolvidos pelo bloco)",
                        "InOut"    => "Entrada/Saída (passados por referência)",
                        "Static"   => "Variáveis estáticas (persistem entre ciclos, apenas FB)",
                        "Temp"     => "Variáveis temporárias (existem só durante execução do bloco)",
                        "Constant" => "Constantes",
                        _          => sec.Name
                    };
                    sb.AppendLine($"- **{sec.Name}** _{secDesc}_");
                    sb.AppendLine();
                    sb.AppendLine("  ```");
                    MdMembers(sb, sec.Members, "  ");
                    sb.AppendLine("  ```");
                    sb.AppendLine();
                }
            }

            // Networks — DBs/iDBs não repetem: a Interface já cobre a estrutura
            if (b.Networks.Count > 0 && b.Type != "DB" && b.Type != "iDB")
            {
                var codeHint = lang == "SCL"   ? "SCL (Structured Control Language — semelhante a Pascal/C)" :
                               lang == "LAD"   ? "LAD (Ladder Diagram — contactos e bobinas)" :
                               lang == "FBD"   ? "FBD (Function Block Diagram — blocos ligados)" :
                               lang == "STL"   ? "STL (Statement List — linguagem assembly PLC)" :
                               lang == "GRAPH" ? "GRAPH (Sequential Function Chart — sequências)" : lang;

                sb.AppendLine($"**Lógica — {b.Networks.Count} rede(s) em {codeHint}:**");
                sb.AppendLine();
                foreach (var net in b.Networks)
                {
                    // Cabeçalho auto-contido — cada rede é um chunk independente para RAG
                    var netTitle  = string.IsNullOrWhiteSpace(net.Title) ? $"Network {net.Index}" : $"Network {net.Index}: {net.Title}";
                    var ctxFolder = !string.IsNullOrEmpty(b.FolderPath) ? $" · {b.FolderPath}" : "";
                    sb.AppendLine($"##### {b.Name} — {netTitle}");
                    sb.AppendLine($"*{b.Type}{b.Number} · {lang} · {b.Device}{ctxFolder}*");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(net.Comment))
                    {
                        sb.AppendLine($"> {net.Comment}");
                        sb.AppendLine();
                    }
                    if (net.Lines.Count > 0)
                    {
                        sb.AppendLine($"```{LangHint(net.Language)}");
                        foreach (var line in net.Lines)
                            sb.AppendLine(line);
                        sb.AppendLine("```");
                    }
                    else
                    {
                        sb.AppendLine("_(rede vazia)_");
                    }

                    // Glossário de variáveis — só tags reais da tag table
                    var resolved = net.UsedVariables
                        .Where(v => tagLookup.TryGetValue(v, out _))
                        .Select(v => (Name: v, Tag: tagLookup[v]))
                        .Where(x => !string.IsNullOrWhiteSpace(x.Tag.Address) ||
                                    !string.IsNullOrWhiteSpace(x.Tag.Comment))
                        .ToList();
                    if (resolved.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("| Variável | Endereço | Descrição |");
                        sb.AppendLine("|----------|----------|-----------|");
                        foreach (var (name, tag) in resolved)
                            sb.AppendLine($"| `{Esc(name)}` | `{Esc(tag.Address)}` | {Esc(tag.Comment)} |");
                    }

                    // Membros de DB usados nesta rede
                    var resolvedDb = net.UsedDbMembers
                        .Where(k => dbLookup.TryGetValue(k, out _))
                        .Select(k => (Key: k, M: dbLookup[k]))
                        .ToList();
                    if (resolvedDb.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("| DB.Membro | Nº DB | Tipo | Valor Inicial | Descrição |");
                        sb.AppendLine("|-----------|-------|------|---------------|-----------|");
                        foreach (var (key, m) in resolvedDb)
                        {
                            var dbName = key.Contains('.') ? key.Substring(0, key.IndexOf('.')) : key;
                            var dbNum  = dbBlockInfo.TryGetValue(dbName, out var dbi)
                                ? FormatDbAddress(dbi.Number, m) : "";
                            var init   = string.IsNullOrWhiteSpace(m.InitialValue) ? "" : m.InitialValue;
                            var cmt    = string.IsNullOrWhiteSpace(m.Comment)      ? "" : m.Comment;
                            sb.AppendLine($"| `{Esc(key)}` | `{Esc(dbNum)}` | `{Esc(m.DataType)}` | {Esc(init)} | {Esc(cmt)} |");
                        }
                    }

                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("_(sem redes — bloco vazio ou DB puro)_");
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string FormatDbAddress(int dbNumber, MemberInfo m)
        {
            if (m.BitOffset < 0) return $"DB{dbNumber}";  // otimizado
            int byteOff = m.BitOffset / 8;
            int bitOff  = m.BitOffset % 8;
            var u = m.DataType?.ToUpperInvariant() ?? "";
            if (u == "BOOL")
                return $"DB{dbNumber}.DBX{byteOff}.{bitOff}";
            if (u == "BYTE" || u == "SINT" || u == "USINT" || u == "CHAR")
                return $"DB{dbNumber}.DBB{byteOff}";
            if (u == "WORD" || u == "INT" || u == "UINT" || u == "WCHAR" || u == "DATE" || u == "S5TIME")
                return $"DB{dbNumber}.DBW{byteOff}";
            if (u == "DWORD" || u == "DINT" || u == "UDINT" || u == "REAL" ||
                u == "TIME"  || u == "TOD"  || u == "TIME_OF_DAY" || u == "DT")
                return $"DB{dbNumber}.DBD{byteOff}";
            // Struct, Array, String, Large types: show byte address
            return $"DB{dbNumber}.DBB{byteOff}";
        }

        private Dictionary<string, TagInfo> BuildTagLookup(string device)
        {
            var dict = new Dictionary<string, TagInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var tt in _tagTables.Where(t => t.Device == device))
                foreach (var tag in tt.Tags)
                    if (!string.IsNullOrEmpty(tag.Name) && !dict.ContainsKey(tag.Name))
                        dict[tag.Name] = tag;
            return dict;
        }

        // Lookup "DB_POSICAO.POSICAO_ENT_DIR" → MemberInfo  (só Static section dos DB do mesmo device)
        private Dictionary<string, MemberInfo> BuildDbMemberLookup(string device)
        {
            var dict = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var db in _blocks.Where(b => (b.Type == "DB" || b.Type == "iDB") && b.Device == device))
            {
                var staticSec = db.Interface.FirstOrDefault(s => s.Name == "Static");
                if (staticSec == null) continue;
                FlattenDbMembers(db.Name, staticSec.Members, "", dict);
            }
            return dict;
        }

        private static void FlattenDbMembers(string dbName, List<MemberInfo> members,
                                             string prefix, Dictionary<string, MemberInfo> dict)
        {
            foreach (var m in members)
            {
                var path = string.IsNullOrEmpty(prefix) ? m.Name : prefix + "." + m.Name;
                var key  = dbName + "." + path;
                if (!dict.ContainsKey(key)) dict[key] = m;
                if (m.Members.Count > 0)
                    FlattenDbMembers(dbName, m.Members, path, dict);
            }
        }

        private static string LangHint(string lang)
        {
            return lang switch
            {
                "SCL"   => "pascal",
                "LAD"   => "",
                "FBD"   => "",
                "STL"   => "asm",
                "GRAPH" => "",
                _       => ""
            };
        }

        private static void MdMembers(StringBuilder sb, List<MemberInfo> members, string indent)
        {
            foreach (var m in members)
            {
                var init = string.IsNullOrWhiteSpace(m.InitialValue) ? "" : $" := {m.InitialValue}";
                var cmt  = string.IsNullOrWhiteSpace(m.Comment) ? "" : $"   // {m.Comment}";
                sb.AppendLine($"{indent}{m.Name} : {m.DataType}{init}{cmt}");
                if (m.Members.Count > 0)
                    MdMembers(sb, m.Members, indent + "    ");
            }
        }

        private static string Esc(string s) => s?.Replace("|", "\\|") ?? "";

        // ── Índice de Falhas e Alarmes ────────────────────────────────────────
        private static void MdFaultIndex(StringBuilder sb, List<BlockInfo> blocks, List<TagTableInfo> tagTables)
        {
            var prefixes = new[] { "DEF_", "AL_", "ALARM_", "ERR_", "FAULT_" };
            var faultTags = tagTables
                .SelectMany(tt => tt.Tags)
                .Where(t => prefixes.Any(p => t.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.Name)
                .ToList();

            if (faultTags.Count == 0) return;

            // Mapa tag → redes onde aparece
            var occ = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in blocks)
                foreach (var net in b.Networks)
                    foreach (var v in net.UsedVariables)
                    {
                        if (!occ.ContainsKey(v)) occ[v] = new List<string>();
                        var r = string.IsNullOrWhiteSpace(net.Title)
                            ? $"{b.Name} N{net.Index}"
                            : $"{b.Name} N{net.Index} \"{net.Title}\"";
                        if (!occ[v].Contains(r)) occ[v].Add(r);
                    }

            sb.AppendLine("### Índice de Falhas e Alarmes");
            sb.AppendLine();
            sb.AppendLine("> Tags com prefixo DEF_, AL_, ALARM_, ERR_ — condições de falha e alarme do sistema.");
            sb.AppendLine();
            sb.AppendLine("| Tag | Endereço | Tipo | Descrição | Aparece nas redes |");
            sb.AppendLine("|-----|----------|------|-----------|-------------------|");
            foreach (var tag in faultTags)
            {
                var uses = occ.TryGetValue(tag.Name, out var lst)
                    ? string.Join(", ", lst.Take(6)) + (lst.Count > 6 ? $" (+{lst.Count - 6})" : "")
                    : "—";
                sb.AppendLine($"| `{Esc(tag.Name)}` | `{Esc(tag.Address)}` | {Esc(tag.DataType)} | {Esc(tag.Comment)} | {Esc(uses)} |");
            }
            sb.AppendLine();
        }

        // ── I/O Disponível ────────────────────────────────────────────────────
        private static void MdAvailableIO(StringBuilder sb, List<TagTableInfo> tagTables)
        {
            var usedI = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedQ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tagTables.SelectMany(tt => tt.Tags))
            {
                if (string.IsNullOrEmpty(tag.Address)) continue;
                var a = tag.Address.TrimStart('%');
                if      (IsBitAddr(a, "I")) usedI.Add(a);
                else if (IsBitAddr(a, "Q")) usedQ.Add(a);
            }

            if (usedI.Count == 0 && usedQ.Count == 0) return;

            int maxI = MaxByte(usedI, "I");
            int maxQ = MaxByte(usedQ, "Q");

            sb.AppendLine("### Entradas e Saídas Digitais Disponíveis");
            sb.AppendLine();
            sb.AppendLine("> Endereços digitais livres — disponíveis para novos sensores e atuadores.");
            sb.AppendLine();

            var freeI = FreeBits("I", usedI, maxI + 1);
            var freeQ = FreeBits("Q", usedQ, maxQ + 1);

            sb.AppendLine("**Entradas livres (%I):**");
            sb.AppendLine();
            if (freeI.Count > 0)
                sb.AppendLine("`" + string.Join("`  `", freeI.Select(a => "%" + a)) + "`");
            else
                sb.AppendLine("_(todas as entradas estão atribuídas)_");

            sb.AppendLine();
            sb.AppendLine("**Saídas livres (%Q):**");
            sb.AppendLine();
            if (freeQ.Count > 0)
                sb.AppendLine("`" + string.Join("`  `", freeQ.Select(a => "%" + a)) + "`");
            else
                sb.AppendLine("_(todas as saídas estão atribuídas)_");

            sb.AppendLine();
        }

        private static bool IsBitAddr(string a, string prefix) =>
            a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith(prefix + "W", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith(prefix + "D", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith(prefix + "B", StringComparison.OrdinalIgnoreCase) &&
            a.Contains('.');

        private static int MaxByte(HashSet<string> bits, string prefix)
        {
            int max = 0;
            foreach (var b in bits)
            {
                var s = b.Substring(prefix.Length);
                var dot = s.IndexOf('.');
                if (dot > 0 && int.TryParse(s.Substring(0, dot), out int n))
                    max = Math.Max(max, n);
            }
            return max;
        }

        private static List<string> FreeBits(string prefix, HashSet<string> used, int maxByte)
        {
            var free = new List<string>();
            for (int b = 0; b <= maxByte; b++)
                for (int bit = 0; bit <= 7; bit++)
                {
                    var a = $"{prefix}{b}.{bit}";
                    if (!used.Contains(a)) free.Add(a);
                }
            return free;
        }

        // ── Cross-Reference de Tags ───────────────────────────────────────────
        private static void MdTagCrossRef(StringBuilder sb, List<BlockInfo> blocks, List<TagTableInfo> tagTables)
        {
            var allTagNames = new HashSet<string>(
                tagTables.SelectMany(tt => tt.Tags).Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            var xref = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in blocks)
                foreach (var net in b.Networks)
                    foreach (var v in net.UsedVariables)
                    {
                        if (!allTagNames.Contains(v)) continue;
                        if (!xref.ContainsKey(v)) xref[v] = new List<string>();
                        var r = string.IsNullOrWhiteSpace(net.Title)
                            ? $"{b.Name} N{net.Index}"
                            : $"{b.Name} N{net.Index}";
                        if (!xref[v].Contains(r)) xref[v].Add(r);
                    }

            if (xref.Count == 0) return;

            var tagLookup = tagTables.SelectMany(tt => tt.Tags)
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            sb.AppendLine("### Referências Cruzadas de Tags");
            sb.AppendLine();
            sb.AppendLine("> Para cada tag, lista todos os blocos e redes onde é utilizada. Essencial para rastrear sinal desde a entrada física até à saída.");
            sb.AppendLine();
            sb.AppendLine("| Tag | Endereço | Comentário | Usada em (bloco N°rede) |");
            sb.AppendLine("|-----|----------|------------|------------------------|");
            foreach (var kv in xref.OrderBy(x => x.Key))
            {
                tagLookup.TryGetValue(kv.Key, out var ti);
                var addr = ti?.Address ?? "";
                var cmt  = ti?.Comment ?? "";
                var uses = string.Join(", ", kv.Value.Take(10));
                if (kv.Value.Count > 10) uses += $" (+{kv.Value.Count - 10})";
                sb.AppendLine($"| `{Esc(kv.Key)}` | `{Esc(addr)}` | {Esc(cmt)} | {Esc(uses)} |");
            }
            sb.AppendLine();
        }

        // ── Tags Sem Descrição ────────────────────────────────────────────────
        private static void MdTagsWithoutDescription(StringBuilder sb, List<TagTableInfo> tagTables)
        {
            var missing = tagTables
                .SelectMany(tt => tt.Tags
                    .Where(t => string.IsNullOrWhiteSpace(t.Comment))
                    .Select(t => (Table: tt.Name, Tag: t)))
                .OrderBy(x => x.Table).ThenBy(x => x.Tag.Name)
                .ToList();

            if (missing.Count == 0) return;

            sb.AppendLine("### ⚠ Tags Sem Descrição");
            sb.AppendLine();
            sb.AppendLine($"> **{missing.Count} tags** não têm comentário preenchido no TIA Portal.");
            sb.AppendLine($"> Adicionar descrições a estas tags melhora significativamente a qualidade da análise por IA.");
            sb.AppendLine($"> Para a IA, um tag sem comentário é um sinal desconhecido — pode descrever o seu propósito funcional aqui.");
            sb.AppendLine();
            sb.AppendLine("| Tag Table | Tag | Endereço | Tipo |");
            sb.AppendLine("|-----------|-----|----------|------|");
            foreach (var (table, tag) in missing)
                sb.AppendLine($"| {Esc(table)} | `{Esc(tag.Name)}` | `{Esc(tag.Address)}` | {Esc(tag.DataType)} |");
            sb.AppendLine();
        }

        private static void MdHardware(StringBuilder sb, HwDeviceInfo hw)
        {
            sb.AppendLine("### Topologia de Hardware");
            sb.AppendLine();

            // Cabeçalho do dispositivo
            if (!string.IsNullOrEmpty(hw.OrderNumber))
                sb.AppendLine($"> **Referência:** `{hw.OrderNumber}`");
            if (!string.IsNullOrEmpty(hw.Comment))
                sb.AppendLine($"> **Descrição:** {hw.Comment}");

            var net = hw.Network;
            if (!string.IsNullOrEmpty(net.IpAddress))
            {
                sb.Append($"> **IP:** `{net.IpAddress}`");
                if (!string.IsNullOrEmpty(net.SubnetMask))    sb.Append($"  Máscara: `{net.SubnetMask}`");
                if (!string.IsNullOrEmpty(net.RouterAddress)) sb.Append($"  Gateway: `{net.RouterAddress}`");
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(net.ProfinetName))
                sb.AppendLine($"> **PROFINET:** `{net.ProfinetName}`");

            sb.AppendLine();

            // Tabela de módulos
            if (hw.Modules.Count > 0)
            {
                sb.AppendLine("| Slot | Módulo | Referência | Entradas | Saídas | Comentário |");
                sb.AppendLine("|------|--------|------------|----------|--------|------------|");
                foreach (var m in hw.Modules)
                {
                    sb.AppendLine($"| {m.Slot} | {Esc(m.Name)} | `{Esc(m.OrderNumber)}` | {Esc(m.InputRange)} | {Esc(m.OutputRange)} | {Esc(m.Comment)} |");
                    foreach (var sub in m.SubModules)
                        sb.AppendLine($"| └ {sub.Slot} | {Esc(sub.Name)} | `{Esc(sub.OrderNumber)}` | {Esc(sub.InputRange)} | {Esc(sub.OutputRange)} | {Esc(sub.Comment)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        private void ExportDeviceMd(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;
            var installName = AskInstallationName(deviceName);
            if (installName == null) return;  // user cancelled
            var devBlocks    = _blocks.Where(b => b.Device == deviceName).ToList();
            var devTagTables = _tagTables.Where(t => t.Device == deviceName).ToList();
            var devUdts      = _udts.Where(u => u.Device == deviceName).ToList();
            var md = BuildMarkdown(devBlocks, devTagTables, devUdts, _txtPath.Text, installName);
            var safeName = string.Concat(deviceName.Split(Path.GetInvalidFileNameChars()));
            using var dlg = new SaveFileDialog
            {
                Title    = "Exportar PLC para IA",
                Filter   = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
                FileName = $"DaniloTracker_IA_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.md"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, md, Encoding.UTF8);
                var lines = md.Count(c => c == '\n');
                SetStatus($"Exportado: {Path.GetFileName(dlg.FileName)}  ({lines:N0} linhas)", C_OK);
            }
        }

        // ── Exportar Tags deste PLC (com glossário global e filtro) ──────────────
        private void ExportTagsMd(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;
            var installName = AskInstallationName(deviceName);
            if (installName == null) return;

            var devTags = _tagTables.Where(t => t.Device == deviceName).ToList();
            if (devTags.Count == 0)
            {
                MessageBox.Show("Nenhuma Tag Table encontrada para este PLC.", "Tags",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var md       = BuildTagsMd(deviceName, installName, devTags);
            var safeName = string.Concat(deviceName.Split(Path.GetInvalidFileNameChars()));
            using var dlg = new SaveFileDialog
            {
                Title    = "Exportar Tags para IA",
                Filter   = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
                FileName = $"Tags_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.md"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, md, Encoding.UTF8);
                var lines = md.Count(c => c == '\n');
                SetStatus($"Tags exportadas: {Path.GetFileName(dlg.FileName)}  ({lines:N0} linhas)", C_OK);
            }
        }

        // ── Export Multi-ficheiro ─────────────────────────────────────────────────

        private void ExportTagsMultiMd(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;
            var installName = AskInstallationName(deviceName);
            if (installName == null) return;

            var devTags = _tagTables.Where(t => t.Device == deviceName).ToList();
            if (devTags.Count == 0)
            {
                MessageBox.Show("Nenhuma Tag Table encontrada para este PLC.", "Tags",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Escolher pasta base
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Escolhe a pasta base onde criar {deviceName}/tags/"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            // Criar estrutura: <base>/<PLC>/<tags>/
            var safeName  = string.Concat(deviceName.Split(Path.GetInvalidFileNameChars()));
            var tagsDir   = Path.Combine(dlg.SelectedPath, safeName, "tags");
            Directory.CreateDirectory(tagsDir);

            var files = BuildMultiTagFiles(deviceName, installName, devTags);
            int total  = 0;
            foreach (var kv in files)
            {
                File.WriteAllText(Path.Combine(tagsDir, kv.Key), kv.Value, Encoding.UTF8);
                total++;
            }

            SetStatus($"Exportados {total} ficheiros → {tagsDir}", C_OK);
            MessageBox.Show(
                $"{total} ficheiros criados em:\n{tagsDir}\n\nCarrega a pasta no AnythingLLM.",
                "Export Multi-ficheiro concluído",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static Dictionary<string, string> BuildMultiTagFiles(
            string deviceName, string installName, List<TagTableInfo> tagTables)
        {
            var result = new Dictionary<string, string>();
            var stamp  = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // Cabeçalho reutilizável
            string Hdr(string title) =>
                $"# {title} — {installName}\n\n" +
                $"> **PLC:** `{deviceName}`  \n" +
                $"> **Exportado em:** {stamp}  \n" +
                $"> **Fonte:** TiaTracker — Barragem de Crestuma-Lever\n\n---\n\n";

            // Filtrar tags
            var allTags = tagTables.SelectMany(tt => tt.Tags
                .Where(t => !IsAnonymousTag(t) && !IsSystemTag(t))
                .Select(t => (Table: tt.Name, Tag: t))).ToList();

            var diTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "di").ToList();
            var doTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "do").ToList();
            var aiTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "ai").ToList();
            var aoTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "ao").ToList();
            var mbTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "mb").ToList();
            var mwTags = allTags.Where(x => GetTagCategory(x.Tag.Address) == "mw").ToList();

            // ── Resumo de equipamentos ──────────────────────────────────────────
            {
                var sb = new StringBuilder();
                sb.Append(Hdr("Resumo de Equipamentos"));
                sb.AppendLine($"Este ficheiro é o ponto de entrada para consultas gerais sobre o **{deviceName}**.");
                sb.AppendLine("Descreve em linguagem simples os equipamentos físicos controlados por este PLC.");
                sb.AppendLine();
                AppendEquipmentSummary(sb, deviceName, diTags, doTags);
                result["resumo_equipamentos.md"] = sb.ToString();
            }

            // ── Entradas Digitais por grupo ────────────────────────────────────
            static string GetDiG(string n) { var u = n.ToUpperInvariant();
                if (u.StartsWith("RM_")||u.StartsWith("DEF_ARR")||u.StartsWith("PROT_BOMB")||u.StartsWith("PROT_VALV")||u.StartsWith("DISP_INT_GER")||u.StartsWith("PROT_ANALIS")||u.StartsWith("PROT_DESCARR")||u.StartsWith("DEF_DESCARR")||u.StartsWith("ENCARQ_")||u.StartsWith("VARIAD_")||u.StartsWith("TRAV_")||u.StartsWith("FREIO_")) return "di_motores_bombas";
                if (u.StartsWith("FC_")) return "di_fins_curso";
                if (u.StartsWith("BT_")||u.StartsWith("STOP_")||u.StartsWith("RESET")||u.StartsWith("TEST_")) return "di_botoes_comandos";
                if (u.StartsWith("COND_")||u.StartsWith("DISP_")||u.StartsWith("PRES_")||u.StartsWith("EMERG_")||u.StartsWith("PS_")||u.StartsWith("AL_MOD")||u.StartsWith("DEF_MOD")||u.StartsWith("PROT_24")||u.StartsWith("PRES_24")||u.StartsWith("PROT_")||u.StartsWith("FALT_")) return "di_condicoes_alimentacao";
                if (u.StartsWith("RESERV_")||u.StartsWith("RESERVA_")) return "di_reservas";
                return "di_outros"; }

            var diGroupTitles = new Dictionary<string, (string Title, string Prose)>
            {
                { "di_motores_bombas",       ("Entradas Digitais — Motores e Bombas",
                  "As bombas hidráulicas são os motores que pressurizam o óleo para mover os pistões das comportas. " +
                  "Cada motor tem retorno de marcha (confirma que está a girar), defeito de arranque e proteção elétrica. " +
                  "Quando um motor não arranca: verificar proteção → retorno de marcha → defeito de arranque.") },
                { "di_fins_curso",           ("Entradas Digitais — Sensores de Posição (Fins de Curso)",
                  "Sensores mecânicos nos pistões hidráulicos. Indicam se o pistão chegou ao topo (aberto), " +
                  "está estabilizado, ou desceu completamente (fechado). " +
                  "Se o enchimento não avança de fase, verificar se o sensor da fase atual confirmou a posição.") },
                { "di_botoes_comandos",      ("Entradas Digitais — Botões e Comandos do Quadro",
                  "Botões físicos do quadro elétrico local e comandos remotos: subir, descer, stop, " +
                  "seleção de modo manual/automático e seleção de comporta A (direita) ou B (esquerda).") },
                { "di_condicoes_alimentacao",("Entradas Digitais — Condições, Presenças de Tensão e Alimentação",
                  "Condições de autorização de outros PLCs, presenças de tensão (400VAC, 220VDC, 24VDC) " +
                  "e proteções gerais do quadro. Se o sistema não arranca sem causa aparente, verificar estes sinais.") },
                { "di_reservas",             ("Entradas Digitais — Entradas Reservadas",
                  "Entradas físicas não ligadas a nenhum equipamento. Reservadas para expansão futura. " +
                  "O seu estado não influencia o funcionamento do sistema.") },
                { "di_outros",               ("Entradas Digitais — Outros", "Entradas digitais sem classificação definida.") },
            };

            foreach (var grp in diTags.GroupBy(x => GetDiG(x.Tag.Name)).OrderBy(g => g.Key))
            {
                if (!diGroupTitles.TryGetValue(grp.Key, out var meta)) continue;
                var sb = new StringBuilder();
                sb.Append(Hdr(meta.Title));
                sb.AppendLine(meta.Prose);
                sb.AppendLine();
                sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|------------|---------------------|");
                foreach (var (table, tag) in grp.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {cmt} | {InferTagMeaning(tag.Name)} |");
                }
                result[$"{grp.Key}.md"] = sb.ToString();
            }

            // ── Saídas Digitais por grupo ──────────────────────────────────────
            static string GetDoG(string n) { var u = n.ToUpperInvariant();
                if (u.StartsWith("OS_")||u.StartsWith("OM_")||u.StartsWith("OA_")||u.StartsWith("OD_")) return "do_ordens_marcha";
                if (u.StartsWith("SIN_BOMB")||u.StartsWith("SIN_VALV")||u.StartsWith("SIN_COMP")||u.StartsWith("SIN_ESTAB")||u.StartsWith("SIN_V2_")||u.StartsWith("SIN_V_DESC")||u.StartsWith("SIN_DEF_COMP")||u.StartsWith("SIN_AG")||u.StartsWith("SIN_CIRC")) return "do_sinalizacoes_comportas";
                if (u.StartsWith("SIN_")) return "do_sinalizacoes_gerais";
                if (u.StartsWith("INTERF_")||u.StartsWith("INT_")) return "do_interface_comando";
                if (u.StartsWith("BYPASS_")||u.StartsWith("BY_PASS_")||u.StartsWith("SEMAF_")||u.StartsWith("RESERV_")||u.StartsWith("RESERVA_")||u.StartsWith("CPU_")) return "do_outros";
                return "do_outros"; }

            var doGroupTitles = new Dictionary<string, (string Title, string Prose)>
            {
                { "do_ordens_marcha",         ("Saídas Digitais — Ordens de Arranque e Marcha",
                  "Comandos que ligam e desligam motores, bombas e válvulas hidráulicas. " +
                  "OS_ arranca um motor; OM_ abre ou fecha uma válvula. " +
                  "Após emitir uma ordem de arranque, o PLC aguarda o retorno de marcha — se não chegar, regista defeito.") },
                { "do_sinalizacoes_comportas", ("Saídas Digitais — Sinalizações de Comportas e Bombas",
                  "Lâmpadas e indicações no painel que mostram o estado das comportas e bombas. " +
                  "Não comandam equipamentos — apenas sinalizam o estado para os operadores.") },
                { "do_sinalizacoes_gerais",    ("Saídas Digitais — Sinalizações Gerais (modo, alimentação, defeitos)",
                  "Lâmpadas de modo (automático/manual/desligado), presença de tensão, faltas e defeitos gerais do quadro.") },
                { "do_interface_comando",      ("Saídas Digitais — Interface para PLC Comando",
                  "Impulsos de comunicação enviados ao PLC_Comando. Não atuam em hardware local — " +
                  "informam o PLC_Comando sobre o estado do enchimento para gerir as autorizações.") },
                { "do_outros",                 ("Saídas Digitais — Bypass, Semáforos e Reservas",
                  "Bypass de segurança (manutenção), semáforos náuticos para embarcações e saídas reservadas.") },
            };

            foreach (var grp in doTags.GroupBy(x => GetDoG(x.Tag.Name)).OrderBy(g => g.Key))
            {
                if (!doGroupTitles.TryGetValue(grp.Key, out var meta)) continue;
                var sb = new StringBuilder();
                sb.Append(Hdr(meta.Title));
                sb.AppendLine(meta.Prose);
                sb.AppendLine();
                sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|------------|---------------------|");
                foreach (var (table, tag) in grp.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {cmt} | {InferTagMeaning(tag.Name)} |");
                }
                result[$"{grp.Key}.md"] = sb.ToString();
            }

            // ── Analógicas ────────────────────────────────────────────────────
            if (aiTags.Count > 0 || aoTags.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(Hdr("Entradas e Saídas Analógicas"));
                sb.AppendLine("Medições analógicas (níveis, velocidades, tensões, correntes, potências) e setpoints para variadores.");
                sb.AppendLine();
                if (aiTags.Count > 0)
                {
                    sb.AppendLine("### Entradas Analógicas");
                    sb.AppendLine();
                    sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                    sb.AppendLine("|----------|------|----------|------------|---------------------|");
                    foreach (var (_, tag) in aiTags.OrderBy(x => x.Tag.Address))
                        sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {(string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim()))} | {InferTagMeaning(tag.Name)} |");
                    sb.AppendLine();
                }
                if (aoTags.Count > 0)
                {
                    sb.AppendLine("### Saídas Analógicas");
                    sb.AppendLine();
                    sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                    sb.AppendLine("|----------|------|----------|------------|---------------------|");
                    foreach (var (_, tag) in aoTags.OrderBy(x => x.Tag.Address))
                        sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {(string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim()))} | {InferTagMeaning(tag.Name)} |");
                }
                result["analogicas.md"] = sb.ToString();
            }

            // ── Memórias Bits por grupo ────────────────────────────────────────
            static string GetMbG(string n) { var u = n.ToUpperInvariant();
                if (u.StartsWith("DEF_")) return "mb_defeitos";
                if (u.StartsWith("AL_"))  return "mb_alarmes";
                if (u.StartsWith("REG_")) return "mb_registos";
                if (u.StartsWith("IMP_")||u.StartsWith("INTERF_")) return "mb_impulsos";
                if (u.StartsWith("INTERD_")||u.StartsWith("AUTOR_")||u.StartsWith("COND_")) return "mb_interdicoes";
                if (u.StartsWith("ENT_")||u.StartsWith("SAID_")) return "mb_navegacao";
                if (u.StartsWith("FC_ENCOD_")||u.StartsWith("M_")) return "mb_controlo";
                return "mb_outros"; }

            var mbGroupTitles = new Dictionary<string, (string Title, string Prose)>
            {
                { "mb_defeitos",    ("Memórias — Defeitos",
                  "Bits que ficam verdadeiro quando um equipamento falha. Um defeito bloqueia a operação e acende alarme no painel. " +
                  "Para repor: corrigir a causa e fazer reset. Resolver primeiro defeitos de alimentação e emergência.") },
                { "mb_alarmes",     ("Memórias — Alarmes",
                  "Condições anormais do processo: velocidade fora dos limites, pressão baixa, tempo excedido. " +
                  "Um alarme não bloqueia necessariamente mas indica que algo está fora dos parâmetros normais.") },
                { "mb_registos",    ("Memórias — Registos de Estado",
                  "Guardam o estado atual do ciclo: modo automático/manual, fase do enchimento, posição das comportas. " +
                  "Quando algo falha, consultar os registos para perceber em que fase o processo estava.") },
                { "mb_impulsos",    ("Memórias — Impulsos e Interfaces entre PLCs",
                  "Sinais de comunicação entre PLCs. Não representam equipamentos físicos. " +
                  "Se houver falha de coordenação entre PLCs, verificar estes bits.") },
                { "mb_interdicoes", ("Memórias — Interdições e Autorizações",
                  "Interdições bloqueiam uma operação por segurança; autorizações permitem avançar. " +
                  "Se o sistema não executa uma manobra esperada, verificar interdições ativas ou autorizações em falta.") },
                { "mb_navegacao",   ("Memórias — Permissões de Navegação",
                  "Autorização de entrada/saída de embarcações na eclusa. Coordenados com o PLC_Comando.") },
                { "mb_controlo",    ("Memórias — Controlo de Atuadores e Encoder",
                  "Bits internos de controlo dos pistões: ordem de subida/descida ativa, pausa, diferença de posição entre lados. " +
                  "Também inclui posições calculadas por encoder.") },
                { "mb_outros",      ("Memórias — Outros Merkers",
                  "Clocks de temporização, resets, flags de operação e sinalizações internas diversas.") },
            };

            foreach (var grp in mbTags.GroupBy(x => GetMbG(x.Tag.Name)).OrderBy(g => g.Key))
            {
                if (!mbGroupTitles.TryGetValue(grp.Key, out var meta)) continue;
                var sb = new StringBuilder();
                sb.Append(Hdr(meta.Title));
                sb.AppendLine(meta.Prose);
                sb.AppendLine();
                sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|------------|---------------------|");
                foreach (var (table, tag) in grp.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {cmt} | {InferTagMeaning(tag.Name)} |");
                }
                result[$"{grp.Key}.md"] = sb.ToString();
            }

            // ── Words / Events ─────────────────────────────────────────────────
            if (mwTags.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(Hdr("Memórias — Palavras de Alarme e Eventos"));
                sb.AppendLine("Palavras de registo de alarmes (ALARM_) e eventos de operação (EVENTOS_). " +
                              "Cada bit dentro destas palavras corresponde a um alarme ou evento específico.");
                sb.AppendLine();
                sb.AppendLine("| Variável | Tipo | Endereço | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|------------|---------------------|");
                foreach (var (_, tag) in mwTags.OrderBy(x => x.Tag.Address))
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {(string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim()))} | {InferTagMeaning(tag.Name)} |");
                result["mw_alarmes_eventos.md"] = sb.ToString();
            }

            return result;
        }

        private static string BuildTagsMd(string deviceName, string installName, List<TagTableInfo> tagTables)
        {
            var sb = new StringBuilder();

            // ── Cabeçalho ────────────────────────────────────────────────────────
            sb.AppendLine($"# Tags — {installName}");
            sb.AppendLine();
            sb.AppendLine($"> **PLC:** `{deviceName}`  ");
            sb.AppendLine($"> **Exportado em:** {DateTime.Now:yyyy-MM-dd HH:mm}  ");
            sb.AppendLine($"> **Fonte:** TiaTracker — Barragem de Crestuma-Lever");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // ── Recolher e filtrar todos os tags ─────────────────────────────────
            var allTags = tagTables.SelectMany(tt => tt.Tags
                .Where(t => !IsAnonymousTag(t))
                .Select(t => (Table: tt.Name, Tag: t))
            ).ToList();

            var sysTags = allTags.Where(x => IsSystemTag(x.Tag)).ToList();
            var diTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "di").ToList();
            var doTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "do").ToList();
            var aiTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "ai").ToList();
            var aoTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "ao").ToList();
            var mbTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "mb").ToList();
            var mwTags  = allTags.Where(x => !IsSystemTag(x.Tag) && GetTagCategory(x.Tag.Address) == "mw").ToList();

            // ── Resumo de equipamentos em linguagem simples ───────────────────────
            AppendEquipmentSummary(sb, deviceName, diTags, doTags);

            int filteredCount = tagTables.SelectMany(t => t.Tags).Count() - allTags.Count;
            sb.AppendLine($"_Total de tags exportadas: **{allTags.Count}**  |  Tags anónimas filtradas: **{filteredCount}**_");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // ── Secções por categoria ─────────────────────────────────────────────
            AppendDigitalInputsSubdivided(sb, diTags);
            AppendDigitalOutputsSubdivided(sb, doTags);

            AppendTagSection(sb, "Entradas Analógicas (%IW / %ID — Int / Real)",
                "Medições analógicas: níveis, velocidades, tensões, correntes, potências.",
                aiTags);

            AppendTagSection(sb, "Saídas Analógicas (%QW — Int)",
                "Setpoints analógicos para variadores de frequência.",
                aoTags);

            AppendMemoryBitsSubdivided(sb, mbTags);

            AppendTagSection(sb, "Memórias — Words/DWords (%MW / %MD / %MB)",
                "Palavras de alarme, registo de eventos, contadores e parâmetros.",
                mwTags);

            // Tags de sistema (informativo, sem destaque)
            if (sysTags.Count > 0)
            {
                sb.AppendLine("## Tags de Sistema S7 (informativo)");
                sb.AppendLine();
                sb.AppendLine("> Geradas automaticamente pelo TIA Portal. Não modificar.");
                sb.AppendLine();
                sb.AppendLine("| Variável | Tipo | Endereço |");
                sb.AppendLine("|----------|------|----------|");
                foreach (var (_, tag) in sysTags.OrderBy(x => x.Tag.Address))
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | `{Esc(tag.Address)}` |");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gera uma secção de resumo em linguagem simples dos equipamentos do PLC.
        /// Este chunk é o primeiro a ser recuperado em queries genéricas ("bomba", "comporta", etc.)
        /// </summary>
        private static void AppendEquipmentSummary(StringBuilder sb, string deviceName,
            List<(string Table, TagInfo Tag)> diTags,
            List<(string Table, TagInfo Tag)> doTags)
        {
            sb.AppendLine("## O que controla este PLC");
            sb.AppendLine();
            sb.AppendLine($"Este documento descreve todos os sinais e equipamentos controlados pelo **{deviceName}**.");
            sb.AppendLine("Pode ser consultado por qualquer pessoa — operador, técnico ou engenheiro — em linguagem natural.");
            sb.AppendLine("Para cada equipamento físico da eclusa está documentado o sinal correspondente no PLC, o seu endereço de memória e o seu estado esperado.");
            sb.AppendLine();

            // Motores: detectar pelos RM_ (retorno de marcha = confirmação de motor ligado)
            var motors = diTags
                .Where(x => x.Tag.Name.StartsWith("RM_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Tag.Name.Substring(3)) // remove "RM_"
                .Distinct().OrderBy(n => n).ToList();

            // Actuadores de saída: OS_ e OM_ (ordens de arranque/marcha)
            var actuators = doTags
                .Where(x => x.Tag.Name.StartsWith("OS_", StringComparison.OrdinalIgnoreCase)
                         || x.Tag.Name.StartsWith("OM_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Tag.Name)
                .Distinct().OrderBy(n => n).ToList();

            // Sensores de posição: FC_ nas entradas
            var sensors = diTags
                .Where(x => x.Tag.Name.StartsWith("FC_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Tag.Name)
                .Distinct().OrderBy(n => n).ToList();

            if (motors.Count > 0)
            {
                sb.AppendLine("### Motores e Bombas Hidráulicas");
                sb.AppendLine();
                sb.AppendLine("As bombas hidráulicas são os motores elétricos que pressurizam o óleo do circuito hidráulico para mover os pistões (cilindros) das comportas de enchimento.");
                sb.AppendLine("Quando uma bomba arranca, o contactor fecha e o motor começa a girar — isso é confirmado pelo sinal de retorno de marcha.");
                sb.AppendLine("Se o motor não arranca, disparou a proteção, ou parou sozinho, o PLC regista um defeito e bloqueia a operação.");
                sb.AppendLine("Os motores identificados neste PLC são:");
                sb.AppendLine("| Motor / Bomba | Retorno de Marcha | Ordem de Arranque | Descrição |");
                sb.AppendLine("|---------------|-------------------|-------------------|-----------|");
                foreach (var m in motors)
                {
                    var rmTag  = $"RM_{m}";
                    var osTag  = doTags.FirstOrDefault(x =>
                        x.Tag.Name.StartsWith("OS_", StringComparison.OrdinalIgnoreCase) &&
                        x.Tag.Name.IndexOf(m.Split('_')[0], StringComparison.OrdinalIgnoreCase) >= 0).Tag?.Name ?? "—";
                    var desc   = InferTagMeaning(rmTag).Replace("Retorno Marcha — ", "");
                    sb.AppendLine($"| {desc} | `{rmTag}` | `{osTag}` | {InferTagMeaning(rmTag)} |");
                }
                sb.AppendLine();
            }

            if (sensors.Count > 0)
            {
                sb.AppendLine("### Sensores de Posição das Comportas (Fins de Curso)");
                sb.AppendLine();
                sb.AppendLine("Os fins de curso são sensores mecânicos instalados nos pistões (cilindros hidráulicos) das comportas.");
                sb.AppendLine("Cada pistão tem sensores que indicam se chegou ao topo (comporta aberta), se está na posição intermédia (estabilizado), ou se desceu completamente (comporta fechada).");
                sb.AppendLine("Se o processo de enchimento parar a meio ou não avançar de fase, verificar se o sensor da fase atual confirmou a posição.");
                sb.AppendLine("| Sensor | Endereço | Descrição |");
                sb.AppendLine("|--------|----------|-----------|");
                foreach (var (_, tag) in diTags
                    .Where(x => x.Tag.Name.StartsWith("FC_", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Tag.Address))
                {
                    sb.AppendLine($"| `{tag.Name}` | {HumanAddress(tag.Address)} | {InferTagMeaning(tag.Name)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>
        /// Entradas Digitais subdivididas por grupo funcional.
        /// Garante que "motores", "botões", "fins de curso" ficam em chunks separados.
        /// </summary>
        private static void AppendDigitalInputsSubdivided(StringBuilder sb,
            List<(string Table, TagInfo Tag)> diTags)
        {
            if (diTags.Count == 0) return;

            static string GetDiGroup(string name)
            {
                var up = name.ToUpperInvariant();
                if (up.StartsWith("RM_")    || up.StartsWith("DEF_ARR") ||
                    up.StartsWith("PROT_BOMB") || up.StartsWith("PROT_VALV") ||
                    up.StartsWith("DISP_INT_GER") || up.StartsWith("PROT_ANALIS") ||
                    up.StartsWith("PROT_DESCARR") || up.StartsWith("DEF_DESCARR") ||
                    up.StartsWith("ENCARQ_") || up.StartsWith("VARIAD_") ||
                    up.StartsWith("TRAV_")   || up.StartsWith("FREIO_"))
                    return "1:Motores e Bombas";
                if (up.StartsWith("FC_"))
                    return "2:Fins de Curso — Sensores de Posição";
                if (up.StartsWith("BT_") || up.StartsWith("STOP_") ||
                    up.StartsWith("RESET") || up.StartsWith("TEST_"))
                    return "3:Botões e Comandos do Quadro";
                if (up.StartsWith("COND_"))
                    return "4:Condições e Permissões";
                if (up.StartsWith("DISP_") || up.StartsWith("PRES_"))
                    return "5:Presenças de Tensão e Disponibilidades";
                if (up.StartsWith("EMERG_") || up.StartsWith("PS_") ||
                    up.StartsWith("AL_MOD")  || up.StartsWith("DEF_MOD") ||
                    up.StartsWith("PROT_24") || up.StartsWith("PRES_24") ||
                    up.StartsWith("PROT_")   || up.StartsWith("FALT_"))
                    return "6:Alimentação e Proteções Gerais";
                if (up.StartsWith("RESERV_") || up.StartsWith("RESERVA_"))
                    return "7:Entradas Reservadas";
                return "8:Outros";
            }

            var groupDesc = new Dictionary<string, string>
            {
                { "1:Motores e Bombas",
                  "As bombas hidráulicas são os motores que pressurizam o óleo para mover os pistões das comportas. " +
                  "Cada motor tem três sinais de estado: o **retorno de marcha** confirma que o motor está a girar; " +
                  "o **defeito de arranque** indica que o motor foi comandado mas não arrancou; " +
                  "a **proteção elétrica** indica que o relé térmico ou disjuntor disparou por sobrecorrente ou sobreaquecimento. " +
                  "Quando uma bomba ou motor não arranca, para sozinho ou apresenta defeito, verificar nesta ordem: " +
                  "proteção elétrica disparada → retorno de marcha ausente → defeito de arranque ativo." },
                { "2:Fins de Curso — Sensores de Posição",
                  "Os fins de curso são sensores mecânicos instalados nos pistões hidráulicos das comportas de enchimento. " +
                  "Indicam com precisão a posição física do pistão: subida completa significa que a comporta está totalmente aberta; " +
                  "estabilizado significa posição intermédia segura; descida completa significa comporta fechada. " +
                  "Se o processo de enchimento parar ou não avançar para a fase seguinte, " +
                  "verificar se o sensor de posição da fase atual confirmou o estado esperado." },
                { "3:Botões e Comandos do Quadro",
                  "São os botões físicos do quadro elétrico local e os comandos enviados por controlo remoto. " +
                  "Incluem os botões de subir (abrir), descer (fechar) e stop, a seleção entre modo manual e automático, " +
                  "e a escolha de qual comporta (A lado direito ou B lado esquerdo) está a ser operada. " +
                  "Os comandos com sufixo REM vêm do sistema de controlo remoto; os sem sufixo são locais no quadro. " +
                  "Se o equipamento não responde a um comando, verificar se o botão está ativo e se o modo de operação correto está selecionado." },
                { "4:Condições e Permissões",
                  "São sinais de autorização enviados pelos outros PLCs da eclusa — por exemplo o PLC_Comando ou o PLC_Porta_Jusante. " +
                  "O processo de enchimento só pode iniciar ou continuar quando todas estas condições estão satisfeitas. " +
                  "Se o enchimento não inicia ou bloqueia inesperadamente, verificar primeiro se todas as condições estão a verdadeiro." },
                { "5:Presenças de Tensão e Disponibilidades",
                  "Confirmam que as fontes de alimentação elétrica estão presentes e que os equipamentos principais estão prontos a operar. " +
                  "Se faltar presença de 400VAC, 220VDC ou 24VDC o sistema bloqueia por segurança. " +
                  "Verificar estes sinais quando o sistema não arranca sem causa aparente." },
                { "6:Alimentação e Proteções Gerais",
                  "Estado das fontes de alimentação do quadro, proteções elétricas gerais e sinal de paragem de emergência. " +
                  "O sinal de emergência deve estar sempre ativo — se estiver a falso o sistema para completamente e não pode ser comandado. " +
                  "As fontes de alimentação convertem as tensões para os circuitos de controlo e medição." },
                { "7:Entradas Reservadas",
                  "Entradas físicas do módulo de I/O que não estão ligadas a nenhum equipamento atualmente. " +
                  "Estão reservadas para futuras expansões. O seu estado não influencia o funcionamento do sistema." },
                { "8:Outros",
                  "Entradas digitais com função específica não classificada nos grupos anteriores." },
            };

            var groups = diTags.GroupBy(x => GetDiGroup(x.Tag.Name))
                               .OrderBy(g => g.Key).ToList();

            sb.AppendLine("## Entradas Digitais (%I — Bool)");
            sb.AppendLine();
            sb.AppendLine($"Sinais físicos de entrada do PLC — {diTags.Count} sinais no total, organizados por tipo de equipamento.");
            sb.AppendLine("As entradas digitais leem o estado real dos equipamentos: se um motor está a girar, se uma comporta está aberta, se um botão foi premido.");
            sb.AppendLine();

            foreach (var g in groups)
            {
                var label = g.Key.Substring(2);
                sb.AppendLine($"### {label}");
                sb.AppendLine();
                if (groupDesc.TryGetValue(g.Key, out var desc))
                { sb.AppendLine(desc); sb.AppendLine(); }
                sb.AppendLine("| Variável | Tipo | Endereço | Tabela | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|--------|------------|---------------------|");
                foreach (var (table, tag) in g.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {Esc(table)} | {cmt} | {InferTagMeaning(tag.Name)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>
        /// Saídas Digitais subdivididas por grupo funcional.
        /// </summary>
        private static void AppendDigitalOutputsSubdivided(StringBuilder sb,
            List<(string Table, TagInfo Tag)> doTags)
        {
            if (doTags.Count == 0) return;

            static string GetDoGroup(string name)
            {
                var up = name.ToUpperInvariant();
                if (up.StartsWith("OS_") || up.StartsWith("OM_") || up.StartsWith("OA_") || up.StartsWith("OD_"))
                    return "1:Ordens de Arranque e Marcha";
                if (up.StartsWith("SIN_BOMB") || up.StartsWith("SIN_VALV") ||
                    up.StartsWith("SIN_COMP") || up.StartsWith("SIN_ESTAB") ||
                    up.StartsWith("SIN_V2_")  || up.StartsWith("SIN_V_DESC") ||
                    up.StartsWith("SIN_DEF_COMP") || up.StartsWith("SIN_AG") ||
                    up.StartsWith("SIN_CIRC"))
                    return "2:Sinalizações de Comportas e Bombas";
                if (up.StartsWith("SIN_AUTOM") || up.StartsWith("SIN_DESLIG") ||
                    up.StartsWith("SIN_MANUAL") || up.StartsWith("SIN_DEF_UN"))
                    return "3:Sinalizações de Modo de Operação";
                if (up.StartsWith("SIN_PRES") || up.StartsWith("SIN_FALT") ||
                    up.StartsWith("SIN_DISP") || up.StartsWith("SIN_DEF_24") ||
                    up.StartsWith("SIN_EMERG") || up.StartsWith("SIN_DEF_AG") ||
                    up.StartsWith("CPU_"))
                    return "4:Sinalizações de Alimentação e Defeitos Gerais";
                if (up.StartsWith("INTERF_") || up.StartsWith("INT_"))
                    return "5:Interface para PLC Comando";
                if (up.StartsWith("BYPASS_") || up.StartsWith("BY_PASS_"))
                    return "6:Bypass de Segurança";
                if (up.StartsWith("SEMAF_"))
                    return "7:Semáforos Náuticos";
                if (up.StartsWith("RESERV_") || up.StartsWith("RESERVA_"))
                    return "8:Saídas Reservadas";
                return "9:Outros";
            }

            var groupDesc = new Dictionary<string, string>
            {
                { "1:Ordens de Arranque e Marcha",
                  "São os comandos que o PLC envia aos equipamentos físicos para os ligar, desligar ou mover. " +
                  "Uma ordem de start (OS_) arranca um motor ou bomba; uma ordem de marcha (OM_) abre ou fecha uma válvula hidráulica. " +
                  "Quando o PLC emite uma ordem de arranque, espera receber o retorno de marcha correspondente nas entradas — " +
                  "se o retorno não chegar dentro do tempo previsto, regista um defeito de arranque." },
                { "2:Sinalizações de Comportas e Bombas",
                  "São as lâmpadas e indicações luminosas no painel sinótico e no quadro elétrico que mostram o estado atual das comportas e bombas. " +
                  "Indicam visualmente se uma comporta está aberta, fechada ou em movimento, e se uma bomba está ligada. " +
                  "Estas saídas não comandam equipamentos — apenas sinalizam o estado para os operadores." },
                { "3:Sinalizações de Modo de Operação",
                  "Indicam o modo de funcionamento atual do sistema: automático (o PLC controla sozinho), manual (o operador controla) ou desligado. " +
                  "Também indicam quando existe um defeito na unidade hidráulica. " +
                  "Se o modo de operação estiver errado, os comandos podem não funcionar como esperado." },
                { "4:Sinalizações de Alimentação e Defeitos Gerais",
                  "Indicam o estado das alimentações elétricas e defeitos gerais do quadro. " +
                  "Incluem lâmpadas de presença de 400VAC e 220VDC, falta de tensão, e estado geral do sistema. " +
                  "Se estas lâmpadas indicarem defeito, verificar as fontes de alimentação antes de qualquer outra ação." },
                { "5:Interface para PLC Comando",
                  "Não são saídas para equipamentos físicos — são impulsos de comunicação enviados ao PLC_Comando. " +
                  "O PLC_Comando coordena toda a eclusa e precisa de saber o estado do enchimento para gerir as autorizações. " +
                  "Se existir falha de comunicação entre PLCs, estas saídas podem estar envolvidas." },
                { "6:Bypass de Segurança",
                  "Saídas que ativam condições de bypass — contornam uma condição de segurança para permitir operação em modo de manutenção. " +
                  "Usar com precaução: quando ativo, o sistema pode operar sem todas as proteções habituais." },
                { "7:Semáforos Náuticos",
                  "Sinalizações luminosas visíveis pelas embarcações que aguardam entrada ou saída da eclusa. " +
                  "Controlam o tráfego fluvial durante as operações de enchimento e esvaziamento." },
                { "8:Saídas Reservadas",
                  "Saídas físicas do módulo de I/O que não estão ligadas a nenhum equipamento atualmente. Reservadas para expansão futura." },
                { "9:Outros",
                  "Saídas digitais com função específica não classificada nos grupos anteriores." },
            };

            var groups = doTags.GroupBy(x => GetDoGroup(x.Tag.Name))
                               .OrderBy(g => g.Key).ToList();

            sb.AppendLine("## Saídas Digitais (%Q — Bool)");
            sb.AppendLine();
            sb.AppendLine($"Sinais físicos de saída do PLC — {doTags.Count} sinais no total, organizados por função.");
            sb.AppendLine("As saídas digitais comandam os equipamentos: ligam motores, abrem válvulas, acendem lâmpadas de sinalização.");
            sb.AppendLine();

            foreach (var g in groups)
            {
                var label = g.Key.Substring(2);
                sb.AppendLine($"### {label}");
                sb.AppendLine();
                if (groupDesc.TryGetValue(g.Key, out var desc))
                { sb.AppendLine(desc); sb.AppendLine(); }
                sb.AppendLine("| Variável | Tipo | Endereço | Tabela | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|--------|------------|---------------------|");
                foreach (var (table, tag) in g.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {Esc(table)} | {cmt} | {InferTagMeaning(tag.Name)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>
        /// Gera a secção "Memórias — Bits" subdividida por grupo semântico,
        /// evitando chunks demasiado grandes no RAG.
        /// </summary>
        private static void AppendMemoryBitsSubdivided(StringBuilder sb,
            List<(string Table, TagInfo Tag)> mbTags)
        {
            if (mbTags.Count == 0) return;

            // Classificar cada merker no seu grupo semântico
            static string GetGroup(string name)
            {
                var up = (name ?? "").ToUpperInvariant();
                if (up.StartsWith("DEF_"))    return "1:Defeitos";
                if (up.StartsWith("AL_"))     return "2:Alarmes";
                if (up.StartsWith("REG_"))    return "3:Registos de Estado";
                if (up.StartsWith("IMP_") || up.StartsWith("INTERF_"))
                                              return "4:Impulsos e Interfaces";
                if (up.StartsWith("INTERD_") || up.StartsWith("AUTOR_") || up.StartsWith("COND_"))
                                              return "5:Interdições e Autorizações";
                if (up.StartsWith("ENT_") || up.StartsWith("SAID_"))
                                              return "6:Permissões de Navegação";
                if (up.StartsWith("FC_ENCOD_"))
                                              return "7:Fins de Curso Encoder";
                if (up.StartsWith("M_"))      return "8:Memórias de Controlo";
                return "9:Outros Merkers";
            }

            var groupDesc = new Dictionary<string, string>
            {
                { "1:Defeitos",
                  "Os bits de defeito ficam a verdadeiro quando um equipamento falha ou está em estado de erro. " +
                  "Um defeito ativo bloqueia a operação do equipamento afetado e normalmente acende uma lâmpada de alarme no painel. " +
                  "Para repor um defeito é necessário corrigir a causa (por exemplo repor a proteção elétrica) e depois fazer reset ao sistema. " +
                  "Se existirem vários defeitos ativos ao mesmo tempo, resolver primeiro os defeitos de alimentação elétrica e emergência." },
                { "2:Alarmes",
                  "Os alarmes indicam condições anormais do processo — por exemplo velocidade fora dos limites, pressão baixa ou tempo excedido. " +
                  "Um alarme não bloqueia necessariamente a operação mas indica que algo não está dentro dos parâmetros normais. " +
                  "Alarmes de velocidade (VELOC) indicam que o pistão está a mover-se demasiado rápido ou demasiado lento em relação ao esperado." },
                { "3:Registos de Estado",
                  "Os registos guardam o estado atual do ciclo de operação: se o sistema está em automático ou manual, " +
                  "que fase do enchimento está ativa, se a comporta A ou B está selecionada, se os pistões estão na posição correta. " +
                  "Quando algo falha durante o enchimento, consultar os registos de estado para perceber em que fase o processo estava." },
                { "4:Impulsos e Interfaces",
                  "São sinais de comunicação entre este PLC e os outros PLCs da eclusa. " +
                  "Um impulso é ativado momentaneamente para informar outro PLC de um evento — por exemplo que o enchimento terminou. " +
                  "Não representam equipamentos físicos. Se existir falha de coordenação entre PLCs, verificar estes bits." },
                { "5:Interdições e Autorizações",
                  "Controlam as permissões de manobra: uma interdição bloqueia uma operação por razões de segurança; " +
                  "uma autorização permite que uma operação avance. " +
                  "Se o sistema não executa uma manobra esperada, verificar se existe alguma interdição ativa ou autorização em falta." },
                { "6:Permissões de Navegação",
                  "Controlam a autorização de entrada e saída de embarcações na eclusa. " +
                  "Estes bits comunicam com o PLC_Comando para coordenar o tráfego fluvial durante o enchimento." },
                { "7:Fins de Curso Encoder",
                  "Posições calculadas por encoder — o PLC calcula a posição do pistão a partir do número de pulsos do encoder, " +
                  "em vez de depender apenas dos sensores mecânicos. Permite posicionamento mais preciso e deteção de desvios." },
                { "8:Memórias de Controlo",
                  "Bits internos que guardam estados intermédios do controlo dos atuadores: " +
                  "se uma ordem de subida ou descida está ativa, se está em pausa, se existe diferença de posição entre os dois lados. " +
                  "São usados internamente pelo programa e não correspondem a sinais físicos." },
                { "9:Outros Merkers",
                  "Bits de memória interna com funções diversas: clocks de temporização, resets, flags de operação e sinalizações internas." },
            };

            var groups = mbTags
                .GroupBy(x => GetGroup(x.Tag.Name))
                .OrderBy(g => g.Key)
                .ToList();

            sb.AppendLine("## Memórias — Bits (%M — Bool)");
            sb.AppendLine();
            sb.AppendLine($"Bits de memória interna do PLC — {mbTags.Count} no total, organizados por função.");
            sb.AppendLine("As memórias não correspondem a sinais físicos — são variáveis internas que o programa usa para guardar estados, defeitos, alarmes e registos de operação.");
            sb.AppendLine();

            foreach (var g in groups)
            {
                var label = g.Key.Substring(2); // remover prefixo de ordenação "1:"
                sb.AppendLine($"### {label}");
                sb.AppendLine();
                if (groupDesc.TryGetValue(g.Key, out var desc))
                {
                    sb.AppendLine(desc);
                    sb.AppendLine();
                }
                sb.AppendLine("| Variável | Tipo | Endereço | Tabela | Comentário | Significado Inferido |");
                sb.AppendLine("|----------|------|----------|--------|------------|---------------------|");
                foreach (var (table, tag) in g.OrderBy(x => x.Tag.Address))
                {
                    var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                    var inf = InferTagMeaning(tag.Name);
                    sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {Esc(table)} | {cmt} | {inf} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        private static void AppendTagSection(StringBuilder sb, string title, string description,
            List<(string Table, TagInfo Tag)> tags)
        {
            if (tags.Count == 0) return;
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine($"> {description}");
            sb.AppendLine();
            sb.AppendLine("| Variável | Tipo | Endereço | Tabela | Comentário | Significado Inferido |");
            sb.AppendLine("|----------|------|----------|--------|------------|---------------------|");
            foreach (var (table, tag) in tags.OrderBy(x => x.Tag.Address))
            {
                var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "—" : Esc(tag.Comment.Trim());
                var inf = InferTagMeaning(tag.Name);
                sb.AppendLine($"| `{Esc(tag.Name)}` | {tag.DataType} | {HumanAddress(tag.Address)} | {Esc(table)} | {cmt} | {inf} |");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>Tag anónima sem utilidade para o RAG — filtrar.</summary>
        private static bool IsAnonymousTag(TagInfo tag)
        {
            var name = (tag.Name ?? "").Trim();
            // Tag_1 ... Tag_N
            if (Regex.IsMatch(name, @"^Tag_\d+$")) return true;
            // Tag_M209.0, Tag_M212.3 etc.
            if (Regex.IsMatch(name, @"^Tag_[A-Z]+\d+\.\d+$")) return true;
            // Tag_80 com tipo Hw_Device
            if (Regex.IsMatch(name, @"^Tag_\d+$") && tag.DataType == "Hw_Device") return true;
            return false;
        }

        /// <summary>Tags geradas automaticamente pelo sistema S7.</summary>
        private static bool IsSystemTag(TagInfo tag)
        {
            var name = (tag.Name ?? "").Trim();
            return name == "FirstScan"       || name == "DiagStatusUpdate" ||
                   name == "AlwaysTRUE"      || name == "AlwaysFALSE"      ||
                   name == "System_Byte"     || name == "Clock_Byte"        ||
                   name.StartsWith("Clock_");
        }

        /// <summary>
        /// Converte endereço Siemens (%I3.2, %Q4.0, %M10.0, etc.) para formato legível
        /// sem o símbolo %, de modo a que o embedding semântico funcione correctamente.
        /// Exemplo: %I3.2 → "Entrada I3.2" | %Q4.0 → "Saída Q4.0" | %M10.0 → "Memória M10.0"
        /// </summary>
        private static string HumanAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return address ?? "";
            var a = address.TrimStart('%');
            var up = a.ToUpperInvariant();
            if      (up.StartsWith("IW") || up.StartsWith("ID") || up.StartsWith("IB")) return $"Entrada Word {a}";
            if      (up.StartsWith("QW") || up.StartsWith("QD") || up.StartsWith("QB")) return $"Saída Word {a}";
            if      (up.StartsWith("MW") || up.StartsWith("MD") || up.StartsWith("MB")) return $"Memória Word {a}";
            if      (up.StartsWith("I"))  return $"Entrada {a}";
            if      (up.StartsWith("Q"))  return $"Saída {a}";
            if      (up.StartsWith("M"))  return $"Memória {a}";
            return a;
        }

        /// <summary>
        /// Decompõe o nome da tag em tokens e constrói uma descrição em português.
        /// Exemplo: RM_BOMB_A → "Retorno Marcha — Bomba A"
        /// </summary>
        private static string InferTagMeaning(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName)) return "—";

            var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Functional prefixes
                { "DEF",      "Defeito" },        { "AL",      "Alarme" },
                { "SIN",      "Sinal" },           { "MC",      "Mapa Controlo" },
                { "BS",       "Barr./Sin." },      { "OM",      "Ordem Marcha" },
                { "OA",       "Ordem Abertura" },  { "OD",      "Ordem Direta" },
                { "OS",       "Ordem Start" },     { "RM",      "Retorno Marcha" },
                { "FC",       "Fim de Curso" },    { "ENCOD",   "Encoder" },
                { "BT",       "Botão" },           { "PROT",    "Proteção" },
                { "PRES",     "Presença Tens." },  { "DISP",    "Disponível" },
                { "COND",     "Condição" },        { "INP",     "Input Analóg." },
                { "REG",      "Registo" },         { "EV",      "Evento" },
                { "IMP",      "Impulso" },         { "ENT",     "Entrada Nav." },
                { "SAID",     "Saída Nav." },      { "INTERD",  "Interdição" },
                { "AUTOR",    "Autorização" },     { "SEMAF",   "Semáforo" },
                { "BYPASS",   "Bypass" },          { "BY",      "Bypass" },
                { "HMI",      "Cmd HMI" },         { "VARIAD",  "Variador" },
                { "SP",       "SetPoint" },        { "ESTAD",   "Estado" },
                { "VELOC",    "Velocidade" },      { "INTENS",  "Intensidade" },
                { "TRAV",     "Travão" },          { "FREIO",   "Freio" },
                { "ENCARQ",   "Sobrec.Corr." },    { "CORR",    "Corrente" },
                { "ESF",      "Esforço" },         { "FALT",    "Falta" },
                { "PRESS",    "Pressão" },         { "LIG",     "Ligar" },
                { "INTERF",   "Interface→Cmd" },   { "K",       "Relé K" },
                { "RESERV",   "Reserva" },         { "RESERVA", "Reserva" },
                { "ALARM",    "Alarmes" },         { "EVENTOS", "Eventos" },
                { "VAR",      "Variador" },        { "PASS",    "Passagem" },
                { "PASS2",    "Passagem2" },
                // Equipment tokens
                { "BOMB",     "Bomba" },           { "MOTOR",   "Motor" },
                { "VALV",     "Válvula" },         { "COMP",    "Comporta" },
                { "PORTA",    "Porta" },           { "ECLUSA",  "Eclusa" },
                { "CAMARA",   "Câmara" },          { "NIVEL",   "Nível" },
                { "DIST",     "Distribuição" },    { "HIDRO",   "Hidráulico" },
                { "CIRC",     "Circuito" },        { "CENT",    "Central" },
                { "UPS",      "UPS" },             { "TRAFO",   "Transformador" },
                { "REDE",     "Rede" },            { "EMERG",   "Emergência" },
                { "PILOT",    "Piloto" },          { "TUNEL",   "Túnel" },
                // Position / state suffixes
                { "MONT",     "Montante" },        { "JUS",     "Jusante" },
                { "DIR",      "Direita" },         { "ESQ",     "Esquerda" },
                { "SUB",      "Subida" },          { "DESC",    "Descida" },
                { "AB",       "Aberta" },          { "AUT",     "Auto" },
                { "MAN",      "Manual" },          { "LOC",     "Local" },
                { "REM",      "Remoto" },          { "ALT",     "Alta" },
                { "LENT",     "Lenta" },           { "RAP",     "Rápida" },
                { "MAX",      "Máx." },            { "MIN",     "Mín." },
                { "A",        "A" },               { "B",       "B" },
                { "C",        "C" },               { "V",       "V" },
            };

            var parts = tagName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "—";

            string Translate(string p) =>
                tokenMap.TryGetValue(p, out var v) ? v : p;

            var first = Translate(parts[0]);
            if (parts.Length == 1) return first;

            var rest = string.Join(" ", parts.Skip(1).Select(Translate));
            return $"{first} — {rest}";
        }

        /// <summary>Categoriza a tag pelo prefixo do endereço.</summary>
        private static string GetTagCategory(string address)
        {
            var a = (address ?? "").Trim();
            if (a.StartsWith("%IW") || a.StartsWith("%ID") || a.StartsWith("%IB")) return "ai";
            if (a.StartsWith("%QW") || a.StartsWith("%QD") || a.StartsWith("%QB")) return "ao";
            if (a.StartsWith("%I"))  return "di";
            if (a.StartsWith("%Q"))  return "do";
            if (a.StartsWith("%MW") || a.StartsWith("%MD") || a.StartsWith("%MB")) return "mw";
            if (a.StartsWith("%M"))  return "mb";
            return "other";
        }

        private void SaveMd()
        {
            if (_mdResult == null) return;
            var installName = AskInstallationName(Path.GetFileNameWithoutExtension(_txtPath.Text));
            if (installName == null) return;
            var md = BuildMarkdown(_blocks, _tagTables, _udts, _hwDevices, _txtPath.Text, installName);
            using var dlg = new SaveFileDialog
            {
                Title    = "Exportar Markdown para IA",
                Filter   = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
                FileName = "DaniloTracker_IA_" + Path.GetFileNameWithoutExtension(_txtPath.Text)
                           + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, md, Encoding.UTF8);
                var lines = md.Count(c => c == '\n');
                SetStatus($"Markdown exportado: {Path.GetFileName(dlg.FileName)}  ({lines:N0} linhas)", C_OK);
            }
        }

        // ── Diálogo: Nome da Instalação ────────────────────────────────────────
        private static string AskInstallationName(string defaultName)
        {
            using var form = new Form
            {
                Text            = "Identificar Instalação",
                Size            = new Size(440, 170),
                StartPosition   = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox     = false,
                MinimizeBox     = false,
                BackColor       = Color.FromArgb(22, 27, 34),
                ForeColor       = Color.FromArgb(201, 209, 217),
                Font            = new Font("Segoe UI", 9f)
            };

            var lbl = new Label
            {
                Text      = "Nome da instalação (ex: Eclusa de Crestuma):",
                AutoSize  = true,
                Location  = new Point(14, 18),
                ForeColor = Color.FromArgb(201, 209, 217)
            };
            var txt = new TextBox
            {
                Text        = defaultName ?? "",
                Location    = new Point(14, 42),
                Width       = 400,
                BackColor   = Color.FromArgb(33, 38, 50),
                ForeColor   = Color.FromArgb(201, 209, 217),
                BorderStyle = BorderStyle.FixedSingle
            };
            var btnOk = new Button
            {
                Text          = "OK",
                DialogResult  = DialogResult.OK,
                Location      = new Point(224, 86),
                Width         = 88,
                Height        = 28,
                BackColor     = Color.FromArgb(31, 111, 235),
                ForeColor     = Color.White,
                FlatStyle     = FlatStyle.Flat
            };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button
            {
                Text          = "Cancelar",
                DialogResult  = DialogResult.Cancel,
                Location      = new Point(320, 86),
                Width         = 94,
                Height        = 28,
                BackColor     = Color.FromArgb(33, 38, 50),
                ForeColor     = Color.FromArgb(201, 209, 217),
                FlatStyle     = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });

            return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
        }

        private void SaveXml()
        {
            if (_xmlResult == null) return;
            using var dlg = new SaveFileDialog
            {
                Title    = "Exportar relatório XML",
                Filter   = "XML (*.xml)|*.xml",
                FileName = "DaniloTracker_" + Path.GetFileNameWithoutExtension(_txtPath.Text)
                           + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, _xmlResult, Encoding.UTF8);
                SetStatus($"Guardado: {Path.GetFileName(dlg.FileName)}", C_OK);
            }
        }

        // ── TCP Server ────────────────────────────────────────────────────────
        private TcpServerDialog _tcpDialog;
        private void OpenTcpServer()
        {
            if (_tcpDialog == null || _tcpDialog.IsDisposed)
                _tcpDialog = new TcpServerDialog();
            _tcpDialog.Show();
            _tcpDialog.BringToFront();
        }

        // ── Thread-safe helpers ───────────────────────────────────────────────
        public void DetailLine(string text, Color color, bool bold = false, float? size = null)
        {
            if (InvokeRequired) { Invoke((Action)(() => DetailLine(text, color, bold, size))); return; }
            _rtbDetail.SuspendLayout();
            _rtbDetail.SelectionStart  = _rtbDetail.TextLength;
            _rtbDetail.SelectionLength = 0;
            _rtbDetail.SelectionColor  = color;
            var f = _rtbDetail.Font;
            _rtbDetail.SelectionFont = (bold || size.HasValue)
                ? new Font(f.FontFamily, size ?? f.Size, bold ? FontStyle.Bold : FontStyle.Regular)
                : f;
            _rtbDetail.AppendText(text + "\n");
            _rtbDetail.SelectionColor = _rtbDetail.ForeColor;
            _rtbDetail.SelectionFont  = f;
            _rtbDetail.ResumeLayout();
        }

        public void LogLine(string text, Color? color = null, bool bold = false)
        {
            if (InvokeRequired) { Invoke((Action)(() => LogLine(text, color, bold))); return; }
            _rtbLog.SuspendLayout();
            _rtbLog.SelectionStart  = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor  = color ?? C_GROUP;
            _rtbLog.SelectionFont   = bold ? new Font(_rtbLog.Font, FontStyle.Bold) : _rtbLog.Font;
            _rtbLog.AppendText(text + "\n");
            _rtbLog.SelectionColor  = _rtbLog.ForeColor;
            _rtbLog.SelectionFont   = _rtbLog.Font;
            _rtbLog.ScrollToCaret();
            _rtbLog.ResumeLayout();
        }

        private void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStatus(text, color))); return; }
            _lblStatus.Text      = text;
            _lblStatus.ForeColor = color;
        }

        private void SetStats(string text)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStats(text))); return; }
            _lblStats.Text = text;
        }

        private void SetProgress(int pct)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetProgress(pct))); return; }
            _progress.Value = Math.Max(0, Math.Min(100, pct));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _conn?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ── Console → Log panel ───────────────────────────────────────────────────
    internal class RtbWriter : TextWriter
    {
        private readonly MainForm _form;
        public RtbWriter(MainForm form) { _form = form; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => Write(value);
        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var col = Color.FromArgb(125, 133, 144);
            if (value.Contains("ERRO") || value.Contains("ERROR") || value.Contains("SKIP"))
                col = Color.FromArgb(248, 81, 73);
            else if (value.Contains("OK") || value.Contains("Projecto :") || value.Contains("aberto"))
                col = Color.FromArgb(86, 211, 100);
            else if (value.Contains("[AutoAccept]"))
                col = Color.FromArgb(88, 166, 255);
            _form.LogLine(value, col);
        }
    }

    // ── Dark theme renderer para ContextMenuStrip ─────────────────────────────
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color BG_MENU  = Color.FromArgb(22, 27, 34);
        static readonly Color FG_MENU  = Color.FromArgb(230, 237, 243);
        static readonly Color SEL_MENU = Color.FromArgb(31, 45, 63);
        static readonly Color BORDER   = Color.FromArgb(48, 54, 61);

        public DarkMenuRenderer() : base(new DarkMenuColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var c = e.Item.Selected ? SEL_MENU : BG_MENU;
            e.Graphics.FillRectangle(new SolidBrush(c), e.Item.ContentRectangle);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            => e.Graphics.FillRectangle(new SolidBrush(BG_MENU), e.AffectedBounds);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            => e.Graphics.DrawRectangle(new Pen(BORDER), 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);

        private class DarkMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected         => Color.FromArgb(31, 45, 63);
            public override Color MenuItemBorder           => Color.FromArgb(48, 54, 61);
            public override Color MenuBorder               => Color.FromArgb(48, 54, 61);
            public override Color ToolStripDropDownBackground => Color.FromArgb(22, 27, 34);
            public override Color ImageMarginGradientBegin => Color.FromArgb(22, 27, 34);
            public override Color ImageMarginGradientMiddle=> Color.FromArgb(22, 27, 34);
            public override Color ImageMarginGradientEnd   => Color.FromArgb(22, 27, 34);
        }
    }
}
