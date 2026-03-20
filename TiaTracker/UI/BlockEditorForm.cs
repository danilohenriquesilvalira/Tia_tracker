using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TiaTracker.Core;
using TiaTracker.Core.BlockWriter;

namespace TiaTracker.UI
{
    /// <summary>
    /// Editor visual para criar blocos FC/FB com redes FBD e importar direto no TIA Portal.
    /// </summary>
    public class BlockEditorForm : Form
    {
        // ── Theme (idêntico ao MainForm) ─────────────────────────────────────
        static readonly Color BG     = Color.FromArgb( 30,  30,  30);
        static readonly Color PANEL  = Color.FromArgb( 37,  37,  38);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_OK   = Color.FromArgb(106, 153,  85);
        static readonly Color C_ERR  = Color.FromArgb(244,  71,  71);
        static readonly Color C_BLUE = Color.FromArgb(  0, 120, 212);
        static readonly Color C_SEL  = Color.FromArgb( 38,  79, 120);
        static readonly Color C_GOLD = Color.FromArgb(215, 186, 125);
        static readonly Color BORDER = Color.FromArgb( 60,  60,  60);

        // ── Tipos de instrução disponíveis ───────────────────────────────────
        static readonly string[] InstructionTypes =
        {
            "Move", "Coil", "SCoil", "RCoil",
            "Add", "Sub", "Mul", "Div", "Mod", "Neg", "Abs", "Inc", "Dec",
            "Eq", "Ne", "Gt", "Lt", "Ge", "Le",
            "TON", "TOF", "TP", "TONR",
            "CTU", "CTD",
            "Convert", "AND", "OR",
            "MoveBlockI", "MoveBlockU",
        };

        // ── Descrições das instruções ────────────────────────────────────────
        static readonly string[] InstructionDescriptions =
        {
            "MOVE  — copiar valor (Param1=fonte, Out1=destino)",
            "COIL  — bobina saída (Param1=sinal Bool, Out1=variável Bool)",
            "SCOIL — bobina Set (Param1=sinal Bool, Out1=variável Bool)",
            "RCOIL — bobina Reset (Param1=sinal Bool, Out1=variável Bool)",
            "ADD   — adição (Param1=in1, Param2=in2, Out1=resultado)",
            "SUB   — subtracção (Param1=in1, Param2=in2, Out1=resultado)",
            "MUL   — multiplicação (Param1=in1, Param2=in2, Out1=resultado)",
            "DIV   — divisão (Param1=in1, Param2=in2, Out1=resultado)",
            "MOD   — resto (Param1=in1, Param2=in2, Out1=resultado)",
            "NEG   — negativo (Param1=in, Out1=resultado)",
            "ABS   — valor absoluto (Param1=in, Out1=resultado)",
            "INC   — incremento in-place (Param1=variável)",
            "DEC   — decremento in-place (Param1=variável)",
            "EQ    — comparar == (Param1=in1, Param2=in2, Out1=coil Bool)",
            "NE    — comparar <> (Param1=in1, Param2=in2, Out1=coil Bool)",
            "GT    — comparar >  (Param1=in1, Param2=in2, Out1=coil Bool)",
            "LT    — comparar <  (Param1=in1, Param2=in2, Out1=coil Bool)",
            "GE    — comparar >= (Param1=in1, Param2=in2, Out1=coil Bool)",
            "LE    — comparar <= (Param1=in1, Param2=in2, Out1=coil Bool)",
            "TON   — timer ON (Enable=instância, Param1=IN, Param2=PT, Out1=Q, Out2=ET)",
            "TOF   — timer OFF (Enable=instância, Param1=IN, Param2=PT, Out1=Q, Out2=ET)",
            "TP    — timer pulso (Enable=instância, Param1=IN, Param2=PT, Out1=Q, Out2=ET)",
            "TONR  — timer retenção (Enable=instância, Param1=IN, Param2=PT, Out1=Q, Out2=ET)",
            "CTU   — contador UP (Enable=instância, Param1=CU, Param2=PV, Out1=Q, Out2=CV)",
            "CTD   — contador DOWN (Enable=instância, Param1=CD, Param2=PV, Out1=Q, Out2=CV)",
            "CONV  — converter tipo (Param1=in, Param3=SrcType, Param4=DestType, Out1=out)",
            "AND   — E lógico (Param1=in1, Param2=in2, Out1=out)",
            "OR    — OU lógico (Param1=in1, Param2=in2, Out1=out)",
            "MOVE_BLK  — copiar bloco (Param1=in, Param2=count, Out1=out)",
            "UMOVE_BLK — copiar bloco ininterruptível (Param1=in, Param2=count, Out1=out)",
        };

