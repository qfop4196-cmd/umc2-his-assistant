using System;
using System.Drawing;
using System.Windows.Forms;

namespace MockHis
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MockHisForm());
        }
    }

    /// <summary>
    /// Fake HIS screen for testing without real patient data. Control names mirror UMC2HIS (mabn, lydo, benhly...).
    /// The "Bệnh nhân thử" selector simulates opening another patient so auto-detection can be tested.
    /// </summary>
    internal sealed class MockHisForm : Form
    {
        private static readonly string[][] Patients =
        {
            new[] { "BN-TEST-001", "NGUYỄN VĂN TEST", "1970" },
            new[] { "BN-TEST-002", "TRẦN THỊ MẪU", "1988" }
        };

        private readonly Label saveCountLabel = new Label();
        private readonly ComboBox patientBox = new ComboBox();
        private TextBox idBox;
        private TextBox nameBox;
        private TextBox birthBox;
        private TableLayoutPanel layout;
        private int saveCount;

        public MockHisForm()
        {
            Text = "MOCK HIS - Phiếu khám vào viện";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 760);
            Font = new Font("Segoe UI", 9F);

            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            patientBox.Name = "mockPatient";
            patientBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var p in Patients) patientBox.Items.Add(p[0] + " — " + p[1] + " — " + p[2]);
            patientBox.Dock = DockStyle.Fill;
            AddRow("Bệnh nhân thử (mở hồ sơ)", patientBox, false);

            idBox = AddField("Mã bệnh nhân", "mabn", false, true);
            nameBox = AddField("Họ tên", "hoten", false, true);
            birthBox = AddField("Năm sinh", "namsinh", false, true);
            AddField("Lý do vào viện", "lydo", true, false);
            AddField("Quá trình bệnh lý", "benhly", true, false);
            AddField("Tiền sử bản thân", "banthan", true, false);
            AddField("Tiền sử gia đình", "giadinh", true, false);
            AddField("Dị ứng", "diung", true, false);
            AddField("Khám toàn thân", "toanthan", true, false);
            AddField("Khám các bộ phận", "bophan", true, false);
            AddField("Tóm tắt", "tomtat", true, false);
            AddField("Chẩn đoán", "chandoan", true, false);
            AddField("Chẩn đoán sơ bộ", "sobo", true, false);
            AddField("Xử trí", "xuli", true, false);
            AddField("Chú ý", "chuy", true, false);
            AddField("Mạch (lần/phút)", "mach", false, false);
            AddField("Nhiệt độ (°C)", "nhietdo", false, false);
            AddField("Huyết áp (mmHg)", "huyetap", false, false);
            AddField("Nhịp thở (lần/phút)", "nhiptho", false, false);
            AddField("Cân nặng (kg)", "cannang", false, false);
            AddField("Chiều cao (cm)", "cao", false, false);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var save = new Button { Name = "butLuu", Text = "Lưu", AutoSize = true };
            save.Click += delegate { saveCount++; UpdateSaveCount(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(new Button { Name = "butBoqua", Text = "Bỏ qua", AutoSize = true });
            saveCountLabel.Name = "saveCount";
            saveCountLabel.AutoSize = true;
            saveCountLabel.Padding = new Padding(20, 7, 0, 0);
            buttons.Controls.Add(saveCountLabel);
            AddRow("Thao tác", buttons, false);

            patientBox.SelectedIndexChanged += delegate { OpenPatient(patientBox.SelectedIndex); };
            patientBox.SelectedIndex = 0;
            UpdateSaveCount();
        }

        private void OpenPatient(int index)
        {
            if (index < 0) return;
            foreach (Control control in layout.Controls)
            {
                var box = control as TextBox;
                if (box != null && !box.ReadOnly) box.Text = string.Empty;
            }
            idBox.Text = Patients[index][0];
            nameBox.Text = Patients[index][1];
            birthBox.Text = Patients[index][2];
        }

        private TextBox AddField(string label, string name, bool multiline, bool readOnly)
        {
            var box = new TextBox
            {
                Name = name,
                ReadOnly = readOnly,
                Multiline = multiline,
                Dock = DockStyle.Fill,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };
            AddRow(label, box, multiline);
            return box;
        }

        private void AddRow(string label, Control control, bool tall)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(tall ? SizeType.Absolute : SizeType.AutoSize, tall ? 58 : 28));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void UpdateSaveCount()
        {
            saveCountLabel.Text = "Số lần bấm Lưu: " + saveCount;
        }
    }
}
