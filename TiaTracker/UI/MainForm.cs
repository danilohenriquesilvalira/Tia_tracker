using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
        private string             _xmlResult;
        private string             _mdResult;
        private TiaConnection      _conn;
        private List<BlockInfo>    _blocks    = new List<BlockInfo>();
        private List<TagTableInfo> _tagTables = new List<TagTableInfo>();
        private List<UdtInfo>      _udts      = new List<UdtInfo>();

        // ── Theme ─────────────────────────────────────────────────────────────
        static readonly Color BG      = Color.FromArgb( 18,  18,  20);
        static readonly Color PANEL   = Color.FromArgb( 28,  28,  34);
        static readonly Color TREE_BG = Color.FromArgb( 22,  22,  28);
        static readonly Color LOG_BG  = Color.FromArgb( 14,  14,  18);
        static readonly Color C_OB    = Color.FromArgb( 78, 201, 176);
        static readonly Color C_FB    = Color.FromArgb(197, 134, 192);
        static readonly Color C_FC    = Color.FromArgb( 86, 156, 214);
        static readonly Color C_DB    = Color.FromArgb(220, 160,  80);
        static readonly Color C_TAGS  = Color.FromArgb(220, 110, 110);
        static readonly Color C_UDT   = Color.FromArgb(150, 200, 130);
        static readonly Color C_NET   = Color.FromArgb(106, 153,  85);
        static readonly Color C_IFACE = Color.FromArgb(156, 156, 156);
        static readonly Color C_TEXT  = Color.FromArgb(212, 212, 212);
        static readonly Color C_OK    = Color.FromArgb( 80, 200,  80);
        static readonly Color C_ERR   = Color.FromArgb(220,  70,  70);
        static readonly Color C_GOLD  = Color.FromArgb(220, 180,  60);
        static readonly Color C_GROUP = Color.FromArgb(110, 110, 125);
        static readonly Color C_BLUE  = Color.FromArgb(  0, 122, 204);

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

            // ── Header com gradiente pintado ──────────────────────────────────
            var header = new Panel { Dock = DockStyle.Top, Height = 54 };
            header.Paint += (s, e) =>
            {
                var rc = header.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;   // guard against zero-size
                using var br = new LinearGradientBrush(rc,
                    Color.FromArgb(28, 24, 48), Color.FromArgb(18, 18, 26),
                    LinearGradientMode.Horizontal);
                e.Graphics.FillRectangle(br, rc);
                using var pen = new Pen(Color.FromArgb(80, 60, 160), 2);
                e.Graphics.DrawLine(pen, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
            };

            var pnlHeaderLeft = new Panel { Dock = DockStyle.Left, Width = 54, BackColor = Color.Transparent };
            pnlHeaderLeft.Paint += (s, e) =>
            {
                var g  = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // círculo centrado no painel
                int cx = pnlHeaderLeft.Width  / 2;
                int cy = pnlHeaderLeft.Height / 2;
                int r  = 18;
                var circleRect = new Rectangle(cx - r, cy - r, r * 2, r * 2);

                if (circleRect.Width <= 0 || circleRect.Height <= 0) return;

                using var br = new LinearGradientBrush(circleRect, C_BLUE, Color.FromArgb(100, 40, 180), 135f);
                g.FillEllipse(br, circleRect);

                // "D" centrado com StringFormat — garante alinhamento perfeito
                using var f  = new Font("Segoe UI", 15f, FontStyle.Bold);
                using var sf = new StringFormat
                {
                    Alignment     = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                using var sb = new SolidBrush(Color.White);
                g.DrawString("D", f, sb, new RectangleF(circleRect.X, circleRect.Y, circleRect.Width, circleRect.Height), sf);
            };

            var lblTitle = new Label
            {
                Text      = "DANILO TRACKER",
                Dock      = DockStyle.Left,
                Width     = 210,
                Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            var lblSub = new Label
            {
                Text      = "TIA Portal V18  ·  Leitura offline de projeto PLC  ·  Exportação XML & Markdown para IA",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(140, 130, 180),
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
                Height    = 50,
                BackColor = Color.FromArgb(24, 24, 32),
                Padding   = new Padding(10, 8, 10, 8)
            };

            var tbl = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 6,
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

            var lblPath = new Label
            {
                Text      = "Projeto:",
                Dock      = DockStyle.Fill,
                ForeColor = Color.FromArgb(160, 155, 190),
                TextAlign = ContentAlignment.MiddleRight,
                Font      = new Font("Segoe UI", 8.5f)
            };

            _txtPath = new TextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(18, 18, 28),
                ForeColor   = Color.FromArgb(200, 195, 230),
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Consolas", 8.8f),
                Text        = @"C:\Users\Admin\Desktop\C#_PLC\C#_PLC.ap18",
                Margin      = new Padding(6, 0, 6, 0)
            };

            _btnBrowse  = MkBtn("···",              38, Color.FromArgb(42, 42, 60));
            _btnRun     = MkBtn("▶  Conectar e Ler", 174, C_BLUE);
            _btnSaveXml = MkBtn("💾  XML",          138, Color.FromArgb(50, 58, 75));
            _btnSaveMd  = MkBtn("🤖  Exportar para IA", 158, Color.FromArgb(28, 72, 42));

            _btnRun.Font        = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnRun.Margin      = new Padding(4, 0, 4, 0);
            _btnSaveXml.Enabled = false;
            _btnSaveMd.Enabled  = false;
            _btnSaveMd.FlatAppearance.BorderColor = Color.FromArgb(50, 130, 70);

            _btnBrowse.Click  += (s, e) => BrowseProject();
            _btnRun.Click     += async (s, e) => await RunAsync();
            _btnSaveXml.Click += (s, e) => SaveXml();
            _btnSaveMd.Click  += (s, e) => SaveMd();

            // hover nos botões
            AddHover(_btnBrowse,  Color.FromArgb(55, 55, 80),   Color.FromArgb(42, 42, 60));
            AddHover(_btnSaveXml, Color.FromArgb(60, 72, 95),   Color.FromArgb(50, 58, 75));
            AddHover(_btnSaveMd,  Color.FromArgb(38, 95, 55),   Color.FromArgb(28, 72, 42));
            AddHover(_btnRun,     Color.FromArgb(30, 140, 230),  C_BLUE);

            tbl.Controls.Add(lblPath,     0, 0);
            tbl.Controls.Add(_txtPath,    1, 0);
            tbl.Controls.Add(_btnBrowse,  2, 0);
            tbl.Controls.Add(_btnRun,     3, 0);
            tbl.Controls.Add(_btnSaveXml, 4, 0);
            tbl.Controls.Add(_btnSaveMd,  5, 0);
            toolbar.Controls.Add(tbl);

            _progress = new ProgressBar
            {
                Dock                  = DockStyle.Top,
                Height                = 2,
                Style                 = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                BackColor             = Color.FromArgb(24, 24, 32)
            };

            // ── StatusStrip ───────────────────────────────────────────────────
            var strip = new StatusStrip { BackColor = Color.FromArgb(16, 16, 24), SizingGrip = false };
            _lblStatus = new ToolStripStatusLabel
            {
                Text      = "  Pronto. Selecione um projeto e clique em  ▶ Conectar e Ler.",
                ForeColor = Color.FromArgb(150, 145, 180),
                Spring    = true
            };
            _lblStats = new ToolStripStatusLabel { Text = "", ForeColor = C_GROUP, Spring = false };
            strip.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblStats });

            // ── Outer split ───────────────────────────────────────────────────
            var outerSplit = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = Orientation.Vertical,
                SplitterWidth = 3,
                BackColor     = Color.FromArgb(35, 35, 45),
            };

            // ── Left panel ────────────────────────────────────────────────────
            var treeTitle = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 30,
                BackColor = Color.FromArgb(20, 20, 30)
            };
            treeTitle.Paint += (s, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(20, 20, 30)), treeTitle.ClientRectangle);
                using var f = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                using var b = new SolidBrush(Color.FromArgb(100, 95, 140));
                e.Graphics.DrawString("  ESTRUTURA DO PROJETO", f, b, 2, 9);
            };

            _txtSearch = new TextBox
            {
                Dock        = DockStyle.Top,
                Height      = 28,
                BackColor   = Color.FromArgb(20, 20, 32),
                ForeColor   = Color.FromArgb(130, 125, 165),
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 9f),
                Text        = "  🔍  Filtrar blocos...",
                Padding     = new Padding(4, 0, 0, 0)
            };
            _txtSearch.GotFocus += (s, e) =>
            {
                if (_txtSearch.Text.Contains("Filtrar")) { _txtSearch.Text = ""; _txtSearch.ForeColor = Color.White; }
            };
            _txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSearch.Text)) { _txtSearch.Text = "  🔍  Filtrar blocos..."; _txtSearch.ForeColor = Color.FromArgb(130, 125, 165); }
            };
            _txtSearch.TextChanged += (s, e) => FilterTree(_txtSearch.Text);

            var searchBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(45, 45, 65) };

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
            _tree.DrawNode  += DrawTreeNode;
            _tree.AfterSelect += OnTreeSelect;

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
                SplitterWidth = 3,
                BackColor     = Color.FromArgb(35, 35, 45),
                FixedPanel    = FixedPanel.Panel2
            };

            // Detail header card
            _detailHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = Color.FromArgb(24, 22, 40),
                Visible   = false
            };
            _detailHeader.Paint += (s, e) =>
            {
                var rc = _detailHeader.ClientRectangle;
                using var pen = new Pen(Color.FromArgb(70, 60, 120), 1);
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
                Location  = new Point(14, 30),
                Size      = new Size(800, 16),
                Font      = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(140, 130, 175),
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
                Text      = "  LOG DE LIGAÇÃO",
                Dock      = DockStyle.Top,
                Height    = 22,
                BackColor = Color.FromArgb(18, 18, 28),
                ForeColor = Color.FromArgb(90, 85, 120),
                Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _rtbLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = LOG_BG,
                ForeColor   = Color.FromArgb(120, 115, 150),
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
            Color bgColor = selected ? Color.FromArgb(45, 42, 78) : TREE_BG;
            g.FillRectangle(new SolidBrush(bgColor), new Rectangle(0, rc.Y, _tree.Width, rc.Height));

            // linha de hover simulada (selected border accent)
            if (selected)
            {
                using var pen = new Pen(Color.FromArgb(100, 80, 200), 2);
                g.DrawLine(pen, 0, rc.Y, 0, rc.Bottom);
            }

            // sinal +/- (expand/collapse)
            if (node.Nodes.Count > 0)
            {
                int midY  = rc.Y + rc.Height / 2;
                int arrowX = rc.X - 14;
                using var pen = new Pen(Color.FromArgb(90, 85, 130), 1);
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

            // badge colorido por tipo (só folhas com Tag)
            int textX = rc.X + 4;
            if (node.Tag != null)
            {
                Color badge = node.Tag is BlockInfo b  ? (b.Type == "OB" ? C_OB : b.Type == "FB" ? C_FB : b.Type == "FC" ? C_FC : C_DB) :
                              node.Tag is TagTableInfo ? C_TAGS : C_UDT;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var br = new SolidBrush(badge);
                g.FillRectangle(br, rc.X + 2, rc.Y + 7, 5, rc.Height - 14);
                g.SmoothingMode = SmoothingMode.Default;
                textX = rc.X + 12;
            }

            // texto
            var textColor = node.ForeColor == Color.Empty ? C_TEXT : node.ForeColor;
            var font      = node.NodeFont ?? _tree.Font;
            TextRenderer.DrawText(g, node.Text, font,
                new Rectangle(textX, rc.Y, rc.Width - textX + rc.X, rc.Height),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
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
                MinimumSize = new Size(width, 28),
                BackColor   = bg ?? Color.FromArgb(48, 48, 58),
                ForeColor   = Color.White,
                FlatStyle   = FlatStyle.Flat,
                Cursor      = Cursors.Hand,
                Margin      = Padding.Empty
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 95);
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
            _progress.MarqueeAnimationSpeed = 30;
            SetStatus("A preparar...", Color.Silver);
            SetStats("");

            var path = _txtPath.Text.Trim();

            await Task.Run(() =>
            {
                Console.SetOut(new RtbWriter(this));
                try
                {
                    SetStatus("A conectar ao TIA Portal...", Color.Silver);
                    _conn?.Dispose();
                    _conn = new TiaConnection(path);

                    if (!_conn.Connect())
                    {
                        SetStatus("Falha na ligação!", C_ERR);
                        return;
                    }

                    var reader = new ProjectReader(_conn.Project);

                    SetStatus("A ler blocos  (OB / FB / FC / DB)...", Color.Silver);
                    _blocks = reader.ReadAllBlocks();

                    SetStatus("A ler Tag Tables...", Color.Silver);
                    _tagTables = reader.ReadAllTagTables();

                    SetStatus("A ler Tipos de Dados (UDTs)...", Color.Silver);
                    _udts = reader.ReadAllUDTs();

                    SetStatus("A construir árvore...", Color.Silver);
                    Invoke((Action)BuildTree);

                    _xmlResult = BuildXml(_blocks, _tagTables, _udts, path);
                    _mdResult  = BuildMarkdown(_blocks, _tagTables, _udts, path);

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

            _running = false;
            _btnRun.Enabled = true;
            _progress.MarqueeAnimationSpeed = 0;
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
                var devNode = new TreeNode($"  {dev}")
                {
                    ForeColor = C_GOLD,
                    NodeFont  = new Font("Segoe UI", 10f, FontStyle.Bold)
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
                    AddBlockGroup(devNode, devBlocks, "OB",  "Blocos de Organização  [OB]", C_OB);
                    AddBlockGroup(devNode, devBlocks, "FB",  "Blocos de Função  [FB]",      C_FB);
                    AddBlockGroup(devNode, devBlocks, "FC",  "Funções  [FC]",               C_FC);
                    AddBlockGroup(devNode, devBlocks, "DB",  "Blocos de Dados  [DB]",       C_DB);
                    AddBlockGroup(devNode, devBlocks, "iDB", "Instance DBs  [iDB]",         C_DB);
                }

                // Tag Tables
                var devTags = _tagTables.Where(t => t.Device == dev).OrderBy(t => t.Name).ToList();
                if (devTags.Count > 0)
                {
                    var grp = MakeGroupNode($"  Tag Tables  ({devTags.Count})");
                    foreach (var tt in devTags)
                        grp.Nodes.Add(new TreeNode($"  {tt.Name}  ({tt.Tags.Count} tags)")
                            { ForeColor = C_TAGS, Tag = tt });
                    devNode.Nodes.Add(grp);
                }

                // UDTs
                var devUdts = _udts.Where(u => u.Device == dev).OrderBy(u => u.Name).ToList();
                if (devUdts.Count > 0)
                {
                    var grp = MakeGroupNode($"  Tipos de Dados — UDTs  ({devUdts.Count})");
                    foreach (var udt in devUdts)
                        grp.Nodes.Add(new TreeNode($"  {udt.Name}")
                            { ForeColor = C_UDT, Tag = udt });
                    devNode.Nodes.Add(grp);
                }

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
                var folderNode = MakeFolderNode($"  📁  {folder}  ({folderBlocks.Count})", folderBlocks.Count);
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
                var subNode   = MakeFolderNode($"  📁  {sub}  ({subBlocks.Count})", subBlocks.Count);
                AddFolderChildren(subNode, subBlocks, subPath);
                parent.Nodes.Add(subNode);
            }
        }

        private TreeNode MakeBlockLeaf(BlockInfo b)
        {
            var col  = b.Type == "OB" ? C_OB : b.Type == "FB" ? C_FB : b.Type == "FC" ? C_FC : C_DB;
            var nets = b.Networks.Count > 0 ? $"  [{b.Networks.Count}]" : "";
            return new TreeNode($"  {b.Type}{b.Number}  —  {b.Name}{nets}") { ForeColor = col, Tag = b };
        }

        private TreeNode MakeFolderNode(string text, int count) =>
            new TreeNode(text) { ForeColor = Color.FromArgb(180, 170, 100), NodeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold) };

        private TreeNode MakeGroupNode(string text) =>
            new TreeNode(text) { ForeColor = C_GROUP, NodeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold) };

        private void AddBlockGroup(TreeNode parent, List<BlockInfo> blocks, string type, string label, Color color)
        {
            var list = blocks.Where(b => b.Type == type).OrderBy(b => b.Number).ToList();
            if (list.Count == 0) return;
            var grp = MakeGroupNode($"  {label}  ({list.Count})");
            foreach (var b in list)
            {
                var nets = b.Networks.Count > 0 ? $"  [{b.Networks.Count} redes]" : "";
                grp.Nodes.Add(new TreeNode($"  {type}{b.Number}  —  {b.Name}{nets}")
                    { ForeColor = color, Tag = b });
            }
            parent.Nodes.Add(grp);
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

            return sb.ToString();
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

            // Networks
            if (b.Networks.Count > 0)
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
}