        // ── Controls ─────────────────────────────────────────────────────────
        private TabControl     _tabs;
        private TextBox        _txtName, _txtNumber, _txtDesc;
        private ComboBox       _cmbBlockType;
        private DataGridView   _gridVars;
        private ComboBox       _cmbVarSection;
        private ListBox        _lstNetworks;
        private Button         _btnAddNet, _btnDelNet, _btnMoveUp, _btnMoveDown;
        private TextBox        _txtNetTitle, _txtNetComment;
        private DataGridView   _gridInstructions;
        private Label          _lblInstHelp;
        private RichTextBox    _rtbPreview;
        private Label          _lblStatus;
        private Button         _btnGenerate, _btnImport;
        private CheckBox       _chkAddOb1;

        private BlockDefinition _def = new BlockDefinition();
        private string          _lastXml = null;
        private TiaConnection   _conn;

        // ════════════════════════════════════════════════════════════════════
        public BlockEditorForm(TiaConnection conn)
        {
            _conn = conn;
            BuildUI();
            RefreshNetworkList();
            RefreshVarGrid();
            UpdateImportButtons();
        }

        // ════════════════════════════════════════════════════════════════════
        // UI Build
        // ════════════════════════════════════════════════════════════════════
        void BuildUI()
        {
            Text          = "Danilo Tracker  —  Editor de Blocos FBD";
            Size          = new Size(1100, 760);
            MinimumSize   = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = C_TEXT;
            Font          = new Font("Segoe UI", 9f);

            _tabs = new TabControl
            {
                Dock        = DockStyle.Fill,
                BackColor   = PANEL,
                ForeColor   = C_TEXT,
                Appearance  = TabAppearance.Normal,
                DrawMode    = TabDrawMode.OwnerDrawFixed,
                ItemSize    = new Size(140, 28),
            };
            _tabs.DrawItem += TabsDrawItem;

            _tabs.TabPages.Add(BuildTabInfo());
            _tabs.TabPages.Add(BuildTabInterface());
            _tabs.TabPages.Add(BuildTabNetworks());
            _tabs.TabPages.Add(BuildTabPreview());

            // ── Bottom bar ───────────────────────────────────────────────────
            var bottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 48,
                BackColor = PANEL,
            };

            _lblStatus = new Label
            {
                AutoSize  = false,
                Width     = 500,
                Height    = 32,
                Location  = new Point(8, 8),
                ForeColor = Color.Silver,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _chkAddOb1 = new CheckBox
            {
                Text      = "Adicionar chamada no OB1",
                Location  = new Point(520, 14),
                AutoSize  = true,
                ForeColor = C_TEXT,
                Checked   = true,
            };

            _btnGenerate = MakeBtn("▶  Gerar XML", C_BLUE, 820, 9, 130);
            _btnImport   = MakeBtn("⬆  Importar no TIA", C_OK,  960, 9, 130);
            _btnGenerate.Click += (s, e) => GenerateXml();
            _btnImport.Click   += (s, e) => ImportBlock();

            bottom.Controls.AddRange(new Control[]
                { _lblStatus, _chkAddOb1, _btnGenerate, _btnImport });

            Controls.Add(_tabs);
            Controls.Add(bottom);
        }

