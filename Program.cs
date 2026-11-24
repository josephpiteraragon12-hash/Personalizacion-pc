using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IsabellaML
{
    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, string data, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(int evt, uint flags, string item1, IntPtr item2);
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int evt, uint flags, IntPtr item1, IntPtr item2);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    // Todo lo que toca Windows vive aquí. Solo usa HKCU y APIs de usuario: no pide administrador.
    static class Tweaks
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IsabellaML");
        const string Desk = @"HKEY_CURRENT_USER\Control Panel\Desktop";
        const string Cur = @"HKEY_CURRENT_USER\Control Panel\Cursors";
        const string Dwm = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";
        const string Pers = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Acc = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

        static string NewPath(string prefix, string ext)
        {
            Directory.CreateDirectory(Dir);
            return Path.Combine(Dir, prefix + DateTime.Now.Ticks + ext);
        }

        static void Prune(string prefix, params string[] keep)
        {
            foreach (var f in Directory.GetFiles(Dir, prefix + "*"))
                if (Array.IndexOf(keep, f) < 0) try { File.Delete(f); } catch { }
        }

        // ---------- Fondo ----------
        public static void SetWallpaper(string src, string style)
        {
            string dst, ext = Path.GetExtension(src).ToLowerInvariant();
            using (var img = Image.FromFile(src))
            {
                // Más de 4K no se ve mejor y gasta memoria: se reduce.
                float k = Math.Min(1f, 3840f / Math.Max(img.Width, img.Height));
                if (k < 1f)
                {
                    dst = NewPath("wp_", ".jpg");
                    using (var small = new Bitmap(img, (int)(img.Width * k), (int)(img.Height * k)))
                    {
                        var enc = Array.Find(ImageCodecInfo.GetImageEncoders(), e => e.FormatID == ImageFormat.Jpeg.Guid);
                        var ps = new EncoderParameters(1);
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        small.Save(dst, enc, ps);
                    }
                }
                else { dst = NewPath("wp_", ext); File.Copy(src, dst); }
            }
            string ws = "10", tile = "0";
            switch (style)
            {
                case "Ajustar": ws = "6"; break;
                case "Estirar": ws = "2"; break;
                case "Mosaico": ws = "0"; tile = "1"; break;
                case "Centrar": ws = "0"; break;
                case "Expandir": ws = "22"; break;
            }
            Registry.SetValue(Desk, "WallpaperStyle", ws, RegistryValueKind.String);
            Registry.SetValue(Desk, "TileWallpaper", tile, RegistryValueKind.String);
            if (!Native.SystemParametersInfo(0x14, 0, dst, 0x03))
                throw new Exception("Windows rechazó el fondo (código " + Marshal.GetLastWin32Error() + ")");
            Prune("wp_", dst);
        }

        // ---------- Cursor ----------
        public static void SetCursor(Bitmap bmp)
        {
            string a = NewPath("arrow_", ".cur"), h = NewPath("hand_", ".cur");
            WriteIcon(bmp, a, true);
            File.Copy(a, h);
            Registry.SetValue(Cur, "Arrow", a, RegistryValueKind.ExpandString);
            Registry.SetValue(Cur, "Hand", h, RegistryValueKind.ExpandString);
            Native.SystemParametersInfo(0x57, 0, null, 0x03);
            Prune("arrow_", a); Prune("hand_", h);
        }

        public static void ResetCursor()
        {
            Registry.SetValue(Cur, "Arrow", @"%SystemRoot%\cursors\aero_arrow.cur", RegistryValueKind.ExpandString);
            Registry.SetValue(Cur, "Hand", @"%SystemRoot%\cursors\aero_link.cur", RegistryValueKind.ExpandString);
            Native.SystemParametersInfo(0x57, 0, null, 0x03);
            Prune("arrow_"); Prune("hand_");
        }

        // ---------- Colores ----------
        public static void SetDark(bool dark)
        {
            int v = dark ? 0 : 1;
            Registry.SetValue(Pers, "AppsUseLightTheme", v, RegistryValueKind.DWord);
            Registry.SetValue(Pers, "SystemUsesLightTheme", v, RegistryValueKind.DWord);
            Notify();
        }

        public static void SetAccent(Color c)
        {
            int abgr = unchecked((int)(0xFF000000u | (uint)(c.B << 16 | c.G << 8 | c.R)));
            int argb = unchecked((int)(0xC4000000u | (uint)(c.R << 16 | c.G << 8 | c.B)));
            Registry.SetValue(Desk, "AutoColorization", 0, RegistryValueKind.DWord);
            Registry.SetValue(Dwm, "AccentColor", abgr, RegistryValueKind.DWord);
            Registry.SetValue(Dwm, "ColorizationColor", argb, RegistryValueKind.DWord);
            Registry.SetValue(Dwm, "ColorPrevalence", 1, RegistryValueKind.DWord);
            Registry.SetValue(Pers, "ColorPrevalence", 1, RegistryValueKind.DWord);
            Registry.SetValue(Acc, "AccentColorMenu", abgr, RegistryValueKind.DWord);
            Registry.SetValue(Acc, "StartColorMenu", abgr, RegistryValueKind.DWord);
            Notify();
        }

        static void Notify()
        {
            IntPtr r;
            Native.SendMessageTimeout((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out r);
        }

        // ---------- Iconos de carpeta ----------
        public static void SetFolderIcon(string folder, string img)
        {
            ClearFolderIcon(folder);
            string ico = Path.Combine(folder, "isabella_" + DateTime.Now.Ticks + ".ico");
            string ini = Path.Combine(folder, "desktop.ini");
            using (var src = Image.FromFile(img))
            using (var b = Render(src, 256, true)) WriteIcon(b, ico, false);
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + Path.GetFileName(ico) + ",0\r\n", Encoding.Unicode);
            File.SetAttributes(ico, FileAttributes.Hidden);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            Refresh(folder);
        }

        public static void ClearFolderIcon(string folder)
        {
            var old = new List<string>(Directory.GetFiles(folder, "isabella_*.ico"));
            old.Add(Path.Combine(folder, "desktop.ini"));
            foreach (var p in old)
                if (File.Exists(p)) { File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); }
            File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            Refresh(folder);
        }

        static void Refresh(string folder)
        {
            Native.SHChangeNotify(0x00002000, 0x0005, folder, IntPtr.Zero);
            Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        // ---------- Imágenes ----------
        public static Bitmap Render(Image s, int n, bool cover)
        {
            var b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                float ka = (float)n / s.Width, kb = (float)n / s.Height;
                float k = cover ? Math.Max(ka, kb) : Math.Min(ka, kb);
                float w = s.Width * k, h = s.Height * k;
                g.DrawImage(s, (n - w) / 2, (n - h) / 2, w, h);
            }
            return b;
        }

        // .ico / .cur con PNG dentro (Windows Vista en adelante). El "punto de clic" del cursor es el centro.
        static void WriteIcon(Bitmap b, string path, bool cursor)
        {
            byte[] png;
            using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)(cursor ? 2 : 1)); w.Write((short)1);
                w.Write((byte)(b.Width % 256)); w.Write((byte)(b.Height % 256)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)(cursor ? b.Width / 2 : 1)); w.Write((short)(cursor ? b.Height / 2 : 32));
                w.Write(png.Length); w.Write(22); w.Write(png);
            }
        }
    }

    // Cursores prediseñados, dibujados por código: cero archivos extra, el .exe pesa muy poco.
    static class Art
    {
        public static Bitmap Cursor(string kind, Color c)
        {
            var b = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            var ps = new List<GraphicsPath>();
            Func<GraphicsPath> P = () => { var p = new GraphicsPath(); ps.Add(p); return p; };
            switch (kind)
            {
                case "heart":
                    var h = P();
                    h.AddBezier(16, 29, -2, 16, 4, 2, 16, 9);
                    h.AddBezier(16, 9, 28, 2, 34, 16, 16, 29);
                    h.CloseFigure();
                    break;
                case "star":
                    var pts = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 13.5 : 5.8;
                        pts[i] = new PointF((float)(16 + r * Math.Cos(a)), (float)(16.5 + r * Math.Sin(a)));
                    }
                    P().AddPolygon(pts);
                    break;
                case "bow":
                    P().AddPolygon(new[] { new PointF(2, 7), new PointF(15, 16), new PointF(2, 25) });
                    P().AddPolygon(new[] { new PointF(30, 7), new PointF(17, 16), new PointF(30, 25) });
                    P().AddEllipse(11, 11, 10, 10);
                    break;
                case "paw":
                    P().AddEllipse(8, 15, 16, 13);
                    P().AddEllipse(2, 9, 7, 9); P().AddEllipse(9, 3, 7, 9);
                    P().AddEllipse(17, 3, 7, 9); P().AddEllipse(23, 9, 7, 9);
                    break;
            }
            using (var g = Graphics.FromImage(b))
            using (var br = new SolidBrush(c))
            using (var pen = new Pen(Color.White, 3f) { LineJoin = LineJoin.Round })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var p in ps) g.DrawPath(pen, p);
                foreach (var p in ps) g.FillPath(br, p);
            }
            foreach (var p in ps) p.Dispose();
            return b;
        }
    }

    sealed class MainForm : Form
    {
        const string Releases = "https://github.com/TU_USUARIO/IsabellaML/releases/latest";
        static readonly Color Orange = Color.FromArgb(249, 115, 22), Ink = Color.FromArgb(31, 31, 35),
            Paper = Color.FromArgb(251, 248, 246), Line = Color.FromArgb(229, 222, 216);
        static readonly string[][] Presets = {
            new[] { "Corazón rosa", "heart", "#FF5C8A" }, new[] { "Corazón rojo", "heart", "#E5253D" },
            new[] { "Lazo rosa", "bow", "#FF8FB1" },      new[] { "Lazo naranja", "bow", "#FB923C" },
            new[] { "Estrella", "star", "#FFC93C" },      new[] { "Huellita", "paw", "#C98B5E" } };

        readonly Panel[] pages = new Panel[4];
        readonly Button[] nav = new Button[4];
        readonly Label status;
        readonly Panel marker;
        PictureBox pic;
        Button apply, selTile;
        ComboBox fit;
        TextBox folderBox;
        string wpPath, folderPath;
        Bitmap curBmp;

        public MainForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Isabella ML Studio";
            Font = new Font("Segoe UI", 10f);
            BackColor = Paper;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 470);
            DoubleBuffered = true;

            var side = new Panel { Location = new Point(0, 0), Size = new Size(200, 470), BackColor = Ink };
            var host = new Panel { Location = new Point(200, 0), Size = new Size(580, 470), BackColor = Paper };
            Controls.Add(side); Controls.Add(host);

            Lbl(side, "Isabella", 22, 22, 18, true).ForeColor = Color.White;
            Lbl(side, "ML Studio", 24, 58, 10, false).ForeColor = Orange;

            string[] names = { "Fondo de pantalla", "Cursor", "Colores", "Carpetas" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                nav[i] = new Button
                {
                    Text = names[i], Location = new Point(0, 110 + i * 46), Size = new Size(200, 44),
                    FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(24, 0, 0, 0),
                    BackColor = Ink, ForeColor = Color.FromArgb(200, 200, 206), Cursor = Cursors.Hand, UseVisualStyleBackColor = false
                };
                nav[i].FlatAppearance.BorderSize = 0;
                nav[i].FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 42, 48);
                nav[i].Click += (o, ev) => ShowPage(idx);
                side.Controls.Add(nav[i]);

                pages[i] = new Panel { Location = new Point(0, 0), Size = new Size(580, 430), BackColor = Paper, Visible = false };
                host.Controls.Add(pages[i]);
            }
            marker = new Panel { Size = new Size(4, 44), BackColor = Orange };
            side.Controls.Add(marker);

            var ver = Lbl(side, "v" + Application.ProductVersion, 24, 404, 8, false);
            ver.ForeColor = Color.FromArgb(130, 130, 138);
            var upd = Lbl(side, "Buscar actualizaciones", 24, 428, 9, false);
            upd.ForeColor = Color.FromArgb(170, 170, 178); upd.Cursor = Cursors.Hand;
            upd.Click += (o, ev) => Run(() => Process.Start(new ProcessStartInfo(Releases) { UseShellExecute = true }), "Abriendo GitHub…");

            status = new Label { Location = new Point(28, 436), Size = new Size(524, 24), AutoEllipsis = true, BackColor = Color.Transparent };
            host.Controls.Add(status);

            BuildWallpaper(pages[0]); BuildCursor(pages[1]); BuildColors(pages[2]); BuildFolders(pages[3]);
            ShowPage(0);
        }

        // ---------- helpers ----------
        Label Lbl(Control p, string t, int x, int y, float size, bool bold, bool gray = false)
        {
            var l = new Label
            {
                Text = t, AutoSize = true, MaximumSize = new Size(520, 0), Location = new Point(x, y), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = gray ? Color.FromArgb(120, 112, 105) : Ink
            };
            p.Controls.Add(l);
            return l;
        }

        Button Btn(Control p, string t, int x, int y, int w, bool primary, EventHandler click)
        {
            var b = new Button
            {
                Text = t, Location = new Point(x, y), Size = new Size(w, 40), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Orange : Color.White, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.Click += click;
            p.Controls.Add(b);
            return b;
        }

        void Run(Action a, string ok)
        {
            try { a(); status.ForeColor = Color.FromArgb(90, 90, 90); status.Text = ok; }
            catch (Exception ex) { status.ForeColor = Color.FromArgb(200, 40, 40); status.Text = "Error: " + ex.Message; }
        }

        void ShowPage(int i)
        {
            for (int j = 0; j < 4; j++)
            {
                pages[j].Visible = j == i;
                nav[j].BackColor = j == i ? Color.FromArgb(42, 42, 48) : Ink;
                nav[j].ForeColor = j == i ? Color.White : Color.FromArgb(200, 200, 206);
            }
            marker.Top = nav[i].Top; marker.BringToFront();
            status.Text = "";
        }

        static string PickImage()
        {
            using (var d = new OpenFileDialog { Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp" })
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        }

        // ---------- Fondo ----------
        void BuildWallpaper(Panel p)
        {
            Lbl(p, "Fondo de pantalla", 28, 22, 18, true);
            Lbl(p, "Elige una foto, ajústala y listo.", 30, 58, 10, false, true);
            pic = new PictureBox { Location = new Point(28, 96), Size = new Size(340, 191), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(238, 233, 229), AllowDrop = true };
            pic.Paint += (o, ev) =>
            {
                if (pic.Image == null)
                    TextRenderer.DrawText(ev.Graphics, "Arrastra una foto aquí", Font, pic.ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            pic.DragEnter += (o, ev) => { if (ev.Data.GetDataPresent(DataFormats.FileDrop)) ev.Effect = DragDropEffects.Copy; };
            pic.DragDrop += (o, ev) => LoadWp(((string[])ev.Data.GetData(DataFormats.FileDrop))[0]);
            p.Controls.Add(pic);

            Lbl(p, "Ajuste", 388, 96, 10, true);
            fit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(388, 122), Width = 164 };
            fit.Items.AddRange(new object[] { "Rellenar", "Ajustar", "Estirar", "Mosaico", "Centrar", "Expandir" });
            fit.SelectedIndex = 0;
            p.Controls.Add(fit);

            Btn(p, "Elegir foto…", 388, 172, 164, false, (o, ev) => { var f = PickImage(); if (f != null) LoadWp(f); });
            apply = Btn(p, "Aplicar como fondo", 388, 222, 164, true, (o, ev) =>
                Run(() => Tweaks.SetWallpaper(wpPath, (string)fit.SelectedItem), "Fondo aplicado."));
            apply.Enabled = false;
            Lbl(p, "Se aplica a todos los monitores.", 388, 276, 9, false, true).MaximumSize = new Size(164, 0);
        }

        void LoadWp(string f)
        {
            Run(() =>
            {
                using (var img = Image.FromFile(f))
                {
                    float k = Math.Min(340f / img.Width, 191f / img.Height);
                    var old = pic.Image;
                    pic.Image = new Bitmap(img, Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
                    if (old != null) old.Dispose();
                }
                wpPath = f; apply.Enabled = true;
            }, "Foto cargada. Pulsa Aplicar como fondo.");
        }

        // ---------- Cursor ----------
        void BuildCursor(Panel p)
        {
            Lbl(p, "Cursor", 28, 22, 18, true);
            Lbl(p, "Elige uno o carga tu PNG. No necesita administrador.", 30, 58, 10, false, true);
            var flow = new FlowLayoutPanel { Location = new Point(28, 96), Size = new Size(524, 192), BackColor = Color.Transparent };
            p.Controls.Add(flow);
            foreach (var pr in Presets)
            {
                var bmp = Art.Cursor(pr[1], ColorTranslator.FromHtml(pr[2]));
                var t = new Button
                {
                    Size = new Size(84, 84), Margin = new Padding(0, 0, 12, 12), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink,
                    Image = bmp, Tag = bmp, Text = pr[0], Font = new Font("Segoe UI", 8f), ImageAlign = ContentAlignment.TopCenter,
                    TextAlign = ContentAlignment.BottomCenter, TextImageRelation = TextImageRelation.ImageAboveText, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
                };
                t.FlatAppearance.BorderColor = Line;
                t.Click += (o, ev) => SelectTile((Button)o);
                flow.Controls.Add(t);
            }
            SelectTile((Button)flow.Controls[0]);

            Btn(p, "Aplicar cursor", 28, 300, 180, true, (o, ev) => Run(() => Tweaks.SetCursor(curBmp), "Cursor aplicado."));
            Btn(p, "Usar mi PNG…", 220, 300, 170, false, (o, ev) =>
            {
                var f = PickImage();
                if (f == null) return;
                Run(() => { using (var img = Image.FromFile(f)) using (var b = Tweaks.Render(img, 32, false)) Tweaks.SetCursor(b); }, "Cursor aplicado.");
            });
            Btn(p, "Restaurar", 402, 300, 150, false, (o, ev) => Run(Tweaks.ResetCursor, "Cursor original restaurado."));
        }

        void SelectTile(Button t)
        {
            if (selTile != null) { selTile.FlatAppearance.BorderColor = Line; selTile.FlatAppearance.BorderSize = 1; }
            selTile = t;
            t.FlatAppearance.BorderColor = Orange; t.FlatAppearance.BorderSize = 2;
            curBmp = (Bitmap)t.Tag;
        }

        // ---------- Colores ----------
        void BuildColors(Panel p)
        {
            Lbl(p, "Colores de Windows", 28, 22, 18, true);
            Lbl(p, "Modo", 28, 96, 11, true);
            Btn(p, "Oscuro (negro)", 28, 128, 170, false, (o, ev) => Run(() => Tweaks.SetDark(true), "Modo oscuro activado."));
            Btn(p, "Claro (blanco)", 210, 128, 170, false, (o, ev) => Run(() => Tweaks.SetDark(false), "Modo claro activado."));
            Lbl(p, "Color de acento", 28, 196, 11, true);
            var flow = new FlowLayoutPanel { Location = new Point(28, 228), Size = new Size(524, 60), BackColor = Color.Transparent };
            p.Controls.Add(flow);
            foreach (var hex in new[] { "#FF5C8A", "#F97316", "#FFD400", "#B57EDC", "#3B82F6", "#22C55E", "#2B2B2B" })
            {
                var col = ColorTranslator.FromHtml(hex);
                var sw = new Button { Size = new Size(44, 44), Margin = new Padding(0, 0, 10, 10), BackColor = col, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
                sw.FlatAppearance.BorderSize = 0;
                sw.Click += (o, ev) => Run(() => Tweaks.SetAccent(col), "Color aplicado.");
                flow.Controls.Add(sw);
            }
            Btn(flow, "Otro…", 0, 0, 80, false, (o, ev) =>
            {
                using (var d = new ColorDialog { FullOpen = true })
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    var c = d.Color;
                    Run(() => Tweaks.SetAccent(c), "Color aplicado.");
                }
            });
            Lbl(p, "Si la barra de tareas no cambia, cierra sesión y vuelve a entrar.", 28, 300, 9, false, true);
        }

        // ---------- Carpetas ----------
        void BuildFolders(Panel p)
        {
            Lbl(p, "Carpetas con foto", 28, 22, 18, true);
            Lbl(p, "Elige una carpeta y una foto: la foto pasa a ser su icono.", 30, 58, 10, false, true);
            folderBox = new TextBox { Location = new Point(28, 100), Width = 524, ReadOnly = true };
            p.Controls.Add(folderBox);
            Btn(p, "Elegir carpeta…", 28, 146, 170, false, (o, ev) =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                    if (d.ShowDialog() == DialogResult.OK) { folderPath = d.SelectedPath; folderBox.Text = folderPath; }
            });
            Btn(p, "Elegir foto y aplicar", 210, 146, 200, true, (o, ev) =>
            {
                if (folderPath == null) { status.Text = "Primero elige una carpeta."; return; }
                var f = PickImage();
                if (f != null) Run(() => Tweaks.SetFolderIcon(folderPath, f), "Icono de carpeta aplicado.");
            });
            Btn(p, "Quitar foto", 422, 146, 130, false, (o, ev) =>
            {
                if (folderPath == null) { status.Text = "Primero elige una carpeta."; return; }
                Run(() => Tweaks.ClearFolderIcon(folderPath), "Icono original restaurado.");
            });
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
