using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ExpeditionArchive
{
    static class Program
    {
        static string RepoRoot;

        [STAThread]
        static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--repo" && i + 1 < args.Length) RepoRoot = args[++i];
            if (!string.IsNullOrEmpty(RepoRoot))
            {
                RepoRoot = RepoRoot.Trim().Trim('"').Trim();
                if (RepoRoot.Length > 3) RepoRoot = RepoRoot.TrimEnd('\\');
            }
            if (string.IsNullOrEmpty(RepoRoot))
            {
                string p = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 7 && p != null; i++)
                {
                    if (Directory.Exists(Path.Combine(p, "wiki", "content"))) { RepoRoot = p; break; }
                    p = Path.GetDirectoryName(p.TrimEnd('\\'));
                }
            }
            if (args.Length > 1 && args[0] == "--check") return CliCheck(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { LogCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { LogCrash(e.ExceptionObject as Exception); };
            Application.Run(new ImportForm());
            return 0;
        }

        static int CliCheck(string path)
        {
            var log = new StringBuilder();
            try
            {
                var d = ImportForm.ReadPackage(path);
                log.AppendLine("package: " + path);
                log.AppendLine("exported: " + ImportForm.S(d, "exported"));
                log.AppendLine("events=" + ImportForm.AsList(d, "events").Count
                    + " items=" + ImportForm.AsList(d, "items").Count
                    + " floors=" + ImportForm.AsList(d, "floors").Count
                    + " exits=" + ImportForm.AsList(d, "exits").Count
                    + " endings=" + ImportForm.AsList(d, "endings").Count);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string staging = Path.Combine(RepoRoot, "wiki", "_incoming", "player-" + stamp);
                Directory.CreateDirectory(staging);
                ImportForm.WriteJsonl(staging, "events-import.jsonl", ImportForm.AsList(d, "events"));
                ImportForm.WriteJsonl(staging, "items-import.jsonl", ImportForm.AsList(d, "items"));
                ImportForm.WriteJsonl(staging, "floors-import.jsonl", ImportForm.AsList(d, "floors"));
                ImportForm.WriteJsonl(staging, "exits-import.jsonl", ImportForm.AsList(d, "exits"));
                ImportForm.WriteJsonl(staging, "endings-import.jsonl", ImportForm.AsList(d, "endings"));
                log.AppendLine("staging: " + staging);
                string importer = Path.Combine(RepoRoot, "客户端", "Recorder", "structured", "import-manual.mjs");
                var psi = new ProcessStartInfo("node", "\"" + importer + "\" --dir \"" + staging + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.WorkingDirectory = RepoRoot;
                var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                log.AppendLine("exit=" + p.ExitCode);
                log.AppendLine(outp);
                if (err.Length > 0) log.AppendLine(err);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "check-out.txt"), log.ToString(), new UTF8Encoding(false));
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR " + ex);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "check-out.txt"), log.ToString(), new UTF8Encoding(false));
                return 1;
            }
        }

        static void LogCrash(Exception ex)
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExpeditionArchive-error.log");
                File.WriteAllText(p, (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
            MessageBox.Show(ex == null ? "未知錯誤" : ex.ToString(), "Expedition Archive 錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        class ImportForm : Form
        {
            TextBox Package;
            TextBox Output;
            Label Status;
            ProgressBar Progress;
            Timer Pulse;
            DateTime Started;
            string Phase = "";
            Button CheckBtn, ApplyBtn, StagingBtn;
            string _staging = "";

            public ImportForm()
            {
                Text = "Expedition Archive — 玩家資料包導入";
                ClientSize = new Size(780, 580);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                BackColor = Color.FromArgb(28, 28, 31);
                ForeColor = Color.FromArgb(230, 230, 235);

                var l = new Label();
                l.Text = "玩家資料包";
                l.SetBounds(14, 17, 80, 20);
                l.ForeColor = Color.FromArgb(190, 190, 198);
                Controls.Add(l);
                Package = new TextBox();
                Package.SetBounds(96, 13, 540, 24);
                Package.BackColor = Color.FromArgb(40, 40, 44);
                Package.ForeColor = Color.White;
                Package.BorderStyle = BorderStyle.FixedSingle;
                Controls.Add(Package);
                var browse = new Button();
                browse.Text = "選擇 記錄-*.zip";
                browse.SetBounds(644, 11, 122, 27);
                browse.FlatStyle = FlatStyle.Flat;
                browse.BackColor = Color.FromArgb(44, 44, 47);
                browse.ForeColor = Color.White;
                browse.Click += delegate
                {
                    using (var d = new OpenFileDialog())
                    {
                        d.Filter = "玩家資料包 (*.zip;*.json)|*.zip;*.json|Zip (*.zip)|*.zip|JSON (*.json)|*.json|所有檔案 (*.*)|*.*";
                        if (d.ShowDialog() == DialogResult.OK) { Package.Text = d.FileName; LoadPackage(); }
                    }
                };
                Controls.Add(browse);

                CheckBtn = MkBtn("只檢查（不改 Wiki）", 14, 50, 170);
                CheckBtn.Click += delegate { RunSafe(false); };
                ApplyBtn = MkBtn("導入 Wiki", 194, 50, 130);
                ApplyBtn.Click += delegate { RunSafe(true); };
                StagingBtn = MkBtn("開啟暫存資料夾", 334, 50, 150);
                StagingBtn.Click += delegate { OpenStaging(); };
                var openWiki = MkBtn("開啟 Wiki 目錄", 494, 50, 140);
                openWiki.Click += delegate { OpenWiki(); };
                var rebuild = MkBtn("重建 Wiki", 644, 50, 122);
                rebuild.Click += delegate { Rebuild(); };

                Status = new Label();
                Status.Text = "先選玩家的 記錄-*.zip；「只檢查」不會改正式 Wiki。";
                Status.SetBounds(14, 94, 752, 22);
                Status.ForeColor = Color.FromArgb(190, 190, 198);
                Controls.Add(Status);

                Progress = new ProgressBar();
                Progress.SetBounds(14, 118, 752, 14);
                Progress.Style = ProgressBarStyle.Marquee;
                Progress.MarqueeAnimationSpeed = 25;
                Progress.Visible = false;
                Controls.Add(Progress);

                Output = new TextBox();
                Output.Multiline = true;
                Output.ReadOnly = true;
                Output.ScrollBars = ScrollBars.Both;
                Output.WordWrap = false;
                Output.SetBounds(14, 140, 752, 420);
                Output.BackColor = Color.FromArgb(22, 22, 25);
                Output.ForeColor = Color.FromArgb(220, 220, 226);
                Output.BorderStyle = BorderStyle.FixedSingle;
                Output.Font = new Font("Consolas", 9.5F);
                Controls.Add(Output);

                Pulse = new Timer();
                Pulse.Interval = 1000;
                Pulse.Tick += delegate
                {
                    if (!Progress.Visible) return;
                    int sec = (int)(DateTime.Now - Started).TotalSeconds;
                    Status.Text = Phase + "… 已跑 " + sec + " 秒";
                };

                WireDrop(this);
                WireDrop(Package);
                WireDrop(Output);
            }

            Button MkBtn(string text, int x, int y, int w)
            {
                var b = new Button();
                b.Text = text;
                b.SetBounds(x, y, w, 32);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(44, 44, 47);
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 84);
                Controls.Add(b);
                return b;
            }

            void WireDrop(Control c)
            {
                c.AllowDrop = true;
                c.DragEnter += delegate (object s, DragEventArgs e)
                {
                    e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                };
                c.DragDrop += delegate (object s, DragEventArgs e)
                {
                    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files == null || files.Length == 0) return;
                    Package.Text = CleanPath(files[0]);
                    LoadPackage();
                };
            }

            static string CleanPath(string path)
            {
                string s = (path ?? "").Trim().Trim('"').Trim();
                var sb = new StringBuilder(s.Length);
                foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
                return sb.ToString();
            }

            /* ── 讀包 ─────────────────────────────────────── */
            Dictionary<string, object> _data;

            void LoadPackage()
            {
                try
                {
                    string path = CleanPath(Package.Text);
                    _data = ReadPackage(path);
                    Output.Text = BuildSummary(path, _data);
                    Status.ForeColor = Color.FromArgb(190, 190, 198);
                    Status.Text = "已讀取資料包。按「只檢查」看會導入什麼。";
                }
                catch (Exception ex)
                {
                    _data = null;
                    Status.ForeColor = Color.FromArgb(220, 90, 80);
                    Status.Text = "資料包錯誤：" + ex.Message;
                    Output.Text = ex.ToString();
                }
            }

            public static Dictionary<string, object> ReadPackage(string path)
            {
                if (string.IsNullOrEmpty(path)) throw new Exception("沒有選資料包");
                if (!File.Exists(path)) throw new Exception("找不到檔案：" + path);
                string json;
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    json = File.ReadAllText(path, Encoding.UTF8);
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var z = ZipFile.OpenRead(path))
                    {
                        ZipArchiveEntry entry = z.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').TrimStart('/') == "記錄.json");
                        if (entry == null) entry = z.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                        if (entry == null) throw new Exception("壓縮檔裡找不到 記錄.json");
                        using (var sr = new StreamReader(entry.Open(), Encoding.UTF8)) json = sr.ReadToEnd();
                    }
                }
                else throw new Exception("只支援 .zip 或 .json");
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 64 * 1024 * 1024;
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) throw new Exception("記錄.json 解析失敗");
                int total = 0;
                foreach (var k in new string[] { "events", "items", "floors", "exits", "endings" })
                    total += AsList(d, k).Count;
                if (total == 0) throw new Exception("資料包裡沒有任何記錄");
                return d;
            }

            public static ArrayList AsList(Dictionary<string, object> d, string key)
            {
                if (d == null || !d.ContainsKey(key)) return new ArrayList();
                return d[key] as ArrayList ?? new ArrayList();
            }

            public static string S(Dictionary<string, object> d, string k)
            {
                if (d == null || !d.ContainsKey(k) || d[k] == null) return "";
                return Convert.ToString(d[k]);
            }

            string BuildSummary(string path, Dictionary<string, object> d)
            {
                var sb = new StringBuilder();
                sb.AppendLine("資料包：" + Path.GetFileName(path));
                sb.AppendLine("路徑：" + path);
                sb.AppendLine("導出時間：" + S(d, "exported"));
                sb.AppendLine();
                sb.AppendLine("事件 " + AsList(d, "events").Count
                    + " · 物品 " + AsList(d, "items").Count
                    + " · 樓層 " + AsList(d, "floors").Count
                    + " · 出口 " + AsList(d, "exits").Count
                    + " · 結局 " + AsList(d, "endings").Count);
                sb.AppendLine();

                var events = AsList(d, "events");
                if (events.Count > 0)
                {
                    sb.AppendLine("── 事件 ──");
                    foreach (var o in events)
                    {
                        var e = o as Dictionary<string, object>;
                        if (e == null) continue;
                        string title = "";
                        var steps = e.ContainsKey("steps") ? e["steps"] as ArrayList : null;
                        if (steps != null && steps.Count > 0) title = S(steps[0] as Dictionary<string, object>, "title");
                        if (title == "") title = S(e, "title");
                        sb.AppendLine("  Level " + S(e, "floorId") + " " + S(e, "floorName") + " — " + (title == "" ? "（無標題）" : title));
                    }
                    sb.AppendLine();
                }
                var items = AsList(d, "items");
                if (items.Count > 0)
                {
                    sb.AppendLine("── 物品 ──");
                    foreach (var o in items)
                    {
                        var it = o as Dictionary<string, object>;
                        if (it == null) continue;
                        sb.AppendLine("  " + S(it, "name") + "（" + S(it, "type") + "）");
                    }
                    sb.AppendLine();
                }
                var floors = AsList(d, "floors");
                if (floors.Count > 0)
                {
                    sb.AppendLine("── 樓層介紹 ──");
                    foreach (var o in floors)
                    {
                        var f = o as Dictionary<string, object>;
                        if (f == null) continue;
                        sb.AppendLine("  Level " + S(f, "floorId") + " " + S(f, "floorName"));
                    }
                    sb.AppendLine();
                }
                var exits = AsList(d, "exits");
                if (exits.Count > 0)
                {
                    sb.AppendLine("── 出口 ──");
                    foreach (var o in exits)
                    {
                        var x = o as Dictionary<string, object>;
                        if (x == null) continue;
                        sb.AppendLine("  Level " + S(x, "floorId") + "：" + S(x, "event") + " → " + (S(x, "target") == "" ? "（待處理）" : "Level " + S(x, "target")));
                    }
                    sb.AppendLine();
                }
                var endings = AsList(d, "endings");
                if (endings.Count > 0)
                {
                    sb.AppendLine("── 結局 ──");
                    foreach (var o in endings)
                    {
                        var x = o as Dictionary<string, object>;
                        if (x == null) continue;
                        sb.AppendLine("  " + S(x, "name") + "（" + S(x, "category") + "）");
                    }
                }
                return sb.ToString();
            }

            /* ── 導入 ─────────────────────────────────────── */
            void RunSafe(bool apply)
            {
                try { Run(apply); }
                catch (Exception ex) { LogCrash(ex); }
            }

            void Run(bool apply)
            {
                string root = RepoRoot;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "wiki", "content")))
                {
                    Status.Text = "找不到 Wiki（請把工具放在 BACKROOMS 裡，或加 --repo）。";
                    return;
                }
                string importer = Path.Combine(root, "客户端", "Recorder", "structured", "import-manual.mjs");
                if (!File.Exists(importer)) { Status.Text = "找不到 import-manual.mjs。"; return; }
                if (_data == null) LoadPackage();
                if (_data == null) return;

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string staging = Path.Combine(root, "wiki", "_incoming", "player-" + stamp);
                Directory.CreateDirectory(staging);
                WriteJsonl(staging, "events-import.jsonl", AsList(_data, "events"));
                WriteJsonl(staging, "items-import.jsonl", AsList(_data, "items"));
                WriteJsonl(staging, "floors-import.jsonl", AsList(_data, "floors"));
                WriteJsonl(staging, "exits-import.jsonl", AsList(_data, "exits"));
                WriteJsonl(staging, "endings-import.jsonl", AsList(_data, "endings"));
                File.WriteAllText(Path.Combine(staging, "source.txt"),
                    "package: " + CleanPath(Package.Text) + "\r\nimported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n",
                    new UTF8Encoding(false));
                _staging = staging;

                CheckBtn.Enabled = false;
                ApplyBtn.Enabled = false;
                Output.Clear();
                StartProgress(apply ? "導入 Wiki" : "檢查資料包");

                var t = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        string args = "\"" + importer + "\" --dir \"" + staging + "\"" + (apply ? " --confirm" : "");
                        var psi = new ProcessStartInfo("node", args);
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                        psi.WorkingDirectory = root;
                        var p = new Process();
                        p.StartInfo = psi;
                        var sb = new StringBuilder();
                        p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                        {
                            if (e.Data == null) return;
                            lock (sb) sb.AppendLine(e.Data);
                        };
                        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                        {
                            if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
                        };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();
                        string log;
                        lock (sb) log = sb.ToString();
                        int code = p.ExitCode;
                        BeginInvoke((Action)delegate
                        {
                            StopProgress();
                            Output.Text = log;
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                            if (code == 0)
                            {
                                Status.ForeColor = Color.FromArgb(90, 170, 90);
                                Status.Text = apply ? "已導入 Wiki（備份路徑見上方日誌）。" : "檢查完成（尚未改正式 Wiki）。";
                            }
                            else
                            {
                                Status.ForeColor = Color.FromArgb(220, 90, 80);
                                Status.Text = "失敗：見上方輸出。";
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke((Action)delegate
                        {
                            StopProgress();
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                            Status.ForeColor = Color.FromArgb(220, 90, 80);
                            Status.Text = "失敗：" + ex.Message;
                            Output.Text = ex.ToString();
                        });
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            public static void WriteJsonl(string dir, string name, ArrayList arr)
            {
                if (arr == null || arr.Count == 0) return;
                var ser = new JavaScriptSerializer();
                var sb = new StringBuilder();
                foreach (var o in arr) sb.AppendLine(ser.Serialize(o));
                File.WriteAllText(Path.Combine(dir, name), sb.ToString(), new UTF8Encoding(false));
            }

            void StartProgress(string phase)
            {
                Phase = phase;
                Started = DateTime.Now;
                Progress.Visible = true;
                Progress.Style = ProgressBarStyle.Marquee;
                Pulse.Start();
                Status.Text = Phase + "… 已跑 0 秒";
            }

            void StopProgress()
            {
                Pulse.Stop();
                Progress.Visible = false;
            }

            void Rebuild()
            {
                string root = RepoRoot;
                string script = Path.Combine(root ?? "", "wiki", "content", "build.mjs");
                if (!File.Exists(script)) { Status.Text = "找不到 build.mjs。"; return; }
                StartProgress("重建 Wiki");
                CheckBtn.Enabled = false;
                ApplyBtn.Enabled = false;
                var t = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        var psi = new ProcessStartInfo("node", "\"" + script + "\"");
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                        psi.WorkingDirectory = root;
                        var p = Process.Start(psi);
                        string o = p.StandardOutput.ReadToEnd();
                        string e = p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        BeginInvoke((Action)delegate
                        {
                            StopProgress();
                            Output.Text = o + (e.Length > 0 ? Environment.NewLine + e : "");
                            Status.ForeColor = p.ExitCode == 0 ? Color.FromArgb(90, 170, 90) : Color.FromArgb(220, 90, 80);
                            Status.Text = p.ExitCode == 0 ? "Wiki 已重建：wiki/site/index.html" : "重建失敗：見上方輸出。";
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke((Action)delegate
                        {
                            StopProgress();
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                            Status.ForeColor = Color.FromArgb(220, 90, 80);
                            Status.Text = "重建失敗：" + ex.Message;
                        });
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            void OpenStaging()
            {
                try
                {
                    string dir = !string.IsNullOrEmpty(_staging) ? _staging : Path.Combine(RepoRoot ?? "", "wiki", "_incoming");
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch { }
            }

            void OpenWiki()
            {
                try
                {
                    string dir = Path.Combine(RepoRoot ?? "", "wiki", "content");
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch { }
            }
        }
    }
}