        // ── Tab: Informação do bloco ─────────────────────────────────────────
        TabPage BuildTabInfo()
        {
            var page = DarkTab("  ⚙  Bloco");
            var panel = DarkPanel();
            panel.Dock = DockStyle.Fill;

            int y = 24;

            void Row(string label, Control ctrl, int height = 26)
            {
                var lbl = DarkLabel(label, 24, y);
                ctrl.Location = new Point(160, y);
                ctrl.Width    = 340;
                ctrl.Height   = height;
                ctrl.BackColor = Color.FromArgb(45, 45, 48);
                ctrl.ForeColor = C_TEXT;
                panel.Controls.Add(lbl);
                panel.Controls.Add(ctrl);
                y += height + 10;
            }

            _cmbBlockType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbBlockType.Items.AddRange(new object[] { "FC", "FB" });
            _cmbBlockType.SelectedIndex = 0;
            _cmbBlockType.SelectedIndexChanged += (s, e) => SyncDefFromUI();
            Row("Tipo de bloco:", _cmbBlockType);

            _txtName = new TextBox { Text = "FC_New" };
            _txtName.TextChanged += (s, e) => SyncDefFromUI();
            Row("Nome:", _txtName);

            _txtNumber = new TextBox { Text = "100" };
            _txtNumber.TextChanged += (s, e) => SyncDefFromUI();
            Row("Número:", _txtNumber);

            _txtDesc = new TextBox { Text = "", Multiline = true };
            _txtDesc.TextChanged += (s, e) => SyncDefFromUI();
            Row("Descrição:", _txtDesc, 52);

            // Hint
            y += 16;
            var hint = new Label
            {
                Text      = "Dica: use a aba Interface para definir as variáveis do bloco,\ne a aba Redes para adicionar as instruções FBD.",
                Location  = new Point(24, y),
                AutoSize  = true,
                ForeColor = Color.FromArgb(120, 120, 120),
            };
            panel.Controls.Add(hint);

            page.Controls.Add(panel);
            return page;
        }

        // ── Tab: Interface ───────────────────────────────────────────────────
        TabPage BuildTabInterface()
        {
            var page = DarkTab("  📋  Interface");
            var panel = DarkPanel();
            panel.Dock = DockStyle.Fill;

            // Section selector
            var lblSection = DarkLabel("Secção:", 12, 12);
            _cmbVarSection = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location      = new Point(80, 9),
                Width         = 140,
                BackColor     = Color.FromArgb(45, 45, 48),
                ForeColor     = C_TEXT,
            };
            _cmbVarSection.Items.AddRange(new object[]
                { "Input", "Output", "InOut", "Static (FB)", "Temp" });
            _cmbVarSection.SelectedIndex = 0;
            _cmbVarSection.SelectedIndexChanged += (s, e) => RefreshVarGrid();

            var btnAddVar = MakeSmallBtn("+ Variável", 240, 9);
            var btnDelVar = MakeSmallBtn("− Remover",  350, 9);
            btnAddVar.Click += (s, e) => AddVar();
            btnDelVar.Click += (s, e) => DelVar();

            _gridVars = new DataGridView
            {
                Location          = new Point(8, 42),
                BackgroundColor   = PANEL,
                ForeColor         = C_TEXT,
                GridColor         = BORDER,
                BorderStyle       = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(50,50,52), ForeColor = C_TEXT },
                DefaultCellStyle  = { BackColor = PANEL, ForeColor = C_TEXT, SelectionBackColor = C_SEL },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                SelectionMode     = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
            };
            _gridVars.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name",     HeaderText = "Nome",      FillWeight = 30 });
            _gridVars.Columns.Add(MakeDataTypeColumn());
            _gridVars.Columns.Add(new DataGridViewTextBoxColumn { Name = "Default",  HeaderText = "Inicial",   FillWeight = 20 });
            _gridVars.Columns.Add(new DataGridViewTextBoxColumn { Name = "Comment",  HeaderText = "Comentário",FillWeight = 35 });
            _gridVars.CellValueChanged += (s, e) => SaveVarGrid();
            _gridVars.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;

