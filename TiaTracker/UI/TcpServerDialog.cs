using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TiaTracker.Core.TcpServer;

namespace TiaTracker.UI
{
    public class TcpServerDialog : Form
    {
        // ── Theme ─────────────────────────────────────────────────────────────
        static readonly Color BG     = Color.FromArgb( 13,  17,  23);
        static readonly Color PANEL  = Color.FromArgb( 22,  27,  34);
        static readonly Color BORDER = Color.FromArgb( 48,  54,  61);
        static readonly Color TEXT   = Color.FromArgb(201, 209, 217);
        static readonly Color GREEN  = Color.FromArgb( 86, 211, 100);
        static readonly Color BLUE   = Color.FromArgb( 31, 111, 235);
        static readonly Color MUTED  = Color.FromArgb(125, 133, 144);
        static readonly Color SURFACE = Color.FromArgb(33, 38, 50);

        // ── Controls ──────────────────────────────────────────────────────────
        private NumericUpDown  _nudPort;
        private Button         _btnStart;
        private Button         _btnStop;
        private Label          _lblStatus;
        private ListBox        _lstClients;
        private DataGridView   _dgvTags;
        private RichTextBox    _rtbLog;
        private Button         _btnClose;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly PlcTcpServer _server = new PlcTcpServer();
        // tag name -> row index
        private readonly Dictionary<string, int> _tagRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public TcpServerDialog()
        {
            BuildUI();
            _server.DataReceived += OnDataReceived;
            _server.LogMessage   += OnLogMessage;
        }

