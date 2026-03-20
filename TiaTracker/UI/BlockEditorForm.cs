using System;
using System.Drawing;
using System.Windows.Forms;
using TiaTracker.Core;
using TiaTracker.Core.BlockWriter;

namespace TiaTracker.UI
{
    /// <summary>
    /// Editor focado: criar FC/FB em SCL, criar DB, chamar na OB1.
    /// </summary>
    public class BlockEditorForm : Form
    {
        // ── Theme ────────────────────────────────────────────────────────────
        static readonly Color BG     = Color.FromArgb( 30,  30,  30);
        static readonly Color PANEL  = Color.FromArgb( 37,  37,  38);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_OK   = Color.FromArgb( 78, 160,  78);
        static readonly Color C_ERR  = Color.FromArgb(200,  60,  60);
        static readonly Color C_BLUE = Color.FromArgb(  0, 120, 212);
        static readonly Color C_PURP = Color.FromArgb( 90,  50, 140);
        static readonly Color C_GOLD = Color.FromArgb(180, 140,  50);
        static readonly Color C_GRAY = Color.FromArgb( 60,  60,  65);
        static readonly Color BORDER = Color.FromArgb( 60,  60,  60);

        // ── Controls ─────────────────────────────────────────────────────────
        private Panel       _leftPanel;
        private Panel       _rightPanel;
        private Label       _lblTitle;
        private TextBox     _txtName;
        private TextBox     _txtNumber;
        private ComboBox    _cmbFbRef;       // DB: referência ao FB
        private TextBox     _txtFbNumber;   // DB: número do FB
        private RichTextBox _rtbCode;        // editor SCL
        private Label       _lblStatus;
        private Button      _btnExecute;
        private Panel       _pnlDbOptions;
        private RadioButton _rdoInstanceDB, _rdoGlobalDB;

        private enum Mode { CreateFC, CreateFB, CreateDB, CallOB1 }
        private Mode _mode = Mode.CreateFC;
        private TiaConnection _conn;

        // ════════════════════════════════════════════════════════════════════
        public BlockEditorForm(TiaConnection conn)
        {
            _conn = conn;
            BuildUI();
            SetMode(Mode.CreateFC);
        }