            panel.Controls.AddRange(new Control[]
                { lblSection, _cmbVarSection, btnAddVar, btnDelVar, _gridVars });

            panel.Resize += (s, e) =>
            {
                _gridVars.Width  = panel.Width  - 16;
                _gridVars.Height = panel.Height - 52;
            };

            page.Controls.Add(panel);
            return page;
        }

        // ── Tab: Redes ───────────────────────────────────────────────────────
        TabPage BuildTabNetworks()
        {
            var page = DarkTab("  🔗  Redes");
            var splitMain = new SplitContainer
            {
                Dock           = DockStyle.Fill,
                BackColor      = BG,
                Orientation    = Orientation.Vertical,
                SplitterWidth  = 4,
                SplitterDistance = 220,
            };

            // ── Panel esquerdo: lista de redes ───────────────────────────────
            var leftPanel = DarkPanel();
            leftPanel.Dock = DockStyle.Fill;

            var lblNets = DarkLabel("REDES:", 8, 8);
            lblNets.Font = new Font("Segoe UI", 8f, FontStyle.Bold);

            _lstNetworks = new ListBox
            {
                Location          = new Point(4, 30),
                BackColor         = PANEL,
                ForeColor         = C_TEXT,
                BorderStyle       = BorderStyle.None,
                SelectionMode     = SelectionMode.One,
                IntegralHeight    = false,
            };
            _lstNetworks.SelectedIndexChanged += (s, e) => RefreshNetworkEditor();
            leftPanel.Controls.AddRange(new Control[] { lblNets, _lstNetworks });

            _btnAddNet   = MakeSmallBtn("+ Rede",  4, 0);
            _btnDelNet   = MakeSmallBtn("− Del",  84, 0);
            _btnMoveUp   = MakeSmallBtn("↑",     162, 0, 28);
            _btnMoveDown = MakeSmallBtn("↓",     194, 0, 28);
            _btnAddNet.Click   += (s, e) => AddNetwork();
            _btnDelNet.Click   += (s, e) => DelNetwork();
            _btnMoveUp.Click   += (s, e) => MoveNetwork(-1);
            _btnMoveDown.Click += (s, e) => MoveNetwork(+1);

            var netBtnBar = new Panel { Height = 30, Dock = DockStyle.Bottom, BackColor = BG };
            netBtnBar.Controls.AddRange(new Control[]
                { _btnAddNet, _btnDelNet, _btnMoveUp, _btnMoveDown });
            leftPanel.Controls.Add(netBtnBar);

            leftPanel.Resize += (s, e) =>
            {
                _lstNetworks.Width  = leftPanel.Width - 8;
                _lstNetworks.Height = leftPanel.Height - 65;
            };

            splitMain.Panel1.Controls.Add(leftPanel);

            // ── Panel direito: editor de rede ────────────────────────────────
            var rightPanel = DarkPanel();
            rightPanel.Dock = DockStyle.Fill;

            var lblTitle   = DarkLabel("Título:",    8,  8);
            _txtNetTitle   = DarkTextBox(70, 6, 300);
            _txtNetTitle.TextChanged += (s, e) => SaveNetworkMeta();

            var lblComment = DarkLabel("Comentário:", 8, 36);
            _txtNetComment = DarkTextBox(90, 34, 300);
            _txtNetComment.TextChanged += (s, e) => SaveNetworkMeta();

            var lblInst    = DarkLabel("INSTRUÇÕES:", 8, 66);
            lblInst.Font   = new Font("Segoe UI", 8f, FontStyle.Bold);

            var btnAddInst = MakeSmallBtn("+ Instrução", 110, 62);
            var btnDelInst = MakeSmallBtn("− Remover",   215, 62);
            btnAddInst.Click += (s, e) => AddInstruction();
            btnDelInst.Click += (s, e) => DelInstruction();

            _gridInstructions = new DataGridView
            {
                Location         = new Point(4, 90),
                BackgroundColor  = PANEL,
                ForeColor        = C_TEXT,
                GridColor        = BORDER,
                BorderStyle      = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(50,50,52), ForeColor = C_TEXT },
                DefaultCellStyle = { BackColor = PANEL, ForeColor = C_TEXT, SelectionBackColor = C_SEL },
                AllowUserToAddRows = false,
                SelectionMode    = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
            };
            BuildInstructionColumns();
            _gridInstructions.CellValueChanged  += (s, e) => SaveInstructions();
            _gridInstructions.SelectionChanged  += (s, e) => UpdateInstHelp();

            _lblInstHelp = new Label
            {
                AutoSize  = false,
                Height    = 40,
                ForeColor = Color.FromArgb(120, 120, 170),
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8f),
                Dock      = DockStyle.Bottom,
            };

            rightPanel.Controls.AddRange(new Control[]
            {
                lblTitle, _txtNetTitle, lblComment, _txtNetComment,
                lblInst, btnAddInst, btnDelInst,
                _gridInstructions, _lblInstHelp
            });

            rightPanel.Resize += (s, e) =>
            {
                _txtNetTitle.Width   = rightPanel.Width - 80;
                _txtNetComment.Width = rightPanel.Width - 100;
                _gridInstructions.Width  = rightPanel.Width - 8;
                _gridInstructions.Height = rightPanel.Height - 140;
                _lblInstHelp.Width = rightPanel.Width;
            };

            splitMain.Panel2.Controls.Add(rightPanel);
            page.Controls.Add(splitMain);
            return page;
        }

        // ── Tab: Preview XML ─────────────────────────────────────────────────
        TabPage BuildTabPreview()
        {
            var page = DarkTab("  📄  Preview XML");
            _rtbPreview = new RichTextBox
            {
                Dock       = DockStyle.Fill,
                BackColor  = Color.FromArgb(28, 28, 28),
                ForeColor  = Color.FromArgb(180, 220, 180),
                Font       = new Font("Cascadia Code", 9f),
                ReadOnly   = true,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Both,
                WordWrap    = false,
            };
            page.Controls.Add(_rtbPreview);
            return page;
        }

        // ════════════════════════════════════════════════════════════════════
        // Colunas do grid de instruções
        // ════════════════════════════════════════════════════════════════════
        void BuildInstructionColumns()
        {
            var typeCol = new DataGridViewComboBoxColumn
            {
                Name           = "Type",
                HeaderText     = "Instrução",
                FillWeight     = 15,
                DataSource     = InstructionTypes,
                DisplayStyle   = DataGridViewComboBoxDisplayStyle.ComboBox,
            };
            _gridInstructions.Columns.Add(typeCol);

            string[] cols =
            {
                "Enable/Instância", "Param1 (in/in1/IN)",
                "Param2 (in2/PT/PV)", "Param3 (DataType/SrcType)",
                "Param4 (DestType)", "Saída 1 (out/Q)", "Saída 2 (ET/CV)",
            };
            int[] weights = { 14, 14, 14, 12, 12, 13, 13 };
            for (int i = 0; i < cols.Length; i++)
                _gridInstructions.Columns.Add(new DataGridViewTextBoxColumn
                    { Name = $"P{i}", HeaderText = cols[i], FillWeight = weights[i] });
        }

        // ════════════════════════════════════════════════════════════════════
        // Variáveis
        // ════════════════════════════════════════════════════════════════════
        DataGridViewComboBoxColumn MakeDataTypeColumn()
        {
            var col = new DataGridViewComboBoxColumn
            {
                Name = "DataType", HeaderText = "Tipo", FillWeight = 25,
                DataSource = new[]
                {
                    "Bool","Byte","Word","DWord","LWord",
                    "SInt","Int","DInt","LInt","USInt","UInt","UDInt","ULInt",
                    "Real","LReal","Time","LTime","String","Char","Void",
                },
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            };
            return col;
        }

        List<InterfaceVar> CurrentVarList()
        {
            switch (_cmbVarSection.SelectedItem?.ToString())
            {
                case "Input":       return _def.Inputs;
                case "Output":      return _def.Outputs;
                case "InOut":       return _def.InOuts;
                case "Static (FB)": return _def.Statics;
                case "Temp":        return _def.Temps;
                default:            return _def.Inputs;
            }
        }

        void RefreshVarGrid()
        {
            _gridVars.Rows.Clear();
            foreach (var v in CurrentVarList())
                _gridVars.Rows.Add(v.Name, v.DataType, v.Default, v.Comment);
        }

        void SaveVarGrid()
        {
            var list = CurrentVarList();
            list.Clear();
            foreach (DataGridViewRow row in _gridVars.Rows)
            {
                var name = row.Cells["Name"]?.Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new InterfaceVar
                {
                    Name     = name,
                    DataType = row.Cells["DataType"]?.Value?.ToString() ?? "Bool",
                    Default  = row.Cells["Default"]?.Value?.ToString() ?? "",
                    Comment  = row.Cells["Comment"]?.Value?.ToString() ?? "",
                });
            }
        }

        void AddVar()
        {
            _gridVars.Rows.Add("Var" + (_gridVars.Rows.Count + 1), "Bool", "", "");
            SaveVarGrid();
        }

        void DelVar()
        {
            foreach (DataGridViewRow row in _gridVars.SelectedRows)
                if (!row.IsNewRow) _gridVars.Rows.Remove(row);
            SaveVarGrid();
        }

        // ════════════════════════════════════════════════════════════════════
        // Redes
        // ════════════════════════════════════════════════════════════════════
        void RefreshNetworkList()
        {
            int sel = _lstNetworks.SelectedIndex;
            _lstNetworks.BeginUpdate();
            _lstNetworks.Items.Clear();
            for (int i = 0; i < _def.Networks.Count; i++)
            {
                var n = _def.Networks[i];
                var title = string.IsNullOrWhiteSpace(n.Title) ? $"Network {i + 1}" : n.Title;
                _lstNetworks.Items.Add($"  {i + 1}.  {title}  [{n.Instructions.Count} inst.]");
            }
            _lstNetworks.EndUpdate();
            if (sel >= 0 && sel < _lstNetworks.Items.Count)
                _lstNetworks.SelectedIndex = sel;
            else if (_lstNetworks.Items.Count > 0)
                _lstNetworks.SelectedIndex = 0;
        }

        NetworkDef SelectedNetwork()
        {
            int idx = _lstNetworks.SelectedIndex;
            return (idx >= 0 && idx < _def.Networks.Count) ? _def.Networks[idx] : null;
        }

        void RefreshNetworkEditor()
        {
            var net = SelectedNetwork();
            bool hasNet = net != null;
            _txtNetTitle.Enabled   = hasNet;
            _txtNetComment.Enabled = hasNet;
            _gridInstructions.Enabled = hasNet;

            if (!hasNet) { _txtNetTitle.Text = ""; _txtNetComment.Text = ""; _gridInstructions.Rows.Clear(); return; }

            _txtNetTitle.Text   = net.Title;
            _txtNetComment.Text = net.Comment;

            _gridInstructions.CellValueChanged -= (s, e) => SaveInstructions();
            _gridInstructions.Rows.Clear();
            foreach (var inst in net.Instructions)
            {
                _gridInstructions.Rows.Add(
                    inst.InstructType,
                    inst.EnableSignal,
                    inst.Param1, inst.Param2, inst.Param3, inst.Param4,
                    inst.OutVar1, inst.OutVar2);
            }
            _gridInstructions.CellValueChanged += (s, e) => SaveInstructions();
        }

        void SaveNetworkMeta()
        {
            var net = SelectedNetwork();
            if (net == null) return;
            net.Title   = _txtNetTitle.Text;
            net.Comment = _txtNetComment.Text;
            RefreshNetworkList();
        }

        void SaveInstructions()
        {
            var net = SelectedNetwork();
            if (net == null) return;
            net.Instructions.Clear();
            foreach (DataGridViewRow row in _gridInstructions.Rows)
            {
                var tp = row.Cells["Type"]?.Value?.ToString();
                if (string.IsNullOrWhiteSpace(tp)) continue;
                net.Instructions.Add(new InstructionDef
                {
                    InstructType  = tp,
                    EnableSignal  = row.Cells["P0"]?.Value?.ToString() ?? "",
                    Param1        = row.Cells["P1"]?.Value?.ToString() ?? "",
                    Param2        = row.Cells["P2"]?.Value?.ToString() ?? "",
                    Param3        = row.Cells["P3"]?.Value?.ToString() ?? "",
                    Param4        = row.Cells["P4"]?.Value?.ToString() ?? "",
                    OutVar1       = row.Cells["P5"]?.Value?.ToString() ?? "",
                    OutVar2       = row.Cells["P6"]?.Value?.ToString() ?? "",
                });
            }
            RefreshNetworkList();
        }

        void AddNetwork()
        {
            _def.Networks.Add(new NetworkDef { Title = $"Network {_def.Networks.Count + 1}" });
            RefreshNetworkList();
            _lstNetworks.SelectedIndex = _def.Networks.Count - 1;
        }

        void DelNetwork()
        {
            int idx = _lstNetworks.SelectedIndex;
            if (idx < 0 || idx >= _def.Networks.Count) return;
            _def.Networks.RemoveAt(idx);
            RefreshNetworkList();
        }

        void MoveNetwork(int delta)
        {
            int idx = _lstNetworks.SelectedIndex;
            int newIdx = idx + delta;
            if (idx < 0 || newIdx < 0 || newIdx >= _def.Networks.Count) return;
            var tmp = _def.Networks[idx];
            _def.Networks[idx]    = _def.Networks[newIdx];
            _def.Networks[newIdx] = tmp;
            RefreshNetworkList();
            _lstNetworks.SelectedIndex = newIdx;
        }

        void AddInstruction()
        {
            var net = SelectedNetwork();
            if (net == null) { AddNetwork(); net = SelectedNetwork(); }
            _gridInstructions.Rows.Add("Move", "", "", "", "", "", "", "");
            SaveInstructions();
        }

        void DelInstruction()
        {
            foreach (DataGridViewRow row in _gridInstructions.SelectedRows)
                if (!row.IsNewRow) _gridInstructions.Rows.Remove(row);
            SaveInstructions();
        }

        void UpdateInstHelp()
        {
            if (_gridInstructions.CurrentRow == null) { _lblInstHelp.Text = ""; return; }
            var tp = _gridInstructions.CurrentRow.Cells["Type"]?.Value?.ToString() ?? "";
            int idx = Array.IndexOf(InstructionTypes, tp);
            _lblInstHelp.Text = idx >= 0 ? "  " + InstructionDescriptions[idx] : "";
        }

        // ════════════════════════════════════════════════════════════════════
        // Sync def ← UI
        // ════════════════════════════════════════════════════════════════════
        void SyncDefFromUI()
        {
            _def.Name        = _txtName.Text.Trim();
            _def.Description = _txtDesc.Text.Trim();
            _def.BlockType   = _cmbBlockType.SelectedItem?.ToString() == "FB"
                               ? PlcBlockType.FB : PlcBlockType.FC;
            if (int.TryParse(_txtNumber.Text, out int n)) _def.Number = n;
        }

        // ════════════════════════════════════════════════════════════════════
        // Gerar XML
        // ════════════════════════════════════════════════════════════════════
        void GenerateXml()
        {
            SyncDefFromUI();
            SaveVarGrid();
            SaveInstructions();

            try
            {
                _lastXml = FbdXmlGenerator.Generate(_def);
                _rtbPreview.Text = _lastXml;
                _tabs.SelectedIndex = 3; // vai para a tab Preview
                SetStatus("XML gerado com sucesso!", C_OK);
                UpdateImportButtons();
            }
            catch (Exception ex)
            {
                SetStatus($"Erro ao gerar XML: {ex.Message}", C_ERR);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Importar no TIA Portal
        // ════════════════════════════════════════════════════════════════════
        void ImportBlock()
        {
            if (_lastXml == null) { GenerateXml(); if (_lastXml == null) return; }

            if (_conn == null || _conn.Project == null)
            {
                SetStatus("Sem ligação ao TIA Portal — conecta primeiro no ecrã principal.", C_ERR);
                return;
            }

            _btnImport.Enabled   = false;
            _btnGenerate.Enabled = false;
            SetStatus("A importar bloco...", Color.Silver);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var importer = new BlockImporter(_conn);
                    var name = importer.ImportBlock(_lastXml);
                    SetStatus($"Bloco '{name}' importado!", C_OK);

                    if (_chkAddOb1.Checked)
                    {
                        Invoke((Action)(() => SetStatus($"A adicionar chamada no OB1...", Color.Silver)));
                        importer.AddCallToOb1(name, _def.BlockType.ToString());
                        SetStatus($"'{name}' importado + chamada no OB1 adicionada!", C_OK);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro ao importar: {ex.Message}", C_ERR);
                }
                finally
                {
                    Invoke((Action)(() =>
                    {
                        _btnImport.Enabled   = true;
                        _btnGenerate.Enabled = true;
                    }));
                }
            });
        }

        void UpdateImportButtons()
        {
            bool connected = _conn != null && _conn.Project != null;
            _btnImport.BackColor  = connected ? C_OK  : Color.FromArgb(60, 90, 60);
            _btnImport.ForeColor  = connected ? Color.White : Color.FromArgb(120, 140, 120);
            if (!connected)
                SetStatus("Sem ligação ao TIA Portal — só geração de XML disponível.", Color.Silver);
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers UI
        // ════════════════════════════════════════════════════════════════════
        void SetStatus(string msg, Color color)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStatus(msg, color))); return; }
            _lblStatus.Text      = msg;
            _lblStatus.ForeColor = color;
        }

        static TabPage DarkTab(string text)
        {
            return new TabPage(text)
            {
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(212, 212, 212),
            };
        }

        static Panel DarkPanel()
        {
            return new Panel { BackColor = Color.FromArgb(30, 30, 30), Dock = DockStyle.Fill };
        }

        static Label DarkLabel(string text, int x, int y)
        {
            return new Label
            {
                Text      = text,
                Location  = new Point(x, y),
                AutoSize  = true,
                ForeColor = Color.FromArgb(180, 180, 180),
            };
        }

        static TextBox DarkTextBox(int x, int y, int w)
        {
            return new TextBox
            {
                Location  = new Point(x, y),
                Width     = w,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(212, 212, 212),
                BorderStyle = BorderStyle.FixedSingle,
            };
        }

        static Button MakeBtn(string text, Color bg, int x, int y, int w = 120)
        {
            return new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(w, 30),
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f),
                Cursor    = Cursors.Hand,
            };
        }

        static Button MakeSmallBtn(string text, int x, int y, int w = 76)
        {
            return new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(w, 22),
                BackColor = Color.FromArgb(55, 55, 60),
                ForeColor = Color.FromArgb(212, 212, 212),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8f),
                Cursor    = Cursors.Hand,
            };
        }

        static void TabsDrawItem(object sender, DrawItemEventArgs e)
        {
            var tc     = (TabControl)sender;
            var bounds = tc.GetTabRect(e.Index);
            bool sel   = (e.State & DrawItemState.Selected) != 0;
            var bg     = sel ? Color.FromArgb(30, 30, 30) : Color.FromArgb(40, 40, 42);
            var fg     = sel ? Color.FromArgb(0, 150, 255) : Color.FromArgb(180, 180, 180);

            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, bounds);

            if (sel)
            {
                using var accentBrush = new SolidBrush(Color.FromArgb(0, 120, 212));
                e.Graphics.FillRectangle(accentBrush, new Rectangle(bounds.Left, bounds.Bottom - 2, bounds.Width, 2));
            }

            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var fgBrush = new SolidBrush(fg);
            e.Graphics.DrawString(tc.TabPages[e.Index].Text, tc.Font, fgBrush, bounds, fmt);
        }
    }
}