        // ── UI Construction ───────────────────────────────────────────────────
        private void BuildUI()
        {
            Text          = "⬡ Servidor TCP — PLC Live Data";
            Size          = new Size(900, 620);
            MinimumSize   = new Size(700, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = TEXT;
            Font          = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;

            // ── Top toolbar ───────────────────────────────────────────────────
            var pnlTop = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = PANEL,
                Padding   = new Padding(10, 6, 10, 6)
            };

            var lblPort = new Label
            {
                Text      = "Porta:",
                AutoSize  = true,
                ForeColor = MUTED,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblPort.Location = new Point(10, 12);

            _nudPort = new NumericUpDown
            {
                Minimum   = 1,
                Maximum   = 65535,
                Value     = 2000,
                Width     = 70,
                BackColor = SURFACE,
                ForeColor = TEXT,
                Location  = new Point(60, 9)
            };

            _btnStart = new Button
            {
                Text      = "▶ Iniciar",
                Width     = 90,
                Height    = 28,
                Location  = new Point(144, 8),
                BackColor = BLUE,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnStart.FlatAppearance.BorderSize = 0;
            _btnStart.Click += BtnStart_Click;

            _btnStop = new Button
            {
                Text      = "■ Parar",
                Width     = 90,
                Height    = 28,
                Location  = new Point(242, 8),
                BackColor = Color.FromArgb(60, 40, 40),
                ForeColor = Color.FromArgb(240, 100, 80),
                FlatStyle = FlatStyle.Flat,
                Enabled   = false
            };
            _btnStop.FlatAppearance.BorderSize = 0;
            _btnStop.Click += BtnStop_Click;

            _lblStatus = new Label
            {
                Text      = "Offline",
                AutoSize  = true,
                ForeColor = MUTED,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location  = new Point(346, 13)
            };

            pnlTop.Controls.AddRange(new Control[] { lblPort, _nudPort, _btnStart, _btnStop, _lblStatus });

            // ── Middle split ──────────────────────────────────────────────────
            var splitter = new SplitContainer
            {
                Dock        = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor   = BG
            };
            Load += (s, e) =>
            {
                splitter.Panel1MinSize    = 140;
                splitter.Panel2MinSize    = 200;
                splitter.SplitterDistance = 220;
            };

            // Left: Ligações
            var grpClients = new GroupBox
            {
                Text      = "Ligações",
                Dock      = DockStyle.Fill,
                ForeColor = MUTED,
                BackColor = PANEL,
                Padding   = new Padding(6)
            };

            _lstClients = new ListBox
            {
                Dock      = DockStyle.Fill,
                BackColor = BG,
                ForeColor = GREEN,
                BorderStyle = BorderStyle.None,
                Font      = new Font("Consolas", 8.5f)
            };
            grpClients.Controls.Add(_lstClients);
            splitter.Panel1.Controls.Add(grpClients);
            splitter.Panel1.BackColor = PANEL;

            // Right: Tags
            var grpTags = new GroupBox
            {
                Text      = "Tags em Tempo Real",
                Dock      = DockStyle.Fill,
                ForeColor = MUTED,
                BackColor = PANEL,
                Padding   = new Padding(6)
            };

            _dgvTags = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                BackgroundColor       = BG,
                ForeColor             = TEXT,
                GridColor             = BORDER,
                BorderStyle           = BorderStyle.None,
                RowHeadersVisible     = false,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                ReadOnly              = true,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                Font                  = new Font("Consolas", 8.5f),
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill
            };

            // Enable double buffering via reflection to avoid flickering
            var dgvType = _dgvTags.GetType();
            var pi = dgvType.GetProperty("DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (pi != null) pi.SetValue(_dgvTags, true, null);

            // Columns
            _dgvTags.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name        = "colTag",
                HeaderText  = "Tag",
                FillWeight  = 35
            });
            _dgvTags.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name        = "colValue",
                HeaderText  = "Valor",
                FillWeight  = 30
            });
            _dgvTags.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name        = "colTime",
                HeaderText  = "Ultima atualizacao",
                FillWeight  = 35
            });

            // Dark theme for DGV
            _dgvTags.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = PANEL,
                ForeColor = MUTED,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _dgvTags.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor        = BG,
                ForeColor        = TEXT,
                SelectionBackColor = Color.FromArgb(31, 45, 63),
                SelectionForeColor = TEXT
            };
            _dgvTags.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor        = PANEL,
                ForeColor        = TEXT,
                SelectionBackColor = Color.FromArgb(31, 45, 63),
                SelectionForeColor = TEXT
            };
            _dgvTags.EnableHeadersVisualStyles = false;

            grpTags.Controls.Add(_dgvTags);
            splitter.Panel2.Controls.Add(grpTags);
            splitter.Panel2.BackColor = PANEL;

            // ── Bottom log ────────────────────────────────────────────────────
            var pnlBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 160,
                BackColor = BG,
                Padding   = new Padding(6, 4, 6, 4)
            };

            _rtbLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(13, 17, 23),
                ForeColor   = Color.FromArgb(139, 148, 158),
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Consolas", 8.5f),
                ScrollBars  = RichTextBoxScrollBars.Vertical
            };
            pnlBottom.Controls.Add(_rtbLog);

            var pnlBottomBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 38,
                BackColor = PANEL,
                Padding   = new Padding(6, 5, 6, 5)
            };

            _btnClose = new Button
            {
                Text      = "Fechar",
                Width     = 90,
                Height    = 28,
                Dock      = DockStyle.Right,
                BackColor = SURFACE,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat
            };
            _btnClose.FlatAppearance.BorderColor = BORDER;
            _btnClose.Click += (s, e) => Close();
            pnlBottomBar.Controls.Add(_btnClose);

            // ── Assembly order (bottom-up) ────────────────────────────────────
            Controls.Add(splitter);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottomBar);
            Controls.Add(pnlBottom);
        }

        // ── Event Handlers ────────────────────────────────────────────────────
        private async void BtnStart_Click(object sender, EventArgs e)
        {
            _btnStart.Enabled = false;
            _btnStop.Enabled  = true;
            _lblStatus.Text   = "Online";
            _lblStatus.ForeColor = GREEN;
            _nudPort.Enabled  = false;

            int port = (int)_nudPort.Value;
            await _server.StartAsync(port);
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            _server.Stop();
            _btnStart.Enabled = true;
            _btnStop.Enabled  = false;
            _lblStatus.Text   = "Offline";
            _lblStatus.ForeColor = MUTED;
            _nudPort.Enabled  = true;
        }

        private void OnDataReceived(string endpoint, Dictionary<string, TagValue> tags)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, Dictionary<string, TagValue>>(OnDataReceived), endpoint, tags);
                return;
            }

            // Add client to list if not already there
            if (!_lstClients.Items.Contains(endpoint))
                _lstClients.Items.Add(endpoint);

            // Update DataGridView rows
            _dgvTags.SuspendLayout();
            foreach (var kv in tags)
            {
                var tag = kv.Value;
                if (_tagRows.TryGetValue(tag.Name, out int rowIdx))
                {
                    _dgvTags.Rows[rowIdx].Cells["colValue"].Value = tag.Value;
                    _dgvTags.Rows[rowIdx].Cells["colTime"].Value  = tag.LastUpdate.ToString("HH:mm:ss.fff");
                    // timestamp cell muted color
                    _dgvTags.Rows[rowIdx].Cells["colTime"].Style.ForeColor = MUTED;
                }
                else
                {
                    int newRow = _dgvTags.Rows.Add(tag.Name, tag.Value, tag.LastUpdate.ToString("HH:mm:ss.fff"));
                    _dgvTags.Rows[newRow].Cells["colTime"].Style.ForeColor = MUTED;
                    _tagRows[tag.Name] = newRow;
                }
            }
            _dgvTags.ResumeLayout();
        }

        private void OnLogMessage(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnLogMessage), msg);
                return;
            }

            _rtbLog.AppendText(msg + Environment.NewLine);
            _rtbLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _server.Stop();
            base.OnFormClosing(e);
        }
    }
}
