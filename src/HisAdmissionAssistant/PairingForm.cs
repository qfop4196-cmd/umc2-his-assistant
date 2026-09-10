using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    /// <summary>One-time pairing of this doctor's PC with the intake server using a 6-digit code from the admin page.</summary>
    public sealed class PairingForm : Form
    {
        private readonly ComboBox serverBox = new ComboBox();
        private readonly TextBox nameBox = new TextBox();
        private readonly TextBox codeBox = new TextBox();
        private readonly Button findButton = new Button();
        private readonly Button okButton = new Button();
        private readonly Label statusLabel = new Label();

        public PairingForm(string serverUrl, string deviceName)
        {
            Text = "Ghép nối với máy chủ tờ khai";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 330);
            Font = new Font("Segoe UI", 9.5F);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            for (var i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Text = "Quản trị viên tạo mã 6 số tại trang điều dưỡng → Quản trị → Máy bác sĩ. Mã dùng một lần, hết hạn sau 10 phút. " +
                    "Khóa của máy được lưu mã hóa cho tài khoản Windows này; không lưu dữ liệu bệnh nhân.",
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Margin = new Padding(0, 0, 0, 12),
                ForeColor = Color.FromArgb(70, 80, 90)
            };
            layout.Controls.Add(intro, 0, 0);
            layout.SetColumnSpan(intro, 3);

            layout.Controls.Add(Caption("Địa chỉ máy chủ"), 0, 1);
            serverBox.Dock = DockStyle.Fill;
            serverBox.DropDownStyle = ComboBoxStyle.DropDown;
            serverBox.Text = string.IsNullOrWhiteSpace(serverUrl) ? string.Empty : serverUrl;
            layout.Controls.Add(serverBox, 1, 1);
            findButton.Text = "Tự tìm";
            findButton.Dock = DockStyle.Fill;
            findButton.Click += delegate { Discover(); };
            layout.Controls.Add(findButton, 2, 1);

            layout.Controls.Add(Caption("Tên máy này"), 0, 2);
            nameBox.Dock = DockStyle.Fill;
            nameBox.Text = deviceName;
            nameBox.MaxLength = 60;
            layout.Controls.Add(nameBox, 1, 2);
            layout.SetColumnSpan(nameBox, 2);

            layout.Controls.Add(Caption("Mã ghép nối"), 0, 3);
            codeBox.Dock = DockStyle.Fill;
            codeBox.MaxLength = 7;
            codeBox.Font = new Font("Consolas", 18F, FontStyle.Bold);
            codeBox.TextAlign = HorizontalAlignment.Center;
            layout.Controls.Add(codeBox, 1, 3);

            statusLabel.AutoSize = true;
            statusLabel.MaximumSize = new Size(510, 0);
            statusLabel.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(statusLabel, 0, 4);
            layout.SetColumnSpan(statusLabel, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Width = 100, Height = 36 };
            okButton.Text = "Ghép nối";
            okButton.Width = 130;
            okButton.Height = 36;
            okButton.BackColor = Color.FromArgb(11, 110, 140);
            okButton.ForeColor = Color.White;
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.Click += delegate { Pair(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 3);

            Controls.Add(layout);
            AcceptButton = okButton;
            CancelButton = cancel;
            Shown += delegate
            {
                if (string.IsNullOrWhiteSpace(serverBox.Text)) Discover();
                else codeBox.Focus();
            };
        }

        public PairResult Result { get; private set; }
        public string ServerUrl { get; private set; }

        private static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        }

        private void Discover()
        {
            findButton.Enabled = false;
            statusLabel.ForeColor = Color.FromArgb(70, 80, 90);
            statusLabel.Text = "Đang tìm máy chủ trong mạng LAN…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                IList<DiscoveredServer> found;
                try { found = IntakeClient.Discover(1500); }
                catch (Exception) { found = new List<DiscoveredServer>(); }
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    findButton.Enabled = true;
                    serverBox.Items.Clear();
                    foreach (var server in found) serverBox.Items.Add(server);
                    if (found.Count > 0)
                    {
                        serverBox.SelectedIndex = 0;
                        statusLabel.Text = "Tìm thấy " + found.Count + " máy chủ. Nhập mã ghép nối rồi bấm Ghép nối.";
                        codeBox.Focus();
                    }
                    else
                    {
                        statusLabel.Text = "Không tự tìm thấy (mạng có thể chặn broadcast). Hãy nhập địa chỉ hiển thị ở trang quản trị, ví dụ http://192.168.1.20:8080";
                        serverBox.Focus();
                    }
                });
            });
        }

        private void Pair()
        {
            var selected = serverBox.SelectedItem as DiscoveredServer;
            var raw = selected != null && serverBox.Text == selected.ToString() ? selected.Url : serverBox.Text;
            string url;
            try { url = IntakeClient.NormalizeUrl(raw); }
            catch (InvalidOperationException ex)
            {
                ShowStatus(ex.Message, true);
                return;
            }
            var code = Regex.Replace(codeBox.Text ?? string.Empty, "[^0-9]", string.Empty);
            if (url.Length == 0) { ShowStatus("Nhập địa chỉ máy chủ.", true); return; }
            if (code.Length != 6) { ShowStatus("Mã ghép nối gồm 6 chữ số.", true); return; }
            var name = nameBox.Text.Trim();
            okButton.Enabled = false;
            ShowStatus("Đang ghép nối…", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                PairResult result = null;
                string error = null;
                try { result = new IntakeClient().Pair(url, code, name); }
                catch (Exception ex) { error = ex.Message; }
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    okButton.Enabled = true;
                    if (error != null || result == null || string.IsNullOrEmpty(result.Token))
                    {
                        ShowStatus(error ?? "Máy chủ không trả về khóa.", true);
                        return;
                    }
                    Result = result;
                    ServerUrl = url;
                    DialogResult = DialogResult.OK;
                    Close();
                });
            });
        }

        private void ShowStatus(string text, bool error)
        {
            statusLabel.ForeColor = error ? Color.FromArgb(179, 38, 30) : Color.FromArgb(70, 80, 90);
            statusLabel.Text = text;
        }
    }
}
