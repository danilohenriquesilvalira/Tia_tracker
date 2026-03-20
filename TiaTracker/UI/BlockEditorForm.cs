using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TiaTracker.Core;
using TiaTracker.Core.BlockWriter;

namespace TiaTracker.UI
{
    /// <summary>
    /// Editor focado: criar FC/FB em FBD, criar DB, chamar na OB1.
    /// Gera o mesmo XML FBD que o TIA Portal exporta — import garantido.
    /// </summary>
    public class BlockEditorForm : Form
    {
        static readonly Color BG     = Color.FromArgb( 30,  30,  30);
        static readonly Color PANEL  = Color.FromArgb( 37,  37,  38);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_OK   = Color.FromArgb( 78, 160,  78);
        static readonly Color C_ERR  = Color.FromArgb(200,  60,  60);
        static readonly Color C_BLUE = Color.FromArgb(  0, 120, 212);
        static readonly Color C_PURP = Color.FromArgb( 90,  50, 140);
        static readonly Color C_GOLD = Color.FromArgb(170, 130,  40);
        static readonly Color BORDER = Color.FromArgb( 60,  60,  60);

        static readonly string[] InstrTypes =
        {
            "Move","Coil","SCoil","RCoil",
            "Add","Sub","Mul","Div","Mod","Neg","Abs","Inc","Dec",
            "Eq","Ne","Gt","Lt","Ge","Le",
            "TON","TOF","TP","TONR","CTU","CTD",
            "Convert","AND","OR","MoveBlockI","MoveBlockU",
        };

        static readonly string[] DataTypes =
        {
            "Bool","Byte","Word","DWord","Int","DInt","UInt","UDInt",
            "Real","LReal","Time","String","Char",
        };

        // ── controls ─────────────────────────────────────────────────────────
        private enum Mode { FC, FB, DB, Call }
        private Mode          _mode = Mode.FC;
        private TiaConnection _conn;

        private Label         _lblTitle;
        private TextBox       _txtName, _txtNumber;

        // FC/FB
        private DataGridView  _gridVars;          // variáveis de interface
        private DataGridView  _gridNets;          // redes (instruções)

        // DB
        private Panel         _pnlDb;
        private RadioButton   _rdoInst, _rdoGlobal;
        private TextBox       _txtFbName, _txtFbNum;

        // Call
        private Panel         _pnlCall;
        private ComboBox      _cmbCallType;

        private Label  _lblStatus;
        private Button _btnOk;

        // ════════════════════════════════════════════════════════════════════
        public BlockEditorForm(TiaConnection conn)
        {
            _conn = conn;
            Build();
            SetMode(Mode.FC);
        }

        // ════════════════════════════════════════════════════════════════════
        void Build()
        {
            Text          = "Danilo Tracker  —  Criar Blocos";
            Size          = new Size(1000, 660);
            MinimumSize   = new Size(780, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = C_TEXT;
            Font          = new Font("Segoe UI", 9f);

            // ── Left: botões de modo ─────────────────────────────────────────
            var left = new Panel { Width = 180, Dock = DockStyle.Left, BackColor = PANEL, Padding = new Padding(8,12,8,8) };

            var lbl = new Label { Text = "TIPO DE BLOCO", Dock = DockStyle.Top, Height = 22,
                ForeColor = Color.FromArgb(110,110,110), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };

            var bCall = ModeBtn("⚡  CALL na OB1",   C_OK,   Mode.Call);
            var sep   = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = BORDER };
            var bDB   = ModeBtn("🗄  DB — Data Block", C_GOLD, Mode.DB);
            var bFB   = ModeBtn("📦  FB — Func. Block",C_PURP, Mode.FB);
            var bFC   = ModeBtn("⚙  FC — Function",   C_BLUE, Mode.FC);

            left.Controls.Add(bCall); left.Controls.Add(sep);
            left.Controls.Add(bDB);  left.Controls.Add(bFB);
            left.Controls.Add(bFC);  left.Controls.Add(lbl);

            // ── Right: editor ────────────────────────────────────────────────
            var right = new Panel { Dock = DockStyle.Fill, BackColor = BG, Padding = new Padding(12,10,12,8) };

            // título
            _lblTitle = new Label { Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };

            // nome + número
            var fTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            fTop.Controls.Add(Lbl("Nome:",   0, 10));
            _txtName   = DarkBox(52, 6, 220); fTop.Controls.Add(_txtName);
            fTop.Controls.Add(Lbl("Nº:",   290, 10));
            _txtNumber = DarkBox(310, 6, 65);  fTop.Controls.Add(_txtNumber);

            // ── grids FC/FB ──────────────────────────────────────────────────
            var lblVars = SectionLabel("VARIÁVEIS DE INTERFACE  (Nome | Tipo | Direção)");
            _gridVars = MakeGrid(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name="VN", HeaderText="Nome",    FillWeight=35 },
                MakeComboCol("VT","Tipo",    DataTypes,   25),
                MakeComboCol("VD","Direção", new[]{"Input","Output","InOut","Static","Temp"}, 20),
                new DataGridViewTextBoxColumn { Name="VC", HeaderText="Comentário", FillWeight=20 },
            });
            _gridVars.Height = 130;

