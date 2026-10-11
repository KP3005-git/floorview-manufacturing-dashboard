using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ManufacturingDashboard
{
    public sealed class UnitRecord
    {
        public DateTime ProductionDate { get; set; }
        public int Shift { get; set; }
        public string UnitId { get; set; }
        public DateTime CompletedAt { get; set; }
        public bool FirstPass { get; set; }
        public string FailureReason { get; set; }
    }

    public static class DemoData
    {
        public const int GoalPerShift = 70;
        public static List<UnitRecord> Create()
        {
            var rows = new List<UnitRecord>();
            string[] reasons = { "Leak check", "Sensor check", "Wiring check" };
            for (int day = 0; day < 3; day++)
            {
                DateTime date = new DateTime(2026, 10, 6).AddDays(day);
                for (int shift = 1; shift <= 2; shift++)
                {
                    int count = shift == 1 ? 60 + day * 2 : 54 + day * 2;
                    for (int i = 0; i < count; i++)
                    {
                        bool firstPass = i % 6 != 0;
                        rows.Add(new UnitRecord {
                            ProductionDate = date, Shift = shift,
                            UnitId = String.Format("DEMO-{0:MMdd}-{1}-{2:000}", date, shift, i + 1),
                            CompletedAt = date.AddHours(shift == 1 ? 6 : 16).AddMinutes(i * 9),
                            FirstPass = firstPass,
                            FailureReason = firstPass ? "" : reasons[(i / 6 + shift + day) % reasons.Length]
                        });
                    }
                }
            }
            return rows;
        }

        public static List<UnitRecord> Select(IEnumerable<UnitRecord> rows, DateTime date, int shift)
        {
            return rows.Where(r => r.ProductionDate.Date == date.Date && (shift == 0 || r.Shift == shift))
                .OrderBy(r => r.CompletedAt).ToList();
        }

        private static string Escape(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string ToCsv(IEnumerable<UnitRecord> rows)
        {
            var csv = new StringBuilder("ProductionDate,Shift,UnitId,CompletedAt,FirstPass,FailureReason\r\n");
            foreach (var r in rows)
                csv.AppendFormat(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd},{1},{2},{3:yyyy-MM-dd HH:mm:ss},{4},{5}\r\n",
                    r.ProductionDate, r.Shift, Escape(r.UnitId), r.CompletedAt,
                    r.FirstPass ? "true" : "false", Escape(r.FailureReason));
            return csv.ToString();
        }
    }

    public sealed class DashboardForm : Form
    {
        public bool CaptureMode { get; set; }
        protected override bool ShowWithoutActivation { get { return CaptureMode; } }
        private readonly List<UnitRecord> allRows = DemoData.Create();
        private List<UnitRecord> selectedRows = new List<UnitRecord>();
        private readonly ComboBox datePicker = new ComboBox();
        private readonly ComboBox shiftPicker = new ComboBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Color background = Color.FromArgb(15, 23, 42);
        private readonly Color surface = Color.FromArgb(30, 41, 59);
        private readonly Color muted = Color.FromArgb(148, 163, 184);
        private readonly Color teal = Color.FromArgb(45, 212, 191);
        private readonly Color amber = Color.FromArgb(251, 191, 36);
        private readonly Font titleFont = new Font("Segoe UI", 25, FontStyle.Bold);
        private readonly Font metricFont = new Font("Segoe UI", 27, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Segoe UI", 10);
        private readonly Font smallFont = new Font("Segoe UI", 9);
        private readonly Font sectionFont = new Font("Segoe UI", 12, FontStyle.Bold);

        public DashboardForm()
        {
            Text = "Floorview | Manufacturing Dashboard Demonstration";
            ClientSize = new Size(1260, 820);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = background;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            datePicker.SetBounds(32, 113, 180, 30);
            shiftPicker.SetBounds(232, 113, 180, 30);
            foreach (var picker in new[] { datePicker, shiftPicker })
            {
                picker.DropDownStyle = ComboBoxStyle.DropDownList;
                picker.Font = bodyFont;
                picker.BackColor = surface;
                picker.ForeColor = Color.White;
                Controls.Add(picker);
            }
            foreach (var date in allRows.Select(r => r.ProductionDate).Distinct())
                datePicker.Items.Add(date.ToString("yyyy-MM-dd"));
            shiftPicker.Items.AddRange(new object[] { "Both shifts", "Shift 1", "Shift 2" });
            datePicker.SelectedIndex = 2;
            shiftPicker.SelectedIndex = 0;
            datePicker.SelectedIndexChanged += (s, e) => RefreshSelection();
            shiftPicker.SelectedIndexChanged += (s, e) => RefreshSelection();

            var exportButton = new Button {
                Text = "Export selected records", Font = bodyFont,
                BackColor = teal, ForeColor = background, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            exportButton.FlatAppearance.BorderSize = 0;
            exportButton.SetBounds(1018, 108, 210, 36);
            exportButton.Click += ExportSelection;
            Controls.Add(exportButton);

            grid.SetBounds(48, 580, 1164, 166);
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoGenerateColumns = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = surface;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(51, 65, 85);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                BackColor = surface, ForeColor = muted, Font = smallFont
            };
            grid.ColumnHeadersHeight = 30;
            grid.RowTemplate.Height = 26;
            grid.DefaultCellStyle = new DataGridViewCellStyle {
                BackColor = surface, ForeColor = Color.White, Font = smallFont,
                SelectionBackColor = Color.FromArgb(51, 65, 85), SelectionForeColor = Color.White
            };
            AddColumn("Unit ID", "UnitId", 150);
            AddColumn("Shift", "Shift", 60);
            AddColumn("Completed at", "CompletedAt", 190);
            grid.Columns[2].DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
            AddColumn("First pass", "FirstPass", 95);
            AddColumn("Initial failure reason", "FailureReason", 200);
            grid.CellFormatting += (s, e) => {
                if (e.ColumnIndex == 3 && e.Value is bool) {
                    bool passed = (bool)e.Value;
                    e.Value = passed ? "PASS" : "REWORK";
                    e.CellStyle.ForeColor = passed ? teal : amber;
                    e.FormattingApplied = true;
                }
            };
            Controls.Add(grid);
            RefreshSelection();
        }

        private void AddColumn(string title, string property, int weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = title, DataPropertyName = property, FillWeight = weight
            });
        }

        public void SetFilter(DateTime date, int shift)
        {
            datePicker.SelectedItem = date.ToString("yyyy-MM-dd");
            shiftPicker.SelectedIndex = shift;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (datePicker.SelectedItem == null || shiftPicker.SelectedIndex < 0) return;
            selectedRows = DemoData.Select(allRows, DateTime.ParseExact(datePicker.SelectedItem.ToString(),
                "yyyy-MM-dd", CultureInfo.InvariantCulture), shiftPicker.SelectedIndex);
            grid.DataSource = null;
            grid.DataSource = selectedRows;
            grid.ClearSelection();
            Invalidate();
        }

        private void ExportSelection(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog {
                Filter = "CSV files (*.csv)|*.csv", FileName = "production-" + datePicker.SelectedItem + ".csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    File.WriteAllText(dialog.FileName, DemoData.ToCsv(selectedRows), new UTF8Encoding(true));
                    MessageBox.Show(this, "Exported " + selectedRows.Count + " selected records.", "Export complete");
                }
                catch (IOException ex) { MessageBox.Show(this, ex.Message, "Unable to export"); }
                catch (UnauthorizedAccessException ex) { MessageBox.Show(this, ex.Message, "Unable to export"); }
            }
        }

        private void Label(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y);
        }
        private void Fill(Graphics g, Color color, float x, float y, float width, float height)
        {
            using (var brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height);
        }
        private void Card(Graphics g, int x, string caption, string value, string note, Color accent)
        {
            Fill(g, surface, x, 168, 284, 108);
            Fill(g, accent, x, 168, 284, 3);
            Label(g, caption, smallFont, muted, x + 16, 181);
            Label(g, value, metricFont, Color.White, x + 14, 199);
            Label(g, note, smallFont, muted, x + 16, 249);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Label(g, "FLOORVIEW", titleFont, Color.White, 28, 20);
            Label(g, "Manufacturing performance dashboard", bodyFont, muted, 32, 67);
            Fill(g, surface, 1062, 30, 166, 30);
            Label(g, "PORTFOLIO DEMO", smallFont, teal, 1075, 36);
            Label(g, "PRODUCTION DATE", smallFont, muted, 32, 91);
            Label(g, "SHIFT FILTER", smallFont, muted, 232, 91);
            Label(g, "Completed shifts / sample dataset", smallFont, muted, 449, 120);

            int count = selectedRows.Count;
            int passed = selectedRows.Count(r => r.FirstPass);
            int reworked = count - passed;
            int goal = DemoData.GoalPerShift * (shiftPicker.SelectedIndex == 0 ? 2 : 1);
            double fpy = count == 0 ? 0 : passed * 100.0 / count;
            Card(g, 32, "COMPLETED UNITS", count.ToString(), "Final completions, including rework", teal);
            Card(g, 336, "FIRST PASS YIELD", fpy.ToString("0.0") + "%",
                passed + " / " + count + " passed on the first attempt", fpy >= 90 ? teal : amber);
            Card(g, 640, "PRODUCTION GOAL", (count * 100.0 / goal).ToString("0.0") + "%",
                count + " completed / " + goal + " planned", teal);
            Card(g, 944, "REWORKED UNITS", reworked.ToString(), "Completed after an initial failed test", amber);

            Fill(g, surface, 32, 296, 770, 218);
            Label(g, "Hourly completions", sectionFont, Color.White, 48, 309);
            Label(g, "Actual completion time, including after midnight", smallFont, muted, 48, 336);
            var hours = selectedRows.GroupBy(r => new DateTime(r.CompletedAt.Year,
                r.CompletedAt.Month, r.CompletedAt.Day, r.CompletedAt.Hour, 0, 0))
                .OrderBy(group => group.Key).ToList();
            int max = Math.Max(8, hours.Count == 0 ? 0 : hours.Max(group => group.Count()));
            float step = 730f / Math.Max(1, hours.Count);
            for (int i = 0; i < hours.Count; i++) {
                float x = 52 + step * i;
                float height = hours[i].Count() * 94f / max;
                Fill(g, teal, x, 470 - height, Math.Max(8, step - 10), height);
                Label(g, hours[i].Count().ToString(), smallFont, Color.White, x + 1, 450 - height);
                Label(g, hours[i].Key.ToString("HH"), smallFont, muted, x, 479);
            }

            Fill(g, surface, 824, 296, 404, 218);
            Label(g, "Initial failure breakdown", sectionFont, Color.White, 840, 309);
            Label(g, "First pass target: 90% (demo setting)", smallFont, muted, 840, 337);
            var failures = selectedRows.Where(r => !r.FirstPass).GroupBy(r => r.FailureReason)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key).ToList();
            for (int i = 0; i < failures.Count; i++) {
                float y = 371 + i * 43;
                Label(g, failures[i].Key, smallFont, Color.White, 840, y);
                Label(g, failures[i].Count().ToString(), smallFont, amber, 1190, y);
                Fill(g, Color.FromArgb(51, 65, 85), 840, y + 23, 364, 7);
                Fill(g, amber, 840, y + 23, 364f * failures[i].Count() / Math.Max(1, reworked), 7);
            }
            Fill(g, surface, 32, 534, 1196, 226);
            Label(g, "Completed unit records", sectionFont, Color.White, 48, 547);
            Label(g, count + " records / scroll to inspect", smallFont, muted, 984, 550);
            Label(g, "SYNTHETIC DATA  |  Demonstration only  |  No live factory or database connection",
                smallFont, muted, 32, 783);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                titleFont.Dispose(); metricFont.Dispose(); bodyFont.Dispose();
                smallFont.Dispose(); sectionFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest();
            if (args.Length > 1 && args[0] == "--export-sample") {
                File.WriteAllText(args[1], DemoData.ToCsv(DemoData.Create()), new UTF8Encoding(true));
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new DashboardForm()) {
                if (args.Length > 1 && args[0] == "--capture") {
                    form.SetFilter(new DateTime(2026, 10, 8), args.Length > 2 ? Int32.Parse(args[2]) : 0);
                    // Render the real controls off-screen without taking focus.
                    form.CaptureMode = true;
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.Show();
                    Application.DoEvents();
                    form.PerformLayout();
                    using (var bitmap = new Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                    return 0;
                }
                Application.Run(form);
            }
            return 0;
        }

        private static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
        private static int SelfTest()
        {
            try {
                var rows = DemoData.Create();
                Require(rows.Count == 354, "Seed record count");
                Require(rows.Select(r => r.UnitId).Distinct().Count() == rows.Count, "Unique IDs");
                var date = new DateTime(2026, 10, 8);
                var both = DemoData.Select(rows, date, 0);
                var first = DemoData.Select(rows, date, 1);
                var second = DemoData.Select(rows, date, 2);
                Require(both.Count == 122 && first.Count == 64 && second.Count == 58, "Date and shift filtering");
                Require(both.Count(r => r.FirstPass) == 101, "First pass denominator includes reworked units");
                Require(Math.Abs(101 * 100.0 / both.Count - 82.786885) < 0.00001, "Yield calculation");
                Require(second.Any(r => r.CompletedAt.Date > date), "After-midnight completions retain production date");
                Require(DemoData.Select(rows, date.AddDays(4), 0).Count == 0, "Empty selection");
                Require(DemoData.ToCsv(first).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length == 65,
                    "Selected CSV record count");
                var escaped = new UnitRecord { ProductionDate = date, Shift = 1, UnitId = "DEMO,\"QUOTE\"",
                    CompletedAt = date, FailureReason = "Reason, test" };
                Require(DemoData.ToCsv(new[] { escaped }).Contains("\"DEMO,\"\"QUOTE\"\"\""), "CSV quoting");
                Console.WriteLine("PASS: 9 checks covering seed data, filters, FPY, midnight handling and CSV output.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