        // ════════════════════════════════════════════════════════════════════
        void BuildUI()
        {
            Text          = "Danilo Tracker  —  Criar Blocos PLC";
            Size          = new Size(1050, 680);
            MinimumSize   = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = C_TEXT;
            Font          = new Font("Segoe UI", 9f);

            // ── Painel esquerdo: acções ──────────────────────────────────────
            _leftPanel = new Panel
            {
                Width     = 190,
                Dock      = DockStyle.Left,
                BackColor = PANEL,
                Padding   = new Padding(8, 12, 8, 8),
            };

            var lblActions = new Label
            {
                Text      = "O QUE CRIAR",
                Dock      = DockStyle.Top,
                Height    = 24,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font      = new Font("Segoe UI", 8f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var btnFC    = ActionBtn("  FC  —  Function",            C_BLUE,  Mode.CreateFC);
            var btnFB    = ActionBtn("  FB  —  Function Block",      C_PURP,  Mode.CreateFB);
            var btnDB    = ActionBtn("  DB  —  Data Block",          C_GOLD,  Mode.CreateDB);
            var btnCall  = ActionBtn("  CALL  —  Chamar na OB1",     C_OK,    Mode.CallOB1);

            var sep = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = BORDER, Margin = new Padding(0, 8, 0, 8) };

            _leftPanel.Controls.Add(btnCall);
            _leftPanel.Controls.Add(sep);
            _leftPanel.Controls.Add(btnDB);
            _leftPanel.Controls.Add(btnFB);
            _leftPanel.Controls.Add(btnFC);
            _leftPanel.Controls.Add(lblActions);

            // ── Painel direito: editor ───────────────────────────────────────
            _rightPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = BG,
                Padding   = new Padding(12, 10, 12, 10),
            };

            _lblTitle = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 32,
                Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = C_BLUE,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            // Campos nome + número
            var fieldsPanel = new Panel { Dock = DockStyle.Top, Height = 38 };

            var lblName = new Label { Text = "Nome:", Location = new Point(0, 10), AutoSize = true, ForeColor = Color.Silver };
            _txtName = new TextBox
            {
                Location  = new Point(50, 7),
                Width     = 220,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font      = new Font("Segoe UI", 10f),
            };

            var lblNum = new Label { Text = "Número:", Location = new Point(290, 10), AutoSize = true, ForeColor = Color.Silver };
            _txtNumber = new TextBox
            {
                Location  = new Point(355, 7),
                Width     = 70,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font      = new Font("Segoe UI", 10f),
            };

            fieldsPanel.Controls.AddRange(new Control[] { lblName, _txtName, lblNum, _txtNumber });

            // Opções DB (instância vs global + referência ao FB)
            _pnlDbOptions = new Panel { Dock = DockStyle.Top, Height = 70, Visible = false };

            _rdoInstanceDB = new RadioButton { Text = "DB de Instância (para FB)", Location = new Point(0,  6), AutoSize = true, Checked = true, ForeColor = C_TEXT };
            _rdoGlobalDB   = new RadioButton { Text = "DB Global (dados)",          Location = new Point(0, 28), AutoSize = true,                ForeColor = C_TEXT };
            _rdoInstanceDB.CheckedChanged += (s, e) => RefreshDbOptions();

            var lblFbRef   = new Label { Text = "FB:", Location = new Point(260, 8),  AutoSize = true, ForeColor = Color.Silver };
            _cmbFbRef = new ComboBox
            {
                Location      = new Point(285, 4),
                Width         = 200,
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor     = Color.FromArgb(45, 45, 48),
                ForeColor     = C_TEXT,
                Font          = new Font("Segoe UI", 9f),
            };

            var lblFbNum   = new Label { Text = "Nº FB:", Location = new Point(500, 8), AutoSize = true, ForeColor = Color.Silver };
            _txtFbNumber = new TextBox
            {
                Location    = new Point(548, 5),
                Width       = 60,
                BackColor   = Color.FromArgb(45, 45, 48),
                ForeColor   = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
            };

            _pnlDbOptions.Controls.AddRange(new Control[]
                { _rdoInstanceDB, _rdoGlobalDB, lblFbRef, _cmbFbRef, lblFbNum, _txtFbNumber });

            // Editor SCL
            _rtbCode = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(28, 28, 28),
                ForeColor   = Color.FromArgb(200, 220, 200),
                Font        = new Font("Cascadia Code", 10f),
                BorderStyle = BorderStyle.None,
                AcceptsTab  = true,
                WordWrap    = false,
                ScrollBars  = RichTextBoxScrollBars.Both,
            };
            _rtbCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Tab) { e.SuppressKeyPress = true; _rtbCode.SelectedText = "    "; }
            };

            _rightPanel.Controls.Add(_rtbCode);
            _rightPanel.Controls.Add(_pnlDbOptions);
            _rightPanel.Controls.Add(fieldsPanel);
            _rightPanel.Controls.Add(_lblTitle);

            // ── Barra de baixo ───────────────────────────────────────────────
            var bottomBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 46,
                BackColor = PANEL,
                Padding   = new Padding(10, 8, 10, 8),
            };

            _lblStatus = new Label
            {
                AutoSize  = false,
                Width     = 550,
                Height    = 30,
                Location  = new Point(10, 8),
                ForeColor = Color.Silver,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _btnExecute = new Button
            {
                Text      = "Executar",
                Location  = new Point(900, 8),
                Size      = new Size(130, 30),
                BackColor = C_OK,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
            };
            _btnExecute.Click += (s, e) => Execute();

            bottomBar.Controls.AddRange(new Control[] { _lblStatus, _btnExecute });

            Controls.Add(_rightPanel);
            Controls.Add(_leftPanel);
            Controls.Add(bottomBar);

            // Status inicial
            bool connected = _conn?.Project != null;
            if (!connected)
                SetStatus("Sem ligação ao TIA Portal — conecta primeiro para poder importar.", Color.FromArgb(180, 140, 60));
        }

        // ════════════════════════════════════════════════════════════════════
        // Mudar modo
        // ════════════════════════════════════════════════════════════════════
        void SetMode(Mode m)
        {
            _mode = m;

            _pnlDbOptions.Visible = (m == Mode.CreateDB);
            _rtbCode.Visible      = (m != Mode.CreateDB && m != Mode.CallOB1);

            switch (m)
            {
                case Mode.CreateFC:
                    _lblTitle.Text      = "Criar FC — Function (SCL)";
                    _lblTitle.ForeColor = C_BLUE;
                    _txtName.Text       = "FC_New";
                    _txtNumber.Text     = "100";
                    _rtbCode.Text       = SclTemplateFC();
                    _btnExecute.Text    = "▶  Criar FC no TIA Portal";
                    _btnExecute.BackColor = C_BLUE;
                    break;

                case Mode.CreateFB:
                    _lblTitle.Text      = "Criar FB — Function Block (SCL)";
                    _lblTitle.ForeColor = C_PURP;
                    _txtName.Text       = "FB_New";
                    _txtNumber.Text     = "1";
                    _rtbCode.Text       = SclTemplateFB();
                    _btnExecute.Text    = "▶  Criar FB no TIA Portal";
                    _btnExecute.BackColor = C_PURP;
                    break;

                case Mode.CreateDB:
                    _lblTitle.Text      = "Criar DB — Data Block";
                    _lblTitle.ForeColor = C_GOLD;
                    _txtName.Text       = "DB_New";
                    _txtNumber.Text     = "200";
                    PopulateFbList();
                    RefreshDbOptions();
                    _btnExecute.Text    = "▶  Criar DB no TIA Portal";
                    _btnExecute.BackColor = C_GOLD;
                    break;

                case Mode.CallOB1:
                    _lblTitle.Text      = "Adicionar CALL na OB1";
                    _lblTitle.ForeColor = C_OK;
                    _txtName.Text       = "FC_New";
                    _txtNumber.Text     = "100";
                    _rtbCode.Visible    = false;
                    _btnExecute.Text    = "▶  Adicionar CALL na OB1";
                    _btnExecute.BackColor = C_OK;
                    SetStatus("Preenche o Nome e Número do bloco a chamar.", Color.Silver);
                    break;
            }
        }

        void RefreshDbOptions()
        {
            bool isInstance = _rdoInstanceDB.Checked;
            _cmbFbRef.Visible   = isInstance;
            _txtFbNumber.Visible = isInstance;
            foreach (Control c in _pnlDbOptions.Controls)
                if (c is Label lbl && (lbl.Text == "FB:" || lbl.Text == "Nº FB:"))
                    lbl.Visible = isInstance;
        }

        void PopulateFbList()
        {
            _cmbFbRef.Items.Clear();
            _cmbFbRef.Items.Add("(escreve o nome do FB)");
            _cmbFbRef.SelectedIndex = 0;
            _txtFbNumber.Text = "1";
        }

        // ════════════════════════════════════════════════════════════════════
        // Templates SCL
        // ════════════════════════════════════════════════════════════════════
        static string SclTemplateFC() =>
