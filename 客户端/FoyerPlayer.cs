using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FoyerPlayer
{
    static class Program
    {
        static string Url = "https://hulin-backrooms-rpg.firebaseapp.com/";
        static int W = 1600, H = 900, Port = 0;
        static string ProfileDir, RecordOut, StopFile, RecorderScript;
        static Process Recorder;
        static Process VolumeWatcher;
        static string VolumeStopFile;
        static bool Record = true, RecorderStarted = false;
        static bool ShowOverlay = true;
        static double CaptureInterval = 0.5;
        static bool GameClosed = false, PipelineRunning = false, AllowAppExit = false, PendingShutdown = false;
        static bool ClientOpening = false, WebViewInitializing = false;
        static Form Window;
        static Form ControlWindow;
        static OcrOverlayForm Overlay;
        static OcrReadoutForm Readout;
        static Mutex SingleInstance;
        static WebView2 View;
        static CoreWebView2Environment WebEnv;
        static Button SnapButton;
        static Button OpenClientButton;
        static Button StopButton;
        static Button CloseClientButton;
        static Button ExitButton;
        static Button ExportButton;
        static Label SnapStatus;
        static Label StateLabel;
        static Label StageLabel;
        static Label InputLabel;
        static ProgressBar StageBar;
        static Button LangButton;
        static Button StartRecBtn;
        static Button PauseRecBtn;
        static System.Windows.Forms.Timer UiTimer;

        /* ── 介面語言（繁體／簡體）──
           遊戲本身是簡體，但工具介面預設繁體；有些使用者只看簡體，
           所以在控制視窗放一顆切換鈕，選擇存在 %LOCALAPPDATA%\FoyerLogger\ui.txt。 */
        static bool UiHans = false;
        static string UiLangFile;
        class UiItem { public Control C; public string Hant; public string Hans; public float Size; public bool Bold; }
        static readonly System.Collections.Generic.List<UiItem> UiItems = new System.Collections.Generic.List<UiItem>();

        static string T(string hant, string hans) { return UiHans ? hans : hant; }

        /* 字形也要跟著語言換：用繁中字體（JhengHei）畫簡體字，
           「关」「门」「这」這些字會長出繁體的骨架，看起來就是不對。 */
        static Font UiFont(float size, bool bold)
        {
            string fam = UiHans ? "Microsoft YaHei UI" : "Microsoft JhengHei UI";
            var style = bold ? FontStyle.Bold : FontStyle.Regular;
            try
            {
                var f = new Font(fam, size, style);
                if (string.Equals(f.Name, fam, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
            string alt = UiHans ? "SimSun" : "Microsoft YaHei UI";
            try { return new Font(alt, size, style); }
            catch { return SystemFonts.DefaultFont; }
        }

        static void Ui(Control c, string hant, string hans, float size, bool bold)
        {
            UiItems.Add(new UiItem { C = c, Hant = hant, Hans = hans, Size = size, Bold = bold });
            c.Text = UiHans ? hans : hant;
            try { c.Font = UiFont(size, bold); } catch { }
        }

        static void Ui(Control c, string hant, string hans) { Ui(c, hant, hans, 9F, false); }

        /* 深色底上「停用」的按鈕會變成看不見的灰字，所以一律保持可按，
           真正不能做的事在 handler 裡擋掉並給一句提示。 */
        static void StyleBtn(Button b, bool primary)
        {
            try
            {
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = primary ? Color.FromArgb(110, 160, 235) : Color.FromArgb(78, 78, 84);
                b.BackColor = primary ? Color.FromArgb(46, 66, 98) : Color.FromArgb(44, 44, 47);
                b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(58, 82, 122) : Color.FromArgb(58, 58, 63);
                b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(36, 52, 78) : Color.FromArgb(34, 34, 37);
                b.ForeColor = primary ? Color.White : Color.FromArgb(226, 226, 230);
            }
            catch { }
        }

        static GroupBox NewGroup(string hant, string hans, int x, int y, int w, int h)
        {
            var g = new GroupBox();
            g.ForeColor = Color.FromArgb(150, 150, 156);
            g.SetBounds(x, y, w, h);
            Ui(g, hant, hans, 8.5F, false);
            ControlWindow.Controls.Add(g);
            return g;
        }

        static void ApplyUiLanguage()
        {
            foreach (var it in UiItems)
            {
                if (it.C == null || it.C.IsDisposed) continue;
                it.C.Text = UiHans ? it.Hans : it.Hant;
                try { it.C.Font = UiFont(it.Size, it.Bold); } catch { }
            }
            if (ControlWindow != null && !ControlWindow.IsDisposed)
                ControlWindow.Text = T("狐霖記錄器控制", "狐霖记录器控制");
            if (LangButton != null && !LangButton.IsDisposed)
                LangButton.Text = T("語言：繁體", "语言：简体");
        }

        static void LoadUiLanguage()
        {
            try
            {
                UiLangFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "ui.txt");
                if (File.Exists(UiLangFile))
                {
                    string v = File.ReadAllText(UiLangFile).Trim();
                    UiHans = v.StartsWith("hans", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        static void SaveUiLanguage()
        {
            try
            {
                if (string.IsNullOrEmpty(UiLangFile))
                    UiLangFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "ui.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(UiLangFile));
                File.WriteAllText(UiLangFile, UiHans ? "hans" : "hant");
            }
            catch { }
        }

        static void ToggleUiLanguage()
        {
            UiHans = !UiHans;
            ApplyUiLanguage();
            SaveUiLanguage();
            RefreshStatus();
        }
        static string ManualDir;
        static string ActiveSession;
        static string ExportSession;
        static DateTime PipelineStartedAt;
        static long InputCount = 0;
        static readonly object InputLock = new object();
        static readonly System.Collections.Generic.List<string> InputBuffer = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.List<Form> LoginPopups = new System.Collections.Generic.List<Form>();

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                string preset = "1600x900";
                int ovW = 0, ovH = 0; bool hasW = false, hasH = false;
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--url": if (i + 1 < args.Length) Url = args[++i]; break;
                        case "--preset": if (i + 1 < args.Length) preset = args[++i]; break;
                        case "--w": if (i + 1 < args.Length && int.TryParse(args[++i], out ovW)) hasW = true; break;
                        case "--h": if (i + 1 < args.Length && int.TryParse(args[++i], out ovH)) hasH = true; break;
                        case "--port": if (i + 1 < args.Length) int.TryParse(args[++i], out Port); break;
                        case "--profile": if (i + 1 < args.Length) ProfileDir = args[++i]; break;
                        case "--logdir": if (i + 1 < args.Length) RecordOut = args[++i]; break;
                        case "--interval": { double iv; if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out iv) && iv >= 0.1 && iv <= 5) CaptureInterval = iv; break; }
                        case "--record": Record = true; break;
                        case "--no-record": Record = false; break;
                        case "--overlay": ShowOverlay = true; break;
                        case "--no-overlay": ShowOverlay = false; break;
                    }
                }
                bool createdNew = false;
                SingleInstance = new Mutex(true, "Local\\FoyerRecorder-SingleInstance", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("狐霖記錄器已經在執行中。\n一次只能開啟一個客戶端。", "狐霖記錄器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                int pw, ph;
                if (TryPreset(preset, out pw, out ph)) { W = pw; H = ph; }
                if (hasW && ovW >= 800) W = ovW;
                if (hasH && ovH >= 600) H = ovH;
                if (string.IsNullOrEmpty(ProfileDir))
                    ProfileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "webview2-profile");
                Directory.CreateDirectory(ProfileDir);
                Port = Port > 0 ? FindFreeDebugPort(Port) : 0;

                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log("FATAL: " + e.ExceptionObject); };
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log("THREAD: " + e.Exception); };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                CreateControlWindow();
                ControlWindow.Show();
                ApplicationContext appCtx = new ApplicationContext(ControlWindow);
                Application.Run(appCtx);
                return 0;
            }
            catch (Exception ex) { Log("FATAL MAIN: " + ex); return 1; }
        }

        static void OpenGameClient()
        {
            try
            {
                if (Window != null && !Window.IsDisposed)
                {
                    Window.Activate();
                    SetState("狀態：客戶端已在執行", Color.FromArgb(255, 190, 100));
                    return;
                }
                if (ClientOpening) return;
                if (PipelineRunning) { SetState("狀態：整理中，請稍後再開啟客戶端", Color.FromArgb(255, 190, 100)); return; }
                ClientOpening = true;
                WebViewInitializing = false;
                GameClosed = false;
                RecorderStarted = false;
                PendingShutdown = false;
                ActiveSession = null;
                ExportSession = null;
                CreateGameWindow();
                Window.Show();
                if (ShowOverlay) CreateOverlay();
            }
            catch (Exception ex) { Log("OpenGameClient: " + ex); }
            finally { ClientOpening = false; }
        }

        static void CreateGameWindow()
        {
            Window = new Form();
            Window.Text = "狐霖的後室 RPG";
            Window.ClientSize = new Size(W, H);
            Window.FormBorderStyle = FormBorderStyle.FixedSingle;
            Window.MaximizeBox = false;
            Window.StartPosition = FormStartPosition.CenterScreen;
            Window.BackColor = Color.Black;

            View = new WebView2();
            View.Dock = DockStyle.Fill;
            View.DefaultBackgroundColor = Color.Black;
            Window.Controls.Add(View);

            Window.FormClosed += delegate
            {
                /* 遊戲視窗關閉 → 讓視窗消失，但控制視窗與整理流程留在背景。
                   整理完成後控制視窗仍可由「開啟客戶端」再開新的一局。 */
                GameClosed = true;
                CloseOverlay();
                /* 這裡以前會把「開啟客戶端」設成 Enabled=false，但 RefreshStatus 已經
                   不再重新啟用它 —— 結果關一次遊戲之後那顆按鈕就永久變灰、按不下去，
                   想開下一局只能重開整個程式。按鈕現在一律保持可按。 */
                foreach (var p in LoginPopups.ToArray()) { try { p.Close(); } catch { } }
                if (PipelineRunning) PendingShutdown = true;
                else BeginShutdown(false);
            };

            Window.Shown += async delegate
            {
                try { if (ControlWindow != null && !ControlWindow.Visible) ControlWindow.Show(); } catch { }
                if (WebViewInitializing) return;
                WebViewInitializing = true;
                try
                {
                    string args2 = (Port > 0 ? "--remote-debugging-port=" + Port + " --remote-allow-origins=* " : "")
                        + "--disable-features=CalculateNativeWinOcclusion --autoplay-policy=no-user-gesture-required";
                    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "foyer-debug.log"), DateTime.Now.ToString("HH:mm:ss") + " profile=" + ProfileDir + " port=" + Port + " args=" + args2 + Environment.NewLine); } catch { }
                    CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, ProfileDir, new CoreWebView2EnvironmentOptions(args2));
                    WebEnv = env;
                    await View.EnsureCoreWebView2Async(env);
                    if (SnapButton != null) SnapButton.Enabled = true;
                    View.CoreWebView2.NewWindowRequested += async delegate (object s3, CoreWebView2NewWindowRequestedEventArgs e3)
                    {
                        var deferral = e3.GetDeferral();
                        Form popup = null;
                        try
                        {
                            e3.Handled = true;
                            popup = new Form();
                            popup.Text = "登入";
                            popup.ClientSize = new Size(520, 720);
                            popup.StartPosition = FormStartPosition.CenterParent;
                            popup.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                            popup.MaximizeBox = false;
                            popup.MinimizeBox = false;
                            popup.ShowInTaskbar = false;
                            var pv = new WebView2();
                            pv.Dock = DockStyle.Fill;
                            pv.DefaultBackgroundColor = Color.White;
                            popup.Controls.Add(pv);
                            LoginPopups.Add(popup);
                            popup.FormClosed += delegate { try { LoginPopups.Remove(popup); } catch { } };
                            popup.Show(Window);
                            await pv.EnsureCoreWebView2Async(WebEnv);
                            pv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                            pv.CoreWebView2.Settings.IsStatusBarEnabled = false;
                            pv.CoreWebView2.WindowCloseRequested += delegate { try { popup.Close(); } catch { } };
                            e3.NewWindow = pv.CoreWebView2;
                        }
                        catch (Exception ex)
                        {
                            Log("登入 popup 失敗：" + ex.Message);
                            try { if (popup != null) popup.Close(); } catch { }
                        }
                        finally { try { deferral.Complete(); } catch { } }
                    };
                    View.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    View.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    View.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    try
                    {
                        View.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                        await View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InputCaptureScript);
                        Log("輸入捕捉腳本已注入");
                    }
                    catch (Exception iex) { Log("輸入捕捉注入失敗：" + iex.Message); }
                    View.NavigationCompleted += delegate { StartAfterLoad(); };
                    View.CoreWebView2.Navigate(Url);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Edge WebView2 初始化失敗：\n" + ex.Message, "狐霖記錄器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    try { Window.Close(); } catch { }
                }
            };
        }

        static void CreateControlWindow()
        {
            LoadUiLanguage();
            ControlWindow = new Form();
            ControlWindow.Text = T("狐霖記錄器控制", "狐霖记录器控制");
            ControlWindow.ClientSize = new Size(440, 530);
            ControlWindow.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ControlWindow.MaximizeBox = false;
            ControlWindow.MinimizeBox = true;
            ControlWindow.ShowInTaskbar = true;
            ControlWindow.TopMost = true;
            ControlWindow.StartPosition = FormStartPosition.Manual;
            ControlWindow.BackColor = Color.FromArgb(28, 28, 30);
            ControlWindow.ForeColor = Color.White;
            var screen = Screen.PrimaryScreen.WorkingArea;
            if (Window != null && !Window.IsDisposed)
            {
                ControlWindow.Location = new Point(Window.Right + 12, Window.Top + 20);
                if (ControlWindow.Right > screen.Right) ControlWindow.Location = new Point(Math.Max(screen.Left, Window.Left - ControlWindow.Width - 12), Window.Top + 20);
            }
            else
            {
                ControlWindow.Location = new Point(Math.Max(screen.Left + 12, screen.Right - ControlWindow.Width - 24), screen.Top + 60);
            }

            /* ── 版面：依功能分三組（客戶端／整理／輸出），狀態放最下面 ──
               2026-10-30 重排：以前 9 顆按鈕散在四排、沒有分組，常用的
               「開啟客戶端」和「結束程式」長得一樣大。 */

            /* ── 1. 客戶端 ── */
            var grpClient = NewGroup("客戶端", "客户端", 12, 10, 416, 132);

            OpenClientButton = new Button();
            OpenClientButton.SetBounds(14, 26, 190, 40);
            OpenClientButton.Click += delegate { OpenGameClient(); };
            Ui(OpenClientButton, "開啟客戶端", "开启客户端", 10F, true);
            StyleBtn(OpenClientButton, true);
            grpClient.Controls.Add(OpenClientButton);

            CloseClientButton = new Button();
            CloseClientButton.SetBounds(212, 26, 190, 40);
            CloseClientButton.Click += delegate { CloseGameClient(); };
            Ui(CloseClientButton, "關閉客戶端", "关闭客户端", 10F, true);
            StyleBtn(CloseClientButton, false);
            grpClient.Controls.Add(CloseClientButton);

            StartRecBtn = new Button();
            StartRecBtn.SetBounds(14, 72, 120, 28);
            StartRecBtn.Click += delegate { ResumeRecording(); };
            Ui(StartRecBtn, "開始記錄", "开始记录", 9F, false);
            StyleBtn(StartRecBtn, false);
            grpClient.Controls.Add(StartRecBtn);

            PauseRecBtn = new Button();
            PauseRecBtn.SetBounds(142, 72, 120, 28);
            PauseRecBtn.Click += delegate { PauseRecording(); };
            Ui(PauseRecBtn, "暫停記錄", "暂停记录", 9F, false);
            StyleBtn(PauseRecBtn, false);
            grpClient.Controls.Add(PauseRecBtn);

            SnapButton = new Button();
            SnapButton.SetBounds(270, 72, 132, 28);
            SnapButton.Click += async delegate { await CaptureManual(); };
            Ui(SnapButton, "手動截圖", "手动截图", 9F, false);
            StyleBtn(SnapButton, false);
            grpClient.Controls.Add(SnapButton);

            SnapStatus = new Label();
            SnapStatus.AutoEllipsis = true;
            SnapStatus.SetBounds(14, 104, 388, 18);
            SnapStatus.ForeColor = Color.FromArgb(185, 185, 185);
            Ui(SnapStatus, "暫停後再按「開始記錄」會續錄同一場；截圖存到 logs\\manual", "暂停后再按「开始记录」会续录同一场；截图存到 logs\\manual", 8.5F, false);
            grpClient.Controls.Add(SnapStatus);

            /* ── 2. 整理 ── */
            var grpRun = NewGroup("整理", "整理", 12, 150, 416, 158);

            StopButton = new Button();
            StopButton.SetBounds(14, 26, 190, 34);
            StopButton.Click += delegate { BeginShutdown(false); };
            Ui(StopButton, "停止並整理", "停止并整理", 9.5F, true);
            StyleBtn(StopButton, true);
            grpRun.Controls.Add(StopButton);

            var refresh = new Button();
            refresh.SetBounds(212, 26, 190, 34);
            refresh.Click += delegate { RefreshStatus(); };
            Ui(refresh, "重整狀態", "刷新状态", 9.5F, false);
            StyleBtn(refresh, false);
            grpRun.Controls.Add(refresh);

            StageLabel = new Label();
            StageLabel.ForeColor = Color.FromArgb(205, 205, 210);
            StageLabel.SetBounds(14, 68, 388, 38);
            Ui(StageLabel, "目前整理：—", "目前整理：—", 9F, false);
            grpRun.Controls.Add(StageLabel);

            StageBar = new ProgressBar();
            StageBar.SetBounds(14, 112, 388, 14);
            StageBar.Minimum = 0;
            StageBar.Maximum = 100;
            StageBar.Value = 0;
            grpRun.Controls.Add(StageBar);

            /* ── 3. 輸出 ── */
            var grpOut = NewGroup("輸出", "输出", 12, 316, 416, 108);

            ExportButton = new Button();
            ExportButton.SetBounds(14, 24, 388, 32);
            ExportButton.Click += delegate { BeginExportSnapshot(); };
            Ui(ExportButton, "匯出目前 logs", "导出目前 logs", 9.5F, false);
            StyleBtn(ExportButton, false);
            grpOut.Controls.Add(ExportButton);

            var folder = new Button();
            folder.SetBounds(14, 64, 120, 30);
            folder.Click += delegate { OpenLogFolder(); };
            Ui(folder, "開啟 logs", "打开 logs");
            StyleBtn(folder, false);
            grpOut.Controls.Add(folder);

            var open = new Button();
            open.SetBounds(142, 64, 120, 30);
            open.Click += delegate { OpenManualFolder(); };
            Ui(open, "開啟截圖", "打开截图");
            StyleBtn(open, false);
            grpOut.Controls.Add(open);

            var exports = new Button();
            exports.SetBounds(270, 64, 132, 30);
            exports.Click += delegate { OpenExportFolder(); };
            Ui(exports, "開啟匯出", "打开导出");
            StyleBtn(exports, false);
            grpOut.Controls.Add(exports);

            /* ── 4. 狀態列 ── */
            StateLabel = new Label();
            StateLabel.ForeColor = Color.FromArgb(232, 232, 236);
            StateLabel.SetBounds(14, 436, 412, 22);
            Ui(StateLabel, "狀態：等待遊戲載入", "状态：等待游戏载入", 10F, true);
            ControlWindow.Controls.Add(StateLabel);

            InputLabel = new Label();
            InputLabel.ForeColor = Color.FromArgb(170, 220, 255);
            InputLabel.SetBounds(14, 460, 412, 20);
            Ui(InputLabel, "輸入事件：0", "输入事件：0", 9F, false);
            ControlWindow.Controls.Add(InputLabel);

            /* ── 5. 語言切換 + 結束 ── */
            LangButton = new Button();
            LangButton.SetBounds(14, 486, 130, 28);
            LangButton.Click += delegate { ToggleUiLanguage(); };
            LangButton.Text = T("語言：繁體", "语言：简体");
            LangButton.Font = UiFont(9F, false);
            StyleBtn(LangButton, false);
            ControlWindow.Controls.Add(LangButton);

            ExitButton = new Button();
            ExitButton.SetBounds(152, 486, 276, 28);
            ExitButton.Click += delegate { ExitApplication(); };
            Ui(ExitButton, "結束程式", "结束程序", 9F, false);
            StyleBtn(ExitButton, false);
            ControlWindow.Controls.Add(ExitButton);

            ControlWindow.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (AllowAppExit) return;
                bool busy = PipelineRunning || (Recorder != null && !Recorder.HasExited);
                if (busy)
                {
                    e.Cancel = true;
                    SetState("請先按「停止並整理」或「結束程式」", Color.FromArgb(255, 190, 100));
                }
                else
                {
                    AllowAppExit = true;
                }
            };

            UiTimer = new System.Windows.Forms.Timer();
            UiTimer.Interval = 500;
            UiTimer.Tick += delegate { RefreshStatus(); };
            UiTimer.Start();
        }

        static string GetManualDir()
        {
            try
            {
                if (!string.IsNullOrEmpty(RecordOut) && Directory.Exists(RecordOut))
                {
                    var dirs = Directory.GetDirectories(RecordOut, "backroom-*");
                    if (dirs.Length > 0)
                    {
                        Array.Sort(dirs);
                        return Path.Combine(dirs[dirs.Length - 1], "manual");
                    }
                    return Path.Combine(RecordOut, "manual");
                }
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manual");
        }

        static async System.Threading.Tasks.Task CaptureManual()
        {
            string file = null;
            try
            {
                if (View == null || View.CoreWebView2 == null) { SnapStatus.Text = "WebView 尚未就緒"; return; }
                ManualDir = GetManualDir();
                Directory.CreateDirectory(ManualDir);
                file = Path.Combine(ManualDir, "manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                using (var fs = File.Create(file))
                {
                    await View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                }
                try
                {
                    string rec = "{\"time\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",\"file\":\"" + Esc(file) + "\",\"url\":\"" + Esc(Url) + "\"}" + Environment.NewLine;
                    File.AppendAllText(Path.Combine(ManualDir, "index.jsonl"), rec, new UTF8Encoding(false));
                }
                catch { }
                SnapStatus.Text = "已存：" + Path.GetFileName(file);
            }
            catch (Exception ex)
            {
                // WebView 擷取失敗時退回視窗畫面（不含控制窗）
                try
                {
                    if (file == null) { ManualDir = GetManualDir(); Directory.CreateDirectory(ManualDir); file = Path.Combine(ManualDir, "manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png"); }
                    var rect = Window.Bounds;
                    using (var bmp = new Bitmap(rect.Width, rect.Height))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size);
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    SnapStatus.Text = "已存（視窗截圖）：" + Path.GetFileName(file);
                }
                catch { SnapStatus.Text = "截圖失敗：" + ex.Message; }
            }
        }

        static void OpenManualFolder()
        {
            try { string d = GetManualDir(); Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); } catch { }
        }

        static void OpenLogFolder()
        {
            try
            {
                string d = RecordOut;
                if (string.IsNullOrEmpty(d)) d = AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(d);
                Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch { }
        }

        static void OpenExportFolder()
        {
            try
            {
                string d = Path.Combine(string.IsNullOrEmpty(RecordOut) ? AppDomain.CurrentDomain.BaseDirectory : RecordOut, "exports");
                Directory.CreateDirectory(d);
                Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch { }
        }

        static void StartAfterLoad()
        {
            if (RecorderStarted) return;
            RecorderStarted = true;
            var t = new Thread(delegate ()
            {
                Thread.Sleep(2500);
                try
                {
                    Window.BeginInvoke((Action)delegate
                    {
                        if (Record) StartRecorder(Window.Handle);
                        StartVolumeWatcher();
                        WriteHandshake();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static int FindFreeDebugPort(int preferred)
        {
            for (int p = Math.Max(1024, preferred); p < Math.Max(1024, preferred) + 50; p++)
            {
                TcpListener l = null;
                try
                {
                    l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    return p;
                }
                catch { }
                finally { try { if (l != null) l.Stop(); } catch { } }
            }
            // 指派的埠被其他程式（例如 OVRServer）佔用時，讓 OS 給一個真正空閒的埠。
            for (int i = 0; i < 5; i++)
            {
                TcpListener l = null;
                try
                {
                    l = new TcpListener(IPAddress.Loopback, 0);
                    l.Start();
                    return ((IPEndPoint)l.LocalEndpoint).Port;
                }
                catch { }
                finally { try { if (l != null) l.Stop(); } catch { } }
            }
            return preferred;
        }

        static bool TryPreset(string s, out int w, out int h)
        {
            w = 0; h = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim().ToLowerInvariant().Replace('×', 'x').Replace('*', 'x');
            switch (t)
            {
                case "1280x720": w = 1280; h = 720; return true;
                case "1600x900": w = 1600; h = 900; return true;
                case "1920x1080": w = 1920; h = 1080; return true;
            }
            return false;
        }

        static bool EnsureWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(p, "ok");
                try { File.Delete(p); } catch { }
                return true;
            }
            catch { return false; }
        }

        static void StartVolumeWatcher()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string script = Path.Combine(exeDir, "volume-watch.ps1");
                if (!File.Exists(script)) { Log("找不到 volume-watch.ps1，這次不記錄音量。"); return; }
                string logDir = string.IsNullOrEmpty(RecordOut) ? Path.Combine(exeDir, "logs") : RecordOut;
                Directory.CreateDirectory(logDir);
                VolumeStopFile = Path.Combine(Path.GetTempPath(), "FoyerVolume-stop-" + Process.GetCurrentProcess().Id + ".flag");
                try { if (File.Exists(VolumeStopFile)) File.Delete(VolumeStopFile); } catch { }
                string ps = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -LogDir \"" + logDir + "\" -StopFile \"" + VolumeStopFile + "\"";
                var psi = new ProcessStartInfo("powershell.exe", ps);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                VolumeWatcher = Process.Start(psi);
                Log("音量監測啟動：PID " + (VolumeWatcher == null ? "?" : VolumeWatcher.Id.ToString()) + "，輸出 " + logDir);
            }
            catch (Exception ex) { Log("音量監測啟動失敗：" + ex.Message); }
        }

        static void StartRecorder(IntPtr hwnd)
        {
            StartRecorder(hwnd, null);
        }

        /* resumeDir：非空 = 續錄到同一個場次（暫停 → 開始記錄）。 */
        static void StartRecorder(IntPtr hwnd, string resumeDir)
        {
            try
            {
                /* 先收掉舊的記錄器：以前開第二個客戶端會同時跑兩個記錄器，
                   兩個都往 logs 寫，收尾時只整理到最後一個，前一個變孤兒
                   （2026-10-30 09:58 那場就是這樣被強殺、沒有收工）。 */
                if (Recorder != null && !Recorder.HasExited)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(StopFile)) File.WriteAllText(StopFile, "stop");
                        if (!Recorder.WaitForExit(8000)) { try { Recorder.Kill(); } catch { } }
                        if (!string.IsNullOrEmpty(StopFile) && File.Exists(StopFile)) File.Delete(StopFile);
                    }
                    catch { }
                }
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] cs = {
                    Path.Combine(exeDir, "backroom-logger.ps1"),
                    Path.GetFullPath(Path.Combine(exeDir, @"..\backroom-logger.ps1")),
                    Path.GetFullPath(Path.Combine(exeDir, @"..\..\记录器\backroom-logger.ps1"))
                };
                foreach (var c in cs) if (File.Exists(c)) { RecorderScript = c; break; }
                if (RecorderScript == null) { Log("找不到 backroom-logger.ps1，這次不錄 logs。"); return; }
                if (string.IsNullOrEmpty(RecordOut)) RecordOut = Path.Combine(exeDir, "logs");
                if (!EnsureWritable(RecordOut)) { RecordOut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "logs"); EnsureWritable(RecordOut); }
                Directory.CreateDirectory(RecordOut);
                StopFile = Path.Combine(Path.GetTempPath(), "FoyerLogger-stop-" + Process.GetCurrentProcess().Id + ".flag");
                try { if (File.Exists(StopFile)) File.Delete(StopFile); } catch { }
                string ps = "-NoProfile -ExecutionPolicy Bypass -File \"" + RecorderScript + "\""
                    + " -OutDir \"" + RecordOut + "\" -Hwnd " + hwnd.ToInt64()
                    + " -NoResize -Quiet -Interval " + CaptureInterval.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " -SaveLineCrops -AutoFloorShots -StopFile \"" + StopFile + "\""
                    + (ShowOverlay ? "" : " -NoOverlay");
                if (!string.IsNullOrEmpty(resumeDir) && Directory.Exists(resumeDir))
                    ps += " -SessionDir \"" + resumeDir + "\"";
                var psi = new ProcessStartInfo("powershell.exe", ps);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Path.GetDirectoryName(RecorderScript);
                Recorder = Process.Start(psi);
                Log("logs 記錄器啟動：PID " + (Recorder == null ? "?" : Recorder.Id.ToString()) + "，輸出 " + RecordOut + (string.IsNullOrEmpty(resumeDir) ? "" : "（續錄）"));
            }
            catch (Exception ex) { Log("logs 記錄器啟動失敗：" + ex.Message); }
        }

        static void CloseGameClient()
        {
            if (GameClosed) return;
            try { if (Window != null && !Window.IsDisposed) Window.Close(); } catch { }
        }

        /* ── 開始記錄／暫停記錄（不用關遊戲也不用重開程式）──
           暫停只停記錄器，不跑整理；再按開始會「續錄」回同一個場次
           （靠記錄器的 -SessionDir），檔案往後追加，不會變成兩個場次。 */
        static bool Paused = false;

        static void PauseRecording()
        {
            if (PipelineRunning) return;
            if (Recorder == null || Recorder.HasExited)
            {
                SetState(T("狀態：目前沒有在錄影", "状态：目前没有在录像"), Color.FromArgb(200, 200, 200));
                return;
            }
            SetState(T("狀態：正在暫停…", "状态：正在暂停…"), Color.FromArgb(255, 190, 100));
            var t = new Thread(delegate ()
            {
                try
                {
                    StopRecorderCore();
                    Paused = true;
                    SetState(T("狀態：已暫停（遊戲還開著）", "状态：已暂停（游戏还开着）"), Color.FromArgb(255, 190, 100));
                    Log("recording paused");
                }
                catch (Exception ex) { Log("pause failed: " + ex); }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void ResumeRecording()
        {
            if (PipelineRunning) return;
            if (Recorder != null && !Recorder.HasExited)
            {
                SetState(T("狀態：已經在錄影", "状态：已经在录像"), Color.FromArgb(130, 220, 140));
                return;
            }
            if (Window == null || Window.IsDisposed)
            {
                SetState(T("狀態：遊戲視窗不在，請先開啟客戶端", "状态：游戏窗口不在，请先开启客户端"), Color.FromArgb(255, 190, 100));
                return;
            }
            string resume = Paused ? ActiveSession : null;
            if (!string.IsNullOrEmpty(resume) && !Directory.Exists(resume)) resume = null;
            StartRecorder(Window.Handle, resume);
            Paused = false;
            SetState(T("狀態：記錄中", "状态：记录中"), Color.FromArgb(130, 220, 140));
        }

        static void ExitApplication()
        {
            if (PipelineRunning)
            {
                SetState("整理中，完成後會自動結束", Color.FromArgb(255, 190, 100));
                return;
            }
            if (Recorder != null && !Recorder.HasExited)
            {
                BeginShutdown(true);
                return;
            }
            AllowAppExit = true;
            Application.Exit();
        }

        static void BeginShutdown(bool exitAfter)
        {
            if (PipelineRunning) return;
            /* 沒有在錄影就不要觸發整理（避免誤按）；但「暫停中」是例外 ——
               使用者按了暫停、又把遊戲關掉，這時還是要把這一場整理掉。 */
            if ((Recorder == null || Recorder.HasExited) && !Paused)
            {
                SetState(T("狀態：目前沒有在錄影", "状态：目前没有在录像"), Color.FromArgb(200, 200, 200));
                return;
            }
            PipelineRunning = true;
            PipelineStartedAt = DateTime.Now;
            Paused = false;
            SetState(T("狀態：正在停止記錄器…", "状态：正在停止记录器…"), Color.FromArgb(255, 190, 100));

            var t = new Thread(delegate ()
            {
                try
                {
                    StopRecorderCore();
                    RunPostprocess();
                }
                catch (Exception ex) { Log("shutdown: " + ex); }
                finally
                {
                    PipelineRunning = false;
                    if (exitAfter)
                    {
                        AllowAppExit = true;
                        try { ControlWindow.BeginInvoke((Action)delegate { Application.Exit(); }); } catch { }
                    }
                    else
                    {
                        SetState("狀態：整理完成", Color.FromArgb(130, 220, 140));
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void BeginExportSnapshot()
        {
            if (PipelineRunning) return;
            string live = string.IsNullOrEmpty(ActiveSession) ? LatestSession() : ActiveSession;
            if (string.IsNullOrEmpty(live) || !Directory.Exists(live))
            {
                SetState("狀態：還沒有可匯出的場次", Color.FromArgb(255, 190, 100));
                return;
            }
            PipelineRunning = true;
            ExportSession = null;
            SetState("狀態：建立 logs 快照…", Color.FromArgb(255, 190, 100));
            if (StopButton != null) StopButton.Enabled = false;
            if (ExportButton != null) ExportButton.Enabled = false;
            if (ExitButton != null) ExitButton.Enabled = false;
            if (CloseClientButton != null) CloseClientButton.Enabled = false;

            var t = new Thread(delegate ()
            {
                try
                {
                    string snap = CreateSnapshot(live);
                    ExportSession = snap;
                    SetSnapStatus("已建立快照：" + Path.GetFileName(snap));
                    SetState("狀態：匯出處理中…", Color.FromArgb(255, 190, 100));
                    RunSnapshotPipeline(snap);
                    SetSnapStatus("已匯出：" + Path.GetFileName(snap));
                    SetState("狀態：已匯出目前 logs", Color.FromArgb(130, 220, 140));
                }
                catch (Exception ex)
                {
                    Log("snapshot export failed: " + ex);
                    SetState("狀態：匯出失敗（看 player.log）", Color.FromArgb(255, 120, 110));
                }
                finally
                {
                    PipelineRunning = false;
                    if (ExportButton != null) ExportButton.Enabled = true;
                    if (StopButton != null) StopButton.Enabled = Recorder != null && !Recorder.HasExited;
                    if (ExitButton != null) ExitButton.Enabled = true;
                    if (CloseClientButton != null) CloseClientButton.Enabled = !GameClosed;
                    if (PendingShutdown)
                    {
                        PendingShutdown = false;
                        BeginShutdown(false);
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static string CreateSnapshot(string live)
        {
            string root = Path.Combine(RecordOut, "exports");
            string name = "snapshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string dst = Path.Combine(root, name);
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.GetFiles(live))
            {
                string fn = Path.GetFileName(file);
                string to = Path.Combine(dst, fn);
                if (fn.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) CopyJsonlSnapshot(file, to);
                else CopyShared(file, to);
            }
            foreach (string dir in Directory.GetDirectories(live))
            {
                string dn = Path.GetFileName(dir);
                if (dn.Equals("manual", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectoryShared(dir, Path.Combine(dst, dn));
            }
            string meta = "{\"source\":\"" + Esc(live) + "\",\"created\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",\"inputEvents\":" + InputCount + "}";
            File.WriteAllText(Path.Combine(dst, "snapshot.json"), meta, new UTF8Encoding(false));
            Log("logs 快照：" + dst);
            return dst;
        }

        static void CopyJsonlSnapshot(string src, string dst)
        {
            string text;
            using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true)) text = sr.ReadToEnd();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var keep = new System.Collections.Generic.List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (i == lines.Length - 1 && !text.EndsWith("\n")) continue;
                keep.Add(lines[i]);
            }
            File.WriteAllText(dst, string.Join(Environment.NewLine, keep) + (keep.Count > 0 ? Environment.NewLine : ""), new UTF8Encoding(false));
        }

        static void CopyShared(string src, string dst)
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output, 131072);
            }
        }

        static void CopyDirectoryShared(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                try { CopyShared(f, Path.Combine(dst, Path.GetFileName(f))); } catch { }
            }
            foreach (string d in Directory.GetDirectories(src))
            {
                try { CopyDirectoryShared(d, Path.Combine(dst, Path.GetFileName(d))); } catch { }
            }
        }

        static void RunSnapshotPipeline(string snap)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string run = Path.Combine(exeDir, "structured", "run.mjs");
            if (!File.Exists(run)) throw new FileNotFoundException("structured/run.mjs");
            var psi = new ProcessStartInfo(NodeExe(exeDir), "\"" + run + "\" \"" + snap + "\" --portable");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = exeDir;
            var p = Process.Start(psi);
            if (p == null) throw new Exception("無法啟動 structured/run.mjs");
            p.WaitForExit();
            Log("snapshot pipeline exit " + p.ExitCode + "：" + snap);
            if (p.ExitCode != 0) throw new Exception("structured/run.mjs exit " + p.ExitCode);
        }

        static void StopRecorderCore()
        {
            try
            {
                if (!string.IsNullOrEmpty(StopFile)) File.WriteAllText(StopFile, "stop");
                if (Recorder != null && !Recorder.HasExited && !Recorder.WaitForExit(5000))
                { try { Recorder.Kill(); } catch { } }
            }
            catch { }
            finally { try { if (!string.IsNullOrEmpty(StopFile) && File.Exists(StopFile)) File.Delete(StopFile); } catch { } }
            try
            {
                if (!string.IsNullOrEmpty(VolumeStopFile)) File.WriteAllText(VolumeStopFile, "stop");
                if (VolumeWatcher != null && !VolumeWatcher.HasExited && !VolumeWatcher.WaitForExit(2000))
                { try { VolumeWatcher.Kill(); } catch { } }
                if (!string.IsNullOrEmpty(VolumeStopFile) && File.Exists(VolumeStopFile)) File.Delete(VolumeStopFile);
            }
            catch { }
            try
            {
                string ses = string.IsNullOrEmpty(ActiveSession) ? LatestSession() : ActiveSession;
                if (!string.IsNullOrEmpty(ses)) { ActiveSession = ses; FlushInputBuffer(ses); }
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

        static void SetState(string text, Color color)
        {
            try
            {
                if (ControlWindow == null || ControlWindow.IsDisposed) return;
                if (ControlWindow.InvokeRequired)
                {
                    ControlWindow.BeginInvoke((Action)delegate { SetState(text, color); });
                    return;
                }
                if (StateLabel != null) { StateLabel.Text = text; StateLabel.ForeColor = color; }
            }
            catch { }
        }

        static void SetProgressBar(int pct)
        {
            try
            {
                if (StageBar == null || StageBar.IsDisposed) return;
                if (ControlWindow != null && ControlWindow.InvokeRequired)
                {
                    ControlWindow.BeginInvoke((Action)delegate { SetProgressBar(pct); });
                    return;
                }
                int v = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                if (StageBar.Value != v) StageBar.Value = v;
            }
            catch { }
        }

        static void SetSnapStatus(string text)        {
            try
            {
                if (ControlWindow == null || ControlWindow.IsDisposed) return;
                if (ControlWindow.InvokeRequired)
                {
                    ControlWindow.BeginInvoke((Action)delegate { SetSnapStatus(text); });
                    return;
                }
                if (SnapStatus != null) SnapStatus.Text = text;
            }
            catch { }
        }

        static int IntAfter(string s, string key, int fallback)
        {
            try
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (i < 0) return fallback;
                i = s.IndexOf(':', i);
                if (i < 0) return fallback;
                int j = i + 1;
                while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
                int k = j;
                while (k < s.Length && char.IsDigit(s[k])) k++;
                int v;
                return int.TryParse(s.Substring(j, k - j), out v) ? v : fallback;
            }
            catch { return fallback; }
        }

        static string StrAfter(string s, string key)
        {
            try
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (i < 0) return null;
                i = s.IndexOf(':', i);
                if (i < 0) return null;
                i = s.IndexOf('"', i + 1);
                if (i < 0) return null;
                var sb = new StringBuilder();
                for (int j = i + 1; j < s.Length; j++)
                {
                    if (s[j] == '\\' && j + 1 < s.Length) { sb.Append(s[++j]); continue; }
                    if (s[j] == '"') break;
                    sb.Append(s[j]);
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        static void RefreshStatus()
        {
            try
            {
                if (ControlWindow == null || ControlWindow.IsDisposed) return;
                if (ControlWindow.InvokeRequired)
                {
                    ControlWindow.BeginInvoke((Action)RefreshStatus);
                    return;
                }
                string ses = LatestSession();
                if (!string.IsNullOrEmpty(ses)) { ActiveSession = ses; FlushInputBuffer(ses); }

                bool rec = Recorder != null && !Recorder.HasExited;
                if (InputLabel != null) InputLabel.Text = T("輸入事件：", "输入事件：") + InputCount;
                /* 按鈕不再切 Enabled：深色底上的停用按鈕會變成看不見的灰字。
                   該不該做由各 handler 自己判斷，並在狀態列說明。 */

                string progressSession = !string.IsNullOrEmpty(ExportSession) ? ExportSession : ActiveSession;
                string pf = string.IsNullOrEmpty(progressSession) ? null : Path.Combine(progressSession, "progress.json");
                int pct = -1, cur = 0, total = 0;
                string stage = null, detail = null, item = null;
                if (!string.IsNullOrEmpty(pf) && File.Exists(pf))
                {
                    string txt = File.ReadAllText(pf);
                    pct = IntAfter(txt, "percent", -1);
                    stage = StrAfter(txt, "stage");
                    detail = StrAfter(txt, "detail");
                    cur = IntAfter(txt, "current", 0);
                    total = IntAfter(txt, "total", 0);
                    item = StrAfter(txt, "item");
                }

                if (PipelineRunning)
                {
                    if (pct >= 0 || !string.IsNullOrEmpty(stage))
                    {
                        /* 進度一律寫成 (n/100)，跟使用者要的一致 */
                        string line1 = T("目前整理：", "目前整理：") + (stage ?? T("整理中", "整理中"));
                        if (pct >= 0) line1 += " (" + pct + "/100)";
                        string line2 = "";
                        if (cur > 0 && total > 0) line2 = T("第 ", "第 ") + cur + " / " + total + T(" 條", " 条") + (string.IsNullOrEmpty(item) ? "" : "：" + item);
                        else if (!string.IsNullOrEmpty(item)) line2 = item;
                        else if (!string.IsNullOrEmpty(detail)) line2 = detail;
                        if (StageLabel != null) StageLabel.Text = line2.Length > 0 ? line1 + "\n" + line2 : line1;
                    }
                    else
                    {
                        if (StageLabel != null) StageLabel.Text = T("目前整理：準備中…", "目前整理：准备中…");
                    }
                    SetProgressBar(pct);
                    SetState(T("狀態：整理中", "状态：整理中"), Color.FromArgb(255, 190, 100));
                    return;
                }

                if (rec)
                {
                    Paused = false;
                    SetState(T("狀態：記錄中", "状态：记录中"), Color.FromArgb(130, 220, 140));
                    if (StageLabel != null) StageLabel.Text = T("目前整理：等待停止後整理", "目前整理：等待停止后整理");
                    SetProgressBar(0);
                    return;
                }

                if (Paused && !GameClosed)
                {
                    SetState(T("狀態：已暫停", "状态：已暂停"), Color.FromArgb(255, 190, 100));
                    if (StageLabel != null) StageLabel.Text = T("按「開始記錄」續錄同一場", "按「开始记录」续录同一场");
                    SetProgressBar(0);
                    return;
                }

                if (GameClosed)
                {
                    SetState(T("狀態：客戶端已關閉", "状态：客户端已关闭"), Color.FromArgb(200, 200, 200));
                    if (StageLabel != null) StageLabel.Text = pct >= 0 ? T("整理完成：", "整理完成：") + (stage ?? T("完成", "完成")) + " (" + pct + "/100)" : T("已完成或無場次", "已完成或無场次");
                    SetProgressBar(pct);
                }
                else
                {
                    SetState(RecorderStarted ? T("狀態：已載入（未錄影）", "状态：已载入（未录像）") : T("狀態：等待遊戲載入", "状态：等待游戏载入"), Color.FromArgb(200, 200, 200));
                    /* 這裡也要更新 StageLabel：以前只改狀態列，於是
                       「目前整理：build (90/100)」會一直留在那裡，看起來像卡死
                       ——其實整理早就跑完、資料也進 Wiki 了。 */
                    if (StageLabel != null)
                    {
                        StageLabel.Text = (pct >= 100)
                            ? T("上次整理：", "上次整理：") + (stage ?? T("完成", "完成")) + " (" + pct + "/100)"
                            : T("目前整理：—", "目前整理：—");
                    }
                    SetProgressBar(0);
                }
            }
            catch { }
        }

        static readonly string InputCaptureScript = @"
(function(){
  if(window.__foyerInputCapture) return;
  window.__foyerInputCapture = true;
  function targetName(e){
    try { var t=e.target; return (t && t.tagName) ? String(t.tagName) : ''; } catch(x){ return ''; }
  }
  function post(o){
    try {
      o.t = Date.now();
      o.p = (window.performance && performance.now) ? Math.round(performance.now()) : 0;
      o.url = location.href;
      o.title = document.title || '';
      window.chrome.webview.postMessage(o);
    } catch(e) {}
  }
  addEventListener('keydown', function(e){
    post({type:'keydown',key:e.key,code:e.code,keyCode:e.keyCode,repeat:!!e.repeat,target:targetName(e)});
  }, true);
  addEventListener('keyup', function(e){
    post({type:'keyup',key:e.key,code:e.code,keyCode:e.keyCode,target:targetName(e)});
  }, true);
  addEventListener('mousedown', function(e){
    post({type:'mousedown',x:e.clientX,y:e.clientY,button:e.button,buttons:e.buttons,target:targetName(e)});
  }, true);
  addEventListener('mouseup', function(e){
    post({type:'mouseup',x:e.clientX,y:e.clientY,button:e.button,buttons:e.buttons,target:targetName(e)});
  }, true);
  addEventListener('wheel', function(e){
    post({type:'wheel',x:e.clientX,y:e.clientY,dx:e.deltaX,dy:e.deltaY,target:targetName(e)});
  }, {capture:true, passive:true});
  addEventListener('touchstart', function(e){
    var t=e.changedTouches && e.changedTouches[0];
    post({type:'touchstart',x:t?t.clientX:0,y:t?t.clientY:0,target:targetName(e)});
  }, {capture:true, passive:true});
  addEventListener('touchend', function(e){
    var t=e.changedTouches && e.changedTouches[0];
    post({type:'touchend',x:t?t.clientX:0,y:t?t.clientY:0,target:targetName(e)});
  }, {capture:true, passive:true});
  addEventListener('blur', function(){ post({type:'blur'}); }, true);
  addEventListener('focus', function(){ post({type:'focus'}); }, true);
})();
";

        static void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(json)) return;
                lock (InputLock)
                {
                    InputCount++;
                    string session = ActiveSession;
                    if (string.IsNullOrEmpty(session)) session = LatestSession();
                    if (!string.IsNullOrEmpty(session))
                    {
                        ActiveSession = session;
                        AppendInput(session, json);
                        FlushInputBuffer(session);
                    }
                    else
                    {
                        InputBuffer.Add(json);
                        if (InputBuffer.Count > 2000) InputBuffer.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex) { Log("input message failed: " + ex.Message); }
        }

        static void AppendInput(string session, string json)
        {
            try
            {
                string line = "{\"hostTs\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\",\"event\":" + json + "}" + Environment.NewLine;
                File.AppendAllText(Path.Combine(session, "input.jsonl"), line, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("input write failed: " + ex.Message); }
        }

        static void FlushInputBuffer(string session)
        {
            if (InputBuffer.Count == 0) return;
            foreach (string line in InputBuffer.ToArray()) AppendInput(session, line);
            InputBuffer.Clear();
        }

        static string NodeExe(string dir) { string p = Path.Combine(dir, "node.exe"); return File.Exists(p) ? p : "node.exe"; }

        static void RunPostprocess()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string script = Path.Combine(exeDir, "postprocess.mjs");
                string session = LatestSession();
                if (string.IsNullOrEmpty(session)) { SetState("狀態：沒有可整理的場次", Color.FromArgb(255, 150, 120)); return; }
                ActiveSession = session;
                if (!File.Exists(script)) return;

                var psi = new ProcessStartInfo(NodeExe(exeDir), "\"" + script + "\" \"" + session + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = exeDir;
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit();
                Log("已整理：" + Path.Combine(session, "clean"));
            }
            catch (Exception ex) { Log("整理失敗：" + ex.Message); }
        }

        static void WriteHandshake()
        {
            try
            {
                string json = "{\"pid\":" + Process.GetCurrentProcess().Id + ",\"hwnd\":" + Window.Handle.ToInt64()
                    + ",\"port\":" + Port + ",\"url\":\"" + Esc(Url) + "\",\"w\":" + W + ",\"h\":" + H
                    + ",\"logs\":\"" + Esc(RecordOut == null ? "" : RecordOut) + "\""
                    + ",\"logger\":" + (Recorder == null ? "0" : Recorder.Id.ToString())
                    + ",\"kind\":\"webview2\",\"started\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"}";
                WriteJson(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state", "player.json"), json);
                WriteJson(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoyerLogger", "player.json"), json);
            }
            catch { }
        }

        static void WriteJson(string path, string json)
        { try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json, new UTF8Encoding(false)); } catch { } }

        static string Esc(string s) { return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        static string OverlayPath()
        {
            return string.IsNullOrEmpty(RecordOut) ? null : Path.Combine(RecordOut, "overlay.json");
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);

        /* 覆蓋層要貼在 WebView2 的 render 子視窗，不是整個 Form client：
           render 子視窗不一定從 (0,0) 開始，也不一定等於 ClientSize。
           找不到時才退回 game.ClientRectangle。 */
        static Rectangle GetGameRenderRect(Form game)
        {
            Rectangle fallback = game.RectangleToScreen(game.ClientRectangle);
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(game.Handle, delegate(IntPtr h, IntPtr lp)
            {
                StringBuilder cn = new StringBuilder(128);
                GetClassName(h, cn, 128);
                if (IsWindowVisible(h) && cn.ToString() == "Chrome_RenderWidgetHostHWND")
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                RECT r;
                if (GetWindowRect(found, out r))
                {
                    Rectangle rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (rect.Width > 100 && rect.Height > 100) return rect;
                }
            }
            return fallback;
        }

        static OcrOverlayState ReadOverlay()
        {
            try
            {
                string p = OverlayPath();
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
                string json = File.ReadAllText(p, Encoding.UTF8);
                return new JavaScriptSerializer().Deserialize<OcrOverlayState>(json);
            }
            catch { return null; }
        }

        static void CreateOverlay()
        {
            try
            {
                if (Window == null || Window.IsDisposed) return;
                if (Overlay != null && !Overlay.IsDisposed) return;
                Overlay = new OcrOverlayForm(Window);
                Overlay.Show();
                Readout = new OcrReadoutForm(Window);
                Readout.Show();
                Log("OCR 覆蓋層已啟動");
            }
            catch (Exception ex) { Log("OCR 覆蓋層啟動失敗：" + ex.Message); }
        }

        static void CloseOverlay()
        {
            try { if (Overlay != null && !Overlay.IsDisposed) Overlay.Close(); } catch { }
            try { if (Readout != null && !Readout.IsDisposed) Readout.Close(); } catch { }
            Overlay = null;
            Readout = null;
        }

        static void Log(string line)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "player.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        /* 透明、點擊穿透的區塊框；只畫線，不畫底色，不擋操作。 */
        class OcrOverlayForm : Form
        {
            readonly Form game;
            readonly System.Windows.Forms.Timer timer;
            OcrOverlayState state;

            public OcrOverlayForm(Form game)
            {
                this.game = game;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                Width = 120; Height = 120;
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
                    CreateParams cp = base.CreateParams;
                    /* WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW */
                    cp.ExStyle |= 0x00000020 | 0x00080000 | 0x08000000 | 0x00000080;
                    return cp;
                }
            }

            void RefreshOverlay()
            {
                try
                {
                    if (game == null || game.IsDisposed || !game.Visible || game.WindowState == FormWindowState.Minimized)
                    {
                        if (Visible) Visible = false;
                        return;
                    }
                    Rectangle b = Program.GetGameRenderRect(game);
                    if (Bounds != b) Bounds = b;
                    state = Program.ReadOverlay();
                    if (!Visible) Visible = true;
                    Invalidate();
                }
                catch { }
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

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                /* 一定要填 TransparencyKey 的顏色；留空會變成黑色不透明，整個遊戲被蓋住。 */
                e.Graphics.Clear(BackColor);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                OcrOverlayState st = state;
                if (st == null || st.w <= 0 || st.h <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sx = ClientSize.Width / (float)st.w;
                float sy = ClientSize.Height / (float)st.h;
                g.ScaleTransform(sx, sy);
                float penW = 1.6f / Math.Max(0.2f, Math.Min(sx, sy));

                if (st.regions != null)
                {
                    foreach (OcrOverlayRegion r in st.regions)
                    {
                        using (Pen p = new Pen(RegionColor(r.n), penW))
                            g.DrawRectangle(p, r.x, r.y, r.w, r.h);
                    }
                }
                if (st.measured != null)
                {
                    using (Pen p = new Pen(Color.Orange, penW))
                    {
                        p.DashStyle = DashStyle.Dash;
                        foreach (OcrOverlayRegion r in st.measured)
                            g.DrawRectangle(p, r.x, r.y, r.w, r.h);
                    }
                }
                if (st.lines != null)
                {
                    foreach (OcrOverlayLine l in st.lines)
                    {
                        using (Pen p = new Pen(Color.FromArgb(170, RegionColor(l.r)), penW * 0.55f))
                            g.DrawRectangle(p, l.x, l.y, Math.Max(1, l.w), Math.Max(1, l.h));
                    }
                }
            }
        }

        /* 放在遊戲視窗旁邊的讀取面板；不覆蓋遊戲，能看目前 OCR 行。 */
        class OcrReadoutForm : Form
        {
            readonly Form game;
            readonly RichTextBox box;
            readonly System.Windows.Forms.Timer timer;
            string lastText = "";

            public OcrReadoutForm(Form game)
            {
                this.game = game;
                Text = "OCR 讀取";
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(16, 18, 22);
                Width = 400;
                Height = 620;
                box = new RichTextBox();
                box.Dock = DockStyle.Fill;
                box.ReadOnly = true;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Color.FromArgb(16, 18, 22);
                box.ForeColor = Color.Gainsboro;
                box.Font = new Font("Consolas", 9f);
                box.WordWrap = false;
                box.ScrollBars = RichTextBoxScrollBars.Vertical;
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
                    CreateParams cp = base.CreateParams;
                    /* WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW */
                    cp.ExStyle |= 0x08000000 | 0x00000080;
                    return cp;
                }
            }

            void RefreshReadout()
            {
                try
                {
                    if (game == null || game.IsDisposed || !game.Visible || game.WindowState == FormWindowState.Minimized)
                    {
                        if (Visible) Visible = false;
                        return;
                    }
                    Rectangle b = Program.GetGameRenderRect(game);
                    Rectangle wa = Screen.FromControl(game).WorkingArea;
                    int x;
                    if (b.Right + 10 + Width <= wa.Right) x = b.Right + 10;
                    else if (b.Left - 10 - Width >= wa.Left) x = b.Left - 10 - Width;
                    else { if (Visible) Visible = false; return; }   // 兩側都放不下就收起，不蓋遊戲
                    int y = Math.Max(wa.Top, Math.Min(b.Top, wa.Bottom - Height));
                    Location = new Point(x, y);
                    if (!Visible) Visible = true;

                    OcrOverlayState st = Program.ReadOverlay();
                    string text = FormatState(st);
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

            static string FormatState(OcrOverlayState st)
            {
                StringBuilder sb = new StringBuilder();
                if (st == null)
                {
                    sb.AppendLine("等待記錄器 overlay.json …");
                    sb.AppendLine(Program.OverlayPath() ?? "（RecordOut 尚未設定）");
                    return sb.ToString();
                }
                sb.AppendLine("FRAME " + st.frame + "  " + st.w + "x" + st.h + "  layout=" + st.layout + "  bg=" + st.bg);
                sb.AppendLine("SESSION " + st.session + "  fallbacks=" + st.fallbacks + "  running=" + st.running);
                sb.AppendLine("顏色：title 金／enemy 紅／main 綠／opts 青／char 洋紅；measured 橘虛線");
                sb.AppendLine();
                sb.AppendLine("REGIONS（實線）");
                if (st.regions != null)
                {
                    foreach (OcrOverlayRegion r in st.regions)
                        sb.AppendLine("  " + (r.n ?? "?").PadRight(7) + " x=" + r.x + " y=" + r.y + "  " + r.w + "x" + r.h);
                }
                sb.AppendLine();
                sb.AppendLine("MEASURED（虛線，自動校正量到的邊界）");
                if (st.measured != null && st.measured.Length > 0)
                {
                    foreach (OcrOverlayRegion r in st.measured)
                        sb.AppendLine("  " + (r.n ?? "?").PadRight(15) + " x=" + r.x + " y=" + r.y + "  " + r.w + "x" + r.h);
                }
                else sb.AppendLine("  （這一幀沒有量到）");
                sb.AppendLine();
                sb.AppendLine("LINES " + (st.lines == null ? 0 : st.lines.Length));
                if (st.lines != null)
                {
                    foreach (OcrOverlayLine l in st.lines)
                        sb.AppendLine("[" + (l.r ?? "?") + "] (" + l.x + "," + l.y + " " + l.w + "x" + l.h + ") " + Trunc(l.t, 120));
                }
                if (!st.running) sb.AppendLine();
                if (!st.running) sb.AppendLine("（記錄器已停止；這是 Close 前最後一幀）");
                return sb.ToString();
            }
        }
    }
}

/* JavaScriptSerializer 用的公開資料模型（overlay.json 由 backroom-logger.ps1 產生）。 */
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
