using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ExpeditionArchive
{
    static class Program
    {
        static string RepoRoot;

        [STAThread]
        static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--repo" && i + 1 < args.Length) RepoRoot = args[++i];
            if (string.IsNullOrEmpty(RepoRoot))
            {
                string p = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && p != null; i++)
                {
                    if (Directory.Exists(Path.Combine(p, "wiki", "content"))) { RepoRoot = p; break; }
                    p = Path.GetDirectoryName(p.TrimEnd('\\'));
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { LogCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { LogCrash(e.ExceptionObject as Exception); };
            Application.Run(new ImportForm());
        }

        static void LogCrash(Exception ex)
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "ExpeditionArchive-error.log");
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
            Button CheckBtn, ApplyBtn;

            public ImportForm()
            {
                Text = "Expedition Archive";
                ClientSize = new Size(720, 460);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;

                var l = new Label();
                l.Text = "紀錄包";
                l.SetBounds(14, 16, 50, 20);
                Controls.Add(l);
                Package = new TextBox();
                Package.SetBounds(64, 12, 500, 24);
                Controls.Add(Package);
                var browse = new Button();
                browse.Text = "選擇 .expedition.zip";
                browse.SetBounds(574, 11, 132, 26);
                browse.Click += delegate
                {
                    using (var d = new OpenFileDialog())
                    {
                        d.Filter = "紀錄包 (*.expedition.zip)|*.expedition.zip|Zip (*.zip)|*.zip|所有檔案 (*.*)|*.*";
                        if (d.ShowDialog() == DialogResult.OK) Package.Text = d.FileName;
                    }
                };
                Controls.Add(browse);

                CheckBtn = new Button();
                CheckBtn.Text = "只匯入並檢查";
                CheckBtn.SetBounds(14, 50, 150, 32);
                CheckBtn.Click += delegate { RunSafe(false); };
                Controls.Add(CheckBtn);

                ApplyBtn = new Button();
                ApplyBtn.Text = "整合進 Wiki";
                ApplyBtn.SetBounds(176, 50, 150, 32);
                ApplyBtn.Click += delegate { RunSafe(true); };
                Controls.Add(ApplyBtn);

                var open = new Button();
                open.Text = "開啟 staging";
                open.SetBounds(338, 50, 120, 32);
                open.Click += delegate { OpenStaging(); };
                Controls.Add(open);

                Status = new Label();
                Status.Text = "先選紀錄包；「只匯入並檢查」不會改正式 Wiki。";
                Status.SetBounds(14, 94, 690, 22);
                Controls.Add(Status);

                Progress = new ProgressBar();
                Progress.SetBounds(14, 118, 690, 14);
                Progress.Minimum = 0;
                Progress.Maximum = 100;
                Progress.Style = ProgressBarStyle.Marquee;
                Progress.MarqueeAnimationSpeed = 25;
                Progress.Visible = false;
                Controls.Add(Progress);

                Output = new TextBox();
                Output.Multiline = true;
                Output.ReadOnly = true;
                Output.ScrollBars = ScrollBars.Both;
                Output.SetBounds(14, 140, 690, 304);
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
                    AcceptPackage(files[0]);
                };
            }

            void AcceptPackage(string path)
            {
                Package.Text = CleanPath(path);
                if (!ValidatePackage(Package.Text)) return;
                Status.ForeColor = Color.FromArgb(60, 60, 64);
                Status.Text = "已拖入紀錄包，開始匯入檢查…";
                RunSafe(false);
            }

            static string CleanPath(string path)
            {
                string s = (path ?? "").Trim().Trim('"').Trim();
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    if (!char.IsControl(c)) sb.Append(c);
                }
                return sb.ToString();
            }

            void RunSafe(bool apply)
            {
                try { Run(apply); }
                catch (Exception ex) { LogCrash(ex); }
            }

            bool ValidatePackage(string path)
            {
                try
                {
                    path = CleanPath(path);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        throw new Exception("找不到紀錄包：" + path);
                    if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        throw new Exception("不是 .zip／.expedition.zip：" + path);
                    using (ZipArchive z = ZipFile.OpenRead(path))
                    {
                        bool has = z.Entries.Any(e => e.FullName.Replace('\\', '/').TrimStart('/') == "staging/validation.json");
                        if (!has) throw new Exception("紀錄包缺少 staging/validation.json，不能匯入");
                    }
                    Status.ForeColor = Color.FromArgb(60, 60, 64);
                    Status.Text = "紀錄包格式正常。";
                    return true;
                }
                catch (Exception ex)
                {
                    Status.ForeColor = Color.FromArgb(200, 70, 60);
                    Status.Text = "紀錄包錯誤：" + ex.Message;
                    Output.Text = "紀錄包錯誤：" + ex.Message + Environment.NewLine + path;
                    return false;
                }
            }

            string FirstError(string log)
            {
                if (string.IsNullOrEmpty(log)) return "沒有輸出";
                /* PowerShell 的 NativeCommandError 會整段包成 CLIXML；直接顯示 XML 對使用者沒用。
                   抽出 <S S="Error">…</S> 的正文，沒有就退回第一行。 */
                if (log.IndexOf("CLIXML", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(log, "<S S=\"Error\">(.*?)</S>", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (matches.Count > 0)
                    {
                        string m = matches[0].Groups[1].Value.Replace("_x000D_", "").Replace("_x000A_", " ").Trim();
                        if (m.Length > 180) m = m.Substring(0, 180);
                        if (m.Length > 0) return m;
                    }
                }
                foreach (string line in log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("+") || t.StartsWith("At ") || t.StartsWith("CategoryInfo") ||
                        t.StartsWith("FullyQualifiedErrorId") || t.StartsWith("所在位置")) continue;
                    if (t.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.StartsWith("Exception", StringComparison.OrdinalIgnoreCase) ||
                        t.IndexOf("錯誤", StringComparison.Ordinal) >= 0 ||
                        t.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                        t.IndexOf("失敗", StringComparison.Ordinal) >= 0)
                        return t.Length > 180 ? t.Substring(0, 180) : t;
                }
                string first = log.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "沒有輸出";
                return first.Length > 180 ? first.Substring(0, 180) : first;
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

            void SetPhase(string phase)
            {
                Phase = phase;
                int sec = (int)(DateTime.Now - Started).TotalSeconds;
                if (Progress.Visible) Status.Text = Phase + "… 已跑 " + sec + " 秒";
            }

            void StopProgress()
            {
                Pulse.Stop();
                Progress.Visible = false;
            }

            void Run(bool apply)
            {
                string root = RepoRoot;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "wiki")))
                {
                    Status.Text = "找不到 Wiki 目錄（請用 --repo 指定 BACKROOMS 根目錄）。";
                    return;
                }
                string ps1 = Path.Combine(root, "wiki", "import-package.ps1");
                if (!File.Exists(ps1)) { Status.Text = "找不到 import-package.ps1。"; return; }
                string pkg = CleanPath(Package.Text);
                string latest = LatestStaging(root);
                if (apply)
                {
                    if (!string.IsNullOrEmpty(latest) && string.IsNullOrEmpty(pkg))
                    {
                        // use latest staging in apply mode below
                    }
                    else if (!File.Exists(pkg))
                    {
                        Status.Text = "找不到紀錄包。";
                        return;
                    }
                }
                else if (!File.Exists(pkg))
                {
                    Status.Text = "找不到紀錄包。";
                    return;
                }
                if (!apply || !string.IsNullOrEmpty(pkg))
                {
                    if (!ValidatePackage(pkg)) return;
                }
                CheckBtn.Enabled = false;
                ApplyBtn.Enabled = false;
                Output.Clear();
                StartProgress(apply ? "整合進 Wiki" : "匯入並檢查");
                var t = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        string args;
                        if (apply && !string.IsNullOrEmpty(pkg)) args = "-Package \"" + pkg + "\" -Apply";
                        else if (apply && !string.IsNullOrEmpty(latest)) args = "-Staging \"" + latest + "\" -Apply";
                        else args = "-Package \"" + pkg + "\"";
                        string script = "$OutputEncoding=[Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); & '" + ps1.Replace("'", "''") + "' " + args;
                        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                        var psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded);
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                        var p = new Process();
                        p.StartInfo = psi;
                        var sb = new StringBuilder();
                        p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                        {
                            if (e.Data == null) return;
                            lock (sb) sb.AppendLine(e.Data);
                            string line = e.Data;
                            string phase = line.IndexOf("驗證 staging", StringComparison.Ordinal) >= 0 ? "驗證 staging"
                                : line.IndexOf("產生審查報告", StringComparison.Ordinal) >= 0 ? "產生審查報告"
                                : line.IndexOf("合併正式 Wiki", StringComparison.Ordinal) >= 0 ? "合併正式 Wiki"
                                : line.IndexOf("重建 Wiki", StringComparison.Ordinal) >= 0 ? "重建 Wiki"
                                : null;
                            if (phase != null) { try { BeginInvoke((Action)delegate { SetPhase(phase); }); } catch { } }
                        };
                        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();
                        string log;
                        lock (sb) log = sb.ToString();
                        BeginInvoke((Action)delegate
                        {
                            Output.Text = log;
                            StopProgress();
                            Status.Text = p.ExitCode == 0 ? (apply ? "已整合進 Wiki。" : "檢查完成（尚未改正式 Wiki）。") : ("失敗：" + FirstError(log));
                            Status.ForeColor = p.ExitCode == 0 ? Color.FromArgb(80, 160, 80) : Color.FromArgb(200, 70, 60);
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke((Action)delegate
                        {
                            StopProgress();
                            Status.Text = "失敗：" + ex.Message;
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                        });
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            string LatestStaging(string root)
            {
                try
                {
                    string dir = Path.Combine(root, "wiki", "_incoming");
                    if (!Directory.Exists(dir)) return "";
                    return Directory.GetDirectories(dir, "import-*").OrderByDescending(Directory.GetLastWriteTime).FirstOrDefault() ?? "";
                }
                catch { return ""; }
            }

            void OpenStaging()
            {
                try
                {
                    string dir = Path.Combine(RepoRoot ?? "", "wiki", "_incoming");
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch { }
            }
        }
    }
}