@"// ──────────────────────────────────────────────────────────────
// FC gerada pelo Danilo Tracker
// ──────────────────────────────────────────────────────────────

// Declara as variáveis na interface (tab Interface do TIA Portal)
// ou directamente aqui em SCL:

VAR_INPUT
    Enable : Bool;
    Value  : Int;
END_VAR

VAR_OUTPUT
    Result : Int;
END_VAR

VAR_TEMP
    Tmp : Int;
END_VAR

BEGIN
    IF Enable THEN
        Tmp    := Value * 2;
        Result := Tmp;
    ELSE
        Result := 0;
    END_IF;
END_FUNCTION
";

        static string SclTemplateFB() =>
@"// ──────────────────────────────────────────────────────────────
// FB gerada pelo Danilo Tracker
// ──────────────────────────────────────────────────────────────

VAR_INPUT
    Enable : Bool;
    Setpoint : Real;
END_VAR

VAR_OUTPUT
    Running : Bool;
    Fault   : Bool;
END_VAR

VAR
    // Variáveis Static (persistentes entre chamadas)
    Timer1 : TON;
    Counter : Int;
END_VAR

VAR_TEMP
    Tmp : Real;
END_VAR

BEGIN
    // Lógica do bloco
    Timer1(IN := Enable, PT := T#5S);

    IF Timer1.Q THEN
        Counter := Counter + 1;
    END_IF;

    Running := Enable AND NOT Fault;
END_FUNCTION_BLOCK
";

        // ════════════════════════════════════════════════════════════════════
        // Executar acção
        // ════════════════════════════════════════════════════════════════════
        void Execute()
        {
            if (_conn?.Project == null)
            {
                SetStatus("Sem ligação ao TIA Portal — conecta primeiro.", C_ERR);
                return;
            }

            string name   = _txtName.Text.Trim();
            string numStr = _txtNumber.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            { SetStatus("Preenche o Nome do bloco.", C_ERR); return; }

            if (!int.TryParse(numStr, out int number))
            { SetStatus("Número inválido.", C_ERR); return; }

            _btnExecute.Enabled = false;
            SetStatus("A processar...", Color.Silver);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var importer = new BlockImporter(_conn);
                    string xml;

                    switch (_mode)
                    {
                        case Mode.CreateFC:
                            xml = SclBlockXmlGenerator.GenerateFC(name, number, _rtbCode.Text);
                            importer.ImportBlock(xml);
                            SetStatus($"FC '{name}' criada com sucesso!", C_OK);
                            break;

                        case Mode.CreateFB:
                            xml = SclBlockXmlGenerator.GenerateFB(name, number, _rtbCode.Text);
                            importer.ImportBlock(xml);
                            SetStatus($"FB '{name}' criada com sucesso!", C_OK);
                            break;

                        case Mode.CreateDB:
                            string fbName   = "";
                            int    fbNumber = 0;
                            bool   isInst   = false;
                            Invoke((Action)(() =>
                            {
                                isInst   = _rdoInstanceDB.Checked;
                                fbName   = _cmbFbRef.Text.Trim();
                                int.TryParse(_txtFbNumber.Text, out fbNumber);
                            }));

                            if (isInst)
                                xml = SclBlockXmlGenerator.GenerateInstanceDB(name, number, fbName, fbNumber);
                            else
                                xml = SclBlockXmlGenerator.GenerateGlobalDB(name, number);

                            importer.ImportBlock(xml);
                            SetStatus($"DB '{name}' criada com sucesso!", C_OK);
                            break;

                        case Mode.CallOB1:
                            string blockType = name.StartsWith("FB", StringComparison.OrdinalIgnoreCase) ? "FB" : "FC";
                            importer.AddCallToOb1(name, blockType);
                            SetStatus($"CALL '{name}' adicionado na OB1!", C_OK);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro: {ex.Message}", C_ERR);
                }
                finally
                {
                    Invoke((Action)(() => _btnExecute.Enabled = true));
                }
            });
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════
        void SetStatus(string msg, Color color)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStatus(msg, color))); return; }
            _lblStatus.Text      = msg;
            _lblStatus.ForeColor = color;
        }

        Button ActionBtn(string text, Color color, Mode mode)
        {
            var btn = new Button
            {
                Text      = text,
                Dock      = DockStyle.Top,
                Height    = 42,
                BackColor = Color.FromArgb(45, 45, 50),
                ForeColor = color,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 0, 0, 4),
            };
            btn.FlatAppearance.BorderColor = color;
            btn.FlatAppearance.BorderSize  = 1;
            btn.Click += (s, e) => SetMode(mode);
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(55, 55, 65);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(45, 45, 50);
            return btn;
        }
    }
}
