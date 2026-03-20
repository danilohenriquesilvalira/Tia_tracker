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
using System.Xml.Linq;
using TiaTracker.Core;

namespace TiaTracker.UI
{
    public class MainForm : Form
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private TextBox              _txtPath;
        private TextBox              _txtSearch;
        private Button               _btnBrowse, _btnRun, _btnSaveXml, _btnSaveMd;
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
        private Dictionary<string, List<ProjectReader.CallEdge>> _callGraph = new Dictionary<string, List<ProjectReader.CallEdge>>();

        // Tag especial para o nó de grafo na TreeView
        private class CallGraphTag { public string Device; }

        // ── Win32: impede que o TIA Portal desforme a nossa janela ────────────
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

        // ── Theme — VS Code Dark+ ──────────────────────────────────────────────
        static readonly Color BG      = Color.FromArgb( 30,  30,  30);   // #1E1E1E
        static readonly Color PANEL   = Color.FromArgb( 37,  37,  38);   // #252526
        static readonly Color TREE_BG = Color.FromArgb( 37,  37,  38);   // #252526
        static readonly Color LOG_BG  = Color.FromArgb( 30,  30,  30);   // #1E1E1E
        static readonly Color C_OB    = Color.FromArgb( 86, 156, 214);   // #569CD6 keyword blue
        static readonly Color C_FB    = Color.FromArgb( 78, 201, 176);   // #4EC9B0 type teal
        static readonly Color C_FC    = Color.FromArgb(220, 220, 170);   // #DCDCAA function yellow
        static readonly Color C_DB    = Color.FromArgb(206, 145, 120);   // #CE9178 string orange
        static readonly Color C_TAGS  = Color.FromArgb(156, 220, 254);   // #9CDCFE variable blue
        static readonly Color C_UDT   = Color.FromArgb(197, 134, 192);   // #C586C0 purple
        static readonly Color C_NET   = Color.FromArgb(106, 153,  85);   // #6A9955 comment green
        static readonly Color C_IFACE = Color.FromArgb(128, 128, 128);   // #808080 grey
        static readonly Color C_TEXT  = Color.FromArgb(212, 212, 212);   // #D4D4D4 default text
        static readonly Color C_OK    = Color.FromArgb(106, 153,  85);   // #6A9955 green
        static readonly Color C_ERR   = Color.FromArgb(244,  71,  71);   // #F44747 red
        static readonly Color C_GOLD  = Color.FromArgb(215, 186, 125);   // #D7BA7D device nodes
        static readonly Color C_GROUP = Color.FromArgb(128, 128, 128);   // #808080 grey
        static readonly Color C_BLUE  = Color.FromArgb(  0, 120, 212);   // #0078D4 accent blue
        static readonly Color C_SEL   = Color.FromArgb( 38,  79, 120);   // #264F78 selection

