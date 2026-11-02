using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FoyerPanel
{
    static class Program
    {
        static string Url = "https://hulin-backrooms-rpg.firebaseapp.com/";
        static string ProfileDir, RecordOut, RecorderScript, StopFile, ClientPath;
        static Process Client, Recorder;
        static bool ClientOwned = false;
        static IntPtr GameHwnd = IntPtr.Zero;
        static bool PipelineRunning = false, AllowExit = false, ShowOcr = true;
        static bool ProgressDone = false;
        static string ActiveSession;
        static Mutex SingleInstance;
        static Form ControlWindow;
        static Label StateLabel;
        static Label StageLabel;
        static ProgressBar StageBar;
        static CheckBox OcrCheck;
        static Button StartRecBtn, StopRecBtn;
        static System.Windows.Forms.Timer UiTimer;
        static OcrOverlayForm Overlay;
        static OcrReadoutForm Readout;
        static double CaptureInterval = 0.5;
        static bool AutoRecord = true;
        static string Preset = "1600x900";

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--url": if (i + 1 < args.Length) Url = args[++i]; break;
                        case "--logdir": if (i + 1 < args.Length) RecordOut = args[++i]; break;
                        case "--profile": if (i + 1 < args.Length) ProfileDir = args[++i]; break;
                        case "--preset": if (i + 1 < args.Length) Preset = args[++i]; break;
                        case "--overlay": ShowOcr = true; break;
                        case "--no-overlay": ShowOcr = false; break;
                        case "--no-record": AutoRecord = false; break;
                    }
                }
                bool createdNew = false;
                SingleInstance = new Mutex(true, "Local\\FoyerPanel-SingleInstance", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("狐霖記錄器控制台已經在執行中。", "狐霖記錄器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                ClientPath = Path.Combine(exeDir, "FoyerClient.exe");
                if (!File.Exists(ClientPath)) ClientPath = Path.Combine(exeDir, "FoyerPlayer.exe");
                RecorderScript = Path.Combine(exeDir, "backroom-logger.ps1");
                if (string.IsNullOrEmpty(RecordOut))
                    RecordOut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerRecorder", "logs");
                if (string.IsNullOrEmpty(ProfileDir))
                    ProfileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "webview2-profile");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CreateUi();
                ControlWindow.Show();
                FindClient();
                if (GameHwnd == IntPtr.Zero) StartClient();
                else { SetState("狀態：客戶端已開", Color.FromArgb(130, 220, 140)); ScheduleAutoRecord(); }
                Application.Run(new ApplicationContext(ControlWindow));
                return 0;
            }
            catch (Exception ex) { Log("FATAL: " + ex); return 1; }
        }

        static void CreateUi()
        {
            ControlWindow = new Form();
            ControlWindow.Text = "狐霖記錄器控制";
            ControlWindow.ClientSize = new Size(430, 340);
            ControlWindow.StartPosition = FormStartPosition.Manual;
            ControlWindow.FormBorderStyle = FormBorderStyle.FixedSingle;
            ControlWindow.MaximizeBox = false;
            ControlWindow.BackColor = Color.FromArgb(28, 28, 31);
            ControlWindow.ForeColor = Color.FromArgb(230, 230, 235);
            var wa = Screen.PrimaryScreen.WorkingArea;
            ControlWindow.Location = new Point(Math.Max(wa.Left + 20, wa.Right - ControlWindow.Width - 24), wa.Top + 60);

            var clientGroup = Group("客戶端", 12, 10, 406, 92);
            var open = Btn("開啟客戶端", 14, 24, 180, 28);
            open.Click += delegate { EnsureClient(true); };
            clientGroup.Controls.Add(open);
            var close = Btn("關閉客戶端", 202, 24, 180, 28);
            close.Click += delegate { CloseClient(); };
            clientGroup.Controls.Add(close);
            OcrCheck = new CheckBox();
            OcrCheck.Text = "OCR 浮層";
            OcrCheck.SetBounds(14, 58, 120, 22);
            OcrCheck.Checked = ShowOcr;
            OcrCheck.ForeColor = Color.FromArgb(220, 220, 226);
            OcrCheck.BackColor = Color.FromArgb(44, 44, 47);
            OcrCheck.FlatStyle = FlatStyle.Flat;
            OcrCheck.CheckedChanged += delegate
            {
                ShowOcr = OcrCheck.Checked;
                if (ShowOcr) CreateOverlay();
                else CloseOverlay();
            };
            clientGroup.Controls.Add(OcrCheck);
            var logs = Btn("開啟 logs", 202, 58, 180, 22);
            logs.Click += delegate { OpenFolder(RecordOut); };
            clientGroup.Controls.Add(logs);

            var resLabel = new Label();
            resLabel.SetBounds(14, 186, 54, 20);
            resLabel.ForeColor = Color.FromArgb(200, 200, 206);
            resLabel.Text = "解析度";
            ControlWindow.Controls.Add(resLabel);
            var resCombo = new ComboBox();
            resCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            resCombo.SetBounds(72, 183, 150, 24);
            resCombo.Items.AddRange(new object[] { "1280x720", "1600x900", "1920x1080", "2400x1350" });
            resCombo.SelectedItem = Preset;
            if (resCombo.SelectedIndex < 0) resCombo.SelectedItem = "1600x900";
            resCombo.SelectedIndexChanged += delegate
            {
                if (resCombo.SelectedItem != null) Preset = Convert.ToString(resCombo.SelectedItem);
            };
            ControlWindow.Controls.Add(resCombo);

            var recGroup = Group("記錄", 12, 110, 406, 66);
            StartRecBtn = Btn("開始記錄", 14, 24, 120, 32);
            StartRecBtn.Click += delegate { StartRecording(); };
            recGroup.Controls.Add(StartRecBtn);
            StopRecBtn = Btn("停止記錄", 142, 24, 120, 32);
            StopRecBtn.Click += delegate { StopRecording(); };
            recGroup.Controls.Add(StopRecBtn);

            StateLabel = new Label();
            StateLabel.SetBounds(14, 218, 402, 24);
            StateLabel.ForeColor = Color.FromArgb(232, 232, 236);
            StateLabel.Text = "狀態：等待客戶端";
            ControlWindow.Controls.Add(StateLabel);

            StageLabel = new Label();
            StageLabel.SetBounds(14, 244, 402, 18);
            StageLabel.ForeColor = Color.FromArgb(205, 205, 210);
            StageLabel.Text = "目前整理：—";
            ControlWindow.Controls.Add(StageLabel);

            StageBar = new ProgressBar();
            StageBar.SetBounds(14, 266, 402, 14);
            StageBar.Minimum = 0;
            StageBar.Maximum = 100;
            StageBar.Value = 0;
            ControlWindow.Controls.Add(StageBar);

            var exportBtn = new Button();
            exportBtn.Text = "停止並匯出";
            exportBtn.SetBounds(14, 292, 195, 30);
            exportBtn.Click += delegate { StopAndExport(); };
            ControlWindow.Controls.Add(exportBtn);

            var importBtn = new Button();
            importBtn.Text = "整合 Wiki（獨立程式）";
            importBtn.SetBounds(217, 292, 195, 30);
            importBtn.Click += delegate { LaunchTool("FoyerImport.exe", "--repo \"" + RepoRoot() + "\""); };
            ControlWindow.Controls.Add(importBtn);

            ControlWindow.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (AllowExit) return;
                AllowExit = true;
                StopRecorder(true);
                CloseOverlay();
                // 控制台退出，客戶端 process 不動
            };

            UiTimer = new System.Windows.Forms.Timer();
            UiTimer.Interval = 500;
            UiTimer.Tick += delegate { RefreshStatus(); };
            UiTimer.Start();
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

        static void SetState(string text, Color color)
        {
            try
            {
                if (ControlWindow == null || ControlWindow.IsDisposed) return;
                if (ControlWindow.InvokeRequired) { ControlWindow.BeginInvoke((Action)delegate { SetState(text, color); }); return; }
                StateLabel.Text = text;
                StateLabel.ForeColor = color;
            }
            catch { }
        }

        static void RefreshStatus()
        {
            try
            {
                if (GameHwnd != IntPtr.Zero && !IsWindow(GameHwnd))
                {
                    GameHwnd = IntPtr.Zero;
                    Client = null;
                    CloseOverlay();
                    StopRecorder(true);
                }
                bool rec = Recorder != null && !Recorder.HasExited;
                RefreshProgress();
                if (PipelineRunning) SetState("狀態：整理中…", Color.FromArgb(255, 190, 100));
                else if (rec) SetState("狀態：記錄中", Color.FromArgb(130, 220, 140));
                else if (GameHwnd != IntPtr.Zero) SetState("狀態：客戶端已開（未記錄）", Color.FromArgb(200, 200, 200));
                else SetState("狀態：客戶端未開", Color.FromArgb(200, 200, 200));
            }
            catch { }
        }

        static bool EnsureClient(bool launch)
        {
            if (GameHwnd != IntPtr.Zero && IsWindow(GameHwnd)) return true;
            if (FindClient()) return true;
            if (!launch) return false;
            return StartClient();
        }

        static bool FindClient()
        {
            try
            {
                string[] names = { Path.GetFileNameWithoutExtension(ClientPath), "FoyerClient", "FoyerPlayer" };
                foreach (string n in names)
                {
                    foreach (var p in Process.GetProcessesByName(n))
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero && IsWindow(p.MainWindowHandle))
                            {
                                Client = p;
                                GameHwnd = p.MainWindowHandle;
                                ClientOwned = false;
                                if (ShowOcr) CreateOverlay();
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        static bool StartClient()
        {
            try
            {
                if (!File.Exists(ClientPath)) { SetState("找不到 FoyerClient.exe", Color.FromArgb(255, 120, 110)); return false; }
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
                    if (Client.MainWindowHandle != IntPtr.Zero && IsWindow(Client.MainWindowHandle))
                    {
                        GameHwnd = Client.MainWindowHandle;
                        if (ShowOcr) CreateOverlay();
                        SetState("狀態：客戶端已開", Color.FromArgb(130, 220, 140));
                        ScheduleAutoRecord();
                        return true;
                    }
                }
                SetState("客戶端沒有成功開啟（看 player.log）", Color.FromArgb(255, 120, 110));
                return false;
            }
            catch (Exception ex)
            {
                Log("start client: " + ex);
                SetState("開啟客戶端失敗", Color.FromArgb(255, 120, 110));
                return false;
            }
        }

        static void ScheduleAutoRecord()
        {
            if (!AutoRecord) return;
            var t = new Thread(delegate ()
            {
                Thread.Sleep(2500);
                try
                {
                    if (ControlWindow == null || ControlWindow.IsDisposed) return;
                    ControlWindow.BeginInvoke((Action)delegate
                    {
                        if (GameHwnd == IntPtr.Zero || !IsWindow(GameHwnd)) return;
                        if (Recorder == null || Recorder.HasExited) StartRecording();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void CloseClient()
        {
            try
            {
                StopRecorder(true);
                if (GameHwnd != IntPtr.Zero) PostMessage(GameHwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (Client != null && !Client.HasExited)
                {
                    if (!Client.WaitForExit(3000) && ClientOwned) { try { Client.Kill(); } catch { } }
                }
            }
            catch { }
            GameHwnd = IntPtr.Zero;
            Client = null;
            ClientOwned = false;
            CloseOverlay();
            SetState("狀態：客戶端已關閉", Color.FromArgb(200, 200, 200));
        }

        static void StartRecording()
        {
            if (PipelineRunning) return;
            if (!EnsureClient(true)) return;
            if (Recorder != null && !Recorder.HasExited) { SetState("狀態：已經在記錄", Color.FromArgb(130, 220, 140)); return; }
            try
            {
                if (!File.Exists(RecorderScript)) { SetState("找不到 backroom-logger.ps1", Color.FromArgb(255, 120, 110)); return; }
                Directory.CreateDirectory(RecordOut);
                StopFile = Path.Combine(Path.GetTempPath(), "FoyerPanel-stop-" + Process.GetCurrentProcess().Id + ".flag");
                try { if (File.Exists(StopFile)) File.Delete(StopFile); } catch { }
                string ps = "-NoProfile -ExecutionPolicy Bypass -File \"" + RecorderScript + "\""
                    + " -OutDir \"" + RecordOut + "\" -Hwnd " + GameHwnd.ToInt64()
                    + " -NoResize -Quiet -Interval " + CaptureInterval.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + " -SaveLineCrops -AutoFloorShots -StopFile \"" + StopFile + "\"";
                var psi = new ProcessStartInfo("powershell.exe", ps);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Path.GetDirectoryName(RecorderScript);
                Recorder = Process.Start(psi);
                if (ShowOcr) CreateOverlay();
                ProgressDone = false;
                StageLabel.Text = "目前整理：—";
                StageBar.Value = 0;
                SetState("狀態：記錄中", Color.FromArgb(130, 220, 140));
            }
            catch (Exception ex)
            {
                Log("start recorder: " + ex);
                SetState("開始記錄失敗", Color.FromArgb(255, 120, 110));
            }
        }

        static void StopRecording()
        {
            if (PipelineRunning) return;
            StopRecorder(true);
            SetState(GameHwnd != IntPtr.Zero ? "狀態：已停止記錄（客戶端仍開）" : "狀態：已停止記錄", Color.FromArgb(255, 190, 100));
        }

        static string RepoRoot()
        {
            try
            {
                string p = Path.GetFullPath(Path.Combine(RecordOut, "..", ".."));
                if (Directory.Exists(Path.Combine(p, "wiki", "content"))) return p;
            }
            catch { }
            return Path.GetFullPath(RecordOut);
        }

        static void LaunchTool(string name, string args)
        {
            try
            {
                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (!File.Exists(exe)) { SetState("找不到 " + name, Color.FromArgb(255, 120, 110)); return; }
                var psi = new ProcessStartInfo(exe, args);
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log("launch: " + ex); SetState("開啟工具失敗", Color.FromArgb(255, 120, 110)); }
        }

        static void StopAndExport()
        {
            if (PipelineRunning) return;
            string session = LatestSession();
            if (session == null) { SetState("沒有可匯出的場次", Color.FromArgb(255, 150, 120)); return; }
            PipelineRunning = true;
            SetState("狀態：停止記錄並匯出…", Color.FromArgb(255, 190, 100));
            var t = new Thread(delegate ()
            {
                try
                {
                    StopRecorder(false);
                    RunExport(session);
                    SetState("狀態：匯出完成（尚未進 Wiki）", Color.FromArgb(130, 220, 140));
                }
                catch (Exception ex) { Log("export: " + ex); SetState("匯出失敗（看 panel.log）", Color.FromArgb(255, 120, 110)); }
                finally { PipelineRunning = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void RunExport(string session)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string run = Path.Combine(exeDir, "structured", "run.mjs");
            if (!File.Exists(run)) run = Path.Combine(RepoRoot(), "记录器", "structured", "run.mjs");
            if (!File.Exists(run)) throw new Exception("找不到 run.mjs");
            string node = Path.Combine(exeDir, "node.exe");
            if (!File.Exists(node)) node = "node.exe";
            string staging = Path.Combine(session, "staging");
            var psi = new ProcessStartInfo(node, "\"" + run + "\" \"" + session + "\" --portable --staging \"" + staging + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = exeDir;
            var p = Process.Start(psi);
            if (p != null) p.WaitForExit();
            if (p != null && p.ExitCode != 0) throw new Exception("run.mjs exit " + p.ExitCode);
        }

        static void StopAndProcess()
        {
            if (PipelineRunning) return;
            string ses = LatestSession();
            if (ses == null) { SetState("沒有可整理的場次", Color.FromArgb(255, 150, 120)); return; }
            PipelineRunning = true;
            ProgressDone = false;
            SetState("狀態：停止記錄並整理…", Color.FromArgb(255, 190, 100));
            var t = new Thread(delegate ()
            {
                try
                {
                    StopRecorder(false);
                    RunPostprocess();
                    SetState("狀態：整理完成", Color.FromArgb(130, 220, 140));
                }
                catch (Exception ex) { Log("process: " + ex); SetState("整理失敗（看 player.log）", Color.FromArgb(255, 120, 110)); }
                finally
                {
                    PipelineRunning = false;
                    ProgressDone = true;
                    StageLabel.Text = "上次整理：完成";
                    StageBar.Value = 100;
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void RefreshProgress()
        {
            try
            {
                if (!PipelineRunning)
                {
                    if (ProgressDone) return;
                    StageLabel.Text = "目前整理：—";
                    StageBar.Value = 0;
                    return;
                }
                string ses = LatestSession();
                string pf = string.IsNullOrEmpty(ses) ? null : Path.Combine(ses, "progress.json");
                int pct = -1, cur = 0, total = 0;
                string stage = null, item = null;
                if (!string.IsNullOrEmpty(pf) && File.Exists(pf))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(pf));
                    pct = DInt(d, "percent", -1);
                    stage = DStr(d, "stage");
                    item = DStr(d, "item");
                    cur = DInt(d, "current", 0);
                    total = DInt(d, "total", 0);
                }
                string line = "目前整理：" + (string.IsNullOrEmpty(stage) ? "準備中…" : stage);
                if (pct >= 0) line += " (" + pct + "/100)";
                if (cur > 0 && total > 0) line += "　" + cur + "/" + total + (string.IsNullOrEmpty(item) ? "" : "：" + item);
                StageLabel.Text = line;
                int v = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                if (StageBar.Value != v) StageBar.Value = v;
            }
            catch { }
        }

        static int DInt(Dictionary<string, object> d, string key, int fallback)
        {
            try
            {
                object v;
                if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToInt32(v);
            }
            catch { }
            return fallback;
        }

        static string DStr(Dictionary<string, object> d, string key)
        {
            try
            {
                object v;
                if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            }
            catch { }
            return null;
        }

        static void StopRecorder(bool fast)
        {
            try
            {
                if (!string.IsNullOrEmpty(StopFile)) File.WriteAllText(StopFile, "stop");
                int wait = fast ? 1200 : 5000;
                if (Recorder != null && !Recorder.HasExited && !Recorder.WaitForExit(wait))
                { try { Recorder.Kill(); } catch { } }
            }
            catch { }
            finally { try { if (!string.IsNullOrEmpty(StopFile) && File.Exists(StopFile)) File.Delete(StopFile); } catch { } }
            try
            {
                if (Recorder != null && !Recorder.HasExited) Recorder.WaitForExit(500);
            }
            catch { }
        }

        static string LatestSession()
        {
            try
            {
                if (string.IsNullOrEmpty(RecordOut) || !Directory.Exists(RecordOut)) return null;
                var dirs = Directory.GetDirectories(RecordOut, "backroom-*");
                if (dirs.Length == 0) return null;
                Array.Sort(dirs);
                return dirs[dirs.Length - 1];
            }
            catch { return null; }
        }

        static void RunPostprocess()
        {
            string session = LatestSession();
            if (session == null) return;
            ActiveSession = session;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string script = Path.Combine(exeDir, "postprocess.mjs");
            if (!File.Exists(script)) return;
            string node = Path.Combine(exeDir, "node.exe");
            if (!File.Exists(node)) node = "node.exe";
            var psi = new ProcessStartInfo(node, "\"" + script + "\" \"" + session + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = exeDir;
            var p = Process.Start(psi);
            if (p != null) p.WaitForExit();
        }

        static string OverlayPath()
        {
            return string.IsNullOrEmpty(RecordOut) ? null : Path.Combine(RecordOut, "overlay.json");
        }

        static OcrOverlayState ReadOverlay()
        {
            try
            {
                string p = OverlayPath();
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
                return new JavaScriptSerializer().Deserialize<OcrOverlayState>(File.ReadAllText(p, Encoding.UTF8));
            }
            catch { return null; }
        }

        static void CreateOverlay()
        {
            try
            {
                if (!ShowOcr || GameHwnd == IntPtr.Zero || !IsWindow(GameHwnd)) return;
                if (Overlay == null || Overlay.IsDisposed)
                {
                    Overlay = new OcrOverlayForm(GameHwnd);
                    Overlay.Show();
                }
                if (Readout == null || Readout.IsDisposed)
                {
                    Readout = new OcrReadoutForm(GameHwnd);
                    Readout.Show();
                }
            }
            catch (Exception ex) { Log("overlay: " + ex); }
        }

        static void CloseOverlay()
        {
            try { if (Overlay != null && !Overlay.IsDisposed) Overlay.Close(); } catch { }
            try { if (Readout != null && !Readout.IsDisposed) Readout.Close(); } catch { }
            Overlay = null;
            Readout = null;
        }

        static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        static void Log(string line)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "panel.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        static Rectangle GetRenderRect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return Rectangle.Empty;
            RECT cr; GetClientRect(hwnd, out cr);
            POINT p = new POINT(); ClientToScreen(hwnd, ref p);
            Rectangle fallback = new Rectangle(p.X, p.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(hwnd, delegate (IntPtr h, IntPtr lp)
            {
                var cn = new StringBuilder(128); GetClassName(h, cn, 128);
                if (IsWindowVisible(h) && cn.ToString() == "Chrome_RenderWidgetHostHWND") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                RECT r;
                if (GetWindowRect(found, out r))
                {
                    var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (rect.Width > 100 && rect.Height > 100) return rect;
                }
            }
            return fallback;
        }

        static Color RegionColor(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "title": return Color.Gold;
                case "enemy": return Color.OrangeRed;
                case "main": return Color.Lime;
                case "opts": return Color.Cyan;
                case "char": return Color.Magenta;
                default: return Color.White;
            }
        }

        class OcrOverlayForm : Form
        {
            readonly IntPtr game;
            readonly System.Windows.Forms.Timer timer;
            OcrOverlayState state;

            public OcrOverlayForm(IntPtr game)
            {
                this.game = game;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 180;
                timer.Tick += delegate { RefreshOverlay(); };
                timer.Start();
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x00000020 | 0x00080000 | 0x08000000 | 0x00000080;
                    return cp;
                }
            }

            void RefreshOverlay()
            {
                try
                {
                    if (!IsWindow(game)) { Visible = false; return; }
                    Rectangle b = GetRenderRect(game);
                    if (b.Width <= 0 || b.Height <= 0) { Visible = false; return; }
                    if (Bounds != b) Bounds = b;
                    state = Program.ReadOverlay();
                    if (!Visible) Visible = true;
                    Invalidate();
                }
                catch { }
            }

            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var st = state;
                if (st == null || st.w <= 0 || st.h <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sx = ClientSize.Width / (float)st.w;
                float sy = ClientSize.Height / (float)st.h;
                g.ScaleTransform(sx, sy);
                float penW = 1.6f / Math.Max(0.2f, Math.Min(sx, sy));
                if (st.regions != null)
                    foreach (var r in st.regions)
                        using (var p = new Pen(RegionColor(r.n), penW)) g.DrawRectangle(p, r.x, r.y, r.w, r.h);
                if (st.lines != null)
                    foreach (var l in st.lines)
                        using (var p = new Pen(Color.FromArgb(170, RegionColor(l.r)), penW * 0.55f))
                            g.DrawRectangle(p, l.x, l.y, Math.Max(1, l.w), Math.Max(1, l.h));
            }
        }

        class OcrReadoutForm : Form
        {
            readonly IntPtr game;
            readonly RichTextBox box;
            readonly System.Windows.Forms.Timer timer;
            string lastText = "";

            public OcrReadoutForm(IntPtr game)
            {
                this.game = game;
                Text = "OCR 讀取";
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(16, 18, 22);
                Width = 640;
                Height = 620;
                MinimumSize = new Size(320, 200);
                box = new RichTextBox();
                box.Dock = DockStyle.Fill;
                box.ReadOnly = true;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Color.FromArgb(16, 18, 22);
                box.ForeColor = Color.Gainsboro;
                box.Font = new Font("Consolas", 9f);
                box.WordWrap = false;
                box.ScrollBars = RichTextBoxScrollBars.Both;
                Controls.Add(box);
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 300;
                timer.Tick += delegate { RefreshReadout(); };
                timer.Start();
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x00000080;
                    return cp;
                }
            }

            void RefreshReadout()
            {
                try
                {
                    if (!IsWindow(game)) { Visible = false; return; }
                    Rectangle b = GetRenderRect(game);
                    if (b.Width <= 0) { Visible = false; return; }
                    var wa = Screen.FromPoint(new Point(b.Right, b.Top)).WorkingArea;
                    int x;
                    if (b.Right + 10 + Width <= wa.Right) x = b.Right + 10;
                    else if (b.Left - 10 - Width >= wa.Left) x = b.Left - 10 - Width;
                    else { Visible = false; return; }
                    int y = Math.Max(wa.Top, Math.Min(b.Top, wa.Bottom - Height));
                    Location = new Point(x, y);
                    if (!Visible) Visible = true;
                    string text = FormatState(Program.ReadOverlay());
                    if (text != lastText)
                    {
                        lastText = text;
                        int keep = box.SelectionStart;
                        box.Text = text;
                        box.SelectionStart = Math.Min(keep, box.TextLength);
                    }
                }
                catch { }
            }

            static string Trunc(string s, int n)
            {
                s = s ?? "";
                return s.Length <= n ? s : s.Substring(0, n) + "…";
            }

            static bool EndsPunct(string s)
            {
                return !string.IsNullOrEmpty(s) && "。！？!?」』）)".IndexOf(s[s.Length - 1]) >= 0;
            }

            static bool StartsPunct(string s)
            {
                return !string.IsNullOrEmpty(s) && "「『（(。，、；：！？!?…".IndexOf(s[0]) >= 0;
            }

            static OcrOverlayLine[] MergeLines(OcrOverlayLine[] raw)
            {
                if (raw == null) return null;
                var arr = (OcrOverlayLine[])raw.Clone();
                Array.Sort(arr, delegate (OcrOverlayLine a, OcrOverlayLine b)
                {
                    int c = a.y.CompareTo(b.y);
                    return c != 0 ? c : a.x.CompareTo(b.x);
                });
                var list = new List<OcrOverlayLine>();
                foreach (var l in arr)
                {
                    var last = list.Count > 0 ? list[list.Count - 1] : null;
                    bool sameLine = last != null
                        && Math.Abs(last.y - l.y) <= 4
                        && (l.x - (last.x + last.w)) >= -8
                        && (l.x - (last.x + last.w)) <= 36
                        && (!EndsPunct(last.t) || StartsPunct(l.t));
                    bool wrapped = last != null
                        && !EndsPunct(last.t)
                        && Math.Abs(last.x - l.x) <= 14
                        && (l.y - (last.y + last.h)) <= Math.Max(14, (int)(0.75 * l.h));
                    if (sameLine || wrapped)
                    {
                        last.t = (last.t ?? "") + (l.t ?? "");
                        last.w = Math.Max(last.w, (l.x + l.w) - last.x);
                        last.h = Math.Max(last.h, (l.y + l.h) - last.y);
                        continue;
                    }
                    list.Add(new OcrOverlayLine { r = l.r, t = l.t, x = l.x, y = l.y, w = l.w, h = l.h });
                }
                return list.ToArray();
            }

            static string FormatState(OcrOverlayState st)
            {
                var sb = new StringBuilder();
                if (st == null) { sb.AppendLine("等待記錄器 overlay.json …"); return sb.ToString(); }
                sb.AppendLine("FRAME " + st.frame + "  " + st.w + "x" + st.h + "  layout=" + st.layout + "  bg=" + st.bg);
                sb.AppendLine("SESSION " + st.session + "  fallbacks=" + st.fallbacks + "  running=" + st.running);
                sb.AppendLine("顏色：title 金／enemy 紅／main 綠／opts 青／char 洋紅");
                sb.AppendLine();
                sb.AppendLine("REGIONS");
                if (st.regions != null)
                    foreach (var r in st.regions) sb.AppendLine("  " + (r.n ?? "?").PadRight(7) + " x=" + r.x + " y=" + r.y + "  " + r.w + "x" + r.h);
                sb.AppendLine();
                var merged = MergeLines(st.lines);
                sb.AppendLine("LINES " + (merged == null ? 0 : merged.Length));
                if (merged != null)
                    foreach (var l in merged) sb.AppendLine("[" + (l.r ?? "?") + "] (" + l.x + "," + l.y + " " + l.w + "x" + l.h + ") " + Trunc(l.t, 500));
                if (!st.running) sb.AppendLine();
                if (!st.running) sb.AppendLine("（記錄器已停止；這是 Close 前最後一幀）");
                return sb.ToString();
            }
        }
    }

    public class OcrOverlayRegion
    {
        public string n { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
    }

    public class OcrOverlayLine
    {
        public string r { get; set; }
        public string t { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
    }

    public class OcrOverlayState
    {
        public bool running { get; set; }
        public string session { get; set; }
        public int frame { get; set; }
        public int w { get; set; }
        public int h { get; set; }
        public string layout { get; set; }
        public string bg { get; set; }
        public OcrOverlayRegion[] regions { get; set; }
        public OcrOverlayRegion[] measured { get; set; }
        public OcrOverlayLine[] lines { get; set; }
        public int fallbacks { get; set; }
        public string detail { get; set; }
    }
}
