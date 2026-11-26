using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    static class Theme
    {
        public static readonly Color Bar = Color.FromArgb(34, 41, 51), Panel = Color.FromArgb(61, 70, 84), Field = Color.FromArgb(33, 40, 48), Text = Color.FromArgb(235, 238, 242), Dim = Color.FromArgb(160, 170, 185), Accent = Color.FromArgb(0, 120, 215);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        public static void DarkTitle(Form f)
        {
            f.HandleCreated += (s, e) => { try { int on = 1; if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4); } catch { } };
        }
        public static void DarkList(ListView lv)
        {
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            lv.DrawItem += (s, e) => { if ((lv.View != View.Details)) e.DrawDefault = true; };
            lv.DrawSubItem += (s, e) =>
            {
                bool sel = e.Item.Selected; var back = sel ? Accent : Field;
                using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
        public static Form Base(string title, int w, int h)
        {
            var f = new Form { Text = title, BackColor = Bar, ForeColor = Text, Font = new Font("Segoe UI", 9f), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(w, h), ShowInTaskbar = false };
            DarkTitle(f);
            return f;
        }
        public static Label Lbl(string t, int x, int y, int w = 360, bool dim = false) { return new Label { Text = t, Left = x, Top = y, Width = w, AutoSize = false, Height = 20, ForeColor = dim ? Dim : Text, BackColor = Color.Transparent }; }
        public static Button Btn(string t, int x, int y, int w, bool primary = false)
        {
            var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Text, BackColor = primary ? Accent : Color.FromArgb(86, 98, 116), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0; return b;
        }
        public static void Style(Control c) { c.BackColor = Field; c.ForeColor = Text; }
    }

    static class CsPicker
    {
        static readonly int[] Codes = { 32637, 32638, 32639, 20437, 20438, 20439, 4326, 3857, 0 };
        static readonly string[] Names = { "WGS 84 / UTM zone 37N (EPSG:32637)", "WGS 84 / UTM zone 38N (EPSG:32638)", "WGS 84 / UTM zone 39N (EPSG:32639)", "Ain el Abd / UTM zone 37N (EPSG:20437)", "Ain el Abd / UTM zone 38N (EPSG:20438)", "Ain el Abd / UTM zone 39N (EPSG:20439)", "WGS 84 longitude/latitude (EPSG:4326)", "Web Mercator (EPSG:3857)", "Other EPSG code..." };
        public static int Ask()
        {
            using (var f = Theme.Base("BPlus Live Map - coordinate system", 420, 330))
            {
                f.Controls.Add(Theme.Lbl("This drawing has no coordinate system. Choose the one it uses:", 16, 14, 390));
                var list = new ListBox { Left = 16, Top = 42, Width = 388, Height = 170, BorderStyle = BorderStyle.None };
                Theme.Style(list); list.Items.AddRange(Names); list.SelectedIndex = 2;
                var other = new TextBox { Left = 16, Top = 222, Width = 120, BorderStyle = BorderStyle.FixedSingle, Visible = false };
                Theme.Style(other);
                var hint = Theme.Lbl("EPSG code:", 144, 225, 200, true); hint.Visible = false;
                list.SelectedIndexChanged += (s, e) => { bool o = list.SelectedIndex == Codes.Length - 1; other.Visible = o; hint.Visible = o; };
                f.Controls.AddRange(new Control[] { list, other, hint });
                f.Controls.Add(Theme.Lbl("The choice is saved inside this drawing. Change it later with BPGMAPCS.", 16, 256, 390, true));
                var ok = Theme.Btn("OK", 224, 288, 85, true); var cancel = Theme.Btn("Cancel", 319, 288, 85);
                int result = 0;
                ok.Click += (s, e) =>
                {
                    int c = Codes[list.SelectedIndex];
                    if (c == 0) { if (!int.TryParse(other.Text.Trim(), out c) || CoordSys.FromEpsg(c) == null) { MessageBox.Show(f, "That EPSG code is not supported. Supported: 4326, 3857, 326xx and 327xx UTM, 20435 to 20439.", "BPlus Live Map"); return; } }
                    result = c; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? result : 0;
            }
        }
    }

    static class SettingsDialog
    {
        public static void Show()
        {
            using (var f = Theme.Base("BPlus Live Map - settings", 460, 560))
            {
                int y = 14;
                var shared = new RadioButton { Left = 16, Top = y, Width = 428, Text = Config.SharedAvailable ? "Use the BPlus map server (no key needed, shared monthly limit)" : "BPlus map server: not available yet (use your own key)", Enabled = Config.SharedAvailable, Checked = !Config.UseOwnKey, ForeColor = Theme.Text, BackColor = Color.Transparent }; f.Controls.Add(shared); y += 24;
                var own = new RadioButton { Left = 16, Top = y, Width = 428, Text = "Use my own Google Maps Platform key (my own Google account and cost)", Checked = Config.UseOwnKey, ForeColor = Theme.Text, BackColor = Color.Transparent }; f.Controls.Add(own); y += 30;
                f.Controls.Add(Theme.Lbl("Your Google key (only for 'my own key')", 16, y)); y += 22;
                var key = new TextBox { Left = 16, Top = y, Width = 428, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
                Theme.Style(key); if (KeyStore.HasKey) key.Text = "";
                f.Controls.Add(key); y += 26;
                f.Controls.Add(Theme.Lbl(KeyStore.HasKey ? "A key is saved on this PC. Leave empty to keep it." : "No key saved yet. Paste it here. It stays on this PC only.", 16, y, 428, true)); y += 30;
                f.Controls.Add(Theme.Lbl("Map type", 16, y, 100));
                var kind = new ComboBox { Left = 130, Top = y - 3, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
                Theme.Style(kind); kind.Items.AddRange(new object[] { "Satellite", "Roadmap", "Hybrid (satellite + labels)" });
                kind.SelectedIndex = (int)Config.Kind == (int)MapKind.Satellite ? 0 : Config.Kind == MapKind.Roadmap ? 1 : 2;
                f.Controls.Add(kind); y += 34;
                f.Controls.Add(Theme.Lbl("Opacity %", 16, y, 100));
                var op = new NumericUpDown { Left = 130, Top = y - 3, Width = 80, Minimum = 10, Maximum = 100, Value = Config.OpacityPercent, BorderStyle = BorderStyle.FixedSingle };
                Theme.Style(op); f.Controls.Add(op); y += 34;
                f.Controls.Add(Theme.Lbl("Warn me at (requests)", 16, y, 120));
                var warn = new NumericUpDown { Left = 130, Top = y - 3, Width = 100, Minimum = 100, Maximum = 10000000, Increment = 5000, Value = Math.Min(10000000, Math.Max(100, Config.WarnAt)), BorderStyle = BorderStyle.FixedSingle };
                Theme.Style(warn); f.Controls.Add(warn); y += 34;
                f.Controls.Add(Theme.Lbl("Map requests from this PC this month: " + Engine.Usage.ThisMonth, 16, y, 428, true)); y += 30;
                var flip = new CheckBox { Left = 16, Top = y, Width = 428, Text = "Flip image rows (turn on/off if the map looks upside down)", Checked = Config.FlipRows, ForeColor = Theme.Text, BackColor = Color.Transparent }; f.Controls.Add(flip); y += 32;
                var terms = new CheckBox { Left = 16, Top = y, Width = 428, Height = 62, Text = "I agree: the map includes Google Maps content, and my use is subject to the Google Maps/Google Earth Additional Terms (maps.google.com/help/terms_maps) and the Google Privacy Policy (policies.google.com/privacy). I will not trace or digitize from the imagery.", ForeColor = Theme.Text, BackColor = Color.Transparent, Checked = Config.TermsAccepted }; f.Controls.Add(terms); y += 68;
                var status = Theme.Lbl("", 16, y, 428, true); status.Height = 36; f.Controls.Add(status);
                var test = Theme.Btn("Test connection", 16, 516, 120);
                var ok = Theme.Btn("Save", 264, 516, 85, true); var cancel = Theme.Btn("Cancel", 359, 516, 85);
                test.Click += async (s, e) =>
                {
                    Config.UseOwnKey = own.Checked; if (key.Text.Trim().Length > 0) KeyStore.Set(key.Text.Trim());
                    status.ForeColor = Theme.Dim; status.Text = "Testing...";
                    try
                    {
                        var r = await Engine.Client.GetTileAsync(MapKind.Satellite, new TileId(10, 644, 439), System.Threading.CancellationToken.None);
                        status.ForeColor = r.Data != null ? Color.FromArgb(120, 210, 140) : Color.FromArgb(240, 140, 120);
                        status.Text = r.Data != null ? "Connected. The key works." : (r.Error ?? "Could not get a tile.");
                    }
                    catch (Exception ex) { status.ForeColor = Color.FromArgb(240, 140, 120); status.Text = ex.Message; }
                };
                ok.Click += (s, e) =>
                {
                    Config.UseOwnKey = own.Checked;
                    if (key.Text.Trim().Length > 0) KeyStore.Set(key.Text.Trim());
                    Config.Kind = kind.SelectedIndex == 0 ? MapKind.Satellite : kind.SelectedIndex == 1 ? MapKind.Roadmap : MapKind.Hybrid;
                    Config.OpacityPercent = (int)op.Value; Config.WarnAt = (int)warn.Value; Config.FlipRows = flip.Checked; Config.TermsAccepted = terms.Checked;
                    Engine.RefreshNow(); f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { test, ok, cancel }); f.CancelButton = cancel;
                f.ShowDialog();
            }
        }
    }
}
