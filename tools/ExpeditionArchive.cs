using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
            Application.Run(new ImportForm());
        }

        class ImportForm : Form
        {
            TextBox Package;
            TextBox Output;
            Label Status;
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
                browse.Text = "選擇 .hulinlog.zip";
                browse.SetBounds(574, 11, 132, 26);
                browse.Click += delegate
                {
                    using (var d = new OpenFileDialog())
                    {
                        d.Filter = "紀錄包 (*.hulinlog.zip)|*.hulinlog.zip|Zip (*.zip)|*.zip|所有檔案 (*.*)|*.*";
                        if (d.ShowDialog() == DialogResult.OK) Package.Text = d.FileName;
                    }
                };
                Controls.Add(browse);

                CheckBtn = new Button();
                CheckBtn.Text = "只匯入並檢查";
                CheckBtn.SetBounds(14, 50, 150, 32);
                CheckBtn.Click += delegate { Run(false); };
                Controls.Add(CheckBtn);

                ApplyBtn = new Button();
                ApplyBtn.Text = "整合進 Wiki";
                ApplyBtn.SetBounds(176, 50, 150, 32);
                ApplyBtn.Click += delegate { Run(true); };
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

                Output = new TextBox();
                Output.Multiline = true;
                Output.ReadOnly = true;
                Output.ScrollBars = ScrollBars.Both;
                Output.SetBounds(14, 122, 690, 322);
                Controls.Add(Output);
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
                string pkg = Package.Text.Trim().Trim('"');
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
                CheckBtn.Enabled = false;
                ApplyBtn.Enabled = false;
                Status.Text = apply ? "整合中…" : "匯入並檢查中…";
                Output.Clear();
                var t = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        string args;
                        if (apply && !string.IsNullOrEmpty(pkg)) args = "-Package \"" + pkg + "\" -Apply";
                        else if (apply && !string.IsNullOrEmpty(latest)) args = "-Staging \"" + latest + "\" -Apply";
                        else args = "-Package \"" + pkg + "\"";
                        var psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\" " + args);
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                        var p = new Process();
                        p.StartInfo = psi;
                        var sb = new StringBuilder();
                        p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
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
                            Status.Text = p.ExitCode == 0 ? (apply ? "已整合進 Wiki。" : "檢查完成（尚未改正式 Wiki）。") : ("失敗（exit " + p.ExitCode + "）");
                            Status.ForeColor = p.ExitCode == 0 ? Color.FromArgb(80, 160, 80) : Color.FromArgb(200, 70, 60);
                            CheckBtn.Enabled = true;
                            ApplyBtn.Enabled = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke((Action)delegate
                        {
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