            var barVars = BtnBar(
                SmBtn("+ Variável", () => _gridVars.Rows.Add("Var"+((_gridVars.Rows.Count)+1),"Bool","Input","")),
                SmBtn("− Remover",  () => { foreach(DataGridViewRow r in _gridVars.SelectedRows) if(!r.IsNewRow) _gridVars.Rows.Remove(r); })
            );

            var lblNets = SectionLabel("REDES / INSTRUÇÕES  — uma instrução por rede");
            _gridNets = MakeGrid(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name="NT", HeaderText="Título rede", FillWeight=14 },
                MakeComboCol("NI","Instrução", InstrTypes, 12),
                new DataGridViewTextBoxColumn    { Name="N0", HeaderText="Enable/Instância", FillWeight=12 },
                new DataGridViewTextBoxColumn    { Name="N1", HeaderText="Param1 (in/in1)", FillWeight=11 },
                new DataGridViewTextBoxColumn    { Name="N2", HeaderText="Param2 (in2/PT)", FillWeight=11 },
                new DataGridViewTextBoxColumn    { Name="N3", HeaderText="Param3 (Tipo)", FillWeight=10 },
                new DataGridViewTextBoxColumn    { Name="N4", HeaderText="Param4 (DestType)", FillWeight=10 },
                new DataGridViewTextBoxColumn    { Name="N5", HeaderText="Saída 1 (out/Q)",  FillWeight=11 },
                new DataGridViewTextBoxColumn    { Name="N6", HeaderText="Saída 2 (ET/CV)",  FillWeight=10 },
            });

            var barNets = BtnBar(
                SmBtn("+ Rede",    () => _gridNets.Rows.Add("","Move","","","","","","","")),
                SmBtn("− Remover", () => { foreach(DataGridViewRow r in _gridNets.SelectedRows) if(!r.IsNewRow) _gridNets.Rows.Remove(r); }),
                SmBtn("↑ Subir",   () => MoveRow(_gridNets, -1)),
                SmBtn("↓ Descer",  () => MoveRow(_gridNets, +1))
            );

            // ── painel DB ────────────────────────────────────────────────────
            _pnlDb = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };

            _rdoInst   = new RadioButton { Text = "DB de Instância (para um FB)", Location = new Point(0, 10),  AutoSize=true, Checked=true, ForeColor=C_TEXT };
            _rdoGlobal = new RadioButton { Text = "DB Global (dados globais)",     Location = new Point(0, 36), AutoSize=true, ForeColor=C_TEXT };
            _rdoInst.CheckedChanged += (s,e) => RefreshDbPanel();

            _pnlDb.Controls.Add(_rdoInst); _pnlDb.Controls.Add(_rdoGlobal);
            _pnlDb.Controls.Add(Lbl("FB (nome):",   0, 70));
            _txtFbName = DarkBox(85, 66, 200); _pnlDb.Controls.Add(_txtFbName);
            _pnlDb.Controls.Add(Lbl("Nº FB:", 300, 70));
            _txtFbNum  = DarkBox(350, 66, 60); _pnlDb.Controls.Add(_txtFbNum);
            _txtFbNum.Text = "1";

            var hintDb = new Label { Text =
                "DB de Instância: cria uma DB associada a um FB existente (ou que vais criar).\n"+
                "DB Global: cria uma DB com dados globais (estrutura definida depois no TIA Portal).",
                Location = new Point(0, 105), AutoSize=true, ForeColor=Color.FromArgb(110,110,110) };
            _pnlDb.Controls.Add(hintDb);

            // ── painel CALL ──────────────────────────────────────────────────
            _pnlCall = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };
            _pnlCall.Controls.Add(Lbl("Tipo:", 0, 10));
            _cmbCallType = new ComboBox { Location=new Point(45,7), Width=80, DropDownStyle=ComboBoxStyle.DropDownList,
                BackColor=Color.FromArgb(45,45,48), ForeColor=C_TEXT };
            _cmbCallType.Items.AddRange(new object[]{"FC","FB"});
            _cmbCallType.SelectedIndex = 0;
            _pnlCall.Controls.Add(_cmbCallType);
            var hintCall = new Label { Text =
                "Vai adicionar uma rede na OB1 que chama o bloco com o Nome e Número acima.\n"+
                "O bloco já deve existir no TIA Portal (cria primeiro com FC ou FB acima).",
                Location = new Point(0, 40), AutoSize=true, ForeColor=Color.FromArgb(110,110,110) };
            _pnlCall.Controls.Add(hintCall);

            // ── bottom bar ───────────────────────────────────────────────────
            var bot = new Panel { Dock=DockStyle.Bottom, Height=44, BackColor=PANEL };
            _lblStatus = new Label { Location=new Point(10,10), AutoSize=false, Width=560, Height=24,
                ForeColor=Color.Silver, TextAlign=ContentAlignment.MiddleLeft };
            _btnOk = new Button { Text="▶  Criar no TIA Portal", Location=new Point(850,7),
                Size=new Size(135,30), BackColor=C_BLUE, ForeColor=Color.White,
                FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI",9.5f,FontStyle.Bold), Cursor=Cursors.Hand };
            _btnOk.Click += (s,e) => Execute();

            var btnXml = new Button { Text="Ver XML", Location=new Point(580,7),
                Size=new Size(70,30), BackColor=Color.FromArgb(50,50,55), ForeColor=C_TEXT,
                FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand };
            btnXml.Click += (s,e) => PreviewXml();

            bot.Controls.AddRange(new Control[]{ _lblStatus, btnXml, _btnOk });

            // ── montar right ─────────────────────────────────────────────────
            // ordem inversa no Dock.Top
            var fcfbPanel = new Panel { Dock=DockStyle.Fill, BackColor=BG };
            fcfbPanel.Controls.Add(_gridNets);
            fcfbPanel.Controls.Add(barNets);
            fcfbPanel.Controls.Add(lblNets);
            fcfbPanel.Controls.Add(_gridVars);
            fcfbPanel.Controls.Add(barVars);
            fcfbPanel.Controls.Add(lblVars);

            var stack = new Panel { Dock=DockStyle.Fill, BackColor=BG };
            stack.Controls.Add(fcfbPanel);
            stack.Controls.Add(_pnlCall);
            stack.Controls.Add(_pnlDb);

            right.Controls.Add(stack);
            right.Controls.Add(fTop);
            right.Controls.Add(_lblTitle);

            Controls.Add(right); Controls.Add(left); Controls.Add(bot);

            // layout dinâmico dos grids
            fcfbPanel.Resize += (s,e) =>
            {
                int w = fcfbPanel.Width - 4;
                _gridVars.Width = w;
                _gridNets.Width = w;
                _gridNets.Height = fcfbPanel.Height - _gridVars.Height - 80;
            };

            if (_conn?.Project == null)
                SetStatus("Sem ligação — conecta primeiro no ecrã principal para poder importar.", Color.FromArgb(180,140,50));
        }

        // ════════════════════════════════════════════════════════════════════
        void SetMode(Mode m)
        {
            _mode = m;
            bool isFcFb = m == Mode.FC || m == Mode.FB;
            _gridVars.Parent.Visible = isFcFb;
            _pnlDb.Visible           = m == Mode.DB;
            _pnlCall.Visible         = m == Mode.Call;

            switch (m)
            {
                case Mode.FC:
                    _lblTitle.Text = "Criar FC — Function";
                    _lblTitle.ForeColor = C_BLUE;
                    _txtName.Text = "FC_New"; _txtNumber.Text = "100";
                    _btnOk.Text = "▶  Criar FC"; _btnOk.BackColor = C_BLUE;
                    break;
                case Mode.FB:
                    _lblTitle.Text = "Criar FB — Function Block";
                    _lblTitle.ForeColor = C_PURP;
                    _txtName.Text = "FB_New"; _txtNumber.Text = "1";
                    _btnOk.Text = "▶  Criar FB"; _btnOk.BackColor = C_PURP;
                    break;
                case Mode.DB:
                    _lblTitle.Text = "Criar DB — Data Block";
                    _lblTitle.ForeColor = C_GOLD;
                    _txtName.Text = "DB_New"; _txtNumber.Text = "200";
                    _btnOk.Text = "▶  Criar DB"; _btnOk.BackColor = C_GOLD;
                    RefreshDbPanel();
                    break;
                case Mode.Call:
                    _lblTitle.Text = "Adicionar CALL na OB1";
                    _lblTitle.ForeColor = C_OK;
                    _txtName.Text = "FC_New"; _txtNumber.Text = "100";
                    _btnOk.Text = "▶  Adicionar CALL"; _btnOk.BackColor = C_OK;
                    break;
            }
        }

        void RefreshDbPanel()
        {
            bool isInst = _rdoInst.Checked;
            _txtFbName.Visible = isInst; _txtFbNum.Visible = isInst;
            foreach (Control c in _pnlDb.Controls)
                if (c is Label l && (l.Text=="FB (nome):" || l.Text=="Nº FB:"))
                    l.Visible = isInst;
        }

        // ════════════════════════════════════════════════════════════════════
        void PreviewXml()
        {
            string name = _txtName.Text.Trim();
            if (!int.TryParse(_txtNumber.Text.Trim(), out int num)) num = 0;
            string xml = null;
            try
            {
                if (_mode == Mode.FC || _mode == Mode.FB)
                {
                    var def = BuildDefinition(string.IsNullOrWhiteSpace(name) ? "Preview" : name, num, _mode == Mode.FB);
                    xml = FbdXmlGenerator.Generate(def);
                }
                else if (_mode == Mode.DB)
                {
                    bool isInst = _rdoInst.Checked;
                    if (isInst)
                        xml = SclBlockXmlGenerator.GenerateInstanceDB(name, num, _txtFbName.Text.Trim(),
                              int.TryParse(_txtFbNum.Text, out int fn) ? fn : 1);
                    else
                        xml = SclBlockXmlGenerator.GenerateGlobalDB(name, num);
                }
                else
                {
                    SetStatus("Seleciona FC, FB ou DB para pré-visualizar o XML.", C_ERR); return;
                }
            }
            catch (Exception ex) { SetStatus("Erro ao gerar XML: " + ex.Message, C_ERR); return; }

            // Guardar no Desktop
            string path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"TiaTracker_{name}.xml");
            System.IO.File.WriteAllText(path, xml, new System.Text.UTF8Encoding(true));
            SetStatus($"XML guardado: {path}", C_OK);
            System.Diagnostics.Process.Start("notepad.exe", path);
        }

        // ════════════════════════════════════════════════════════════════════
        void Execute()
        {
            if (_conn?.Project == null)
            { SetStatus("Sem ligação ao TIA Portal — conecta primeiro.", C_ERR); return; }

            string name = _txtName.Text.Trim();
            if (!int.TryParse(_txtNumber.Text.Trim(), out int num))
            { SetStatus("Número inválido.", C_ERR); return; }
            if (string.IsNullOrWhiteSpace(name))
            { SetStatus("Preenche o Nome.", C_ERR); return; }

            _btnOk.Enabled = false;
            SetStatus("A gerar e importar...", Color.Silver);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var importer = new BlockImporter(_conn);
                    string xml;

                    switch (_mode)
                    {
                        case Mode.FC:
                        case Mode.FB:
                            var def = BuildDefinition(name, num, _mode == Mode.FB);
                            xml = FbdXmlGenerator.Generate(def);
                            importer.ImportBlock(xml);
                            SetStatus($"'{name}' criado com sucesso no TIA Portal!", C_OK);
                            break;

                        case Mode.DB:
                            bool isInst = false; string fbName=""; int fbNum=1;
                            Invoke((Action)(() =>
                            {
                                isInst = _rdoInst.Checked;
                                fbName = _txtFbName.Text.Trim();
                                int.TryParse(_txtFbNum.Text, out fbNum);
                            }));
                            if (isInst)
                                xml = SclBlockXmlGenerator.GenerateInstanceDB(name, num, fbName, fbNum);
                            else
                                xml = SclBlockXmlGenerator.GenerateGlobalDB(name, num);
                            importer.ImportBlock(xml);
                            SetStatus($"DB '{name}' criada com sucesso!", C_OK);
                            break;

                        case Mode.Call:
                            string bType = "FC";
                            Invoke((Action)(() => bType = _cmbCallType.SelectedItem?.ToString() ?? "FC"));
                            importer.AddCallToOb1(name, bType);
                            SetStatus($"CALL '{name}' adicionado na OB1!", C_OK);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro: {ex.Message}", C_ERR);
                }
                finally { Invoke((Action)(() => _btnOk.Enabled = true)); }
            });
        }

        // ════════════════════════════════════════════════════════════════════
        // Constrói BlockDefinition a partir das grids
        // ════════════════════════════════════════════════════════════════════
        BlockDefinition BuildDefinition(string name, int num, bool isFb)
        {
            var def = new BlockDefinition
            {
                BlockType = isFb ? PlcBlockType.FB : PlcBlockType.FC,
                Name      = name,
                Number    = num,
            };

            // variáveis
            foreach (DataGridViewRow row in _gridVars.Rows)
            {
                var vname = row.Cells["VN"]?.Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(vname)) continue;
                var v = new InterfaceVar
                {
                    Name     = vname,
                    DataType = row.Cells["VT"]?.Value?.ToString() ?? "Bool",
                    Comment  = row.Cells["VC"]?.Value?.ToString() ?? "",
                };
                switch (row.Cells["VD"]?.Value?.ToString())
                {
                    case "Output": def.Outputs.Add(v); break;
                    case "InOut":  def.InOuts.Add(v);  break;
                    case "Static": def.Statics.Add(v); break;
                    case "Temp":   def.Temps.Add(v);   break;
                    default:       def.Inputs.Add(v);  break;
                }
            }

            // redes
            foreach (DataGridViewRow row in _gridNets.Rows)
            {
                var itype = row.Cells["NI"]?.Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(itype)) continue;
                var net = new NetworkDef { Title = row.Cells["NT"]?.Value?.ToString() ?? "" };
                net.Instructions.Add(new InstructionDef
                {
                    InstructType = itype,
                    EnableSignal = row.Cells["N0"]?.Value?.ToString() ?? "",
                    Param1       = row.Cells["N1"]?.Value?.ToString() ?? "",
                    Param2       = row.Cells["N2"]?.Value?.ToString() ?? "",
                    Param3       = row.Cells["N3"]?.Value?.ToString() ?? "",
                    Param4       = row.Cells["N4"]?.Value?.ToString() ?? "",
                    OutVar1      = row.Cells["N5"]?.Value?.ToString() ?? "",
                    OutVar2      = row.Cells["N6"]?.Value?.ToString() ?? "",
                });
                def.Networks.Add(net);
            }

            return def;
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers UI
        // ════════════════════════════════════════════════════════════════════
        void SetStatus(string msg, Color c)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStatus(msg, c))); return; }
            _lblStatus.Text = msg; _lblStatus.ForeColor = c;
        }

        static void MoveRow(DataGridView g, int delta)
        {
            int i = g.CurrentRow?.Index ?? -1;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= g.Rows.Count) return;
            var vals = new object[g.Columns.Count];
            for (int k = 0; k < g.Columns.Count; k++) vals[k] = g.Rows[i].Cells[k].Value;
            for (int k = 0; k < g.Columns.Count; k++) g.Rows[i].Cells[k].Value = g.Rows[j].Cells[k].Value;
            for (int k = 0; k < g.Columns.Count; k++) g.Rows[j].Cells[k].Value = vals[k];
            g.CurrentCell = g.Rows[j].Cells[0];
        }

        Button ModeBtn(string text, Color accent, Mode mode)
        {
            var b = new Button { Text=text, Dock=DockStyle.Top, Height=42,
                BackColor=Color.FromArgb(44,44,50), ForeColor=accent,
                FlatStyle=FlatStyle.Flat, TextAlign=ContentAlignment.MiddleLeft,
                Font=new Font("Segoe UI",9.5f,FontStyle.Bold), Cursor=Cursors.Hand,
                Margin=new Padding(0,0,0,3) };
            b.FlatAppearance.BorderColor = accent; b.FlatAppearance.BorderSize = 1;
            b.Click      += (s,e) => SetMode(mode);
            b.MouseEnter += (s,e) => b.BackColor = Color.FromArgb(55,55,65);
            b.MouseLeave += (s,e) => b.BackColor = Color.FromArgb(44,44,50);
            return b;
        }

        static DataGridView MakeGrid(DataGridViewColumn[] cols)
        {
            var g = new DataGridView
            {
                Dock=DockStyle.Top, BackgroundColor=PANEL, ForeColor=C_TEXT,
                GridColor=BORDER, BorderStyle=BorderStyle.None, AllowUserToAddRows=false,
                SelectionMode=DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible=false,
                AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersDefaultCellStyle = { BackColor=Color.FromArgb(50,50,52), ForeColor=C_TEXT },
                DefaultCellStyle = { BackColor=PANEL, ForeColor=C_TEXT, SelectionBackColor=Color.FromArgb(38,79,120) },
            };
            foreach (var c in cols) g.Columns.Add(c);
            return g;
        }

        static DataGridViewComboBoxColumn MakeComboCol(string name, string header, string[] items, int weight)
        {
            return new DataGridViewComboBoxColumn
            { Name=name, HeaderText=header, FillWeight=weight, DataSource=new List<string>(items), DisplayStyle=DataGridViewComboBoxDisplayStyle.ComboBox };
        }

        static Panel BtnBar(params Button[] btns)
        {
            var p = new Panel { Dock=DockStyle.Top, Height=26 };
            int x = 0;
            foreach (var b in btns) { b.Location = new Point(x, 2); x += b.Width + 4; p.Controls.Add(b); }
            return p;
        }

        static Button SmBtn(string text, Action onClick, int w = 88)
        {
            var b = new Button { Text=text, Size=new Size(w,22), BackColor=Color.FromArgb(55,55,60),
                ForeColor=C_TEXT, FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand };
            b.Click += (s,e) => onClick();
            return b;
        }

        static Label Lbl(string t, int x, int y) =>
            new Label { Text=t, Location=new Point(x,y), AutoSize=true, ForeColor=Color.FromArgb(160,160,160) };

        static Label SectionLabel(string t)
        {
            return new Label { Text=t, Dock=DockStyle.Top, Height=22, ForeColor=Color.FromArgb(120,120,140),
                Font=new Font("Segoe UI",7.5f,FontStyle.Bold), TextAlign=ContentAlignment.MiddleLeft };
        }

        static TextBox DarkBox(int x, int y, int w) =>
            new TextBox { Location=new Point(x,y), Width=w, BackColor=Color.FromArgb(45,45,48),
                ForeColor=Color.FromArgb(212,212,212), BorderStyle=BorderStyle.FixedSingle };
    }
}
