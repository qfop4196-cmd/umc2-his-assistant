using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    public enum QuickTone
    {
        Idle,
        Ready,
        Check,
        Working
    }

    public sealed class QuickPanelState
    {
        public QuickPanelState()
        {
            ServerText = PatientText = MatchText = string.Empty;
        }

        public bool Connected { get; set; }
        public string ServerText { get; set; }
        public string PatientText { get; set; }
        public string MatchText { get; set; }
        public bool CanFill { get; set; }
        public bool CanConfirm { get; set; }
        public bool Busy { get; set; }
        public QuickTone Tone { get; set; }
    }

    /// <summary>
    /// Small always-on-top panel for doctors: shows the patient detected on HIS and whether an approved intake
    /// is ready, with one-click "Điền vào HIS" and "Đã lưu". Draggable; position is remembered.
    /// </summary>
    public sealed class QuickPanel : Form
    {
        private const int WmNcLButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private const int CsDropShadow = 0x20000;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly Panel header = new Panel();
        private readonly Label titleLabel = new Label();
        private readonly Label serverLabel = new Label();
        private readonly Label patientLabel = new Label();
        private readonly Label matchLabel = new Label();
        private readonly Button fillButton = new Button();
        private readonly Button savedButton = new Button();
        private readonly Timer moveTimer = new Timer { Interval = 600 };

        public QuickPanel()
        {
            Text = "UMC2 · Tờ khai BN";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(430, 178);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5F);
            Padding = new Padding(1);

            header.Dock = DockStyle.Top;
            header.Height = 34;
            header.Padding = new Padding(10, 0, 4, 0);
            titleLabel.Text = "UMC2 · Tờ khai BN";
            titleLabel.Dock = DockStyle.Left;
            titleLabel.AutoSize = false;
            titleLabel.Width = 150;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Font = new Font(Font, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            serverLabel.Dock = DockStyle.Fill;
            serverLabel.TextAlign = ContentAlignment.MiddleLeft;
            serverLabel.ForeColor = Color.FromArgb(230, 245, 250);
            serverLabel.AutoEllipsis = true;
            var expand = HeaderButton("⤢", "Mở cửa sổ trợ lý");
            expand.Click += delegate { Raise(ExpandRequested); };
            var hide = HeaderButton("—", "Ẩn bảng gọn (mở lại trong tab Tờ khai BN)");
            hide.Click += delegate { Raise(HideRequested); };
            header.Controls.Add(serverLabel);
            header.Controls.Add(titleLabel);
            header.Controls.Add(expand);
            header.Controls.Add(hide);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12, 8, 12, 10), BackColor = Color.White };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            patientLabel.Dock = DockStyle.Fill;
            patientLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            patientLabel.AutoEllipsis = true;
            patientLabel.TextAlign = ContentAlignment.MiddleLeft;
            body.Controls.Add(patientLabel, 0, 0);
            body.SetColumnSpan(patientLabel, 2);
            matchLabel.Dock = DockStyle.Fill;
            matchLabel.AutoEllipsis = true;
            matchLabel.TextAlign = ContentAlignment.MiddleLeft;
            body.Controls.Add(matchLabel, 0, 1);
            body.SetColumnSpan(matchLabel, 2);
            StyleAction(fillButton, "ĐIỀN VÀO HIS", Color.FromArgb(11, 110, 140));
            fillButton.Click += delegate { Raise(FillRequested); };
            StyleAction(savedButton, "✓ ĐÃ LƯU", Color.FromArgb(25, 116, 74));
            savedButton.Click += delegate { Raise(SavedRequested); };
            body.Controls.Add(fillButton, 0, 2);
            body.Controls.Add(savedButton, 1, 2);

            Controls.Add(body);
            Controls.Add(header);

            foreach (Control draggable in new Control[] { header, titleLabel, serverLabel, body, patientLabel, matchLabel })
                draggable.MouseDown += StartDrag;
            LocationChanged += delegate { moveTimer.Stop(); moveTimer.Start(); };
            moveTimer.Tick += delegate { moveTimer.Stop(); Raise(PositionChanged); };
            Apply(new QuickPanelState { PatientText = "Đang khởi động…" });
        }

        public event EventHandler FillRequested;
        public event EventHandler SavedRequested;
        public event EventHandler ExpandRequested;
        public event EventHandler HideRequested;
        public event EventHandler PositionChanged;

        public bool AllowClose { get; set; }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CsDropShadow;
                return cp;
            }
        }

        public void PlaceAt(int x, int y)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            if (x < 0 || y < 0)
            {
                Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
                return;
            }
            var visible = false;
            foreach (var screen in Screen.AllScreens)
                if (screen.WorkingArea.IntersectsWith(new Rectangle(x, y, Width, Height))) visible = true;
            Location = visible ? new Point(x, y) : new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        }

        public void Apply(QuickPanelState state)
        {
            Color strip;
            switch (state.Tone)
            {
                case QuickTone.Ready: strip = Color.FromArgb(25, 116, 74); break;
                case QuickTone.Check: strip = Color.FromArgb(166, 104, 0); break;
                case QuickTone.Working: strip = Color.FromArgb(47, 90, 168); break;
                default: strip = Color.FromArgb(11, 110, 140); break;
            }
            if (!state.Connected) strip = Color.FromArgb(120, 130, 138);
            header.BackColor = strip;
            BackColor = strip;
            serverLabel.Text = "● " + state.ServerText;
            patientLabel.Text = state.PatientText;
            matchLabel.Text = state.MatchText;
            matchLabel.ForeColor = state.Tone == QuickTone.Ready ? Color.FromArgb(25, 116, 74)
                : state.Tone == QuickTone.Check ? Color.FromArgb(138, 83, 0)
                : Color.FromArgb(60, 70, 80);
            fillButton.Enabled = state.CanFill && !state.Busy;
            savedButton.Enabled = state.CanConfirm && !state.Busy;
            fillButton.BackColor = fillButton.Enabled ? Color.FromArgb(11, 110, 140) : Color.FromArgb(200, 208, 214);
            savedButton.BackColor = savedButton.Enabled ? Color.FromArgb(25, 116, 74) : Color.FromArgb(200, 208, 214);
            UseWaitCursor = state.Busy;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Raise(HideRequested);
                return;
            }
            base.OnFormClosing(e);
        }

        private void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Button HeaderButton(string text, string tooltip)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                Width = 34,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            new ToolTip().SetToolTip(button, tooltip);
            return button;
        }

        private static void StyleAction(Button button, string text, Color color)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 4, 6, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
        }
    }
}
