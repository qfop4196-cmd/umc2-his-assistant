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

    internal sealed class MockHisForm : Form
    {
        private readonly Label saveCountLabel = new Label();
        private int saveCount;

        public MockHisForm()
        {
            Text = "MOCK HIS - Phiếu khám vào viện";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(850, 650);
            Font = new Font("Segoe UI", 9F);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            AddField(layout, "Mã bệnh nhân", "mabn", "BN-TEST-001", false, true);
            AddField(layout, "Lý do vào viện", "lydo", string.Empty, true, false);
            AddField(layout, "Quá trình bệnh lý", "benhly", string.Empty, true, false);
            AddField(layout, "Tiền sử bản thân", "banthan", string.Empty, true, false);
            AddField(layout, "Tiền sử gia đình", "giadinh", string.Empty, true, false);
            AddField(layout, "Dị ứng", "diung", string.Empty, true, false);
            AddField(layout, "Khám toàn thân", "toanthan", string.Empty, true, false);
            AddField(layout, "Khám các bộ phận", "bophan", string.Empty, true, false);
            AddField(layout, "Tóm tắt", "tomtat", string.Empty, true, false);
            AddField(layout, "Chẩn đoán", "chandoan", string.Empty, true, false);
            AddField(layout, "Chẩn đoán sơ bộ", "sobo", string.Empty, true, false);
            AddField(layout, "Xử trí", "xuli", string.Empty, true, false);
            AddField(layout, "Chú ý", "chuy", string.Empty, true, false);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var save = new Button { Name = "butLuu", Text = "Lưu", AutoSize = true };
            save.Click += delegate { saveCount++; UpdateSaveCount(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(new Button { Name = "butBoqua", Text = "Bỏ qua", AutoSize = true });
            saveCountLabel.Name = "saveCount";
            saveCountLabel.AutoSize = true;
            saveCountLabel.Padding = new Padding(20, 7, 0, 0);
            buttons.Controls.Add(saveCountLabel);
            layout.Controls.Add(new Label { Text = "Thao tác", AutoSize = true }, 0, layout.RowCount);
            layout.Controls.Add(buttons, 1, layout.RowCount++);
            UpdateSaveCount();
        }

        private static void AddField(TableLayoutPanel layout, string label, string name, string value, bool multiline, bool readOnly)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(multiline ? SizeType.Absolute : SizeType.AutoSize, multiline ? 58 : 28));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new TextBox
            {
                Name = name,
                Text = value,
                ReadOnly = readOnly,
                Multiline = multiline,
                Dock = DockStyle.Fill,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };
            layout.Controls.Add(box, 1, row);
        }

        private void UpdateSaveCount()
        {
            saveCountLabel.Text = "Số lần bấm Lưu: " + saveCount;
        }
    }
}
