// UI 探针：加载 ExpeditionRecorder.exe，实例化 已記錄 / 编辑窗体，截图 + 量控件边界。
// 仅用于临时 UI 审查，不进入产品构建。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Probe
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    struct RECT { public int L, T, R, B; }

    static Assembly Asm;
    static string OutDir;
    static StringBuilder Log = new StringBuilder();

    [STAThread]
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PROBE-ERROR: " + ex);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "probe-crash.txt"), ex.ToString()); } catch { }
            return 1;
        }
    }

    static int Run(string[] args)
    {
        // args: <exe> <outdir> <scale> <kind> <w> <h> [dataFile]
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Asm = Assembly.UnsafeLoadFrom(Path.GetFullPath(args[0]));
        OutDir = args[1];
        Directory.CreateDirectory(OutDir);
        float scale = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
        string kind = args[3];
        int w = int.Parse(args[4]);
        int h = int.Parse(args[5]);
        string dataFile = args.Length > 6 ? args[6] : null;

        Asm.GetType("ExpeditionRecorder.UiScale").GetProperty("Value").SetValue(null, scale, null);
        string lang = Environment.GetEnvironmentVariable("PROBE_LANG");
        if (!string.IsNullOrEmpty(lang))
            Asm.GetType("ExpeditionRecorder.UiText").GetField("Lang").SetValue(null,
                Enum.Parse(Asm.GetType("ExpeditionRecorder.UiLang"), lang));

        Form f;
        if (kind == "__records")
        {
            f = (Form)Asm.GetType("ExpeditionRecorder.RecordsForm").GetConstructor(Type.EmptyTypes).Invoke(null);
        }
        else
        {
            var data = LoadRecord(dataFile, kind);
            var t = Asm.GetType("ExpeditionRecorder.RecordEditForm");
            if (Environment.GetEnvironmentVariable("PROBE_ENTRY") == "1")
            {
                var et = Asm.GetType("ExpeditionRecorder.RecordEntry");
                var entry = Activator.CreateInstance(et);
                string file = null, line = null;
                foreach (var p2 in Directory.GetFiles(dataFile, "events-*.jsonl"))
                {
                    var ln = File.ReadAllLines(p2);
                    if (ln.Length > 0 && ln[0].Trim().Length > 0) { file = p2; line = ln[0]; break; }
                }
                et.GetField("File").SetValue(entry, file);
                et.GetField("Line").SetValue(entry, line);
                et.GetField("Kind").SetValue(entry, kind);
                et.GetField("Data").SetValue(entry, data);
                f = (Form)Activator.CreateInstance(t, new object[] { entry });
            }
            else
            {
                var ctor = t.GetConstructor(new Type[] { typeof(string), typeof(Dictionary<string, object>), typeof(Action<Dictionary<string, object>>), typeof(bool) });
                f = (Form)ctor.Invoke(new object[] { kind, data, null, false });
            }
        }

        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.TopMost = false;
        f.ShowInTaskbar = false;
        f.ClientSize = new Size(w, h);
        f.Show();
        Pump(700);
        f.ClientSize = new Size(w, h);
        Pump(500);

        string tag = kind + "-" + w + "x" + h + "-s" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        string png = Path.Combine(OutDir, tag + ".png");
        Capture(f, png);
        string pngc = Path.Combine(OutDir, tag + "-client.png");
        CaptureClient(f, pngc);
        var pfield = f.GetType().GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic);
        if (pfield != null)
        {
            var panel = pfield.GetValue(f) as Control;
            if (panel != null) CaptureControl(panel, Path.Combine(OutDir, tag + "-panel.png"));
        }
        Dump(f, tag);
        if (Environment.GetEnvironmentVariable("PROBE_FLOW") == "1")
        {
            Panel sp = null;
            if (kind == "__records")
            {
                var ef = f.GetType().GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic);
                object editor = ef == null ? null : ef.GetValue(f);
                if (editor != null)
                {
                    var pf2 = editor.GetType().GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic);
                    sp = pf2 == null ? null : pf2.GetValue(editor) as Panel;
                }
            }
            else sp = f.GetType().GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f) as Panel;
            if (sp != null)
            {
                Log.AppendLine("---- FLOW ----");
                Log.AppendLine("start scroll=" + sp.AutoScrollPosition + " client=" + sp.ClientSize + " width=" + sp.Width);
                int max = sp.VerticalScroll.Maximum - sp.VerticalScroll.LargeChange + 1;
                sp.AutoScrollPosition = new Point(0, Math.Max(0, max));
                Pump(400);
                Log.AppendLine("scrolled=" + sp.AutoScrollPosition + " max=" + max);
                CaptureClient(f, Path.Combine(OutDir, tag + "-flow1.png"));
                int newW = Math.Max(700, f.ClientSize.Width - 260);
                f.ClientSize = new Size(newW, f.ClientSize.Height);
                Pump(1000);
                Log.AppendLine("after resize w=" + f.ClientSize.Width + " scroll=" + sp.AutoScrollPosition);
                var lm = f.GetType().GetMethod("LayoutRecords", BindingFlags.Instance | BindingFlags.NonPublic);
                if (lm != null) { try { lm.Invoke(f, null); Log.AppendLine("manual LayoutRecords ok"); } catch (Exception ex2) { Log.AppendLine("manual LayoutRecords threw: " + ex2.InnerException); } Pump(500); }
                CaptureClient(f, Path.Combine(OutDir, tag + "-flow2.png"));
                var lwF = f.GetType().GetField("_listW", BindingFlags.Instance | BindingFlags.NonPublic);
                if (lwF != null && lm != null)
                {
                    lwF.SetValue(f, 9999);
                    try { lm.Invoke(f, null); } catch { }
                    Pump(400);
                    var hostF2 = f.GetType().GetField("_editorHost", BindingFlags.Instance | BindingFlags.NonPublic);
                    var host2 = hostF2 == null ? null : hostF2.GetValue(f) as Control;
                    Log.AppendLine("after drag list to max: host=" + (host2 == null ? "-" : host2.Bounds.ToString()) + " panel=" + sp.ClientSize);
                    CaptureClient(f, Path.Combine(OutDir, tag + "-flow4.png"));
                }
                sp.AutoScrollPosition = new Point(0, 0);
                Pump(500);
                Log.AppendLine("back to top scroll=" + sp.AutoScrollPosition);
                CaptureClient(f, Path.Combine(OutDir, tag + "-flow3.png"));
                var first = sp.Controls.Count > 0 ? sp.Controls[0] : null;
                Log.AppendLine("panel client=" + sp.ClientSize + " controls=" + sp.Controls.Count + " first=" + (first == null ? "-" : first.GetType().Name + " at " + first.Location));
                var hostF = f.GetType().GetField("_editorHost", BindingFlags.Instance | BindingFlags.NonPublic);
                if (hostF != null) { var host = hostF.GetValue(f) as Control; Log.AppendLine("host=" + host.Bounds + " children=" + host.Controls.Count + (host.Controls.Count > 0 ? " child=" + host.Controls[0].Bounds : "")); }
            }
        }
        Behavior(f, tag);
        if (Environment.GetEnvironmentVariable("PROBE_EDIT") == "1")
        {
            TextBox target = null;
            foreach (var c2 in AllControls(f)) { var t2 = c2 as TextBox; if (t2 != null && !t2.Multiline) { target = t2; break; } }
            if (target != null)
            {
                target.Text = target.Text + "（测试）";
                Pump(1600);
                Log.AppendLine("---- EDIT ----");
                Log.AppendLine("text=" + target.Text);
                var sf = f.GetType().GetField("_headerStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                var lbl = sf == null ? null : sf.GetValue(f) as Label;
                Log.AppendLine("status=" + (lbl == null ? "-" : lbl.Text));
                Log.AppendLine("dirty=" + Convert.ToString(f.GetType().GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f)));
            }
        }
        if (Environment.GetEnvironmentVariable("PROBE_RESIZE") == "1")
        {
            f.ClientSize = new Size(w + 60, h);
            Pump(900);
            Log.AppendLine("---- AFTER RESIZE +60 ----");
            Dump(f, tag + "-resized");
        }
        f.Close();
        f.Dispose();
        File.WriteAllText(Path.Combine(OutDir, tag + ".txt"), Log.ToString(), new UTF8Encoding(false));
        return 0;
    }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(8); }
    }

    static void Capture(Form f, string path)
    {
        RECT wr; GetWindowRect(f.Handle, out wr);
        int ww = wr.R - wr.L, wh = wr.B - wr.T;
        using (var bmp = new Bitmap(Math.Max(1, ww), Math.Max(1, wh)))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(f.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
                if (!ok)
                {
                    g.Clear(Color.Magenta);
                }
            }
            bmp.Save(path, ImageFormat.Png);
        }
        Log.AppendLine("WINDOW " + ww + "x" + wh + " client=" + f.ClientSize.Width + "x" + f.ClientSize.Height
            + " dpi=" + f.DeviceDpi + " vsb=" + GetSystemMetrics(2));
    }

    static void CaptureClient(Form f, string path)
    {
        using (var bmp = new Bitmap(Math.Max(1, f.ClientSize.Width), Math.Max(1, f.ClientSize.Height)))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(f.Handle, hdc, 1);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    static void Behavior(Form f, string tag)
    {
        var pfield = f.GetType().GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic);
        var panel = pfield == null ? null : pfield.GetValue(f) as Panel;
        Log.AppendLine("---- BEHAVIOR ----");
        foreach (Control c in AllControls(f))
        {
            var tb = c as TextBox;
            if (tb == null || !tb.Multiline) continue;
            int lines = tb.IsHandleCreated ? tb.GetLineFromCharIndex(tb.TextLength) + 1 : -1;
            int lastY = tb.IsHandleCreated && tb.TextLength > 0 ? tb.GetPositionFromCharIndex(tb.TextLength - 1).Y : -1;
            int need = lastY >= 0 ? lastY + tb.Font.Height + 6 : -1;
            Log.AppendLine("TEXTBOX h=" + tb.Height + " lines=" + lines + " lastY=" + lastY
                + " fontH=" + tb.Font.Height + " need=" + need + " overflow=" + (need > tb.Height)
                + " w=" + tb.Width + " | " + (tb.Text.Length > 12 ? tb.Text.Substring(0, 12) : tb.Text).Replace("\r", " ").Replace("\n", " "));
        }
        if (panel != null && panel.VerticalScroll.Visible)
        {
            int max = panel.VerticalScroll.Maximum - panel.VerticalScroll.LargeChange + 1;
            panel.AutoScrollPosition = new Point(0, max);
            Pump(200);
            Log.AppendLine("scroll after set max: " + panel.AutoScrollPosition + " max=" + max);
            f.ClientSize = new Size(f.ClientSize.Width + 60, f.ClientSize.Height);
            Pump(700);
            Log.AppendLine("scroll after resize +60: " + panel.AutoScrollPosition + "  (0 = jumped back to top)");
            Log.AppendLine("panel client after resize: " + panel.ClientSize.Width + " scrollV=" + panel.VerticalScroll.Visible);
        }
    }

    static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllControls(c)) yield return d;
        }
    }

    static void CaptureControl(Control c, string path)
    {
        RECT r; GetWindowRect(c.Handle, out r);
        int w = Math.Max(1, r.R - r.L), h = Math.Max(1, r.B - r.T);
        using (var bmp = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(c.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    static void Dump(Form f, string tag)
    {
        Point origin = f.PointToScreen(Point.Empty);
        Log.AppendLine("FORM " + f.GetType().Name + " client=" + f.ClientSize.Width + "x" + f.ClientSize.Height);
        var t = f.GetType();
        var pw = t.GetMethod("PanelWidth", BindingFlags.Instance | BindingFlags.NonPublic);
        var lw = t.GetMethod("FieldLabelWidth", BindingFlags.Instance | BindingFlags.NonPublic);
        var gw = t.GetMethod("FieldGap", BindingFlags.Instance | BindingFlags.NonPublic);
        Log.AppendLine("PanelWidth()=" + (pw == null ? "-" : Convert.ToString(pw.Invoke(f, null))) + " FieldLabelWidth()=" + (lw == null ? "-" : Convert.ToString(lw.Invoke(f, null))) + " FieldGap()=" + (gw == null ? "-" : Convert.ToString(gw.Invoke(f, null))));
        var ff = t.GetField("_optionFrames", BindingFlags.Instance | BindingFlags.NonPublic);
        var frames = ff == null ? null : ff.GetValue(f) as IEnumerable;
        if (frames != null)
        {
            foreach (var fr in frames)
            {
                var rect = (Rectangle)fr.GetType().GetField("Rect").GetValue(fr);
                Log.AppendLine("FRAME " + rect);
            }
        }
        Walk(f, origin, 0, f.ClientSize.Width);
    }

    static void Walk(Control parent, Point origin, int depth, int clientW)
    {
        foreach (Control c in parent.Controls)
        {
            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
            r.Offset(-origin.X, -origin.Y);
            string text = c.Text ?? "";
            if (text.Length > 18) text = text.Substring(0, 18) + "…";
            text = text.Replace("\r", " ").Replace("\n", " ");
            string extra = "";
            var tb = c as TextBox;
            if (tb != null) extra = " multi=" + tb.Multiline + " lines=" + tb.Lines.Length;
            var panel = c as Panel;
            if (panel != null) extra = " autoScroll=" + panel.AutoScroll + " vScroll=" + panel.VerticalScroll.Visible
                + " hScroll=" + panel.HorizontalScroll.Visible + " disp=" + panel.DisplayRectangle;
            Log.AppendLine(new string(' ', depth * 2) + c.GetType().Name
                + " [" + r.X + "," + r.Y + " " + r.Width + "x" + r.Height + "]"
                + " R=" + r.Right + " (dR=" + (clientW - r.Right) + ")"
                + (c.Visible ? "" : " HIDDEN") + extra
                + " | " + text);
            if (c.HasChildren) Walk(c, origin, depth + 1, clientW);
        }
    }

    static Dictionary<string, object> LoadRecord(string file, string kind)
    {
        string prefix = kind == "事件" ? "events-" : kind == "物品" ? "items-" : kind == "樓層" ? "floors-"
            : kind == "出口" ? "exits-" : kind == "結局" ? "endings-" : "shops-";
        var ser = new JavaScriptSerializer();
        int want = 0;
        int.TryParse(Environment.GetEnvironmentVariable("PROBE_INDEX") ?? "0", out want);
        int seen = 0;
        foreach (var path in Directory.GetFiles(file, prefix + "*.jsonl"))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Trim().Length == 0) continue;
                var o = ser.Deserialize<Dictionary<string, object>>(line);
                if (o == null) continue;
                if (seen++ == want) return Normalize(o) as Dictionary<string, object>;
            }
        }
        throw new Exception("no record for " + kind + " in " + file);
    }

    static object Normalize(object o)
    {
        var d = o as Dictionary<string, object>;
        if (d != null)
        {
            var keys = new List<string>(d.Keys);
            foreach (var k in keys) d[k] = Normalize(d[k]);
            return d;
        }
        var arr = o as object[];
        if (arr != null)
        {
            var al = new ArrayList();
            foreach (var x in arr) al.Add(Normalize(x));
            return al;
        }
        var al2 = o as ArrayList;
        if (al2 != null)
        {
            for (int i = 0; i < al2.Count; i++) al2[i] = Normalize(al2[i]);
            return al2;
        }
        return o;
    }
}
