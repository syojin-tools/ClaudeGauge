using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\ClaudeGauge_SingleInstance", out createdNew))
            {
                if (!createdNew) return;   // 二重起動しない

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    DiagLog.Write("未処理例外: {0}", ex == null ? "(不明)" : ex.ToString());
                };
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    DiagLog.Write("UI スレッド例外: {0}", e.Exception.ToString());
                };

                using (TrayApp app = new TrayApp())
                {
                    Application.Run(app);
                }
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Settings _settings;
        private readonly System.Windows.Forms.Timer _tick;
        private FileSystemWatcher _watcher;
        private DetailForm _form;
        private TaskbarWidget _widget;
        private readonly ContextMenuStrip _menu;

        private RenderedIcon _icon;
        private UsageSnapshot _snap;

        private volatile bool _fileDirty;
        private DateTime _lastRead = DateTime.MinValue;
        private int _consecutiveFailures;

        // 通知の重複防止
        private bool _notifiedSession;
        private bool _notifiedWeekly;
        private int _prevSession = -1;
        private int _prevWeekly = -1;
        private DateTime? _prevSessionStart;
        private bool _firstRead = true;

        public TrayApp()
        {
            _settings = Settings.Load();
            Settings.CleanupLegacyStartup();
            DiagLog.Write("起動しました（{0} v{1}）", Constants.AppName, Constants.AppVersion);

            _menu = BuildMenu();

            _tray = new NotifyIcon();
            _tray.Visible = true;
            _tray.Text = Constants.AppDisplayName;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += OnTrayClick;
            _tray.BalloonTipClicked += delegate { ShowDetail(); };
            SetIcon("…", Theme.LevelColor(UsageLevel.Normal), false);

            ApplyWidgetVisibility();
            SetupWatcher();

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 1000;
            _tick.Tick += OnTick;
            _tick.Start();

            Refresh();
        }

        // ------------------------------------------------------------------
        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("詳細を開く");
            open.Click += delegate { ShowDetail(); };
            open.Font = new Font(menu.Font, FontStyle.Bold);

            ToolStripMenuItem now = new ToolStripMenuItem("今すぐ更新");
            now.Click += delegate { Refresh(); };

            ToolStripMenuItem exit = new ToolStripMenuItem("終了");
            exit.Click += delegate { ExitApp(); };

            menu.Items.Add(open);
            menu.Items.Add(now);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            return menu;
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowDetail();
        }

        private void ShowDetail()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new DetailForm(_settings, RefreshAndReturn, OnSettingsChanged, ResetWidgetPosition);
            }
            _form.UpdateSnapshot(_snap);
            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        }

        private UsageSnapshot RefreshAndReturn()
        {
            Refresh();
            return _snap;
        }

        private void OnSettingsChanged()
        {
            DiagLog.Write("設定が変更されました（更新間隔 {0} 秒 / 閾値 {1}% / 週次切替 {2}% / タスクバー表示 {3}）",
                _settings.PollSeconds, _settings.WarnPercent, _settings.WeeklySwitchPercent,
                _settings.ShowTaskbarWidget);
            ApplyWidgetVisibility();
            _lastRead = DateTime.MinValue;   // 次の tick で即読み直す
        }

        /// <summary>タスクバー上の大きい表示を、設定に合わせて出し入れする。</summary>
        private void ApplyWidgetVisibility()
        {
            try
            {
                if (_settings.ShowTaskbarWidget)
                {
                    if (_widget == null || _widget.IsDisposed)
                    {
                        _widget = new TaskbarWidget(_settings, ShowDetail, _menu);
                        _widget.Show();
                        DiagLog.Write("タスクバー表示を有効にしました（{0}）", _widget.Location);
                    }
                    _widget.SetData(_snap);
                }
                else if (_widget != null)
                {
                    _widget.Close();
                    _widget.Dispose();
                    _widget = null;
                    DiagLog.Write("タスクバー表示を無効にしました");
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("タスクバー表示の切り替えに失敗: {0} {1}", ex.GetType().Name, ex.Message);
            }
        }

        /// <summary>詳細ウィンドウの「位置をリセット」から呼ぶ。</summary>
        public void ResetWidgetPosition()
        {
            if (_widget != null && !_widget.IsDisposed) _widget.ResetPosition();
        }

        // ------------------------------------------------------------------
        private void SetupWatcher()
        {
            try
            {
                if (!Directory.Exists(Constants.ClaudeDataDir))
                {
                    DiagLog.Write("Claude データフォルダが無いため監視を開始しません: {0}", Constants.ClaudeDataDir);
                    return;
                }
                _watcher = new FileSystemWatcher(Constants.ClaudeDataDir, Constants.UsageHistoryFileName);
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                _watcher.Changed += OnFileEvent;
                _watcher.Created += OnFileEvent;
                _watcher.Renamed += OnFileEvent;
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
                DiagLog.Write("ファイル監視を開始しました");
            }
            catch (Exception ex)
            {
                DiagLog.Write("ファイル監視を開始できません（定期読み取りのみで動作）: {0}", ex.Message);
                _watcher = null;
            }
        }

        // 別スレッドから来るのでフラグだけ立て、UI タイマー側で拾う
        private void OnFileEvent(object sender, FileSystemEventArgs e) { _fileDirty = true; }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            DiagLog.Write("ファイル監視でエラー: {0}", e.GetException().Message);
            try
            {
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
            }
            catch { }
            SetupWatcher();
        }

        // ------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            if (_widget != null && !_widget.IsDisposed)
            {
                _widget.EnsureTopMost();
                _widget.Tick();   // 残り時間はファイルを読み直さなくても進む
            }

            double since = (DateTime.UtcNow - _lastRead).TotalSeconds;

            // 連続失敗時は指数バックオフ（最大 15 分）
            double interval = _settings.PollSeconds;
            if (_consecutiveFailures > 0)
            {
                double factor = Math.Pow(2, Math.Min(_consecutiveFailures, 6));
                interval = Math.Min(900, _settings.PollSeconds * factor);
            }

            bool due = since >= interval;
            bool dirty = _fileDirty && since >= 2;   // 書き込み直後のロックを避けて 2 秒待つ

            if (due || dirty)
            {
                _fileDirty = false;
                Refresh();
            }
            else if (_snap != null && _snap.Ok)
            {
                // 「あと何分」「最終更新 X 分前」は時間経過だけでも変わる
                UpdateTooltip();
            }
        }

        // ------------------------------------------------------------------
        private void Refresh()
        {
            _lastRead = DateTime.UtcNow;
            UsageSnapshot snap = UsageReader.Read(_settings.StaleMinutes);

            if (!snap.Ok)
            {
                _consecutiveFailures++;
                DiagLog.Write("読み取り失敗 {0} 回目: {1}", _consecutiveFailures, snap.ErrorMessage);
            }
            else
            {
                if (_consecutiveFailures > 0) DiagLog.Write("読み取りが回復しました");
                _consecutiveFailures = 0;
            }

            UsageSnapshot prev = _snap;
            _snap = snap;

            ApplyIcon(snap);
            UpdateTooltip();
            if (_widget != null && !_widget.IsDisposed) _widget.SetData(snap);

            if (snap.Ok) HandleNotifications(snap);

            if (_form != null && !_form.IsDisposed && _form.Visible) _form.UpdateSnapshot(snap);

            if (prev == null || prev.Ok != snap.Ok)
                DiagLog.Write("状態: {0}", snap.Ok ? "正常" : "エラー");
        }

        // ------------------------------------------------------------------
        private void ApplyIcon(UsageSnapshot snap)
        {
            string text;
            Color color;
            bool underline;
            IconState.Compute(snap, _settings, out text, out color, out underline);
            SetIcon(text, color, underline);
        }

        private void SetIcon(string text, Color color, bool underline)
        {
            try
            {
                RenderedIcon fresh = TrayIconRenderer.Render(text, color, underline);
                RenderedIcon old = _icon;
                _icon = fresh;
                _tray.Icon = fresh.Icon;
                if (old != null) old.Dispose();
            }
            catch (Exception ex)
            {
                DiagLog.Write("アイコンの描画に失敗: {0} {1}", ex.GetType().Name, ex.Message);
            }
        }

        // ------------------------------------------------------------------
        private void UpdateTooltip()
        {
            string text;
            UsageSnapshot s = _snap;

            if (s == null)
            {
                text = "読み取り中…";
            }
            else if (!s.Ok)
            {
                text = s.SourceMissing
                    ? "取得できません\nClaude アプリにログインしてください"
                    : "取得できません\n詳細ウィンドウを確認してください";
            }
            else
            {
                string line1 = "セッション " + Fmt(s.Session) + " / 週次 " + Fmt(s.Weekly);
                string line2;
                if (s.Stale)
                    line2 = "更新が止まっています";
                else if (s.SessionResetLocal.HasValue)
                {
                    TimeSpan left = s.SessionResetLocal.Value - DateTime.Now;
                    if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                    line2 = "リセットまで 約" + (left.TotalHours >= 1
                        ? (int)left.TotalHours + "時間" + left.Minutes + "分"
                        : Math.Max(0, (int)left.TotalMinutes) + "分");
                }
                else line2 = "リセット時刻は不明";

                // 数字が動かないときに「アプリが生きているか」を判断できるよう、
                // データの新しさと、最後にファイルを見に行った時刻を分けて出す。
                string line3 = "データ " + DetailForm.FormatAge(s.AgeMinutes)
                             + " / 確認 " + _lastRead.ToLocalTime().ToString("H:mm:ss");
                text = line1 + "\n" + line2 + "\n" + line3;
            }

            // NotifyIcon.Text は 63 文字まで
            if (text.Length > 63) text = text.Substring(0, 63);
            try { _tray.Text = text; }
            catch (Exception ex) { DiagLog.Write("ツールチップ設定に失敗: {0}", ex.Message); }
        }

        private static string Fmt(int v) { return v < 0 ? "－" : v + "%"; }

        // ------------------------------------------------------------------
        private void HandleNotifications(UsageSnapshot snap)
        {
            int session = snap.Session;
            int weekly = snap.Weekly;
            DateTime? start = snap.SessionStartLocal;

            // セッションが切り替わったら通知済みフラグを戻す
            bool newSession =
                (_prevSession > 0 && session == 0) ||
                (start.HasValue && _prevSessionStart.HasValue &&
                 Math.Abs((start.Value - _prevSessionStart.Value).TotalMinutes) > 10);

            if (newSession) _notifiedSession = false;
            if (_prevWeekly >= 0 && weekly >= 0 && weekly <= _prevWeekly - Constants.WeeklyResetDropPoints)
                _notifiedWeekly = false;

            if (!_firstRead && _settings.NotificationsEnabled)
            {
                if (_prevSession > 0 && session == 0)
                    Notify("5時間セッションがリセットされました", "使用率が 0% に戻りました。", ToolTipIcon.Info);

                if (_prevWeekly >= 0 && weekly >= 0 && weekly <= _prevWeekly - Constants.WeeklyResetDropPoints)
                    Notify("週次の使用量がリセットされました",
                        "週次使用率が " + _prevWeekly + "% → " + weekly + "% になりました。", ToolTipIcon.Info);

                if (!_notifiedSession && session >= _settings.WarnPercent)
                {
                    Notify("5時間セッションの使用率が " + session + "%",
                        "上限に近づいています（閾値 " + _settings.WarnPercent + "%）。", ToolTipIcon.Warning);
                    _notifiedSession = true;
                }

                if (!_notifiedWeekly && weekly >= _settings.WarnPercent)
                {
                    Notify("週次の使用率が " + weekly + "%",
                        "上限に近づいています（閾値 " + _settings.WarnPercent + "%）。", ToolTipIcon.Warning);
                    _notifiedWeekly = true;
                }
            }
            else if (_firstRead)
            {
                // 起動直後は現状を通知済みとみなし、いきなり鳴らさない
                if (session >= _settings.WarnPercent) _notifiedSession = true;
                if (weekly >= _settings.WarnPercent) _notifiedWeekly = true;
            }

            _prevSession = session;
            _prevWeekly = weekly;
            _prevSessionStart = start;
            _firstRead = false;
        }

        private void Notify(string title, string body, ToolTipIcon icon)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = body;
                _tray.BalloonTipIcon = icon;
                _tray.ShowBalloonTip(10000);
                DiagLog.Write("通知: {0}", title);
            }
            catch (Exception ex)
            {
                DiagLog.Write("通知の表示に失敗: {0}", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        private void ExitApp()
        {
            DiagLog.Write("終了します");
            _tick.Stop();
            _tray.Visible = false;
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tick != null) { _tick.Stop(); _tick.Dispose(); }
                if (_watcher != null) { try { _watcher.EnableRaisingEvents = false; } catch { } _watcher.Dispose(); }
                if (_widget != null && !_widget.IsDisposed) _widget.Dispose();
                if (_form != null && !_form.IsDisposed) _form.Dispose();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                if (_icon != null) _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
