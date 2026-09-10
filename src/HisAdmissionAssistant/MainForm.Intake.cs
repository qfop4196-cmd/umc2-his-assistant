using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    /// <summary>
    /// "Tờ khai BN" workflow: patient fills the web form → nurse approves on the intake server →
    /// this PC detects which patient is open on HIS, shows the matching approved intake and fills the doctor's part.
    /// The doctor still reviews and presses Save on HIS; nothing is saved or signed automatically.
    /// </summary>
    public sealed partial class MainForm
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SwRestore = 9;

        private ConnectionSettings connection;
        private readonly IntakeClient intakeClient = new IntakeClient();
        private PatientContextWatcher watcher;
        private PatientContext patientContext;
        private List<IntakeMatch> intakeMatches = new List<IntakeMatch>();
        private IntakeMatch activeIntake;
        private string activeObservedId = string.Empty;
        private int activeFilledCount;
        private System.Windows.Forms.Timer intakeTimer;
        private volatile bool matchInFlight;
        private bool intakeBusy;
        private bool serverReachable;
        private string serverMessage = string.Empty;
        private QuickPanel quickPanel;
        private TabControl mainTabs;
        private string lastListContextKey = string.Empty;

        private readonly Label connectionLabel = new Label();
        private readonly Button pairButton = new Button();
        private readonly Button unpairButton = new Button();
        private readonly CheckBox autoDetectBox = new CheckBox();
        private readonly CheckBox showAllBox = new CheckBox();
        private readonly Panel patientBanner = new Panel();
        private readonly Label patientTitle = new Label();
        private readonly Label patientDetail = new Label();
        private readonly DataGridView matchGrid = new DataGridView();
        private readonly TextBox previewBox = new TextBox();
        private readonly Button intakeFillButton = new Button();
        private readonly Button intakeSavedButton = new Button();
        private readonly Button intakeReleaseButton = new Button();
        private readonly Label intakeStatus = new Label();

        // ------------------------------------------------------------------ UI
        private TabPage BuildIntakeTab()
        {
            var tab = new TabPage("Tờ khai BN");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Row 0: connection
            var conn = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            conn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++) conn.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connectionLabel.AutoSize = true;
            connectionLabel.Dock = DockStyle.Fill;
            connectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            connectionLabel.Padding = new Padding(4, 8, 4, 8);
            conn.Controls.Add(connectionLabel, 0, 0);
            autoDetectBox.Text = "Tự nhận diện BN trên HIS";
            autoDetectBox.AutoSize = true;
            autoDetectBox.Margin = new Padding(8, 9, 8, 4);
            autoDetectBox.CheckedChanged += delegate { OnAutoDetectChanged(); };
            conn.Controls.Add(autoDetectBox, 1, 0);
            pairButton.Text = "Ghép nối máy chủ…";
            pairButton.AutoSize = true;
            pairButton.Click += delegate { ShowPairingDialog(); };
            conn.Controls.Add(pairButton, 2, 0);
            unpairButton.Text = "Ngắt ghép nối";
            unpairButton.AutoSize = true;
            unpairButton.Click += delegate { Unpair(); };
            conn.Controls.Add(unpairButton, 3, 0);
            var compact = new Button { Text = "Bảng gọn luôn nổi", AutoSize = true };
            compact.Click += delegate { ShowQuickPanel(true); };
            conn.Controls.Add(compact, 4, 0);
            layout.Controls.Add(conn, 0, 0);

            // Row 1: patient banner
            patientBanner.Dock = DockStyle.Fill;
            patientBanner.Height = 70;
            patientBanner.Padding = new Padding(14, 8, 14, 8);
            patientBanner.Margin = new Padding(0, 0, 0, 8);
            patientTitle.Dock = DockStyle.Top;
            patientTitle.Height = 30;
            patientTitle.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
            patientTitle.AutoEllipsis = true;
            patientDetail.Dock = DockStyle.Fill;
            patientDetail.AutoEllipsis = true;
            var redetect = new Button { Text = "Nhận diện lại", Dock = DockStyle.Right, Width = 120 };
            redetect.Click += delegate { RedetectNow(); };
            patientBanner.Controls.Add(patientDetail);
            patientBanner.Controls.Add(patientTitle);
            patientBanner.Controls.Add(redetect);
            layout.Controls.Add(patientBanner, 0, 1);

            // Row 2: list header
            var listHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            listHeader.Controls.Add(new Label
            {
                Text = "Tờ khai đã được điều dưỡng duyệt cho bệnh nhân này",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(0, 6, 0, 4)
            }, 0, 0);
            showAllBox.Text = "Hiện tất cả tờ khai đã duyệt (48 giờ)";
            showAllBox.AutoSize = true;
            showAllBox.CheckedChanged += delegate { RequestMatches(); };
            listHeader.Controls.Add(showAllBox, 1, 0);
            layout.Controls.Add(listHeader, 0, 2);

            // Row 3: matches grid
            ConfigureMatchGrid();
            layout.Controls.Add(matchGrid, 0, 3);

            // Row 4: preview
            var previewGroup = new GroupBox { Text = "Nội dung sẽ điền vào HIS (chỉ nằm trong RAM)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            previewBox.Dock = DockStyle.Fill;
            previewBox.Multiline = true;
            previewBox.ReadOnly = true;
            previewBox.ScrollBars = ScrollBars.Vertical;
            previewBox.BackColor = Color.White;
            previewBox.Font = new Font("Segoe UI", 9.5F);
            previewGroup.Controls.Add(previewBox);
            layout.Controls.Add(previewGroup, 0, 4);

            // Row 5: actions
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            intakeStatus.Dock = DockStyle.Fill;
            intakeStatus.TextAlign = ContentAlignment.MiddleLeft;
            intakeStatus.AutoEllipsis = true;
            actions.Controls.Add(intakeStatus, 0, 0);
            StyleButton(intakeReleaseButton, "Trả lại hàng chờ", Color.White, Color.FromArgb(40, 40, 40));
            intakeReleaseButton.Click += delegate { ReleaseActiveIntake(true); };
            actions.Controls.Add(intakeReleaseButton, 1, 0);
            StyleButton(intakeSavedButton, "✓ ĐÃ LƯU TRÊN HIS", Color.FromArgb(25, 116, 74), Color.White);
            intakeSavedButton.Click += delegate { ConfirmIntakeSaved(); };
            actions.Controls.Add(intakeSavedButton, 2, 0);
            StyleButton(intakeFillButton, "ĐIỀN VÀO HIS", Color.FromArgb(11, 110, 140), Color.White);
            intakeFillButton.Click += delegate { FillSelectedIntake(); };
            actions.Controls.Add(intakeFillButton, 3, 0);
            layout.Controls.Add(actions, 0, 5);

            tab.Controls.Add(layout);
            return tab;
        }

        private static void StyleButton(Button button, string text, Color back, Color fore)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(text.Length > 14 ? 190 : 150, 44);
            button.Margin = new Padding(6, 0, 0, 0);
            button.BackColor = back;
            button.ForeColor = fore;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = back == Color.White ? Color.FromArgb(190, 200, 208) : back;
            button.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        }

        private void ConfigureMatchGrid()
        {
            matchGrid.Dock = DockStyle.Fill;
            matchGrid.AllowUserToAddRows = false;
            matchGrid.AllowUserToDeleteRows = false;
            matchGrid.ReadOnly = true;
            matchGrid.MultiSelect = false;
            matchGrid.RowHeadersVisible = false;
            matchGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            matchGrid.BackgroundColor = Color.White;
            matchGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Match", HeaderText = "Mức khớp", FillWeight = 80 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Mã tờ khai", FillWeight = 45 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Họ tên", FillWeight = 80 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Birth", HeaderText = "NS", FillWeight = 25 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "HisId", HeaderText = "Mã BN (ĐD xác nhận)", FillWeight = 55 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "Lý do khám", FillWeight = 110 });
            matchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Approved", HeaderText = "Duyệt bởi", FillWeight = 70 });
            matchGrid.SelectionChanged += delegate { UpdatePreview(); UpdateIntakeButtons(); };
            matchGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) FillSelectedIntake(); };
        }

        // ------------------------------------------------------------------ lifecycle
        private void InitIntake()
        {
            connection = ConnectionSettings.Load();
            intakeClient.BaseUrl = connection.ServerUrl;
            intakeClient.Token = connection.Token;
            autoDetectBox.Checked = connection.AutoDetect;

            watcher = new PatientContextWatcher(automation);
            watcher.ContextChanged += OnWatcherContextChanged;
            watcher.SetProfiles(profileStore.LoadAll());
            if (connection.AutoDetect) watcher.Start();

            intakeTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            intakeTimer.Tick += delegate { RequestMatches(); };
            intakeTimer.Start();

            UpdateConnectionUi();
            UpdatePatientBanner();
            UpdateIntakeButtons();
            if (connection.IsPaired)
            {
                RequestMatches();
                if (connection.QuickPanel) ShowQuickPanel(false);
            }
            else if (mainTabs != null)
            {
                intakeStatus.Text = "Bắt đầu: bấm \"Ghép nối máy chủ…\" và nhập mã 6 số do quản trị viên cấp.";
            }
        }

        private void ShutdownIntake(FormClosingEventArgs e)
        {
            if (activeIntake != null && e != null && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(
                    "Tờ khai " + activeIntake.Code + " đã được điền vào HIS nhưng chưa xác nhận \"Đã lưu trên HIS\".\r\n\r\n" +
                    "Yes = Đã lưu trên HIS (đánh dấu hoàn tất)\r\nNo = Trả lại hàng chờ\r\nCancel = Không đóng ứng dụng",
                    "Tờ khai đang xử lý", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes) ConfirmIntakeSaved();
                else ReleaseActiveIntake(false);
            }
            if (intakeTimer != null) intakeTimer.Stop();
            if (watcher != null) watcher.Stop();
            if (quickPanel != null && !quickPanel.IsDisposed)
            {
                SaveQuickPanelPosition();
                quickPanel.AllowClose = true;
                quickPanel.Close();
            }
        }

        private void RefreshWatcherProfiles()
        {
            if (watcher != null) watcher.SetProfiles(profileStore.LoadAll());
        }

        private void OnAutoDetectChanged()
        {
            if (connection == null || watcher == null) return;
            connection.AutoDetect = autoDetectBox.Checked;
            SaveConnection();
            if (autoDetectBox.Checked)
            {
                watcher.Start();
                watcher.DetectNow();
            }
            else
            {
                watcher.Stop();
                patientContext = null;
                UpdatePatientBanner();
                RequestMatches();
            }
        }

        private void RedetectNow()
        {
            if (watcher == null) return;
            if (!watcher.IsRunning)
            {
                autoDetectBox.Checked = true;
                return;
            }
            intakeStatus.Text = "Đang nhận diện… hãy chuyển sang cửa sổ HIS đang mở hồ sơ BN.";
            watcher.DetectNow();
        }

        private void OnWatcherContextChanged(object sender, PatientContextEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (intakeBusy) return;
                    patientContext = e.Context;
                    UpdatePatientBanner();
                    if (activeIntake != null && patientContext != null && !SameId(patientContext.PatientId, activeObservedId))
                        intakeStatus.Text = "Lưu ý: tờ khai " + activeIntake.Code + " (BN " + activeObservedId +
                            ") đã điền nhưng chưa bấm \"Đã lưu trên HIS\". Quay lại hồ sơ đó để kiểm tra và xác nhận.";
                    RequestMatches();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        // ------------------------------------------------------------------ server
        private void SaveConnection()
        {
            try { connection.Save(); }
            catch (Exception ex) { Log("Không lưu được cấu hình kết nối: " + ex.Message); }
        }

        private void ShowPairingDialog()
        {
            using (var dialog = new PairingForm(connection.ServerUrl, string.IsNullOrEmpty(connection.DeviceName) ? Environment.MachineName : connection.DeviceName))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;
                connection.ServerUrl = dialog.ServerUrl;
                connection.Token = dialog.Result.Token;
                connection.DeviceId = dialog.Result.DeviceId;
                connection.DeviceName = dialog.Result.DeviceName;
                connection.HospitalName = dialog.Result.HospitalName;
                SaveConnection();
                intakeClient.BaseUrl = connection.ServerUrl;
                intakeClient.Token = connection.Token;
                serverReachable = true;
                serverMessage = string.Empty;
                Log("Đã ghép nối với máy chủ tờ khai (" + connection.ServerUrl + ") với tên máy " + connection.DeviceName + ".");
                UpdateConnectionUi();
                RequestMatches();
                ShowQuickPanel(false);
            }
        }

        private void Unpair()
        {
            if (!connection.IsPaired) return;
            if (MessageBox.Show("Ngắt ghép nối máy này khỏi máy chủ tờ khai?\r\nMuốn dùng lại cần mã ghép nối mới.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            connection.ForgetPairing();
            SaveConnection();
            intakeClient.Token = string.Empty;
            intakeMatches.Clear();
            PopulateMatches();
            UpdateConnectionUi();
            Log("Đã ngắt ghép nối máy chủ tờ khai.");
        }

        private void UpdateConnectionUi()
        {
            if (connection == null) return;
            if (!connection.IsPaired)
            {
                connectionLabel.Text = "● Chưa ghép nối máy chủ tờ khai";
                connectionLabel.ForeColor = Color.FromArgb(139, 72, 0);
            }
            else if (!serverReachable && serverMessage.Length > 0)
            {
                connectionLabel.Text = "● Mất kết nối: " + serverMessage;
                connectionLabel.ForeColor = Color.FromArgb(179, 38, 30);
            }
            else
            {
                connectionLabel.Text = "● " + (string.IsNullOrEmpty(connection.HospitalName) ? "Máy chủ tờ khai" : connection.HospitalName) +
                    "  —  " + connection.ServerUrl + "  —  máy: " + connection.DeviceName;
                connectionLabel.ForeColor = Color.FromArgb(25, 116, 74);
            }
            unpairButton.Enabled = connection.IsPaired;
            pairButton.Text = connection.IsPaired ? "Ghép nối lại…" : "Ghép nối máy chủ…";
            UpdateQuickPanel();
        }

        private void RequestMatches()
        {
            if (connection == null || !connection.IsPaired || matchInFlight || intakeBusy) return;
            var context = patientContext;
            var showAll = showAllBox.Checked || context == null;
            matchInFlight = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                IList<IntakeMatch> result = null;
                Exception error = null;
                try
                {
                    var list = new List<IntakeMatch>();
                    if (context != null) list.AddRange(intakeClient.Match(context.PatientId, context.PatientName, context.BirthYear));
                    if (showAll)
                        foreach (var item in intakeClient.Recent())
                            if (!list.Any(m => m.Id == item.Id)) list.Add(item);
                    result = list;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    matchInFlight = false;
                }
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate { OnMatchesLoaded(context, result, error); });
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnMatchesLoaded(PatientContext context, IList<IntakeMatch> result, Exception error)
        {
            if (error != null)
            {
                var api = error as IntakeApiException;
                serverReachable = false;
                serverMessage = error.Message;
                if (api != null && api.IsUnauthorized)
                {
                    serverMessage = "Khóa máy đã bị thu hồi — hãy ghép nối lại.";
                    connection.ForgetPairing();
                    SaveConnection();
                    intakeClient.Token = string.Empty;
                }
                UpdateConnectionUi();
                return;
            }
            var wasOffline = !serverReachable;
            serverReachable = true;
            serverMessage = string.Empty;
            if (wasOffline) UpdateConnectionUi();
            if (!ReferenceEquals(context, patientContext))
            {
                RequestMatches(); // a newer context arrived meanwhile: ask again for the current patient
                return;
            }
            var selectedId = SelectedIntake() == null ? null : SelectedIntake().Id;
            var contextKey = context == null ? string.Empty : context.Key;
            if (contextKey != lastListContextKey) selectedId = null; // new patient: pick the best match again
            lastListContextKey = contextKey;
            intakeMatches = result == null ? new List<IntakeMatch>() : result.ToList();
            PopulateMatches();
            if (selectedId != null) SelectIntakeRow(selectedId);
            else if (intakeMatches.Count > 0 && intakeMatches[0].Score >= 90) SelectIntakeRow(intakeMatches[0].Id);
            UpdatePreview();
            UpdateIntakeButtons();
        }

        private void PopulateMatches()
        {
            matchGrid.Rows.Clear();
            foreach (var match in intakeMatches)
            {
                string matchText;
                if (match.Score >= 100) matchText = "✔ Đúng mã BN";
                else if (match.Score >= 90) matchText = "≈ " + match.MatchReason;
                else if (match.Score > 0) matchText = "? " + match.MatchReason;
                else matchText = "Chọn thủ công";
                if (match.Status == "claimed" && !string.IsNullOrEmpty(match.ClaimedBy)) matchText += " · đang ở máy " + match.ClaimedBy;
                var index = matchGrid.Rows.Add(matchText, match.Code, match.FullName, match.BirthYear > 0 ? match.BirthYear.ToString() : string.Empty,
                    match.HisPatientId, match.ChiefComplaint, match.ApprovedBy + " " + match.ApprovedClock);
                var row = matchGrid.Rows[index];
                row.Tag = match;
                if (match.Score >= 100) row.DefaultCellStyle.BackColor = Color.FromArgb(226, 244, 233);
                else if (match.Score >= 90) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 219);
            }
            matchGrid.ClearSelection();
        }

        private void SelectIntakeRow(string id)
        {
            foreach (DataGridViewRow row in matchGrid.Rows)
            {
                var match = row.Tag as IntakeMatch;
                if (match == null || match.Id != id) continue;
                row.Selected = true;
                matchGrid.CurrentCell = row.Cells[0];
                return;
            }
        }

        private IntakeMatch SelectedIntake()
        {
            return matchGrid.SelectedRows.Count == 0 ? null : matchGrid.SelectedRows[0].Tag as IntakeMatch;
        }

        // ------------------------------------------------------------------ display
        private void UpdatePatientBanner()
        {
            if (patientContext == null)
            {
                patientBanner.BackColor = Color.FromArgb(241, 244, 247);
                patientTitle.ForeColor = Color.FromArgb(70, 80, 90);
                patientTitle.Text = autoDetectBox.Checked ? "Chưa thấy hồ sơ bệnh nhân trên HIS" : "Tự nhận diện đang tắt";
                patientDetail.Text = autoDetectBox.Checked
                    ? "Mở màn hình Khám bệnh / Phiếu khám vào viện của bệnh nhân trên HIS. Trợ lý chỉ đọc mã BN, không ghi gì cho tới khi bạn bấm Điền."
                    : "Bật \"Tự nhận diện BN trên HIS\" hoặc chọn tờ khai trong danh sách tất cả tờ khai đã duyệt.";
            }
            else
            {
                patientBanner.BackColor = Color.FromArgb(228, 241, 245);
                patientTitle.ForeColor = Color.FromArgb(9, 76, 97);
                patientTitle.Text = "BN đang mở trên HIS: " + patientContext.Describe();
                patientDetail.Text = "Màn hình: " + patientContext.Profile.Name + "  ·  " + patientContext.Window.ProcessName +
                    " (PID " + patientContext.Window.ProcessId + ")";
            }
            UpdateQuickPanel();
        }

        private void UpdatePreview()
        {
            var match = SelectedIntake();
            if (match == null)
            {
                previewBox.Text = intakeMatches.Count == 0
                    ? (patientContext == null ? string.Empty : "Không có tờ khai đã duyệt cho bệnh nhân này. Nếu BN đã khai, nhờ điều dưỡng kiểm tra mã BN trên trang duyệt.")
                    : "Chọn một tờ khai để xem nội dung.";
                return;
            }
            var profile = patientContext != null ? patientContext.Profile : currentProfile;
            var sb = new StringBuilder();
            sb.AppendLine("Tờ khai " + match.Code + " — " + match.FullName + (match.BirthYear > 0 ? " (" + match.BirthYear + ")" : string.Empty) +
                (string.IsNullOrEmpty(match.HisPatientId) ? "  ·  CHƯA có mã BN do điều dưỡng xác nhận" : "  ·  Mã BN " + match.HisPatientId));
            if (match.Fields.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("(Nội dung sẽ tải khi bấm Điền vào HIS.)");
                previewBox.Text = sb.ToString();
                return;
            }
            sb.AppendLine();
            if (profile != null)
            {
                foreach (var field in profile.Fields)
                {
                    if (string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase) || UiaAutomationService.IsReadOnly(field)) continue;
                    string value;
                    if (!match.Fields.TryGetValue(field.Key, out value) || string.IsNullOrWhiteSpace(value)) continue;
                    sb.AppendLine("■ " + field.Label + ":");
                    sb.AppendLine("   " + value.Replace("\n", "\r\n   "));
                }
                var unused = match.Fields.Keys.Where(k => k != "PatientId" && !profile.Fields.Any(f => string.Equals(f.Key, k, StringComparison.OrdinalIgnoreCase))).ToList();
                if (unused.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("(Màn hình hiện tại không có ô cho: " + string.Join(", ", unused.ToArray()) + ")");
                }
            }
            previewBox.Text = sb.ToString();
        }

        private void UpdateIntakeButtons()
        {
            var paired = connection != null && connection.IsPaired;
            var selected = SelectedIntake();
            intakeFillButton.Enabled = paired && !intakeBusy && activeIntake == null && selected != null && patientContext != null;
            intakeSavedButton.Enabled = paired && !intakeBusy && activeIntake != null;
            intakeReleaseButton.Enabled = paired && !intakeBusy && activeIntake != null;
            if (!intakeBusy && activeIntake == null && paired)
            {
                if (patientContext == null) intakeStatus.Text = "Mở hồ sơ bệnh nhân trên HIS để trợ lý tìm tờ khai tương ứng.";
                else if (intakeMatches.Count == 0) intakeStatus.Text = "Không có tờ khai đã duyệt cho BN " + patientContext.PatientId + ".";
                else if (selected == null) intakeStatus.Text = "Chọn tờ khai rồi bấm ĐIỀN VÀO HIS.";
                else intakeStatus.Text = "Sẵn sàng điền tờ khai " + selected.Code + " vào " + patientContext.Profile.Name + ".";
            }
            UpdateQuickPanel();
        }

        // ------------------------------------------------------------------ fill / complete
        private void FillSelectedIntake()
        {
            var match = SelectedIntake();
            var context = patientContext;
            if (match == null || context == null || intakeBusy || activeIntake != null) return;

            // 1. Re-read the patient ID right now: the doctor may have switched patients since the last poll.
            var profile = FindLoadedProfile(context.Profile.Name) ?? context.Profile;
            var idField = profile.Fields.FirstOrDefault(PatientContextWatcher.IsPatientIdField);
            string observed = null;
            try
            {
                var element = idField == null ? null : automation.Locate(context.Window.Element, idField);
                if (element != null) automation.TryRead(element, out observed);
            }
            catch (Exception ex)
            {
                ShowError("Không đọc lại được mã BN trên HIS: " + ex.Message);
                return;
            }
            observed = (observed ?? string.Empty).Trim();
            if (observed.Length == 0 || !SameId(observed, context.PatientId))
            {
                MessageBox.Show("Hồ sơ trên HIS vừa thay đổi. Hãy mở đúng bệnh nhân rồi thử lại.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                watcher.DetectNow();
                return;
            }
            if (match.HisPatientId.Length > 0 && !SameId(match.HisPatientId, observed))
            {
                MessageBox.Show("Mã BN điều dưỡng xác nhận (" + match.HisPatientId + ") KHÁC mã BN đang mở trên HIS (" + observed + ").\r\nĐã dừng, không điền.",
                    "Không khớp bệnh nhân", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }
            if (match.Score < 100)
            {
                var question = "Tờ khai " + match.Code + ": " + match.FullName + (match.BirthYear > 0 ? " (" + match.BirthYear + ")" : string.Empty) + "\r\n" +
                    "Bệnh nhân đang mở trên HIS: " + context.Describe() + "\r\n\r\n" +
                    "Điều dưỡng chưa gắn mã BN cho tờ khai này. Bạn xác nhận đây là CÙNG MỘT NGƯỜI?";
                if (MessageBox.Show(question, "Xác nhận đúng bệnh nhân", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            SetIntakeBusy(true, "Đang nhận tờ khai " + match.Code + "…");
            IntakeMatch claimed;
            try
            {
                try
                {
                    claimed = intakeClient.Claim(match.Id, observed, false);
                }
                catch (IntakeApiException ex)
                {
                    if (ex.Code != "claimed_elsewhere") throw;
                    if (MessageBox.Show(ex.Message + "\r\nVẫn nhận về máy này?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        SetIntakeBusy(false, "Đã hủy.");
                        return;
                    }
                    claimed = intakeClient.Claim(match.Id, observed, true);
                }
            }
            catch (Exception ex)
            {
                SetIntakeBusy(false, "Không nhận được tờ khai.");
                ShowError(ex.Message);
                RequestMatches();
                return;
            }

            // 2. Load values into the real profile shown in the "Nhập liệu" grid (RAM only).
            SelectProfileByName(profile.Name);
            if (currentProfile == null || currentProfile.Name != profile.Name)
            {
                SafeRelease(claimed.Id);
                SetIntakeBusy(false, "Không mở được profile \"" + profile.Name + "\" — hãy bấm Nạp profile rồi thử lại.");
                return;
            }
            var fields = currentProfile.Fields;
            foreach (var field in fields)
            {
                string value;
                if (string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase))
                {
                    // Verify against the nurse-confirmed ID when there is one (already checked equal to the screen above).
                    field.Value = field.Key.Equals("PatientId", StringComparison.OrdinalIgnoreCase)
                        ? (claimed.HisPatientId.Length > 0 ? claimed.HisPatientId : observed)
                        : string.Empty;
                    field.EnabledForFill = field.Value.Length > 0;
                }
                else if (UiaAutomationService.IsReadOnly(field))
                {
                    field.Value = string.Empty;
                    field.EnabledForFill = false;
                }
                else
                {
                    field.Value = claimed.Fields.TryGetValue(field.Key, out value) ? value : string.Empty;
                    field.EnabledForFill = !string.IsNullOrWhiteSpace(field.Value);
                }
            }
            PopulateFieldGrid();
            var toFill = fields.Count(f => f.EnabledForFill && !string.Equals(f.Operation, "Verify", StringComparison.OrdinalIgnoreCase));
            if (toFill == 0)
            {
                SafeRelease(claimed.Id);
                SetIntakeBusy(false, "Tờ khai không có nội dung phù hợp với màn hình này.");
                ClearSensitiveValues();
                return;
            }

            // 3. Never silently overwrite what the doctor already typed on HIS.
            watcher.Paused = true;
            try
            {
                var existing = automation.ReadExistingValues(context.Window.Element, fields);
                if (existing.Count > 0)
                {
                    var choice = AskOverwrite(existing);
                    if (choice == DialogResult.Cancel)
                    {
                        SafeRelease(claimed.Id);
                        SetIntakeBusy(false, "Đã hủy, không điền.");
                        ClearSensitiveValues();
                        return;
                    }
                    if (choice == DialogResult.No)
                        foreach (var field in existing.Keys) field.EnabledForFill = false;
                }

                // 4. Fill with the existing safety engine: preflight all selectors + verify patient ID, no Save click.
                var results = automation.Apply(context.Window.Element, fields);
                ApplyResults(results);
                var changed = results.Count(r => r.Changed);
                var failed = results.Where(r => !r.Found || r.Message.StartsWith("Không") || r.Message.StartsWith("Lỗi") || r.Message.StartsWith("Thiếu")).ToList();
                Log(string.Format("Tờ khai {0}: điền {1} trường vào PID {2}; {3} mục cần kiểm tra (không ghi nội dung vào log).",
                    claimed.Code, changed, context.Window.ProcessId, failed.Count));
                if (changed == 0)
                {
                    SafeRelease(claimed.Id);
                    SetIntakeBusy(false, "Chưa điền được trường nào — xem tab Nhập liệu.");
                    MessageBox.Show("Không điền được: " + string.Join("; ", failed.Select(f => f.Field.Label + ": " + f.Message).Take(6).ToArray()) +
                        "\r\n\r\nTờ khai đã được trả về hàng chờ. Có thể cần hiệu chỉnh selector (tab Hiệu chỉnh UIA).",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ClearSensitiveValues();
                    return;
                }
                activeIntake = claimed;
                activeObservedId = observed;
                activeFilledCount = changed;
                SetIntakeBusy(false, string.Format("Đã điền {0} trường{1}. Kiểm tra trên HIS, bấm Lưu trên HIS, rồi bấm \"Đã lưu trên HIS\".",
                    changed, failed.Count > 0 ? " (" + failed.Count + " mục cần xem lại)" : string.Empty));
                BringHisToFront(context.Window);
            }
            finally
            {
                watcher.Paused = false;
            }
        }

        private DialogResult AskOverwrite(IDictionary<FieldMapping, string> existing)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Các ô sau trên HIS ĐÃ CÓ nội dung:");
            foreach (var pair in existing.Take(8))
            {
                var current = pair.Value.Replace("\r", " ").Replace("\n", " ");
                if (current.Length > 60) current = current.Substring(0, 57) + "…";
                sb.AppendLine("  • " + pair.Key.Label + ": \"" + current + "\"");
            }
            sb.AppendLine();
            sb.AppendLine("Yes = Ghi đè bằng nội dung tờ khai");
            sb.AppendLine("No = Chỉ điền các ô còn trống (khuyên dùng)");
            sb.AppendLine("Cancel = Không điền");
            return MessageBox.Show(sb.ToString(), "HIS đã có dữ liệu", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        }

        private void ConfirmIntakeSaved()
        {
            if (activeIntake == null) return;
            var intake = activeIntake;
            try
            {
                intakeClient.Complete(intake.Id, "completed", "Bác sĩ xác nhận đã kiểm tra và lưu trên HIS.", activeObservedId, activeFilledCount);
                Log("Đã hoàn tất tờ khai " + intake.Code + ".");
            }
            catch (Exception ex)
            {
                ShowError("Chưa báo được hoàn tất cho máy chủ: " + ex.Message);
                return;
            }
            activeIntake = null;
            activeObservedId = string.Empty;
            activeFilledCount = 0;
            ClearSensitiveValues();
            intakeStatus.Text = "Đã hoàn tất tờ khai " + intake.Code + ".";
            RequestMatches();
            UpdateIntakeButtons();
        }

        private void ReleaseActiveIntake(bool ask)
        {
            if (activeIntake == null) return;
            if (ask && MessageBox.Show("Trả tờ khai " + activeIntake.Code + " về hàng chờ?\r\nNội dung đã điền trên HIS (nếu có) không tự xóa — hãy tự kiểm tra trên HIS.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            SafeRelease(activeIntake.Id);
            activeIntake = null;
            activeObservedId = string.Empty;
            activeFilledCount = 0;
            ClearSensitiveValues();
            intakeStatus.Text = "Đã trả tờ khai về hàng chờ.";
            RequestMatches();
            UpdateIntakeButtons();
        }

        private void SafeRelease(string id)
        {
            try { intakeClient.Release(id); }
            catch (Exception ex) { Log("Không trả được tờ khai về hàng chờ: " + ex.Message); }
        }

        private void SetIntakeBusy(bool busy, string message)
        {
            intakeBusy = busy;
            intakeStatus.Text = message;
            UseWaitCursor = busy;
            UpdateIntakeButtons();
            Application.DoEvents();
        }

        private static bool SameId(string a, string b)
        {
            Func<string, string> fold = s => new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return fold(a).Length > 0 && fold(a) == fold(b);
        }

        private AutomationProfile FindLoadedProfile(string name)
        {
            foreach (var item in profileBox.Items)
            {
                var profile = item as AutomationProfile;
                if (profile != null && profile.Name == name) return profile;
            }
            return null;
        }

        private void SelectProfileByName(string name)
        {
            for (var i = 0; i < profileBox.Items.Count; i++)
            {
                var profile = (AutomationProfile)profileBox.Items[i];
                if (profile.Name != name) continue;
                if (profileBox.SelectedIndex != i) profileBox.SelectedIndex = i;
                return;
            }
        }

        private static void BringHisToFront(TargetWindow window)
        {
            try
            {
                var handle = new IntPtr(window.Element.Current.NativeWindowHandle);
                if (handle == IntPtr.Zero) return;
                if (IsIconic(handle)) ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // ------------------------------------------------------------------ quick panel
        private void ShowQuickPanel(bool minimizeMain)
        {
            if (quickPanel == null || quickPanel.IsDisposed)
            {
                quickPanel = new QuickPanel();
                quickPanel.FillRequested += delegate { FillSelectedIntake(); };
                quickPanel.SavedRequested += delegate { ConfirmIntakeSaved(); };
                quickPanel.ExpandRequested += delegate { RestoreForAction(); };
                quickPanel.HideRequested += delegate
                {
                    connection.QuickPanel = false;
                    SaveConnection();
                    SaveQuickPanelPosition();
                    quickPanel.Hide();
                };
                quickPanel.PositionChanged += delegate { SaveQuickPanelPosition(); };
                quickPanel.PlaceAt(connection.QuickPanelX, connection.QuickPanelY);
            }
            connection.QuickPanel = true;
            SaveConnection();
            UpdateQuickPanel();
            quickPanel.Show();
            if (minimizeMain) WindowState = FormWindowState.Minimized;
        }

        private void RestoreForAction()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (mainTabs != null && mainTabs.TabPages.Count > 0) mainTabs.SelectedIndex = 0;
            Activate();
        }

        private void SaveQuickPanelPosition()
        {
            if (quickPanel == null || quickPanel.IsDisposed || connection == null) return;
            connection.QuickPanelX = quickPanel.Left;
            connection.QuickPanelY = quickPanel.Top;
            SaveConnection();
        }

        private void UpdateQuickPanel()
        {
            if (quickPanel == null || quickPanel.IsDisposed || connection == null) return;
            var selected = SelectedIntake();
            var state = new QuickPanelState
            {
                Connected = connection.IsPaired && serverReachable,
                ServerText = !connection.IsPaired ? "Chưa ghép nối máy chủ" : (serverReachable ? "Đã kết nối" : "Mất kết nối máy chủ"),
                PatientText = patientContext == null ? "Chưa thấy hồ sơ BN trên HIS" : patientContext.DescribeShort(),
                Busy = intakeBusy,
                CanFill = intakeFillButton.Enabled,
                CanConfirm = intakeSavedButton.Enabled
            };
            if (activeIntake != null && patientContext != null && !SameId(patientContext.PatientId, activeObservedId))
            {
                state.Tone = QuickTone.Check;
                state.MatchText = "⚠ " + activeIntake.Code + " (BN " + activeObservedId + ") chưa xác nhận Lưu";
            }
            else if (activeIntake != null)
            {
                state.Tone = QuickTone.Working;
                state.MatchText = "Đã điền " + activeFilledCount + " trường từ " + activeIntake.Code + " → kiểm tra & Lưu trên HIS";
            }
            else if (patientContext == null)
            {
                state.Tone = QuickTone.Idle;
                state.MatchText = "Mở hồ sơ BN trên HIS";
            }
            else if (intakeMatches.Count == 0)
            {
                state.Tone = QuickTone.Idle;
                state.MatchText = "Không có tờ khai đã duyệt";
            }
            else if (selected != null)
            {
                state.Tone = selected.Score >= 100 ? QuickTone.Ready : QuickTone.Check;
                state.MatchText = (selected.Score >= 100 ? "✔ Có tờ khai " : "? Cần xác nhận: ") + selected.Code +
                    (string.IsNullOrEmpty(selected.ApprovedBy) ? string.Empty : " · " + selected.ApprovedBy);
            }
            else
            {
                state.Tone = QuickTone.Check;
                state.MatchText = intakeMatches.Count + " tờ khai — mở rộng để chọn";
            }
            quickPanel.Apply(state);
        }
    }
}
