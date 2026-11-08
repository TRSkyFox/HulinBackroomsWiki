using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExpeditionRecorder
{
    /* ══ 原生 ══════════════════════════════════════════════ */
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winini);

        static bool _comboAnimWasOn;

        public static void DisableComboAnimation()
        {
            try
            {
                bool on = false;
                if (SystemParametersInfo(0x1004, 0, ref on, 0))
                {
                    _comboAnimWasOn = on;
                    if (on)
                    {
                        bool off = false;
                        SystemParametersInfo(0x1005, 0, ref off, 2);
                    }
                }
            }
            catch { }
        }

        public static void RestoreComboAnimation()
        {
            try
            {
                if (!_comboAnimWasOn) return;
                bool on = true;
                SystemParametersInfo(0x1005, 0, ref on, 2);
            }
            catch { }
        }

        public static void DarkScroll(Control c)
        {
            if (c == null) return;
            Action apply = delegate
            {
                try
                {
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                    int on = 1;
                    DwmSetWindowAttribute(c.Handle, 20, ref on, sizeof(int));
                    var form = c.FindForm();
                    if (form != null)
                    {
                        SetWindowTheme(form.Handle, "DarkMode_Explorer", null);
                        DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
                    }
                }
                catch { }
            };
            if (c.IsHandleCreated)
            {
                apply();
                try { c.BeginInvoke((Action)apply); } catch { }
            }
            else c.HandleCreated += delegate { apply(); };
        }
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hwnd, bool enable);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int n);
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDst, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    }

    /* ══ OCR 行 ═════════════════════════════════════════════ */
    class OcrLine
    {
        public string Text;
        public double Score;
        public Color TextColor = Color.Empty;
        public int X, Y, W, H;
        public bool IsFloor, IsTitle, IsOption, IsChrome, IsCondition, IsSpecial, Selected;
        public string Check = "";
        public string Condition = "";
        public string Requirement = "";
        public string Desc = "";
        public string ReqType = "";
        public string ReqName = "";
        public bool ReqConsume = false;
        public int ReqMin = 0;
        public int OptionGroup = -1;
        public int ConditionFor = -1;
        public int CenterX { get { return X + W / 2; } }
        public int CenterY { get { return Y + H / 2; } }
        public Rectangle Rect { get { return new Rectangle(X, Y, W, H); } }
    }

    /* ══ OCR 服務（rapid-primary-service，TCP） ═════════════ */
    class OcrService
    {
        Process _proc;
        TcpClient _client;
        NetworkStream _stream;
        string _work;
        readonly object _sync = new object();

        public bool Ready { get { return _stream != null; } }

        bool _starting;

        public bool Start(string exeDir)
        {
            lock (_sync)
            {
                if (_stream != null) return true;
                if (_starting) return false;
                _starting = true;
            }
            try
            {
                return StartLocked(exeDir);
            }
            finally
            {
                lock (_sync) { _starting = false; }
            }
        }

        bool StartLocked(string exeDir)
        {
            string exe = Path.Combine(exeDir, "Recorder", "structured", "rapid-primary-service", "rapid-primary-service.exe");
            if (!File.Exists(exe)) exe = Path.Combine(exeDir, "structured", "rapid-primary-service", "rapid-primary-service.exe");
            if (!File.Exists(exe)) return false;

            _work = Path.Combine(exeDir, "_ocrwork");
            try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch { }
            try { Directory.CreateDirectory(_work); } catch { }

            var psi = new ProcessStartInfo(exe, "\"" + _work + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = _work;
            _proc = Process.Start(psi);

            string ready = Path.Combine(_work, "ready");
            string portFile = Path.Combine(_work, "port");
            var deadline = DateTime.Now.AddSeconds(40);
            while (DateTime.Now < deadline && !File.Exists(ready)) Thread.Sleep(100);
            if (!File.Exists(ready)) return false;
            while (DateTime.Now < deadline && !File.Exists(portFile)) Thread.Sleep(50);
            if (!File.Exists(portFile)) return false;
            int port = int.Parse(File.ReadAllText(portFile).Trim());

            _client = new TcpClient();
            _client.Connect("127.0.0.1", port);
            _stream = _client.GetStream();
            return true;
        }

        static byte[] ReadExact(NetworkStream s, int n)
        {
            byte[] buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) throw new Exception("connection closed");
                off += r;
            }
            return buf;
        }

        public List<OcrLine> Recognize(Bitmap bmp)
        {
            lock (_sync) { return RecognizeLocked(bmp); }
        }

        List<OcrLine> RecognizeLocked(Bitmap bmp)
        {
            var list = new List<OcrLine>();
            if (_stream == null) return list;
            byte[] png;
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
            try
            {
                byte[] len = BitConverter.GetBytes(png.Length);
                _stream.Write(len, 0, 4);
                _stream.Write(png, 0, png.Length);
                _stream.Flush();
                byte[] hdr = ReadExact(_stream, 4);
                int n = BitConverter.ToInt32(hdr, 0);
                if (n <= 0 || n > 16 * 1024 * 1024) return list;
                byte[] payload = ReadExact(_stream, n);
                string json = Encoding.UTF8.GetString(payload);
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 32 * 1024 * 1024;
                var root = ser.Deserialize<Dictionary<string, object>>(json);
                object rowsObj;
                if (root == null || !root.TryGetValue("rows", out rowsObj)) return list;
                var rows = rowsObj as System.Collections.ArrayList;
                if (rows == null) return list;
                foreach (var ro in rows)
                {
                    var row = ro as Dictionary<string, object>;
                    if (row == null) continue;
                    string text = row.ContainsKey("text") ? Convert.ToString(row["text"]) : "";
                    if (string.IsNullOrEmpty(text)) continue;
                    double score = row.ContainsKey("score") ? Convert.ToDouble(row["score"]) : 0;
                    int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                    var box = row.ContainsKey("box") ? row["box"] as System.Collections.ArrayList : null;
                    if (box == null) continue;
                    foreach (var po in box)
                    {
                        var pt = po as System.Collections.ArrayList;
                        if (pt == null || pt.Count < 2) continue;
                        int x = (int)Math.Round(Convert.ToDouble(pt[0]));
                        int y = (int)Math.Round(Convert.ToDouble(pt[1]));
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                    int rw = Math.Max(1, maxX - minX);
                    int rh = Math.Max(1, maxY - minY);
                    Color tc = SampleTextColor(bmp, new Rectangle(minX, minY, rw, rh));
                    list.Add(new OcrLine { Text = text, Score = score, X = minX, Y = minY, W = rw, H = rh, TextColor = tc });
                }
            }
            catch
            {
                try { _stream.Close(); } catch { }
                try { _client.Close(); } catch { }
                _stream = null;
            }
            return list;
        }

        /* 取这一行的文字主色：先找出现最多的背景色，再取与背景差最大的
           一小部分像素平均。用来区分选项（白/米白）与底部金色“继续”提示、
           灰色“人看完了”计数，避免把它们当成选项。 */
        static Color SampleTextColor(Bitmap bmp, Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width < 2 || r.Height < 2) return Color.Empty;
            var counts = new Dictionary<int, int>();
            int sx = Math.Max(1, r.Width / 80);
            int sy = Math.Max(1, r.Height / 8);
            for (int y = r.Top; y < r.Bottom; y += sy)
            {
                for (int x = r.Left; x < r.Right; x += sx)
                {
                    Color c = bmp.GetPixel(x, y);
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    int n;
                    counts.TryGetValue(key, out n);
                    counts[key] = n + 1;
                }
            }
            int best = -1, bestKey = 0;
            foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
            Color bg = Color.FromArgb(((bestKey >> 8) & 0xF) * 17, ((bestKey >> 4) & 0xF) * 17, (bestKey & 0xF) * 17);

            var pixels = new List<int[]>();
            for (int y = r.Top; y < r.Bottom; y += sy)
            {
                for (int x = r.Left; x < r.Right; x += sx)
                {
                    Color c = bmp.GetPixel(x, y);
                    int d = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
                    if (d > 45) pixels.Add(new int[] { d, c.R, c.G, c.B });
                }
            }
            if (pixels.Count == 0) return bg;
            pixels.Sort(delegate (int[] a, int[] b) { return b[0].CompareTo(a[0]); });
            int take = Math.Max(1, pixels.Count / 8);
            long rr = 0, gg = 0, bb = 0;
            for (int i = 0; i < take; i++) { rr += pixels[i][1]; gg += pixels[i][2]; bb += pixels[i][3]; }
            return Color.FromArgb((int)(rr / take), (int)(gg / take), (int)(bb / take));
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopLocked();
            }
        }

        void StopLocked()
        {
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _stream = null;
            try { if (_work != null) File.WriteAllText(Path.Combine(_work, "stop"), ""); } catch { }
            try { if (_proc != null && !_proc.HasExited) _proc.WaitForExit(2500); } catch { }
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        }
    }

    /* ══ 抓圖 ═══════════════════════════════════════════════ */
    static class Capture
    {
        public static IntPtr FindGame()
        {
            var pids = new List<int>();
            string[] names = { "ExpeditionClient", "ExpeditionPlayer" };
            foreach (var n in names)
            {
                try { foreach (var p in Process.GetProcessesByName(n)) pids.Add(p.Id); } catch { }
            }
            foreach (var pid in pids)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
                catch { }
            }
            foreach (var pid in pids)
            {
                int target = pid;
                IntPtr found = IntPtr.Zero;
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    uint wp;
                    Native.GetWindowThreadProcessId(h, out wp);
                    if ((int)wp == target && Native.IsWindowVisible(h)) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
            }
            return IntPtr.Zero;
        }

        public static Rectangle RelRegion(string kind, int cw, int ch)
        {
            double sx = cw / 1600.0, sy = ch / 900.0;
            Rectangle r;
            /* 4 人队伍时头像列会占到 y≈420；只截 130 高会漏掉后两名。 */
            if (kind == "party") r = new Rectangle(1000, 55, 600, 380);
            else if (kind == "item") r = new Rectangle(1000, 150, 600, 700);
            else r = new Rectangle(0, 0, 1030, 890);
            var rr = new Rectangle((int)(r.X * sx), (int)(r.Y * sy), (int)(r.Width * sx), (int)(r.Height * sy));
            rr = Rectangle.Intersect(rr, new Rectangle(0, 0, cw, ch));
            return rr;
        }

        public static Bitmap GrabRegion(IntPtr hwnd, string kind, out Rectangle screenRect, out double sx, out double sy)
        {
            screenRect = Rectangle.Empty;
            sx = 1; sy = 1;
            Rectangle clientScreen;
            Bitmap client = Grab(hwnd, out clientScreen);
            if (client == null) return null;
            Rectangle design = RelRegion(kind, 1600, 900);
            double kx = client.Width / 1600.0, ky = client.Height / 900.0;
            var rel = new Rectangle((int)(design.X * kx), (int)(design.Y * ky), (int)(design.Width * kx), (int)(design.Height * ky));
            rel = Rectangle.Intersect(rel, new Rectangle(0, 0, client.Width, client.Height));
            if (rel.Width < 32 || rel.Height < 20) { client.Dispose(); return null; }
            var crop = client.Clone(rel, PixelFormat.Format32bppArgb);
            client.Dispose();
            screenRect = new Rectangle(clientScreen.X + rel.X, clientScreen.Y + rel.Y, rel.Width, rel.Height);
            return crop;
        }

        public static IntPtr RenderChild(IntPtr root)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                Native.EnumChildWindows(root, delegate (IntPtr h, IntPtr l)
                {
                    var sb = new StringBuilder(256);
                    Native.GetClassName(h, sb, 256);
                    if (sb.ToString().IndexOf("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        public static Bitmap Grab(IntPtr hwnd, out Rectangle clientScreen)
        {
            clientScreen = Rectangle.Empty;
            IntPtr child = RenderChild(hwnd);
            Bitmap bmp = null;
            if (child != IntPtr.Zero) bmp = GrabWindow(child, out clientScreen);
            if (bmp == null) bmp = GrabWindow(hwnd, out clientScreen);
            return bmp;
        }

        static Bitmap GrabWindow(IntPtr hwnd, out Rectangle clientScreen)
        {
            clientScreen = Rectangle.Empty;
            RECT wr, cr;
            if (!Native.GetWindowRect(hwnd, out wr)) return null;
            if (!Native.GetClientRect(hwnd, out cr)) return null;
            var pt = new POINT { X = cr.Left, Y = cr.Top };
            Native.ClientToScreen(hwnd, ref pt);
            clientScreen = new Rectangle(pt.X, pt.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);

            int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
            if (ww <= 0 || wh <= 0) return null;
            var full = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                IntPtr oldCtx = IntPtr.Zero;
                try
                {
                    oldCtx = Native.SetThreadDpiAwarenessContext(new IntPtr(-2));
                    ok = Native.PrintWindow(hwnd, hdc, 2);
                    if (!ok) ok = Native.PrintWindow(hwnd, hdc, 0);
                }
                finally
                {
                    if (oldCtx != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(oldCtx);
                    g.ReleaseHdc(hdc);
                }
                if (!ok) { full.Dispose(); return null; }
            }
            int cx = clientScreen.X - wr.Left, cy = clientScreen.Y - wr.Top;
            cx = Math.Max(0, Math.Min(cx, ww - 1));
            cy = Math.Max(0, Math.Min(cy, wh - 1));
            int cw = Math.Min(clientScreen.Width, ww - cx), ch = Math.Min(clientScreen.Height, wh - cy);
            if (cw < 64 || ch < 64) { full.Dispose(); return null; }
            var crop = full.Clone(new Rectangle(cx, cy, cw, ch), PixelFormat.Format32bppArgb);
            full.Dispose();
            clientScreen = new Rectangle(clientScreen.X, clientScreen.Y, cw, ch);
            return crop;
        }
    }

    /* ══ 解析 ═══════════════════════════════════════════════ */
    static class Parse
    {
        public static readonly string[] Attrs = { "力量", "敏捷", "體質", "体质", "感知", "智力", "魅力" };
        public static string FloorId, FloorName;
        public static string FloorType = "normal";

        public static bool IsNormalFloorId(string id)
        {
            return System.Text.RegularExpressions.Regex.IsMatch((id ?? "").Trim(), @"^\d+(?:\.\d+)?$");
        }

        public static bool IsChrome(string t)
        {
            string c = System.Text.RegularExpressions.Regex.Replace(t ?? "", @"[\s\u3000]+", "");
            return c.StartsWith("要怎麼做") || c.StartsWith("要怎么做")
                || c.StartsWith("要怎麽做") || c.StartsWith("要怎做")
                || c.StartsWith("跟隨多數") || c.StartsWith("跟随多数")
                || c.StartsWith("要走哪條路") || c.StartsWith("要走哪条路")
                || c.StartsWith("各自选") || c.StartsWith("各自選")
                || c.StartsWith("全队投票") || c.StartsWith("全隊投票")
                || c == "繼續" || c == "继续" || c.StartsWith("繼續探索") || c.StartsWith("继续探索")
                || c == "說出來" || c == "说出来";
        }

        static bool IsGoldText(Color c)
        {
            if (c.A == 0) return false;
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            return max > 150 && c.R >= c.G && c.G >= c.B && (c.R - c.B) > 45 && (max - min) > 35;
        }

        static bool IsGrayText(Color c)
        {
            if (c.A == 0) return false;
            return Math.Abs(c.R - c.G) < 26 && Math.Abs(c.G - c.B) < 26 && c.R > 100;
        }

        /* 事件底部不是选项：
           “看完之后按「继续」”是金色提示，“(0/1 人看完了)”是灰色计数。
           先看位置和文字，再看 OCR 行取到的文字颜色。 */
        static bool IsFooterLine(OcrLine l, string text, int imgH)
        {
            string c = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"[\s\u3000]+", "");
            if (c.Length == 0 || l.Y < imgH * 0.5) return false;
            if (c.StartsWith("看完之后按") || c.StartsWith("看完之後按")) return true;
            if (c.IndexOf("人看完了") >= 0) return true;
            bool hasContinue = c.IndexOf("继续") >= 0 || c.IndexOf("繼續") >= 0;
            if (hasContinue && (c.IndexOf("看完") >= 0 || IsGoldText(l.TextColor))) return true;
            if (IsGoldText(l.TextColor) && c.IndexOf("看完") >= 0) return true;
            if (IsGrayText(l.TextColor) && c.IndexOf("人看完") >= 0) return true;
            return false;
        }

        public static void Classify(List<OcrLine> lines, int imgH)
        {
            FloorId = ""; FloorName = ""; FloorType = "normal";
            foreach (var l in lines)
            {
                l.IsFloor = l.IsTitle = l.IsOption = l.IsChrome = l.IsCondition = l.IsSpecial = false;
                l.Selected = false;
                l.Check = ""; l.Condition = ""; l.Requirement = "";
                l.OptionGroup = -1;
                l.ConditionFor = -1;
            }
            foreach (var l in lines)
            {
                string t = (l.Text ?? "").Trim();
                if (IsChrome(t) || IsFooterLine(l, t, imgH)) { l.IsChrome = true; continue; }
                if (IsGoldText(l.TextColor) || t.TrimStart().StartsWith("★")) l.IsSpecial = true;
                var mf = System.Text.RegularExpressions.Regex.Match(t, @"Level\s*([^「『\[\n]+?)\s*[「『\[]([^」』\]\n]*)[」』\]]?");
                if (mf.Success && l.Y < imgH * 0.16)
                {
                    l.IsFloor = true;
                    FloorId = mf.Groups[1].Value.Trim();
                    if (FloorId == "O" || FloorId == "o") FloorId = "0";
                    if (FloorId == "I" || FloorId == "l") FloorId = "1";
                    FloorName = mf.Groups[2].Value;
                    FloorType = IsNormalFloorId(FloorId) ? "normal" : "anomalous";
                    continue;
                }
                if (t.IndexOf('【') >= 0 && t.IndexOf('】') > t.IndexOf('【')) l.IsTitle = true;
                if (l.Y > imgH * 0.56 && l.X < 1100) l.IsOption = true;
            }
            var opts = lines.FindAll(delegate (OcrLine l) { return l.IsOption; });
            opts.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });
            int g = 0;
            /* opts 里同时有“选项”和“条件”行；条件行不增加 g，所以不能再用
               opts[g-1] 找上一条选项，否则第二条条件会挂到第一条条件/选项上。
               单独维护 lastOption，条件永远归属最近一条真正选项。 */
            OcrLine lastOption = null;
            foreach (var l in opts)
            {
                bool sub = false;
                foreach (var a in Attrs)
                {
                    if (l.Text.IndexOf(a, StringComparison.Ordinal) >= 0 && (l.Text.IndexOf("難度", StringComparison.Ordinal) >= 0 || l.Text.IndexOf("难度", StringComparison.Ordinal) >= 0))
                    { sub = true; break; }
                }
                if (l.Text.TrimStart().StartsWith("★") || l.Text.TrimStart().StartsWith("「")) sub = true;
                if (sub && lastOption != null)
                {
                    var prev = lastOption;
                    if (l.Y - (prev.Y + prev.H) < 40)
                    {
                        string t = l.Text.Trim();
                        string subText = t.TrimStart('★', ' ', '　');
                        bool consumed = subText.IndexOf("会用掉", StringComparison.Ordinal) >= 0 || subText.IndexOf("會用掉", StringComparison.Ordinal) >= 0;
                        var mchk = System.Text.RegularExpressions.Regex.Match(subText, @"(力量|敏捷|體質|体质|感知|智力|魅力)\s*(?:檢定|检定)?\s*難度\s*([0-9Xx]+)");
                        if (mchk.Success) prev.Check = mchk.Groups[1].Value;
                        if (t.StartsWith("★"))
                        {
                            var mi = System.Text.RegularExpressions.Regex.Match(subText, "「([^」]+)」");
                            if (mi.Success) { prev.ReqType = "item"; prev.ReqName = mi.Groups[1].Value; prev.ReqConsume = consumed; }
                            else
                            {
                                var ma = System.Text.RegularExpressions.Regex.Match(subText, @"^(力量|敏捷|體質|体质|感知|智力|魅力)\s*([0-9]+)\s*\+");
                                if (ma.Success) { prev.ReqType = "attr"; prev.ReqName = ma.Groups[1].Value; prev.ReqMin = int.Parse(ma.Groups[2].Value); }
                                else
                                {
                                    int sep = subText.IndexOf('｜'); if (sep < 0) sep = subText.IndexOf('|');
                                    if (sep > 0) { prev.ReqType = "character"; prev.ReqName = subText.Substring(0, sep).Trim(); }
                                    else { prev.ReqType = "skill"; prev.ReqName = subText.Trim(); }
                                }
                            }
                            prev.Requirement = prev.ReqName;
                        }
                        else if (subText.StartsWith("「"))
                        {
                            var mi2 = System.Text.RegularExpressions.Regex.Match(subText, "「([^」]+)」");
                            if (mi2.Success) { prev.ReqType = "item"; prev.ReqName = mi2.Groups[1].Value; prev.ReqConsume = consumed; }
                        }
                        prev.Condition = (prev.Condition + " " + t).Trim();
                        l.IsCondition = true;
                        l.ConditionFor = prev.OptionGroup;
                        l.IsOption = false;
                    }
                }
                if (l.IsOption)
                {
                    if (lastOption != null)
                    {
                        if (l.Y - (lastOption.Y + lastOption.H) < 48 && l.Text.Trim().Length > 22)
                        {
                            lastOption.Desc = (lastOption.Desc == "" ? "" : lastOption.Desc + " ") + l.Text.Trim();
                            l.OptionGroup = -2;
                            l.IsOption = false;
                            continue;
                        }
                    }
                    l.OptionGroup = g++;
                    lastOption = l;
                }
            }
        }

        public static bool TryParseCheck(string t, out string attr, out string dc, out string roll, out string outcome, out string player)
        {
            attr = dc = roll = outcome = player = "";
            var m = System.Text.RegularExpressions.Regex.Match(t, @"[（(\[〔【]\s*(力量|敏捷|體質|体质|感知|智力|魅力)\s*(?:難度|难度)\s*([0-9Xx]+)\s*[）)\]〕】]");
            if (!m.Success) return false;
            attr = m.Groups[1].Value;
            dc = m.Groups[2].Value;
            var mn = System.Text.RegularExpressions.Regex.Match(t.Substring(m.Index + m.Length), @"^\s*([^\s：:]{1,12}?)\s*(?:擲出|掷出)");
            if (mn.Success) player = mn.Groups[1].Value.Trim();
            var mr = System.Text.RegularExpressions.Regex.Match(t, @"(?:擲出|掷出)\s*([0-9IVXLCDM+\-=\s]+)");
            if (mr.Success) roll = mr.Groups[1].Value.Trim();
            if (t.IndexOf("失敗", StringComparison.Ordinal) >= 0 || t.IndexOf("失败", StringComparison.Ordinal) >= 0) outcome = "失敗";
            else if (t.IndexOf("成功", StringComparison.Ordinal) >= 0) outcome = "成功";
            return true;
        }

        public static string TitleOf(List<OcrLine> sel)
        {
            foreach (var l in sel)
            {
                var m = System.Text.RegularExpressions.Regex.Match(l.Text, "【([^】]+)】");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return "";
        }
    }

    /* ══ 辨識中的即時提示 ═══════════════════════════════════ */
    class BusyForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }

        public BusyForm(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(18, 18, 22);
            ClientSize = new Size(120, 34);
            var p = Cursor.Position;
            Location = new Point(p.X + 20, p.Y + 20);
            var l = new Label();
            l.Text = text;
            l.SetBounds(0, 0, 120, 34);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.ForeColor = Color.FromArgb(240, 240, 245);
            l.Font = new Font("Microsoft JhengHei UI", 9F);
            Controls.Add(l);
        }
    }

    /* ══ 輸入框聚焦狀態 ══════════════════════════════════════
       客戶端（含 Google 登入 popup）的注入腳本會偵測 focusin／focusout，
       把「現在是否在文字輸入框裡」寫進共享記憶體的一個 byte。
       鉤子讀這個旗標：true 就把 Q／E／Esc 原樣放行給頁面，
       不然使用者在遊戲的登入框打 q／e 會變成開記錄模式。 */
    static class TypingState
    {
        const string MapName = "Local\\ExpeditionTyping";
        static readonly object Sync = new object();
        static MemoryMappedFile _map;
        static MemoryMappedViewAccessor _view;
        static bool _failed;

        public static bool IsEditing
        {
            get
            {
                if (_failed) return false;
                try
                {
                    lock (Sync)
                    {
                        if (_view == null)
                        {
                            _map = MemoryMappedFile.CreateOrOpen(MapName, 8);
                            _view = _map.CreateViewAccessor();
                        }
                        if (_view.ReadByte(0) != 1) return false;
                        /* 客戶端在輸入框聚焦期間每 500ms 會刷新心跳（offset 4）；
                           超過 4 秒沒更新就當作已經離開輸入框——頁面跳轉、popup
                           被關、客戶端崩潰都不會補送 focusout，沒有這個保險，
                           旗標會卡在 1，Q／E 就永遠沒反應。 */
                        int beat = _view.ReadInt32(4);
                        int age = unchecked(Environment.TickCount - beat);
                        return age >= 0 && age < 4000;
                    }
                }
                catch { _failed = true; return false; }
            }
        }

        /* 客戶端關掉時旗標會停在最後一次的值（記錄器仍握著映射，
           所以區段不會消失）。接上／脫離客戶端時清零，否則下次
           開客戶端前 Q／E 會被自己擋掉。 */
        public static void Clear()
        {
            try { lock (Sync) { if (_view != null) _view.Write(0, (byte)0); } }
            catch { }
        }
    }

    /* ══ 低級鍵盤鉤子：只在遊戲視窗是前景時吃掉 ` 與 Q ═══════ */
    class KeyboardHook
    {
        IntPtr _hook;
        Native.HookProc _proc;
        int _downVk;
        int _downTick;
        int _installedTick;
        public Func<IntPtr> GetGame;
        public int ModeVk = 0x51, StepVk = 0x45, CancelVk = 0x43;
        public bool Enabled = true;
        public Action<int> OnTrigger;
        public Action<int> OnRelease;
        public Func<bool> EscapeHandler;
        public Action OverlayCancel;
        /* 回呼超過 100ms 就代表這次拖慢了鉤子。Windows 會在使用者沒按鍵的
           空檔把反應太慢的低級鉤子悄悄移除，而且不通知任何人——移除之後
           Q／E 就完全沒反應，看起來就像「有時候鍵盤不聽了」。 */
        public bool Slow;
        public bool Installed { get { return _hook != IntPtr.Zero; } }
        public int InstalledTick { get { return _installedTick; } }

        public bool Install()
        {
            _proc = Hook;
            _hook = Native.SetWindowsHookEx(13, _proc, Native.GetModuleHandle(null), 0);
            _installedTick = Environment.TickCount;
            return _hook != IntPtr.Zero;
        }

        public void Uninstall()
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            _downVk = 0;
        }

        /* 重掛。Windows 移除鉤子不發通知，只能靠定期重掛當保險。 */
        public bool Reinstall()
        {
            Uninstall();
            return Install();
        }

        /* 漏接 keyup 時把卡住的按鍵放掉，否則下一次按同一個鍵會被
           「_downVk 還是它」擋掉，永遠不觸發。 */
        public void ClearStuckDown(int ms)
        {
            if (_downVk != 0 && unchecked(Environment.TickCount - _downTick) > ms) _downVk = 0;
        }

        /* 框選/對話框關閉後主動放掉按鍵狀態，避免漏接 keyup 後
           下一次按 E 被「_downVk 還是它」擋掉。 */
        public void ResetDown()
        {
            _downVk = 0;
        }

        bool IsGameFocused(IntPtr fg)
        {
            IntPtr game = GetGame != null ? GetGame() : IntPtr.Zero;
            if (game == IntPtr.Zero || fg == IntPtr.Zero) return false;
            if (fg == game) return true;
            IntPtr root = Native.GetAncestor(fg, 2);
            return root == game || Native.IsChild(game, fg);
        }

        bool IsOwnProcessWindow(IntPtr fg)
        {
            if (fg == IntPtr.Zero) return false;
            uint pid = 0;
            Native.GetWindowThreadProcessId(fg, out pid);
            return (int)pid == System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!Enabled)
            {
                _downVk = 0;
                return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    bool down = msg == 0x0100 || msg == 0x0104;
                    bool up = msg == 0x0101 || msg == 0x0105;
                    if (down || up)
                    {
                        int vk = Marshal.ReadInt32(lParam);
                        /* 框选/扫描覆盖层打开时，直接由钩子收取消键，
                           不依赖覆盖层是否抢到焦点。 */
                        if (down && vk == CancelVk && OverlayCancel != null)
                        {
                            OverlayCancel();
                            return (IntPtr)1;
                        }
                        /* Esc 只在游戏窗口，或记录器自己的浮层（Q 圆盘）上处理；
                           其他程序一律放行。 */
                        if (down && vk == 0x1B && EscapeHandler != null && !TypingState.IsEditing)
                        {
                            IntPtr efg = Native.GetForegroundWindow();
                            if ((IsGameFocused(efg) || IsOwnProcessWindow(efg)) && EscapeHandler()) return (IntPtr)1;
                        }
                        if (vk == ModeVk || vk == StepVk || vk == CancelVk)
                        {
                            /* keyup：只有真的被我們吃掉的 keydown 才吃 keyup。
                               Q 圆盘打开后前景会变成记录器浮层，不能在这里再要求
                               前景是游戏，否则 _downVk 会卡在 Q，下一次按 Q 没反应。 */
                            if (up)
                            {
                                if (_downVk != vk) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
                                _downVk = 0;
                                if (OnRelease != null) OnRelease(vk);
                                return (IntPtr)1;
                            }
                            /* 只认当前游戏 client 窗口（或其子窗口）。记录器面板、
                               浏览器、其他程序都不再触发 Q／E。 */
                            if (!IsGameFocused(Native.GetForegroundWindow())) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
                            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
                            bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
                            bool typing = TypingState.IsEditing;
                            if (!typing && !ctrl && !alt && _downVk != vk)
                            {
                                _downVk = vk;
                                _downTick = Environment.TickCount;
                                if (OnTrigger != null) OnTrigger(vk);
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (System.Diagnostics.Stopwatch.GetTimestamp() - t0 > System.Diagnostics.Stopwatch.Frequency / 10) Slow = true;
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    /* ══ 模式選擇 ═══════════════════════════════════════════ */
    class ModeForm : Form
    {
        public string Picked = "";
        public ModeForm()
        {
            Text = "記錄什麼？";
            ClientSize = new Size(330, 252);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            var tip = new Label();
            tip.Text = "選一個；不選就按 Q（預設事件）";
            tip.SetBounds(12, 8, 306, 22);
            tip.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(tip);

            string[] modes = { "event", "item", "floor", "exit", "ending" };
            string[] labels = { "事件（預設）", "物品", "樓層介紹", "出口", "結局" };
            for (int i = 0; i < modes.Length; i++)
            {
                var b = new Button();
                b.Text = labels[i];
                b.SetBounds(12, 34 + i * 40, 306, 32);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(44, 44, 47);
                b.ForeColor = Color.FromArgb(232, 232, 236);
                b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
                string m = modes[i];
                b.Click += delegate { Picked = m; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(b);
            }
        }
    }

    /* ══ 結束事件：事件類型＋導向＋標籤 ═══════════════════ */
    class EndForm : Form
    {
        public List<string> Scopes = new List<string>();
        public List<string> Characters = new List<string>();
        public List<string> Outcomes = new List<string>();
        public List<string> Tags = new List<string>();
        public string EventKind = "normal";
        public string JumpTo = "";
        public string FloorId = "", FloorName = "";
        public string CombatEnemy = "";

        RadioButton _normal, _free, _junction, _character, _cooperation, _common;
        TextBox _floorBox, _charBox, _jumpBox, _enemyBox;
        CheckBox _jump, _combat, _rest;
        Label _floorLabel, _charLabel;
        string _curId = "", _curName = "";

        static bool IsFloorKind(string k) { return k == "normal" || k == "free" || k == "junction"; }
        static bool IsCharacterKind(string k) { return k == "character" || k == "cooperation"; }

        public EndForm(string curId, string curName, int steps, string prefill, bool characterEvent = false, string characterName = "", string ocrKind = "")
        {
            _curId = curId ?? "";
            _curName = curName ?? "";
            Text = "結束事件（" + steps + " 步）";
            KeyPreview = true;
            ClientSize = new Size(560, 452);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            var l1 = new Label(); l1.Text = "事件類型（單選）"; l1.SetBounds(12, 8, 520, 20); l1.ForeColor = Color.FromArgb(180, 180, 188); Controls.Add(l1);
            _normal = MkRadio("普通事件", 12, 32, 105);
            _free = MkRadio("自由事件", 120, 32, 105);
            _junction = MkRadio("岔路事件", 228, 32, 105);
            _character = MkRadio("角色事件", 12, 58, 105);
            _cooperation = MkRadio("合力事件", 120, 58, 105);
            _common = MkRadio("通用事件", 228, 58, 105);
            string init = ocrKind;
            if (init != "free" && init != "junction" && init != "character" && init != "cooperation" && init != "common") init = characterEvent ? "character" : "normal";
            _normal.Checked = init == "normal"; _free.Checked = init == "free"; _junction.Checked = init == "junction";
            _character.Checked = init == "character"; _cooperation.Checked = init == "cooperation"; _common.Checked = init == "common";

            _floorLabel = new Label();
            _floorLabel.Text = _curId == "" ? "事件出現樓層（OCR 未識別）" : "事件出現樓層（OCR：Level " + _curId + " " + _curName + "）";
            _floorLabel.SetBounds(12, 140, 520, 20);
            _floorLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(_floorLabel);
            _floorBox = MkBox(12, 162, 300);
            _floorBox.Text = (_curId + " " + _curName).Trim();

            _charLabel = new Label();
            _charLabel.Text = "角色（角色事件填一個，合力事件可填多個，用、分隔）";
            _charLabel.SetBounds(12, 194, 520, 20);
            _charLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(_charLabel);
            _charBox = MkBox(12, 216, 500);
            string ch = !string.IsNullOrEmpty(characterName) ? characterName : prefill;
            _charBox.Text = ch ?? "";

            var l2 = new Label(); l2.Text = "結果導向"; l2.SetBounds(12, 252, 520, 20); l2.ForeColor = Color.FromArgb(255, 214, 120); Controls.Add(l2);
            _jump = MkCheck("切換樓層 →", 12, 276, 100);
            _jumpBox = MkBox(118, 276, 160);
            _combat = MkCheck("進入戰鬥，實體名稱：", 12, 306, 180);
            _enemyBox = MkBox(196, 306, 350);

            var l3 = new Label(); l3.Text = "事件標籤"; l3.SetBounds(12, 90, 520, 20); l3.ForeColor = Color.FromArgb(180, 180, 188); Controls.Add(l3);
            _rest = MkCheck("休息處", 12, 112, 120);

            _normal.CheckedChanged += delegate { UpdateEnabled(); };
            _free.CheckedChanged += delegate { UpdateEnabled(); };
            _junction.CheckedChanged += delegate { UpdateEnabled(); };
            _character.CheckedChanged += delegate { UpdateEnabled(); };
            _cooperation.CheckedChanged += delegate { UpdateEnabled(); };
            _common.CheckedChanged += delegate { UpdateEnabled(); };

            var save = new Button(); save.Text = "保存"; save.SetBounds(12, 400, 120, 30); save.FlatStyle = FlatStyle.Flat; save.BackColor = Color.FromArgb(44, 44, 47); save.ForeColor = Color.White;
            save.Click += delegate
            {
                EventKind = _free.Checked ? "free" : _junction.Checked ? "junction" : _character.Checked ? "character"
                    : _cooperation.Checked ? "cooperation" : _common.Checked ? "common" : "normal";
                Scopes.Clear();
                if (IsFloorKind(EventKind)) Scopes.Add("floor");
                else if (EventKind == "common") Scopes.Add("common");
                else Scopes.Add("character");

                if (IsFloorKind(EventKind))
                {
                    string raw = _floorBox.Text.Trim();
                    if (raw.StartsWith("Level ", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(6).Trim();
                    int sp = raw.IndexOfAny(new char[] { ' ', '　', '|', '/' });
                    if (sp > 0) { FloorId = raw.Substring(0, sp).Trim(); FloorName = raw.Substring(sp + 1).Trim(); }
                    else { FloorId = raw; FloorName = ""; }
                    if (FloorId == "") { FloorId = _curId; FloorName = _curName; }
                }
                else { FloorId = ""; FloorName = ""; }

                Characters.Clear();
                if (IsCharacterKind(EventKind))
                {
                    foreach (var x in System.Text.RegularExpressions.Regex.Split(_charBox.Text, @"[、,，;；]"))
                    {
                        string c = (x ?? "").Trim();
                        if (c != "" && !Characters.Contains(c)) Characters.Add(c);
                    }
                }

                Outcomes.Clear();
                if (_jump.Checked) Outcomes.Add("floor-jump");
                if (_combat.Checked) Outcomes.Add("combat");
                if (_rest.Checked) Outcomes.Add("rest");
                if (Outcomes.Count == 0) Outcomes.Add("normal");
                Tags.Clear();
                if (_rest.Checked) Tags.Add("rest");
                JumpTo = _jump.Checked ? _jumpBox.Text.Trim() : "";
                CombatEnemy = _combat.Checked ? _enemyBox.Text.Trim() : "";
                DialogResult = DialogResult.OK; Close();
            };
            Controls.Add(save);
            var cancel = new Button(); cancel.Text = "取消"; cancel.SetBounds(144, 400, 120, 30); cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Color.FromArgb(44, 44, 47); cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            string k = _free.Checked ? "free" : _junction.Checked ? "junction" : _character.Checked ? "character"
                : _cooperation.Checked ? "cooperation" : _common.Checked ? "common" : "normal";
            bool floor = IsFloorKind(k);
            bool chr = IsCharacterKind(k);
            _floorBox.Enabled = floor;
            _floorLabel.ForeColor = floor ? Color.FromArgb(180, 180, 188) : Color.FromArgb(110, 110, 118);
            _charBox.Enabled = chr;
            _charLabel.ForeColor = chr ? Color.FromArgb(180, 180, 188) : Color.FromArgb(110, 110, 118);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        TextBox MkBox(int x, int y, int w)
        {
            var t = new TextBox();
            t.SetBounds(x, y, w, 24);
            t.BackColor = Color.FromArgb(40, 40, 44);
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(t);
            return t;
        }

        CheckBox MkCheck(string text, int x, int y, int w)
        {
            var c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 24);
            c.ForeColor = Color.FromArgb(225, 225, 230);
            c.BackColor = Color.Transparent;
            Controls.Add(c);
            return c;
        }

        RadioButton MkRadio(string text, int x, int y, int w)
        {
            var r = new RadioButton();
            r.Text = text;
            r.SetBounds(x, y, w, 22);
            r.ForeColor = Color.FromArgb(220, 220, 226);
            r.BackColor = Color.Transparent;
            Controls.Add(r);
            return r;
        }
    }

    /* ══ 物品確認窗 ═════════════════════════════════════════ */
    class ItemForm : Form
    {
        public Dictionary<string, object> Item;
        TextBox _name, _type, _price, _eff, _cures, _desc, _char;

        public ItemForm(Dictionary<string, object> item)
        {
            Item = item;
            Text = "記錄物品";
            ClientSize = new Size(500, 470);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            int y = 12;
            _name = Field("名稱", Convert.ToString(item["name"]), ref y, 24);
            _type = Field("類型", Convert.ToString(item["type"]), ref y, 24);
            _price = Field("價格", Convert.ToString(item["price"]), ref y, 24);
            _eff = Field("效果", Join(item["eff"]), ref y, 84);
            _cures = Field("治得好", Join(item["cures"]), ref y, 64);
            _desc = Field("介紹", Convert.ToString(item["desc"]), ref y, 96);
            _char = Field("角色", Convert.ToString(item["character"]), ref y, 24);

            var save = new Button();
            save.Text = "保存";
            save.SetBounds(12, y + 4, 120, 30);
            save.FlatStyle = FlatStyle.Flat;
            save.BackColor = Color.FromArgb(44, 44, 47);
            save.ForeColor = Color.White;
            save.Click += delegate
            {
                Item["name"] = _name.Text.Trim();
                Item["type"] = _type.Text.Trim();
                Item["price"] = _price.Text.Trim();
                Item["eff"] = Split(_eff.Text);
                Item["cures"] = Split(_cures.Text);
                Item["desc"] = _desc.Text.Trim();
                Item["character"] = _char.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(144, y + 4, 120, 30);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(44, 44, 47);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }

        static string Join(object o)
        {
            var a = o as List<string>;
            return a == null ? "" : string.Join("、", a.ToArray());
        }

        static List<string> Split(string text)
        {
            var list = new List<string>();
            foreach (var part in (text ?? "").Split(new char[] { '、', '\n', '\r', ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        TextBox Field(string title, string value, ref int y, int h)
        {
            var l = new Label();
            l.Text = title;
            l.SetBounds(12, y + 2, 56, 20);
            l.ForeColor = Color.FromArgb(160, 160, 168);
            Controls.Add(l);
            var t = new TextBox();
            t.Multiline = h > 30;
            if (h > 30) t.ScrollBars = ScrollBars.Vertical;
            t.SetBounds(72, y, 416, h);
            t.BackColor = Color.FromArgb(36, 36, 40);
            t.ForeColor = Color.FromArgb(230, 230, 236);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Text = value;
            Controls.Add(t);
            y += h + 8;
            return t;
        }
    }

    /* ══ 樓層記錄：常規／異常＋樓層 ID／名稱＋主題 ═══════════ */
    class ThemeForm : Form
    {
        public string Theme = "dark";
        public string FloorType = "normal";
        public string FloorId = "";
        public string FloorName = "";
        RadioButton _normal, _anomalous;
        Label _prefix, _floorLabel;
        TextBox _idBox, _nameBox;
        Dictionary<string, object> _palette;

        public ThemeForm(string floorId, string floorName, string floorType = "", Dictionary<string, object> palette = null, string theme = "")
        {
            FloorId = floorId ?? "";
            FloorName = floorName ?? "";
            _palette = palette;
            string initialTheme = theme == "light" ? "light" : "dark";
            Theme = initialTheme;
            if (floorType != "normal" && floorType != "anomalous")
                floorType = Parse.IsNormalFloorId(FloorId) ? "normal" : "anomalous";
            FloorType = floorType;

            Text = "樓層記錄";
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Resize += delegate { Invalidate(true); };
            ClientSize = new Size(460, 356);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            var typeLabel = new Label();
            typeLabel.Text = "類型";
            typeLabel.SetBounds(12, 8, 120, 20);
            typeLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(typeLabel);
            var typeGroup = new Panel();
            typeGroup.SetBounds(12, 30, 430, 24);
            typeGroup.BackColor = Color.Transparent;
            Controls.Add(typeGroup);
            _normal = new RadioButton();
            _normal.Text = "常規樓層";
            _normal.SetBounds(0, 0, 120, 24);
            _normal.Checked = FloorType == "normal";
            _normal.ForeColor = Color.White;
            typeGroup.Controls.Add(_normal);
            _anomalous = new RadioButton();
            _anomalous.Text = "異常樓層";
            _anomalous.SetBounds(130, 0, 120, 24);
            _anomalous.Checked = FloorType == "anomalous";
            _anomalous.ForeColor = Color.White;
            typeGroup.Controls.Add(_anomalous);

            _floorLabel = new Label();
            _floorLabel.Text = "樓層";
            _floorLabel.SetBounds(12, 64, 120, 20);
            _floorLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(_floorLabel);
            _prefix = new Label();
            _prefix.Text = "Level";
            _prefix.SetBounds(12, 88, 42, 22);
            _prefix.ForeColor = Color.FromArgb(220, 220, 226);
            Controls.Add(_prefix);
            _idBox = MkBox(60, 86, 180);
            _idBox.Text = FloorId;
            _idBox.KeyPress += delegate (object s, KeyPressEventArgs e)
            {
                if (!_normal.Checked || char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar) || e.KeyChar == '.') return;
                e.Handled = true;
            };
            _nameBox = MkBox(250, 86, 192);
            _nameBox.Text = FloorName;

            var previewLabel = new Label();
            previewLabel.Text = "配色預覽";
            previewLabel.SetBounds(12, 120, 300, 20);
            previewLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(previewLabel);

            var preview = new BufferedPanel();
            preview.SetBounds(12, 142, 430, 76);
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.Paint += delegate (object s, PaintEventArgs e) { DrawPalettePreview(e.Graphics, preview.ClientSize); };
            Controls.Add(preview);

            var themeLabel = new Label();
            themeLabel.Text = "主題";
            themeLabel.SetBounds(12, 230, 120, 20);
            themeLabel.ForeColor = Color.FromArgb(180, 180, 188);
            Controls.Add(themeLabel);
            var themeGroup = new Panel();
            themeGroup.SetBounds(12, 252, 430, 24);
            themeGroup.BackColor = Color.Transparent;
            Controls.Add(themeGroup);
            var light = new RadioButton();
            light.Text = "明亮";
            light.SetBounds(0, 0, 150, 24);
            light.Checked = initialTheme == "light";
            light.ForeColor = Color.White;
            themeGroup.Controls.Add(light);
            var dark = new RadioButton();
            dark.Text = "昏暗";
            dark.SetBounds(158, 0, 150, 24);
            dark.Checked = initialTheme == "dark";
            dark.ForeColor = Color.White;
            themeGroup.Controls.Add(dark);
            light.CheckedChanged += delegate { preview.Invalidate(); };
            dark.CheckedChanged += delegate { preview.Invalidate(); };

            _normal.CheckedChanged += delegate { UpdateType(); };
            _anomalous.CheckedChanged += delegate { UpdateType(); };
            UpdateType();

            var save = new Button();
            save.Text = "保存";
            save.SetBounds(12, 304, 120, 30);
            save.FlatStyle = FlatStyle.Flat;
            save.BackColor = Color.FromArgb(44, 44, 47);
            save.ForeColor = Color.White;
            save.Click += delegate
            {
                string id = _idBox.Text.Trim();
                if (id.StartsWith("Level ", StringComparison.OrdinalIgnoreCase)) id = id.Substring(6).Trim();
                if (_normal.Checked)
                {
                    if (id != "" && !Parse.IsNormalFloorId(id))
                    {
                        MessageBox.Show("常規樓層只能填數字（可含小數點）。", "樓層記錄", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    FloorType = "normal";
                }
                else FloorType = "anomalous";
                FloorId = id;
                FloorName = _nameBox.Text.Trim();
                Theme = light.Checked ? "light" : "dark";
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);
            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(144, 304, 120, 30);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(44, 44, 47);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }

        void DrawPalettePreview(Graphics g, Size size)
        {
            Color bg = PaletteColor("bg", Theme == "light" ? Color.FromArgb(244, 240, 230) : Color.FromArgb(28, 28, 32));
            Color title = PaletteColor("title", Theme == "light" ? Color.FromArgb(92, 82, 58) : Color.FromArgb(255, 214, 120));
            Color text = PaletteColor("text", Theme == "light" ? Color.FromArgb(52, 50, 46) : Color.FromArgb(230, 230, 235));
            g.Clear(bg);
            float fs = Math.Max(9F, 10F * UiScale.Scale);
            using (var font = UiText.MakeFont(fs, FontStyle.Regular))
            using (var titleBrush = new SolidBrush(title))
            using (var textBrush = new SolidBrush(text))
            {
                g.DrawString("範例標題", font, titleBrush, UiScale.Px(12), UiScale.Px(10));
                g.DrawString("這是樓層正文的範例文字。", font, textBrush, UiScale.Px(12), UiScale.Px(40));
            }
        }

        Color PaletteColor(string key, Color fallback)
        {
            try
            {
                if (_palette == null || !_palette.ContainsKey(key)) return fallback;
                var c = ColorTranslator.FromHtml(Convert.ToString(_palette[key]));
                return c == Color.Empty ? fallback : c;
            }
            catch { return fallback; }
        }

        void UpdateType()
        {
            bool normal = _normal.Checked;
            _prefix.Visible = normal;
            _idBox.SetBounds(normal ? 60 : 12, 86, normal ? 180 : 220, 24);
            _nameBox.SetBounds(normal ? 250 : 240, 86, normal ? 192 : 202, 24);
        }

        TextBox MkBox(int x, int y, int w)
        {
            var t = new TextBox();
            t.SetBounds(x, y, w, 24);
            t.BackColor = Color.FromArgb(40, 40, 44);
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(t);
            return t;
        }
    }

    /* ══ 出口：輸入通往哪一層 ═══════════════════════════════ */
    class ExitForm : Form
    {
        public string Target = "";
        public bool PendingOnly = false;
        TextBox _box;
        public ExitForm(string hint, string preset)
        {
            Text = "出口通往哪裡？";
            ClientSize = new Size(420, 180);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);
            var l = new Label(); l.Text = hint; l.SetBounds(12, 8, 390, 40); l.ForeColor = Color.FromArgb(200, 200, 208); Controls.Add(l);
            _box = new TextBox(); _box.SetBounds(12, 54, 390, 24); _box.BackColor = Color.FromArgb(40, 40, 44); _box.ForeColor = Color.White; _box.BorderStyle = BorderStyle.FixedSingle; _box.Text = preset; Controls.Add(_box);
            var tip = new Label(); tip.Text = "樓層 id 不限數字：14.1 / The Hub / LX 都可以"; tip.SetBounds(12, 82, 390, 20); tip.ForeColor = Color.FromArgb(150, 150, 158); Controls.Add(tip);
            var save = new Button(); save.Text = "保存"; save.SetBounds(12, 112, 120, 30); save.FlatStyle = FlatStyle.Flat; save.BackColor = Color.FromArgb(44, 44, 47); save.ForeColor = Color.White;
            save.Click += delegate { Target = _box.Text.Trim(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(save);
            var pend = new Button(); pend.Text = "留待處理"; pend.SetBounds(144, 112, 120, 30); pend.FlatStyle = FlatStyle.Flat; pend.BackColor = Color.FromArgb(44, 44, 47); pend.ForeColor = Color.White;
            pend.Click += delegate { Target = ""; PendingOnly = true; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(pend);
            var cancel = new Button(); cancel.Text = "取消"; cancel.SetBounds(276, 112, 120, 30); cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Color.FromArgb(44, 44, 47); cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }
    }

    /* ══ 結局分類 ═══════════════════════════════════════════ */
    class EndingForm : Form
    {
        public string Category = "settle";
        public EndingForm(string name)
        {
            Text = "結局分類";
            ClientSize = new Size(380, 190);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);
            var l = new Label(); l.Text = name; l.SetBounds(12, 10, 350, 22); l.ForeColor = Color.FromArgb(220, 220, 226); Controls.Add(l);
            var bad = new RadioButton(); bad.Text = "壞結局"; bad.SetBounds(12, 42, 110, 24); bad.ForeColor = Color.White; Controls.Add(bad);
            var settle = new RadioButton(); settle.Text = "安頓下來"; settle.SetBounds(130, 42, 110, 24); settle.Checked = true; settle.ForeColor = Color.White; Controls.Add(settle);
            var neutral = new RadioButton(); neutral.Text = "中立結局"; neutral.SetBounds(248, 42, 110, 24); neutral.ForeColor = Color.White; Controls.Add(neutral);
            var save = new Button(); save.Text = "保存"; save.SetBounds(12, 110, 120, 30); save.FlatStyle = FlatStyle.Flat; save.BackColor = Color.FromArgb(44, 44, 47); save.ForeColor = Color.White;
            save.Click += delegate { Category = bad.Checked ? "bad" : neutral.Checked ? "neutral" : "settle"; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(save);
            var cancel = new Button(); cancel.Text = "取消"; cancel.SetBounds(144, 110, 120, 30); cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Color.FromArgb(44, 44, 47); cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }
    }

    /* ══ 角色表／隊伍映射 ═══════════════════════════════════ */
    class CharacterInfo { public string n; public string title; }

    static class Party
    {
        public static List<CharacterInfo> All = new List<CharacterInfo>();
        public static Dictionary<char, char> S2T = new Dictionary<char, char>();
        public static Dictionary<string, string> NameToChar = new Dictionary<string, string>();
        public static string LocalName = "";
        public static string LocalCharacter = "";
        static readonly object Sync = new object();

        public static void Load(string exeDir)
        {
            try
            {
                string p = Path.Combine(exeDir, "Recorder", "characters.json");
                if (!File.Exists(p)) p = Path.Combine(exeDir, "structured", "characters.json");
                if (!File.Exists(p)) p = Path.Combine(exeDir, "characters.json");
                if (!File.Exists(p)) return;
                var ser = new JavaScriptSerializer();
                var arr = ser.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(p));
                All.Clear();
                foreach (var d in arr)
                    All.Add(new CharacterInfo { n = Convert.ToString(d["n"]), title = Convert.ToString(d["title"]) });
                string sp = Path.Combine(exeDir, "Recorder", "s2t.json");
                if (!File.Exists(sp)) sp = Path.Combine(exeDir, "structured", "s2t.json");
                if (File.Exists(sp))
                {
                    var m = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(sp));
                    S2T.Clear();
                    foreach (var kv in m) { string k = kv.Key; string v = Convert.ToString(kv.Value); if (k.Length == 1 && v.Length == 1) S2T[k[0]] = v[0]; }
                }
            }
            catch { }
        }

        static string CleanPlayerName(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"Lv\s*\d+[★☆]?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[★☆].*$", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            s = s.Trim('★', '☆', '·', '・', ' ', '　', '（', '）', '(', ')');
            return s;
        }

        public static void Scan(List<OcrLine> lines, int W)
        {
            lock (Sync)
            {
            NameToChar.Clear(); LocalName = ""; LocalCharacter = "";
            var right = new List<OcrLine>();
            foreach (var l in lines) right.Add(l);
            right.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });

            /* 每行先抓 Lv 前面的玩家名：名字里可能带 ★、?、_、英文；
               Lv 后面的称号再对 characters.json。 */
            foreach (var l in right)
            {
                string t = (l.Text ?? "").Trim();
                int li = t.IndexOf("Lv", StringComparison.OrdinalIgnoreCase);
                if (li <= 0) continue;
                string nm = CleanPlayerName(t.Substring(0, li));
                if (nm.Length == 0 || nm.Length >= 24) continue;
                bool isLocal = t.IndexOf("（你", StringComparison.Ordinal) >= 0 || t.IndexOf("(你", StringComparison.Ordinal) >= 0;
                if (isLocal) LocalName = nm;
                string tail = ToHant(t.Substring(li));
                CharacterInfo best = null;
                foreach (var c in All)
                {
                    if (c.title.Length < 3) continue;
                    if (tail.IndexOf(c.title, StringComparison.Ordinal) < 0) continue;
                    if (best == null || c.title.Length > best.title.Length) best = c;
                }
                if (best != null)
                {
                    NameToChar[nm] = best.n;
                    if (isLocal) LocalCharacter = best.n;
                }
            }

            foreach (var l in right)
            {
                string t = (l.Text ?? "").Trim();
                foreach (var c in All)
                {
                    if (c.title.Length < 3) continue;
                    string tt = ToHant(t);
                    int idx = tt.IndexOf(c.title, StringComparison.Ordinal);
                    if (idx < 0) continue;

                    string nameLine = null;
                    string name = "";
                    if (idx > 0) name = t.Substring(0, idx).Trim();
                    else
                    {
                        foreach (var q in right)
                        {
                            if (q.Y < l.Y && l.Y - q.Y < 60 && q.X < l.X) { nameLine = (q.Text ?? "").Trim(); break; }
                        }
                        name = nameLine ?? "";
                    }
                    bool isLocal = name.IndexOf("（你", StringComparison.Ordinal) >= 0 || name.IndexOf("(你", StringComparison.Ordinal) >= 0
                        || t.IndexOf("（你", StringComparison.Ordinal) >= 0 || t.IndexOf("(你", StringComparison.Ordinal) >= 0;
                    name = CleanPlayerName(name);
                    if (name.Length > 0 && name.Length < 24 && !NameToChar.ContainsKey(name)) NameToChar[name] = c.n;
                    if (isLocal)
                    {
                        if (LocalName == "" && name != "") LocalName = name;
                        if (LocalCharacter == "") LocalCharacter = c.n;
                    }
                    break;
                }
            }
            }
        }

        public static string ToHant(string s)
        {
            if (string.IsNullOrEmpty(s) || S2T.Count == 0) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) { char h; sb.Append(S2T.TryGetValue(ch, out h) ? h : ch); }
            return sb.ToString();
        }

        public static void SetLocal(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            name = name.Trim('（', '）', '(', ')', ' ', '　');
            if (name.Length < 1 || name.Length > 12) return;
            lock (Sync) { LocalName = name; }
        }

        /* 玩家名 → 角色名。NameToChar 由右侧队伍栏的“角色称号”建立；
           找不到（例如后缀是自选技能）就回空，表示自订/未识别。 */
        public static string MapPlayer(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = CleanPlayerName(name);
            if (name.Length < 2) return "";
            lock (Sync)
            {
                string c;
                if (NameToChar.TryGetValue(name, out c)) return c;
                foreach (var kv in NameToChar)
                {
                    if (kv.Key.Length < 2) continue;
                    if (name.IndexOf(kv.Key, StringComparison.Ordinal) >= 0
                        || kv.Key.IndexOf(name, StringComparison.Ordinal) >= 0)
                        return kv.Value;
                }
                return "";
            }
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            lock (Sync)
            {
                string s = text;
                if (LocalName.Length > 0) s = s.Replace(LocalName, "你");
                foreach (var kv in NameToChar)
                {
                    if (kv.Key == LocalName || kv.Key.Length < 2) continue;
                    s = s.Replace(kv.Key, kv.Value);
                }
                return s;
            }
        }
    }

    /* ══ UI 繁／簡／English 切换 ═══════════════════════════ */
    enum UiLang { Traditional, Simplified, English }

    static class UiText
    {
        public static UiLang Lang = UiLang.Traditional;
        public static bool Simplified { get { return Lang == UiLang.Simplified; } }
        static Dictionary<char, char> _t2s;
        static Dictionary<Control, string> _source = new Dictionary<Control, string>();
        static readonly HashSet<Control> NoTranslate = new HashSet<Control>();
        static readonly HashSet<Form> SeenForms = new HashSet<Form>();

        public static void KeepText(Control c)
        {
            if (c != null) NoTranslate.Add(c);
        }
        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "客戶端", "Clients" },
            { "開啟客戶端", "Open client" },
            { "關閉客戶端", "Close client" },
            { "刷新網頁", "Refresh page" },
            { "清理緩存", "Clear cache" },
            { "再按一次確認", "Confirm again" },
            { "最後確認", "Final confirm" },
            { "解析度", "Resolution" },
            { "記錄", "Recording" },
            { "按鍵設定", "Hotkeys" },
            { "快捷鍵：開", "Hotkeys: On" },
            { "快捷鍵：關", "Hotkeys: Off" },
            { "本次記錄：開", "Session: On" },
            { "本次記錄：關", "Session: Off" },
            { "檢視與輸出", "View / export" },
            { "已記錄", "Recorded" },
            { "待處理", "Pending" },
            { "導出", "Export" },
            { "狀態：—", "Status: —" },
            { "設置", "Settings" },
            { "字號", "Text" },
            { "操作說明", "Help" },
            { "關閉", "Close" },
            { "說明", "Help" },
            { "繁體", "Traditional" },
            { "簡體", "Simplified" },
            { "English", "English" },
            { "狀態：客戶端未接上（點「開啟客戶端」可接上）", "Status: client not connected (click \"Open client\")" },
            { "快捷鍵已啟用（Q/E/C 可用）", "Hotkeys enabled (Q/E/C active)" },
            { "快捷鍵已暫停（Q/E/C 直接給遊戲）", "Hotkeys paused (Q/E/C go to game)" },
            { "狀態：已切換成簡體", "Status: Switched to Simplified" },
            { "狀態：已切換成繁體", "Status: Switched to Traditional" },
            { "狀態：已切換成英文", "Status: Switched to English" },
            { "已存一條事件", "Event saved" },
            { "寫入失敗，尚未存檔", "Write failed; not saved" },
            { "搜尋", "Search" },
            { "篩選", "Filter" },
            { "重整", "Refresh" },
            { "開啟資料夾", "Open folder" }
        };

        static void Ensure()
        {
            if (_t2s != null) return;
            _t2s = new Dictionary<char, char>();
            foreach (var kv in Party.S2T) _t2s[kv.Value] = kv.Key;
            /* s2t 裡有「爲／衆／冂／啓」等異體字也指向同一個繁體字；
               反查時會被它們覆蓋，造成「开启」變「开啓」。 */
            _t2s['啟'] = '启';
            _t2s['為'] = '为';
            _t2s['眾'] = '众';
            _t2s['門'] = '门';
            _t2s['籤'] = '签';
            _t2s['籖'] = '签';
            _t2s['鬥'] = '斗';
        }

        static readonly Dictionary<string, string> SPhrases = new Dictionary<string, string>
        {
            { "開啟資料夾", "打开文件夹" },
            { "重新整理", "刷新" },
            { "重整", "刷新" },
            { "搜尋", "搜索" },
            { "篩選", "筛选" },
            { "解析度", "分辨率" },
            { "匯出", "导出" },
            { "匯入", "导入" },
            { "介面", "界面" },
            { "預設", "默认" },
            { "載入", "加载" },
            { "儲存", "保存" },
            { "選單", "菜单" },
            { "檔案", "文件" },
            { "資料夾", "文件夹" },
            { "連線", "连接" },
            { "網路", "网络" },
            { "資訊", "信息" },
            { "帳號", "账号" },
            { "登入", "登录" },
            { "使用者", "用户" }
        };

        public static string FontFamily
        {
            get { return Lang == UiLang.Simplified ? "Microsoft YaHei UI" : "Microsoft JhengHei UI"; }
        }

        public static Font MakeFont(float size, FontStyle style)
        {
            try { return new Font(FontFamily, size, style); }
            catch { return new Font("Microsoft JhengHei UI", size, style); }
        }

        public static void ApplyFonts(Control root)
        {
            if (root == null) return;
            var form = root as Form;
            if (form != null) form.AutoScaleMode = AutoScaleMode.None;
            try
            {
                if (root.Font != null)
                    root.Font = MakeFont(root.Font.Size, root.Font.Style);
            }
            catch { }
            foreach (Control child in root.Controls) ApplyFonts(child);
        }

        public static void StyleCombo(ComboBox cb)
        {
            if (cb == null) return;
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = Color.FromArgb(44, 44, 47);
            cb.ForeColor = Color.White;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            /* 控制項字級之後會被 UiScale.Apply 放大，列高要按「目標字級」算，
               否則 1.6 倍時下拉選項會被切。 */
            int comboLineH;
            using (var cf = new Font(FontFamily, 9F * UiScale.Scale)) comboLineH = cf.Height;
            cb.ItemHeight = Math.Max(UiScale.Px(20), comboLineH + UiScale.Px(2));
            Native.DarkScroll(cb);
            cb.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                var box = (ComboBox)s;
                bool on = (e.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(on ? Color.FromArgb(62, 68, 78) : Color.FromArgb(44, 44, 47)))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                string text = e.Index >= 0 && e.Index < box.Items.Count ? System.Convert.ToString(box.Items[e.Index]) : "";
                TextRenderer.DrawText(e.Graphics, text, box.Font, e.Bounds, Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
        }

        public static void SetComboItems(ComboBox cb, params string[] items)
        {
            if (cb == null) return;
            cb.Tag = items;
            ApplyComboItems(cb);
        }

        static void ApplyComboItems(ComboBox cb)
        {
            var items = cb.Tag as string[];
            if (items == null) return;
            bool same = cb.Items.Count == items.Length;
            if (same)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (System.Convert.ToString(cb.Items[i]) != Convert(items[i], Lang == UiLang.Simplified)) { same = false; break; }
                }
            }
            /* 內容沒變就不要重建：Items.Clear() 會把 SelectedIndex 歸零再設回，
               每次都害 SelectedIndexChanged 亂發一次（自動保存因此被誤觸發）。 */
            if (same) return;
            int selected = cb.SelectedIndex;
            cb.BeginUpdate();
            try
            {
                cb.Items.Clear();
                foreach (var s in items) cb.Items.Add(Convert(s, Lang == UiLang.Simplified));
                if (selected >= 0 && selected < cb.Items.Count) cb.SelectedIndex = selected;
            }
            finally { cb.EndUpdate(); }
        }

        public static string ToTraditional(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            Ensure();
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) { char c; sb.Append(Party.S2T.TryGetValue(ch, out c) ? c : ch); }
            return sb.ToString();
        }

        public static string Convert(string s, bool toSimplified)
        {
            if (string.IsNullOrEmpty(s)) return s;
            Ensure();
            if (toSimplified)
            {
                string phrase;
                if (SPhrases.TryGetValue(s, out phrase)) return phrase;
            }
            var map = toSimplified ? _t2s : Party.S2T;
            if (map == null || map.Count == 0) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) { char c; sb.Append(map.TryGetValue(ch, out c) ? c : ch); }
            return sb.ToString();
        }

        static string Between(string s, string left, string right)
        {
            int a = s.IndexOf(left, StringComparison.Ordinal);
            if (a < 0) return "";
            a += left.Length;
            int b = s.IndexOf(right, a, StringComparison.Ordinal);
            return b < 0 ? "" : s.Substring(a, b - a);
        }

        static string English(string raw)
        {
            string s = ToTraditional(raw);
            string v;
            if (En.TryGetValue(s, out v)) return v;
            if (s.StartsWith("字號 ")) return "Text " + s.Substring(3);
            if (s.StartsWith("已記錄 ")) return "Recorded " + s.Substring(4);
            if (s.StartsWith("待處理 ")) return "Pending " + s.Substring(4);
            if (s.StartsWith("按 ") && s.EndsWith(" 開始記錄"))
            {
                string key = Between(s, "按 ", " 開始記錄");
                if (key != "") return "Press " + key + " to start";
            }
            if (s.Length >= 3 && s[1] == ' ' && s.EndsWith(" 開始記錄")) return s.Substring(0, 1) + " Start";
            if (s.Length >= 3 && s[1] == ' ' && s.EndsWith(" 結束記錄")) return s.Substring(0, 1) + " Stop";
            if (s.Length >= 3 && s[1] == ' ' && s.EndsWith(" 記錄下一步")) return s.Substring(0, 1) + " Next step";
            if (s.Length >= 3 && s[1] == ' ' && s.EndsWith(" 取消記錄")) return s.Substring(0, 1) + " Cancel";
            if (s.StartsWith("已記 ") && s.EndsWith(" 步"))
            {
                string n = Between(s, "已記 ", " 步");
                if (n != "") return "Recorded " + n + " steps";
            }
            if (s.StartsWith("狀態：客戶端已開，按 ") && s.EndsWith(" 開始"))
            {
                string key = Between(s, "按 ", " 開始");
                if (key != "") return "Status: client connected, press " + key + " to start";
            }
            if (s.StartsWith("狀態：事件模式（已記 ") && s.EndsWith(" 步）"))
            {
                string n = Between(s, "已記 ", " 步");
                if (n != "") return "Status: event mode (" + n + " steps recorded)";
            }
            if (s.StartsWith("狀態：")) return "Status: " + s.Substring(3);
            if (s.StartsWith("狀態:")) return s;
            return s;
        }

        public static string T(string s)
        {
            if (Lang == UiLang.Simplified) return Convert(s, true);
            if (Lang == UiLang.English) return English(s);
            return s;
        }

        static string Translate(string s)
        {
            if (Lang == UiLang.Traditional) return s;
            if (Lang == UiLang.Simplified) return Convert(s, true);
            return English(s);
        }

        static void Apply(Control c)
        {
            if (c == null) return;
            var combo = c as ComboBox;
            if (combo != null) ApplyComboItems(combo);
            /* 下拉選單／文字輸入的內容不是 UI 標籤，不能翻譯。 */
            if (!(c is ComboBox) && !(c is TextBox) && !NoTranslate.Contains(c))
            {
                string source;
                if (!_source.TryGetValue(c, out source))
                {
                    source = ToTraditional(c.Text);
                    _source[c] = source;
                }
                if (!string.IsNullOrEmpty(source)) c.Text = Translate(source);
            }
            foreach (Control child in c.Controls) Apply(child);
        }

        public static void SetText(Control c, string traditional)
        {
            if (c == null) return;
            _source[c] = traditional;
            c.Text = Translate(traditional);
        }

        static int _applyDepth;
        /* 套用翻譯／字型時會重設 ComboBox 的 SelectedIndex，那不是使用者操作；
           在這段期間內不要觸發自動保存。 */
        public static bool Applying { get { return _applyDepth > 0; } }

        public static void ApplyTo(Control root)
        {
            _applyDepth++;
            try
            {
                Apply(root);
                ApplyFonts(root);
            }
            finally { _applyDepth--; }
            var f = root as Form;
            if (f != null) SeenForms.Add(f);
        }

        public static void SetLang(UiLang lang)
        {
            Lang = lang;
            _applyDepth++;
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    try { Apply(f); } catch { }
                    try { ApplyFonts(f); } catch { }
                    SeenForms.Add(f);
                }
            }
            finally { _applyDepth--; }
        }

        public static void ApplyOpenForms()
        {
            var forms = new List<Form>();
            foreach (Form f in Application.OpenForms) if (f != null && !f.IsDisposed) forms.Add(f);
            _applyDepth++;
            try
            {
                foreach (var f in forms)
                {
                    if (!SeenForms.Add(f)) continue;
                    try { Apply(f); } catch { }
                    try { ApplyFonts(f); } catch { }
                }
            }
            finally { _applyDepth--; }
        }
    }

    /* ══ 記錄視窗字號（固定四檔：1.0／1.2／1.4／1.6） ═══════ */
    static class UiScale
    {
        public static readonly float[] Options = { 1.0f, 1.2f, 1.4f, 1.6f };
        public static float Scale = 1.0f;
        static readonly Dictionary<Control, Font> BaseFonts = new Dictionary<Control, Font>();

        public static float NearestScale(float value)
        {
            float best = Options[0];
            float bestDiff = float.MaxValue;
            foreach (float o in Options)
            {
                float d = Math.Abs(value - o);
                if (d < bestDiff - 0.0001f) { best = o; bestDiff = d; }
            }
            return best;
        }

        public static int Px(int value)
        {
            return Math.Max(1, (int)Math.Round(value * Scale));
        }

        public static float Value
        {
            get { return Scale; }
            set { Scale = Math.Max(1.0f, Math.Min(2.0f, value)); }
        }

        public static Form MainWindow;
        class WinSnap
        {
            public Size Client;
            public Dictionary<Control, Rectangle> Bounds = new Dictionary<Control, Rectangle>();
            public Dictionary<Control, Font> Fonts = new Dictionary<Control, Font>();
        }
        static readonly Dictionary<Form, WinSnap> Windows = new Dictionary<Form, WinSnap>();
        static readonly Dictionary<Form, float> Applied = new Dictionary<Form, float>();

        public static Size FitSize(int w, int h)
        {
            int tw = Math.Max(200, (int)Math.Round(w * Scale));
            int th = Math.Max(160, (int)Math.Round(h * Scale));
            var wa = Screen.PrimaryScreen.WorkingArea;
            tw = Math.Min(tw, Math.Max(240, wa.Width - 60));
            th = Math.Min(th, Math.Max(180, wa.Height - 100));
            return new Size(tw, th);
        }

        static void Collect(Control c, List<Control> list)
        {
            if (c == null) return;
            list.Add(c);
            foreach (Control child in c.Controls) Collect(child, list);
        }

        static void ApplyWindow(Form f)
        {
            if (f == null || f.IsDisposed) return;
            if (f is LayeredForm || f is BusyForm) return;
            if (f is RecordsForm || f is RecordEditForm) return;
            f.AutoScaleMode = AutoScaleMode.None;
            float applied;
            if (Applied.TryGetValue(f, out applied) && Math.Abs(applied - Scale) < 0.001f) return;
            bool main = f == MainWindow;
            WinSnap snap;
            if (!Windows.TryGetValue(f, out snap))
            {
                snap = new WinSnap();
                snap.Client = f.ClientSize;
                var controls = new List<Control>();
                Collect(f, controls);
                foreach (var c in controls)
                {
                    if (c == f) continue;
                    snap.Bounds[c] = c.Bounds;
                    snap.Fonts[c] = c.Font;
                }
                Windows[f] = snap;
            }
            f.SuspendLayout();
            try
            {
                if (!main && f.FormBorderStyle != FormBorderStyle.None)
                {
                    f.FormBorderStyle = FormBorderStyle.SizableToolWindow;
                    f.MaximizeBox = true;
                    f.AutoScroll = true;
                }
                if (main) f.AutoScroll = true;
                f.ClientSize = FitSize(snap.Client.Width, snap.Client.Height);
                foreach (var kv in snap.Bounds)
                {
                    var c = kv.Key;
                    if (c == null || c.IsDisposed || c.Dock != DockStyle.None) continue;
                    var b = kv.Value;
                    c.SetBounds((int)Math.Round(b.X * Scale), (int)Math.Round(b.Y * Scale),
                        Math.Max(1, (int)Math.Round(b.Width * Scale)),
                        Math.Max(1, (int)Math.Round(b.Height * Scale)));
                }
                foreach (var kv in snap.Fonts)
                {
                    var c = kv.Key;
                    if (c == null || c.IsDisposed || c is TrackBar) continue;
                    var b = kv.Value;
                    if (b == null) continue;
                    try
                    {
                        string bn = b.FontFamily.Name;
                        string fam = (bn.IndexOf("JhengHei", StringComparison.OrdinalIgnoreCase) >= 0
                            || bn.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0)
                            ? UiText.FontFamily : bn;
                        c.Font = new Font(fam, b.Size * Scale, b.Style, GraphicsUnit.Point);
                    }
                    catch { }
                }
            }
            finally { f.ResumeLayout(true); }
            if (main)
            {
                var wa = Screen.FromControl(f).WorkingArea;
                int x = Math.Min(f.Left, wa.Right - f.Width - 12);
                int y = Math.Min(f.Top, wa.Bottom - f.Height - 12);
                f.Location = new Point(Math.Max(wa.Left + 12, x), Math.Max(wa.Top + 12, y));
            }
            Applied[f] = Scale;
            try { f.Invalidate(true); } catch { }
        }

        public static void SyncOpenForms()
        {
            var forms = new List<Form>();
            foreach (Form f in Application.OpenForms)
            {
                if (f != null && !f.IsDisposed) forms.Add(f);
            }
            foreach (var f in forms)
            {
                if (f is LayeredForm || f is BusyForm) continue;
                if (f is RecordsForm || f is RecordEditForm) continue;
                ApplyWindow(f);
            }
        }

        public static void SetScale(float value)
        {
            Value = NearestScale(value);
            SyncOpenForms();
            if (RecordsForm.Instance != null && !RecordsForm.Instance.IsDisposed) RecordsForm.Instance.ApplyScale();
            if (CurrentDock.Instance != null && !CurrentDock.Instance.IsDisposed) CurrentDock.Instance.RefreshScale();
        }

        public static void Apply(Control root)
        {
            if (root == null) return;
            var controls = new List<Control>();
            Collect(root, controls);
            /* 先記下所有原始字級，再統一放大；不能在遞迴途中讀 Font，
               否則子控制項會讀到已經放大的父字級，變成 2×2×2。 */
            foreach (var c in controls)
            {
                if (c is TrackBar) continue;
                Font b;
                if (!BaseFonts.TryGetValue(c, out b))
                {
                    b = c.Font;
                    BaseFonts[c] = b;
                }
            }
            foreach (var c in controls)
            {
                if (c is TrackBar) continue;
                Font b;
                if (BaseFonts.TryGetValue(c, out b) && b != null)
                {
                    try
                    {
                        string bn = b.FontFamily.Name;
                        string fam = (bn.IndexOf("JhengHei", StringComparison.OrdinalIgnoreCase) >= 0
                            || bn.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0)
                            ? UiText.FontFamily : bn;
                        c.Font = new Font(fam, b.Size * Scale, b.Style, GraphicsUnit.Point);
                    }
                    catch { }
                }
            }
            try { root.Invalidate(true); } catch { }
        }
    }

    class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    class DarkComboBox : ComboBox
    {
        public DarkComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.FromArgb(44, 44, 47);
            ForeColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (DroppedDown) return;
            var r = ClientRectangle;
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(6, 0, Math.Max(10, r.Width - 26), r.Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Color.FromArgb(150, 150, 158)))
            {
                int ax = r.Right - 16, ay = r.Top + r.Height / 2 - 2;
                e.Graphics.DrawLine(pen, ax, ay, ax + 8, ay);
                e.Graphics.DrawLine(pen, ax, ay, ax + 4, ay + 4);
                e.Graphics.DrawLine(pen, ax + 8, ay, ax + 4, ay + 4);
            }
        }
    }

    /* 面板滾輪：只接管「真的落在本面板上」的滾輪。
       原本的低級滑鼠鉤子（WH_MOUSE_LL）拿不到視窗 z-order，別的視窗蓋在上面時
       底下的編輯器照樣被滾、事件還被吞掉；改成執行緒內的 IMessageFilter，
       用 WindowFromPoint 取真正最上層的視窗，再判斷它是不是本面板的子孫。 */
    class PanelWheelFilter : IMessageFilter
    {
        readonly Panel _panel;
        public PanelWheelFilter(Panel panel) { _panel = panel; }

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(POINT p);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;
            if (_panel == null || _panel.IsDisposed || !_panel.IsHandleCreated || !_panel.Visible) return false;
            IntPtr hwnd = WindowFromPoint(new POINT { X = Cursor.Position.X, Y = Cursor.Position.Y });
            if (hwnd == IntPtr.Zero) return false;
            Control under = Control.FromChildHandle(hwnd);
            if (under == null) return false;
            if (under != _panel && !_panel.Contains(under)) return false;
            /* 下拉選單、清單自己吃滾輪。 */
            if (under is ComboBox || under is ListBox) return false;
            int max = _panel.VerticalScroll.Maximum - _panel.VerticalScroll.LargeChange + 1;
            if (max <= 0) return false;
            int delta = (short)((long)m.WParam >> 16);
            int current = -_panel.AutoScrollPosition.Y;
            int next = Math.Max(0, Math.Min(max, current - delta));
            if (next == current) return true;
            _panel.AutoScrollPosition = new Point(0, next);
            return true;
        }
    }

    /* ══ 待處理清單 ═════════════════════════════════════════ */
    class PendingForm : Form
    {
        ListBox _list;
        List<string[]> _refs = new List<string[]>();

        public PendingForm()
        {
            Text = "待處理";
            ClientSize = new Size(700, 470);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            _list = new ListBox();
            _list.SetBounds(12, 10, 676, 370);
            _list.BackColor = Color.FromArgb(36, 36, 40);
            _list.ForeColor = Color.FromArgb(226, 226, 232);
            _list.IntegralHeight = false;
            Controls.Add(_list);

            var reload = MkBtn("重新整理", 12, 392, 110); reload.Click += delegate { Reload(); };
            var del = MkBtn("刪除選中", 132, 392, 110); del.Click += delegate { Delete(); };
            var open = MkBtn("開啟資料夾", 252, 392, 110); open.Click += delegate { try { Process.Start("explorer.exe", Program.DataDir()); } catch { } };
            var close = MkBtn("關閉", 372, 392, 110); close.Click += delegate { Close(); };
            Controls.Add(reload); Controls.Add(del); Controls.Add(open); Controls.Add(close);

            Reload();
        }

        Button MkBtn(string text, int x, int y, int w)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(44, 44, 47);
            b.ForeColor = Color.FromArgb(232, 232, 236);
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
            return b;
        }

        void Reload()
        {
            _list.Items.Clear();
            _refs.Clear();
            try
            {
                string dir = Program.DataDir();
                foreach (var f in Directory.GetFiles(dir, "pending-*.jsonl"))
                {
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        string summary = line;
                        try
                        {
                            var ser = new JavaScriptSerializer();
                            var o = ser.Deserialize<Dictionary<string, object>>(line);
                            string type = o.ContainsKey("type") ? Convert.ToString(o["type"]) : "?";
                            string name = "";
                            if (o.ContainsKey("data"))
                            {
                                var d = o["data"] as Dictionary<string, object>;
                                if (d != null)
                                {
                                    if (d.ContainsKey("floorName")) name = Convert.ToString(d["floorName"]);
                                    else if (d.ContainsKey("event")) name = Convert.ToString(d["event"]);
                                    else if (d.ContainsKey("name")) name = Convert.ToString(d["name"]);
                                }
                            }
                            summary = type + "　" + name;
                        }
                        catch { }
                        _refs.Add(new string[] { f, line });
                        _list.Items.Add(summary);
                    }
                }
            }
            catch { }
            if (_list.Items.Count == 0) _list.Items.Add("（沒有待處理）");
        }

        void Delete()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _refs.Count) return;
            string file = _refs[i][0], line = _refs[i][1];
            try
            {
                var lines = new List<string>(File.ReadAllLines(file));
                lines.Remove(line);
                File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
            Reload();
        }
    }

    /* ══ 操作說明 ═══════════════════════════════════════════ */
    class HelpForm : Form
    {
        public static HelpForm Instance;
        TextBox _text;
        Button _close;

        public HelpForm()
        {
            Instance = this;
            UiText.SetText(this, "操作說明");
            ClientSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.WordWrap = true;
            _text.SetBounds(12, 12, 536, 456);
            _text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _text.BackColor = Color.FromArgb(22, 22, 25);
            _text.ForeColor = Color.FromArgb(224, 224, 230);
            _text.BorderStyle = BorderStyle.FixedSingle;
            _text.Font = new Font("Microsoft JhengHei UI", 10F);
            Controls.Add(_text);

            _close = new Button();
            UiText.SetText(_close, "關閉");
            _close.SetBounds(448, 476, 100, 30);
            _close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _close.FlatStyle = FlatStyle.Flat;
            _close.BackColor = Color.FromArgb(44, 44, 47);
            _close.ForeColor = Color.White;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            RefreshLanguage();
        }

        public void RefreshLanguage()
        {
            UiText.SetText(this, "操作說明");
            if (_close != null) UiText.SetText(_close, "關閉");
            SetText(Program.HelpText());
        }

        public void SetText(string text) { if (_text != null) { _text.Text = text; _text.Select(0, 0); } }

        protected override void OnShown(EventArgs e) { base.OnShown(e); try { _text.Select(0, 0); } catch { } }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Instance == this) Instance = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 按鍵設定 ═══════════════════════════════════════════ */
    class KeysForm : Form
    {
        public static KeysForm Instance;
        Button _mode, _step, _cancel;
        int _cap = 0;

        public KeysForm()
        {
            Instance = this;
            Text = "按鍵設定";
            ClientSize = new Size(380, 236);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);
            KeyPreview = true;

            var tip = new Label();
            tip.Text = "點一個鍵，然後按下新的按鍵（Esc 取消）";
            tip.SetBounds(12, 8, 356, 20);
            tip.ForeColor = Color.FromArgb(170, 170, 178);
            Controls.Add(tip);

            _mode = MkBtn(20, 36);
            _mode.Click += delegate { _cap = 1; RefreshText(); };
            _step = MkBtn(20, 82);
            _step.Click += delegate { _cap = 2; RefreshText(); };
            _cancel = MkBtn(20, 128);
            _cancel.Click += delegate { _cap = 3; RefreshText(); };

            var save = new Button();
            save.Text = "保存";
            save.SetBounds(20, 176, 100, 30);
            save.FlatStyle = FlatStyle.Flat;
            save.BackColor = Color.FromArgb(44, 44, 47);
            save.ForeColor = Color.White;
            save.Click += delegate
            {
                if (Program.ModeVk == Program.StepVk || Program.ModeVk == Program.CancelVk || Program.StepVk == Program.CancelVk)
                { MessageBox.Show("三個鍵不能相同"); return; }
                Program.SaveHotkeys();
                Program.ApplyHotkeys();
                Close();
            };
            Controls.Add(save);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(132, 176, 100, 30);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(44, 44, 47);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            RefreshText();
        }

        Button MkBtn(int x, int y)
        {
            var b = new Button();
            b.SetBounds(x, y, 340, 34);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(44, 44, 47);
            b.ForeColor = Color.FromArgb(232, 232, 236);
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
            Controls.Add(b);
            return b;
        }

        void RefreshText()
        {
            _mode.Text = _cap == 1 ? "按下新鍵…（Esc 取消）" : "模式鍵：" + Program.VkName(Program.ModeVk);
            _step.Text = _cap == 2 ? "按下新鍵…（Esc 取消）" : "記錄鍵：" + Program.VkName(Program.StepVk);
            _cancel.Text = _cap == 3 ? "按下新鍵…（Esc 取消）" : "取消鍵：" + Program.VkName(Program.CancelVk);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_cap == 0) { base.OnKeyDown(e); return; }
            if (e.KeyCode == Keys.Escape) { _cap = 0; RefreshText(); return; }
            if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu) return;
            int vk = (int)e.KeyCode;
            if (_cap == 1) Program.ModeVk = vk;
            else if (_cap == 2) Program.StepVk = vk;
            else Program.CancelVk = vk;
            _cap = 0;
            RefreshText();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Instance == this) Instance = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 待處理：貼在遊戲右側的常駐浮窗，可直接改 ═════════ */
    class PendingDock : Form
    {
        static PendingDock _inst;
        public static void Toggle()
        {
            if (_inst == null || _inst.IsDisposed) { _inst = new PendingDock(); _inst.Show(); }
            else { _inst.Close(); _inst = null; }
        }
        public static void Notify()
        {
            try
            {
                if (_inst == null || _inst.IsDisposed) { _inst = new PendingDock(); _inst.Show(); }
                else _inst.Reload();
            }
            catch { }
        }

        ListBox _list;
        Label _type;
        Label _l1, _l2, _l3;
        TextBox _f1, _f2, _f3;
        Button _edit;
        List<string[]> _refs = new List<string[]>();
        List<Dictionary<string, object>> _objs = new List<Dictionary<string, object>>();
        System.Windows.Forms.Timer _follow, _refresh;
        bool _loading;

        protected override bool ShowWithoutActivation { get { return true; } }

        public PendingDock()
        {
            Text = "待處理";
            ClientSize = new Size(390, 560);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.FromArgb(230, 230, 235);

            _list = new ListBox();
            _list.SetBounds(10, 8, 370, 170);
            _list.BackColor = Color.FromArgb(36, 36, 40);
            _list.ForeColor = Color.FromArgb(226, 226, 232);
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += delegate { ShowItem(); };
            Controls.Add(_list);

            _type = new Label();
            _type.SetBounds(10, 186, 370, 20);
            _type.ForeColor = Color.FromArgb(180, 180, 190);
            Controls.Add(_type);

            _l1 = MkLabel(10, 212); _f1 = MkBox(96, 210, 284, false);
            _l2 = MkLabel(10, 242); _f2 = MkBox(96, 240, 284, false);
            _l3 = MkLabel(10, 272); _f3 = MkBox(10, 296, 370, true);

            var save = MkBtn("保存", 10, 452, 100); save.Click += delegate { Save(); };
            var del = MkBtn("刪除", 120, 452, 100); del.Click += delegate { Delete(); };
            var refresh = MkBtn("重新整理", 230, 452, 120); refresh.Click += delegate { Reload(); };
            Controls.Add(save); Controls.Add(del); Controls.Add(refresh);
            _edit = MkBtn("編輯結構", 10, 500, 370); _edit.Click += delegate { EditSelected(); };
            Controls.Add(_edit);

            var tip = new Label();
            tip.Text = "事件請按「編輯結構」修改所有文字，不要改 JSON 原文";
            tip.SetBounds(10, 532, 370, 20);
            tip.ForeColor = Color.FromArgb(140, 140, 148);
            Controls.Add(tip);

            _follow = new System.Windows.Forms.Timer();
            _follow.Interval = 400;
            _follow.Tick += delegate { FollowGame(); };
            _follow.Start();
            _refresh = new System.Windows.Forms.Timer();
            _refresh.Interval = 2500;
            _refresh.Tick += delegate { if (!_loading) Reload(); };
            _refresh.Start();

            FollowGame();
            Reload();
        }

        Label MkLabel(int x, int y)
        {
            var l = new Label();
            l.SetBounds(x, y + 4, 84, 20);
            l.ForeColor = Color.FromArgb(160, 160, 168);
            Controls.Add(l);
            return l;
        }

        TextBox MkBox(int x, int y, int w, bool multi)
        {
            var t = new TextBox();
            t.Multiline = multi;
            if (multi) t.ScrollBars = ScrollBars.Vertical;
            t.SetBounds(x, y, w, multi ? 140 : 24);
            t.BackColor = Color.FromArgb(40, 40, 44);
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(t);
            return t;
        }

        Button MkBtn(string text, int x, int y, int w)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(44, 44, 47);
            b.ForeColor = Color.FromArgb(232, 232, 236);
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
            return b;
        }

        void FollowGame()
        {
            try
            {
                IntPtr h = Program.GameHandle;
                if (h == IntPtr.Zero) return;
                RECT cr;
                if (!Native.GetClientRect(h, out cr)) return;
                var pt = new POINT { X = cr.Left, Y = cr.Top };
                Native.ClientToScreen(h, ref pt);
                int cw = cr.Right - cr.Left, ch = cr.Bottom - cr.Top;
                if (cw < 100 || ch < 100) return;
                var wa = Screen.FromPoint(new Point(pt.X + 10, pt.Y + 10)).WorkingArea;
                int x = pt.X + cw + 8;
                if (x + Width > wa.Right) x = pt.X + cw - Width - 8;
                var cur = CurrentDock.Instance;
                if (cur != null && !cur.IsDisposed && cur.Visible)
                {
                    int leftOfCurrent = cur.Left - Width - 8;
                    if (leftOfCurrent >= pt.X) x = leftOfCurrent;
                }
                if (x < wa.Left) x = wa.Left;
                int y = Math.Max(wa.Top, Math.Min(pt.Y, wa.Bottom - Height));
                if (Location.X != x || Location.Y != y) Location = new Point(x, y);
            }
            catch { }
        }

        void Reload()
        {
            _loading = true;
            try
            {
                int sel = _list.SelectedIndex;
                _list.Items.Clear();
                _refs.Clear();
                _objs.Clear();
                foreach (var f in Directory.GetFiles(Program.DataDir(), "pending-*.jsonl"))
                {
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            var o = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line);
                            if (o == null) continue;
                            string type = o.ContainsKey("type") ? Convert.ToString(o["type"]) : "?";
                            string sum = type;
                            var d = o.ContainsKey("data") ? o["data"] as Dictionary<string, object> : null;
                            if (d != null)
                            {
                                if (d.ContainsKey("floorName") && Convert.ToString(d["floorName"]) != "") sum += "　" + Convert.ToString(d["floorName"]);
                                else if (d.ContainsKey("event")) sum += "　" + Convert.ToString(d["event"]);
                            }
                            _refs.Add(new string[] { f, line });
                            _objs.Add(o);
                            _list.Items.Add(sum);
                        }
                        catch { }
                    }
                }
                Text = "待處理 " + _list.Items.Count;
                if (_list.Items.Count == 0) _list.Items.Add("（沒有待處理）");
                else if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
                else _list.SelectedIndex = 0;
            }
            catch { }
            _loading = false;
            ShowItem();
        }

        void ShowItem()
        {
            int i = _list.SelectedIndex;
            bool ok = i >= 0 && i < _objs.Count;
            _type.Text = ok ? Convert.ToString(_objs[i]["type"]) : "";
            var data = ok ? _objs[i]["data"] as Dictionary<string, object> : null;
            string type = ok ? Convert.ToString(_objs[i]["type"]) : "";
            _l1.Visible = _f1.Visible = _l2.Visible = _f2.Visible = _l3.Visible = _f3.Visible = ok;
            if (_edit != null) _edit.Visible = ok && IsStructuredPending(type);
            if (!ok || data == null) { _f1.Text = _f2.Text = _f3.Text = ""; return; }
            if (IsStructuredPending(type))
            {
                _l1.Visible = _f1.Visible = _l2.Visible = _f2.Visible = _l3.Visible = _f3.Visible = false;
                _type.Text = type + "（按下方「編輯結構」修改全部文字）";
                return;
            }
            if (type == "floor-unknown")
            {
                _l1.Text = "樓層 id"; _l2.Text = "名稱"; _l3.Text = "介紹";
                _f1.Text = Convert.ToString(data.ContainsKey("floorId") ? data["floorId"] : "");
                _f2.Text = Convert.ToString(data.ContainsKey("floorName") ? data["floorName"] : "");
                _f3.Text = Convert.ToString(data.ContainsKey("intro") ? data["intro"] : "");
            }
            else if (type == "exit-target")
            {
                _l1.Text = "目標樓層"; _f1.Text = Convert.ToString(data.ContainsKey("target") ? data["target"] : "");
                _l2.Text = "樓層"; _f2.Text = Convert.ToString(data.ContainsKey("floorId") ? data["floorId"] : "");
                _l3.Text = "出口事件"; _f3.Text = Convert.ToString(data.ContainsKey("event") ? data["event"] : "");
            }
            else
            {
                _l1.Text = "原始 JSON"; _l2.Text = ""; _l3.Text = "";
                _f1.Text = ""; _f2.Text = "";
                _f3.Text = new JavaScriptSerializer().Serialize(data);
            }
        }

        static string EditKind(string type)
        {
            if (type == "combat-enemy" || type == "character-missing") return "事件";
            if (type == "item-incomplete") return "物品";
            if (type == "shop-incomplete") return "商店";
            if (type == "floor-unknown") return "樓層";
            if (type == "exit-target") return "出口";
            return "";
        }

        static bool IsStructuredPending(string type)
        {
            return EditKind(type) != "";
        }

        void EditSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _objs.Count) return;
            var o = _objs[i];
            var data = o["data"] as Dictionary<string, object>;
            if (data == null) return;
            string type = Convert.ToString(o["type"]);
            string kind = EditKind(type);
            if (kind == "")
            {
                MessageBox.Show("這一類待處理項目尚未提供結構化編輯。", "待處理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var refs = _refs[i];
            var f = new RecordEditForm(kind, data, delegate (Dictionary<string, object> nd)
            {
                o["data"] = nd;
                PatchSourceEvent(Convert.ToString(nd.ContainsKey("id") ? nd["id"] : ""), nd);
                RemoveLine(refs[0], refs[1]);
            });
            if (f.ShowDialog() == DialogResult.OK) Reload();
            f.Dispose();
        }

        void Save()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _objs.Count) return;
            var o = _objs[i];
            var data = o["data"] as Dictionary<string, object>;
            string type = Convert.ToString(o["type"]);
            if (data == null) return;
            if (IsStructuredPending(type)) { EditSelected(); return; }
            if (type == "floor-unknown")
            {
                data["floorId"] = _f1.Text.Trim();
                data["floorName"] = _f2.Text.Trim();
                data["intro"] = _f3.Text;
            }
            else if (type == "exit-target")
            {
                data["target"] = _f1.Text.Trim();
                data["floorId"] = _f2.Text.Trim();
                data["event"] = _f3.Text;
                data["pending"] = false;
            }
            else
            {
                try
                {
                    var nd = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(_f3.Text);
                    if (nd == null) return;
                    data = nd;
                    o["data"] = nd;
                }
                catch { MessageBox.Show("JSON 格式錯誤"); return; }
            }
            PatchSource(Convert.ToString(data.ContainsKey("id") ? data["id"] : ""), data);
            RemoveLine(_refs[i][0], _refs[i][1]);
            Reload();
        }

        void Delete()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _refs.Count) return;
            RemoveLine(_refs[i][0], _refs[i][1]);
            Reload();
        }

        static void RemoveLine(string file, string line)
        {
            try
            {
                var lines = new List<string>(File.ReadAllLines(file));
                lines.Remove(line);
                File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        static void PatchSourceEvent(string id, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(id) || data == null) return;
            try
            {
                foreach (var f in Directory.GetFiles(Program.DataDir(), "*.jsonl"))
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("pending-") || name.StartsWith("draft-")) continue;
                    var lines = new List<string>(File.ReadAllLines(f));
                    bool changed = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().Length == 0) continue;
                        try
                        {
                            var rec = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(lines[i]);
                            if (rec == null || !rec.ContainsKey("id")) continue;
                            if (Convert.ToString(rec["id"]) != id) continue;
                            lines[i] = new JavaScriptSerializer().Serialize(data);
                            changed = true;
                        }
                        catch { }
                    }
                    if (changed) File.WriteAllLines(f, lines.ToArray(), new UTF8Encoding(false));
                }
            }
            catch { }
        }

        static void PatchSource(string id, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                foreach (var f in Directory.GetFiles(Program.DataDir(), "*.jsonl"))
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("pending-") || name.StartsWith("draft-")) continue;
                    var lines = new List<string>(File.ReadAllLines(f));
                    bool changed = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().Length == 0) continue;
                        try
                        {
                            var rec = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(lines[i]);
                            if (rec == null || !rec.ContainsKey("id")) continue;
                            if (Convert.ToString(rec["id"]) != id) continue;
                            foreach (var k in new string[] { "floorId", "floorName", "intro", "target", "event", "pending" })
                                if (data.ContainsKey(k)) rec[k] = data[k];
                            lines[i] = new JavaScriptSerializer().Serialize(rec);
                            changed = true;
                        }
                        catch { }
                    }
                    if (changed) File.WriteAllLines(f, lines.ToArray(), new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    /* ══ 本輪記錄：貼在遊戲右側，預設編輯最後一筆 ═══════════ */
    class CurrentDock : Form
    {
        public static CurrentDock Instance;
        public static bool DockEnabled = true;
        static System.Windows.Forms.Timer _follow;
        ListBox _list;
        Label _title;
        Label _empty;
        Button _refresh;
        Panel _editorHost;
        RecordEditForm _editor;
        bool _closingEditor;
        bool _loading;
        bool _programmaticClose;
        readonly List<RecordEntry> _entries = new List<RecordEntry>();

        public static void ShowDock()
        {
            DockEnabled = true;
            if (Instance == null || Instance.IsDisposed)
            {
                Instance = new CurrentDock();
                Instance.Show();
                UiScale.SyncOpenForms();
                Instance.RefreshScale();
            }
            else
            {
                Instance.Show();
                Instance.Activate();
                Instance.Reload(true);
            }
        }

        public static void HideDock()
        {
            DockEnabled = false;
            if (Instance != null && !Instance.IsDisposed)
            {
                Instance._programmaticClose = true;
                try { Instance.Close(); } catch { }
            }
        }

        public static void Notify()
        {
            try
            {
                if (Instance != null && !Instance.IsDisposed)
                    Instance.BeginInvoke((Action)delegate { Instance.Reload(true); });
            }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public CurrentDock()
        {
            Instance = this;
            Text = "本次記錄";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(420, 620);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.FromArgb(230, 230, 235);

            _title = new Label();
            _title.SetBounds(10, 8, 300, 22);
            _title.ForeColor = Color.FromArgb(232, 232, 236);
            Controls.Add(_title);

            _refresh = new Button();
            _refresh.Text = "重整";
            _refresh.FlatStyle = FlatStyle.Flat;
            _refresh.BackColor = Color.FromArgb(44, 44, 47);
            _refresh.ForeColor = Color.White;
            _refresh.Click += delegate { Reload(false); };
            Controls.Add(_refresh);

            _list = new ListBox();
            _list.BackColor = Color.FromArgb(36, 36, 40);
            _list.ForeColor = Color.FromArgb(226, 226, 232);
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += delegate { ShowEntry(); };
            Controls.Add(_list);

            _editorHost = new BufferedPanel();
            _editorHost.AutoScroll = true;
            _editorHost.BackColor = Color.FromArgb(24, 24, 27);
            _editorHost.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_editorHost);

            _empty = new Label();
            _empty.Text = "完成一條記錄後會自動出現在這裡。";
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.ForeColor = Color.FromArgb(160, 160, 168);
            _empty.BackColor = Color.FromArgb(24, 24, 27);
            Controls.Add(_empty);
            Resize += delegate { LayoutDock(); };

            _follow = new System.Windows.Forms.Timer();
            _follow.Interval = 400;
            _follow.Tick += delegate { FollowGame(); };
            _follow.Start();
            FollowGame();
            Reload(true);
            _list.ItemHeight = Math.Max(16, UiScale.Px(20));
            LayoutDock();
        }

        int S(int v) { return Math.Max(1, (int)Math.Round(v * UiScale.Scale)); }

        void LayoutDock()
        {
            if (_title == null || _list == null || _editorHost == null) return;
            int m = S(10);
            int titleH = S(24);
            int headerY = S(6);
            int btnW = S(90);
            _title.SetBounds(m, headerY, Math.Max(S(80), ClientSize.Width - btnW - m * 3), titleH);
            _refresh.SetBounds(ClientSize.Width - m - btnW, headerY, btnW, S(24));
            int listY = S(36);
            int listH = Math.Max(S(100), (int)(ClientSize.Height * 0.24));
            listH = Math.Min(listH, Math.Max(S(100), ClientSize.Height - listY - S(120)));
            _list.SetBounds(m, listY, Math.Max(S(120), ClientSize.Width - m * 2), listH);
            int editorY = listY + listH + S(8);
            _editorHost.SetBounds(m, editorY, Math.Max(S(120), ClientSize.Width - m * 2),
                Math.Max(S(80), ClientSize.Height - editorY - m));
            _empty.SetBounds(m, listY, Math.Max(S(120), ClientSize.Width - m * 2),
                Math.Max(S(80), ClientSize.Height - listY - m));
            bool empty = _entries.Count == 0;
            _list.Visible = !empty;
            _editorHost.Visible = !empty;
            _empty.Visible = empty;
        }

        void FollowGame()
        {
            try
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                IntPtr h = Program.GameHandle;
                if (h != IntPtr.Zero)
                {
                    RECT cr;
                    if (Native.GetClientRect(h, out cr))
                    {
                        var pt = new POINT { X = cr.Left, Y = cr.Top };
                        Native.ClientToScreen(h, ref pt);
                        int cw = cr.Right - cr.Left, ch = cr.Bottom - cr.Top;
                        if (cw >= 100 && ch >= 100)
                        {
                            wa = Screen.FromPoint(new Point(pt.X + 10, pt.Y + 10)).WorkingArea;
                            int x = pt.X + cw + 8;
                            if (x + Width > wa.Right) x = pt.X - Width - 8;
                            if (x < wa.Left) x = wa.Left;
                            int y = Math.Max(wa.Top, Math.Min(pt.Y, wa.Bottom - Height));
                            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
                            return;
                        }
                    }
                }
                int rx = wa.Right - Width - 12;
                int ry = wa.Top + 80;
                if (Location.X != rx || Location.Y != ry) Location = new Point(rx, ry);
            }
            catch { }
        }

        static List<RecordEntry> LoadSession()
        {
            var list = new List<RecordEntry>();
            try
            {
                foreach (var f in Directory.GetFiles(Program.DataDir(), "*.jsonl"))
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("draft-") || name.StartsWith("pending-") || name.StartsWith("floor-lengths-")) continue;
                    string kind = name.StartsWith("events-") ? "事件" : name.StartsWith("items-") ? "物品" : name.StartsWith("floors-") ? "樓層" : name.StartsWith("exits-") ? "出口" : name.StartsWith("endings-") ? "結局" : name.StartsWith("shops-") ? "商店" : "其他";
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            var o = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line);
                            if (o == null) continue;
                            string ts = o.ContainsKey("ts") ? Convert.ToString(o["ts"]) : "";
                            DateTimeOffset at;
                            if (!DateTimeOffset.TryParse(ts, out at)) continue;
                            if (at.LocalDateTime < Program.SessionStart) continue;
                            var e = new RecordEntry { File = f, Line = line, Data = o, Kind = kind, At = at.LocalDateTime };
                            e.Time = at.LocalDateTime.ToString("HH:mm");
                            e.Title = RecordsForm.TitleOf(kind, o);
                            list.Add(e);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            list.Sort(delegate (RecordEntry a, RecordEntry b) { return a.At.CompareTo(b.At); });
            return list;
        }

        void Reload(bool selectLast)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                int old = _list.SelectedIndex;
                string oldLine = old >= 0 && old < _entries.Count ? _entries[old].Line : null;
                var entries = LoadSession();
                _entries.Clear();
                _entries.AddRange(entries);
                _list.Items.Clear();
                foreach (var e in _entries) _list.Items.Add(e.Kind + "　" + e.Title + "　" + e.Time);
                _title.Text = "本次記錄 · " + _entries.Count + " 筆";
                if (_entries.Count == 0)
                {
                    _list.Items.Add("（本次尚無記錄）");
                    _list.SelectedIndex = -1;
                }
                else
                {
                    int idx = _entries.Count - 1;
                    if (!selectLast && oldLine != null)
                    {
                        for (int i = 0; i < _entries.Count; i++) if (_entries[i].Line == oldLine) { idx = i; break; }
                    }
                    _list.SelectedIndex = idx;
                }
            }
            catch { }
            finally { _loading = false; }
            ShowEntry();
            LayoutDock();
        }

        void ClearEditor()
        {
            _closingEditor = true;
            try
            {
                if (_editor != null)
                {
                    try { if (!_editor.IsDisposed) _editor.Close(); } catch { }
                    try { if (!_editor.IsDisposed) _editor.Dispose(); } catch { }
                    _editor = null;
                }
                _editorHost.Controls.Clear();
            }
            finally { _closingEditor = false; }
        }

        void ShowEntry()
        {
            ClearEditor();
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _entries.Count)
            {
                var tip = new Label();
                tip.Text = "本次還沒有記錄；完成一條後會自動出現在這裡。";
                tip.SetBounds(14, 14, 370, 24);
                tip.ForeColor = Color.FromArgb(160, 160, 168);
                _editorHost.Controls.Add(tip);
                UiText.ApplyTo(tip);
                return;
            }
            var f = new RecordEditForm(_entries[i]);
            f.TopLevel = false;
            f.MinimumSize = Size.Empty;
            f.FormBorderStyle = FormBorderStyle.None;
            f.TopMost = false;
            f.ShowInTaskbar = false;
            f.Dock = DockStyle.Fill;
            f.FormClosed += delegate
            {
                if (_editor == f) _editor = null;
                if (!_closingEditor && !IsDisposed) BeginInvoke((Action)delegate { Reload(false); });
            };
            _editor = f;
            _editorHost.Controls.Add(f);
            f.Show();
            UiText.ApplyTo(f);
            _editorHost.Invalidate(true);
        }

        public void RefreshScale()
        {
            _list.ItemHeight = Math.Max(16, UiScale.Px(20));
            LayoutDock();
            Reload(false);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_follow != null) _follow.Stop(); } catch { }
            if (!_programmaticClose && !Program.Closing)
            {
                DockEnabled = false;
                try { Program.SaveHotkeys(); } catch { }
                try { Program.UpdateCurrentDockButton(); } catch { }
            }
            _programmaticClose = false;
            if (Instance == this) Instance = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 導出：選項＋壓縮 ═══════════════════════════════════ */
    class ExportForm : Form
    {
        public static ExportForm Instance;
        CheckBox _ev, _it, _fl, _ex, _en, _shop, _merge, _md, _zip;
        TextBox _dir;
        Button _go;
        ProgressBar _bar;
        Label _status;

        public ExportForm()
        {
            Instance = this;
            Text = "導出記錄";
            ClientSize = new Size(500, 330);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            var l = new Label(); l.Text = "導出內容"; l.SetBounds(12, 10, 200, 20); l.ForeColor = Color.FromArgb(170, 170, 178); Controls.Add(l);
            _ev = MkCheck("事件", 12, 32, 80); _ev.Checked = true;
            _it = MkCheck("物品", 92, 32, 80); _it.Checked = true;
            _fl = MkCheck("樓層", 172, 32, 80); _fl.Checked = true;
            _ex = MkCheck("出口", 252, 32, 80); _ex.Checked = true;
            _en = MkCheck("結局", 332, 32, 80); _en.Checked = true;
            _shop = MkCheck("商店", 412, 32, 70); _shop.Checked = true;

            _merge = MkCheck("合併同名事件", 12, 62, 180); _merge.Checked = true;
            _md = MkCheck("生成 Markdown", 200, 62, 180); _md.Checked = true;
            _zip = MkCheck("壓縮成 ZIP（體積最小）", 12, 92, 260); _zip.Checked = true;

            var dl = new Label(); dl.Text = "輸出到"; dl.SetBounds(12, 128, 60, 20); dl.ForeColor = Color.FromArgb(170, 170, 178); Controls.Add(dl);
            _dir = new TextBox();
            _dir.SetBounds(70, 126, 320, 24);
            _dir.BackColor = Color.FromArgb(40, 40, 44);
            _dir.ForeColor = Color.White;
            _dir.BorderStyle = BorderStyle.FixedSingle;
            _dir.Text = Program.ExportDir();
            Controls.Add(_dir);
            var browse = new Button();
            browse.Text = "瀏覽";
            browse.SetBounds(398, 126, 90, 26);
            browse.FlatStyle = FlatStyle.Flat;
            browse.BackColor = Color.FromArgb(44, 44, 47);
            browse.ForeColor = Color.White;
            browse.Click += delegate
            {
                var d = new FolderBrowserDialog();
                d.SelectedPath = _dir.Text;
                if (d.ShowDialog() == DialogResult.OK) _dir.Text = d.SelectedPath;
            };
            Controls.Add(browse);

            _go = new Button();
            _go.Text = "開始導出";
            _go.SetBounds(12, 166, 140, 34);
            _go.FlatStyle = FlatStyle.Flat;
            _go.BackColor = Color.FromArgb(44, 44, 47);
            _go.ForeColor = Color.White;
            _go.Click += delegate { Export(); };
            Controls.Add(_go);

            var close = new Button();
            close.Text = "關閉";
            close.SetBounds(398, 166, 90, 34);
            close.FlatStyle = FlatStyle.Flat;
            close.BackColor = Color.FromArgb(44, 44, 47);
            close.ForeColor = Color.White;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            _bar = new ProgressBar();
            _bar.SetBounds(12, 214, 476, 16);
            _bar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_bar);

            _status = new Label();
            _status.SetBounds(12, 240, 476, 78);
            _status.ForeColor = Color.FromArgb(190, 190, 198);
            _status.Text = "選好內容按「開始導出」。ZIP 內含 記錄.json（緊湊格式）與 記錄.md。";
            Controls.Add(_status);
        }

        CheckBox MkCheck(string text, int x, int y, int w)
        {
            var c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 24);
            c.ForeColor = Color.FromArgb(225, 225, 230);
            c.BackColor = Color.Transparent;
            Controls.Add(c);
            return c;
        }

        public void Export()
        {
            _go.Enabled = false;
            _bar.Value = 10;
            Application.DoEvents();
            try
            {
                string dir = _dir.Text.Trim();
                if (dir.Length == 0) dir = Program.ExportDir();
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
                var ev = _ev.Checked ? Program.ReadAll("events-") : new List<Dictionary<string, object>>();
                var it = _it.Checked ? Program.ReadAll("items-") : new List<Dictionary<string, object>>();
                var fl = _fl.Checked ? Program.ReadAll("floors-") : new List<Dictionary<string, object>>();
                var ex = _ex.Checked ? Program.ReadAll("exits-") : new List<Dictionary<string, object>>();
                var en = _en.Checked ? Program.ReadAll("endings-") : new List<Dictionary<string, object>>();
                var sh = _shop.Checked ? Program.ReadAll("shops-") : new List<Dictionary<string, object>>();
                if (_merge.Checked) ev = Program.DedupeEvents(ev);
                _bar.Value = 40;
                Application.DoEvents();

                var root = new Dictionary<string, object>();
                root["exported"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var counts = new Dictionary<string, object>();
                counts["events"] = ev.Count; counts["items"] = it.Count; counts["floors"] = fl.Count;
                counts["exits"] = ex.Count; counts["endings"] = en.Count; counts["shops"] = sh.Count; counts["shops"] = sh.Count;
                root["counts"] = counts;
                root["events"] = ev; root["items"] = it; root["floors"] = fl; root["exits"] = ex; root["endings"] = en; root["shops"] = sh; root["shops"] = sh;

                string temp = Path.Combine(dir, ".export-tmp-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                string jsonPath = Path.Combine(temp, "記錄.json");
                File.WriteAllText(jsonPath, new JavaScriptSerializer().Serialize(root), new UTF8Encoding(false));
                if (_md.Checked)
                {
                    File.WriteAllText(Path.Combine(temp, "記錄.md"),
                        Program.BuildMarkdown(ev, it, fl, ex, en, sh, stamp), new UTF8Encoding(false));
                }
                _bar.Value = 70;
                Application.DoEvents();

                string result;
                if (_zip.Checked)
                {
                    result = Path.Combine(dir, "記錄-" + stamp + ".zip");
                    if (File.Exists(result)) File.Delete(result);
                    ZipFile.CreateFromDirectory(temp, result, CompressionLevel.Optimal, false);
                    try { Directory.Delete(temp, true); } catch { }
                }
                else
                {
                    result = Path.Combine(dir, "記錄-" + stamp + ".json");
                    File.Copy(jsonPath, result, true);
                    if (_md.Checked) File.Copy(Path.Combine(temp, "記錄.md"), Path.Combine(dir, "記錄-" + stamp + ".md"), true);
                    try { Directory.Delete(temp, true); } catch { }
                }
                _bar.Value = 100;
                long size = new FileInfo(result).Length;
                string sizeText = size >= 1024 * 1024 ? (size / 1024.0 / 1024.0).ToString("0.0") + " MB" : (size / 1024.0).ToString("0.0") + " KB";
                _status.Text = "已導出：" + Path.GetFileName(result) + "（" + sizeText + "）\n"
                    + "事件 " + ev.Count + " · 物品 " + it.Count + " · 樓層 " + fl.Count + " · 出口 " + ex.Count + " · 結局 " + en.Count + "\n"
                    + "路徑：" + dir;
                try { Process.Start("explorer.exe", "/select,\"" + result + "\""); } catch { }
            }
            catch (Exception ex)
            {
                _status.Text = "導出失敗：" + ex.Message;
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export-err.log"), ex.ToString()); } catch { }
            }
            _go.Enabled = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Instance == this) Instance = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 商店：貨架或商店事件 ═══════════════════════════════ */
    class ShopForm : Form
    {
        public List<string> Items = new List<string>();
        public string Title = "", Intro = "";
        TextBox _items, _title, _intro;

        public ShopForm(List<string> items, string title, string intro)
        {
            bool isShop = items != null;
            Text = isShop ? "記錄商店貨架" : "記錄商店事件";
            ClientSize = new Size(470, 430);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            if (isShop)
            {
                var l = new Label(); l.Text = "貨架物品（每行一個，可手改）"; l.SetBounds(12, 10, 440, 20); l.ForeColor = Color.FromArgb(170, 170, 178); Controls.Add(l);
                _items = new TextBox();
                _items.Multiline = true;
                _items.ScrollBars = ScrollBars.Vertical;
                _items.SetBounds(12, 36, 446, 300);
                _items.BackColor = Color.FromArgb(32, 32, 36);
                _items.ForeColor = Color.White;
                _items.BorderStyle = BorderStyle.FixedSingle;
                _items.Font = new Font("Microsoft JhengHei UI", 10F);
                _items.Text = string.Join("\r\n", items.ToArray());
                Controls.Add(_items);
            }
            else
            {
                var l1 = new Label(); l1.Text = "標題"; l1.SetBounds(12, 10, 60, 20); l1.ForeColor = Color.FromArgb(170, 170, 178); Controls.Add(l1);
                _title = new TextBox();
                _title.SetBounds(12, 34, 446, 24);
                _title.BackColor = Color.FromArgb(32, 32, 36);
                _title.ForeColor = Color.White;
                _title.BorderStyle = BorderStyle.FixedSingle;
                _title.Text = title ?? "";
                Controls.Add(_title);
                var l2 = new Label(); l2.Text = "介紹"; l2.SetBounds(12, 70, 60, 20); l2.ForeColor = Color.FromArgb(170, 170, 178); Controls.Add(l2);
                _intro = new TextBox();
                _intro.Multiline = true;
                _intro.ScrollBars = ScrollBars.Vertical;
                _intro.SetBounds(12, 94, 446, 242);
                _intro.BackColor = Color.FromArgb(32, 32, 36);
                _intro.ForeColor = Color.White;
                _intro.BorderStyle = BorderStyle.FixedSingle;
                _intro.Font = new Font("Microsoft JhengHei UI", 10F);
                _intro.Text = intro ?? "";
                Controls.Add(_intro);
            }

            Shown += delegate { try { if (_items != null) _items.Select(0, 0); if (_intro != null) _intro.Select(0, 0); } catch { } };
            var save = new Button();
            save.Text = "保存";
            save.SetBounds(12, 350, 120, 30);
            save.FlatStyle = FlatStyle.Flat;
            save.BackColor = Color.FromArgb(44, 44, 47);
            save.ForeColor = Color.White;
            save.Click += delegate
            {
                if (isShop)
                {
                    Items.Clear();
                    foreach (var line in _items.Lines)
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && !Items.Contains(t)) Items.Add(t);
                    }
                }
                else
                {
                    Title = _title.Text.Trim();
                    Intro = _intro.Text.Trim();
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(144, 350, 120, 30);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(44, 44, 47);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }
    }

    /* ══ 已記錄：可視化清單 ═════════════════════════════════ */
    class RecordEntry
    {
        public string File, Line, Kind, Title, Time;
        public DateTime At;
        public Dictionary<string, object> Data;
    }

    /* ══ 已記錄：結構化編輯（不讓玩家改 JSON 原文） ═════════ */
    class RecordEditForm : Form
    {
        RecordEntry _entry;
        string _kind;
        Dictionary<string, object> _data;
        Action<Dictionary<string, object>> _externalSave;
        bool _lockIdentity;

        /* ── 版面：所有尺寸只從這幾個數推，其他方法不准自己算一套 ───────── */
        const float BasePt = 9F;
        Panel _header;
        Label _headerTitle, _headerStatus;
        Panel _panel;
        PanelWheelFilter _wheelFilter;
        int _y;
        int _labelW, _fieldH, _comboH, _buttonH, _capH;
        List<Action> _savers = new List<Action>();
        class Decor { public Rectangle Rect; public Rectangle Caption; public bool Divider; public bool HasCaption; }
        List<Decor> _decor = new List<Decor>();
        System.Windows.Forms.Timer _autoSave;
        System.Windows.Forms.Timer _relayout;
        int _lastPanelWidth;
        bool _rebuilding;
        bool _saving;
        bool _dirty;
        bool _normalized;
        bool _firstShown;

        int Px(int v) { return UiScale.Px(v); }

        int TextW(string s, float pt)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            using (var f = new Font(UiText.FontFamily, pt * UiScale.Scale))
                return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        /* 一次性量出所有控制項的「基線高度」；之後版面只認這幾個數。 */
        void MeasureMetrics()
        {
            using (var f = new Font(UiText.FontFamily, BasePt * UiScale.Scale))
            {
                using (var t = new TextBox()) { t.Font = f; _fieldH = t.PreferredHeight; }
                using (var cb = new ComboBox())
                {
                    cb.Font = f;
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.ItemHeight = Math.Max(Px(20), f.Height + Px(2));
                    _comboH = Math.Max(_fieldH, cb.PreferredHeight);
                }
                _capH = f.Height + Px(6);
                _buttonH = Math.Max(Px(26), _fieldH + Px(3));
                _labelW = Math.Max(Px(56), TextW("⭐條件選項", BasePt) + Px(10));
            }
        }

        int PageLeft { get { return Px(14); } }
        int PageRight { get { return Px(14); } }
        int LabelGap { get { return Px(10); } }
        int RowGap { get { return Px(6); } }
        int FieldX { get { return PageLeft + _labelW + LabelGap; } }

        /* 版面寬度永遠扣掉一個滾動條寬度：不管這條記錄要不要滾，輸入框都停在同一條線上，
           翻記錄不會重新斷行。空出來的那 17px 由 PaintDecor 畫成滾動條槽。 */
        int PanelWidth()
        {
            int w = _panel == null ? UiScale.Px(300) : (_panel.Width - SystemInformation.VerticalScrollBarWidth);
            return Math.Max(UiScale.Px(120), w);
        }

        int FieldRight { get { return Math.Max(FieldX + Px(80), PanelWidth() - PageRight); } }
        int FieldW { get { return Math.Max(Px(80), FieldRight - FieldX); } }
        bool Narrow { get { return FieldW < Px(300); } }

        public RecordEntry Entry { get { return _entry; } }

        public RecordEditForm(RecordEntry entry)
            : this(entry.Kind, entry.Data, null, true)
        {
            _entry = entry;
            Text = "編輯記錄：" + entry.Kind;
            /* 只有獨立視窗才需要最小尺寸；嵌進「已記錄」右欄時由 RecordsForm 控制，
               留在這裡會讓內層窗體撐得比宿主寬、右邊被裁掉。 */
            MinimumSize = UiScale.FitSize(430, 320);
        }

        public RecordEditForm(string kind, Dictionary<string, object> data, Action<Dictionary<string, object>> externalSave)
            : this(kind, data, externalSave, false)
        {
        }

        public RecordEditForm(string kind, Dictionary<string, object> data, Action<Dictionary<string, object>> externalSave, bool lockIdentity)
        {
            _kind = kind;
            _data = data;
            _externalSave = externalSave;
            _lockIdentity = lockIdentity;
            Text = "編輯記錄：" + kind;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = UiScale.FitSize(900, 720);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);
            KeyPreview = true;

            _header = new Panel();
            _header.BackColor = Color.FromArgb(24, 24, 27);
            _header.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(48, 48, 54)))
                    e.Graphics.DrawLine(pen, 0, _header.ClientSize.Height - 1,
                        _header.ClientSize.Width, _header.ClientSize.Height - 1);
            };
            Controls.Add(_header);

            _headerTitle = new Label();
            _headerTitle.ForeColor = Color.FromArgb(240, 240, 244);
            _headerTitle.Font = new Font(UiText.FontFamily, 10F, FontStyle.Bold);
            _headerTitle.TextAlign = ContentAlignment.MiddleLeft;
            _headerTitle.AutoEllipsis = true;
            _header.Controls.Add(_headerTitle);

            _headerStatus = new Label();
            _headerStatus.ForeColor = Color.FromArgb(140, 140, 148);
            _headerStatus.TextAlign = ContentAlignment.MiddleRight;
            _headerStatus.AutoEllipsis = true;
            _header.Controls.Add(_headerStatus);

            _panel = new BufferedPanel();
            _panel.AutoScroll = true;
            _panel.BackColor = Color.FromArgb(32, 32, 36);
            _panel.Paint += PaintDecor;
            Native.DarkScroll(_panel);
            Controls.Add(_panel);

            _wheelFilter = new PanelWheelFilter(_panel);
            Application.AddMessageFilter(_wheelFilter);

            MeasureMetrics();
            Resize += delegate { LayoutEditor(); };
            Shown += delegate { LayoutEditor(); };
            LayoutEditor();

            BuildFields();
            UiScale.Apply(this);
            NormalizeTextHeights();
            _lastPanelWidth = PanelWidth();
            _panel.Invalidate(true);
            HookAutoSave(_panel);

            _panel.ClientSizeChanged += delegate
            {
                /* 版面寬度固定（永遠扣掉滾動條），開合只要重畫槽位，不必重排。 */
                HideHorizontalScroll();
                _panel.Invalidate();
            };
            _panel.SizeChanged += delegate { QueueRelayout(false); };
            Shown += delegate
            {
                if (_firstShown) return;
                _firstShown = true;
                LayoutEditor();
                NormalizeTextHeights();
            };

            _autoSave = new System.Windows.Forms.Timer();
            _autoSave.Interval = 600;
            _autoSave.Tick += delegate
            {
                _autoSave.Stop();
                if (!_saving) SaveData(false);
            };
            _relayout = new System.Windows.Forms.Timer();
            _relayout.Interval = 160;
            _relayout.Tick += delegate { _relayout.Stop(); RelayoutFields(); };
            /* 開啟時若把折行的結果併過段，就排一次存檔，讓 JSONL 也變成合併後的樣子。 */
            if (_normalized) ScheduleSave();
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.Control && e.KeyCode == Keys.S)
                {
                    if (_autoSave != null) _autoSave.Stop();
                    SaveData(false);
                }
            };
            SetHeader();
        }

        void LayoutEditor()
        {
            if (_panel == null || _header == null) return;
            int headerH = Px(36);
            _header.SetBounds(0, 0, Math.Max(0, ClientSize.Width), headerH);
            _panel.SetBounds(0, headerH, Math.Max(0, ClientSize.Width), Math.Max(0, ClientSize.Height - headerH));
            LayoutHeader();
        }

        void LayoutHeader()
        {
            if (_headerTitle == null || _headerStatus == null) return;
            int w = _header.ClientSize.Width;
            int statusW = Math.Min(Math.Max(Px(96), TextW("已保存 00:00:00", 8.5F) + Px(18)), Math.Max(Px(60), w / 2));
            _headerStatus.SetBounds(Math.Max(0, w - statusW - Px(12)), 0, statusW, _header.ClientSize.Height);
            _headerTitle.SetBounds(Px(14), 0, Math.Max(Px(40), w - statusW - Px(34)), _header.ClientSize.Height);
        }

        void SetHeader()
        {
            if (_headerTitle == null) return;
            string title = _kind;
            try
            {
                string t = RecordsForm.TitleOf(_kind, _data);
                if (!string.IsNullOrEmpty(t) && t != "?") title += " · " + t;
            }
            catch { }
            UiText.SetText(_headerTitle, title);
            LayoutHeader();
        }

        void SetStatus(string text, Color? color = null)
        {
            if (_headerStatus == null) return;
            UiText.SetText(_headerStatus, text ?? "");
            _headerStatus.ForeColor = color ?? Color.FromArgb(140, 140, 148);
        }

        void HideHorizontalScroll()
        {
            try
            {
                if (_panel != null && _panel.IsHandleCreated)
                {
                    _panel.HorizontalScroll.Maximum = 0;
                    _panel.HorizontalScroll.Enabled = false;
                    _panel.HorizontalScroll.Visible = false;
                }
            }
            catch { }
        }

        /* 選項框（矩形）與結果區（分隔線）畫在面板背景上，子控制項再蓋上去。
           標題位置的上邊線留缺口，不再靠 Label 背景去遮。 */
        void PaintDecor(object sender, PaintEventArgs e)
        {
            /* 沒有滾動條時，把預留的 17px 畫成槽，避免看起來像「右邊多空一條」。 */
            if (!_panel.VerticalScroll.Visible)
            {
                int sw = PanelWidth();
                int cw = _panel.ClientSize.Width;
                if (cw > sw + 2)
                {
                    using (var b = new SolidBrush(Color.FromArgb(25, 25, 29)))
                        e.Graphics.FillRectangle(b, sw, 0, cw - sw, _panel.ClientSize.Height);
                    using (var p = new Pen(Color.FromArgb(40, 40, 46)))
                        e.Graphics.DrawLine(p, sw, 0, sw, _panel.ClientSize.Height);
                }
            }
            if (_decor.Count == 0) return;
            int ox = _panel.AutoScrollPosition.X;
            int oy = _panel.AutoScrollPosition.Y;
            using (var pen = new Pen(Color.FromArgb(96, 99, 108)))
            {
                foreach (var d in _decor)
                {
                    if (d.Rect.Width <= 2) continue;
                    int left = d.Rect.X + ox;
                    int right = left + d.Rect.Width - 1;
                    int top = d.Rect.Y + oy;
                    int bottom = top + d.Rect.Height - 1;
                    if (d.Divider)
                    {
                        if (d.HasCaption)
                        {
                            int cx1 = d.Caption.X + ox - Px(4);
                            int cx2 = d.Caption.Right + ox + Px(4);
                            if (cx1 - left > Px(12)) e.Graphics.DrawLine(pen, left, top, Math.Min(cx1, right), top);
                            if (cx2 < right) e.Graphics.DrawLine(pen, Math.Max(cx2, left), top, right, top);
                        }
                        else e.Graphics.DrawLine(pen, left, top, right, top);
                        continue;
                    }
                    if (bottom <= top) continue;
                    e.Graphics.DrawLine(pen, left, top, left, bottom);
                    e.Graphics.DrawLine(pen, right, top, right, bottom);
                    e.Graphics.DrawLine(pen, left, bottom, right, bottom);
                    if (d.HasCaption)
                    {
                        int cx1 = d.Caption.X + ox - Px(8);
                        int cx2 = d.Caption.Right + ox + Px(8);
                        if (cx1 - left > Px(12)) e.Graphics.DrawLine(pen, left, top, Math.Min(cx1, right), top);
                        if (cx2 < right) e.Graphics.DrawLine(pen, Math.Max(cx2, left), top, right, top);
                    }
                    else e.Graphics.DrawLine(pen, left, top, right, top);
                }
            }
        }

        void AddHeading(string text)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(PageLeft, _y, Math.Max(Px(60), FieldRight - PageLeft), _capH);
            l.ForeColor = Color.FromArgb(255, 214, 120);
            l.Font = new Font(UiText.FontFamily, 10F, FontStyle.Bold);
            l.TextAlign = ContentAlignment.MiddleLeft;
            _panel.Controls.Add(l);
            _y += _capH + RowGap;
        }

        Rectangle AddCaption(string caption, int x, int y)
        {
            var cap = new Label();
            cap.Text = caption;
            int w = TextW(caption, BasePt) + Px(12);
            cap.SetBounds(x, y, w, _capH);
            cap.BackColor = _panel.BackColor;
            cap.ForeColor = Color.FromArgb(255, 214, 120);
            cap.Font = new Font(UiText.FontFamily, BasePt, FontStyle.Bold);
            cap.TextAlign = ContentAlignment.MiddleLeft;
            _panel.Controls.Add(cap);
            return new Rectangle(x, y, w, _capH);
        }

        /* 選項外框：左邊比頁面多探出 Px(6)，右邊和所有輸入框同一條線。 */
        Decor AddOptionFrame(int top, string caption)
        {
            var d = new Decor();
            d.HasCaption = true;
            d.Rect = new Rectangle(PageLeft - Px(6), top + _capH / 2,
                FieldRight - PageLeft + Px(12), Px(40));
            _decor.Add(d);
            d.Caption = AddCaption(caption, PageLeft + Px(4), top);
            _y = top + _capH + Px(2);
            return d;
        }

        /* 結果區：一條分隔線 + 標題，不再畫第二個框（避免兩條框線疊在一起）。 */
        Decor AddDivider(int top, string caption)
        {
            var d = new Decor();
            d.Divider = true;
            d.HasCaption = true;
            d.Rect = new Rectangle(PageLeft + Px(10), top + _capH / 2,
                FieldRight - PageLeft - Px(10), 1);
            _decor.Add(d);
            d.Caption = AddCaption(caption, PageLeft + Px(20), top);
            _y = top + _capH + Px(2);
            return d;
        }

        void CloseDecor(Decor d, int bottom)
        {
            if (d == null || d.Divider) return;
            d.Rect = new Rectangle(d.Rect.X, d.Rect.Y, d.Rect.Width,
                Math.Max(Px(24), bottom - d.Rect.Y));
        }

        void AddFixed(string label, string value)
        {
            var l = new Label();
            l.Text = label;
            l.SetBounds(PageLeft, _y, _labelW, _fieldH);
            l.TextAlign = ContentAlignment.MiddleRight;
            l.ForeColor = Color.FromArgb(170, 170, 178);
            _panel.Controls.Add(l);
            var v = new Label();
            v.Text = value ?? "";
            v.SetBounds(FieldX, _y, FieldW, _fieldH);
            v.TextAlign = ContentAlignment.MiddleLeft;
            v.ForeColor = Color.FromArgb(220, 220, 226);
            _panel.Controls.Add(v);
            _y += _fieldH + RowGap;
        }

        static string S(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k) || d[k] == null) return "";
            return Convert.ToString(d[k]);
        }

        static string A(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k)) return "";
            var arr = d[k] as System.Collections.ArrayList;
            if (arr == null) return S(d, k);
            var parts = new List<string>();
            foreach (var x in arr) parts.Add(Convert.ToString(x));
            return string.Join("、", parts.ToArray());
        }

        static string EventTitleOf(Dictionary<string, object> o)
        {
            var steps = o.ContainsKey("steps") ? o["steps"] as System.Collections.ArrayList : null;
            if (steps != null && steps.Count > 0)
            {
                var s1 = steps[0] as Dictionary<string, object>;
                if (s1 != null && S(s1, "title") != "") return S(s1, "title");
            }
            return S(o, "title");
        }

        void AddLabel(string text, int y, int height, Color? color)
        {
            var l = new Label();
            l.Text = text;
            if (Narrow)
            {
                l.SetBounds(PageLeft, y, FieldW, height);
                l.TextAlign = ContentAlignment.MiddleLeft;
            }
            else
            {
                l.SetBounds(PageLeft, y, _labelW, height);
                l.TextAlign = ContentAlignment.MiddleRight;
            }
            l.ForeColor = color ?? Color.FromArgb(170, 170, 178);
            _panel.Controls.Add(l);
        }

        Font BodyFont()
        {
            return new Font(UiText.FontFamily, BasePt * UiScale.Scale);
        }

        /* 用 TextRenderer 量出「視覺行數」，再乘 TextBox 真正的行距（Font.Height）。
           兩者行距不同（實測 15px vs 16px），直接拿 TextRenderer 的高度會每行少 1px。 */
        int MinMultiHeight() { return Px(24); }

        int MultiHeight(string text)
        {
            using (var f = BodyFont())
            {
                int one = Math.Max(1, TextRenderer.MeasureText("国", f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height);
                int w = Math.Max(24, FieldW - Px(10));
                int lines = 0;
                if (string.IsNullOrEmpty(text)) lines = 1;
                else
                {
                    foreach (var p in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    {
                        if (p.Length == 0) { lines++; continue; }
                        int h = TextRenderer.MeasureText(p, f, new Size(w, int.MaxValue),
                            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding).Height;
                        lines += Math.Max(1, (int)Math.Ceiling(h / (double)one));
                    }
                }
                return Math.Max(MinMultiHeight(), Math.Max(1, lines) * f.Height + Px(8));
            }
        }

        int ExactTextHeight(TextBox t)
        {
            int lineH = t.Font.Height;
            if (string.IsNullOrEmpty(t.Text)) return Math.Max(MinMultiHeight(), lineH + Px(8));
            int last = t.GetPositionFromCharIndex(t.TextLength - 1).Y;
            return Math.Max(MinMultiHeight(), last + lineH + Px(8));
        }

        TextBox AddRaw(string label, string value, bool multi, int height, Color? labelColor = null, Color? textColor = null)
        {
            bool narrow = Narrow;
            var t = new TextBox();
            t.Multiline = multi;
            t.WordWrap = true;
            t.ScrollBars = ScrollBars.None;
            t.Text = value ?? "";
            t.BackColor = Color.FromArgb(40, 40, 44);
            t.ForeColor = textColor ?? Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            int bodyH = multi ? MultiHeight(t.Text) : _fieldH;
            int rowTop = _y;
            if (narrow)
            {
                AddLabel(label, rowTop, _fieldH, labelColor);
                t.SetBounds(PageLeft, rowTop + _fieldH + Px(2), FieldW, bodyH);
                _y = rowTop + _fieldH + Px(2) + bodyH + RowGap;
            }
            else
            {
                AddLabel(label, rowTop, _fieldH, labelColor);
                t.SetBounds(FieldX, rowTop, FieldW, bodyH);
                _y = rowTop + bodyH + RowGap;
            }
            _panel.Controls.Add(t);
            if (multi) t.TextChanged += delegate { AdjustTextHeight(t); };
            return t;
        }

        void AdjustTextHeight(TextBox t, bool force = false)
        {
            if (_rebuilding && !force) return;
            if (t == null || !t.Multiline) return;
            int newH = t.IsHandleCreated ? ExactTextHeight(t) : MultiHeight(t.Text);
            int oldH = t.Height;
            if (newH == oldH) return;
            int oldBottom = t.Top + oldH;
            int delta = newH - oldH;
            _panel.SuspendLayout();
            try
            {
                t.Height = newH;
                foreach (Control c in _panel.Controls)
                {
                    if (c == t) continue;
                    if (c.Top >= oldBottom) c.Top += delta;
                }
                foreach (var d in _decor)
                {
                    if (d.Rect.Top >= oldBottom)
                        d.Rect = new Rectangle(d.Rect.X, d.Rect.Y + delta, d.Rect.Width, d.Rect.Height);
                    else if (d.Rect.Bottom >= oldBottom)
                        d.Rect = new Rectangle(d.Rect.X, d.Rect.Y, d.Rect.Width, Math.Max(1, d.Rect.Height + delta));
                }
                _y += delta;
            }
            finally { _panel.ResumeLayout(true); }
            _panel.Invalidate(true);
        }

        void NormalizeTextHeights()
        {
            var boxes = new List<TextBox>();
            foreach (Control c in _panel.Controls)
            {
                var tb = c as TextBox;
                if (tb != null && tb.Multiline) boxes.Add(tb);
            }
            boxes.Sort(delegate (TextBox a, TextBox b) { return a.Top.CompareTo(b.Top); });
            foreach (var tb in boxes) AdjustTextHeight(tb, true);
        }

        void AddText(Dictionary<string, object> d, string key, string label)
        {
            string val = d != null && d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
            var t = AddRaw(label, val, false, 24);
            _savers.Add(delegate { if (d != null) d[key] = t.Text; });
        }

        void AddRestTag(Dictionary<string, object> d)
        {
            int rowTop = _y;
            var cb = new CheckBox();
            cb.Text = "休息處";
            cb.ForeColor = Color.FromArgb(225, 225, 230);
            string tags = A(d, "tags");
            cb.Checked = tags.IndexOf("rest", StringComparison.Ordinal) >= 0;
            if (Narrow)
            {
                AddLabel("事件標籤", rowTop, _fieldH, null);
                cb.SetBounds(PageLeft, rowTop + _fieldH + Px(2), FieldW, _fieldH);
                _y = rowTop + _fieldH * 2 + Px(2) + RowGap;
            }
            else
            {
                AddLabel("事件標籤", rowTop, _fieldH, null);
                cb.SetBounds(FieldX, rowTop, Math.Min(FieldW, Px(180)), _fieldH);
                _y = rowTop + _fieldH + RowGap;
            }
            _panel.Controls.Add(cb);
            _savers.Add(delegate
            {
                var arr = new System.Collections.ArrayList();
                var old = d.ContainsKey("tags") ? d["tags"] as System.Collections.ArrayList : null;
                if (old != null) foreach (var x in old) { string s = Convert.ToString(x); if (s != "" && s != "rest") arr.Add(s); }
                if (cb.Checked) arr.Add("rest");
                d["tags"] = arr;
            });
        }

        static bool IsConditionOption(Dictionary<string, object> od)
        {
            if (od == null) return false;
            if (S(od, "conditionText") != "") return true;
            var req = od.ContainsKey("requirement") ? od["requirement"] as Dictionary<string, object> : null;
            if (req != null && req.Count > 0) return true;
            string reason = S(od, "specialReason");
            if (reason != "" && reason != "event") return true;
            string text = S(od, "text").TrimStart();
            return text.StartsWith("★") || text.StartsWith("☆");
        }

        static string ChoiceKey(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s ?? "", @"[\s　★☆※]", "");
        }

        static string StatusSummary(System.Collections.ArrayList effs)
        {
            if (effs == null) return "";
            var parts = new List<string>();
            foreach (var x in effs)
            {
                var ed = x as Dictionary<string, object>;
                if (ed == null) continue;
                string kind = S(ed, "kind");
                string cat = "";
                if (kind.IndexOf("体力", StringComparison.Ordinal) >= 0 || kind.IndexOf("體力", StringComparison.Ordinal) >= 0
                    || kind.IndexOf("HP", StringComparison.OrdinalIgnoreCase) >= 0 || kind.IndexOf("生命", StringComparison.Ordinal) >= 0
                    || kind.IndexOf("耐久", StringComparison.Ordinal) >= 0) cat = "体力";
                else if (kind.IndexOf("理智", StringComparison.Ordinal) >= 0 || kind.IndexOf("SAN", StringComparison.OrdinalIgnoreCase) >= 0
                    || kind.IndexOf("算力", StringComparison.Ordinal) >= 0) cat = "理智";
                else if (kind.IndexOf("饥饿", StringComparison.Ordinal) >= 0 || kind.IndexOf("飢餓", StringComparison.Ordinal) >= 0) cat = "饥饿";
                else if (kind.IndexOf("口渴", StringComparison.Ordinal) >= 0) cat = "口渴";
                if (cat == "") continue;
                double amt = 0;
                double.TryParse(S(ed, "amount"), out amt);
                string tag = cat + (amt < 0 ? "损失" : "恢复");
                if (!parts.Contains(tag)) parts.Add(tag);
            }
            return string.Join("、", parts.ToArray());
        }

        void AddBranchFields(Dictionary<string, object> st)
        {
            var ck = st.ContainsKey("check") ? st["check"] as Dictionary<string, object> : null;
            if (ck != null)
            {
                var outs = new List<string> { "成功", "失敗" };
                string cur = S(ck, "outcome");
                if (cur != "" && !outs.Contains(cur)) outs.Add(cur);
                AddChoice(ck, "outcome", "判定結果", outs.ToArray(), outs.ToArray());
                if (S(ck, "sameChoiceBonus") != "" && S(ck, "sameChoiceBonus") != "0")
                    AddFixed("同選加成", "+" + S(ck, "sameChoiceBonus"));
            }
            AddResults(st);
            var eff = st.ContainsKey("effects") ? st["effects"] as System.Collections.ArrayList : null;
            string summary = StatusSummary(eff);
            if (summary != "" && S(st, "status") == "") st["status"] = summary;
            if (S(st, "status") != "") AddText(st, "status", "狀態影響");
            if (S(st, "coins") != "") AddText(st, "coins", "獲得硬幣");
            if (S(st, "items") != "") AddText(st, "items", "獲得物品");
            var pg = st.ContainsKey("progress") ? st["progress"] as Dictionary<string, object> : null;
            if (pg != null && S(pg, "delta") != "") AddText(pg, "delta", "出口進度");
            if (eff != null)
            {
                for (int ei = 0; ei < eff.Count; ei++)
                {
                    var ed = eff[ei] as Dictionary<string, object>;
                    if (ed == null) continue;
                    string kind = S(ed, "kind");
                    if (kind.IndexOf("体力", StringComparison.Ordinal) >= 0 || kind.IndexOf("體力", StringComparison.Ordinal) >= 0
                        || kind.IndexOf("理智", StringComparison.Ordinal) >= 0 || kind.IndexOf("饥饿", StringComparison.Ordinal) >= 0
                        || kind.IndexOf("飢餓", StringComparison.Ordinal) >= 0 || kind.IndexOf("口渴", StringComparison.Ordinal) >= 0
                        || kind.IndexOf("算力", StringComparison.Ordinal) >= 0 || kind.IndexOf("耐久", StringComparison.Ordinal) >= 0) continue;
                    if (ed.ContainsKey("text")) AddText(ed, "text", "影響 " + (ei + 1));
                }
            }
            AddOutcomeDirection(st);
        }

        void AddOutcomeDirection(Dictionary<string, object> st)
        {
            var cb = st.ContainsKey("combat") ? st["combat"] as Dictionary<string, object> : null;
            string enemy = cb != null ? S(cb, "enemy") : S(st, "combat");
            if (enemy != "")
            {
                if (cb != null) AddText(cb, "enemy", "戰鬥實體");
                else
                {
                    var eBox = AddRaw("戰鬥實體", enemy, false, 24);
                    _savers.Add(delegate { st["combat"] = eBox.Text.Trim(); });
                }
            }
            string floor = S(st, "nextFloor");
            if (floor != "") AddText(st, "nextFloor", "目標樓層");
            else if (S(st, "leadsTo") != "") AddText(st, "leadsTo", "目標樓層");
        }

        void AddResults(Dictionary<string, object> st)
        {
            var raw = st.ContainsKey("results") ? st["results"] as System.Collections.ArrayList : null;
            /* 把「被折行的一段話」併回一筆；有變動就讓下一次存檔寫回去。
               本來就沒有 results 的記錄不要因此被塞一個空陣列。 */
            var old = Program.NormalizeResults(raw);
            if (raw != null && raw.Count != old.Count)
            {
                st["results"] = old;
                _normalized = true;
            }
            int n = old.Count;
            if (n == 0)
            {
                var t0 = AddRaw("選擇結果", "", true, 24);
                _savers.Add(delegate
                {
                    var arr = new System.Collections.ArrayList();
                    if (t0.Text.Trim() != "") arr.Add(t0.Text.Trim());
                    st["results"] = arr;
                });
                return;
            }
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                string label = n == 1 ? "選擇結果" : "選擇結果 " + (i + 1);
                var t = AddRaw(label, Convert.ToString(old[i]), true, 24);
                _savers.Add(delegate
                {
                    var arr = st.ContainsKey("results") ? st["results"] as System.Collections.ArrayList : null;
                    if (arr == null)
                    {
                        arr = new System.Collections.ArrayList();
                        foreach (var x in old) arr.Add(Convert.ToString(x));
                        st["results"] = arr;
                    }
                    while (arr.Count <= idx) arr.Add("");
                    arr[idx] = t.Text.Trim();
                });
            }
        }

        /* 選項層級的標記：勾了就表示「選這個不會讓事件通過」（附加選項）。
           放在「選項 N 結果」區塊內，格式是「☐ 附加選項（描述）」。 */
        void AddSideOptionRow(Dictionary<string, object> od)
        {
            int rowTop = _y;
            bool on;
            try { on = string.Equals(S(od, "sideOption"), "true", StringComparison.OrdinalIgnoreCase); }
            catch { on = false; }
            int x = Narrow ? PageLeft : FieldX;
            int cbW = Math.Min(Math.Max(Px(90), TextW("附加選項", BasePt) + Px(28)), Math.Max(Px(90), FieldRight - x));
            var cb = new CheckBox();
            cb.Text = "附加選項";
            cb.ForeColor = Color.FromArgb(228, 228, 234);
            cb.Checked = on;
            cb.SetBounds(x, rowTop, cbW, _fieldH);
            _panel.Controls.Add(cb);
            var hint = new Label();
            hint.Text = "（選擇不會導致通過該事件）";
            hint.ForeColor = Color.FromArgb(146, 146, 154);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.AutoEllipsis = true;
            hint.Cursor = Cursors.Hand;
            hint.Click += delegate { cb.Checked = !cb.Checked; };
            int hintX = x + cbW + Px(2);
            hint.SetBounds(hintX, rowTop, Math.Max(Px(40), FieldRight - hintX), _fieldH);
            _panel.Controls.Add(hint);
            _y = rowTop + _fieldH + RowGap;
            _savers.Add(delegate
            {
                if (cb.Checked) od["sideOption"] = true;
                else od.Remove("sideOption");
            });
        }

        /* 遊戲裡的選項顏色有深有淺（已看過的會變暗），直接拿來當文字色會看不到；
           保持色相、把亮度抬到可讀範圍。 */
        static Color ReadableOptionColor(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            if (max >= 150) return c;
            if (max < 24) return Color.FromArgb(226, 226, 232);
            double k = 185.0 / max;
            return Color.FromArgb(
                Math.Min(255, (int)Math.Round(c.R * k)),
                Math.Min(255, (int)Math.Round(c.G * k)),
                Math.Min(255, (int)Math.Round(c.B * k)));
        }

        void AddOptionText(Dictionary<string, object> d)
        {
            string raw = S(d, "text");
            bool cond = IsConditionOption(d);
            string val = Program.CleanOptionText(raw);
            if (val != raw)
            {
                d["text"] = val;
                if (!d.ContainsKey("special")) d["special"] = true;
                _dirty = true;
            }
            var t = AddRaw(cond ? "⭐條件選項" : "選項", val, false, 24,
                cond ? Color.FromArgb(255, 196, 92) : (Color?)null,
                cond ? Color.FromArgb(255, 226, 150) : (Color?)null);
            string col = S(d, "color");
            if (!cond && col != "")
            {
                try
                {
                    var c = ColorTranslator.FromHtml(col);
                    if (c != Color.Empty) t.ForeColor = ReadableOptionColor(c);
                }
                catch { }
            }
            _savers.Add(delegate { if (d != null) d["text"] = Program.CleanOptionText(t.Text); });
        }

        static readonly string[] CondTypes = { "item", "attribute", "character", "status", "skill", "event" };
        static readonly string[] CondLabels = { "物品", "屬性", "角色", "狀態", "技能", "事件" };

        static string InferConditionType(Dictionary<string, object> od)
        {
            var req = od.ContainsKey("requirement") ? od["requirement"] as Dictionary<string, object> : null;
            string t = req != null ? S(req, "type") : "";
            if (t == "attr") t = "attribute";
            if (Array.IndexOf(CondTypes, t) >= 0) return t;
            string reason = S(od, "specialReason");
            if (reason == "attr") reason = "attribute";
            if (Array.IndexOf(CondTypes, reason) >= 0) return reason;
            if (S(od, "check") != "") return "attribute";
            return "event";
        }

        static string ConditionRequirement(Dictionary<string, object> od)
        {
            string v = S(od, "conditionText");
            if (v != "") return v;
            var req = od.ContainsKey("requirement") ? od["requirement"] as Dictionary<string, object> : null;
            if (req != null && S(req, "name") != "") return S(req, "name");
            return S(od, "check");
        }

        void AddConditionRow(Dictionary<string, object> od)
        {
            int rowTop = _y;
            bool narrow = Narrow;
            string type = InferConditionType(od);
            int idx = Array.IndexOf(CondTypes, type);
            if (idx < 0) idx = 0;
            int cw = Math.Min(Math.Max(Px(76), ComboWidth(CondLabels)), FieldW / 2);
            int fx = narrow ? PageLeft : FieldX;
            int cy = narrow ? rowTop + _fieldH + Px(2) : rowTop;
            AddLabel("條件", rowTop, narrow ? _fieldH : _comboH, Color.FromArgb(255, 196, 92));
            var cb = AddComboCore(fx, cy, cw, CondLabels, idx, Color.FromArgb(255, 226, 150));
            int bx = fx + cw + Px(6);
            int bw = Math.Max(Px(80), FieldRight - bx);
            var box = new TextBox();
            box.SetBounds(bx, cy + Math.Max(0, (_comboH - _fieldH) / 2), bw, _fieldH);
            box.BackColor = Color.FromArgb(40, 40, 44);
            box.ForeColor = Color.FromArgb(255, 226, 150);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = ConditionRequirement(od);
            _panel.Controls.Add(box);
            _y = cy + _comboH + RowGap;

            _savers.Add(delegate
            {
                string t = cb.SelectedIndex >= 0 && cb.SelectedIndex < CondTypes.Length ? CondTypes[cb.SelectedIndex] : "event";
                string name = box.Text.Trim();
                var req = od.ContainsKey("requirement") ? od["requirement"] as Dictionary<string, object> : null;
                if (req == null) { req = new Dictionary<string, object>(); od["requirement"] = req; }
                req["type"] = t;
                req["name"] = name;
                od["special"] = true;
                od["specialReason"] = t;
                if (name != "") od["conditionText"] = name;
                else od.Remove("conditionText");
            });
        }

        void AddChoice(Dictionary<string, object> d, string key, string label, string[] values, string[] texts)
        {
            int rowTop = _y;
            bool narrow = Narrow;
            int bh = _buttonH;
            string current = S(d, key);
            int selected = Array.IndexOf(values, current);
            var buttons = new List<Button>();
            int fieldX = narrow ? PageLeft : FieldX;
            int maxX = FieldRight;
            int x = fieldX;
            int y = narrow ? rowTop + _fieldH + Px(2) : rowTop;
            for (int i = 0; i < values.Length; i++)
            {
                int idx = i;
                int tw = TextW(texts[i], BasePt);
                int bw = Math.Max(Px(44), Math.Min(maxX - fieldX, tw + Px(20)));
                if (x > fieldX && x + bw > maxX)
                {
                    x = fieldX;
                    y += bh + Px(4);
                }
                var b = new Button();
                b.Text = texts[i];
                b.SetBounds(x, y, bw, bh);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(44, 44, 47);
                b.ForeColor = Color.FromArgb(232, 232, 236);
                b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
                b.Click += delegate
                {
                    selected = idx;
                    for (int j = 0; j < buttons.Count; j++)
                    {
                        bool on = j == selected;
                        buttons[j].BackColor = on ? Color.FromArgb(42, 72, 48) : Color.FromArgb(44, 44, 47);
                        buttons[j].ForeColor = on ? Color.FromArgb(190, 240, 190) : Color.FromArgb(232, 232, 236);
                        buttons[j].FlatAppearance.BorderColor = on ? Color.FromArgb(80, 160, 90) : Color.FromArgb(78, 78, 84);
                    }
                };
                if (i == selected)
                {
                    b.BackColor = Color.FromArgb(42, 72, 48);
                    b.ForeColor = Color.FromArgb(190, 240, 190);
                    b.FlatAppearance.BorderColor = Color.FromArgb(80, 160, 90);
                }
                _panel.Controls.Add(b);
                buttons.Add(b);
                x += bw + Px(4);
            }
            AddLabel(label, rowTop, narrow ? _fieldH : bh, null);
            _savers.Add(delegate { if (d != null && selected >= 0) d[key] = values[selected]; });
            _y = y + bh + RowGap;
        }

        /* 寬度 = 文字 + 箭頭 16 + 左右內縮，一次算好；圖上不再用「乘以字級」的舊算法。 */
        int ComboWidth(string[] texts)
        {
            int max = 0;
            foreach (var s in texts)
            {
                int w = TextW(s, BasePt) + Px(40);
                if (w > max) max = w;
            }
            return Math.Max(Px(72), max);
        }

        DarkComboBox AddComboCore(int x, int y, int width, string[] labels, int selected, Color fore)
        {
            var cb = new DarkComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            UiText.StyleCombo(cb);
            UiText.SetComboItems(cb, labels);
            cb.SetBounds(x, y, width, _comboH);
            if (selected >= 0 && selected < cb.Items.Count) cb.SelectedIndex = selected;
            cb.ForeColor = fore;
            _panel.Controls.Add(cb);
            return cb;
        }

        void AddComboField(Dictionary<string, object> d, string key, string label, string[] values, string[] texts)
        {
            int rowTop = _y;
            bool narrow = Narrow;
            int idx = Array.IndexOf(values, S(d, key));
            if (idx < 0) idx = 0;
            int fx = narrow ? PageLeft : FieldX;
            int cy = narrow ? rowTop + _fieldH + Px(2) : rowTop;
            int cw = Math.Min(FieldRight - fx, ComboWidth(texts));
            var cb = AddComboCore(fx, cy, cw, texts, idx, Color.White);
            cb.SelectedIndexChanged += delegate
            {
                if (cb.SelectedIndex >= 0 && cb.SelectedIndex < values.Length) d[key] = values[cb.SelectedIndex];
            };
            AddLabel(label, rowTop, narrow ? _fieldH : _comboH, null);
            _y = cy + _comboH + RowGap;
        }

        void AddFloorFields(Dictionary<string, object> o)
        {
            string ft = S(o, "floorType");
            if (ft != "normal" && ft != "anomalous")
                ft = Parse.IsNormalFloorId(S(o, "floorId")) ? "normal" : "anomalous";
            o["floorType"] = ft;
            bool normal = ft == "normal";
            bool narrow = Narrow;
            int bh = _buttonH;

            int typeTop = _y;
            AddLabel("類型", typeTop, narrow ? _fieldH : bh, null);
            int typeY = narrow ? typeTop + _fieldH + Px(2) : typeTop;
            var normalBtn = new Button();
            var anomalousBtn = new Button();
            int tbw = Math.Max(Px(88), TextW("常規樓層", BasePt) + Px(24));
            normalBtn.Text = "常規樓層";
            normalBtn.SetBounds(narrow ? PageLeft : FieldX, typeY, tbw, bh);
            normalBtn.FlatStyle = FlatStyle.Flat;
            normalBtn.ForeColor = Color.White;
            _panel.Controls.Add(normalBtn);
            anomalousBtn.Text = "異常樓層";
            anomalousBtn.SetBounds((narrow ? PageLeft : FieldX) + tbw + Px(6), typeY, tbw, bh);
            anomalousBtn.FlatStyle = FlatStyle.Flat;
            anomalousBtn.ForeColor = Color.White;
            _panel.Controls.Add(anomalousBtn);
            Action refreshType = delegate
            {
                normalBtn.BackColor = normal ? Color.FromArgb(66, 70, 78) : Color.FromArgb(44, 44, 47);
                anomalousBtn.BackColor = normal ? Color.FromArgb(44, 44, 47) : Color.FromArgb(66, 70, 78);
                normalBtn.FlatAppearance.BorderColor = normal ? Color.FromArgb(130, 170, 215) : Color.FromArgb(78, 78, 84);
                anomalousBtn.FlatAppearance.BorderColor = normal ? Color.FromArgb(78, 78, 84) : Color.FromArgb(130, 170, 215);
            };
            normalBtn.Click += delegate { normal = true; o["floorType"] = "normal"; refreshType(); };
            anomalousBtn.Click += delegate { normal = false; o["floorType"] = "anomalous"; refreshType(); };
            refreshType();
            _y = typeY + bh + RowGap;

            int rowY = _y;
            AddLabel("樓層", rowY, _fieldH, null);
            var prefix = new Label();
            prefix.Text = "Level";
            prefix.ForeColor = Color.FromArgb(220, 220, 226);
            prefix.TextAlign = ContentAlignment.MiddleLeft;
            prefix.Visible = normal;
            _panel.Controls.Add(prefix);
            var idBox = new TextBox();
            idBox.BackColor = Color.FromArgb(40, 40, 44);
            idBox.ForeColor = Color.White;
            idBox.BorderStyle = BorderStyle.FixedSingle;
            idBox.Text = S(o, "floorId");
            _panel.Controls.Add(idBox);
            var nameBox = new TextBox();
            nameBox.BackColor = Color.FromArgb(40, 40, 44);
            nameBox.ForeColor = Color.White;
            nameBox.BorderStyle = BorderStyle.FixedSingle;
            nameBox.Text = S(o, "floorName");
            _panel.Controls.Add(nameBox);

            Action layout = delegate
            {
                int idw = normal ? Px(80) : Px(110);
                if (narrow)
                {
                    int y1 = rowY + _fieldH + Px(2);
                    prefix.SetBounds(PageLeft, y1, Px(54), _fieldH);
                    idBox.SetBounds(PageLeft + (normal ? Px(56) : 0), y1, normal ? Px(90) : Px(110), _fieldH);
                    nameBox.SetBounds(PageLeft, y1 + _fieldH + Px(2), FieldW, _fieldH);
                }
                else
                {
                    int x = FieldX;
                    if (normal) { prefix.SetBounds(x, rowY, Px(54), _fieldH); x += Px(56); }
                    idBox.SetBounds(x, rowY, idw, _fieldH);
                    int nx = x + idw + Px(6);
                    nameBox.SetBounds(nx, rowY, Math.Max(Px(120), FieldRight - nx), _fieldH);
                }
            };
            idBox.KeyPress += delegate (object s, KeyPressEventArgs e)
            {
                if (!normal || char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar) || e.KeyChar == '.') return;
                e.Handled = true;
            };
            _savers.Add(delegate
            {
                o["floorType"] = normal ? "normal" : "anomalous";
                o["floorId"] = idBox.Text.Trim();
                o["floorName"] = nameBox.Text.Trim();
            });
            layout();
            _y = rowY + (narrow ? (_fieldH + Px(2)) * 2 + _fieldH : _fieldH) + RowGap;
        }

        void AddCompactFloorRow(Dictionary<string, object> o, Dictionary<string, object> mirror = null)
        {
            int rowY = _y;
            bool narrow = Narrow;
            bool normal = Parse.IsNormalFloorId(S(o, "floorId"));
            AddLabel("樓層", rowY, _fieldH, null);
            var prefix = new Label();
            prefix.Text = "Level";
            prefix.ForeColor = Color.FromArgb(220, 220, 226);
            prefix.TextAlign = ContentAlignment.MiddleLeft;
            prefix.Visible = normal;
            _panel.Controls.Add(prefix);
            var idBox = new TextBox();
            idBox.BackColor = Color.FromArgb(40, 40, 44);
            idBox.ForeColor = Color.White;
            idBox.BorderStyle = BorderStyle.FixedSingle;
            idBox.Text = S(o, "floorId");
            _panel.Controls.Add(idBox);
            var nameBox = new TextBox();
            nameBox.BackColor = Color.FromArgb(40, 40, 44);
            nameBox.ForeColor = Color.White;
            nameBox.BorderStyle = BorderStyle.FixedSingle;
            nameBox.Text = S(o, "floorName");
            _panel.Controls.Add(nameBox);
            if (narrow)
            {
                int y1 = rowY + _fieldH + Px(2);
                prefix.SetBounds(PageLeft, y1, Px(54), _fieldH);
                idBox.SetBounds(PageLeft + (normal ? Px(56) : 0), y1, normal ? Px(90) : Px(110), _fieldH);
                nameBox.SetBounds(PageLeft, y1 + _fieldH + Px(2), FieldW, _fieldH);
                _y = y1 + (_fieldH + Px(2)) + _fieldH + RowGap;
            }
            else
            {
                int x = FieldX;
                if (normal) { prefix.SetBounds(x, rowY, Px(54), _fieldH); x += Px(56); }
                int idw = normal ? Px(80) : Px(110);
                idBox.SetBounds(x, rowY, idw, _fieldH);
                int nx = x + idw + Px(6);
                nameBox.SetBounds(nx, rowY, Math.Max(Px(120), FieldRight - nx), _fieldH);
                _y = rowY + _fieldH + RowGap;
            }
            _savers.Add(delegate
            {
                string fid = idBox.Text.Trim();
                string fname = nameBox.Text.Trim();
                o["floorId"] = fid;
                o["floorName"] = fname;
                if (mirror != null && mirror != o)
                {
                    mirror["floorId"] = fid;
                    mirror["floorName"] = fname;
                }
            });
        }

        void AddThemeButtons(Dictionary<string, object> o)
        {
            int rowTop = _y;
            bool narrow = Narrow;
            int bh = _buttonH;
            AddLabel("主題", rowTop, narrow ? _fieldH : bh, null);
            int by = narrow ? rowTop + _fieldH + Px(2) : rowTop;
            int bw = Math.Max(Px(84), TextW("昏暗", BasePt) + Px(30));
            int bx = narrow ? PageLeft : FieldX;
            var light = new Button();
            light.Text = "明亮";
            light.SetBounds(bx, by, bw, bh);
            light.FlatStyle = FlatStyle.Flat;
            _panel.Controls.Add(light);
            var dark = new Button();
            dark.Text = "昏暗";
            dark.SetBounds(bx + bw + Px(6), by, bw, bh);
            dark.FlatStyle = FlatStyle.Flat;
            _panel.Controls.Add(dark);
            Action refresh = delegate
            {
                string th = S(o, "theme");
                light.BackColor = th == "light" ? Color.FromArgb(66, 70, 78) : Color.FromArgb(44, 44, 47);
                dark.BackColor = th == "dark" ? Color.FromArgb(66, 70, 78) : Color.FromArgb(44, 44, 47);
                light.ForeColor = Color.White;
                dark.ForeColor = Color.White;
                light.FlatAppearance.BorderColor = th == "light" ? Color.FromArgb(130, 170, 215) : Color.FromArgb(78, 78, 84);
                dark.FlatAppearance.BorderColor = th == "dark" ? Color.FromArgb(130, 170, 215) : Color.FromArgb(78, 78, 84);
            };
            light.Click += delegate { o["theme"] = "light"; refresh(); };
            dark.Click += delegate { o["theme"] = "dark"; refresh(); };
            _savers.Add(delegate { if (S(o, "theme") != "light" && S(o, "theme") != "dark") o["theme"] = "dark"; });
            refresh();
            _y = by + bh + RowGap;
        }

        void AddPalettePreview(Dictionary<string, object> o)
        {
            AddHeading("配色預覽");
            var pal = o.ContainsKey("palette") ? o["palette"] as Dictionary<string, object> : null;
            string theme = S(o, "theme");
            int h = Px(96);
            var panel = new BufferedPanel();
            panel.SetBounds(PageLeft, _y, Math.Max(Px(120), FieldRight - PageLeft), h);
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Paint += delegate (object s, PaintEventArgs e)
            {
                Color bg = PaletteColor(pal, "bg", theme == "light" ? Color.FromArgb(244, 240, 230) : Color.FromArgb(28, 28, 32));
                Color title = PaletteColor(pal, "title", theme == "light" ? Color.FromArgb(92, 82, 58) : Color.FromArgb(255, 214, 120));
                Color text = PaletteColor(pal, "text", theme == "light" ? Color.FromArgb(52, 50, 46) : Color.FromArgb(230, 230, 235));
                e.Graphics.Clear(bg);
                float fs = 10F * UiScale.Scale;
                using (var font = UiText.MakeFont(fs, FontStyle.Regular))
                using (var titleBrush = new SolidBrush(title))
                using (var textBrush = new SolidBrush(text))
                {
                    e.Graphics.DrawString("範例標題", font, titleBrush, Px(12), Px(10));
                    e.Graphics.DrawString("這是樓層正文的範例文字。", font, textBrush, Px(12), Px(46));
                }
            };
            _panel.Controls.Add(panel);
            _y += h + RowGap + Px(4);
        }

        static Color PaletteColor(Dictionary<string, object> pal, string key, Color fallback)
        {
            try
            {
                if (pal == null || !pal.ContainsKey(key)) return fallback;
                var c = ColorTranslator.FromHtml(Convert.ToString(pal[key]));
                return c == Color.Empty ? fallback : c;
            }
            catch { return fallback; }
        }

        void AddLongText(Dictionary<string, object> d, string key, string label)
        {
            string val = d != null && d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
            var t = AddRaw(label, val, true, 86);
            _savers.Add(delegate { if (d != null) d[key] = t.Text; });
        }

        void AddList(Dictionary<string, object> d, string key, string label, bool dictItems)
        {
            var old = d != null && d.ContainsKey(key) ? d[key] as System.Collections.ArrayList : null;
            var vals = new List<string>();
            if (old != null)
            {
                foreach (var x in old)
                {
                    var xd = x as Dictionary<string, object>;
                    vals.Add(xd != null ? Convert.ToString(xd.ContainsKey("text") ? xd["text"] : "") : Convert.ToString(x));
                }
            }
            var t = AddRaw(label, string.Join("\r\n", vals.ToArray()), true, 90);
            var tip = new ToolTip();
            tip.SetToolTip(t, "每行一條");
            _savers.Add(delegate
            {
                var arr = new System.Collections.ArrayList();
                int idx = 0;
                foreach (var line in t.Lines)
                {
                    string v = (line ?? "").Trim();
                    if (v == "") continue;
                    if (dictItems)
                    {
                        Dictionary<string, object> od = null;
                        if (old != null && idx < old.Count) od = old[idx] as Dictionary<string, object>;
                        if (od == null) od = new Dictionary<string, object>();
                        od["text"] = v;
                        arr.Add(od);
                    }
                    else arr.Add(v);
                    idx++;
                }
                if (d != null) d[key] = arr;
            });
        }

        void BuildFields()
        {
            _decor.Clear();
            _y = Px(14);
            var o = _data;
            if (_kind == "事件")
            {
                var steps = o.ContainsKey("steps") ? o["steps"] as System.Collections.ArrayList : null;
                Dictionary<string, object> s1 = steps != null && steps.Count > 0 ? steps[0] as Dictionary<string, object> : o;
                string eventKind = S(o, "eventKind");
                if (eventKind == "") { eventKind = "normal"; o["eventKind"] = eventKind; }
                AddComboField(o, "eventKind", "事件類型",
                    new[] { "normal", "free", "junction", "character", "cooperation", "common" },
                    new[] { "普通事件", "自由事件", "岔路事件", "角色事件", "合力事件", "通用事件" });
                AddRestTag(o);
                string title = s1 != null && s1.ContainsKey("title") ? Convert.ToString(s1["title"]) : EventTitleOf(o);
                var tt = AddRaw("事件名", title, false, 24);
                _savers.Add(delegate
                {
                    string v = tt.Text.Trim();
                    if (s1 != null) s1["title"] = v;
                    o["title"] = v;
                });
                AddCompactFloorRow(o, s1);
                if (s1 != null && s1.ContainsKey("intro")) AddLongText(s1, "intro", "事件介紹");
                else AddLongText(o, "intro", "事件介紹");

                var branches = new List<Dictionary<string, object>>();
                if (steps != null)
                {
                    for (int i = 1; i < steps.Count; i++)
                    {
                        var st = steps[i] as Dictionary<string, object>;
                        if (st == null) continue;
                        var nested = st.ContainsKey("branches") ? st["branches"] as System.Collections.ArrayList : null;
                        if (nested != null)
                        {
                            foreach (var n in nested) { var nd = n as Dictionary<string, object>; if (nd != null) branches.Add(nd); }
                        }
                        else branches.Add(st);
                    }
                }
                else if (o.ContainsKey("choice")) branches.Add(o);

                Program.MoveChecksToOptions(o);
                var opts = s1 != null && s1.ContainsKey("options") ? s1["options"] as System.Collections.ArrayList : null;
                var usedBranches = new HashSet<Dictionary<string, object>>();
                if (opts != null && opts.Count > 0)
                {
                    for (int oi = 0; oi < opts.Count; oi++)
                    {
                        var od = opts[oi] as Dictionary<string, object>;
                        if (od == null) continue;
                        int optTop = _y;
                        string optName = "選項 " + (oi + 1);
                        var optFrame = AddOptionFrame(optTop, optName);
                        AddOptionText(od);
                        if (od.ContainsKey("desc")) AddLongText(od, "desc", "選項說明");
                        bool condOpt = IsConditionOption(od);
                        string condType = condOpt ? InferConditionType(od) : "";
                        if (condOpt) AddConditionRow(od);
                        if ((!condOpt && od.ContainsKey("check")) || (condOpt && condType == "attribute"))
                        {
                            if (condOpt && !od.ContainsKey("check"))
                            {
                                string inferred = ConditionRequirement(od);
                                if (inferred != "") od["check"] = inferred;
                            }
                            if (od.ContainsKey("check"))
                                AddChoice(od, "check", "屬性檢定", new[] { "力量", "敏捷", "體質", "感知", "智力", "魅力" }, new[] { "力量", "敏捷", "體質", "感知", "智力", "魅力" });
                        }
                        string optText = Convert.ToString(od["text"]);
                        Dictionary<string, object> branch = null;
                        foreach (var st in branches)
                        {
                            if (ChoiceKey(Convert.ToString(st.ContainsKey("choice") ? st["choice"] : "")) == ChoiceKey(optText))
                            { branch = st; break; }
                        }
                        if (branch == null && branches.Count == opts.Count) branch = branches[oi];
                        if (branch != null)
                        {
                            usedBranches.Add(branch);
                            int resTop = _y;
                            AddDivider(resTop, optName + " 結果");
                            AddSideOptionRow(od);
                            AddBranchFields(branch);
                            _y += Px(10);
                        }
                        else
                        {
                            /* 沒有結果段時仍要能勾，放在選項框內。 */
                            AddSideOptionRow(od);
                        }
                        CloseDecor(optFrame, _y);
                        _y += Px(12);
                    }
                }
                else
                {
                    AddList(s1 ?? o, "options", "選項", true);
                }
                /* 未掛到任何選項的結果，仍單獨顯示，不靠選項文字硬匹配。 */
                int extra = 0;
                foreach (var st in branches)
                {
                    if (usedBranches.Contains(st)) continue;
                    string c = Convert.ToString(st.ContainsKey("choice") ? st["choice"] : "");
                    bool matched = false;
                    if (opts != null)
                        foreach (var oo in opts)
                        {
                            var od = oo as Dictionary<string, object>;
                            if (od != null && ChoiceKey(Convert.ToString(od["text"])) == ChoiceKey(c)) { matched = true; break; }
                        }
                    if (matched) continue;
                    extra++;
                    int resTop = _y;
                    var resFrame = AddOptionFrame(resTop, "其他結果 " + extra);
                    AddBranchFields(st);
                    CloseDecor(resFrame, _y);
                    _y += Px(12);
                }
            }
            else if (_kind == "物品")
            {
                if (_lockIdentity)
                {
                    AddFixed("名稱", S(o, "name"));
                    AddFixed("分類", S(o, "type"));
                }
                else
                {
                    AddText(o, "name", "名稱");
                    AddText(o, "type", "類型");
                }
                AddText(o, "source", "來源");
                AddText(o, "price", "參考價格");
                AddText(o, "character", "角色");
                AddLongText(o, "desc", "介紹");
                AddList(o, "eff", "效果", false);
                AddList(o, "cures", "可治療", false);
            }
            else if (_kind == "樓層")
            {
                AddFloorFields(o);
                AddThemeButtons(o);
                AddLongText(o, "intro", "樓層介紹");
                AddPalettePreview(o);
            }
            else if (_kind == "出口")
            {
                if (_lockIdentity)
                {
                    AddFixed("樓層", (S(o, "floorId") + " " + S(o, "floorName")).Trim());
                    AddFixed("出口事件", S(o, "event"));
                }
                else
                {
                    AddText(o, "floorId", "樓層 ID");
                    AddText(o, "floorName", "樓層名稱");
                    AddText(o, "event", "出口事件");
                }
                AddText(o, "target", "通往樓層");
                AddLongText(o, "intro", "事件介紹");
                AddLongText(o, "transition", "通往文本");
            }
            else if (_kind == "結局")
            {
                if (_lockIdentity)
                {
                    AddFixed("結局名稱", S(o, "name"));
                    AddFixed("分類", S(o, "category"));
                }
                else
                {
                    AddText(o, "name", "結局名稱");
                    AddText(o, "category", "分類");
                }
                AddLongText(o, "intro", "通用介紹");
                AddLongText(o, "epilogue", "後日談");
            }
            else if (_kind == "商店")
            {
                if (_lockIdentity)
                {
                    AddFixed("商店", S(o, "title"));
                    AddFixed("樓層", (S(o, "floorId") + " " + S(o, "floorName")).Trim());
                }
                else
                {
                    AddText(o, "title", "商店標題");
                    AddText(o, "floorId", "樓層 ID");
                    AddText(o, "floorName", "樓層名稱");
                }
                AddLongText(o, "intro", "商店介紹");
                AddList(o, "items", "商品", false);
            }
            else
            {
                var l = new Label();
                l.Text = "這種記錄尚未提供結構化編輯。";
                l.SetBounds(PageLeft, _y, FieldW, _fieldH);
                l.ForeColor = Color.FromArgb(220, 160, 120);
                _panel.Controls.Add(l);
                _y += _fieldH + RowGap;
            }
            _panel.AutoScrollMinSize = new Size(0, _y + Px(7));
        }

        void QueueRelayout(bool force = false)
        {
            if (_rebuilding || _relayout == null) return;
            if (force) _lastPanelWidth = -1;
            _relayout.Stop();
            _relayout.Start();
        }

        void RelayoutFields()
        {
            if (_rebuilding || _panel == null || _panel.IsDisposed) return;
            int w = _panel.ClientSize.Width;
            if (w <= 0) return;
            if (_lastPanelWidth > 0 && Math.Abs(w - _lastPanelWidth) < 8) return;
            _lastPanelWidth = w;
            _rebuilding = true;
            int scrollY = -_panel.AutoScrollPosition.Y;
            try
            {
                if (_autoSave != null) _autoSave.Stop();
                SaveData(false);
                /* 重建前必須把滾動歸零：AutoScroll 開啟時新增控制項，WinForms 會把
                   目前的滾動量加進 Location；不歸零就會出現「內容整體下移、上方空白」。 */
                _panel.AutoScrollPosition = new Point(0, 0);
                var olds = new List<Control>();
                foreach (Control c in _panel.Controls) olds.Add(c);
                _panel.Controls.Clear();
                foreach (var c in olds) { try { c.Dispose(); } catch { } }
                _savers.Clear();
                BuildFields();
                UiText.ApplyTo(this);
                NormalizeTextHeights();
                HideHorizontalScroll();
                _panel.AutoScrollPosition = new Point(0, scrollY);
                HookAutoSave(_panel);
                _panel.Invalidate(true);
            }
            catch (Exception ex)
            {
                /* 重建失敗不能把整頁吞掉；至少留個紀錄，下一次才查得到。 */
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "exp-relayout-error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " w=" + w + "\r\n" + ex + "\r\n\r\n");
                }
                catch { }
            }
            finally { _rebuilding = false; }
        }

        void HookAutoSave(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var tb = c as TextBox;
                if (tb != null) tb.TextChanged += delegate { ScheduleSave(); };
                var cb = c as CheckBox;
                if (cb != null) cb.CheckedChanged += delegate { ScheduleSave(); };
                var rb = c as RadioButton;
                if (rb != null) rb.CheckedChanged += delegate { ScheduleSave(); };
                var combo = c as ComboBox;
                if (combo != null) combo.SelectedIndexChanged += delegate { ScheduleSave(); };
                var btn = c as Button;
                if (btn != null) btn.Click += delegate { ScheduleSave(); };
                if (c.HasChildren) HookAutoSave(c);
            }
        }

        void ScheduleSave()
        {
            /* 套用翻譯／字型時控制項會回報「變更」，那不是使用者編輯。 */
            if (UiText.Applying) return;
            _dirty = true;
            SetStatus("編輯中…", Color.FromArgb(220, 180, 110));
            if (_autoSave == null) return;
            _autoSave.Stop();
            _autoSave.Start();
        }

        bool SaveData(bool closing)
        {
            if (_saving || !_dirty) return true;
            _saving = true;
            try
            {
                foreach (var save in _savers) save();
                if (_externalSave != null)
                {
                    _externalSave(_data);
                    _dirty = false;
                    SetStatus("已保存 " + DateTime.Now.ToString("HH:mm:ss"), Color.FromArgb(130, 190, 140));
                    return true;
                }
                if (_entry == null) { _dirty = false; return true; }
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(_entry.Data);
                var lines = new List<string>(File.ReadAllLines(_entry.File));
                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] == _entry.Line)
                    {
                        lines[i] = json;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) throw new Exception("找不到原始記錄行，請重新整理後再修改。");
                File.WriteAllLines(_entry.File, lines.ToArray(), new UTF8Encoding(false));
                _entry.Line = json;
                _dirty = false;
                SetStatus("已保存 " + DateTime.Now.ToString("HH:mm:ss"), Color.FromArgb(130, 190, 140));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("保存失敗", Color.FromArgb(220, 120, 110));
                if (closing)
                    MessageBox.Show("保存失敗：" + ex.Message, "已記錄", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    Text = "編輯記錄：" + _kind + "（自動保存失敗）";
                return false;
            }
            finally { _saving = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_autoSave != null) _autoSave.Stop();
            if (_relayout != null) _relayout.Stop();
            if (!SaveData(true)) { e.Cancel = true; return; }
            if (_wheelFilter != null)
            {
                Application.RemoveMessageFilter(_wheelFilter);
                _wheelFilter = null;
            }
            if (_externalSave != null) DialogResult = DialogResult.OK;
            base.OnFormClosing(e);
        }
    }

    class RecordsForm : Form
    {
        public static RecordsForm Instance;
        TextBox _search;
        Label _searchHint;
        Label _typeLabel;
        Panel _splitter;
        Panel _editorHost;
        RecordEditForm _inlineEditor;
        bool _closingInline;
        ListBox _list;
        Label _count;
        ComboBox _kind;
        ToolTip _tip;
        Button _reloadBtn, _delBtn, _openBtn, _closeBtn;
        int _listW;
        int _splitStartX, _splitStartW;
        List<RecordEntry> _all = new List<RecordEntry>();
        List<RecordEntry> _shown = new List<RecordEntry>();

        int Px(int v) { return UiScale.Px(v); }

        /* 編輯區最少留這麼寬（1.0 → 434px，1.6 → 692px）。再窄欄位就開始擁擠，
           而且會比獨立視窗的 MinimumSize 還小；這個值與 RecordsForm 的 MinimumSize 是配套的。 */
        int EditorMinW { get { return UiScale.Px(430) + 4; } }

        int TextW(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            using (var f = new Font(UiText.FontFamily, 9F * UiScale.Scale))
                return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        public RecordsForm()
        {
            Instance = this;
            Text = "已記錄";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = UiScale.FitSize(880, 600);
            MinimumSize = Size.Add(UiScale.FitSize(640, 420), new Size(16, 39));
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 28, 31);
            ForeColor = Color.FromArgb(230, 230, 235);

            _search = new TextBox();
            _search.BackColor = Color.FromArgb(40, 40, 44);
            _search.ForeColor = Color.White;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.TextChanged += delegate { UpdateSearchHint(); ApplyFilter(); };
            _search.Enter += delegate { UpdateSearchHint(); };
            _search.Leave += delegate { UpdateSearchHint(); };
            Controls.Add(_search);

            _searchHint = new Label();
            _searchHint.Text = "搜尋標題…";
            _searchHint.ForeColor = Color.FromArgb(120, 120, 128);
            _searchHint.BackColor = Color.FromArgb(40, 40, 44);
            _searchHint.TextAlign = ContentAlignment.MiddleLeft;
            _searchHint.Cursor = Cursors.IBeam;
            _searchHint.Click += delegate { _search.Focus(); };
            Controls.Add(_searchHint);
            _searchHint.BringToFront();

            _tip = new ToolTip();

            _typeLabel = new Label();
            _typeLabel.Text = "類型";
            _typeLabel.ForeColor = Color.FromArgb(150, 150, 158);
            _typeLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_typeLabel);

            _kind = new DarkComboBox();
            _kind.DropDownStyle = ComboBoxStyle.DropDownList;
            UiText.StyleCombo(_kind);
            UiText.SetComboItems(_kind, "全部", "事件", "物品", "樓層", "出口", "結局", "商店");
            _kind.SelectedIndex = 0;
            _kind.SelectedIndexChanged += delegate { ApplyFilter(); };
            Controls.Add(_kind);

            _list = new ListBox();
            _list.BackColor = Color.FromArgb(36, 36, 40);
            _list.ForeColor = Color.FromArgb(226, 226, 232);
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.None;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = Px(30);
            _list.DrawItem += DrawListItem;
            _list.SelectedIndexChanged += delegate { ShowDetail(); };
            Native.DarkScroll(_list);
            Controls.Add(_list);

            _editorHost = new BufferedPanel();
            _editorHost.BackColor = Color.FromArgb(24, 24, 27);
            _editorHost.AutoScroll = false;
            _editorHost.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(44, 44, 50)))
                    e.Graphics.DrawLine(pen, 0, 0, _editorHost.ClientSize.Width, 0);
            };
            Controls.Add(_editorHost);

            _splitter = new Panel();
            _splitter.BackColor = Color.FromArgb(58, 58, 66);
            _splitter.Cursor = Cursors.VSplit;
            _splitter.MouseDown += delegate
            {
                _splitStartX = Control.MousePosition.X;
                _splitStartW = _listW;
                _splitter.Capture = true;
            };
            _splitter.MouseMove += delegate
            {
                if (!_splitter.Capture) return;
                int dx = Control.MousePosition.X - _splitStartX;
                _listW = Math.Max(Px(160), Math.Min(MaxListWidth(ClientSize.Width), _splitStartW + dx));
                LayoutRecords();
            };
            _splitter.MouseUp += delegate { _splitter.Capture = false; };
            Controls.Add(_splitter);

            _count = new Label();
            _count.ForeColor = Color.FromArgb(160, 160, 168);
            _count.TextAlign = ContentAlignment.MiddleRight;
            _count.AutoEllipsis = true;
            Controls.Add(_count);

            _reloadBtn = MkBtn("重整", 0, 0, 84); _reloadBtn.Click += delegate { LoadAll(); };
            _delBtn = MkBtn("刪除選中", 0, 0, 96); _delBtn.Click += delegate { DeleteSelected(); };
            _openBtn = MkBtn("開啟資料夾", 0, 0, 104); _openBtn.Click += delegate { try { Process.Start("explorer.exe", Program.DataDir()); } catch { } };
            _closeBtn = MkBtn("關閉", 0, 0, 84); _closeBtn.Click += delegate { Close(); };
            Controls.Add(_reloadBtn); Controls.Add(_delBtn); Controls.Add(_openBtn); Controls.Add(_closeBtn);

            UiScale.Apply(this);
            _list.ItemHeight = Px(30);
            LoadAll();
            LayoutRecords();
            Resize += delegate { LayoutRecords(); };
        }

        int ComboItemsWidth(ComboBox cb)
        {
            if (cb == null) return Px(72);
            int max = 0;
            foreach (var item in cb.Items)
                max = Math.Max(max, TextW(Convert.ToString(item)) + Px(40));
            return Math.Max(Px(72), max);
        }

        /* 清單最寬只能到「視窗寬 − 兩側留白 − 分隔 − 編輯器最小寬」，
           否則把分隔條往右拖會把編輯器壓到比內層控制項還窄（右邊被裁掉）。 */
        int MaxListWidth(int w)
        {
            return Math.Max(Px(160), Math.Min(Px(460), w - Px(12) * 2 - Px(8) - EditorMinW));
        }

        void LayoutRecords()
        {
            int pad = Px(12);
            int toolH = Px(28);
            int gap = Px(8);
            int bottomH = Px(52);
            int w = Math.Max(200, ClientSize.Width);
            int h = Math.Max(160, ClientSize.Height);

            int kindW = Math.Min(Px(170), ComboItemsWidth(_kind));
            int typeW = TextW("類型") + Px(10);
            int searchW = Math.Max(Px(90), w - pad * 2 - typeW - kindW - gap * 2);

            _search.SetBounds(pad, pad, searchW, toolH);
            if (_searchHint != null)
            {
                _searchHint.SetBounds(_search.Left + Px(6), _search.Top + Px(2),
                    Math.Max(Px(20), _search.Width - Px(10)), Math.Max(Px(14), _search.Height - Px(4)));
                _searchHint.BringToFront();
                UpdateSearchHint();
            }
            _typeLabel.SetBounds(pad + searchW + gap, pad, typeW, toolH);
            _kind.SetBounds(pad + searchW + gap + typeW, pad + Px(1), kindW, Px(26));

            int contentTop = pad + toolH + gap;
            int contentH = Math.Max(Px(100), h - contentTop - bottomH - pad);
            int minList = Px(160);
            int maxList = MaxListWidth(w);
            if (_listW <= 0) _listW = Math.Max(minList, Math.Min(maxList, w * 28 / 100));
            /* 只夾一個顯示用的值，不回寫 _listW：視窗再放大時，多出來的寬度要留給編輯區。 */
            int listW = Math.Max(minList, Math.Min(maxList, _listW));
            int detailX = pad + listW + Px(8);
            /* 編輯區只吃剩下的寬度；視窗真的太小（低於 MinimumSize 才會發生）就讓它自己變窄，
               絕不超過視窗右緣。 */
            int detailW = Math.Max(Px(200), w - detailX - pad);

            _list.SetBounds(pad, contentTop, listW, contentH);
            _splitter.SetBounds(pad + listW + Px(1), contentTop, Px(6), contentH);
            _splitter.BringToFront();
            _editorHost.SetBounds(detailX, contentTop, detailW, contentH);

            int by = h - pad - Px(30);
            _reloadBtn.SetBounds(pad, by, Px(84), Px(30));
            _delBtn.SetBounds(pad + Px(90), by, Px(96), Px(30));
            _openBtn.SetBounds(pad + Px(192), by, Px(104), Px(30));
            _closeBtn.SetBounds(pad + Px(302), by, Px(84), Px(30));
            _count.SetBounds(pad + Px(392), by, Math.Max(Px(60), w - pad * 2 - Px(392)), Px(30));
        }

        void UpdateSearchHint()
        {
            if (_searchHint == null || _search == null) return;
            bool show = _search.Text.Length == 0 && !_search.Focused;
            if (_searchHint.Visible != show) _searchHint.Visible = show;
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _shown.Count) return;
            var entry = _shown[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var bg = new SolidBrush(sel ? Color.FromArgb(46, 74, 110) : _list.BackColor))
                e.Graphics.FillRectangle(bg, r);
            if (sel)
            {
                using (var pen = new Pen(Color.FromArgb(78, 122, 170)))
                    e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            int padX = Px(8);
            int kindW = TextW("事件") + Px(4);
            int timeW = TextW("00:00") + Px(4);
            using (var f = UiText.MakeFont(9F * UiScale.Scale, FontStyle.Regular))
            {
                var kindRect = new Rectangle(r.X + padX, r.Y, kindW, r.Height);
                TextRenderer.DrawText(e.Graphics, entry.Kind ?? "", f, kindRect, KindColor(entry.Kind),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var timeRect = new Rectangle(r.Right - padX - timeW, r.Y, timeW, r.Height);
                TextRenderer.DrawText(e.Graphics, entry.Time ?? "", f, timeRect,
                    sel ? Color.FromArgb(200, 214, 232) : Color.FromArgb(140, 140, 148),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int tx = kindRect.Right + Px(6);
                int tw = Math.Max(Px(20), timeRect.X - Px(6) - tx);
                TextRenderer.DrawText(e.Graphics, entry.Title ?? "", f, new Rectangle(tx, r.Y, tw, r.Height),
                    sel ? Color.White : Color.FromArgb(226, 226, 232),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        static Color KindColor(string kind)
        {
            if (kind == "事件") return Color.FromArgb(255, 214, 120);
            if (kind == "物品") return Color.FromArgb(150, 200, 220);
            if (kind == "樓層") return Color.FromArgb(150, 180, 235);
            if (kind == "出口") return Color.FromArgb(150, 210, 165);
            if (kind == "結局") return Color.FromArgb(205, 170, 235);
            if (kind == "商店") return Color.FromArgb(235, 175, 140);
            return Color.FromArgb(170, 170, 178);
        }

        public void ApplyScale()
        {
            UiScale.Apply(this);
            _list.ItemHeight = Px(30);
            MinimumSize = Size.Add(UiScale.FitSize(640, 420), new Size(16, 39));
            LayoutRecords();
            ClearInlineEditor();
            ShowDetail();
        }

        Button MkBtn(string text, int x, int y, int w)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(44, 44, 47);
            b.ForeColor = Color.FromArgb(232, 232, 236);
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
            return b;
        }

        void LoadAll()
        {
            _all.Clear();
            try
            {
                string dir = Program.DataDir();
                foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("draft-") || name.StartsWith("pending-") || name.StartsWith("floor-lengths-")) continue;
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            var ser = new JavaScriptSerializer();
                            var o = ser.Deserialize<Dictionary<string, object>>(line);
                            if (o == null) continue;
                            var e = new RecordEntry();
                            e.File = f; e.Line = line; e.Data = o;
                            e.Kind = name.StartsWith("events-") ? "事件" : name.StartsWith("items-") ? "物品" : name.StartsWith("floors-") ? "樓層" : name.StartsWith("exits-") ? "出口" : name.StartsWith("endings-") ? "結局" : name.StartsWith("shops-") ? "商店" : "其他";
                            e.Title = TitleOf(e.Kind, o);
                            e.Time = o.ContainsKey("ts") ? Convert.ToString(o["ts"]) : "";
                            DateTimeOffset at;
                            if (DateTimeOffset.TryParse(e.Time, out at)) e.At = at.LocalDateTime;
                            if (e.Time.Length >= 16) e.Time = e.Time.Substring(11, 5);
                            _all.Add(e);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            _all.Sort(delegate (RecordEntry a, RecordEntry b) { return string.Compare(b.Time, a.Time, StringComparison.Ordinal); });
            ApplyFilter();
        }

        internal static string TitleOf(string kind, Dictionary<string, object> o)
        {
            if (kind == "事件")
            {
                if (o.ContainsKey("steps"))
                {
                    var arr = o["steps"] as System.Collections.ArrayList;
                    if (arr != null && arr.Count > 0)
                    {
                        var s1 = arr[0] as Dictionary<string, object>;
                        if (s1 != null && s1.ContainsKey("title")) return Convert.ToString(s1["title"]);
                    }
                }
                if (o.ContainsKey("title")) return Convert.ToString(o["title"]);
                return "（無標題）";
            }
            if (kind == "物品") return o.ContainsKey("name") ? Convert.ToString(o["name"]) : "?";
            if (kind == "樓層") return (o.ContainsKey("floorId") ? Convert.ToString(o["floorId"]) : "?") + " " + (o.ContainsKey("floorName") ? Convert.ToString(o["floorName"]) : "");
            if (kind == "出口") return (o.ContainsKey("event") ? Convert.ToString(o["event"]) : "出口") + " → " + (o.ContainsKey("target") ? Convert.ToString(o["target"]) : "（未填）");
            if (kind == "結局") return o.ContainsKey("name") ? Convert.ToString(o["name"]) : "?";
            if (kind == "商店") { string t = o.ContainsKey("title") ? Convert.ToString(o["title"]) : ""; var arr = o.ContainsKey("items") ? o["items"] as System.Collections.ArrayList : null; return (t == "" ? "商店" : t) + (arr != null ? "（" + arr.Count + " 件）" : ""); }
            return "?";
        }

        void ApplyFilter()
        {
            string q = _search.Text.Trim();
            _shown.Clear();
            _list.Items.Clear();
            string[] kindDefs = { "全部", "事件", "物品", "樓層", "出口", "結局", "商店" };
            int ki = _kind == null ? 0 : _kind.SelectedIndex;
            string kind = ki > 0 && ki < kindDefs.Length ? kindDefs[ki] : "全部";
            foreach (var e in _all)
            {
                if (kind != "全部" && e.Kind != kind) continue;
                if (q.Length > 0 && (e.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (e.Kind ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _shown.Add(e);
                _list.Items.Add(e.Title ?? "");
            }
            int ne = 0, ni = 0, nf = 0, nx = 0, nd = 0, ns = 0;
            foreach (var e in _all)
            {
                if (e.Kind == "事件") ne++;
                else if (e.Kind == "物品") ni++;
                else if (e.Kind == "樓層") nf++;
                else if (e.Kind == "出口") nx++;
                else if (e.Kind == "結局") nd++;
                else if (e.Kind == "商店") ns++;
            }
            _count.Text = "共 " + _shown.Count + " 筆";
            if (_tip != null)
                _tip.SetToolTip(_count, "事件 " + ne + " · 物品 " + ni + " · 樓層 " + nf
                    + " · 出口 " + nx + " · 結局 " + nd + " · 商店 " + ns);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        void ClearInlineEditor()
        {
            _closingInline = true;
            try
            {
                if (_inlineEditor != null)
                {
                    try { if (!_inlineEditor.IsDisposed) _inlineEditor.Close(); } catch { }
                    try { if (!_inlineEditor.IsDisposed) _inlineEditor.Dispose(); } catch { }
                    _inlineEditor = null;
                }
                _editorHost.Controls.Clear();
            }
            finally { _closingInline = false; }
        }

        void ShowDetail()
        {
            int i = _list.SelectedIndex;
            /* 篩選字一改，ListBox 會把 SelectedIndex 歸零再設回 0；同一筆就不要再重建編輯器。 */
            if (i >= 0 && i < _shown.Count && _inlineEditor != null && !_inlineEditor.IsDisposed
                && _inlineEditor.Entry == _shown[i])
                return;
            ClearInlineEditor();
            if (i < 0 || i >= _shown.Count)
            {
                var tip = new Label();
                tip.Text = "選中左邊的記錄，欄位會直接顯示在這裡。";
                tip.SetBounds(16, 16, 440, 24);
                tip.ForeColor = Color.FromArgb(160, 160, 168);
                _editorHost.Controls.Add(tip);
                UiText.ApplyTo(tip);
                return;
            }
            var f = new RecordEditForm(_shown[i]);
            f.TopLevel = false;
            f.MinimumSize = Size.Empty;
            f.FormBorderStyle = FormBorderStyle.None;
            f.TopMost = false;
            f.ShowInTaskbar = false;
            f.Dock = DockStyle.Fill;
            f.FormClosed += delegate
            {
                if (_inlineEditor == f) _inlineEditor = null;
                if (!_closingInline && !IsDisposed)
                {
                    try { BeginInvoke((Action)LoadAll); } catch { }
                }
            };
            _inlineEditor = f;
            _editorHost.Controls.Add(f);
            f.Show();
            UiText.ApplyTo(f);
            _editorHost.Invalidate(true);
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k) || d[k] == null) return "";
            return Convert.ToString(d[k]);
        }

        static string JoinArr(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k)) return "";
            var arr = d[k] as System.Collections.ArrayList;
            if (arr == null)
            {
                var single = d[k] as string;
                return single ?? Convert.ToString(d[k]);
            }
            var parts = new List<string>();
            foreach (var x in arr) parts.Add(Convert.ToString(x));
            return string.Join("、", parts.ToArray());
        }

        static string ReqText(object v)
        {
            var d = v as Dictionary<string, object>;
            if (d == null) return Convert.ToString(v);
            string t = Str(d, "type"), n = Str(d, "name");
            if (t == "item") return "物品「" + n + "」" + (Str(d, "consume").ToLower() == "true" ? "（會用掉）" : "");
            if (t == "attr") return n + " " + Str(d, "min") + "+";
            if (t == "character") return "角色 " + n;
            return n;
        }

        static string ScopeText(string s)
        {
            var parts = new List<string>();
            foreach (var x in s.Split('、'))
            {
                if (x == "floor") parts.Add("樓層");
                else if (x == "character") parts.Add("角色");
                else if (x == "common") parts.Add("通用");
                else if (x != "") parts.Add(x);
            }
            return string.Join("、", parts.ToArray());
        }

        static string OutcomeText(string s)
        {
            var parts = new List<string>();
            foreach (var x in s.Split('、'))
            {
                if (x == "normal") parts.Add("普通");
                else if (x == "floor-jump") parts.Add("跳層");
                else if (x == "shop") parts.Add("商店");
                else if (x == "rest") parts.Add("休息處");
                else if (x == "combat") parts.Add("戰鬥");
                else if (x != "") parts.Add(x);
            }
            return string.Join("、", parts.ToArray());
        }

        static string EventEffectText(Dictionary<string, object> ed)
        {
            if (ed == null) return "";
            string text = Str(ed, "text");
            string amount = Str(ed, "amount");
            if (text == "") text = (Str(ed, "target") + " " + Str(ed, "kind") + " " + amount).Trim();
            else if (amount != "" && amount != "0" && text.IndexOf(amount, StringComparison.Ordinal) < 0)
                text += " " + amount;
            string target = Str(ed, "target");
            if (target != "" && text.IndexOf(target, StringComparison.Ordinal) < 0) return target + "：" + text;
            return text;
        }

        static string EventProgressText(Dictionary<string, object> pg)
        {
            if (pg == null) return "";
            string txt = "出口進度";
            if (Str(pg, "delta") != "") txt += " " + Str(pg, "delta");
            if (Str(pg, "current") != "") txt += "（" + Str(pg, "current") + "/" + Str(pg, "total") + "）";
            return txt;
        }

        static List<Dictionary<string, object>> EventBranches(Dictionary<string, object> o)
        {
            var list = new List<Dictionary<string, object>>();
            var steps = o.ContainsKey("steps") ? o["steps"] as System.Collections.ArrayList : null;
            if (steps == null) steps = new System.Collections.ArrayList();
            if (steps.Count == 0 && o.ContainsKey("choice")) list.Add(o);
            for (int i = 1; i < steps.Count; i++)
            {
                var st = steps[i] as Dictionary<string, object>;
                if (st == null) continue;
                var nested = st.ContainsKey("branches") ? st["branches"] as System.Collections.ArrayList : null;
                if (nested != null)
                {
                    foreach (var n in nested)
                    {
                        var nd = n as Dictionary<string, object>;
                        if (nd != null) list.Add(nd);
                    }
                }
                else list.Add(st);
            }
            return list;
        }

        static string EventTitle(Dictionary<string, object> o)
        {
            var steps = o.ContainsKey("steps") ? o["steps"] as System.Collections.ArrayList : null;
            if (steps != null && steps.Count > 0)
            {
                var s1 = steps[0] as Dictionary<string, object>;
                if (s1 != null && Str(s1, "title") != "") return Str(s1, "title");
            }
            return Str(o, "title");
        }

        static Dictionary<string, object> EventStep1(Dictionary<string, object> o)
        {
            var steps = o.ContainsKey("steps") ? o["steps"] as System.Collections.ArrayList : null;
            if (steps != null && steps.Count > 0)
                return steps[0] as Dictionary<string, object>;
            return o;
        }

        static void FormatEvent(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("事件名：" + EventTitle(o));
            sb.AppendLine("事件類型：" + Program.EventKindText(Str(o, "eventKind")));
            sb.AppendLine("樓層：" + Str(o, "floorId") + " " + Str(o, "floorName"));
            string scopes = JoinArr(o, "scopes");
            if (scopes == "") scopes = Str(o, "scope");
            string chars = JoinArr(o, "characters");
            if (chars == "") chars = Str(o, "character");
            sb.AppendLine("歸屬：" + ScopeText(scopes) + (chars != "" ? "　角色：" + chars : ""));
            string outs = JoinArr(o, "outcomes");
            if (outs == "") outs = Str(o, "outcome");
            sb.AppendLine("結果類型：" + OutcomeText(outs) + (Str(o, "jumpTo") != "" ? " → " + Str(o, "jumpTo") : ""));
            string tags = JoinArr(o, "tags");
            if (tags != "") sb.AppendLine("事件標籤：" + tags.Replace("rest", "休息處"));
            var s1 = EventStep1(o);
            string intro = s1 != null ? Str(s1, "intro") : Str(o, "intro");
            if (intro != "")
            {
                sb.AppendLine("介紹：");
                foreach (var line in intro.Split('\n')) sb.AppendLine("  " + line.Trim());
            }

            var opts = s1 != null && s1.ContainsKey("options") ? s1["options"] as System.Collections.ArrayList : null;
            var branches = EventBranches(o);
            sb.AppendLine();
            sb.AppendLine("選項：");
            if (opts == null || opts.Count == 0) sb.AppendLine("  （尚無選項）");
            else
            {
                foreach (var oo in opts)
                {
                    var od = oo as Dictionary<string, object>;
                    if (od == null) continue;
                    string optText = Str(od, "text");
                    sb.AppendLine("  · " + optText);
                    if (Str(od, "desc") != "") sb.AppendLine("    " + Str(od, "desc"));
                    if (od.ContainsKey("requirement")) sb.AppendLine("    需求：" + ReqText(od["requirement"]));
                    if (Str(od, "conditionText") != "") sb.AppendLine("    選項條件：" + Str(od, "conditionText"));
                    if (Str(od, "check") != "") sb.AppendLine("    屬性檢定：" + Str(od, "check"));
                    bool needCheck = Str(od, "check") != "";
                    var req = od.ContainsKey("requirement") ? od["requirement"] as Dictionary<string, object> : null;
                    if (req != null && Str(req, "type") == "attr") needCheck = true;
                    foreach (var st in branches)
                    {
                        if (Str(st, "choice") != optText) continue;
                        var ck = st.ContainsKey("check") ? st["check"] as Dictionary<string, object> : null;
                        if (ck != null && Str(ck, "attribute") != "") needCheck = true;
                        if (needCheck)
                        {
                            string attr = ck != null ? Str(ck, "attribute") : Str(od, "check");
                            string outv = ck != null ? Str(ck, "outcome") : "";
                            sb.AppendLine("    判定：" + (outv == "" ? "—" : (outv == "失敗" || outv == "失败" || outv == "failure" ? "失敗" : "成功")) + (attr != "" ? "（" + attr + "）" : ""));
                            if (ck != null && Str(ck, "sameChoiceBonus") != "" && Str(ck, "sameChoiceBonus") != "0")
                                sb.AppendLine("    同選加成：+" + Str(ck, "sameChoiceBonus"));
                        }
                        var qt = st.ContainsKey("qte") ? st["qte"] as Dictionary<string, object> : null;
                        if (qt != null)
                        {
                            string qo = Str(qt, "outcome");
                            sb.AppendLine("    QTE：" + (qo == "failure" ? "失敗" : (qo == "success" ? "成功" : "已通過")));
                        }
                        var res = st.ContainsKey("results") ? st["results"] as System.Collections.ArrayList : null;
                        if (res != null && res.Count > 0)
                        {
                            sb.AppendLine("    結果：");
                            foreach (var r in res) sb.AppendLine("      " + Convert.ToString(r));
                        }
                        var eff = st.ContainsKey("effects") ? st["effects"] as System.Collections.ArrayList : null;
                        var pg = st.ContainsKey("progress") ? st["progress"] as Dictionary<string, object> : null;
                        var cb = st.ContainsKey("combat") ? st["combat"] as Dictionary<string, object> : null;
                        if ((eff != null && eff.Count > 0) || pg != null || cb != null)
                        {
                            sb.AppendLine("    影響：");
                            if (cb != null) sb.AppendLine("      · 戰鬥：" + Str(cb, "enemy"));
                            if (pg != null) sb.AppendLine("      · " + EventProgressText(pg));
                            if (eff != null)
                                foreach (var e in eff)
                                {
                                    var ed = e as Dictionary<string, object>;
                                    if (ed != null) sb.AppendLine("      · " + EventEffectText(ed));
                                }
                        }
                    }
                }
                /* 選中的選項不在選項清單裡（舊記錄/OCR 差異）也要顯示。 */
                var unmatched = branches.FindAll(delegate (Dictionary<string, object> b)
                {
                    string c = Str(b, "choice");
                    if (c == "") return false;
                    foreach (var oo in opts)
                    {
                        var od = oo as Dictionary<string, object>;
                        if (od != null && Str(od, "text") == c) return false;
                    }
                    return true;
                });
                foreach (var st in unmatched)
                {
                    sb.AppendLine("  · 已選（未匹配選項）：" + Str(st, "choice"));
                    var res = st.ContainsKey("results") ? st["results"] as System.Collections.ArrayList : null;
                    if (res != null) foreach (var r in res) sb.AppendLine("    結果：" + Convert.ToString(r));
                    var eff = st.ContainsKey("effects") ? st["effects"] as System.Collections.ArrayList : null;
                    if (eff != null) foreach (var e in eff)
                    {
                        var ed = e as Dictionary<string, object>;
                        if (ed != null) sb.AppendLine("    影響：" + EventEffectText(ed));
                    }
                }
            }
        }

        static void FormatItem(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("名稱：" + Str(o, "name"));
            sb.AppendLine("類型：" + Str(o, "type"));
            sb.AppendLine("來源：" + Str(o, "source") + (Str(o, "price") != "" ? "　參考價格 " + Str(o, "price") : ""));
            if (Str(o, "character") != "") sb.AppendLine("角色：" + Str(o, "character"));
            string eff = JoinArr(o, "eff");
            if (eff != "") { sb.AppendLine("效果："); foreach (var l in eff.Split('、')) sb.AppendLine("  · " + l); }
            string cures = JoinArr(o, "cures");
            if (cures != "") { sb.AppendLine("治得好："); foreach (var l in cures.Split('、')) sb.AppendLine("  · " + l); }
            if (Str(o, "desc") != "") { sb.AppendLine("介紹："); sb.AppendLine("  " + Str(o, "desc").Replace("\n", "\n  ")); }
        }

        static void FormatFloor(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("樓層：" + Str(o, "floorId") + " " + Str(o, "floorName"));
            sb.AppendLine("類型：" + (Str(o, "floorType") == "anomalous" ? "異常樓層" : "常規樓層"));
            sb.AppendLine("主題：" + (Str(o, "theme") == "light" ? "明亮" : "昏暗"));
            if (Str(o, "intro") != "") { sb.AppendLine("介紹："); foreach (var l in Str(o, "intro").Split('\n')) sb.AppendLine("  " + l); }
        }

        static void FormatExit(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("樓層：" + Str(o, "floorId") + " " + Str(o, "floorName"));
            sb.AppendLine("事件：" + Str(o, "event"));
            if (Str(o, "intro") != "") { sb.AppendLine("介紹："); foreach (var l in Str(o, "intro").Split('\n')) sb.AppendLine("  " + l); }
            if (Str(o, "transition") != "") { sb.AppendLine("通往文本："); foreach (var l in Str(o, "transition").Split('\n')) sb.AppendLine("  " + l); }
            sb.AppendLine("目標：" + (Str(o, "target") != "" ? Str(o, "target") : "（待處理）"));
        }

        static void FormatEnding(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("結局：" + Str(o, "name"));
            sb.AppendLine("分類：" + Str(o, "category"));
            if (Str(o, "intro") != "") { sb.AppendLine("通用介紹："); foreach (var l in Str(o, "intro").Split('\n')) sb.AppendLine("  " + l); }
            if (Str(o, "epilogue") != "") { sb.AppendLine("後日談："); foreach (var l in Str(o, "epilogue").Split('\n')) sb.AppendLine("  " + l); }
        }

        static void FormatShop(StringBuilder sb, Dictionary<string, object> o)
        {
            sb.AppendLine("商店：" + Str(o, "title"));
            sb.AppendLine("樓層：" + Str(o, "floorId") + " " + Str(o, "floorName"));
            if (Str(o, "intro") != "")
            {
                sb.AppendLine("介紹：");
                foreach (var l in Str(o, "intro").Split('\n')) sb.AppendLine("  " + l);
            }
            var items = o.ContainsKey("items") ? o["items"] as System.Collections.ArrayList : null;
            if (items != null && items.Count > 0)
            {
                sb.AppendLine("商品：");
                foreach (var it in items)
                {
                    var d = it as Dictionary<string, object>;
                    sb.AppendLine("  · " + (d != null ? Str(d, "n") : Convert.ToString(it)));
                }
            }
        }

        static string FormatValue(object v)
        {
            if (v == null) return "";
            var s = v as string;
            if (s != null) return s;
            var arr = v as System.Collections.ArrayList;
            if (arr != null)
            {
                var sb = new StringBuilder();
                foreach (var x in arr) sb.AppendLine("- " + FormatValue(x).Replace("\n", "\n  "));
                return sb.ToString().TrimEnd();
            }
            var d = v as Dictionary<string, object>;
            if (d != null)
            {
                var ser = new JavaScriptSerializer();
                return ser.Serialize(d);
            }
            return Convert.ToString(v);
        }

        void DeleteSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _shown.Count) return;
            var e = _shown[i];
            try
            {
                var lines = new List<string>(File.ReadAllLines(e.File));
                lines.Remove(e.Line);
                File.WriteAllLines(e.File, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
            LoadAll();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearInlineEditor();
            if (Instance == this) Instance = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 分層透明視窗（不遮畫面、可即時重繪） ═══════════════ */
    class LayeredForm : Form
    {
        /* 覆盖层/圆盘逐个重建时复用同尺寸的位图与 Graphics，避免每次按 E
           都重新分配几百 MB 级别的绘制表面。 */
        static Bitmap _sharedBmp;
        static Graphics _sharedG;
        static int _sharedW, _sharedH;
        Bitmap _bmp;
        Graphics _g;

        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e) { Render(); }

        public void Render()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            if (_sharedBmp == null || _sharedW != Width || _sharedH != Height)
            {
                if (_sharedG != null) _sharedG.Dispose();
                if (_sharedBmp != null) _sharedBmp.Dispose();
                _sharedW = Width; _sharedH = Height;
                _sharedBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                _sharedG = Graphics.FromImage(_sharedBmp);
            }
            _bmp = _sharedBmp;
            _g = _sharedG;
            _g.Clear(Color.Transparent);
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(_g);
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = _bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            var ptDst = new POINT { X = Left, Y = Top };
            var size = new SIZE { CX = Width, CY = Height };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            bool ok = Native.UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref bf, 2);
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layer.log"), DateTime.Now.ToString("HH:mm:ss") + " " + Width + "x" + Height + " ok=" + ok + " at " + Left + "," + Top + "\r\n"); } catch { }
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        protected virtual void Draw(Graphics g) { }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            /* 位图/Graphics 归 LayeredForm 的静态缓存所有，不能在这里释放。 */
            _g = null;
            _bmp = null;
            base.OnFormClosed(e);
        }
    }

    /* ══ 覆蓋層：即時高亮＋分類標註，不遮畫面 ═══════════════ */
    class OverlayForm : LayeredForm
    {
        public Dictionary<string, object> Result;
        public int Step;
        public string Kind = "event";
        public List<OcrLine> Selected;
        List<OcrLine> _lines;
        IntPtr _game;
        Point _down, _cur;
        bool _dragging;

        public OverlayForm(int step, List<OcrLine> lines, Rectangle screenRect, IntPtr game, string kind)
        {
            Step = step; _lines = lines; _game = game; Kind = kind;
            Text = "框選";
            Bounds = screenRect;
            TopMost = true;
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Program.CaptureSw != null)
            {
                Program.PerfLog("overlay-shown", Program.CaptureSw.ElapsedMilliseconds, "");
                Program.CaptureSw = null;
            }
        }

        protected override void Draw(Graphics g)
        {
            // 1) 整層壓暗
            using (var dim = new SolidBrush(Color.FromArgb(122, 6, 6, 8)))
                g.FillRectangle(dim, 0, 0, Width, Height);

            // 2) 辨識到的文字保持原色：把文字框挖空（alpha=0）
            var old = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
            {
                foreach (var l in _lines)
                {
                    if (l.IsChrome || l.OptionGroup == -2) continue;
                    g.FillRectangle(clear, l.X - 3, l.Y - 2, l.W + 6, l.H + 4);
                }
            }
            g.CompositingMode = old;

            // 3) 已分類元素畫框＋標籤
            DrawAnnotations(g);

            // 4) 拖框高亮
            if (_dragging)
            {
                var r = NormRect(_down, _cur);
                using (var band = new SolidBrush(Color.FromArgb(45, 90, 170, 255)))
                    g.FillRectangle(band, r);
                /* 只按 Y 轴判断：横坐标完全不参与，竖着拖也能选中同高度的整行。 */
                using (var fill = new SolidBrush(Color.FromArgb(34, 90, 170, 255)))
                using (var pen = new Pen(Color.FromArgb(235, 90, 170, 255), 1.5f))
                {
                    foreach (var l in _lines)
                    {
                        if (!IsSelectable(l) || !InYBand(l, r)) continue;
                        var box = Rectangle.Inflate(l.Rect, 2, 2);
                        g.FillRectangle(fill, box);
                        g.DrawRectangle(pen, box);
                    }
                }
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, r);
                }
            }

            // 5) 提示
            string hint;
            if (Kind == "floor") hint = "拖框選「樓層名＋介紹」　鬆手即存　Esc 取消";
            else if (Kind == "exit1") hint = "拖框選「出口事件＋介紹」　鬆手即存　Esc 取消";
            else if (Kind == "exit2") hint = "拖框選「進入其他樓層的文本」　鬆手即存　Esc 取消";
            else if (Kind == "ending1") hint = "拖框選「結局名＋通用介紹」　鬆手即存　Esc 取消";
            else if (Kind == "ending2") hint = "拖框選「後日談」　鬆手即存　Esc 取消";
            else if (Step == 1) hint = "第 1 步：拖框選「事件名＋介紹」　鬆手即存　Esc 取消";
            else hint = "第 " + Step + " 步：拖框選「你決定＋結果」　鬆手即存　Esc 取消";
            hint = hint.Replace("Esc", Program.VkName(Program.CancelVk));
            var font = new Font("Microsoft JhengHei UI", 9F);
            var sz = g.MeasureString(hint, font);
            int hx = Math.Max(8, Width - (int)sz.Width - 24);
            int hy = Math.Max(8, Height - (int)sz.Height - 18);
            using (var bg = new SolidBrush(Color.FromArgb(205, 18, 18, 22)))
                g.FillRectangle(bg, hx, hy, sz.Width + 16, sz.Height + 10);
            using (var fg = new SolidBrush(Color.FromArgb(255, 250, 210)))
                g.DrawString(hint, font, fg, hx + 8, hy + 5);
            font.Dispose();
        }

        /* 把 Parse.Classify 的結果畫成帶顏色的框與標籤：
           藍=樓層名、橙=事件名、綠=選項、黃=選項條件。 */
        void DrawAnnotations(Graphics g)
        {
            if (_lines == null || _lines.Count == 0) return;
            using (var font = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Bold))
            {
                foreach (var l in _lines)
                {
                    if (l.IsChrome || l.OptionGroup == -2) continue;
                    Color color;
                    string label;
                    if (l.IsFloor)
                    {
                        color = Color.FromArgb(255, 64, 158, 255);
                        label = "樓層名";
                    }
                    else if (Kind == "event" && l.IsTitle)
                    {
                        color = Color.FromArgb(255, 255, 167, 38);
                        label = "事件名";
                    }
                    else if (Kind == "event" && l.IsOption && l.OptionGroup >= 0)
                    {
                        color = Color.FromArgb(255, 76, 217, 100);
                        bool cond = l.IsCondition || (l.Text ?? "").TrimStart().StartsWith("★");
                        label = cond ? "⭐條件選項" : "選項" + CnNumber(l.OptionGroup + 1);
                    }
                    else if (Kind == "event" && l.IsCondition)
                    {
                        color = Color.FromArgb(255, 255, 214, 64);
                        label = "⭐條件選項";
                    }
                    else continue;
                    DrawTag(g, font, l.Rect, color, label);
                }
            }
        }

        static string CnNumber(int n)
        {
            string[] digits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (n <= 0) return n.ToString();
            if (n < 10) return digits[n];
            if (n == 10) return "十";
            if (n < 20) return "十" + digits[n - 10];
            int tens = n / 10, ones = n % 10;
            return digits[tens] + "十" + (ones == 0 ? "" : digits[ones]);
        }

        void DrawTag(Graphics g, Font font, Rectangle r, Color color, string label)
        {
            var box = Rectangle.Inflate(r, 3, 3);
            using (var fill = new SolidBrush(Color.FromArgb(32, color)))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(color, 2))
                g.DrawRectangle(pen, box);

            var sz = g.MeasureString(label, font);
            int tw = (int)Math.Ceiling(sz.Width) + 10;
            int th = (int)Math.Ceiling(sz.Height) + 4;
            int tx = r.Left - tw - 4;
            if (tx < 2) tx = r.Right + 4;
            if (tx + tw > Width - 2) tx = Math.Max(2, r.Left);
            int ty = Math.Max(2, r.Top + (r.Height - th) / 2);
            if (ty + th > Height - 2) ty = Math.Max(2, Height - th - 2);

            using (var bg = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                g.FillRectangle(bg, tx, ty, tw, th);
            using (var pen = new Pen(color, 1))
                g.DrawRectangle(pen, tx, ty, tw, th);
            using (var fg = new SolidBrush(color))
                g.DrawString(label, font, fg, tx + 5, ty + 2);
        }

        static Rectangle NormRect(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        bool IsSelectable(OcrLine l)
        {
            return l != null && !l.IsChrome && !l.IsOption && !l.IsCondition && l.OptionGroup != -2;
        }

        /* 框選只看垂直帶：同一高度的文字，不管 X 多遠都算選中。 */
        bool InYBand(OcrLine l, Rectangle r)
        {
            return l != null && l.CenterY >= r.Top && l.CenterY <= r.Bottom;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _down = e.Location; _cur = e.Location; _dragging = true;
                Render();
            }
            else if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) { _cur = e.Location; Render(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                _cur = e.Location;
                var r = NormRect(_down, _cur);
                if (r.Height < 8) { Render(); base.OnMouseUp(e); return; }

                var sel = new List<OcrLine>();
                foreach (var l in _lines)
                {
                    if (!IsSelectable(l) || !InYBand(l, r)) continue;
                    sel.Add(l);
                }
                sel.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });
                if (sel.Count == 0) { Render(); base.OnMouseUp(e); return; }

                Selected = sel;
                if (Kind == "floor") Result = BuildFloor(sel);
                else if (Kind == "ending1") Result = BuildNamed(sel, "name");
                else if (Kind == "ending2") Result = BuildLines(sel, "epilogue");
                else if (Kind == "exit1") Result = BuildNamed(sel, "event");
                else if (Kind == "exit2") Result = BuildLines(sel, "transition");
                else Result = Step == 1 ? BuildStep1(sel) : BuildStep2(sel);
                DialogResult = DialogResult.OK;
                Close();
            }
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            /* 扫描/框选中的取消键：只关闭当前这一步，不结束整条记录。 */
            if ((int)e.KeyCode == Program.CancelVk || e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Native.SetForegroundWindow(_game); } catch { }
            base.OnFormClosed(e);
        }

        static string StripTitle(string t)
        {
            var m = System.Text.RegularExpressions.Regex.Match(t, "【([^】]+)】");
            return m.Success ? m.Groups[1].Value.Trim() : t.Trim();
        }

        Dictionary<string, object> BuildNamed(List<OcrLine> sel, string key)
        {
            var lines = new List<string>();
            foreach (var l in sel) lines.Add(Party.Normalize(l.Text.Trim()));
            var rec = new Dictionary<string, object>();
            rec[key] = lines.Count > 0 ? StripTitle(lines[0]) : "";
            if (key == "name") rec["name"] = rec[key];
            var rest = new List<string>();
            for (int i = 1; i < lines.Count; i++) rest.Add(lines[i]);
            rec["intro"] = string.Join("\n", rest.ToArray());
            return rec;
        }

        Dictionary<string, object> BuildFloor(List<OcrLine> sel)
        {
            var lines = new List<string>();
            foreach (var l in sel) lines.Add(Party.Normalize(l.Text.Trim()));
            string first = lines.Count > 0 ? lines[0] : "";
            var rec = new Dictionary<string, object>();
            /* 左上角识别到的楼层优先；框选内容只用来补充简介。 */
            string fid = Parse.FloorId, fname = Parse.FloorName;
            var mf = System.Text.RegularExpressions.Regex.Match(first, @"Level\s*([^「『\[\n]+?)\s*[「『\[]([^」』\]\n]*)[」』\]]?");
            if (fid == "" && mf.Success)
            {
                fid = mf.Groups[1].Value.Trim();
                if (fid == "O" || fid == "o") fid = "0";
                if (fid == "I" || fid == "l") fid = "1";
                fname = mf.Groups[2].Value;
            }
            rec["floorRaw"] = fid != "" ? ("Level " + fid + "「" + fname + "」") : first;
            rec["floorId"] = fid;
            rec["floorName"] = fname;
            rec["floorType"] = Parse.IsNormalFloorId(fid) ? "normal" : "anomalous";
            var rest = new List<string>();
            for (int i = 1; i < lines.Count; i++) rest.Add(lines[i]);
            rec["intro"] = string.Join("\n", rest.ToArray());
            return rec;
        }

        Dictionary<string, object> BuildLines(List<OcrLine> sel, string key)
        {
            var lines = new List<string>();
            foreach (var l in sel) lines.Add(Party.Normalize(l.Text.Trim()));
            var rec = new Dictionary<string, object>();
            rec[key] = string.Join("\n", lines.ToArray());
            return rec;
        }

        static void ApplyFreeChoiceBonus(Dictionary<string, object> rec)
        {
            if (rec == null || Convert.ToString(rec.ContainsKey("eventKind") ? rec["eventKind"] : "") != "free") return;
            var steps = rec.ContainsKey("steps") ? rec["steps"] as System.Collections.ArrayList : null;
            if (steps == null) return;
            var branches = new List<Dictionary<string, object>>();
            for (int i = 1; i < steps.Count; i++)
            {
                var st = steps[i] as Dictionary<string, object>;
                if (st == null) continue;
                var nested = st.ContainsKey("branches") ? st["branches"] as System.Collections.ArrayList : null;
                if (nested != null)
                    foreach (var n in nested) { var nd = n as Dictionary<string, object>; if (nd != null) branches.Add(nd); }
                else branches.Add(st);
            }
            var counts = new Dictionary<string, int>();
            foreach (var b in branches)
            {
                string c = Convert.ToString(b.ContainsKey("choice") ? b["choice"] : "");
                if (c == "") continue;
                counts[c] = (counts.ContainsKey(c) ? counts[c] : 0) + 1;
            }
            foreach (var b in branches)
            {
                string c = Convert.ToString(b.ContainsKey("choice") ? b["choice"] : "");
                if (c == "" || !counts.ContainsKey(c)) continue;
                var ck = b.ContainsKey("check") ? b["check"] as Dictionary<string, object> : null;
                if (ck == null) continue;
                ck["sameChoiceBonus"] = 2 * (counts[c] - 1);
                ck["freeRule"] = "sameChoice";
            }
        }

        internal static string EventKindText(string k)
        {
            if (k == "free") return "自由事件";
            if (k == "junction") return "岔路事件";
            if (k == "character") return "角色事件";
            if (k == "cooperation") return "合力事件";
            if (k == "common") return "通用事件";
            return "普通事件";
        }

        static string NormalizeAttr(string s)
        {
            string t = (s ?? "").Trim();
            if (t.Contains("力量") || t == "力") return "力量";
            if (t.Contains("敏捷") || t == "敏") return "敏捷";
            if (t.Contains("体质") || t.Contains("體質") || t == "體" || t == "体") return "體質";
            if (t.Contains("感知") || t == "感") return "感知";
            if (t.Contains("智力") || t.Contains("智") || t == "智") return "智力";
            if (t.Contains("魅力") || t == "魅") return "魅力";
            return "";
        }

        Dictionary<string, object> BuildStep1(List<OcrLine> sel)
        {
            string title = Parse.TitleOf(sel);
            /* 多人角色事件提示：全队投票：……（「狐天 自己的选择」……） */
            string allText = "";
            foreach (var l in _lines) allText += (l.Text ?? "") + "\n";
            bool characterEvent = false;
            string characterPlayer = "", characterName = "";
            var mce = System.Text.RegularExpressions.Regex.Match(allText, @"[「『]([^」』]{1,16}?)\s*自己的选[择擇][」』]");
            if (mce.Success)
            {
                characterEvent = true;
                characterPlayer = mce.Groups[1].Value.Trim();
                characterName = Party.MapPlayer(characterPlayer);
            }
            bool junctionEvent = System.Text.RegularExpressions.Regex.IsMatch(allText, @"各自[选選](?:一条|一條)路");
            bool freeEvent = System.Text.RegularExpressions.Regex.IsMatch(allText, @"各自[选選](?:一个|一個|一条|一條)");
            bool commonEvent = System.Text.RegularExpressions.Regex.IsMatch(allText, @"通用事件");
            bool coopEvent = System.Text.RegularExpressions.Regex.IsMatch(allText, @"合力|合作|協力|团队合作|團隊合作");
            if (string.IsNullOrEmpty(allText)) { freeEvent = false; commonEvent = false; coopEvent = false; }

            var intro = new List<string>();
            foreach (var l in sel)
            {
                if (title != "" && l.Text.IndexOf("【" + title + "】", StringComparison.Ordinal) >= 0) continue;
                if (Parse.IsChrome(l.Text)) continue;
                if (IsMultiplayerPrompt(l.Text)) continue;
                if (IsStatusWorsening(l.Text)) continue;
                intro.Add(Party.Normalize(l.Text.Trim()));
            }
            var opts = _lines.FindAll(delegate (OcrLine l) { return l.IsOption && l.OptionGroup >= 0; });
            opts.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });
            var optionList = new List<object>();
            foreach (var o in opts)
            {
                if (Parse.IsChrome(o.Text)) continue;
                var d = new Dictionary<string, object>();
                d["text"] = Program.CleanOptionText(Party.Normalize(o.Text.Trim()));
                if (o.Desc != "") d["desc"] = o.Desc;
                string attr = NormalizeAttr(o.Check);
                if (attr != "") d["check"] = attr;
                if (o.ReqType == "attr")
                {
                    string reqAttr = NormalizeAttr(o.ReqName);
                    if (reqAttr != "" && !d.ContainsKey("check")) d["check"] = reqAttr;
                }
                if (o.TextColor != Color.Empty) d["color"] = ColorTranslator.ToHtml(o.TextColor);
                string conditionText = Party.Normalize(o.Condition);
                string conditionAttr = NormalizeAttr(conditionText);
                if (conditionAttr != "" && !d.ContainsKey("check")) d["check"] = conditionAttr;
                if (o.IsSpecial)
                {
                    d["special"] = true;
                    d["specialReason"] = SpecialReason(o);
                    if (conditionText != "" && conditionAttr == "" && !d.ContainsKey("check"))
                        d["conditionText"] = conditionText;
                }
                if (o.ReqType != "" && o.ReqType != "attr")
                {
                    var req = new Dictionary<string, object>();
                    req["type"] = o.ReqType;
                    req["name"] = o.ReqName;
                    if (o.ReqType == "item") req["consume"] = o.ReqConsume;
                    d["requirement"] = req;
                }
                optionList.Add(d);
            }
            var rec = new Dictionary<string, object>();
            rec["step"] = 1;
            rec["floorId"] = Parse.FloorId;
            rec["floorName"] = Parse.FloorName;
            rec["title"] = title;
            rec["intro"] = string.Join("\n", intro.ToArray());
            if (characterEvent)
            {
                rec["characterEvent"] = true;
                rec["characterPlayer"] = characterPlayer;
                if (characterName != "") rec["character"] = characterName;
            }
            if (characterEvent) rec["eventKind"] = "character";
            else if (commonEvent) rec["eventKind"] = "common";
            else if (coopEvent) rec["eventKind"] = "cooperation";
            else if (junctionEvent) rec["eventKind"] = "junction";
            else if (freeEvent) rec["eventKind"] = "free";
            var mlead = System.Text.RegularExpressions.Regex.Match(rec["intro"] as string ?? "", @"(?:只有你察覺到了|只有你察觉到了).*?(?:通到|通往|通向)\s*Level\s*([0-9A-Za-z.]+)");
            if (mlead.Success) rec["leadsTo"] = mlead.Groups[1].Value;
            rec["options"] = optionList;
            return rec;
        }

        static bool IsStatusCondition(string s)
        {
            string[] words = { "扭伤", "扭傷", "感染", "高烧", "高燒", "失温", "失溫", "中毒", "出血", "骨折", "昏迷", "恐惧", "恐懼", "饥饿", "飢餓", "口渴" };
            foreach (var w in words) if ((s ?? "").IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static string SpecialReason(OcrLine o)
        {
            if (o == null) return "event";
            if (o.ReqType == "item") return "item";
            if (o.ReqType == "character") return "character";
            if (o.ReqType == "attr" || o.Check != "") return "attribute";
            if (IsStatusCondition(o.Condition)) return "status";
            if (o.ReqType == "skill") return "skill";
            if (o.Condition != "") return "event";
            return "event";
        }

        static bool TryChoiceLine(string t, out string actor, out string choice)
        {
            actor = ""; choice = "";
            var mc = System.Text.RegularExpressions.Regex.Match(t ?? "", @"(?:你決定|你决定|→\s*([^：:]{1,24}))[：:]\s*[「『](.+?)[」』]");
            if (!mc.Success) return false;
            if (mc.Groups[1].Success && mc.Groups[1].Value.Trim() != "")
            {
                string a = mc.Groups[1].Value.Trim();
                if (a != "队伍决定" && a != "隊伍決定") actor = a;
            }
            choice = mc.Groups[2].Value.Trim();
            return true;
        }

        static List<string> SplitActors(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(raw)) return list;
            string[] parts = System.Text.RegularExpressions.Regex.Split(raw, @"[、,，;；]|\s+(?:和|與|与)\s+");
            foreach (var part in parts)
            {
                string p = (part ?? "").Trim(' ', '　', '（', '）', '(', ')', '★', '☆');
                if (p.Length > 0 && p.Length < 24 && !list.Contains(p)) list.Add(p);
            }
            return list;
        }

        static void AddQtePlayer(List<object> list, string raw, string outcome)
        {
            foreach (var pn in SplitActors(raw))
            {
                bool dup = false;
                foreach (var x in list)
                {
                    var xd = x as Dictionary<string, object>;
                    if (xd != null && Convert.ToString(xd["player"]) == pn && Convert.ToString(xd["outcome"]) == outcome) { dup = true; break; }
                }
                if (dup) continue;
                var d = new Dictionary<string, object>();
                d["player"] = pn;
                d["outcome"] = outcome;
                string cn = Party.MapPlayer(pn);
                if (cn == "" && pn == Party.LocalName) cn = "你";
                if (cn != "") d["character"] = cn;
                list.Add(d);
            }
        }

        Dictionary<string, object> BuildStep2(List<OcrLine> sel)
        {
            /* 自由事件一次框到多个“→ 玩家：「选择」”时，按选择行切成多个分支，
               不再只保留最后一个选择。 */
            var choiceAt = new List<int>();
            for (int ci = 0; ci < sel.Count; ci++)
            {
                string ca0, cc0;
                if (TryChoiceLine(sel[ci].Text, out ca0, out cc0)) choiceAt.Add(ci);
            }
            if (choiceAt.Count > 1)
            {
                var root = new Dictionary<string, object>();
                root["step"] = Step;
                var bList = new List<object>();
                for (int bi = 0; bi < choiceAt.Count; bi++)
                {
                    int start = choiceAt[bi];
                    int end = bi + 1 < choiceAt.Count ? choiceAt[bi + 1] : sel.Count;
                    var seg = sel.GetRange(start, end - start);
                    bList.Add(BuildStep2(seg));
                }
                root["branches"] = bList;
                var last = bList[bList.Count - 1] as Dictionary<string, object>;
                if (last != null)
                {
                    if (last.ContainsKey("choice")) root["choice"] = last["choice"];
                    if (last.ContainsKey("results")) root["results"] = last["results"];
                    if (last.ContainsKey("effects")) root["effects"] = last["effects"];
                    if (last.ContainsKey("check")) root["check"] = last["check"];
                    if (last.ContainsKey("combat")) root["combat"] = last["combat"];
                    if (last.ContainsKey("progress")) root["progress"] = last["progress"];
                    if (last.ContainsKey("qte")) root["qte"] = last["qte"];
                    if (last.ContainsKey("player")) root["player"] = last["player"];
                    if (last.ContainsKey("character")) root["character"] = last["character"];
                }
                return root;
            }

            string choice = "";
            string actor = "";
            bool teamDecision = false;
            string checkAttr = "", outcome = "", enemy = "", leadsTo = "";
            int progressDelta = 0, progressTotal = -1;
            bool hasProgress = false;
            bool hasQte = false;
            string qteOutcome = "";
            var qtePlayers = new List<object>();
            var results = new List<string>();
            var effects = new List<object>();
            foreach (var l in sel)
            {
                string t = l.Text.Trim();
                var ml = System.Text.RegularExpressions.Regex.Match(t, @"(?:只有你察覺到了|只有你察觉到了).*?(?:通到|通往|通向)\s*Level\s*([0-9A-Za-z.]+)");
                if (ml.Success) leadsTo = ml.Groups[1].Value;
                /* 有些楼层显示“→ 天狐：「…」”而不是“→ 你决定：「…」”，
                   箭头开头的这句同样是玩家已选行动，不能混进结果正文。 */
                string ca, cc;
                if (TryChoiceLine(t, out ca, out cc))
                {
                    if (ca != "") actor = ca;
                    if (t.IndexOf("队伍决定", StringComparison.Ordinal) >= 0 || t.IndexOf("隊伍決定", StringComparison.Ordinal) >= 0)
                        teamDecision = true;
                    choice = Party.Normalize(cc);
                    continue;
                }
                var mpd = System.Text.RegularExpressions.Regex.Match(t, @"^出口(?:进度|進度)\s*([+-]\s*\d+)\s*$");
                if (mpd.Success)
                {
                    progressDelta += int.Parse(System.Text.RegularExpressions.Regex.Replace(mpd.Groups[1].Value, @"\s+", ""));
                    hasProgress = true;
                    continue;
                }
                var mpp = System.Text.RegularExpressions.Regex.Match(t, @"^出口(?:进度|進度)\s*(\d+)\s*/\s*(\d+)\s*$");
                if (mpp.Success)
                {
                    /* 这个不是事件结果；只把总数写进楼层长度，事件里不留。 */
                    progressTotal = int.Parse(mpp.Groups[2].Value);
                    if (progressTotal > 0) Program.WriteFloorLength(Parse.FloorId, Parse.FloorName, progressTotal);
                    continue;
                }
                if (IsMultiplayerPrompt(t)) continue;
                if (IsStatusWorsening(t)) continue;
                if (IsQtePrompt(t)) { hasQte = true; continue; }
                string qteResult, qtePlayer;
                if (IsQteResult(t, out qteResult, out qtePlayer))
                {
                    hasQte = true;
                    if (qteResult != "") qteOutcome = qteResult;
                    else if (qteOutcome == "" && qtePlayer != "") qteOutcome = "failure";
                    if (qtePlayer != "") AddQtePlayer(qtePlayers, qtePlayer, qteResult == "" ? "failure" : qteResult);
                    continue;
                }
                /* “狐天（慢了一步）：体力 -2、陷入「作呕感」”仍要拆效果，
                   但括号里的慢了一步也是一次个人 QTE 失败。 */
                var mqSlow = System.Text.RegularExpressions.Regex.Match(t, @"^([^：:]{1,16}?)\s*[（(]\s*慢了一步\s*[）)]\s*[：:]");
                if (mqSlow.Success)
                {
                    hasQte = true;
                    if (qteOutcome == "") qteOutcome = "failure";
                    AddQtePlayer(qtePlayers, mqSlow.Groups[1].Value, "failure");
                }
                string cAttr, cDc, cRoll, cOut, cPl;
                if (Parse.TryParseCheck(t, out cAttr, out cDc, out cRoll, out cOut, out cPl))
                {
                    checkAttr = NormalizeAttr(cAttr);
                    if (cOut != "") outcome = cOut;
                    if (cPl != "")
                    {
                        /* 只有队伍栏没识别出本地玩家时才拿检定角色当 fallback；
                           不能把“狐天”这种其他玩家误设成本地玩家，否则会写成“你”。 */
                        if (Party.LocalName == "" && cPl != "你" && cPl != "妳" && cPl != "全员" && cPl != "全員") Party.SetLocal(cPl);
                        if (actor == "") actor = cPl;
                    }
                    continue;
                }
                /* 饥饿／口渴归零的红色警示与随之扣血、扣理智都不记。 */
                if (IsHungerThirstWarning(t)) continue;

                /* 大成功／大失败的额外奖惩整行不记；检定行本身含“掷出”，
                   已经在上面解析成普通成功／失败，不会走到这里。 */
                if (IsCriticalExtraLine(t)) continue;

                var mcombat = System.Text.RegularExpressions.Regex.Match(t, @"【(?:戰鬥|战斗)】\s*(.+)");
                if (mcombat.Success) { enemy = mcombat.Groups[1].Value.Trim(); continue; }

                bool allEffects;
                var parsedEffects = ParseEffects(t, out allEffects);
                if (allEffects && parsedEffects.Count > 0)
                {
                    effects.AddRange(parsedEffects);
                    continue;
                }
                results.Add(Party.Normalize(t));
            }
            var rec = new Dictionary<string, object>();
            rec["step"] = Step;
            rec["choice"] = choice;
            if (checkAttr != "")
            {
                var ck = new Dictionary<string, object>();
                ck["attribute"] = checkAttr;
                ck["hasCheck"] = true;
                if (outcome != "") ck["outcome"] = outcome;
                rec["check"] = ck;
            }
            if (enemy != "") { var cb = new Dictionary<string, object>(); cb["enemy"] = enemy; rec["combat"] = cb; }
            if (leadsTo != "") rec["leadsTo"] = leadsTo;
            if (effects.Count > 0) rec["effects"] = effects;
            if (hasProgress && progressDelta != 0)
            {
                var pg = new Dictionary<string, object>();
                pg["delta"] = progressDelta;
                rec["progress"] = pg;
            }
            if (hasQte)
            {
                var qd = new Dictionary<string, object>();
                if (qteOutcome != "") qd["outcome"] = qteOutcome;
                if (qtePlayers.Count > 0) qd["players"] = qtePlayers;
                rec["qte"] = qd;
            }
            string playerName = teamDecision && actor == "" ? "" : (actor != "" ? actor : Party.LocalName);
            if (playerName != "") rec["player"] = playerName;
            var players = SplitActors(playerName);
            if (players.Count > 0) rec["players"] = players;
            var chars = new List<string>();
            foreach (var pn in players)
            {
                string cn = Party.MapPlayer(pn);
                if (cn == "" && pn == Party.LocalName) cn = "你";
                if (cn != "" && !chars.Contains(cn)) chars.Add(cn);
            }
            if (chars.Count > 0)
            {
                rec["characters"] = chars;
                if (chars.Count == 1) rec["character"] = chars[0];
            }
            rec["results"] = Program.NormalizeResults(results);
            return rec;
        }

        /* 红三角 QTE 提示：⚠ 灯管摆下来了——闪！ */
        static bool IsQtePrompt(string t)
        {
            string s = (t ?? "").TrimStart();
            return s.StartsWith("⚠") || s.StartsWith("△") || s.StartsWith("▲");
        }

        /* QTE 结果：反应过来了／反应过来的有 N 个人＝成功；
           只有 0 个人反应过来／慢了一步＝失败。 */
        static bool IsQteResult(string t, out string outcome, out string player)
        {
            outcome = ""; player = "";
            string s = t ?? "";
            if (s.IndexOf("只有 0 个人反应", StringComparison.Ordinal) >= 0
                || s.IndexOf("只有0个人反应", StringComparison.Ordinal) >= 0)
            {
                outcome = "failure";
                return true;
            }
            var m = System.Text.RegularExpressions.Regex.Match(s, @"反应过来的有\s*(\d+)\s*个人");
            if (m.Success)
            {
                outcome = Convert.ToInt32(m.Groups[1].Value) > 0 ? "success" : "failure";
                return true;
            }
            if (s.IndexOf("反应过来了", StringComparison.Ordinal) >= 0
                || s.IndexOf("反應過來了", StringComparison.Ordinal) >= 0)
            {
                var mp = System.Text.RegularExpressions.Regex.Match(s, @"^([^：:]{1,24}?)\s*(?:反应过来了|反應過來了)");
                if (mp.Success) player = mp.Groups[1].Value.Trim();
                outcome = "success";
                return true;
            }
            /* “天狐（慢了一步）：体力 -3”是效果行，不是 QTE 结果行。 */
            if (s.IndexOf("慢了一步", StringComparison.Ordinal) >= 0
                && s.IndexOf("：", StringComparison.Ordinal) < 0
                && s.IndexOf(":", StringComparison.Ordinal) < 0)
            {
                var mp = System.Text.RegularExpressions.Regex.Match(s, @"^([^：:]{1,24}?)\s*慢了一步");
                if (mp.Success) player = mp.Groups[1].Value.Trim();
                outcome = "failure";
                return true;
            }
            return false;
        }

        /* 多人分配／角色事件提示，不属于剧情正文。 */
        static bool IsMultiplayerPrompt(string t)
        {
            string s = (t ?? "").Trim();
            if (s.IndexOf("要由谁来", StringComparison.Ordinal) >= 0 || s.IndexOf("要由誰來", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("大家推派", StringComparison.Ordinal) >= 0 || s.IndexOf("大家推選", StringComparison.Ordinal) >= 0
                || s.IndexOf("大家推举", StringComparison.Ordinal) >= 0 || s.IndexOf("大家推舉", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("自己的选", StringComparison.Ordinal) >= 0 && s.IndexOf("择", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("自己的選", StringComparison.Ordinal) >= 0 && s.IndexOf("擇", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("一按就算数", StringComparison.Ordinal) >= 0 || s.IndexOf("一按就算數", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /* 身上状态恶化（感染拖太久→高烧）不是事件结果，不记。 */
        static bool IsStatusWorsening(string t)
        {
            string s = t ?? "";
            return s.IndexOf("拖太久", StringComparison.Ordinal) >= 0
                || s.IndexOf("恶化", StringComparison.Ordinal) >= 0
                || s.IndexOf("惡化", StringComparison.Ordinal) >= 0;
        }

        /* 饥饿／口渴见底的红字提示，以及它附带的体力／理智惩罚，整行不记。 */
        static bool IsHungerThirstWarning(string t)
        {
            string s = t ?? "";
            bool warn = s.IndexOf("肚子饿", StringComparison.Ordinal) >= 0
                || s.IndexOf("肚子餓", StringComparison.Ordinal) >= 0
                || s.IndexOf("饿得", StringComparison.Ordinal) >= 0
                || s.IndexOf("餓得", StringComparison.Ordinal) >= 0
                || s.IndexOf("喉咙干", StringComparison.Ordinal) >= 0
                || s.IndexOf("喉嚨乾", StringComparison.Ordinal) >= 0
                || s.IndexOf("渴得", StringComparison.Ordinal) >= 0
                || s.IndexOf("嘴唇干", StringComparison.Ordinal) >= 0
                || s.IndexOf("嘴唇乾", StringComparison.Ordinal) >= 0;
            bool zero = s.IndexOf("饥饿 0", StringComparison.Ordinal) >= 0
                || s.IndexOf("飢餓 0", StringComparison.Ordinal) >= 0
                || s.IndexOf("口渴 0", StringComparison.Ordinal) >= 0;
            return warn || zero;
        }

        /* “大失败的代价……”“大成功的额外收获……”是暴击附加奖惩，
           按要求整行不记；检定行有“掷出”，不能误杀。 */
        static bool IsCriticalExtraLine(string t)
        {
            string s = t ?? "";
            bool critical = s.IndexOf("大成功", StringComparison.Ordinal) >= 0
                || s.IndexOf("大失败", StringComparison.Ordinal) >= 0
                || s.IndexOf("大失敗", StringComparison.Ordinal) >= 0;
            if (!critical) return false;
            if (s.IndexOf("→", StringComparison.Ordinal) >= 0) return false;
            return s.IndexOf("掷出", StringComparison.Ordinal) < 0
                && s.IndexOf("擲出", StringComparison.Ordinal) < 0;
        }

        /* 把“天狐：体力 -1、陷入「扭伤」”拆成结构化效果。
           体力/理智/饥饿/口渴等数值只记增减方向；状态与物品保留原文。
           只有整行都能识别时才从 results 移出，避免误吞普通叙述。 */
        static List<object> ParseEffects(string raw, out bool all)
        {
            all = false;
            var list = new List<object>();
            string t = Party.Normalize((raw ?? "").Trim());
            if (t.Length == 0) return list;

            string target = Party.LocalCharacter;
            string body = t;
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^([^：:]{1,16})[：:]\s*(.+)$");
            if (m.Success)
            {
                target = m.Groups[1].Value.Trim();
                body = m.Groups[2].Value.Trim();
            }
            /* “天狐（慢了一步）：体力 -3” 里括号是状态说明，不是名字。 */
            target = System.Text.RegularExpressions.Regex.Replace(target ?? "", @"[（(][^）)]*[）)]", "").Trim();
            if (target == "") target = "你";

            /* （某段记忆被留了下来……这次冒险的记忆碎片 +1） */
            var mm = System.Text.RegularExpressions.Regex.Match(t, @"(?:记忆碎片|記憶碎片)\s*([+-]?\s*\d+)");
            if (mm.Success)
            {
                int n = int.Parse(System.Text.RegularExpressions.Regex.Replace(mm.Groups[1].Value, @"\s+", ""));
                var d = new Dictionary<string, object>();
                d["target"] = target; d["kind"] = "記憶碎片"; d["amount"] = n;
                d["text"] = "記憶碎片 " + (n >= 0 ? "+" : "") + n;
                list.Add(d);
                all = true;
                return list;
            }

            string[] parts = body.Split(new char[] { '、', '丶', '，', ',', '；', ';', '·', '・' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return list;
            foreach (var part in parts)
            {
                string p = part.Trim();
                if (p.Length == 0) continue;

                var ms = System.Text.RegularExpressions.Regex.Match(p, @"^陷入\s*[「『]([^」』]+)");
                if (ms.Success)
                {
                    string name = ms.Groups[1].Value.Trim();
                    var d = new Dictionary<string, object>();
                    d["target"] = target; d["kind"] = "status"; d["name"] = name;
                    d["text"] = "陷入「" + name + "」";
                    list.Add(d);
                    continue;
                }
                var mi = System.Text.RegularExpressions.Regex.Match(p, @"^(?:獲得|获得|得到)\s*[「『]([^」』]+)");
                if (mi.Success)
                {
                    string name = mi.Groups[1].Value.Trim();
                    var d = new Dictionary<string, object>();
                    d["target"] = target; d["kind"] = "item"; d["name"] = name;
                    d["text"] = "得到「" + name + "」";
                    list.Add(d);
                    continue;
                }
                var mc = System.Text.RegularExpressions.Regex.Match(p, @"^(?:各|都|每人)?\s*(?:獲得|获得|得到)\s*(\d+)\s*(硬币|硬幣|金币|金幣)");
                if (mc.Success)
                {
                    int n = int.Parse(mc.Groups[1].Value);
                    string coin = mc.Groups[2].Value == "硬币" ? "硬幣" : (mc.Groups[2].Value == "金币" ? "金幣" : mc.Groups[2].Value);
                    bool shared = target == "所有人" || target == "全体" || target == "全體" || target == "大家"
                        || p.StartsWith("各") || p.StartsWith("都") || p.StartsWith("每人");
                    var d = new Dictionary<string, object>();
                    if (shared)
                    {
                        d["target"] = "所有人";
                        d["kind"] = "item"; d["name"] = coin; d["amount"] = n;
                        d["shared"] = true;
                        d["text"] = "所有人 各得到 " + n + " " + coin;
                    }
                    else
                    {
                        d["target"] = target; d["kind"] = "item"; d["name"] = coin; d["amount"] = n;
                        d["text"] = "得到 " + n + " " + coin;
                    }
                    list.Add(d);
                    continue;
                }
                var mr = System.Text.RegularExpressions.Regex.Match(p, @"^(體力|体力|理智|飢餓|饥饿|口渴|生命|精神|經驗|经验)\s*([+-]?\s*\d+)\s*$");
                if (mr.Success)
                {
                    string kind = EffectKind(mr.Groups[1].Value);
                    int amount = int.Parse(System.Text.RegularExpressions.Regex.Replace(mr.Groups[2].Value, @"\s+", ""));
                    var d = new Dictionary<string, object>();
                    d["target"] = target; d["kind"] = kind; d["amount"] = amount;
                    d["text"] = EffectText(kind, amount);
                    list.Add(d);
                    continue;
                }
                var mv = System.Text.RegularExpressions.Regex.Match(p, @"^(體力|体力|理智|飢餓|饥饿|口渴|生命|精神)\s*(\d+)\s*$");
                if (mv.Success)
                {
                    string kind = EffectKind(mv.Groups[1].Value);
                    int amount = int.Parse(mv.Groups[2].Value);
                    var d = new Dictionary<string, object>();
                    d["target"] = target; d["kind"] = kind; d["amount"] = amount;
                    d["text"] = kind + " " + amount;
                    list.Add(d);
                    continue;
                }
                list.Clear();
                return list;
            }
            all = true;
            return list;
        }

        static string EffectKind(string kind)
        {
            if (kind == "体力") return "體力";
            if (kind == "饥饿") return "飢餓";
            if (kind == "经验") return "經驗";
            return kind;
        }

        static string EffectText(string kind, int amount)
        {
            if (kind == "體力" || kind == "生命") return amount < 0 ? "損失" + kind : "恢復" + kind;
            if (kind == "理智" || kind == "精神") return amount < 0 ? "損失" + kind : "恢復" + kind;
            if (kind == "飢餓" || kind == "口渴") return amount < 0 ? "減少" + kind : "增加" + kind;
            if (kind == "經驗") return amount < 0 ? "失去經驗" : "獲得經驗";
            return kind + (amount >= 0 ? " +" : " ") + amount;
        }
    }

    /* ══ 徑向選單：按快捷鍵從滑鼠位置拖方向選模式 ═══════════════ */
    class RadialForm : LayeredForm
    {
        public string Picked = "";
        int _hot = -1;
        Point _center;
        static PrivateFontCollection _pfc;
        static Font _labelFont, _smallFont;
        static readonly string[] Modes = { "event", "item", "floor", "exit", "ending", "shop" };
        static readonly string[] Labels = { "事件", "物品", "樓層介紹", "出口", "結局", "商店" };

        static Font LabelFont()
        {
            if (_labelFont != null) return _labelFont;
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", "NotoSansCJKtc-Regular.otf");
                if (File.Exists(p))
                {
                    _pfc = new PrivateFontCollection();
                    _pfc.AddFontFile(p);
                    if (_pfc.Families.Length > 0)
                    {
                        _labelFont = new Font(_pfc.Families[0], 12F, FontStyle.Bold);
                        _smallFont = new Font(_pfc.Families[0], 8F);
                        return _labelFont;
                    }
                }
            }
            catch { }
            _labelFont = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
            _smallFont = new Font("Microsoft JhengHei UI", 8F);
            return _labelFont;
        }

        static Font SmallFont()
        {
            LabelFont();
            return _smallFont;
        }

        public RadialForm()
        {
            Text = "模式選單";
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = Cursor.Position;
            int w = 480, h = 480;
            int x = Math.Max(wa.Left, Math.Min(p.X - w / 2, wa.Right - w));
            int y = Math.Max(wa.Top, Math.Min(p.Y - h / 2, wa.Bottom - h));
            Bounds = new Rectangle(x, y, w, h);
            TopMost = true;
            KeyPreview = true;
            _center = new Point(w / 2, h / 2);
            Cursor.Position = p;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Capture = true; } catch { }
            _start = PointToClient(Cursor.Position);
            Render();
            if (!HoldMode) return;
            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 30;
            _poll.Tick += delegate
            {
                try { if ((Native.GetAsyncKeyState(Program.ModeVk) & 0x8000) == 0) Release(); }
                catch { }
            };
            _poll.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; } } catch { }
            base.OnFormClosed(e);
        }

        bool _done;
        bool _moved;
        Point _start;
        public bool HoldMode = true;
        System.Windows.Forms.Timer _poll;

        public void Release()
        {
            if (_done) return;
            if (_moved && _hot >= 0) { Commit(); return; }
            try { if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; } } catch { }
        }

        public void Commit()
        {
            if (_done) return;
            _done = true;
            try
            {
                if (_hot >= 0)
                {
                    Picked = Modes[_hot];
                    DialogResult = DialogResult.OK;
                }
                else DialogResult = DialogResult.Cancel;
                Close();
            }
            catch { }
        }

        protected override void Draw(Graphics g)
        {
            int cx = _center.X, cy = _center.Y, r = 175;
            using (var bg = new SolidBrush(Color.FromArgb(215, 18, 18, 22)))
                g.FillEllipse(bg, cx - r, cy - r, r * 2, r * 2);
            using (var pen = new Pen(Color.FromArgb(160, 120, 120, 130), 2))
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

            for (int i = 0; i < 6; i++)
            {
                float start = -120 + i * 60;
                if (i == _hot)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddPie(cx - r + 4, cy - r + 4, (r - 4) * 2, (r - 4) * 2, start, 60);
                        using (var b = new SolidBrush(Color.FromArgb(200, 70, 120, 210))) g.FillPath(b, path);
                    }
                }
                double mid = (start + 30) * Math.PI / 180.0;
                int ix = (int)(cx + Math.Cos(mid) * 92);
                int iy = (int)(cy + Math.Sin(mid) * 92);
                DrawIcon(g, i, ix, iy);

                int tx = (int)(cx + Math.Cos(mid) * 138);
                int ty = (int)(cy + Math.Sin(mid) * 138);
                var font = LabelFont();
                var sz = g.MeasureString(Labels[i], font);
                using (var fg = new SolidBrush(i == _hot ? Color.White : Color.FromArgb(235, 235, 240, 245)))
                    g.DrawString(Labels[i], font, fg, tx - sz.Width / 2, ty - sz.Height / 2);
            }

            using (var fg = new SolidBrush(Color.FromArgb(170, 200, 200, 210)))
            {
                var sf = SmallFont();
                var sz = g.MeasureString("放中間或 Esc = 取消", sf);
                g.DrawString("放中間或 Esc = 取消", sf, fg, cx - sz.Width / 2, cy + 26);
            }
        }

        static void DrawIcon(Graphics g, int i, int x, int y)
        {
            using (var pen = new Pen(Color.FromArgb(240, 240, 245, 250), 2f))
            using (var fill = new SolidBrush(Color.FromArgb(240, 240, 245, 250)))
            {
                switch (i)
                {
                    case 0:
                        g.DrawRectangle(pen, x - 8, y - 10, 16, 20);
                        g.DrawLine(pen, x - 4, y - 4, x + 4, y - 4);
                        g.DrawLine(pen, x - 4, y + 1, x + 4, y + 1);
                        g.DrawLine(pen, x - 4, y + 6, x + 2, y + 6);
                        break;
                    case 1:
                        g.DrawRectangle(pen, x - 9, y - 5, 18, 13);
                        g.DrawLine(pen, x - 9, y - 1, x + 9, y - 1);
                        g.DrawRectangle(pen, x - 3, y - 9, 6, 4);
                        break;
                    case 2:
                        for (int k = 0; k < 3; k++)
                        {
                            int yy = y - 8 + k * 6;
                            g.DrawPolygon(pen, new Point[] {
                                new Point(x - 9, yy), new Point(x, yy - 5), new Point(x + 9, yy), new Point(x, yy + 5)
                            });
                        }
                        break;
                    case 3:
                        g.DrawRectangle(pen, x - 10, y - 11, 12, 22);
                        g.FillEllipse(fill, x - 4, y - 1, 3, 3);
                        g.DrawLine(pen, x + 1, y, x + 10, y);
                        g.DrawLine(pen, x + 6, y - 4, x + 10, y);
                        g.DrawLine(pen, x + 6, y + 4, x + 10, y);
                        break;
                    case 4:
                        g.DrawLine(pen, x - 7, y - 11, x - 7, y + 11);
                        g.FillPolygon(fill, new Point[] {
                            new Point(x - 7, y - 11), new Point(x + 8, y - 6), new Point(x - 7, y - 1)
                        });
                        break;
                }
            }
        }

        int Hit(Point p)
        {
            double dx = p.X - _center.X, dy = p.Y - _center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 40 || dist > 180) return -1;
            double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double norm = (ang + 120 + 720) % 360;
            return (int)(norm / 60) % 6;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_moved)
            {
                int dx = e.X - _start.X, dy = e.Y - _start.Y;
                if (dx * dx + dy * dy > 100) _moved = true;
            }
            int h = Hit(e.Location);
            if (h != _hot) { _hot = h; Render(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= 0) { Picked = Modes[h]; DialogResult = DialogResult.OK; Close(); }
            else { DialogResult = DialogResult.Cancel; Close(); }
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }
    }

    static class Program
    {
        static string Url = "https://hulin-backrooms-rpg.firebaseapp.com/";
        static string ProfileDir, RecordOut, ClientPath;
        static Process Client;
        static bool ClientOwned = false;
        static IntPtr GameHwnd = IntPtr.Zero;
        public static IntPtr GameHandle { get { return GameHwnd; } }
        public static void AttachForDemo() { GameHwnd = Capture.FindGame(); }
        static Mutex SingleInstance;
        static Form ControlWindow;
        static Label StateLabel, StepLabel;
        static Button RecordsBtn, PendingBtn, QBtn, EBtn, CancelBtn, HotkeyBtn, CurrentBtn, HelpBtn, TradBtn, SimpBtn, EngBtn, CacheBtn;
        static Button[] ScaleBtns;
        static int CacheConfirm;
        static System.Windows.Forms.Timer CacheResetTimer;
        static Label ScaleLabel;
        static bool HotkeysEnabled = true;
        internal static bool Closing;
        static System.Windows.Forms.Timer UiTimer;
        static string Preset = "1600x900";
        internal static DateTime SessionStart = DateTime.Now;

        internal static string EventKindText(string k)
        {
            if (k == "free") return "自由事件";
            if (k == "junction") return "岔路事件";
            if (k == "character") return "角色事件";
            if (k == "cooperation") return "合力事件";
            if (k == "common") return "通用事件";
            return "普通事件";
        }

        /* ── 結果分段 ────────────────────────────────────────────
           記錄器是按「畫面上的行」寫 results 的：一段話被折成 3 行，就會變成 3 筆結果。
           這裡把「明顯是同一段」的行併回一筆：
             · 某一行以標點開頭（，。、；：！？）」』…）＝上一行被切斷；
             · 某一行很長卻沒有句末標點（≥ 20 字）＝下一行是它的續行。
           只要組裡出現任一個訊號，就把整組併成一段。
           空字串代表「段落分隔」：分段處理，不會把兩段黏在一起。 */
        internal static System.Collections.ArrayList NormalizeResults(System.Collections.IList src)
        {
            var outp = new System.Collections.ArrayList();
            if (src == null) return outp;
            var lines = new List<string>();
            foreach (var x in src) lines.Add((Convert.ToString(x) ?? "").Trim());
            var groups = new List<List<string>>();
            var cur = new List<string>();
            foreach (var s in lines)
            {
                if (s.Length == 0)
                {
                    if (cur.Count > 0) { groups.Add(cur); cur = new List<string>(); }
                }
                else cur.Add(s);
            }
            if (cur.Count > 0) groups.Add(cur);
            if (groups.Count == 0)
            {
                if (lines.Count > 0) outp.Add("");
                return outp;
            }
            foreach (var g in groups)
            {
                bool wrapped = false;
                for (int i = 1; i < g.Count && !wrapped; i++)
                {
                    if (StartsContinuation(g[i])) wrapped = true;
                    else if (g[i - 1].Length >= 20 && !EndsSentence(g[i - 1])) wrapped = true;
                }
                if (!wrapped)
                {
                    foreach (var s in g) outp.Add(s);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var s in g) sb.Append(s);
                    outp.Add(sb.ToString());
                }
            }
            return outp;
        }

        static bool StartsContinuation(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            const string marks = "，。、；：！？）」』】·…～,.;:!?)]";
            return marks.IndexOf(s[0]) >= 0;
        }

        static bool EndsSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            const string marks = "。！？…；：!?;:」』】\"”）)";
            return marks.IndexOf(s[s.Length - 1]) >= 0;
        }

        internal static string CleanOptionText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";            return System.Text.RegularExpressions.Regex.Replace(s, "[★☆]", "").Trim();
        }

        internal static void MoveChecksToOptions(Dictionary<string, object> rec)
        {
            if (rec == null) return;
            var steps = rec.ContainsKey("steps") ? rec["steps"] as System.Collections.ArrayList : null;
            if (steps == null || steps.Count == 0) return;
            var s1 = steps[0] as Dictionary<string, object>;
            var options = s1 != null && s1.ContainsKey("options") ? s1["options"] as System.Collections.ArrayList : null;
            if (options == null) return;
            foreach (var oo in options)
            {
                var od = oo as Dictionary<string, object>;
                if (od == null) continue;
                string ct = Convert.ToString(od.ContainsKey("conditionText") ? od["conditionText"] : "");
                if (ct == "")
                {
                    if (od.ContainsKey("check")) continue;
                    ct = Convert.ToString(od.ContainsKey("requirement") ? od["requirement"] : "");
                }
                var ma = System.Text.RegularExpressions.Regex.Match(ct, "(力量|敏捷|體質|体质|感知|智力|魅力)");
                if (!ma.Success) continue;
                string attr = ma.Groups[1].Value == "体质" ? "體質" : ma.Groups[1].Value;
                if (!od.ContainsKey("check") || Convert.ToString(od["check"]) == "") od["check"] = attr;
                if (od.ContainsKey("conditionText")) od.Remove("conditionText");
            }
            var branches = new List<Dictionary<string, object>>();
            for (int i = 1; i < steps.Count; i++)
            {
                var st = steps[i] as Dictionary<string, object>;
                if (st == null) continue;
                var nested = st.ContainsKey("branches") ? st["branches"] as System.Collections.ArrayList : null;
                if (nested != null)
                    foreach (var n in nested) { var nd = n as Dictionary<string, object>; if (nd != null) branches.Add(nd); }
                else branches.Add(st);
            }
            foreach (var b in branches)
            {
                var ck = b.ContainsKey("check") ? b["check"] as Dictionary<string, object> : null;
                if (ck == null) continue;
                string attr = Convert.ToString(ck.ContainsKey("attribute") ? ck["attribute"] : "");
                if (attr == "") continue;
                string choice = Convert.ToString(b.ContainsKey("choice") ? b["choice"] : "");
                if (choice == "") continue;
                Dictionary<string, object> match = null;
                foreach (var oo in options)
                {
                    var od = oo as Dictionary<string, object>;
                    if (od != null && Convert.ToString(od.ContainsKey("text") ? od["text"] : "") == choice) { match = od; break; }
                }
                if (match == null) continue;
                if (!match.ContainsKey("check") || Convert.ToString(match["check"]) == "") match["check"] = attr;
                ck.Remove("attribute");
            }
        }

        static void ApplyFreeChoiceBonus(Dictionary<string, object> rec)
        {
            if (rec == null || Convert.ToString(rec.ContainsKey("eventKind") ? rec["eventKind"] : "") != "free") return;
            var steps = rec.ContainsKey("steps") ? rec["steps"] as System.Collections.ArrayList : null;
            if (steps == null) return;
            var branches = new List<Dictionary<string, object>>();
            for (int i = 1; i < steps.Count; i++)
            {
                var st = steps[i] as Dictionary<string, object>;
                if (st == null) continue;
                var nested = st.ContainsKey("branches") ? st["branches"] as System.Collections.ArrayList : null;
                if (nested != null)
                    foreach (var n in nested) { var nd = n as Dictionary<string, object>; if (nd != null) branches.Add(nd); }
                else branches.Add(st);
            }
            var counts = new Dictionary<string, int>();
            foreach (var b in branches)
            {
                string c = Convert.ToString(b.ContainsKey("choice") ? b["choice"] : "");
                if (c == "") continue;
                counts[c] = (counts.ContainsKey(c) ? counts[c] : 0) + 1;
            }
            foreach (var b in branches)
            {
                string c = Convert.ToString(b.ContainsKey("choice") ? b["choice"] : "");
                if (c == "" || !counts.ContainsKey(c)) continue;
                var ck = b.ContainsKey("check") ? b["check"] as Dictionary<string, object> : null;
                if (ck == null) continue;
                ck["sameChoiceBonus"] = 2 * (counts[c] - 1);
                ck["freeRule"] = "sameChoice";
            }
        }
        public static int ModeVk = 0x51, StepVk = 0x45, CancelVk = 0x43;

        static OcrService Ocr = new OcrService();
        static BusyForm _busy;
        static void ShowBusy(string text) { try { HideBusy(); _busy = new BusyForm(text); _busy.Show(); } catch { } }
        static void HideBusy() { try { if (_busy != null) { _busy.Close(); _busy = null; } } catch { } }
        static KeyboardHook Keys;
        static string Mode = "";
        static int Step = 0;
        static List<Dictionary<string, object>> Steps = new List<Dictionary<string, object>>();
        static string EventId = "";
        static bool OverlayOpen = false;
        static RadialForm Picker;

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                SessionStart = DateTime.Now;
                Native.SetProcessDPIAware();
                Native.DisableComboAnimation();
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--url": if (i + 1 < args.Length) Url = args[++i]; break;
                        case "--logdir": if (i + 1 < args.Length) RecordOut = args[++i]; break;
                        case "--profile": if (i + 1 < args.Length) ProfileDir = args[++i]; break;
                        case "--preset": if (i + 1 < args.Length) Preset = args[++i]; break;
                    }
                }
                LoadHotkeys();
                if (args.Length > 2 && args[0] == "--capturedemo") return CaptureDemo(args[1], args[2], args.Length > 3 ? args[3] : "event");
                if (args.Length > 1 && args[0] == "--checktest") return CheckTest(args[1]);
                if (args.Length > 0 && args[0] == "--parsetest") return ParseTest();
                if (args.Length > 2 && args[0] == "--dimdemo") return DimDemo(args[1], args[2]);
                if (args.Length > 2 && args[0] == "--cropdemo") return CropDemo(args[1], args[2], args.Length > 3 ? args[3] : "event");
                if (args.Length > 1 && args[0] == "--pwtest") return PwTest(args[1]);
                if (args.Length > 0 && args[0] == "--selftest") { RunSelfTest(args.Length > 1 ? args[1] : null); return 0; }
                if (args.Length > 0 && args[0] == "--records") { Application.EnableVisualStyles(); Application.Run(new RecordsForm()); return 0; }
                if (args.Length > 0 && args[0] == "--enddemo") { Application.EnableVisualStyles(); new EndForm("0", "大廳", 2, "天狐").ShowDialog(); return 0; }
                if (args.Length > 0 && args[0] == "--export") { ExportAll(); return 0; }
                if (args.Length > 0 && args[0] == "--shopdemo") { Application.EnableVisualStyles(); new ShopForm(new List<string> { "绷带", "杏仁水", "手电筒" }, null, null).ShowDialog(); return 0; }
                if (args.Length > 0 && args[0] == "--helpui") { Application.EnableVisualStyles(); new HelpForm().ShowDialog(); return 0; }
                if (args.Length > 0 && args[0] == "--keysui") { Application.EnableVisualStyles(); new KeysForm().ShowDialog(); return 0; }
                if (args.Length > 0 && args[0] == "--exportzip") { Application.EnableVisualStyles(); new ExportForm().Export(); return 0; }
                if (args.Length > 0 && args[0] == "--exportui") { Application.EnableVisualStyles(); new ExportForm().ShowDialog(); return 0; }
                if (args.Length > 0 && args[0] == "--dockdemo") { Application.EnableVisualStyles(); long hv = 0; if (args.Length > 1) long.TryParse(args[1], out hv); if (hv != 0) GameHwnd = new IntPtr(hv); else AttachForDemo(); Application.Run(new PendingDock()); return 0; }
                if (args.Length > 0 && args[0] == "--radialdemo")
                {
                    Application.EnableVisualStyles();
                    var rf = new RadialForm();
                    rf.HoldMode = false;
                    rf.Show();
                    Application.Run(rf);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--overlaydemo")
                {
                    Application.EnableVisualStyles();
                    string img = args.Length > 1 ? args[1] : null;
                    if (img == null || !File.Exists(img)) return 2;
                    var shot = new Bitmap(img);
                    if (!Ocr.Start(AppDomain.CurrentDomain.BaseDirectory)) return 3;
                    var lines = Ocr.Recognize(shot);
                    Parse.Classify(lines, shot.Height);
                    Party.Load(AppDomain.CurrentDomain.BaseDirectory);
                    try
            {
                Rectangle pr; double psx, psy;
                Bitmap partyShot = Capture.GrabRegion(GameHwnd, "party", out pr, out psx, out psy);
                if (partyShot != null)
                {
                    var plines = Ocr.Recognize(partyShot);
                    Party.Scan(plines, partyShot.Width);
                    partyShot.Dispose();
                }
            }
            catch { }
                    var of = new OverlayForm(1, lines, new Rectangle(0, 0, shot.Width, shot.Height), IntPtr.Zero, "event");
                    of.Show();
                    Application.Run(of);
                    return 0;
                }
                bool createdNew = false;
                SingleInstance = new Mutex(true, "Local\\ExpeditionRecorder-SingleInstance", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("狐霖勘探記錄器已經在執行中。", "狐霖勘探記錄器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                Party.Load(exeDir);
                ClientPath = Path.Combine(exeDir, "ExpeditionClient.exe");
                if (!File.Exists(ClientPath)) ClientPath = Path.Combine(exeDir, "ExpeditionPlayer.exe");
                if (string.IsNullOrEmpty(RecordOut))
                    RecordOut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Expedition", "logs");
                if (string.IsNullOrEmpty(ProfileDir))
                    ProfileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Expedition", "webview2-profile");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { Log("THREAD " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log("CRASH " + e.ExceptionObject); };
                CreateUi();
                ControlWindow.Show();
                var warm = new Thread(delegate () { try { Ocr.Start(AppDomain.CurrentDomain.BaseDirectory); } catch { } });
                warm.IsBackground = true;
                warm.Start();
                FindClient();
                RefreshStatus();
                Application.Run(ControlWindow);
                try { if (Keys != null) Keys.Uninstall(); } catch { }
                Ocr.Stop();
                return 0;
            }
            catch (Exception ex) { Log("FATAL: " + ex); return 1; }
        }

        /* ── 面板 ─────────────────────────────────────────── */
        static void CreateUi()
        {
            ControlWindow = new Form();
            UiScale.MainWindow = ControlWindow;
            ControlWindow.AutoScaleMode = AutoScaleMode.None;
            ControlWindow.Text = "Expedition Recorder";
            ControlWindow.ClientSize = new Size(430, 460);
            ControlWindow.StartPosition = FormStartPosition.Manual;
            ControlWindow.FormBorderStyle = FormBorderStyle.FixedSingle;
            ControlWindow.MaximizeBox = false;
            ControlWindow.BackColor = Color.FromArgb(28, 28, 31);
            ControlWindow.ForeColor = Color.FromArgb(230, 230, 235);
            var wa = Screen.PrimaryScreen.WorkingArea;
            ControlWindow.Location = new Point(Math.Max(wa.Left + 20, wa.Right - ControlWindow.Width - 24), Math.Max(wa.Top + 20, wa.Top + 60));

            var clientGroup = Group("客戶端", 12, 10, 406, 96);
            var open = Btn("開啟客戶端", 14, 24, 120, 28);
            open.Click += delegate { EnsureClient(true); };
            clientGroup.Controls.Add(open);
            var close = Btn("關閉客戶端", 142, 24, 120, 28);
            close.Click += delegate { CloseClient(); };
            clientGroup.Controls.Add(close);
            var refreshPage = Btn("刷新網頁", 270, 24, 120, 28);
            refreshPage.Click += delegate { RefreshWebPage(); };
            clientGroup.Controls.Add(refreshPage);

            var resLabel = new Label();
            resLabel.SetBounds(14, 64, 58, 20);
            resLabel.ForeColor = Color.FromArgb(200, 200, 206);
            resLabel.Text = "解析度";
            clientGroup.Controls.Add(resLabel);
            var resCombo = new DarkComboBox();
            resCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            UiText.StyleCombo(resCombo);
            resCombo.SetBounds(74, 62, 192, 24);
            resCombo.Items.AddRange(new object[] { "1280x720", "1600x900", "1920x1080", "2400x1350" });
            resCombo.SelectedItem = Preset;
            if (resCombo.SelectedIndex < 0) resCombo.SelectedItem = "1600x900";
            resCombo.SelectedIndexChanged += delegate
            {
                if (resCombo.SelectedItem != null)
                {
                    Preset = Convert.ToString(resCombo.SelectedItem);
                    SaveHotkeys();
                }
            };
            clientGroup.Controls.Add(resCombo);

            CacheBtn = Btn("清理緩存", 274, 62, 116, 24);
            CacheBtn.Click += delegate { CacheConfirmClick(); };
            clientGroup.Controls.Add(CacheBtn);

            CacheResetTimer = new System.Windows.Forms.Timer();
            CacheResetTimer.Interval = 8000;
            CacheResetTimer.Tick += delegate { ResetCacheConfirm(); SetState("已取消清理緩存"); };
            ResetCacheConfirm();

            var recGroup = Group("記錄", 12, 112, 406, 140);
            StepLabel = new Label();
            StepLabel.SetBounds(14, 24, 378, 40);
            StepLabel.ForeColor = Color.FromArgb(232, 232, 236);
            UiText.SetText(StepLabel, "按 " + VkName(ModeVk) + " 開始記錄");
            recGroup.Controls.Add(StepLabel);

            /* Q／E／C 的 GUI 按钮；取消记录和快捷键设置分两排。 */
            QBtn = Btn(VkName(ModeVk) + " 開始記錄", 14, 70, 120, 26);
            QBtn.Click += delegate { OnHotkey(1); };
            recGroup.Controls.Add(QBtn);
            EBtn = Btn(VkName(StepVk) + " 記錄下一步", 140, 70, 120, 26);
            EBtn.Click += delegate { OnHotkey(2); };
            recGroup.Controls.Add(EBtn);
            CancelBtn = Btn(VkName(CancelVk) + " 取消記錄", 266, 70, 120, 26);
            CancelBtn.Click += delegate { CancelRecording(); };
            recGroup.Controls.Add(CancelBtn);

            var keyBtn = Btn("按鍵設定", 14, 102, 120, 26);
            keyBtn.Click += delegate
            {
                if (KeysForm.Instance != null && !KeysForm.Instance.IsDisposed) { KeysForm.Instance.Activate(); return; }
                var kf = new KeysForm(); kf.TopMost = false; kf.Show(); UiScale.SyncOpenForms();
            };
            recGroup.Controls.Add(keyBtn);
            HotkeyBtn = Btn("快捷鍵：開", 142, 102, 120, 26);
            HotkeyBtn.Click += delegate { SetHotkeysEnabled(!HotkeysEnabled); };
            recGroup.Controls.Add(HotkeyBtn);
            CurrentBtn = Btn("本次記錄：開", 270, 102, 120, 26);
            CurrentBtn.Click += delegate { SetCurrentDockEnabled(!CurrentDock.DockEnabled); };
            recGroup.Controls.Add(CurrentBtn);
            UpdateRecordButtons();
            UpdateHotkeyButton();
            UpdateCurrentDockButton();

            var viewGroup = Group("檢視與輸出", 12, 260, 406, 64);
            RecordsBtn = Btn("已記錄", 14, 24, 120, 26);
            RecordsBtn.Click += delegate
            {
                if (RecordsForm.Instance != null && !RecordsForm.Instance.IsDisposed) { RecordsForm.Instance.Activate(); return; }
                var rf = new RecordsForm(); rf.TopMost = false; rf.Show(); UiScale.SyncOpenForms();
            };
            viewGroup.Controls.Add(RecordsBtn);
            PendingBtn = Btn("待處理", 140, 24, 120, 26);
            PendingBtn.Click += delegate { PendingDock.Toggle(); };
            viewGroup.Controls.Add(PendingBtn);
            var exportBtn = Btn("導出", 266, 24, 126, 26);
            exportBtn.Click += delegate
            {
                if (ExportForm.Instance != null && !ExportForm.Instance.IsDisposed) { ExportForm.Instance.Activate(); return; }
                var ef = new ExportForm(); ef.TopMost = false; ef.Show(); UiScale.SyncOpenForms();
            };
            viewGroup.Controls.Add(exportBtn);

            StateLabel = new Label();
            StateLabel.SetBounds(14, 332, 402, 26);
            StateLabel.ForeColor = Color.FromArgb(232, 232, 236);
            UiText.SetText(StateLabel, "狀態：—");
            ControlWindow.Controls.Add(StateLabel);

            var settingsGroup = Group("設置", 12, 360, 406, 88);
            HelpBtn = Btn("說明", 14, 22, 80, 26);
            HelpBtn.Click += delegate
            {
                if (HelpForm.Instance != null && !HelpForm.Instance.IsDisposed) { HelpForm.Instance.Activate(); return; }
                var hf = new HelpForm(); hf.TopMost = false; hf.Show(); UiScale.SyncOpenForms();
            };
            settingsGroup.Controls.Add(HelpBtn);
            TradBtn = Btn("繁體", 102, 22, 84, 26);
            TradBtn.Click += delegate { SetUiLang(UiLang.Traditional); };
            settingsGroup.Controls.Add(TradBtn);
            SimpBtn = Btn("簡體", 192, 22, 84, 26);
            SimpBtn.Click += delegate { SetUiLang(UiLang.Simplified); };
            settingsGroup.Controls.Add(SimpBtn);
            EngBtn = Btn("English", 282, 22, 108, 26);
            EngBtn.Click += delegate { SetUiLang(UiLang.English); };
            settingsGroup.Controls.Add(EngBtn);

            ScaleLabel = new Label();
            ScaleLabel.SetBounds(14, 62, 42, 22);
            ScaleLabel.ForeColor = Color.FromArgb(210, 210, 218);
            UiText.SetText(ScaleLabel, "字號");
            settingsGroup.Controls.Add(ScaleLabel);

            ScaleBtns = new Button[UiScale.Options.Length];
            for (int i = 0; i < UiScale.Options.Length; i++)
            {
                float option = UiScale.Options[i];
                var b = Btn(option.ToString("0.#") + "x", 60 + i * 84, 56, 78, 28);
                b.Tag = option;
                b.Click += delegate { SetTextScale((float)b.Tag); };
                ScaleBtns[i] = b;
                settingsGroup.Controls.Add(b);
            }
            UpdateLanguageButtons();
            UpdateScaleLabel();
            /* 載入先前儲存的語言；靜態控制項在建構時是中文，這裡統一轉換。 */
            UiText.ApplyTo(ControlWindow);
            UpdateLanguageButtons();
            UpdateScaleLabel();
            UpdateRecordButtons();
            UpdateHotkeyButton();

            ControlWindow.FormClosing += delegate
            {
                Closing = true;
                Native.RestoreComboAnimation();
                try { if (CacheResetTimer != null) CacheResetTimer.Stop(); } catch { }
                try { if (Keys != null) Keys.Uninstall(); } catch { }
                try { if (GameHwnd != IntPtr.Zero) Native.EnableWindow(GameHwnd, true); } catch { }
                Ocr.Stop();
            };

            Keys = new KeyboardHook();
            Keys.ModeVk = ModeVk;
            Keys.StepVk = StepVk;
            Keys.CancelVk = CancelVk;
            Keys.Enabled = HotkeysEnabled;
            Keys.GetGame = delegate { return GameHwnd; };
            Keys.OnTrigger = delegate (int vk)
            {
                if (vk == ModeVk)
                {
                    if (Mode == "")
                    {
                        /* 不在鉤子回呼裡直接開窗：建立 RadialForm 可能要上百毫秒，
                           超過 LowLevelHooksTimeout 的話 Windows 會把鉤子移除。 */
                        if (Picker == null && ControlWindow != null && ControlWindow.IsHandleCreated && !ControlWindow.IsDisposed)
                            ControlWindow.BeginInvoke((Action)delegate { if (Picker == null && !OverlayOpen) ShowPicker(); });
                    }
                    else if (ControlWindow != null && ControlWindow.IsHandleCreated && !ControlWindow.IsDisposed)
                        ControlWindow.BeginInvoke((Action)delegate { FinishEvent(); });
                    return;
                }
                if (vk == CancelVk)
                {
                    if (ControlWindow != null && ControlWindow.IsHandleCreated && !ControlWindow.IsDisposed)
                        ControlWindow.BeginInvoke((Action)delegate { OnHotkey(3); });
                    return;
                }
                if (ControlWindow != null && ControlWindow.IsHandleCreated && !ControlWindow.IsDisposed)
                    ControlWindow.BeginInvoke((Action)delegate { OnHotkey(2); });
            };
            Keys.OnRelease = delegate (int vk)
            {
                if (vk != ModeVk) return;
                var p = Picker;
                if (p != null && !p.IsDisposed) p.Release();
            };
            Keys.EscapeHandler = delegate
            {
                var p = Picker;
                if (p == null || p.IsDisposed) return false;
                p.Commit();
                return true;
            };
            Keys.Install();

            UiTimer = new System.Windows.Forms.Timer();
            UiTimer.Interval = 500;
            UiTimer.Tick += delegate { UiText.ApplyOpenForms(); RefreshStatus(); HookWatchdog(); };
            UiTimer.Start();

            /* 预热 OCR 服务，并让空闲时的队伍扫描提前完成，避免第一次 E
               才启动服务/扫队伍。 */
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { if (!Ocr.Ready) Ocr.Start(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            });
            Application.Idle += delegate { UiScale.SyncOpenForms(); };
        }

        static GroupBox Group(string text, int x, int y, int w, int h)
        {
            var g = new GroupBox();
            g.Text = text;
            g.SetBounds(x, y, w, h);
            g.ForeColor = Color.FromArgb(150, 150, 156);
            ControlWindow.Controls.Add(g);
            return g;
        }

        static Button Btn(string text, int x, int y, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Color.FromArgb(44, 44, 47);
            b.ForeColor = Color.FromArgb(232, 232, 236);
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 63);
            b.Font = new Font("Microsoft JhengHei UI", 9F);
            return b;
        }

        static void SetState(string text)
        {
            try
            {
                if (ControlWindow == null || ControlWindow.IsDisposed) return;
                if (ControlWindow.InvokeRequired) { ControlWindow.BeginInvoke((Action)delegate { SetState(text); }); return; }
                UiText.SetText(StateLabel, text);
                UpdateRecordButtons();
            }
            catch { }
        }

        /* Q 按钮随记录状态切换文字与高亮；Q/E/C 的字母跟随按键设置。 */
        static void UpdateRecordButtons()
        {
            try
            {
                if (ControlWindow != null && ControlWindow.InvokeRequired)
                {
                    ControlWindow.BeginInvoke((Action)UpdateRecordButtons);
                    return;
                }
                bool recording = Mode != "";
                string qText = VkName(ModeVk) + (recording ? " 結束記錄" : " 開始記錄");
                string eText = VkName(StepVk) + " 記錄下一步";
                string cText = VkName(CancelVk) + " 取消記錄";
                if (QBtn != null)
                {
                    UiText.SetText(QBtn, qText);
                    QBtn.BackColor = recording ? Color.FromArgb(96, 74, 38) : Color.FromArgb(44, 44, 47);
                    QBtn.ForeColor = recording ? Color.FromArgb(255, 232, 160) : Color.FromArgb(232, 232, 236);
                    QBtn.FlatAppearance.BorderColor = recording ? Color.FromArgb(230, 184, 72) : Color.FromArgb(78, 78, 84);
                }
                if (EBtn != null) UiText.SetText(EBtn, eText);
                if (CancelBtn != null) UiText.SetText(CancelBtn, cText);
                UpdateCurrentDockButton();
            }
            catch { }
        }

        static void SetHotkeysEnabled(bool on)
        {
            HotkeysEnabled = on;
            if (Keys != null) { Keys.Enabled = on; Keys.ResetDown(); }
            if (!on && Picker != null)
            {
                var p = Picker; Picker = null;
                try { p.Close(); } catch { }
            }
            UpdateHotkeyButton();
            SaveHotkeys();
            SetState(on ? "快捷鍵已啟用（Q/E/C 可用）" : "快捷鍵已暫停（Q/E/C 直接給遊戲）");
        }

        static void SetCurrentDockEnabled(bool on)
        {
            if (on) CurrentDock.ShowDock(); else CurrentDock.HideDock();
            UpdateCurrentDockButton();
            SaveHotkeys();
            SetState(on ? "本次記錄：開" : "本次記錄：關");
        }

        internal static void UpdateCurrentDockButton()
        {
            try
            {
                if (CurrentBtn == null) return;
                bool on = CurrentDock.DockEnabled;
                UiText.SetText(CurrentBtn, on ? "本次記錄：開" : "本次記錄：關");
                CurrentBtn.BackColor = on ? Color.FromArgb(42, 72, 48) : Color.FromArgb(76, 44, 44);
                CurrentBtn.ForeColor = on ? Color.FromArgb(190, 240, 190) : Color.FromArgb(255, 190, 180);
                CurrentBtn.FlatAppearance.BorderColor = on ? Color.FromArgb(80, 160, 90) : Color.FromArgb(180, 80, 70);
            }
            catch { }
        }

        static void UpdateHotkeyButton()
        {
            try
            {
                if (HotkeyBtn == null) return;
                UiText.SetText(HotkeyBtn, HotkeysEnabled ? "快捷鍵：開" : "快捷鍵：關");
                HotkeyBtn.BackColor = HotkeysEnabled ? Color.FromArgb(42, 72, 48) : Color.FromArgb(76, 44, 44);
                HotkeyBtn.ForeColor = HotkeysEnabled ? Color.FromArgb(190, 240, 190) : Color.FromArgb(255, 190, 180);
                HotkeyBtn.FlatAppearance.BorderColor = HotkeysEnabled ? Color.FromArgb(80, 160, 90) : Color.FromArgb(180, 80, 70);
            }
            catch { }
        }

        static void UpdateLanguageButtons()
        {
            try
            {
                UiText.SetText(HelpBtn, "說明");
                UiText.KeepText(TradBtn);
                UiText.KeepText(SimpBtn);
                UiText.KeepText(EngBtn);
                TradBtn.Text = "繁體";
                SimpBtn.Text = "简体";
                EngBtn.Text = "English";
                var buttons = new[] { TradBtn, SimpBtn, EngBtn };
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] == null) continue;
                    buttons[i].BackColor = Color.FromArgb(44, 44, 47);
                    buttons[i].ForeColor = Color.FromArgb(232, 232, 236);
                    buttons[i].FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
                }
                Button active = UiText.Lang == UiLang.Traditional ? TradBtn
                    : UiText.Lang == UiLang.Simplified ? SimpBtn : EngBtn;
                if (active != null)
                {
                    active.BackColor = Color.FromArgb(42, 72, 48);
                    active.ForeColor = Color.FromArgb(190, 240, 190);
                    active.FlatAppearance.BorderColor = Color.FromArgb(80, 160, 90);
                }
            }
            catch { }
        }

        static void SetTextScale(float value)
        {
            UiScale.SetScale(value);
            UpdateScaleLabel();
            SaveHotkeys();
        }

        static void UpdateScaleLabel()
        {
            if (ScaleLabel != null) UiText.SetText(ScaleLabel, "字號");
            if (ScaleBtns == null) return;
            float selected = UiScale.NearestScale(UiScale.Scale);
            foreach (var b in ScaleBtns)
            {
                if (b == null) continue;
                bool on = Math.Abs(Convert.ToSingle(b.Tag) - selected) < 0.001f;
                b.BackColor = on ? Color.FromArgb(70, 74, 82) : Color.FromArgb(44, 44, 47);
                b.ForeColor = on ? Color.White : Color.FromArgb(232, 232, 236);
                b.FlatAppearance.BorderColor = on ? Color.FromArgb(130, 170, 215) : Color.FromArgb(78, 78, 84);
            }
        }

        static void SetUiLang(UiLang lang)
        {
            UiText.SetLang(lang);
            if (HelpForm.Instance != null && !HelpForm.Instance.IsDisposed) HelpForm.Instance.RefreshLanguage();
            UpdateLanguageButtons();
            UpdateScaleLabel();
            UpdateRecordButtons();
            UpdateHotkeyButton();
            SaveHotkeys();
            SetState(lang == UiLang.Simplified ? "狀態：已切換成簡體"
                : lang == UiLang.English ? "狀態：已切換成英文"
                : "狀態：已切換成繁體");
        }

        static int _lastFindClientTick;
        static void RefreshStatus()
        {
            try
            {
                /* 没接上 client 时不要每 500ms 枚举全部进程：
                   FindClient 会读 MainModule，UI 线程会被拖到 100% CPU。 */
                if (GameHwnd == IntPtr.Zero && unchecked(Environment.TickCount - _lastFindClientTick) > 5000)
                {
                    _lastFindClientTick = Environment.TickCount;
                    try { FindClient(); } catch { }
                }
                if (GameHwnd != IntPtr.Zero && !Native.IsWindow(GameHwnd))
                {
                    GameHwnd = IntPtr.Zero;
                    Client = null;
                    TypingState.Clear();
                }
                string state = Mode == ""
                    ? (GameHwnd != IntPtr.Zero ? "狀態：客戶端已開，按 " + VkName(ModeVk) + " 開始" : "狀態：客戶端未接上（點「開啟客戶端」可接上）")
                    : "狀態：事件模式（已記 " + Step + " 步）";
                UiText.SetText(StateLabel, state);
                int n = 0, p = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(DataDir(), "*.jsonl"))
                    {
                        string fn = Path.GetFileName(f);
                        int c = CountCached(f);
                        if (fn.StartsWith("pending-")) p += c;
                        else if (!fn.StartsWith("draft-") && !fn.StartsWith("floor-lengths-")) n += c;
                    }
                }
                catch { }
                string recText = "已記錄 " + n, pendText = "待處理 " + p;
                if (RecordsBtn != null) UiText.SetText(RecordsBtn, recText);
                if (PendingBtn != null) UiText.SetText(PendingBtn, pendText);
                if (GameHwnd != IntPtr.Zero && Mode == "") ScanPartyAsync();
            }
            catch { }
        }

        /* 每 500ms 重讀所有 jsonl 會把 UI 執行緒卡住，鉤子跟著漏按鍵。
           檔案長度與寫入時間沒變就直接用上次的結果。 */
        static readonly Dictionary<string, long> CountKey = new Dictionary<string, long>();
        static readonly Dictionary<string, int> CountVal = new Dictionary<string, int>();

        static int CountCached(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                long key = fi.Length * 1000003L + fi.LastWriteTimeUtc.Ticks % 1000003L;
                long cachedKey;
                int cachedVal;
                if (CountKey.TryGetValue(path, out cachedKey) && cachedKey == key && CountVal.TryGetValue(path, out cachedVal)) return cachedVal;
                int c = File.ReadAllLines(path).Length;
                CountKey[path] = key;
                CountVal[path] = c;
                return c;
            }
            catch { return 0; }
        }

        /* 低級鉤子的保險絲：
           1) 回呼太慢時 Windows 會靜默移除鉤子 → 立刻重掛；
           2) 每 30 秒重掛一次，擋掉任何原因造成的「再也收不到按鍵」；
           3) 漏接 keyup 卡住的按鍵，超過 3 秒放掉。 */
        static void HookWatchdog()
        {
            try
            {
                if (Keys == null) return;
                if (Keys.Slow || !Keys.Installed)
                {
                    bool ok = Keys.Reinstall();
                    LogDiag("hook reinstalled slow=" + Keys.Slow + " ok=" + ok);
                    Keys.Slow = false;
                }
                else if (unchecked(Environment.TickCount - Keys.InstalledTick) > 30000)
                {
                    Keys.Reinstall();
                    LogDiag("hook periodic reinstall");
                }
                Keys.ClearStuckDown(3000);
            }
            catch (Exception ex) { LogDiag("hook watchdog failed: " + ex.Message); }
        }

        static void LogDiag(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hook-watchdog.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        /* ── 客戶端 ───────────────────────────────────────── */
        static bool EnsureClient(bool launch)
        {
            if (GameHwnd != IntPtr.Zero && Native.IsWindow(GameHwnd)) return true;
            if (FindClient()) return true;
            if (!launch) return false;
            return StartClient();
        }

        static IntPtr WindowOfProcess(Process p)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
            }
            catch { }
            IntPtr found = IntPtr.Zero;
            try
            {
                int pid = p.Id;
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    uint wp;
                    Native.GetWindowThreadProcessId(h, out wp);
                    if ((int)wp == pid && Native.IsWindowVisible(h)) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        static bool AttachClient(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return false;
                if (IsBrowserProcess(p)) return false;
                p.Refresh();
                IntPtr h = WindowOfProcess(p);
                if (h == IntPtr.Zero || !Native.IsWindow(h)) return false;
                Client = p;
                GameHwnd = h;
                TypingState.Clear();
                ClientOwned = false;
                SetState("狀態：已接上客戶端");
                try { ScanPartyAsync(); } catch { }
                try { if (CurrentDock.DockEnabled) CurrentDock.ShowDock(); } catch { }
                return true;
            }
            catch { return false; }
        }

        static bool IsBrowserProcess(Process p)
        {
            string n = (p.ProcessName ?? "").ToLowerInvariant();
            if (n.Contains("browser") || n.Contains("chrome") || n.Contains("edge") ||
                n.Contains("firefox") || n.Contains("opera") || n.Contains("brave") ||
                n.Contains("vivaldi") || n.Contains("360se") || n.Contains("360chrome") ||
                n.Contains("qqbrowser") || n.Contains("sogou") || n.Contains("liebao") ||
                n.Contains("maxthon") || n.Contains("ucbrowser") || n.Contains("2345"))
                return true;
            switch (n)
            {
                case "iexplore":
                case "safari":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLikelyClientProcess(Process p)
        {
            if (p == null || IsBrowserProcess(p)) return false;
            string n = (p.ProcessName ?? "").ToLowerInvariant();
            if (n.Contains("recorder")) return false;
            if (n.Contains("expedition") || n.Contains("hulin") || n.Contains("foyer")) return true;
            try
            {
                string exeDir = Path.GetDirectoryName(Path.GetFullPath(ClientPath));
                string procDir = Path.GetDirectoryName(Path.GetFullPath(p.MainModule.FileName));
                return string.Equals(exeDir, procDir, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static int _lastDeepFindClientTick;
        static bool FindClient()
        {
            try
            {
                string[] names = { Path.GetFileNameWithoutExtension(ClientPath), "ExpeditionClient", "ExpeditionPlayer", "HulinClient", "HulinPlayer", "FoyerClient", "FoyerPlayer" };
                foreach (string n in names)
                {
                    foreach (var p in Process.GetProcessesByName(n))
                        if (AttachClient(p)) return true;
                }
                /* 全进程扫描读 MainModule，代价很高；最多 10 秒一次。 */
                if (unchecked(Environment.TickCount - _lastDeepFindClientTick) < 10000) return false;
                _lastDeepFindClientTick = Environment.TickCount;
                int myPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == myPid) continue;
                        string title = p.MainWindowTitle ?? "";
                        if (title.IndexOf("後室", StringComparison.Ordinal) < 0 &&
                            title.IndexOf("Backrooms", StringComparison.OrdinalIgnoreCase) < 0 &&
                            title.IndexOf("狐霖", StringComparison.Ordinal) < 0) continue;
                        if (!IsLikelyClientProcess(p)) continue;
                        if (AttachClient(p)) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        static bool ClientProcessExists()
        {
            string[] names = { Path.GetFileNameWithoutExtension(ClientPath), "ExpeditionClient", "ExpeditionPlayer", "HulinClient", "HulinPlayer", "FoyerClient", "FoyerPlayer" };
            foreach (string n in names)
            {
                try { if (Process.GetProcessesByName(n).Length > 0) return true; } catch { }
            }
            return false;
        }

        static bool StartClient()
        {
            try
            {
                if (ClientProcessExists())
                {
                    SetState("狀態：已偵測到客戶端，等待視窗就緒…");
                    for (int i = 0; i < 20; i++)
                    {
                        Thread.Sleep(500);
                        Application.DoEvents();
                        if (FindClient()) return true;
                    }
                    SetState("狀態：客戶端程序存在，但找不到視窗");
                    return false;
                }
                if (!File.Exists(ClientPath)) { SetState("找不到 ExpeditionClient.exe"); return false; }
                Directory.CreateDirectory(ProfileDir);
                string ps = "--client-only --url \"" + Url.Replace("\"", "") + "\" --preset " + Preset + " --no-record"
                    + " --logdir \"" + RecordOut.Replace("\"", "") + "\" --profile \"" + ProfileDir.Replace("\"", "") + "\"";
                var psi = new ProcessStartInfo(ClientPath, ps);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(ClientPath);
                Client = Process.Start(psi);
                ClientOwned = true;
                for (int i = 0; i < 40; i++)
                {
                    Thread.Sleep(500);
                    Application.DoEvents();
                    if (Client == null || Client.HasExited) break;
                    Client.Refresh();
                    if (Client.MainWindowHandle != IntPtr.Zero && Native.IsWindow(Client.MainWindowHandle))
                    {
                        GameHwnd = Client.MainWindowHandle;
                        SetState("狀態：客戶端已開");
                        return true;
                    }
                }
                SetState("客戶端沒有成功開啟");
                return false;
            }
            catch (Exception ex)
            {
                Log("start client: " + ex);
                SetState("開啟客戶端失敗");
                return false;
            }
        }

        static void CloseClient()
        {
            try
            {
                if (GameHwnd != IntPtr.Zero) Native.PostMessage(GameHwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (Client != null && !Client.HasExited)
                {
                    if (!Client.WaitForExit(3000) && ClientOwned) { try { Client.Kill(); } catch { } }
                }
            }
            catch { }
            GameHwnd = IntPtr.Zero;
            Client = null;
            ClientOwned = false;
            SetState("狀態：客戶端已關閉");
        }

        static void RefreshWebPage()
        {
            try
            {
                if (GameHwnd == IntPtr.Zero) EnsureClient(false);
                if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
                Native.SetForegroundWindow(GameHwnd);
                System.Threading.Thread.Sleep(80);
                Application.DoEvents();
                if (Native.GetForegroundWindow() == GameHwnd)
                {
                    SendKeys.SendWait("{F5}");
                    SetState("已刷新網頁");
                    return;
                }
                /* 焦點搶不到時，直接把 F5 送進 WebView 的 render 子視窗。 */
                IntPtr target = Capture.RenderChild(GameHwnd);
                if (target == IntPtr.Zero) target = GameHwnd;
                Native.PostMessage(target, 0x0100, (IntPtr)0x74, IntPtr.Zero);
                Native.PostMessage(target, 0x0101, (IntPtr)0x74, IntPtr.Zero);
                SetState("已送出刷新");
            }
            catch (Exception ex) { SetState("刷新失敗：" + ex.Message); }
        }

        static void ResetCacheConfirm()
        {
            CacheConfirm = 0;
            try { if (CacheResetTimer != null) CacheResetTimer.Stop(); } catch { }
            if (CacheBtn != null) UiText.SetText(CacheBtn, "清理緩存");
        }

        static void CacheConfirmClick()
        {
            if (CacheConfirm == 0)
            {
                CacheConfirm = 1;
                UiText.SetText(CacheBtn, "再按一次確認");
                SetState("清理緩存：再按兩下確認（會先關閉遊戲，保留登入與存檔）");
                if (CacheResetTimer != null) { CacheResetTimer.Stop(); CacheResetTimer.Start(); }
                return;
            }
            if (CacheConfirm == 1)
            {
                CacheConfirm = 2;
                UiText.SetText(CacheBtn, "最後確認");
                SetState("清理緩存：再按一下確認");
                if (CacheResetTimer != null) { CacheResetTimer.Stop(); CacheResetTimer.Start(); }
                return;
            }
            ResetCacheConfirm();
            ClearWebCache();
        }

        static bool DeleteCacheDir(string path, ref int removed, ref int failed)
        {
            if (!Directory.Exists(path)) return true;
            try
            {
                Directory.Delete(path, true);
                removed++;
                return true;
            }
            catch
            {
                failed++;
                return false;
            }
        }

        static void ClearWebCache()
        {
            try
            {
                if (ClientProcessExists() || GameHwnd != IntPtr.Zero)
                {
                    SetState("清理緩存：關閉客戶端…");
                    if (GameHwnd == IntPtr.Zero) EnsureClient(false);
                    CloseClient();
                    for (int i = 0; i < 15 && ClientProcessExists(); i++)
                    {
                        System.Threading.Thread.Sleep(200);
                        Application.DoEvents();
                    }
                    if (ClientProcessExists())
                    {
                        SetState("清理失敗：請先手動關閉客戶端");
                        return;
                    }
                }

                SetState("清理緩存中…");
                Application.DoEvents();
                string profile = ProfileDir;
                if (string.IsNullOrEmpty(profile))
                    profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Expedition", "webview2-profile");
                string root = Path.Combine(profile, "EBWebView");
                string def = Path.Combine(root, "Default");
                int removed = 0, failed = 0;
                foreach (string name in new[] { "GPUCache", "GrShaderCache", "ShaderCache", "GPUPersistentCache" })
                    DeleteCacheDir(Path.Combine(root, name), ref removed, ref failed);
                foreach (string name in new[] { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache", "Service Worker\\CacheStorage" })
                    DeleteCacheDir(Path.Combine(def, name), ref removed, ref failed);
                if (failed > 0)
                    SetState("已清理 " + removed + " 項緩存；" + failed + " 項被鎖定");
                else
                    SetState("已清理緩存（保留登入與存檔）；按「開啟客戶端」重開");
            }
            catch (Exception ex) { SetState("清理緩存失敗：" + ex.Message); }
        }

        /* ── 事件記錄 ─────────────────────────────────────── */
        static void OnHotkey(int id)
        {
            if (OverlayOpen) return;
            if (id == 3) { CancelRecording(); return; }
            if (id == 1)
            {
                if (Mode == "") { if (Picker == null) ShowPicker(); }
                else FinishEvent();
                return;
            }
            if (Mode == "") PickMode("event");
            if (Mode == "event") CaptureStep();
            else if (Mode == "item") CaptureItem();
            else if (Mode == "floor") CaptureFloor();
            else if (Mode == "exit") CaptureExit();
            else if (Mode == "ending") CaptureEnding();
            else if (Mode == "shop") CaptureShop();
        }

        static void ShowPicker()
        {
            var p = new RadialForm();
            Picker = p;
            p.FormClosed += delegate
            {
                if (Picker == p) Picker = null;
                if (p.Picked != "") PickMode(p.Picked);
            };
            p.Show();
        }

        static void PickMode(string m)
        {
            Mode = m;
            Step = 0;
            Steps.Clear();
            EventId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var p = Picker;
            Picker = null;
            if (p != null && !p.IsDisposed) { try { p.Close(); } catch { } }
            string hint;
            if (Mode == "item") hint = "物品模式：按 " + VkName(StepVk) + " 收當前物品；按 " + VkName(ModeVk) + " 結束";
            else if (Mode == "floor") hint = "樓層介紹：按 " + VkName(StepVk) + " 拖樓層名＋介紹；按 " + VkName(ModeVk) + " 選亮/暗";
            else if (Mode == "exit") hint = "出口：按 " + VkName(StepVk) + " 拖出口事件＋介紹；再按 " + VkName(StepVk) + " 拖通往文本；按 " + VkName(ModeVk) + " 輸入目標";
            else if (Mode == "ending") hint = "結局：按 " + VkName(StepVk) + " 拖結局名＋通用介紹；再按 " + VkName(StepVk) + " 拖後日談；按 " + VkName(ModeVk) + " 選分類";
            else if (Mode == "shop") hint = "商店模式：按 " + VkName(StepVk) + " 掃描貨架或商店文字；按 " + VkName(ModeVk) + " 結束";
            else hint = "事件模式：按 " + VkName(StepVk) + " 記第 1 步；按 " + VkName(ModeVk) + " 結束";
            UiText.SetText(StepLabel, hint);
            SetState("狀態：" + hint);
        }

        static void ScaleLines(List<OcrLine> lines, double sx, double sy)
        {
            if (Math.Abs(sx - 1) < 0.001 && Math.Abs(sy - 1) < 0.001) return;
            foreach (var l in lines)
            {
                l.X = (int)Math.Round(l.X * sx);
                l.Y = (int)Math.Round(l.Y * sy);
                l.W = Math.Max(1, (int)Math.Round(l.W * sx));
                l.H = Math.Max(1, (int)Math.Round(l.H * sy));
            }
        }

        static DialogResult RunOverlay(OverlayForm f)
        {
            bool disabled = false;
            OverlayOpen = true;
            if (Keys != null)
            {
                Keys.OverlayCancel = delegate
                {
                    try
                    {
                        Action close = delegate
                        {
                            if (!f.IsDisposed) { f.DialogResult = DialogResult.Cancel; f.Close(); }
                        };
                        if (f.IsHandleCreated) f.BeginInvoke(close);
                        else close();
                    }
                    catch { }
                };
            }
            try
            {
                try
                {
                    if (Native.IsWindow(GameHwnd)) { Native.EnableWindow(GameHwnd, false); disabled = true; }
                }
                catch { }
                return f.ShowDialog();
            }
            finally
            {
                OverlayOpen = false;
                try { if (Keys != null) Keys.OverlayCancel = null; } catch { }
                try { if (Keys != null) { Keys.ResetDown(); Keys.Reinstall(); } } catch { }
                try { if (disabled && Native.IsWindow(GameHwnd)) Native.EnableWindow(GameHwnd, true); } catch { }
                try { if (Native.IsWindow(GameHwnd)) Native.SetForegroundWindow(GameHwnd); } catch { }
            }
        }

        static DateTime _partyScanned = DateTime.MinValue;
        static int _partyScanning;
        static void ScanPartyAsync()
        {
            if (!Ocr.Ready || GameHwnd == IntPtr.Zero || OverlayOpen) return;
            if (Interlocked.CompareExchange(ref _partyScanning, 1, 0) != 0) return;
            try
            {
                if ((DateTime.Now - _partyScanned).TotalSeconds < 30)
                {
                    Interlocked.Exchange(ref _partyScanning, 0);
                    return;
                }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { ScanParty(); }
                    finally { Interlocked.Exchange(ref _partyScanning, 0); }
                });
            }
            catch { Interlocked.Exchange(ref _partyScanning, 0); }
        }

        /* 同步版本只给后台线程/演示模式用；E 的响应链路不再调用它。 */
        static void ScanParty()
        {
            if ((DateTime.Now - _partyScanned).TotalSeconds < 30) return;
            try
            {
                Rectangle pr; double px, py;
                Bitmap b = Capture.GrabRegion(GameHwnd, "party", out pr, out px, out py);
                if (b == null) return;
                int bw = b.Width;
                var pl = Ocr.Recognize(b);
                b.Dispose();
                if (pl.Count == 0) return;
                Party.Scan(pl, bw);
                _partyScanned = DateTime.Now;
            }
            catch { }
        }

        static bool EnsureOcr()
        {
            if (Ocr.Ready) return true;
            Ocr.Start(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 50 && !Ocr.Ready; i++)
            {
                Thread.Sleep(100);
                Application.DoEvents();
            }
            return Ocr.Ready;
        }

        public static Stopwatch CaptureSw;
        internal static void PerfLog(string stage, long ms, string extra)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + stage + " " + ms + "ms " + (extra ?? "") + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        /* 只把有文字/高对比内容的区域送 OCR；坐标随后加回原图。 */
        static Rectangle FindTextBounds(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int bgR = 0, bgG = 0, bgB = 0;
            int[,] pts = { { 2, 2 }, { w - 3, 2 }, { 2, h - 3 }, { w - 3, h - 3 } };
            for (int i = 0; i < 4; i++)
            {
                Color c;
                try { c = bmp.GetPixel(pts[i, 0], pts[i, 1]); } catch { c = Color.Black; }
                bgR += c.R; bgG += c.G; bgB += c.B;
            }
            bgR /= 4; bgG /= 4; bgB /= 4;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            BitmapData data;
            try { data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb); }
            catch { return new Rectangle(0, 0, w, h); }
            try
            {
                int stride = data.Stride;
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y += 2)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, w * 4);
                    for (int x = 0; x < w; x += 2)
                    {
                        int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                        int diff = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
                        int mx = Math.Max(r, Math.Max(g, b));
                        /* 只认明亮的文字/图标；暗色背景、深色面板边框不算，
                           否则整张图都会被当成内容，裁切就失去意义。 */
                        if (mx < 165) continue;
                        if (diff < 110 && mx < 215) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            if (maxX < minX || maxY < minY) return new Rectangle(0, 0, w, h);
            int pad = 12;
            int x0 = Math.Max(0, minX - pad), y0 = Math.Max(0, minY - pad);
            int x1 = Math.Min(w - 1, maxX + pad), y1 = Math.Min(h - 1, maxY + pad);
            var crop = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            if (crop.Width < 24 || crop.Height < 24) return new Rectangle(0, 0, w, h);
            /* 省不到 10% 就不裁，避免白做一次 Clone 和坐标换算。 */
            if (crop.Width > w * 0.9 && crop.Height > h * 0.9) return new Rectangle(0, 0, w, h);
            return crop;
        }

        static void CaptureStep()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            ShowBusy("辨識中…");
            var sw = Stopwatch.StartNew();
            CaptureSw = sw;
            long last = 0;
            Application.DoEvents();

            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "event", out rect, out sx, out sy);
            if (shot == null) { HideBusy(); SetState("截圖失敗（PrintWindow）"); return; }
            PerfLog("grab", sw.ElapsedMilliseconds - last, shot.Width + "x" + shot.Height); last = sw.ElapsedMilliseconds;
            if (!EnsureOcr())
            {
                shot.Dispose();
                HideBusy();
                SetState("OCR 服務還在啟動，稍後再按 " + VkName(StepVk));
                return;
            }
            Rectangle crop = FindTextBounds(shot);
            Bitmap ocrShot = shot;
            if (crop.Width < shot.Width || crop.Height < shot.Height)
            {
                try { ocrShot = shot.Clone(crop, PixelFormat.Format32bppArgb); }
                catch { ocrShot = shot; crop = new Rectangle(0, 0, shot.Width, shot.Height); }
            }
            PerfLog("crop", sw.ElapsedMilliseconds - last, crop.Width + "x" + crop.Height); last = sw.ElapsedMilliseconds;
            var lines = Ocr.Recognize(ocrShot);
            if (ocrShot != shot) ocrShot.Dispose();
            PerfLog("event-ocr", sw.ElapsedMilliseconds - last, "lines=" + lines.Count); last = sw.ElapsedMilliseconds;
            if (lines.Count == 0)
            {
                shot.Dispose();
                HideBusy();
                SetState("OCR 沒有結果");
                return;
            }
            if (crop.X != 0 || crop.Y != 0)
                foreach (var l in lines) { l.X += crop.X; l.Y += crop.Y; }
            Parse.Classify(lines, shot.Height);
            PerfLog("parse", sw.ElapsedMilliseconds - last, "lines=" + lines.Count); last = sw.ElapsedMilliseconds;
            ScaleLines(lines, sx, sy);
            HideBusy();
            var f = new OverlayForm(Step + 1, lines, rect, GameHwnd, "event");
            PerfLog("overlay-show", sw.ElapsedMilliseconds - last, "");
            var r = RunOverlay(f);
            PerfLog("overlay-close", sw.ElapsedMilliseconds - last, "result=" + r);
            if (r == DialogResult.OK && f.Result != null)
            {
                Step++;
                Steps.Add(f.Result);
                WriteDraft(EventId, f.Result);
                UiText.SetText(StepLabel, "已記 " + Step + " 步");
                SetState("第 " + Step + " 步已存（辨識 " + sw.ElapsedMilliseconds + "ms）；按 " + VkName(StepVk) + " 記第 " + (Step + 1) + " 步");
            }
            else SetState("這一步已取消（" + sw.ElapsedMilliseconds + "ms）；按 " + VkName(StepVk) + " 重記");
            f.Dispose();
            shot.Dispose();
        }

        static void CaptureItem()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            SetState("狀態：OCR 中…");
            Application.DoEvents();

            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "item", out rect, out sx, out sy);
            if (shot == null) { SetState("截圖失敗（PrintWindow）"); return; }
            if (!Ocr.Ready && !Ocr.Start(AppDomain.CurrentDomain.BaseDirectory))
            {
                shot.Dispose();
                SetState("OCR 服務啟動失敗");
                return;
            }
            var lines = Ocr.Recognize(shot);
            if (lines.Count == 0)
            {
                shot.Dispose();
                SetState("OCR 沒有結果");
                return;
            }
            var item = ParseItem(lines, shot.Width, shot.Height);
            var f = new ItemForm(item);
            var r = f.ShowDialog();
            if (r == DialogResult.OK)
            {
                Step++;
                WriteItem(f.Item);
                SetState("已記 " + Step + " 件物品；按 " + VkName(StepVk) + " 記下一件；按 " + VkName(ModeVk) + " 結束");
            }
            else SetState("已取消；按 " + VkName(StepVk) + " 重記；按 " + VkName(ModeVk) + " 結束");
            f.Dispose();
            shot.Dispose();
        }

        static Dictionary<string, object> ParseItem(List<OcrLine> lines, int W, int H)
        {
            var right = new List<OcrLine>();
            foreach (var l in lines)
            {
                if (!l.IsChrome) right.Add(l);
            }
            right.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });

            string name = "", type = "", desc = "", section = "";
            int price = -1;
            bool exclusive = false;
            var eff = new List<string>();
            var cures = new List<string>();
            foreach (var l in right)
            {
                string t = (l.Text ?? "").Trim();
                if (t.Length == 0) continue;
                if (t == "效果") { section = "eff"; continue; }
                if (t == "治得好") { section = "cure"; continue; }
                if (t == "介绍" || t == "介紹") { section = "desc"; continue; }
                if (t == "这一件" || t == "這一件") { section = "none"; continue; }
                if (t.StartsWith("你的背包") || t.StartsWith("买下") || t.StartsWith("買下") || t.StartsWith("售罄")) continue;

                var mt = System.Text.RegularExpressions.Regex.Match(t, @"^(消耗品|道具|身體|身体|頭部|头部|飾品|饰品|近戰武器|近战武器|遠程武器|远程武器|防具|武器|工具|材料|食物|藥品|药品)");
                if (mt.Success) { if (type == "") type = t.Split('|')[0].Trim(); continue; }

                var mp = System.Text.RegularExpressions.Regex.Match(t, @"参考价格\s*([0-9]+)");
                if (mp.Success) { price = int.Parse(mp.Groups[1].Value); continue; }
                if (t.IndexOf("角色专属", StringComparison.Ordinal) >= 0 || t.IndexOf("角色專屬", StringComparison.Ordinal) >= 0) { exclusive = true; continue; }

                if (name == "")
                {
                    if (t.Length > 24) { name = t.Substring(0, 24); }
                    else name = t;
                    continue;
                }
                if (section == "eff") eff.Add(t.TrimStart('·', '・', '•', ' '));
                else if (section == "cure")
                {
                    var cm = System.Text.RegularExpressions.Regex.Match(t, @"^[·・•]?\s*([\u4e00-\u9fff]{2,6})(?=\s|$)");
                    if (cm.Success) cures.Add(cm.Groups[1].Value);
                }
                else if (section == "desc") desc += (desc == "" ? "" : "\n") + t;
                else if (type == "") { type = t; }
            }
            var d = new Dictionary<string, object>();
            d["name"] = name;
            d["type"] = type;
            d["source"] = price >= 0 ? "backpack" : "shop";
            d["price"] = price >= 0 ? price.ToString() : "";
            d["eff"] = eff;
            d["cures"] = cures;
            d["desc"] = desc;
            d["exclusive"] = exclusive;
            d["character"] = "";
            return d;
        }

        static void MergeList(Dictionary<string, object> old, Dictionary<string, object> neu, string key)
        {
            var b = neu.ContainsKey(key) ? neu[key] as System.Collections.ArrayList : null;
            if (b == null) return;
            var a = old.ContainsKey(key) ? old[key] as System.Collections.ArrayList : null;
            if (a == null) { a = new System.Collections.ArrayList(); old[key] = a; }
            foreach (var x in b) if (!a.Contains(x)) a.Add(x);
        }

        static void WriteItem(Dictionary<string, object> item)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                string dir = DataDir();
                string file = Path.Combine(dir, "items-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl");
                string name = Convert.ToString(item.ContainsKey("name") ? item["name"] : "");
                var lines = File.Exists(file) ? new List<string>(File.ReadAllLines(file)) : new List<string>();
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Length == 0) continue;
                    Dictionary<string, object> old;
                    try { old = ser.Deserialize<Dictionary<string, object>>(lines[i]); } catch { continue; }
                    if (old == null || Convert.ToString(old.ContainsKey("name") ? old["name"] : "") != name) continue;
                    MergeList(old, item, "eff");
                    MergeList(old, item, "cures");
                    if (Convert.ToString(old.ContainsKey("desc") ? old["desc"] : "") == "" && Convert.ToString(item["desc"]) != "") old["desc"] = item["desc"];
                    if (Convert.ToString(old.ContainsKey("price") ? old["price"] : "") == "" && Convert.ToString(item["price"]) != "") old["price"] = item["price"];
                    if (Convert.ToString(old.ContainsKey("type") ? old["type"] : "") == "" && Convert.ToString(item["type"]) != "") old["type"] = item["type"];
                    if (Convert.ToString(old.ContainsKey("character") ? old["character"] : "") == "" && Convert.ToString(item["character"]) != "") old["character"] = item["character"];
                    lines[i] = ser.Serialize(old);
                    File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
                    CurrentDock.Notify();
                    return;
                }
                var rec = new Dictionary<string, object>();
                rec["v"] = 1;
                rec["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                rec["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                rec["kind"] = "item";
                rec["name"] = item["name"];
                rec["type"] = item["type"];
                rec["source"] = item["source"];
                rec["price"] = item["price"];
                rec["eff"] = item["eff"];
                rec["cures"] = item["cures"];
                rec["desc"] = item["desc"];
                rec["exclusive"] = item["exclusive"];
                rec["character"] = item["character"];
                lines.Add(ser.Serialize(rec));
                File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
                CurrentDock.Notify();
                if (name == "" || Convert.ToString(item["desc"]) == "") WritePending("item-incomplete", rec);
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); }
        }

        static void CaptureShop()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            ShowBusy("掃描商店…");
            Application.DoEvents();

            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "event", out rect, out sx, out sy);
            if (shot == null) { HideBusy(); SetState("截圖失敗"); return; }
            if (!EnsureOcr()) { shot.Dispose(); HideBusy(); SetState("OCR 服務還在啟動"); return; }
            var lines = Ocr.Recognize(shot);
            if (lines.Count == 0) { shot.Dispose(); HideBusy(); SetState("OCR 沒有結果"); return; }
            Parse.Classify(lines, shot.Height);
            HideBusy();

            bool shopUi = false;
            var prices = new List<OcrLine>();
            foreach (var l in lines)
            {
                string t = (l.Text ?? "").Trim();
                if (t.IndexOf("硬币", StringComparison.Ordinal) >= 0 || t.IndexOf("硬幣", StringComparison.Ordinal) >= 0)
                {
                    shopUi = true;
                    prices.Add(l);
                }
                else if (t.IndexOf("买下", StringComparison.Ordinal) >= 0 || t.IndexOf("買下", StringComparison.Ordinal) >= 0)
                    shopUi = true;
            }

            if (shopUi)
            {
                var names = new List<string>();
                prices.Sort(delegate (OcrLine a, OcrLine b) { return a.Y.CompareTo(b.Y); });
                foreach (var pr in prices)
                {
                    OcrLine best = null;
                    double bestD = 1e9;
                    foreach (var l in lines)
                    {
                        if (l == pr) continue;
                        if (l.Y + l.H > pr.Y + 4) continue;
                        double d = pr.Y - (l.Y + l.H);
                        if (d < 0 || d > 52) continue;
                        if (Math.Abs(l.X - pr.X) > 140) continue;
                        string t = (l.Text ?? "").Trim();
                        if (t.Length < 1 || t.Length > 24) continue;
                        if (t.IndexOf("硬币", StringComparison.Ordinal) >= 0 || t.IndexOf("硬幣", StringComparison.Ordinal) >= 0) continue;
                        if (d < bestD) { bestD = d; best = l; }
                    }
                    if (best != null)
                    {
                        string t = best.Text.Trim();
                        if (!names.Contains(t)) names.Add(t);
                    }
                }
                var sf = new ShopForm(names, null, null);
                if (sf.ShowDialog() == DialogResult.OK)
                {
                    WriteShop(sf.Items, "", "");
                    SetState("已記商店：" + sf.Items.Count + " 件");
                }
            }
            else
            {
                string title = Parse.TitleOf(lines);
                var intro = new List<string>();
                foreach (var l in lines)
                {
                    if (l.IsChrome || l.IsOption || l.IsFloor || l.IsTitle) continue;
                    if (l.Y < 80 || l.Y > shot.Height * 0.76) continue;
                    intro.Add(l.Text.Trim());
                }
                var sf = new ShopForm(null, title, string.Join("\n", intro.ToArray()));
                if (sf.ShowDialog() == DialogResult.OK)
                {
                    WriteShop(new List<string>(), sf.Title, sf.Intro);
                    SetState("已記商店事件：" + sf.Title);
                }
            }
            shot.Dispose();
        }

        static void WriteShop(List<string> items, string title, string intro)
        {
            try
            {
                var rec = new Dictionary<string, object>();
                rec["v"] = 1;
                rec["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                rec["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                rec["kind"] = "shop";
                rec["floorId"] = Parse.FloorId;
                rec["floorName"] = Parse.FloorName;
                rec["title"] = title;
                rec["intro"] = intro;
                rec["items"] = items;
                File.AppendAllText(Path.Combine(DataDir(), "shops-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(rec) + "\r\n", new UTF8Encoding(false));
                CurrentDock.Notify();
                if (items.Count == 0 && intro == "") WritePending("shop-incomplete", rec);
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); }
        }

        static void CaptureFloor()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            SetState("狀態：OCR 中…");
            Application.DoEvents();
            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "floor", out rect, out sx, out sy);
            if (shot == null) { SetState("截圖失敗"); return; }
            if (!EnsureOcr()) { shot.Dispose(); SetState("OCR 服務還在啟動，稍後再試"); return; }
            var lines = Ocr.Recognize(shot);
            if (lines.Count == 0) { shot.Dispose(); SetState("OCR 沒有結果"); return; }
            Parse.Classify(lines, shot.Height);
            ScaleLines(lines, sx, sy);
            var f = new OverlayForm(1, lines, rect, GameHwnd, "floor");
            var r = RunOverlay(f);
            if (r == DialogResult.OK && f.Result != null)
            {
                Color bg = SampleBg(shot);
                var sel = f.Selected;
                Color title = sel.Count > 0 ? SampleText(shot, sel[0].Rect, bg) : bg;
                Color text = sel.Count > 1 ? SampleText(shot, sel[1].Rect, bg) : title;
                f.Result["palette"] = HexPalette(bg, title, text);
                Steps.Clear();
                Steps.Add(f.Result);
                Step = 1;
                SetState("樓層介紹已抓；按 " + VkName(ModeVk) + " 選明亮／昏暗並保存");
            }
            else SetState("已取消；按 " + VkName(StepVk) + " 重抓；按 " + VkName(ModeVk) + " 離開");
            f.Dispose();
            shot.Dispose();
        }

        static void CaptureExit()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            SetState("狀態：OCR 中…");
            Application.DoEvents();
            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "exit", out rect, out sx, out sy);
            if (shot == null) { SetState("截圖失敗"); return; }
            if (!Ocr.Ready && !Ocr.Start(AppDomain.CurrentDomain.BaseDirectory)) { shot.Dispose(); SetState("OCR 服務啟動失敗"); return; }
            var lines = Ocr.Recognize(shot);
            if (lines.Count == 0) { shot.Dispose(); SetState("OCR 沒有結果"); return; }
            Parse.Classify(lines, shot.Height);
            bool first = Steps.Count == 0;
            var f = new OverlayForm(first ? 1 : 2, lines, rect, GameHwnd, first ? "exit1" : "exit2");
            var r = RunOverlay(f);
            if (r == DialogResult.OK && f.Result != null)
            {
                Steps.Add(f.Result);
                Step = Steps.Count;
                SetState(first ? "出口事件已存；按 " + VkName(StepVk) + " 記「進入其他樓層的文本」" : "出口文本已存；按 " + VkName(ModeVk) + " 輸入通往哪一層");
            }
            else SetState("已取消；按 " + VkName(StepVk) + " 重記；按 " + VkName(ModeVk) + " 結束");
            f.Dispose();
            shot.Dispose();
        }

        static void CaptureEnding()
        {
            if (GameHwnd == IntPtr.Zero) EnsureClient(false);
            if (GameHwnd == IntPtr.Zero) { SetState("找不到客戶端視窗"); return; }
            SetState("狀態：OCR 中…");
            Application.DoEvents();
            Rectangle rect;
            double sx = 1, sy = 1;
            Bitmap shot = Capture.GrabRegion(GameHwnd, "ending", out rect, out sx, out sy);
            if (shot == null) { SetState("截圖失敗"); return; }
            if (!Ocr.Ready && !Ocr.Start(AppDomain.CurrentDomain.BaseDirectory)) { shot.Dispose(); SetState("OCR 服務啟動失敗"); return; }
            var lines = Ocr.Recognize(shot);
            if (lines.Count == 0) { shot.Dispose(); SetState("OCR 沒有結果"); return; }
            Parse.Classify(lines, shot.Height);
            bool first = Steps.Count == 0;
            var f = new OverlayForm(first ? 1 : 2, lines, rect, GameHwnd, first ? "ending1" : "ending2");
            var r = RunOverlay(f);
            if (r == DialogResult.OK && f.Result != null)
            {
                Steps.Add(f.Result);
                Step = Steps.Count;
                SetState(first ? "結局名＋通用介紹已存；按 " + VkName(StepVk) + " 記後日談" : "後日談已存；按 " + VkName(ModeVk) + " 選分類並保存");
            }
            else SetState("已取消；按 " + VkName(StepVk) + " 重記；按 " + VkName(ModeVk) + " 結束");
            f.Dispose();
            shot.Dispose();
        }

        static Color SampleBg(Bitmap bmp)
        {
            var counts = new Dictionary<int, int>();
            for (int y = 0; y < bmp.Height; y += 4)
            {
                for (int x = 0; x < bmp.Width; x += 4)
                {
                    Color c = bmp.GetPixel(x, y);
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    int n;
                    counts.TryGetValue(key, out n);
                    counts[key] = n + 1;
                }
            }
            int best = -1, bestKey = 0;
            foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
            return Color.FromArgb(((bestKey >> 8) & 0xF) * 17, ((bestKey >> 4) & 0xF) * 17, (bestKey & 0xF) * 17);
        }

        static Color SampleText(Bitmap bmp, Rectangle rect, Color bg)
        {
            var list = new List<int[]>();
            for (int y = rect.Top; y < rect.Bottom; y += 2)
            {
                for (int x = rect.Left; x < rect.Right; x += 2)
                {
                    if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                    Color c = bmp.GetPixel(x, y);
                    int d = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
                    list.Add(new int[] { d, c.R, c.G, c.B });
                }
            }
            if (list.Count == 0) return bg;
            list.Sort(delegate (int[] a, int[] b) { return b[0].CompareTo(a[0]); });
            int take = Math.Max(1, list.Count / 10);
            long r = 0, g = 0, bl = 0;
            for (int i = 0; i < take; i++) { r += list[i][1]; g += list[i][2]; bl += list[i][3]; }
            return Color.FromArgb((int)(r / take), (int)(g / take), (int)(bl / take));
        }

        static Dictionary<string, object> HexPalette(Color bg, Color title, Color text)
        {
            var d = new Dictionary<string, object>();
            d["bg"] = "#" + bg.R.ToString("X2") + bg.G.ToString("X2") + bg.B.ToString("X2");
            d["title"] = "#" + title.R.ToString("X2") + title.G.ToString("X2") + title.B.ToString("X2");
            d["text"] = "#" + text.R.ToString("X2") + text.G.ToString("X2") + text.B.ToString("X2");
            return d;
        }

        static void WriteFloor(Dictionary<string, object> rec, string theme)
        {
            try
            {
                var o = new Dictionary<string, object>();
                o["v"] = 1;
                o["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                o["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                o["kind"] = "floor";
                o["floorType"] = Convert.ToString(rec.ContainsKey("floorType") ? rec["floorType"] : "") == "anomalous" ? "anomalous" : "normal";
                o["floorId"] = Convert.ToString(rec["floorId"]);
                o["floorName"] = Convert.ToString(rec["floorName"]);
                o["intro"] = Convert.ToString(rec["intro"]);
                o["theme"] = theme;
                o["palette"] = rec.ContainsKey("palette") ? rec["palette"] : "";
                File.AppendAllText(Path.Combine(DataDir(), "floors-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(o) + "\r\n", new UTF8Encoding(false));
                CurrentDock.Notify();
                if (Convert.ToString(rec["floorId"]) == "") WritePending("floor-unknown", o);
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); }
        }

        static void WriteExit(Dictionary<string, object> rec, string target)
        {
            try
            {
                var o = new Dictionary<string, object>();
                o["v"] = 1;
                o["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                o["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                o["kind"] = "exit";
                o["floorId"] = Parse.FloorId;
                o["floorName"] = Parse.FloorName;
                o["event"] = rec.ContainsKey("event") ? rec["event"] : "";
                o["intro"] = rec.ContainsKey("intro") ? rec["intro"] : "";
                o["transition"] = rec.ContainsKey("transition") ? rec["transition"] : "";
                o["target"] = target;
                o["pending"] = target == "";
                File.AppendAllText(Path.Combine(DataDir(), "exits-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(o) + "\r\n", new UTF8Encoding(false));
                CurrentDock.Notify();
                if (target == "") WritePending("exit-target", o);
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); }
        }

        static void WriteEnding(Dictionary<string, object> rec, string category)
        {
            try
            {
                var o = new Dictionary<string, object>();
                o["v"] = 1;
                o["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                o["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                o["kind"] = "ending";
                o["name"] = rec.ContainsKey("name") ? rec["name"] : "";
                o["intro"] = rec.ContainsKey("intro") ? rec["intro"] : "";
                o["epilogue"] = rec.ContainsKey("epilogue") ? rec["epilogue"] : "";
                o["category"] = category;
                o["floorId"] = Parse.FloorId;
                o["floorName"] = Parse.FloorName;
                File.AppendAllText(Path.Combine(DataDir(), "endings-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(o) + "\r\n", new UTF8Encoding(false));
                CurrentDock.Notify();
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); }
        }

        static void WritePending(string type, Dictionary<string, object> data)
        {
            try
            {
                var o = new Dictionary<string, object>();
                o["v"] = 1;
                o["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                o["type"] = type;
                o["data"] = data;
                File.AppendAllText(Path.Combine(DataDir(), "pending-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(o) + "\r\n", new UTF8Encoding(false));
                try { PendingDock.Notify(); } catch { }
            }
            catch { }
        }

        static void NormalizeSteps()
        {
            foreach (var st in Steps) NormalizeObject(st);
        }

        static void NormalizeObject(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d != null)
            {
                var keys = new List<string>(d.Keys);
                foreach (var k in keys)
                {
                    var v = d[k];
                    var sv = v as string;
                    if (sv != null) { d[k] = Party.Normalize(sv); continue; }
                    NormalizeObject(v);
                }
                return;
            }
            var arr = o as System.Collections.ArrayList;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var sv = arr[i] as string;
                    if (sv != null) { arr[i] = Party.Normalize(sv); continue; }
                    NormalizeObject(arr[i]);
                }
            }
        }

        static void FinishEvent()
        {
            if (Mode == "floor")
            {
                if (Steps.Count == 0) { Mode = ""; SetState("已離開"); return; }
                NormalizeSteps();
                var floorRec = Steps[0];
                var pal = floorRec.ContainsKey("palette") ? floorRec["palette"] as Dictionary<string, object> : null;
                var tf = new ThemeForm(
                    Convert.ToString(floorRec.ContainsKey("floorId") ? floorRec["floorId"] : ""),
                    Convert.ToString(floorRec.ContainsKey("floorName") ? floorRec["floorName"] : ""),
                    Convert.ToString(floorRec.ContainsKey("floorType") ? floorRec["floorType"] : ""),
                    pal,
                    Convert.ToString(floorRec.ContainsKey("theme") ? floorRec["theme"] : "dark"));
                if (tf.ShowDialog() == DialogResult.OK)
                {
                    floorRec["floorType"] = tf.FloorType;
                    floorRec["floorId"] = tf.FloorId;
                    floorRec["floorName"] = tf.FloorName;
                    WriteFloor(floorRec, tf.Theme);
                    SetState("已存樓層介紹：" + (tf.FloorType == "normal" ? "常規" : "異常") + "／" + (tf.Theme == "light" ? "明亮" : "昏暗"));
                }
                else SetState("已取消");
                Mode = ""; Step = 0; Steps.Clear();
                return;
            }
            if (Mode == "exit")
            {
                if (Steps.Count < 2) { SetState("還缺一步：按 " + VkName(StepVk) + " 記「進入其他樓層的文本」"); return; }
                NormalizeSteps();
                var first = Steps[0];
                var second = Steps[1];
                string transition = Convert.ToString(second["transition"]);
                var mt = System.Text.RegularExpressions.Regex.Match(transition, @"Level\s*([0-9A-Za-z.]+)\s*[「『\[]([^」』\]\n]*)[」』\]]?");
                string preset = mt.Success ? mt.Groups[1].Value : "";
                var xf = new ExitForm("這條出口通向哪一層？", preset);
                if (xf.ShowDialog() == DialogResult.OK)
                {
                    first["transition"] = transition;
                    WriteExit(first, xf.Target);
                    SetState(xf.Target == "" ? "已存出口（等號待處理）" : "已存出口 → " + xf.Target);
                }
                else SetState("已取消");
                Mode = ""; Step = 0; Steps.Clear();
                return;
            }
            if (Mode == "ending")
            {
                if (Steps.Count < 2) { SetState("還缺一步：按 " + VkName(StepVk) + " 記「後日談」"); return; }
                var endRec = Steps[0];
                string rawIntro = Convert.ToString(endRec.ContainsKey("intro") ? endRec["intro"] : "");
                string rawEpi = Convert.ToString(Steps[1].ContainsKey("epilogue") ? Steps[1]["epilogue"] : "");
                string ln = Party.LocalName;
                if (ln != "" && rawIntro.IndexOf(ln, StringComparison.Ordinal) >= 0)
                {
                    int li = rawIntro.IndexOf(ln, StringComparison.Ordinal);
                    endRec["intro"] = rawIntro.Substring(0, li).TrimEnd();
                    rawEpi = rawIntro.Substring(li) + (rawEpi == "" ? "" : "\n" + rawEpi);
                    SetState("已按玩家名切分結局介紹");
                }
                endRec["epilogue"] = rawEpi;
                NormalizeSteps();
                var yf = new EndingForm(Convert.ToString(endRec["name"]));
                if (yf.ShowDialog() == DialogResult.OK) { WriteEnding(endRec, yf.Category); SetState("已存結局（" + yf.Category + "）"); }
                else SetState("已取消");
                Mode = ""; Step = 0; Steps.Clear();
                return;
            }
            if (Mode != "event")
            {
                Mode = ""; Step = 0; Steps.Clear();
                SetState("已離開");
                return;
            }
            if (Steps.Count == 0) { Mode = ""; SetState("已離開，按 " + VkName(ModeVk) + " 重新開始"); return; }
            NormalizeSteps();
            string curId = Steps.Count > 0 && Steps[0].ContainsKey("floorId") ? Convert.ToString(Steps[0]["floorId"]) : Parse.FloorId;
            string curName = Steps.Count > 0 && Steps[0].ContainsKey("floorName") ? Convert.ToString(Steps[0]["floorName"]) : Parse.FloorName;
            bool characterEvent = Steps.Count > 0 && Steps[0].ContainsKey("characterEvent") && Convert.ToBoolean(Steps[0]["characterEvent"]);
            string characterName = Steps.Count > 0 && Steps[0].ContainsKey("character") ? Convert.ToString(Steps[0]["character"]) : "";
            string ocrKind = Steps.Count > 0 && Steps[0].ContainsKey("eventKind") ? Convert.ToString(Steps[0]["eventKind"]) : "";
            var ef = new EndForm(curId, curName, Steps.Count, Party.LocalCharacter, characterEvent, characterName, ocrKind);
            var r = ef.ShowDialog();
            if (r != DialogResult.OK) { SetState("已取消結束；按 " + VkName(StepVk) + " 繼續記；按 " + VkName(ModeVk) + " 結束"); return; }

            var rec = new Dictionary<string, object>();
            rec["v"] = 1;
            rec["id"] = EventId;
            rec["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            rec["kind"] = "event";
            rec["eventKind"] = ef.EventKind;
            rec["scopes"] = ef.Scopes;
            rec["characters"] = ef.Characters;
            rec["outcomes"] = ef.Outcomes;
            rec["tags"] = ef.Tags;
            rec["scope"] = ef.Scopes[0];
            rec["outcome"] = ef.Outcomes[0];
            if (ef.EventKind == "character" || ef.EventKind == "cooperation") rec["characterEvent"] = true;
            if (ef.JumpTo != "") rec["jumpTo"] = ef.JumpTo;
            rec["floorId"] = ef.FloorId;
            rec["floorName"] = ef.FloorName;
            string combatEnemy = ef.CombatEnemy;
            if (combatEnemy == "" && Steps.Count > 1)
            {
                var s2 = Steps[1] as Dictionary<string, object>;
                if (s2 != null && s2.ContainsKey("combat"))
                {
                    var oldCombat = s2["combat"] as Dictionary<string, object>;
                    if (oldCombat != null && oldCombat.ContainsKey("enemy")) combatEnemy = Convert.ToString(oldCombat["enemy"]);
                }
            }
            if (combatEnemy != "")
            {
                var cb = new Dictionary<string, object>();
                cb["enemy"] = combatEnemy;
                rec["combat"] = cb;
                if (Steps.Count > 1)
                {
                    var s2 = Steps[1] as Dictionary<string, object>;
                    if (s2 != null) s2["combat"] = cb;
                }
            }
            rec["steps"] = Steps.ToArray();
            MoveChecksToOptions(rec);
            ApplyFreeChoiceBonus(rec);
            if (!WriteEvent(rec))
            {
                UiText.SetText(StepLabel, "寫入失敗，尚未存檔");
                return;
            }
            if (ef.Outcomes.Contains("combat") && combatEnemy == "") WritePending("combat-enemy", rec);
            if (ef.Scopes.Contains("character") && ef.Characters.Count == 0) WritePending("character-missing", rec);
            Mode = ""; Step = 0; Steps.Clear();
            UiText.SetText(StepLabel, "已存一條事件");
            SetState("已存一條事件（" + string.Join("+", ef.Scopes.ToArray()) + " / " + string.Join("+", ef.Outcomes.ToArray()) + "），按 " + VkName(ModeVk) + " 再記");
        }

        public static string DataDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.Combine(baseDir, "Recorder", "logs", "manual");
            if (!Directory.Exists(Path.Combine(baseDir, "Recorder"))) dir = Path.Combine(baseDir, "logs", "manual");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /* “出口进度 N/M”里的 M 是楼层长度，不属于事件。
           独立写成 floor-lengths-*.jsonl，由 import-manual 写回 floor.json。 */
        public static void WriteFloorLength(string floorId, string floorName, int length)
        {
            try
            {
                if (string.IsNullOrEmpty(floorId) || length <= 0) return;
                string dir = DataDir();
                var ser = new JavaScriptSerializer();
                foreach (var f in Directory.GetFiles(dir, "floor-lengths-*.jsonl"))
                {
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        Dictionary<string, object> old;
                        try { old = ser.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                        if (old == null) continue;
                        if (Convert.ToString(old.ContainsKey("floorId") ? old["floorId"] : "") == floorId
                            && Convert.ToInt32(old.ContainsKey("length") ? old["length"] : 0) == length)
                            return;
                    }
                }
                var rec = new Dictionary<string, object>();
                rec["v"] = 1;
                rec["id"] = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                rec["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                rec["kind"] = "floor-length";
                rec["floorId"] = floorId;
                rec["floorName"] = floorName ?? "";
                rec["length"] = length;
                File.AppendAllText(Path.Combine(dir, "floor-lengths-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    ser.Serialize(rec) + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { SetState("寫入樓層長度失敗：" + ex.Message); }
        }

        static void WriteDraft(string id, Dictionary<string, object> step)
        {
            try
            {
                var rec = new Dictionary<string, object>();
                rec["v"] = 1;
                rec["id"] = id;
                rec["ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                rec["draft"] = step;
                File.AppendAllText(Path.Combine(DataDir(), "draft-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    new JavaScriptSerializer().Serialize(rec) + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        static string EventTitle(Dictionary<string, object> rec)
        {
            /* rec["steps"] 可能是 Dictionary[]（刚记完）或 ArrayList（从 JSONL 读回），
               必须用 IList，否则新记录永远被当成“无标题”，同楼层互相覆盖。 */
            var steps = rec.ContainsKey("steps") ? rec["steps"] as System.Collections.IList : null;
            if (steps != null && steps.Count > 0)
            {
                var s1 = steps[0] as Dictionary<string, object>;
                if (s1 != null && s1.ContainsKey("title")) return Convert.ToString(s1["title"]);
            }
            return rec.ContainsKey("title") ? Convert.ToString(rec["title"]) : "";
        }

        static void MarkUnseen(Dictionary<string, object> rec)
        {
            var steps = rec.ContainsKey("steps") ? rec["steps"] as System.Collections.IList : null;
            if (steps == null || steps.Count < 2) return;
            var s1 = steps[0] as Dictionary<string, object>;
            var s2 = steps[1] as Dictionary<string, object>;
            if (s1 == null || s2 == null) return;
            string choice = Convert.ToString(s2.ContainsKey("choice") ? s2["choice"] : "");
            var opts = s1.ContainsKey("options") ? s1["options"] as System.Collections.IList : null;
            if (opts == null) return;
            foreach (var o in opts)
            {
                var od = o as Dictionary<string, object>;
                if (od == null) continue;
                string t = Convert.ToString(od.ContainsKey("text") ? od["text"] : "");
                if (choice != "" && t == choice) od["seen"] = true;
                else od["unseen"] = true;
            }
        }

        static Dictionary<string, object> BranchFromStep(Dictionary<string, object> s2)
        {
            var br = new Dictionary<string, object>();
            br["selected"] = s2.ContainsKey("choice") ? Convert.ToString(s2["choice"]) : "";
            br["results"] = s2.ContainsKey("results") ? s2["results"] : new System.Collections.ArrayList();
            if (s2.ContainsKey("check")) br["check"] = s2["check"];
            if (s2.ContainsKey("combat")) br["combat"] = s2["combat"];
            if (s2.ContainsKey("effects")) br["effects"] = s2["effects"];
            if (s2.ContainsKey("progress")) br["progress"] = s2["progress"];
            if (s2.ContainsKey("qte")) br["qte"] = s2["qte"];
            if (s2.ContainsKey("leadsTo")) br["leadsTo"] = s2["leadsTo"];
            if (s2.ContainsKey("player")) br["player"] = s2["player"];
            if (s2.ContainsKey("players")) br["players"] = s2["players"];
            if (s2.ContainsKey("character")) br["character"] = s2["character"];
            if (s2.ContainsKey("characters")) br["characters"] = s2["characters"];
            return br;
        }

        static bool WriteEvent(Dictionary<string, object> rec)
        {
            try
            {
                MarkUnseen(rec);
                var ser = new JavaScriptSerializer();
                string dir = DataDir();
                string floorId = Convert.ToString(rec.ContainsKey("floorId") ? rec["floorId"] : "");
                string title = EventTitle(rec).Trim();
                string id = Convert.ToString(rec.ContainsKey("id") ? rec["id"] : "");
                /* 没有楼层或标题时不能用 "|" 当 key，否则同楼层的多条无标题事件
                   会互相覆盖，最后只剩第一条。 */
                string key = (floorId == "" ? "#" + id : floorId) + "|" + (title == "" ? "#" + id : title);
                foreach (var f in Directory.GetFiles(dir, "events-*.jsonl"))
                {
                    var lines = new List<string>(File.ReadAllLines(f));
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().Length == 0) continue;
                        Dictionary<string, object> old;
                        try { old = ser.Deserialize<Dictionary<string, object>>(lines[i]); } catch { continue; }
                        if (old == null) continue;
                        string oldFloor = Convert.ToString(old.ContainsKey("floorId") ? old["floorId"] : "");
                        string oldTitle = EventTitle(old).Trim();
                        string oldId = Convert.ToString(old.ContainsKey("id") ? old["id"] : "");
                        string oldKey = (oldFloor == "" ? "#" + oldId : oldFloor) + "|" + (oldTitle == "" ? "#" + oldId : oldTitle);
                        if (oldKey != key) continue;
                        var branches = old.ContainsKey("branches") ? old["branches"] as System.Collections.IList : null;
                        if (branches == null) { branches = new System.Collections.ArrayList(); old["branches"] = branches; }
                        var newBranches = new List<Dictionary<string, object>>();
                        var steps2 = rec["steps"] as System.Collections.IList;
                        if (steps2 != null)
                        {
                            /* 自由事件／岔路事件可以连续按 E 记多个玩家分支；
                               第 2 步之后每一步都当一条分支，不再只取 steps[1]。 */
                            for (int si = 1; si < steps2.Count; si++)
                            {
                                var s2 = steps2[si] as Dictionary<string, object>;
                                if (s2 == null) continue;
                                var nested = s2.ContainsKey("branches") ? s2["branches"] as System.Collections.IList : null;
                                if (nested != null)
                                {
                                    foreach (var nb in nested)
                                    {
                                        var nd = nb as Dictionary<string, object>;
                                        if (nd != null) newBranches.Add(BranchFromStep(nd));
                                    }
                                }
                                else
                                {
                                    var br = BranchFromStep(s2);
                                    if (Convert.ToString(br["selected"]) != "") newBranches.Add(br);
                                }
                            }
                        }
                        /* 只有“选了某个选项”的分支才合并；没有选择时不能直接 return，
                           否则这条新记录会被静默丢掉。 */
                        if (newBranches.Count > 0)
                        {
                            foreach (var br in newBranches)
                            {
                                string selected = Convert.ToString(br["selected"]);
                                string player = br.ContainsKey("player") ? Convert.ToString(br["player"]) : "";
                                bool dup = false;
                                foreach (var b in branches)
                                {
                                    var bd = b as Dictionary<string, object>;
                                    if (bd == null) continue;
                                    string bs = Convert.ToString(bd.ContainsKey("selected") ? bd["selected"] : "");
                                    string bp = bd.ContainsKey("player") ? Convert.ToString(bd["player"]) : "";
                                    if (bs == selected && bp == player) { dup = true; break; }
                                }
                                if (!dup) branches.Add(br);
                            }
                            lines[i] = ser.Serialize(old);
                            File.WriteAllLines(f, lines.ToArray(), new UTF8Encoding(false));
                            CurrentDock.Notify();
                            return true;
                        }
                    }
                }
                File.AppendAllText(Path.Combine(dir, "events-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"),
                    ser.Serialize(rec) + "\r\n", new UTF8Encoding(false));
                CurrentDock.Notify();
                return true;
            }
            catch (Exception ex) { SetState("寫入失敗：" + ex.Message); return false; }
        }

        static string HotkeyFile() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkeys.json"); }

        static void LoadHotkeys()
        {
            try
            {
                if (!File.Exists(HotkeyFile())) return;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(HotkeyFile()));
                if (d != null && d.ContainsKey("mode")) ModeVk = Convert.ToInt32(d["mode"]);
                if (d != null && d.ContainsKey("step")) StepVk = Convert.ToInt32(d["step"]);
                if (d != null && d.ContainsKey("cancel")) CancelVk = Convert.ToInt32(d["cancel"]);
                if (d != null && d.ContainsKey("textScale"))
                {
                    float sc;
                    if (float.TryParse(Convert.ToString(d["textScale"]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc))
                        UiScale.Value = UiScale.NearestScale(sc);
                }
                if (d != null && d.ContainsKey("preset"))
                {
                    string ps = Convert.ToString(d["preset"]);
                    if (ps == "1280x720" || ps == "1600x900" || ps == "1920x1080" || ps == "2400x1350") Preset = ps;
                }
                if (d != null && d.ContainsKey("lang"))
                {
                    string lg = Convert.ToString(d["lang"]);
                    if (lg == "Traditional") UiText.Lang = UiLang.Traditional;
                    else if (lg == "Simplified") UiText.Lang = UiLang.Simplified;
                    else if (lg == "English") UiText.Lang = UiLang.English;
                }
                if (d != null && d.ContainsKey("hotkeysEnabled"))
                {
                    string he = Convert.ToString(d["hotkeysEnabled"]).ToLowerInvariant();
                    if (he == "true") HotkeysEnabled = true;
                    else if (he == "false") HotkeysEnabled = false;
                }
                if (d != null && d.ContainsKey("currentDock"))
                {
                    string cd = Convert.ToString(d["currentDock"]).ToLowerInvariant();
                    if (cd == "true") CurrentDock.DockEnabled = true;
                    else if (cd == "false") CurrentDock.DockEnabled = false;
                }
            }
            catch { }
        }

        public static void SaveHotkeys()
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["mode"] = ModeVk;
                d["step"] = StepVk;
                d["cancel"] = CancelVk;
                d["textScale"] = Math.Round(UiScale.Scale, 1);
                d["preset"] = Preset;
                d["lang"] = UiText.Lang.ToString();
                d["hotkeysEnabled"] = HotkeysEnabled;
                d["currentDock"] = CurrentDock.DockEnabled;
                File.WriteAllText(HotkeyFile(), new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string VkName(int vk)
        {
            if (vk == 0xC0) return "`";
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk == 0x20) return "Space";
            if (vk == 0x09) return "Tab";
            return "VK" + vk.ToString("X2");
        }

        public static void ApplyHotkeys()
        {
            if (Keys != null) { Keys.ModeVk = ModeVk; Keys.StepVk = StepVk; Keys.CancelVk = CancelVk; Keys.Enabled = HotkeysEnabled; }
            UpdateHint();
            UpdateRecordButtons();
        }

        static string HelpTextEn()
        {
            string mk = VkName(ModeVk), sk = VkName(StepVk), ck = VkName(CancelVk);
            var sb = new StringBuilder();
            sb.AppendLine("Mode: " + mk + "    Record: " + sk + "    Cancel: " + ck);
            sb.AppendLine();
            sb.AppendLine("Tap or hold " + mk + " -> radial menu; release on a mode = select; no selection leaves it open");
            sb.AppendLine();
            sb.AppendLine("[Event]");
            sb.AppendLine("  " + sk + " step 1: drag \"event name + intro\" -> release to save");
            sb.AppendLine("  " + sk + " step 2: drag \"choice + result\" -> release to save");
            sb.AppendLine("  " + mk + " finish: choose one event type (normal/free/junction/character/cooperation/common); first three allow a floor; then jump/combat and the rest tag");
            sb.AppendLine();
            sb.AppendLine("[Item]");
            sb.AppendLine("  " + sk + " capture current item; " + mk + " finish");
            sb.AppendLine();
            sb.AppendLine("[Floor intro]");
            sb.AppendLine("  " + sk + " drag floor name + intro; " + mk + " choose light/dark");
            sb.AppendLine();
            sb.AppendLine("[Exit]");
            sb.AppendLine("  " + sk + " exit event + intro; " + sk + " transition text; " + mk + " fill target");
            sb.AppendLine();
            sb.AppendLine("[Shop]");
            sb.AppendLine("  " + sk + " scan shelf (shop UI) or shop text; " + mk + " finish");
            sb.AppendLine();
            sb.AppendLine("[Ending]");
            sb.AppendLine("  " + sk + " ending name + common intro; " + sk + " epilogue; " + mk + " choose category");
            sb.AppendLine();
            sb.AppendLine("Selection: Y axis only; every line vertically covered is selected; release to save");
            sb.AppendLine("Cancel: " + ck + " cancels this scan; press again to discard the whole record; Esc/right-click also cancels the current selection");
            sb.AppendLine("Hotkeys can be changed in Hotkeys; this text and the status line update automatically");
            return sb.ToString();
        }

        public static string HelpText()
        {
            if (UiText.Lang == UiLang.English) return HelpTextEn();
            string mk = VkName(ModeVk), sk = VkName(StepVk), ck = VkName(CancelVk);
            var sb = new StringBuilder();
            sb.AppendLine("模式鍵：" + mk + "　　記錄鍵：" + sk + "　　取消鍵：" + ck);
            sb.AppendLine();
            sb.AppendLine(mk + " 按一下或按住 → 圓盤；移到模式上鬆開 = 選擇；沒選會留在畫面");
            sb.AppendLine();
            sb.AppendLine("【事件】");
            sb.AppendLine("  " + sk + " 第1步：拖「事件名＋介紹」→ 鬆手即存");
            sb.AppendLine("  " + sk + " 第2步：拖「決定＋結果」→ 鬆手即存");
            sb.AppendLine("  " + mk + " 結束：單選事件類型（普通/自由/岔路/角色/合力/通用）；前三種可填事件出現樓層；再選跳層/進入戰鬥與休息處標籤");
            sb.AppendLine();
            sb.AppendLine("【物品】");
            sb.AppendLine("  " + sk + " 收當前物品；" + mk + " 結束");
            sb.AppendLine();
            sb.AppendLine("【樓層介紹】");
            sb.AppendLine("  " + sk + " 拖樓層名＋介紹；" + mk + " 選明亮/昏暗");
            sb.AppendLine();
            sb.AppendLine("【出口】");
            sb.AppendLine("  " + sk + " 出口事件＋介紹；" + sk + " 通往文本；" + mk + " 填目標");
            sb.AppendLine();
            sb.AppendLine("【商店】");
            sb.AppendLine("  " + sk + " 掃描貨架（有商店介面）或商店文字；" + mk + " 結束");
            sb.AppendLine();
            sb.AppendLine("【結局】");
            sb.AppendLine("  " + sk + " 結局名＋通用介紹；" + sk + " 後日談；" + mk + " 選分類");
            sb.AppendLine();
            sb.AppendLine("框選：只按 Y 軸，垂直帶覆蓋到的整行都會選中；鬆手即存");
            sb.AppendLine("取消：" + ck + " 掃描中取消這一步；再按一次放棄整條記錄；Esc 或右鍵也可取消當前框選");
            sb.AppendLine("按鍵可在「按鍵設定」改，改完這裡和狀態文字會同步更新");
            string text = sb.ToString();
            return UiText.Lang == UiLang.Simplified ? UiText.Convert(text, true) : text;
        }

        public static void UpdateHint()
        {
            try
            {
                if (HelpForm.Instance != null && !HelpForm.Instance.IsDisposed) HelpForm.Instance.SetText(HelpText());
                if (StepLabel != null && Mode == "") UiText.SetText(StepLabel, "按 " + VkName(ModeVk) + " 開始記錄");
            }
            catch { }
        }

        static void CancelRecording()
        {
            bool had = Mode != "" || Steps.Count > 0;
            Mode = ""; Step = 0; Steps.Clear();
            var p = Picker;
            Picker = null;
            if (p != null && !p.IsDisposed) { try { p.Close(); } catch { } }
            UiText.SetText(StepLabel, "按 " + VkName(ModeVk) + " 開始記錄");
            SetState(had ? "已取消這次記錄" : "沒有進行中的記錄");
        }

        public static string ExportDir()
        {
            string d = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (Directory.Exists(Path.Combine(d, "wiki", "content"))) return Path.Combine(d, "导出");
                var up = Path.GetDirectoryName(d);
                if (up == null || up == d) break;
                d = up;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "狐霖記錄導出");
        }

        public static List<Dictionary<string, object>> ReadAll(string prefix)
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                foreach (var f in Directory.GetFiles(DataDir(), prefix + "*.jsonl"))
                {
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            var o = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line);
                            if (o != null) list.Add(o);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        static string S(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k) || d[k] == null) return "";
            return Convert.ToString(d[k]);
        }

        static string A(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.ContainsKey(k)) return "";
            var arr = d[k] as System.Collections.ArrayList;
            if (arr == null) return Convert.ToString(d[k]);
            var parts = new List<string>();
            foreach (var x in arr) parts.Add(Convert.ToString(x));
            return string.Join("、", parts.ToArray());
        }

        static void ExportAll()
        {
            try
            {
                string dir = ExportDir();
                Directory.CreateDirectory(dir);
                var ev = ReadAll("events-");
                var it = ReadAll("items-");
                var fl = ReadAll("floors-");
                var ex = ReadAll("exits-");
                var en = ReadAll("endings-");
                var sh = ReadAll("shops-");
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
                var root = new Dictionary<string, object>();
                root["exported"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var counts = new Dictionary<string, object>();
                counts["events"] = ev.Count; counts["items"] = it.Count; counts["floors"] = fl.Count;
                counts["exits"] = ex.Count; counts["endings"] = en.Count;
                root["counts"] = counts;
                root["events"] = ev; root["items"] = it; root["floors"] = fl; root["exits"] = ex; root["endings"] = en;
                var ser = new JavaScriptSerializer();
                string jsonPath = Path.Combine(dir, "記錄-" + stamp + ".json");
                File.WriteAllText(jsonPath, ser.Serialize(root), new UTF8Encoding(false));
                string mdPath = Path.Combine(dir, "記錄-" + stamp + ".md");
                File.WriteAllText(mdPath, BuildMarkdown(ev, it, fl, ex, en, sh, stamp), new UTF8Encoding(false));
                SetState("已導出 記錄-" + stamp + ".json / .md");
                try { Process.Start("explorer.exe", "/select,\"" + jsonPath + "\""); } catch { }
            }
            catch (Exception ex) { SetState("導出失敗：" + ex.Message); }
        }

        static string NormFid(string s)
        {
            s = (s ?? "").Trim();
            if (s == "O" || s == "o") s = "0";
            if (s == "I" || s == "l") s = "1";
            return s;
        }

        static string ScopeCn(string s)
        {
            var parts = new List<string>();
            foreach (var x in (s ?? "").Split('、'))
            {
                if (x == "floor") parts.Add("樓層");
                else if (x == "character") parts.Add("角色");
                else if (x == "common") parts.Add("通用");
                else if (x != "") parts.Add(x);
            }
            return string.Join("、", parts.ToArray());
        }

        static string OutcomeCn(string s)
        {
            var parts = new List<string>();
            foreach (var x in (s ?? "").Split('、'))
            {
                if (x == "normal") parts.Add("普通");
                else if (x == "floor-jump") parts.Add("跳層");
                else if (x == "shop") parts.Add("商店");
                else if (x == "rest") parts.Add("休息處");
                else if (x == "combat") parts.Add("戰鬥");
                else if (x != "") parts.Add(x);
            }
            return string.Join("、", parts.ToArray());
        }

        public static List<Dictionary<string, object>> DedupeEvents(List<Dictionary<string, object>> ev)
        {
            var uniq = new List<Dictionary<string, object>>();
            var seen = new Dictionary<string, Dictionary<string, object>>();
            var order = new List<string>();
            foreach (var e in ev)
            {
                var steps0 = e.ContainsKey("steps") ? e["steps"] as System.Collections.ArrayList : null;
                string title0 = steps0 != null && steps0.Count > 0 ? S(steps0[0] as Dictionary<string, object>, "title") : S(e, "title");
                string key = NormFid(S(e, "floorId")) + "|" + (title0 == "" ? "(無標題)" : title0);
                if (!seen.ContainsKey(key))
                {
                    var copy = new Dictionary<string, object>(e);
                    copy["floorId"] = NormFid(S(e, "floorId"));
                    if (steps0 != null) copy["steps"] = new System.Collections.ArrayList(steps0.ToArray());
                    seen[key] = copy;
                    order.Add(key);
                }
                else
                {
                    var keep = seen[key];
                    var keepSteps = keep.ContainsKey("steps") ? keep["steps"] as System.Collections.ArrayList : null;
                    if (keepSteps != null && steps0 != null)
                    {
                        foreach (var st in steps0)
                        {
                            var std = st as Dictionary<string, object>;
                            if (std == null) continue;
                            bool dup = false;
                            foreach (var ks in keepSteps)
                            {
                                var kd = ks as Dictionary<string, object>;
                                if (kd != null && S(kd, "step") == S(std, "step") && S(kd, "choice") == S(std, "choice")) { dup = true; break; }
                            }
                            if (!dup) keepSteps.Add(std);
                        }
                    }
                }
            }
            foreach (var k in order) uniq.Add(seen[k]);
            return uniq;
        }

        public static string BuildMarkdown(List<Dictionary<string, object>> ev, List<Dictionary<string, object>> it,
            List<Dictionary<string, object>> fl, List<Dictionary<string, object>> ex, List<Dictionary<string, object>> en,
            List<Dictionary<string, object>> sh, string stamp)
        {
            var sb = new StringBuilder();
            ev = DedupeEvents(ev);

            sb.AppendLine("# 狐霖記錄導出 " + stamp);
            sb.AppendLine();
            sb.AppendLine("事件 " + ev.Count + " · 物品 " + it.Count + " · 樓層 " + fl.Count + " · 出口 " + ex.Count + " · 結局 " + en.Count);
            sb.AppendLine();

            var groups = new Dictionary<string, List<Dictionary<string, object>>>();
            var order = new List<string>();
            foreach (var e in ev)
            {
                string fid = NormFid(S(e, "floorId"));
                if (!groups.ContainsKey(fid)) { groups[fid] = new List<Dictionary<string, object>>(); order.Add(fid); }
                groups[fid].Add(e);
            }
            foreach (var fid in order)
            {
                var list = groups[fid];
                string name = S(list[0], "floorName");
                sb.AppendLine("## Level " + fid + " " + name);
                sb.AppendLine();
                foreach (var e in list)
                {
                    var steps = e.ContainsKey("steps") ? e["steps"] as System.Collections.ArrayList : null;
                    var s1 = steps != null && steps.Count > 0 ? steps[0] as Dictionary<string, object> : e;
                    string title = S(s1, "title");
                    sb.AppendLine("### 事件：" + (title == "" ? "（無標題）" : title));
                    string scopes = A(e, "scopes"); if (scopes == "") scopes = S(e, "scope");
                    scopes = ScopeCn(scopes); if (scopes == "") scopes = "樓層";
                    string outs = A(e, "outcomes"); if (outs == "") outs = S(e, "outcome");
                    outs = OutcomeCn(outs); if (outs == "") outs = "普通";
                    string chars = A(e, "characters"); if (chars == "") chars = S(e, "character");
                    sb.AppendLine("- 歸屬：" + scopes + (chars != "" ? "　角色：" + chars : ""));
                    sb.AppendLine("- 結果類型：" + outs + (S(e, "jumpTo") != "" ? " → " + S(e, "jumpTo") : ""));
                    string intro = S(s1, "intro");
                    if (intro != "") { sb.AppendLine("- 介紹："); foreach (var l in intro.Split('\n')) sb.AppendLine("  > " + l); }
                    var opts = s1.ContainsKey("options") ? s1["options"] as System.Collections.ArrayList : null;
                    if (opts != null && opts.Count > 0)
                    {
                        sb.AppendLine("- 選項：");
                        int n = 1;
                        foreach (var oo in opts)
                        {
                            var od = oo as Dictionary<string, object>;
                            if (od == null) continue;
                            string line = "  " + n + ". " + S(od, "text");
                            if (S(od, "check") != "") line += "（檢定 " + S(od, "check") + "）";
                            if (od.ContainsKey("unseen") && Convert.ToString(od["unseen"]) == "True") line += "（尚未遇到）";
                            if (od.ContainsKey("requirement"))
                            {
                                var rd = od["requirement"] as Dictionary<string, object>;
                                line += "（需求 " + (rd != null ? S(rd, "name") : Convert.ToString(od["requirement"])) + "）";
                            }
                            sb.AppendLine(line);
                            n++;
                        }
                    }
                    if (steps != null)
                    {
                        for (int i = 1; i < steps.Count; i++)
                        {
                            var st = steps[i] as Dictionary<string, object>;
                            if (st == null) continue;
                            sb.AppendLine("- 第 " + S(st, "step") + " 步：");
                            if (S(st, "choice") != "") sb.AppendLine("  - 決定：" + S(st, "choice"));
                            if (S(st, "leadsTo") != "") sb.AppendLine("  - 感知線索 → Level " + S(st, "leadsTo"));
                            var ck = st.ContainsKey("check") ? st["check"] as Dictionary<string, object> : null;
                            if (ck != null)
                            {
                                string attr = S(ck, "attribute"); if (attr == "") attr = S(ck, "attr");
                                sb.AppendLine("  - 檢定：" + attr + (S(ck, "outcome") != "" ? " → " + S(ck, "outcome") : ""));
                            }
                            var qt = st.ContainsKey("qte") ? st["qte"] as Dictionary<string, object> : null;
                            if (qt != null)
                            {
                                string qo = S(qt, "outcome");
                                sb.AppendLine("  - QTE：" + (qo == "failure" ? "失敗" : (qo == "success" ? "成功" : "已通過")));
                            }
                            var cb = st.ContainsKey("combat") ? st["combat"] as Dictionary<string, object> : null;
                            if (cb != null) sb.AppendLine("  - 戰鬥：" + S(cb, "enemy"));
                            var res = st.ContainsKey("results") ? st["results"] as System.Collections.ArrayList : null;
                            if (res != null) foreach (var r in res) sb.AppendLine("  - " + Convert.ToString(r));
                            var eff = st.ContainsKey("effects") ? st["effects"] as System.Collections.ArrayList : null;
                            if (eff != null) foreach (var eo in eff)
                            {
                                var ed = eo as Dictionary<string, object>;
                                if (ed == null) continue;
                                string text = S(ed, "text");
                                if (text == "") text = (S(ed, "target") + " " + S(ed, "kind") + " " + S(ed, "amount")).Trim();
                                sb.AppendLine("  - 效果：" + text);
                            }
                            var pg = st.ContainsKey("progress") ? st["progress"] as Dictionary<string, object> : null;
                            if (pg != null)
                            {
                                string txt = "出口進度";
                                if (S(pg, "delta") != "") txt += " " + S(pg, "delta");
                                if (S(pg, "current") != "") txt += "（" + S(pg, "current") + "/" + S(pg, "total") + "）";
                                sb.AppendLine("  - " + txt);
                            }
                        }
                    }
                    sb.AppendLine();
                }
            }

            if (it.Count > 0)
            {
                sb.AppendLine("## 物品");
                sb.AppendLine();
                foreach (var x in it)
                {
                    sb.AppendLine("### " + S(x, "name") + "（" + S(x, "type") + "）");
                    if (S(x, "price") != "") sb.AppendLine("- 參考價格：" + S(x, "price"));
                    string eff = A(x, "eff");
                    if (eff != "") sb.AppendLine("- 效果：" + eff);
                    string cures = A(x, "cures");
                    if (cures != "") sb.AppendLine("- 治得好：" + cures);
                    if (S(x, "desc") != "") sb.AppendLine("- 介紹：" + S(x, "desc").Replace("\n", " "));
                    sb.AppendLine();
                }
            }

            if (fl.Count > 0)
            {
                sb.AppendLine("## 樓層介紹");
                sb.AppendLine();
                foreach (var x in fl)
                {
                    sb.AppendLine("### Level " + S(x, "floorId") + " " + S(x, "floorName"));
                    sb.AppendLine("- 主題：" + S(x, "theme"));
                    string intro = S(x, "intro");
                    if (intro != "") foreach (var l in intro.Split('\n')) sb.AppendLine("  > " + l);
                    sb.AppendLine();
                }
            }

            if (ex.Count > 0)
            {
                sb.AppendLine("## 出口");
                sb.AppendLine();
                foreach (var x in ex)
                {
                    sb.AppendLine("- Level " + S(x, "floorId") + " " + S(x, "floorName") + "：" + S(x, "event") + " → " + (S(x, "target") != "" ? "Level " + S(x, "target") : "（待處理）"));
                    if (S(x, "transition") != "") sb.AppendLine("  > " + S(x, "transition").Replace("\n", " "));
                }
                sb.AppendLine();
            }

            if (en.Count > 0)
            {
                sb.AppendLine("## 結局");
                sb.AppendLine();
                foreach (var x in en)
                {
                    sb.AppendLine("### " + S(x, "name") + "（" + S(x, "category") + "）");
                    string intro = S(x, "intro");
                    if (intro != "") foreach (var l in intro.Split('\n')) sb.AppendLine("  > " + l);
                    if (S(x, "epilogue") != "") { sb.AppendLine("- 後日談："); foreach (var l in S(x, "epilogue").Split('\n')) sb.AppendLine("  > " + l); }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        static int PwTest(string hwndArg)
        {
            var sb = new StringBuilder();
            try
            {
                IntPtr h = new IntPtr(long.Parse(hwndArg));
                RECT wr;
                Native.GetWindowRect(h, out wr);
                int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
                sb.AppendLine("hwnd=" + h + " rect=" + ww + "x" + wh);
                foreach (uint flag in new uint[] { 2, 0 })
                {
                    using (var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb))
                    {
                        bool ok;
                        using (var g = Graphics.FromImage(bmp))
                        {
                            IntPtr dc = g.GetHdc();
                            try { ok = Native.PrintWindow(h, dc, flag); }
                            finally { g.ReleaseHdc(dc); }
                        }
                        sb.AppendLine("flag " + flag + " -> " + ok);
                        bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pwtest-" + flag + ".png"), ImageFormat.Png);
                    }
                }
                IntPtr child = Capture.RenderChild(h);
                sb.AppendLine("child=" + child);
                if (child != IntPtr.Zero)
                {
                    RECT cr; Native.GetWindowRect(child, out cr);
                    using (var bmp = new Bitmap(cr.Right - cr.Left, cr.Bottom - cr.Top, PixelFormat.Format32bppArgb))
                    {
                        bool ok;
                        using (var g = Graphics.FromImage(bmp))
                        {
                            IntPtr dc = g.GetHdc();
                            try { ok = Native.PrintWindow(child, dc, 2); }
                            finally { g.ReleaseHdc(dc); }
                        }
                        sb.AppendLine("child pw2 -> " + ok);
                        bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pwtest-child.png"), ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pwtest.txt"), sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        static int CheckTest(string text)
        {
            string attr, dc, roll, outcome, player;
            bool ok = Parse.TryParseCheck(text, out attr, out dc, out roll, out outcome, out player);
            var sb = new StringBuilder();
            if (ok && player != "") Party.SetLocal(player);
            sb.AppendLine("ok=" + ok + " attr=" + attr + " dc=" + dc + " roll=" + roll + " outcome=" + outcome + " player=" + player);
            sb.AppendLine("norm=" + Party.Normalize("天狐：「冷静，这是症状。」"));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checktest.txt"), sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        static int ParseTest()
        {
            var lines = new List<OcrLine>();
            lines.Add(new OcrLine { Text = "Level 0「大厅」", X = 10, Y = 10, W = 200, H = 30 });
            lines.Add(new OcrLine { Text = "【测试事件】", X = 30, Y = 120, W = 140, H = 30 });
            lines.Add(new OcrLine { Text = "要 怎 么 做 ？", X = 30, Y = 520, W = 160, H = 30 });
            lines.Add(new OcrLine { Text = "选项一", X = 30, Y = 570, W = 100, H = 30 });
            lines.Add(new OcrLine { Text = "选项二", X = 30, Y = 610, W = 100, H = 30 });
            Parse.Classify(lines, 890);
            var sb = new StringBuilder();
            foreach (var l in lines) sb.AppendLine(l.Text + " chrome=" + l.IsChrome + " option=" + l.IsOption + " group=" + l.OptionGroup);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsetest.txt"), sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        static int DimDemo(string img, string outp)
        {
            try
            {
                var bmp = new Bitmap(img);
                if (bmp.Width != 1030 || bmp.Height != 890)
                {
                    var t = new Bitmap(1030, 890, PixelFormat.Format32bppArgb);
                    using (var g0 = Graphics.FromImage(t)) g0.DrawImage(bmp, 0, 0, 1030, 890);
                    bmp.Dispose();
                    bmp = t;
                }
                if (!EnsureOcr()) return 2;
                var lines = Ocr.Recognize(bmp);
                Parse.Classify(lines, bmp.Height);
                var orig = new Bitmap(bmp);
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var dim = new SolidBrush(Color.FromArgb(122, 6, 6, 8)))
                        g.FillRectangle(dim, 0, 0, bmp.Width, bmp.Height);
                    var old = g.CompositingMode;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.Transparent))
                    {
                        foreach (var l in lines)
                        {
                            if (l.IsChrome || l.OptionGroup == -2) continue;
                            g.FillRectangle(clear, l.X - 3, l.Y - 2, l.W + 6, l.H + 4);
                        }
                    }
                    g.CompositingMode = old;
                    foreach (var l in lines)
                    {
                        if (l.IsChrome || l.OptionGroup == -2) continue;
                        var dst = new Rectangle(l.X - 3, l.Y - 2, l.W + 6, l.H + 4);
                        g.DrawImage(orig, dst, new Rectangle(dst.X, dst.Y, dst.Width, dst.Height), GraphicsUnit.Pixel);
                    }
                }
                orig.Dispose();
                bmp.Save(outp, ImageFormat.Png);
                bmp.Dispose();
                Ocr.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(outp + ".err", ex.ToString(), new UTF8Encoding(false)); } catch { }
                return 1;
            }
        }

        static int CropDemo(string img, string outdir, string kind)
        {
            try
            {
                Directory.CreateDirectory(outdir);
                var src = new Bitmap(img);
                var resized = new Bitmap(1600, 900, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, 1600, 900);
                }
                src.Dispose();
                Rectangle rel = Capture.RelRegion(kind, 1600, 900);
                var crop = resized.Clone(rel, PixelFormat.Format32bppArgb);
                resized.Dispose();
                crop.Save(Path.Combine(outdir, "crop.png"), ImageFormat.Png);
                if (!EnsureOcr()) { File.WriteAllText(Path.Combine(outdir, "demo-error.txt"), "ocr not ready", new UTF8Encoding(false)); return 2; }
                var lines = Ocr.Recognize(crop);
                Parse.Classify(lines, crop.Height);
                var sb = new StringBuilder();
                sb.AppendLine("region=" + rel.X + "," + rel.Y + "," + rel.Width + "," + rel.Height + " crop=" + crop.Width + "x" + crop.Height);
                sb.AppendLine("lines=" + lines.Count + " floor=" + Parse.FloorId + " " + Parse.FloorName + " title=" + Parse.TitleOf(lines));
                foreach (var l in lines) sb.AppendLine(l.Text + "  [" + l.X + "," + l.Y + " " + l.W + "x" + l.H + "]");
                File.WriteAllText(Path.Combine(outdir, "lines.txt"), sb.ToString(), new UTF8Encoding(false));
                crop.Dispose();
                Ocr.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(outdir, "demo-error.txt"), ex.ToString(), new UTF8Encoding(false)); } catch { }
                return 1;
            }
        }

        static int CaptureDemo(string hwndArg, string outdir, string kind)
        {
            try
            {
                Directory.CreateDirectory(outdir);
                IntPtr h = new IntPtr(long.Parse(hwndArg));
                Rectangle rect;
                double sx = 1, sy = 1;
                Bitmap shot = Capture.GrabRegion(h, kind, out rect, out sx, out sy);
                if (shot == null) { File.WriteAllText(Path.Combine(outdir, "demo-error.txt"), "grab failed", new UTF8Encoding(false)); return 1; }
                shot.Save(Path.Combine(outdir, "crop.png"), ImageFormat.Png);
                if (!EnsureOcr()) { File.WriteAllText(Path.Combine(outdir, "demo-error.txt"), "ocr not ready", new UTF8Encoding(false)); return 2; }
                var lines = Ocr.Recognize(shot);
                Parse.Classify(lines, shot.Height);
                var sb = new StringBuilder();
                sb.AppendLine("rect=" + rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height + " size=" + shot.Width + "x" + shot.Height);
                sb.AppendLine("lines=" + lines.Count + " floor=" + Parse.FloorId + " " + Parse.FloorName);
                foreach (var l in lines) sb.AppendLine(l.Text + "  [" + l.X + "," + l.Y + " " + l.W + "x" + l.H + "]");
                File.WriteAllText(Path.Combine(outdir, "lines.txt"), sb.ToString(), new UTF8Encoding(false));
                shot.Dispose();
                Ocr.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(outdir, "demo-error.txt"), ex.ToString(), new UTF8Encoding(false)); } catch { }
                return 1;
            }
        }

        static void RunSelfTest(string imagePath)
        {
            string outp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-out.json");
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                Party.Load(exeDir);
                Bitmap shot;
                if (!string.IsNullOrEmpty(imagePath)) shot = new Bitmap(imagePath);
                else
                {
                    IntPtr h = Capture.FindGame();
                    if (h == IntPtr.Zero) { File.WriteAllText(outp, "{\"error\":\"no game window\"}"); return; }
                    Rectangle rr;
                    shot = Capture.Grab(h, out rr);
                    if (shot == null) { File.WriteAllText(outp, "{\"error\":\"capture failed\"}"); return; }
                }
                if (!Ocr.Start(exeDir)) { File.WriteAllText(outp, "{\"error\":\"ocr start failed\"}"); shot.Dispose(); return; }
                var lines = Ocr.Recognize(shot);
                Parse.Classify(lines, shot.Height);
                Party.Scan(lines, shot.Width);
                var opts = new List<object>();
                foreach (var l in lines)
                {
                    if (!l.IsOption || l.OptionGroup < 0) continue;
                    var d = new Dictionary<string, object>();
                    d["text"] = l.Text;
                    d["check"] = l.Check;
                    d["reqType"] = l.ReqType;
                    d["reqName"] = l.ReqName;
                    d["reqMin"] = l.ReqMin;
                    opts.Add(d);
                }
                var texts = new List<string>();
                foreach (var l in lines) texts.Add(l.Text);
                var party = new List<string>();
                foreach (var k in Party.NameToChar.Keys) party.Add(k + "=" + Party.NameToChar[k]);
                var rec = new Dictionary<string, object>();
                rec["lines"] = lines.Count;
                rec["floorId"] = Parse.FloorId;
                rec["floorName"] = Parse.FloorName;
                rec["title"] = Parse.TitleOf(lines);
                rec["options"] = opts;
                rec["texts"] = texts;
                rec["party"] = party;
                rec["local"] = Party.LocalCharacter;
                File.WriteAllText(outp, new JavaScriptSerializer().Serialize(rec), new UTF8Encoding(false));
                shot.Dispose();
                Ocr.Stop();
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(outp, "{\"error\":" + new JavaScriptSerializer().Serialize(ex.Message) + "}"); } catch { }
            }
        }

        static void Log(string line)
        {
            try { if (GameHwnd != IntPtr.Zero) Native.EnableWindow(GameHwnd, true); } catch { }
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event-recorder.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + line + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}



































































