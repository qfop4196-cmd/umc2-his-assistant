using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    public sealed class MainForm : Form
    {
        private readonly ProfileStore profileStore = new ProfileStore();
        private readonly UiaAutomationService automation = new UiaAutomationService();
        private readonly CloudQueueClient cloudQueue = new CloudQueueClient();
        private readonly ComboBox profileBox = new ComboBox();
        private readonly ComboBox windowBox = new ComboBox();
        private readonly DataGridView fieldGrid = new DataGridView();
        private readonly DataGridView scanGrid = new DataGridView();
        private readonly TextBox valueEditor = new TextBox();
        private readonly TextBox logBox = new TextBox();
        private readonly TextBox profileNameBox = new TextBox();
        private readonly TextBox titleRegexBox = new TextBox();
        private readonly TextBox processNamesBox = new TextBox();
        private readonly TextBox cloudFormCodeBox = new TextBox();
        private readonly TextBox saveActionIdBox = new TextBox();
        private readonly TextBox closeActionIdBox = new TextBox();
        private readonly CheckBox allowSaveAfterFillBox = new CheckBox();
        private readonly Label editorLabel = new Label();
        private readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        private readonly Button captureButton = new Button();
        private readonly TextBox cloudUrlBox = new TextBox();
        private readonly TextBox cloudSecretBox = new TextBox();
        private readonly CheckBox cloudAutoRunBox = new CheckBox();
        private readonly Label cloudJobLabel = new Label();
        private SplitContainer inputSplit;
        private AutomationProfile currentProfile;
        private bool loadingEditor;
        private int captureCountdown;
        private int captureRowIndex;
        private Timer captureTimer;
        private CloudAutomationJob activeCloudJob;
        private readonly string agentId = Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        public MainForm()
        {
            Text = "HIS Admission Assistant — Webapp → HIS";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1060, 680);
            Size = new Size(1280, 780);
            Font = new Font("Segoe UI", 9F);
            BuildInterface();
            cloudUrlBox.Text = Environment.GetEnvironmentVariable("HIS_AGENT_API_BASE") ?? string.Empty;
            cloudSecretBox.Text = Environment.GetEnvironmentVariable("HIS_AGENT_SECRET") ?? string.Empty;
            LoadProfiles();
            FormClosing += OnFormClosing;
            Shown += delegate { AdjustInputSplit(); };
            SizeChanged += delegate { AdjustInputSplit(); };
        }

        private void BuildInterface()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 92,
                ColumnCount = 6,
                RowCount = 2,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(242, 246, 250)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            top.Controls.Add(MakeLabel("Profile"), 0, 0);
            profileBox.Dock = DockStyle.Fill;
            profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            profileBox.SelectedIndexChanged += delegate { SelectProfile(); };
            top.Controls.Add(profileBox, 1, 0);

            top.Controls.Add(MakeLabel("Cửa sổ HIS"), 2, 0);
            windowBox.Dock = DockStyle.Fill;
            windowBox.DropDownStyle = ComboBoxStyle.DropDownList;
            top.Controls.Add(windowBox, 3, 0);

            var reloadProfiles = MakeButton("Nạp profile", delegate { LoadProfiles(); });
            top.Controls.Add(reloadProfiles, 4, 0);
            var refreshWindows = MakeButton("Tìm cửa sổ", delegate { RefreshWindows(); });
            top.Controls.Add(refreshWindows, 5, 0);

            var notice = new Label
            {
                Text = "Không ghi dữ liệu bệnh nhân xuống đĩa • Luôn đối chiếu mã BN • Không tự ký hồ sơ",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(139, 72, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            top.Controls.Add(notice, 0, 1);
            top.SetColumnSpan(notice, 4);
            top.Controls.Add(MakeButton("Kiểm tra selector", delegate { ValidateSelectors(); }), 4, 1);
            var fillButton = MakeButton("ĐIỀN VÀO HIS", delegate { ApplyToHis(); });
            fillButton.BackColor = Color.FromArgb(25, 118, 210);
            fillButton.ForeColor = Color.White;
            fillButton.FlatStyle = FlatStyle.Flat;
            top.Controls.Add(fillButton, 5, 1);
            Controls.Add(top);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildInputTab());
            tabs.TabPages.Add(BuildCloudTab());
            tabs.TabPages.Add(BuildInspectorTab());
            tabs.TabPages.Add(BuildLogTab());
            Controls.Add(tabs);
            tabs.BringToFront();

            var status = new StatusStrip();
            status.Items.Add(statusLabel);
            statusLabel.Text = "Sẵn sàng";
            Controls.Add(status);
        }

        private TabPage BuildInputTab()
        {
            var tab = new TabPage("Nhập liệu");
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 365,
                FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 260,
                Panel2MinSize = 150,
                SplitterWidth = 6
            };
            inputSplit = split;
            ConfigureFieldGrid();
            split.Panel1.Controls.Add(fieldGrid);

            var editorPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };
            editorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            editorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editorLabel.Dock = DockStyle.Fill;
            editorLabel.Text = "Chọn một trường để nhập nội dung";
            editorLabel.Font = new Font(Font, FontStyle.Bold);
            editorPanel.Controls.Add(editorLabel, 0, 0);
            valueEditor.Dock = DockStyle.Fill;
            valueEditor.Multiline = true;
            valueEditor.AcceptsReturn = true;
            valueEditor.AcceptsTab = true;
            valueEditor.ScrollBars = ScrollBars.Vertical;
            valueEditor.TextChanged += ValueEditorTextChanged;
            editorPanel.Controls.Add(valueEditor, 0, 1);
            split.Panel2.Controls.Add(editorPanel);
            tab.Controls.Add(split);
            return tab;
        }

        private void AdjustInputSplit()
        {
            if (inputSplit == null || inputSplit.Height < 430) return;
            var desired = inputSplit.Height - 205;
            desired = Math.Max(inputSplit.Panel1MinSize, desired);
            desired = Math.Min(inputSplit.Height - inputSplit.Panel2MinSize - inputSplit.SplitterWidth, desired);
            if (desired > 0 && desired != inputSplit.SplitterDistance)
                inputSplit.SplitterDistance = desired;
        }

        private void ConfigureFieldGrid()
        {
            fieldGrid.Dock = DockStyle.Fill;
            fieldGrid.AllowUserToAddRows = false;
            fieldGrid.AllowUserToDeleteRows = false;
            fieldGrid.AutoGenerateColumns = false;
            fieldGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            fieldGrid.MultiSelect = false;
            fieldGrid.RowHeadersVisible = false;
            fieldGrid.BackgroundColor = Color.White;
            fieldGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Điền", Width = 45 });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "Trường", Width = 175, ReadOnly = true });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Operation", HeaderText = "Chế độ", Width = 75, ReadOnly = true });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Dữ liệu (chỉ trong RAM)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AutomationId", HeaderText = "AutomationId", Width = 130 });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 125 });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ControlType", HeaderText = "Loại", Width = 75 });
            fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Trạng thái", Width = 190, ReadOnly = true });
            fieldGrid.SelectionChanged += FieldSelectionChanged;
            fieldGrid.CellEndEdit += delegate(object sender, DataGridViewCellEventArgs e) { SyncRowToField(e.RowIndex); };
            fieldGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (fieldGrid.IsCurrentCellDirty) fieldGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            fieldGrid.DataError += delegate { };
        }

        private TabPage BuildInspectorTab()
        {
            var tab = new TabPage("Hiệu chỉnh UIA");
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 226,
                ColumnCount = 4,
                RowCount = 6,
                Padding = new Padding(8)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.Controls.Add(MakeLabel("Tên profile"), 0, 0);
            profileNameBox.Dock = DockStyle.Fill;
            header.Controls.Add(profileNameBox, 1, 0);
            header.Controls.Add(MakeLabel("Process (phân cách ,)"), 2, 0);
            processNamesBox.Dock = DockStyle.Fill;
            header.Controls.Add(processNamesBox, 3, 0);
            header.Controls.Add(MakeLabel("Regex tiêu đề"), 0, 1);
            titleRegexBox.Dock = DockStyle.Fill;
            header.Controls.Add(titleRegexBox, 1, 1);
            header.SetColumnSpan(titleRegexBox, 3);

            header.Controls.Add(MakeLabel("Mã biểu mẫu webapp"), 0, 2);
            cloudFormCodeBox.Dock = DockStyle.Fill;
            header.Controls.Add(cloudFormCodeBox, 1, 2);
            allowSaveAfterFillBox.Text = "Cho phép Lưu/Đóng tự động sau khi kiểm tra selector";
            allowSaveAfterFillBox.AutoSize = true;
            allowSaveAfterFillBox.Dock = DockStyle.Fill;
            header.Controls.Add(allowSaveAfterFillBox, 2, 2);
            header.SetColumnSpan(allowSaveAfterFillBox, 2);

            header.Controls.Add(MakeLabel("Nút Lưu (AutomationId)"), 0, 3);
            saveActionIdBox.Dock = DockStyle.Fill;
            header.Controls.Add(saveActionIdBox, 1, 3);
            var mapSaveButton = MakeButton("Gán dòng scan → Lưu", delegate { AssignSelectedAction(true); });
            header.Controls.Add(mapSaveButton, 2, 3);
            header.SetColumnSpan(mapSaveButton, 2);

            header.Controls.Add(MakeLabel("Nút Đóng (AutomationId)"), 0, 4);
            closeActionIdBox.Dock = DockStyle.Fill;
            header.Controls.Add(closeActionIdBox, 1, 4);
            var mapCloseButton = MakeButton("Gán dòng scan → Đóng", delegate { AssignSelectedAction(false); });
            header.Controls.Add(mapCloseButton, 2, 4);
            header.SetColumnSpan(mapCloseButton, 2);

            var scanButton = MakeButton("Quét control", delegate { ScanControls(); });
            header.Controls.Add(scanButton, 0, 5);
            captureButton.Text = "Bắt control sau 3 giây";
            captureButton.Dock = DockStyle.Fill;
            captureButton.Click += delegate { BeginCapture(); };
            header.Controls.Add(captureButton, 1, 5);
            var addButton = MakeButton("Thêm field", delegate { AddField(); });
            header.Controls.Add(addButton, 2, 5);
            var saveButton = MakeButton("Lưu profile", delegate { SaveProfile(); });
            header.Controls.Add(saveButton, 3, 5);
            tab.Controls.Add(header);

            scanGrid.Dock = DockStyle.Fill;
            scanGrid.ReadOnly = true;
            scanGrid.AllowUserToAddRows = false;
            scanGrid.AllowUserToDeleteRows = false;
            scanGrid.RowHeadersVisible = false;
            scanGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            scanGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            scanGrid.Columns.Add("AutomationId", "AutomationId");
            scanGrid.Columns.Add("Name", "Name");
            scanGrid.Columns.Add("ControlType", "Loại");
            scanGrid.Columns.Add("ClassName", "ClassName");
            scanGrid.Columns.Add("Enabled", "Enabled");
            scanGrid.Columns.Add("Bounds", "Vị trí");
            scanGrid.DoubleClick += delegate { AssignScannedControl(); };
            tab.Controls.Add(scanGrid);
            scanGrid.BringToFront();
            return tab;
        }

        private TabPage BuildLogTab()
        {
            var tab = new TabPage("Nhật ký phiên");
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.Font = new Font("Consolas", 9F);
            logBox.WordWrap = false;
            tab.Controls.Add(logBox);
            return tab;
        }

        private TabPage BuildCloudTab()
        {
            var tab = new TabPage("Đồng bộ webapp");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 6,
                Padding = new Padding(18),
                BackColor = Color.White
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

            var heading = new Label
            {
                Text = "Nhận bộ hồ sơ đã được bác sĩ duyệt từ webapp",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 12)
            };
            panel.Controls.Add(heading, 0, 0);
            panel.SetColumnSpan(heading, 3);

            panel.Controls.Add(MakeLabel("Địa chỉ webapp"), 0, 1);
            cloudUrlBox.Dock = DockStyle.Fill;
            panel.Controls.Add(cloudUrlBox, 1, 1);
            panel.SetColumnSpan(cloudUrlBox, 2);

            panel.Controls.Add(MakeLabel("Khóa tác nhân"), 0, 2);
            cloudSecretBox.Dock = DockStyle.Fill;
            cloudSecretBox.UseSystemPasswordChar = true;
            panel.Controls.Add(cloudSecretBox, 1, 2);
            var claimButton = MakeButton("Nhận tác vụ kế tiếp", delegate { ClaimCloudJob(); });
            panel.Controls.Add(claimButton, 2, 2);

            cloudAutoRunBox.Text = "Tự điền + Lưu + Đóng khi profile đã hiệu chỉnh và hồ sơ không yêu cầu ký";
            cloudAutoRunBox.AutoSize = true;
            cloudAutoRunBox.Margin = new Padding(3, 12, 3, 8);
            var importButton = MakeButton("Nhập gói pilot JSON", delegate { ImportPilotPackage(); });
            panel.Controls.Add(importButton, 0, 3);
            panel.Controls.Add(cloudAutoRunBox, 1, 3);
            panel.SetColumnSpan(cloudAutoRunBox, 2);

            cloudJobLabel.Text = "Chưa nhận tác vụ. Khóa chỉ giữ trong RAM và không được ghi xuống đĩa.";
            cloudJobLabel.Dock = DockStyle.Fill;
            cloudJobLabel.AutoSize = true;
            cloudJobLabel.Padding = new Padding(12);
            cloudJobLabel.BackColor = Color.FromArgb(238, 246, 250);
            panel.Controls.Add(cloudJobLabel, 0, 4);
            panel.SetColumnSpan(cloudJobLabel, 3);

            var completeButton = MakeButton("Đánh dấu hoàn tất sau khi đã kiểm tra trên HIS", delegate { CompleteActiveCloudJob(); });
            completeButton.Height = 42;
            panel.Controls.Add(completeButton, 1, 5);
            panel.SetColumnSpan(completeButton, 2);

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 110,
                Padding = new Padding(18),
                BackColor = Color.FromArgb(255, 248, 225),
                ForeColor = Color.FromArgb(105, 63, 0),
                Text = "Chế độ tự động chỉ hoạt động khi profile đúng mã biểu mẫu, đã hiệu chỉnh selector Lưu/Đóng và AllowSaveAfterFill=true. " +
                    "Tác vụ yêu cầu chữ ký không được webapp đưa vào hàng đợi tự động."
            };
            tab.Controls.Add(panel);
            tab.Controls.Add(note);
            return tab;
        }

        private void ClaimCloudJob()
        {
            try
            {
                statusLabel.Text = "Đang nhận tác vụ webapp...";
                var job = cloudQueue.Claim(cloudUrlBox.Text.Trim(), cloudSecretBox.Text, agentId);
                if (job == null)
                {
                    cloudJobLabel.Text = "Không có tác vụ đang chờ.";
                    statusLabel.Text = "Hàng đợi trống";
                    return;
                }
                LoadJobIntoProfile(job, true);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ImportPilotPackage()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Gói pilot UMC2 (*.json)|*.json";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var job = cloudQueue.LoadPilotPackage(dialog.FileName);
                    LoadJobIntoProfile(job, false);
                }
                catch (Exception ex)
                {
                    ShowError(ex.Message);
                }
            }
        }

        private void LoadJobIntoProfile(CloudAutomationJob job, bool online)
        {
            activeCloudJob = job;
            var profileIndex = -1;
            for (var i = 0; i < profileBox.Items.Count; i++)
            {
                var profile = (AutomationProfile)profileBox.Items[i];
                if (string.Equals(profile.CloudFormCode, job.FormCode, StringComparison.OrdinalIgnoreCase))
                {
                    profileIndex = i;
                    break;
                }
            }
            if (profileIndex < 0)
            {
                cloudJobLabel.Text = "Tác vụ " + job.FormName + " cần hiệu chỉnh profile trước khi chạy.";
                if (online) cloudQueue.Complete(cloudUrlBox.Text.Trim(), cloudSecretBox.Text, agentId, job, "needs_human", "Không có profile cho mã biểu mẫu.");
                activeCloudJob = null;
                statusLabel.Text = "Thiếu profile " + job.FormCode;
                return;
            }

            profileBox.SelectedIndex = profileIndex;
            foreach (var field in currentProfile.Fields)
            {
                string value;
                field.Value = job.Fields.TryGetValue(field.Key, out value) ? value : string.Empty;
                field.EnabledForFill = !string.IsNullOrWhiteSpace(field.Value) || string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase);
            }
            PopulateFieldGrid();
            RefreshWindows();
            cloudJobLabel.Text = (job.IsOffline ? "Đã nhập gói pilot: " : "Đã nhận: ") + job.FormName + " (" + job.FormCode + "). Dữ liệu đang ở RAM; hãy đối chiếu mã bệnh nhân trên HIS.";
            statusLabel.Text = job.IsOffline ? "Đã nạp gói pilot" : "Đã nạp tác vụ webapp";
            Log((job.IsOffline ? "Đã nhập gói pilot " : "Đã nhận tác vụ webapp ") + job.FormCode + "; không ghi nội dung bệnh nhân vào log.");

            if (!cloudAutoRunBox.Checked) return;
            if (!currentProfile.AllowSaveAfterFill || currentProfile.StopBeforeSave)
            {
                cloudJobLabel.Text += " Profile chưa cho phép Lưu tự động.";
                return;
            }
            if (windowBox.Items.Count != 1)
            {
                cloudJobLabel.Text += " Cần đúng một cửa sổ HIS phù hợp để tự chạy.";
                return;
            }
            windowBox.SelectedIndex = 0;
            ApplyToHis(true);
        }

        private void CompleteActiveCloudJob()
        {
            if (activeCloudJob == null)
            {
                MessageBox.Show("Chưa có tác vụ webapp đang xử lý.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(
                "Chỉ xác nhận sau khi đã kiểm tra biểu mẫu và thao tác Lưu trên HIS. Đánh dấu hoàn tất?",
                "Xác nhận kết quả HIS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                if (!activeCloudJob.IsOffline)
                    cloudQueue.Complete(cloudUrlBox.Text.Trim(), cloudSecretBox.Text, agentId, activeCloudJob, "completed", "Nhân viên xác nhận đã kiểm tra trên HIS.");
                Log("Đã xác nhận hoàn tất tác vụ webapp " + activeCloudJob.FormCode + ".");
                activeCloudJob = null;
                cloudJobLabel.Text = "Tác vụ đã hoàn tất. Có thể nhận tác vụ kế tiếp.";
                ClearSensitiveValues();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static Button MakeButton(string text, EventHandler action)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(4) };
            button.Click += action;
            return button;
        }

        private void LoadProfiles()
        {
            var selectedName = currentProfile == null ? null : currentProfile.Name;
            var profiles = profileStore.LoadAll();
            profileBox.Items.Clear();
            foreach (var profile in profiles) profileBox.Items.Add(profile);
            profileBox.DisplayMember = "Name";
            if (profileBox.Items.Count == 0)
            {
                statusLabel.Text = "Không tìm thấy profile";
                return;
            }
            var index = 0;
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                for (var i = 0; i < profileBox.Items.Count; i++)
                    if (((AutomationProfile)profileBox.Items[i]).Name == selectedName) index = i;
            }
            profileBox.SelectedIndex = index;
            Log("Đã nạp " + profiles.Count + " profile; không nạp dữ liệu bệnh nhân từ đĩa.");
        }

        private void SelectProfile()
        {
            if (profileBox.SelectedItem == null) return;
            currentProfile = (AutomationProfile)profileBox.SelectedItem;
            profileNameBox.Text = currentProfile.Name;
            titleRegexBox.Text = currentProfile.WindowTitleRegex;
            processNamesBox.Text = currentProfile.ProcessNames;
            cloudFormCodeBox.Text = currentProfile.CloudFormCode ?? string.Empty;
            allowSaveAfterFillBox.Checked = currentProfile.AllowSaveAfterFill && !currentProfile.StopBeforeSave;
            saveActionIdBox.Text = currentProfile.SaveAction == null ? string.Empty : currentProfile.SaveAction.AutomationId;
            closeActionIdBox.Text = currentProfile.CloseAction == null ? string.Empty : currentProfile.CloseAction.AutomationId;
            PopulateFieldGrid();
            RefreshWindows();
        }

        private void PopulateFieldGrid()
        {
            loadingEditor = true;
            fieldGrid.Rows.Clear();
            foreach (var field in currentProfile.Fields)
            {
                var index = fieldGrid.Rows.Add(
                    field.EnabledForFill,
                    field.Label,
                    field.Operation,
                    field.Value,
                    field.AutomationId,
                    field.Name,
                    field.ControlType,
                    field.Status);
                fieldGrid.Rows[index].Tag = field;
                if (string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase))
                    fieldGrid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(232, 245, 233);
            }
            loadingEditor = false;
            if (fieldGrid.Rows.Count > 0) fieldGrid.Rows[0].Selected = true;
        }

        private void FieldSelectionChanged(object sender, EventArgs e)
        {
            if (loadingEditor || fieldGrid.SelectedRows.Count == 0) return;
            var field = fieldGrid.SelectedRows[0].Tag as FieldMapping;
            if (field == null) return;
            loadingEditor = true;
            editorLabel.Text = field.Label + (field.Required ? " *" : string.Empty) +
                (string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase) ? " — chỉ đối chiếu, không ghi" : string.Empty);
            valueEditor.Text = field.Value ?? string.Empty;
            loadingEditor = false;
        }

        private void ValueEditorTextChanged(object sender, EventArgs e)
        {
            if (loadingEditor || fieldGrid.SelectedRows.Count == 0) return;
            var row = fieldGrid.SelectedRows[0];
            var field = row.Tag as FieldMapping;
            if (field == null) return;
            field.Value = valueEditor.Text;
            row.Cells["Value"].Value = valueEditor.Text;
        }

        private void SyncRowToField(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= fieldGrid.Rows.Count) return;
            var row = fieldGrid.Rows[rowIndex];
            var field = row.Tag as FieldMapping;
            if (field == null) return;
            field.EnabledForFill = ToBool(row.Cells["Use"].Value, true);
            field.Value = CellText(row, "Value");
            field.AutomationId = CellText(row, "AutomationId");
            field.Name = CellText(row, "Name");
            field.ControlType = CellText(row, "ControlType");
        }

        private void SyncAllRows()
        {
            fieldGrid.EndEdit();
            for (var i = 0; i < fieldGrid.Rows.Count; i++) SyncRowToField(i);
            if (currentProfile != null)
            {
                currentProfile.Name = profileNameBox.Text.Trim();
                currentProfile.WindowTitleRegex = titleRegexBox.Text.Trim();
                currentProfile.ProcessNames = processNamesBox.Text.Trim();
                currentProfile.CloudFormCode = cloudFormCodeBox.Text.Trim();
                currentProfile.AllowSaveAfterFill = allowSaveAfterFillBox.Checked;
                currentProfile.StopBeforeSave = !allowSaveAfterFillBox.Checked;
                if (currentProfile.SaveAction == null) currentProfile.SaveAction = new UiActionMapping();
                if (currentProfile.CloseAction == null) currentProfile.CloseAction = new UiActionMapping();
                currentProfile.SaveAction.AutomationId = saveActionIdBox.Text.Trim();
                currentProfile.CloseAction.AutomationId = closeActionIdBox.Text.Trim();
            }
        }

        private void RefreshWindows()
        {
            if (currentProfile == null) return;
            SyncAllRows();
            try
            {
                var previous = windowBox.SelectedItem as TargetWindow;
                var windows = automation.FindWindows(currentProfile);
                windowBox.Items.Clear();
                foreach (var window in windows) windowBox.Items.Add(window);
                if (windowBox.Items.Count > 0)
                {
                    var index = 0;
                    if (previous != null)
                    {
                        for (var i = 0; i < windowBox.Items.Count; i++)
                            if (((TargetWindow)windowBox.Items[i]).ProcessId == previous.ProcessId) index = i;
                    }
                    windowBox.SelectedIndex = index;
                }
                statusLabel.Text = windows.Count == 0 ? "Chưa thấy cửa sổ HIS phù hợp" : "Đã tìm thấy " + windows.Count + " cửa sổ";
                Log("Tìm cửa sổ: " + windows.Count + " kết quả (không ghi tiêu đề để tránh lộ thông tin).");
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ValidateSelectors()
        {
            SyncAllRows();
            var target = SelectedTarget();
            if (target == null) return;
            try
            {
                var results = automation.ValidateMappings(target.Element, currentProfile.Fields);
                ApplyResults(results);
                var found = results.Count(r => r.Found);
                statusLabel.Text = string.Format("Selector: tìm thấy {0}/{1}", found, results.Count);
                Log(string.Format("Kiểm tra selector PID {0}: tìm thấy {1}/{2}; không đọc giá trị.", target.ProcessId, found, results.Count));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ApplyToHis()
        {
            ApplyToHis(false);
        }

        private void ApplyToHis(bool unattended)
        {
            SyncAllRows();
            var target = SelectedTarget();
            if (target == null) return;
            var valueCount = currentProfile.Fields.Count(f => f.EnabledForFill && !string.IsNullOrWhiteSpace(f.Value));
            if (valueCount == 0)
            {
                MessageBox.Show("Chưa có dữ liệu để điền.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!unattended)
            {
                var answer = MessageBox.Show(
                    string.Format("Sẽ kiểm tra trước rồi điền {0} trường vào cửa sổ HIS đang chọn.\r\n\r\nỨng dụng sẽ chưa bấm Lưu/Xác nhận ở chế độ thủ công. Tiếp tục?", valueCount),
                    "Xác nhận điền HIS",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }

            try
            {
                var results = automation.Apply(target.Element, currentProfile.Fields);
                ApplyResults(results);
                var changed = results.Count(r => r.Changed);
                var failed = results.Count(r => !r.Found || r.Message.StartsWith("Không") || r.Message.StartsWith("Lỗi") || r.Message.StartsWith("Thiếu"));
                statusLabel.Text = string.Format("Đã điền {0} trường; {1} mục cần kiểm tra", changed, failed);
                Log(string.Format("Điền PID {0}: thay đổi {1} trường; {2} mục cần kiểm tra.", target.ProcessId, changed, failed));

                if (unattended && activeCloudJob != null)
                {
                    if (failed > 0)
                    {
                        if (!activeCloudJob.IsOffline)
                            cloudQueue.Complete(cloudUrlBox.Text.Trim(), cloudSecretBox.Text, agentId, activeCloudJob, "needs_human", "Selector hoặc dữ liệu cần kiểm tra.");
                        cloudJobLabel.Text = "Tác vụ dừng để nhân viên kiểm tra vì có trường không điền được.";
                        activeCloudJob = null;
                        return;
                    }
                    if (!string.Equals(activeCloudJob.SignaturePolicy, "save_unsigned", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Tác vụ yêu cầu ký không được phép chạy tự động.");
                    if (!currentProfile.AllowSaveAfterFill || currentProfile.StopBeforeSave)
                        throw new InvalidOperationException("Profile chưa cho phép thao tác Lưu tự động.");

                    automation.InvokeAuthorizedAction(target.Element, currentProfile.SaveAction, "Lưu");
                    if (currentProfile.CloseAction != null && (!string.IsNullOrWhiteSpace(currentProfile.CloseAction.AutomationId) || !string.IsNullOrWhiteSpace(currentProfile.CloseAction.Name)))
                        automation.InvokeAuthorizedAction(target.Element, currentProfile.CloseAction, "Đóng");
                    if (!activeCloudJob.IsOffline)
                        cloudQueue.Complete(cloudUrlBox.Text.Trim(), cloudSecretBox.Text, agentId, activeCloudJob, "completed", "Đã điền, lưu và đóng bằng profile đã hiệu chỉnh.");
                    Log("Đã tự động Lưu/Đóng tác vụ " + activeCloudJob.FormCode + ".");
                    activeCloudJob = null;
                    cloudJobLabel.Text = "Tác vụ tự động đã hoàn tất.";
                    ClearSensitiveValues();
                }
                else
                {
                    MessageBox.Show(
                        string.Format("Đã điền {0} trường.\r\n\r\nHãy kiểm tra trực tiếp trên HIS trước khi tự bấm Lưu.", changed),
                        "Hoàn tất điền",
                        MessageBoxButtons.OK,
                        changed > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ScanControls()
        {
            var target = SelectedTarget();
            if (target == null) return;
            try
            {
                var items = automation.Scan(target.Element, 3000);
                scanGrid.Rows.Clear();
                foreach (var item in items)
                    scanGrid.Rows.Add(item.AutomationId, item.Name, item.ControlType, item.ClassName, item.IsEnabled, item.Bounds);
                statusLabel.Text = "Đã quét " + items.Count + " control (không đọc Value)";
                Log("Quét control PID " + target.ProcessId + ": " + items.Count + " control; không đọc Value.");
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void BeginCapture()
        {
            if (fieldGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Hãy chọn field cần gán ở tab Nhập liệu trước.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            captureRowIndex = fieldGrid.SelectedRows[0].Index;
            captureCountdown = 3;
            captureButton.Enabled = false;
            captureButton.Text = "Trỏ chuột vào control... 3";
            captureTimer = new Timer { Interval = 1000 };
            captureTimer.Tick += CaptureTimerTick;
            captureTimer.Start();
            WindowState = FormWindowState.Minimized;
        }

        private void CaptureTimerTick(object sender, EventArgs e)
        {
            captureCountdown--;
            if (captureCountdown > 0)
            {
                captureButton.Text = "Trỏ chuột vào control... " + captureCountdown;
                return;
            }
            captureTimer.Stop();
            captureTimer.Dispose();
            try
            {
                AutomationElement captured;
                var point = Cursor.Position;
                var snapshot = automation.CaptureAt(point.X, point.Y, out captured);
                if (captureRowIndex >= 0 && captureRowIndex < fieldGrid.Rows.Count)
                {
                    var row = fieldGrid.Rows[captureRowIndex];
                    row.Cells["AutomationId"].Value = snapshot.AutomationId;
                    row.Cells["Name"].Value = snapshot.Name;
                    row.Cells["ControlType"].Value = snapshot.ControlType;
                    row.Cells["Status"].Value = "Đã bắt control dưới chuột";
                    var field = row.Tag as FieldMapping;
                    if (field != null)
                    {
                        field.AutomationId = snapshot.AutomationId;
                        field.Name = snapshot.Name;
                        field.ControlType = snapshot.ControlType;
                        field.ClassName = snapshot.ClassName;
                    }
                }
                Log("Đã bắt một control UIA dưới con trỏ; không đọc Value.");
            }
            catch (Exception ex)
            {
                ShowError("Không bắt được control: " + ex.Message);
            }
            finally
            {
                captureButton.Enabled = true;
                captureButton.Text = "Bắt control sau 3 giây";
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void AssignScannedControl()
        {
            if (scanGrid.SelectedRows.Count == 0 || fieldGrid.SelectedRows.Count == 0) return;
            var source = scanGrid.SelectedRows[0];
            var destination = fieldGrid.SelectedRows[0];
            destination.Cells["AutomationId"].Value = Convert.ToString(source.Cells["AutomationId"].Value);
            destination.Cells["Name"].Value = Convert.ToString(source.Cells["Name"].Value);
            destination.Cells["ControlType"].Value = Convert.ToString(source.Cells["ControlType"].Value);
            var field = destination.Tag as FieldMapping;
            if (field != null)
            {
                field.AutomationId = CellText(destination, "AutomationId");
                field.Name = CellText(destination, "Name");
                field.ControlType = CellText(destination, "ControlType");
                field.ClassName = Convert.ToString(source.Cells["ClassName"].Value);
            }
            statusLabel.Text = "Đã gán selector từ control được quét";
        }

        private void AssignSelectedAction(bool save)
        {
            if (currentProfile == null || scanGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Hãy bấm Quét control và chọn đúng dòng nút trên HIS trước.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var source = scanGrid.SelectedRows[0];
            var controlType = Convert.ToString(source.Cells["ControlType"].Value);
            if (!string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Control đang chọn không được UIA nhận diện là Button.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var action = save
                ? (currentProfile.SaveAction ?? (currentProfile.SaveAction = new UiActionMapping()))
                : (currentProfile.CloseAction ?? (currentProfile.CloseAction = new UiActionMapping()));
            action.AutomationId = Convert.ToString(source.Cells["AutomationId"].Value) ?? string.Empty;
            action.Name = Convert.ToString(source.Cells["Name"].Value) ?? string.Empty;
            action.ClassName = Convert.ToString(source.Cells["ClassName"].Value) ?? string.Empty;
            action.MatchIndex = 0;
            if (save) saveActionIdBox.Text = action.AutomationId;
            else closeActionIdBox.Text = action.AutomationId;
            statusLabel.Text = save ? "Đã gán nút Lưu; hãy lưu profile" : "Đã gán nút Đóng; hãy lưu profile";
            Log(save ? "Đã gán selector hành động Lưu." : "Đã gán selector hành động Đóng.");
        }

        private void AddField()
        {
            if (currentProfile == null) return;
            var field = new FieldMapping
            {
                Key = "NewField" + (currentProfile.Fields.Count + 1),
                Label = "Trường mới",
                ControlType = "Edit",
                EnabledForFill = true
            };
            currentProfile.Fields.Add(field);
            PopulateFieldGrid();
            fieldGrid.Rows[fieldGrid.Rows.Count - 1].Selected = true;
        }

        private void SaveProfile()
        {
            if (currentProfile == null) return;
            SyncAllRows();
            if (string.IsNullOrWhiteSpace(currentProfile.Name))
            {
                MessageBox.Show("Tên profile không được để trống.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var clean = new AutomationProfile
                {
                    Name = currentProfile.Name,
                    WindowTitleRegex = currentProfile.WindowTitleRegex,
                    ProcessNames = currentProfile.ProcessNames,
                    StopBeforeSave = currentProfile.StopBeforeSave,
                    CloudFormCode = currentProfile.CloudFormCode,
                    AllowSaveAfterFill = currentProfile.AllowSaveAfterFill,
                    SaveAction = currentProfile.SaveAction == null ? new UiActionMapping() : currentProfile.SaveAction.Clone(),
                    CloseAction = currentProfile.CloseAction == null ? new UiActionMapping() : currentProfile.CloseAction.Clone(),
                    Fields = currentProfile.Fields.Select(f => f.CloneWithoutValue()).ToList()
                };
                var path = profileStore.SaveUserProfile(clean);
                statusLabel.Text = "Đã lưu profile: " + path;
                Log("Đã lưu selector profile; không lưu dữ liệu nhập.");
                MessageBox.Show("Đã lưu selector vào:\r\n" + path + "\r\n\r\nDữ liệu bệnh nhân không được lưu.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private TargetWindow SelectedTarget()
        {
            var target = windowBox.SelectedItem as TargetWindow;
            if (target == null)
                MessageBox.Show("Chưa chọn được cửa sổ HIS. Hãy mở đúng màn hình rồi bấm Tìm cửa sổ.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return target;
        }

        private void ApplyResults(IEnumerable<FieldResult> results)
        {
            foreach (var result in results)
            {
                result.Field.Status = result.Message;
                foreach (DataGridViewRow row in fieldGrid.Rows)
                {
                    if (!object.ReferenceEquals(row.Tag, result.Field)) continue;
                    row.Cells["Status"].Value = result.Message;
                    row.DefaultCellStyle.BackColor = result.Changed
                        ? Color.FromArgb(220, 245, 222)
                        : (result.Found ? Color.White : Color.FromArgb(255, 235, 238));
                    if (string.Equals(result.Field.Operation, "Verify", StringComparison.OrdinalIgnoreCase) && result.Found)
                        row.DefaultCellStyle.BackColor = Color.FromArgb(232, 245, 233);
                    break;
                }
            }
        }

        private void ShowError(string message)
        {
            statusLabel.Text = "Có lỗi";
            Log("Lỗi: " + message);
            MessageBox.Show(
                message + "\r\n\r\nNếu HIS chạy quyền Administrator, hãy chạy app này cùng mức quyền.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private void Log(string message)
        {
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            ClearSensitiveValues();
            cloudSecretBox.Clear();
            logBox.Clear();
        }

        private void ClearSensitiveValues()
        {
            if (currentProfile != null)
                foreach (var field in currentProfile.Fields) field.Value = string.Empty;
            valueEditor.Clear();
            if (currentProfile != null) PopulateFieldGrid();
        }

        private static string CellText(DataGridViewRow row, string name)
        {
            return Convert.ToString(row.Cells[name].Value) ?? string.Empty;
        }

        private static bool ToBool(object value, bool defaultValue)
        {
            if (value == null) return defaultValue;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) ? parsed : defaultValue;
        }
    }
}