        // ── Constructor ───────────────────────────────────────────────────────
        public MainForm() { BuildUI(); }

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
            var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(37, 37, 38) };
            header.Paint += (s, e) =>
            {
                var rc = header.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;
                using var pen = new Pen(Color.FromArgb(68, 68, 68), 1);
                e.Graphics.DrawLine(pen, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
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
                using var br = new SolidBrush(C_BLUE);
                g.FillEllipse(br, circleRect);
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
                ForeColor = Color.FromArgb(212, 212, 212),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            var lblSub = new Label
            {
                Text      = "TIA Portal V18  ·  Leitura offline de projeto PLC  ·  Exportação XML & Markdown",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(128, 128, 128),
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
                BackColor = Color.FromArgb(37, 37, 38),
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
                ForeColor = Color.FromArgb(128, 128, 128),
                TextAlign = ContentAlignment.MiddleRight,
                Font      = new Font("Segoe UI", 8.5f)
            };

            _txtPath = new TextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(60, 60, 60),
                ForeColor   = Color.FromArgb(212, 212, 212),
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Consolas", 8.8f),
                Text        = @"C:\Users\Admin\Desktop\C#_PLC\C#_PLC.ap18",
                Margin      = new Padding(6, 0, 6, 0)
            };

            _btnBrowse  = MkBtn("···",              38, Color.FromArgb(60, 60, 60));
            _btnRun     = MkBtn("▶  Conectar e Ler", 174, C_BLUE);
            _btnSaveXml = MkBtn("Salvar XML",        138, Color.FromArgb(60, 60, 60));
            _btnSaveMd  = MkBtn("Exportar para IA",  158, Color.FromArgb(60, 60, 60));
            var btnEditor = MkBtn("✏  Criar Bloco",  148, Color.FromArgb(80, 50, 120));

            _btnRun.Font        = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnRun.Margin      = new Padding(4, 0, 4, 0);
            _btnSaveXml.Enabled = false;
            _btnSaveMd.Enabled  = false;
            _btnSaveMd.FlatAppearance.BorderColor = Color.FromArgb(68, 68, 68);

            _btnBrowse.Click  += (s, e) => BrowseProject();
            _btnRun.Click     += async (s, e) => await RunAsync();
            _btnSaveXml.Click += (s, e) => SaveXml();
            _btnSaveMd.Click  += (s, e) => SaveMd();
            btnEditor.Click   += (s, e) => new BlockEditorForm(_conn).Show();

            // hover nos botões
            AddHover(_btnBrowse,  Color.FromArgb(80, 80, 80),   Color.FromArgb(60, 60, 60));
            AddHover(_btnSaveXml, Color.FromArgb(80, 80, 80),   Color.FromArgb(60, 60, 60));
            AddHover(_btnSaveMd,  Color.FromArgb(80, 80, 80),   Color.FromArgb(60, 60, 60));
            AddHover(_btnRun,     Color.FromArgb(14, 99, 156),   C_BLUE);
            AddHover(btnEditor,   Color.FromArgb(100, 60, 150),  Color.FromArgb(80, 50, 120));

            tbl.Controls.Add(lblPath,     0, 0);
            tbl.Controls.Add(_txtPath,    1, 0);
            tbl.Controls.Add(_btnBrowse,  2, 0);
            tbl.Controls.Add(_btnRun,     3, 0);
            tbl.Controls.Add(_btnSaveXml, 4, 0);
            tbl.Controls.Add(_btnSaveMd,  5, 0);
            tbl.Controls.Add(btnEditor,   6, 0);
            toolbar.Controls.Add(tbl);

            _progress = new ProgressBar
            {
                Dock      = DockStyle.Top,
                Height    = 3,
                Style     = ProgressBarStyle.Continuous,
                Minimum   = 0,
                Maximum   = 100,
                Value     = 0,
                BackColor = Color.FromArgb(37, 37, 38)
            };

            // ── StatusStrip ───────────────────────────────────────────────────
            var strip = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), SizingGrip = false };
            _lblStatus = new ToolStripStatusLabel
            {
                Text      = "  Pronto. Selecione um projeto e clique em  ▶ Conectar e Ler.",
                ForeColor = Color.White,
                Spring    = true
            };
            _lblStats = new ToolStripStatusLabel { Text = "", ForeColor = Color.FromArgb(200, 235, 255), Spring = false };
            strip.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblStats });

            // ── Outer split ───────────────────────────────────────────────────
            var outerSplit = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = Orientation.Vertical,
                SplitterWidth = 1,
                BackColor     = Color.FromArgb(68, 68, 68),
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
                using var b = new SolidBrush(Color.FromArgb(128, 128, 128));
                e.Graphics.DrawString("  EXPLORADOR", f, b, 2, 8);
            };

            _txtSearch = new TextBox
            {
                Dock        = DockStyle.Top,
                Height      = 26,
                BackColor   = Color.FromArgb(60, 60, 60),
                ForeColor   = Color.FromArgb(128, 128, 128),
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 9f),
                Text        = "  Filtrar blocos...",
                Padding     = new Padding(4, 0, 0, 0)
            };
            _txtSearch.GotFocus += (s, e) =>
            {
                if (_txtSearch.Text.Contains("Filtrar")) { _txtSearch.Text = ""; _txtSearch.ForeColor = Color.FromArgb(212, 212, 212); }
            };
            _txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSearch.Text)) { _txtSearch.Text = "  Filtrar blocos..."; _txtSearch.ForeColor = Color.FromArgb(128, 128, 128); }
            };
            _txtSearch.TextChanged += (s, e) => FilterTree(_txtSearch.Text);

            var searchBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(68, 68, 68) };

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

            _tree.ContextMenuStrip = ctxMenu;
            ctxMenu.Opening += (s, e) =>
            {
                var node = _tree.SelectedNode;
                ctxExportXml.Enabled    = node?.Tag is BlockInfo b && !string.IsNullOrEmpty(b.RawXml);
                ctxExportDevMd.Enabled  = node?.Tag as string == "device";
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
                BackColor     = Color.FromArgb(68, 68, 68),
                FixedPanel    = FixedPanel.Panel2
            };

            // Detail header card
            _detailHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 48,
                BackColor = Color.FromArgb(37, 37, 38),
                Visible   = false
            };
            _detailHeader.Paint += (s, e) =>
            {
                var rc = _detailHeader.ClientRectangle;
                using var pen = new Pen(Color.FromArgb(68, 68, 68), 1);
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
                ForeColor = Color.FromArgb(128, 128, 128),
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
                BackColor = Color.FromArgb(37, 37, 38),
                ForeColor = Color.FromArgb(128, 128, 128),
                Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _rtbLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = LOG_BG,
                ForeColor   = Color.FromArgb(128, 128, 128),
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
                using var pen = new Pen(Color.FromArgb(128, 128, 128), 1);
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
            int textX = rc.X + 21;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (node.Tag is BlockInfo bi)
            {
                Color col = bi.Type == "OB" ? C_OB : bi.Type == "FB" ? C_FB : bi.Type == "FC" ? C_FC : C_DB;
                DrawIconDocument(g, iconX, rc.Y, rc.Height, col);
            }
            else if (node.Tag is TagTableInfo)
                DrawIconDocument(g, iconX, rc.Y, rc.Height, C_TAGS);
            else if (node.Tag is UdtInfo)
                DrawIconDocument(g, iconX, rc.Y, rc.Height, C_UDT);
            else if (node.Tag is string kind)
            {
                if (kind == "device")
                    DrawIconDevice(g, iconX, rc.Y, rc.Height);
                else if (kind == "folder")
                    DrawIconFolder(g, iconX, rc.Y, rc.Height, Color.FromArgb(220, 186, 90), node.IsExpanded);
                else if (kind.StartsWith("group:"))
                {
                    var t = kind.Substring(6);
                    Color col = t == "OB" ? C_OB : t == "FB" ? C_FB : t == "FC" ? C_FC :
                                t == "DB" ? C_DB : t == "tags" ? C_TAGS : C_UDT;
                    DrawIconGroup(g, iconX, rc.Y, rc.Height, col);
                }
                else textX = rc.X + 4;
            }
            else textX = rc.X + 4;

            g.SmoothingMode = SmoothingMode.Default;

            // texto
            var textColor = node.ForeColor == Color.Empty ? C_TEXT : node.ForeColor;
            var font      = node.NodeFont ?? _tree.Font;
            TextRenderer.DrawText(g, node.Text, font,
                new Rectangle(textX, rc.Y, rc.Right - textX, rc.Height),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        // ── Ícones do TreeView (GDI+) ─────────────────────────────────────────
        private static void DrawIconDocument(Graphics g, int x, int y, int h, Color color)
        {
            int midY = y + h / 2;
            int iw = 11, ih = 13, fold = 3;
            int ix = x, iy = midY - ih / 2;
            // fundo do documento
            using var bodyBr = new SolidBrush(Color.FromArgb(210, 210, 210));
            g.FillRectangle(bodyBr, ix, iy, iw, ih);
            // canto dobrado
            using var foldBr = new SolidBrush(Color.FromArgb(150, 150, 150));
            var foldPts = new Point[] {
                new Point(ix + iw - fold, iy),
                new Point(ix + iw, iy + fold),
                new Point(ix + iw - fold, iy + fold)
            };
            g.FillPolygon(foldBr, foldPts);
            // borda esquerda colorida
            using var edgeBr = new SolidBrush(color);
            g.FillRectangle(edgeBr, ix, iy, 3, ih);
        }

        private static void DrawIconFolder(Graphics g, int x, int y, int h, Color color, bool expanded)
        {
            int midY = y + h / 2;
            int iw = 14, ih = 10;
            int ix = x, iy = midY - ih / 2;
            Color dark = Color.FromArgb(
                (int)(color.R * 0.72f), (int)(color.G * 0.72f), (int)(color.B * 0.72f));
            Color body = expanded
                ? Color.FromArgb((int)(color.R * 0.9f), (int)(color.G * 0.9f), (int)(color.B * 0.9f))
                : color;
            // aba superior
            using var tabBr = new SolidBrush(dark);
            g.FillRectangle(tabBr, ix, iy, 6, 3);
            // corpo
            using var bodyBr = new SolidBrush(body);
            g.FillRectangle(bodyBr, ix, iy + 2, iw, ih - 2);
            // linha brilhante no topo quando aberto
            if (expanded)
            {
                using var hlBr = new SolidBrush(Color.FromArgb(
                    Math.Min(255, color.R + 60), Math.Min(255, color.G + 50), Math.Min(255, color.B + 30)));
                g.FillRectangle(hlBr, ix, iy + 2, iw, 2);
            }
        }

        private static void DrawIconDevice(Graphics g, int x, int y, int h)
        {
            int midY = y + h / 2;
            int iw = 14, ih = 11;
            int ix = x, iy = midY - ih / 2;
            using var bodyBr = new SolidBrush(Color.FromArgb(75, 75, 80));
            g.FillRectangle(bodyBr, ix, iy, iw, ih);
            using var borderPen = new Pen(Color.FromArgb(160, 160, 165), 1);
            g.DrawRectangle(borderPen, ix, iy, iw - 1, ih - 1);
            // slots verticais
            using var slotPen = new Pen(Color.FromArgb(180, 180, 185), 1);
            for (int i = 1; i <= 3; i++)
                g.DrawLine(slotPen, ix + i * 3, iy + 2, ix + i * 3, iy + ih - 3);
        }

        private static void DrawIconGroup(Graphics g, int x, int y, int h, Color color)
        {
            int midY = y + h / 2;
            int ix = x + 1;
            // colchete { com linha horizontal
            using var pen = new Pen(color, 1.5f);
            g.DrawLine(pen, ix + 3, midY - 4, ix + 1, midY - 2);
            g.DrawLine(pen, ix + 1, midY - 2, ix,     midY);
            g.DrawLine(pen, ix,     midY,     ix + 1, midY + 2);
            g.DrawLine(pen, ix + 1, midY + 2, ix + 3, midY + 4);
            g.DrawLine(pen, ix + 4, midY,     ix + 12, midY);
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
                    g.Clear(Color.FromArgb(22, 22, 28));
                    // Fundo arredondado azul
                    using (var b = new SolidBrush(Color.FromArgb(0, 112, 200)))
                        g.FillEllipse(b, 2, 2, 28, 28);
                    // Letra "D" centrada
                    using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
                        TextRenderer.DrawText(g, "D", f, new Rectangle(2, 2, 28, 28), Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        private Button MkBtn(string text, int width, Color? bg = null)
        {
            var b = new Button
            {
                Text        = text,
                Dock        = DockStyle.Fill,
                MinimumSize = new Size(width, 26),
                BackColor   = bg ?? Color.FromArgb(60, 60, 60),
                ForeColor   = Color.FromArgb(212, 212, 212),
                FlatStyle   = FlatStyle.Flat,
                Cursor      = Cursors.Hand,
                Margin      = Padding.Empty
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(68, 68, 68);
            return b;
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
                    SetProgress(80);

                    SetStatus("A construir árvore...", Color.Silver);
                    Invoke((Action)BuildTree);
                    SetProgress(88);

                    _xmlResult = BuildXml(_blocks, _tagTables, _udts, path);
                    SetProgress(94);
                    _mdResult  = BuildMarkdown(_blocks, _tagTables, _udts, path);
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
                    ForeColor = Color.FromArgb(212, 212, 212),
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
                ForeColor = Color.FromArgb(220, 186, 90),
                NodeFont  = new Font("Segoe UI", 8.5f),
                Tag       = "folder"
            };

        private TreeNode MakeGroupNode(string text, string type = "DB") =>
            new TreeNode(text)
            {
                ForeColor = Color.FromArgb(180, 180, 180),
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
                }
            }
            else
                DetailLine("\n  (sem redes — bloco vazio ou DB)", C_GROUP);
        }

        // ── Render: Call Graph ───────────────────────────────────────────────
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
        private string BuildMarkdown(List<BlockInfo> blocks, List<TagTableInfo> tagTables, List<UdtInfo> udts, string path)
        {
            var sb = new StringBuilder();
            var projName = Path.GetFileNameWithoutExtension(path);

            int obC = blocks.Count(b => b.Type == "OB");
            int fbC = blocks.Count(b => b.Type == "FB");
            int fcC = blocks.Count(b => b.Type == "FC");
            int dbC = blocks.Count(b => b.Type == "DB" || b.Type == "iDB");

            // ── Cabeçalho ─────────────────────────────────────────────────────
            sb.AppendLine($"# Projeto PLC: {projName}");
            sb.AppendLine();
            sb.AppendLine($"> **Exportado em:** {DateTime.Now:yyyy-MM-dd HH:mm}  ");
            sb.AppendLine($"> **Ficheiro:** `{path}`  ");
            sb.AppendLine($"> **Resumo:** {obC} OBs · {fbC} FBs · {fcC} FCs · {dbC} DBs · {tagTables.Count} Tag Tables · {udts.Count} UDTs");
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
                                var cmt = string.IsNullOrWhiteSpace(tag.Comment) ? "" : tag.Comment.Trim();
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
                    var title = string.IsNullOrWhiteSpace(net.Title) ? $"Network {net.Index}" : $"Network {net.Index}: {net.Title}";
                    sb.AppendLine($"**{title}**");
                    sb.AppendLine();
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

        private void ExportDeviceMd(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;
            var devBlocks    = _blocks.Where(b => b.Device == deviceName).ToList();
            var devTagTables = _tagTables.Where(t => t.Device == deviceName).ToList();
            var devUdts      = _udts.Where(u => u.Device == deviceName).ToList();
            var md = BuildMarkdown(devBlocks, devTagTables, devUdts, _txtPath.Text);
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

        private void SaveMd()
        {
            if (_mdResult == null) return;
            using var dlg = new SaveFileDialog
            {
                Title    = "Exportar Markdown para IA",
                Filter   = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
                FileName = "DaniloTracker_IA_" + Path.GetFileNameWithoutExtension(_txtPath.Text)
                           + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, _mdResult, Encoding.UTF8);
                SetStatus($"Markdown exportado: {Path.GetFileName(dlg.FileName)}", C_OK);
            }
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
            var col = Color.FromArgb(130, 130, 148);
            if (value.Contains("ERRO") || value.Contains("ERROR") || value.Contains("SKIP"))
                col = Color.FromArgb(210, 80, 80);
            else if (value.Contains("OK") || value.Contains("Projecto :") || value.Contains("aberto"))
                col = Color.FromArgb(90, 190, 90);
            else if (value.Contains("[AutoAccept]"))
                col = Color.FromArgb(80, 170, 220);
            _form.LogLine(value, col);
        }
    }

    // ── Dark theme renderer para ContextMenuStrip ─────────────────────────────
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color BG_MENU  = Color.FromArgb(37, 37, 38);
        static readonly Color FG_MENU  = Color.FromArgb(212, 212, 212);
        static readonly Color SEL_MENU = Color.FromArgb(38, 79, 120);
        static readonly Color BORDER   = Color.FromArgb(68, 68, 68);

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
            public override Color MenuItemSelected         => Color.FromArgb(38, 79, 120);
            public override Color MenuItemBorder           => Color.FromArgb(68, 68, 68);
            public override Color MenuBorder               => Color.FromArgb(68, 68, 68);
            public override Color ToolStripDropDownBackground => Color.FromArgb(37, 37, 38);
            public override Color ImageMarginGradientBegin => Color.FromArgb(37, 37, 38);
            public override Color ImageMarginGradientMiddle=> Color.FromArgb(37, 37, 38);
            public override Color ImageMarginGradientEnd   => Color.FromArgb(37, 37, 38);
        }
    }
}
