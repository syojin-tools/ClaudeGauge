using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    internal sealed class DetailForm : Form
    {
        private readonly Settings _settings;
        private readonly Func<UsageSnapshot> _refresh;
        private readonly Action _settingsChanged;
        private readonly Action _resetWidgetPosition;
        private UsageSnapshot _snap;
        private bool _suppressEvents;
        private RenderedIcon _winIcon;

        private Label _banner;
        private SummaryPanel _summary;
        private TrendChart _chart;
        private FlowLayoutPanel _others;
        private Label _othersEmpty;
        private Label _status;

        private NumericUpDown _nudPoll, _nudWarn, _nudWeekly;
        private CheckBox _chkNotify, _chkStartup, _chkWidget;

        public DetailForm(Settings settings, Func<UsageSnapshot> refresh,
                          Action settingsChanged, Action resetWidgetPosition)
        {
            _settings = settings;
            _refresh = refresh;
            _settingsChanged = settingsChanged;
            _resetWidgetPosition = resetWidgetPosition;
            BuildUi();
        }

        // ------------------------------------------------------------------
        private void BuildUi()
        {
            Text = Constants.AppDisplayName;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 812);
            MinimumSize = new Size(620, 720);
            BackColor = Theme.WindowBack;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            ShowInTaskbar = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(14, 12, 14, 12);
            root.BackColor = Theme.WindowBack;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 0 バナー
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));       // 1 サマリ
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));        // 2 グラフ
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));        // 3 その他の枠
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 272));       // 4 設定
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));        // 5 ボタン
            Controls.Add(root);

            // ---- 0. 状態バナー ----
            _banner = new Label();
            _banner.Dock = DockStyle.Fill;
            _banner.AutoSize = false;
            _banner.Height = 0;
            _banner.Visible = false;
            _banner.TextAlign = ContentAlignment.MiddleLeft;
            _banner.Padding = new Padding(12, 8, 12, 8);
            _banner.Margin = new Padding(0, 0, 0, 8);
            _banner.BackColor = Theme.PanelBack;
            root.Controls.Add(_banner, 0, 0);

            // ---- 1. サマリ ----
            Panel summaryHost = MakeCard();
            summaryHost.Margin = new Padding(0, 0, 0, 10);
            _summary = new SummaryPanel();
            _summary.Dock = DockStyle.Fill;
            summaryHost.Controls.Add(_summary);
            root.Controls.Add(summaryHost, 0, 1);

            // ---- 2. グラフ ----
            Panel chartHost = MakeCard();
            chartHost.Margin = new Padding(0, 0, 0, 10);
            chartHost.Padding = new Padding(1, 26, 1, 1);
            Label chartTitle = new Label();
            chartTitle.Text = "  過去 " + Constants.HistoryRetentionDays + " 日の推移";
            chartTitle.Dock = DockStyle.Top;
            chartTitle.Height = 24;
            chartTitle.TextAlign = ContentAlignment.MiddleLeft;
            chartTitle.ForeColor = Theme.Muted;
            chartTitle.BackColor = Theme.PanelBack;
            _chart = new TrendChart();
            _chart.Dock = DockStyle.Fill;
            chartHost.Controls.Add(_chart);
            chartHost.Controls.Add(chartTitle);
            root.Controls.Add(chartHost, 0, 2);

            // ---- 3. その他の枠 ----
            Panel othersHost = MakeCard();
            othersHost.Margin = new Padding(0, 0, 0, 10);
            othersHost.Padding = new Padding(12, 6, 12, 6);
            _others = new FlowLayoutPanel();
            _others.Dock = DockStyle.Fill;
            _others.BackColor = Theme.PanelBack;
            _others.AutoScroll = true;
            _others.WrapContents = true;
            _othersEmpty = new Label();
            _othersEmpty.AutoSize = true;
            _othersEmpty.ForeColor = Theme.Muted;
            _othersEmpty.Text = "その他の使用枠は記録されていません";
            othersHost.Controls.Add(_others);
            root.Controls.Add(othersHost, 0, 3);

            // ---- 4. 設定 ----
            root.Controls.Add(BuildSettingsCard(), 0, 4);

            // ---- 5. ステータス＋ボタン ----
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Theme.WindowBack;

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Left;
            _status.Width = 300;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.Muted;
            bottom.Controls.Add(_status);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Right;
            btns.FlowDirection = FlowDirection.RightToLeft;
            btns.Width = 350;
            btns.BackColor = Theme.WindowBack;
            btns.Padding = new Padding(0, 6, 0, 0);

            Button bClose = MakeButton("閉じる", 84);
            bClose.Click += delegate { Hide(); };
            Button bDiag = MakeButton("診断ログを保存", 122);
            bDiag.Click += OnSaveDiagnostics;
            Button bNow = MakeButton("今すぐ更新", 100);
            bNow.Click += delegate { UpdateSnapshot(_refresh()); };

            btns.Controls.Add(bClose);
            btns.Controls.Add(bDiag);
            btns.Controls.Add(bNow);
            bottom.Controls.Add(btns);
            root.Controls.Add(bottom, 0, 5);

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;   // 常駐を維持する
                    Hide();
                }
            };
        }

        private Panel MakeCard()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Theme.PanelBack;
            p.Padding = new Padding(1);
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                Panel self = (Panel)s;
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, self.Width - 1, self.Height - 1);
            };
            return p;
        }

        private Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 30;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.BackColor = Theme.PanelBack;
            b.ForeColor = Theme.Text;
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }

        private Control BuildSettingsCard()
        {
            Panel card = MakeCard();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(14, 12, 14, 12);

            Label title = new Label();
            title.Text = "設定";
            title.ForeColor = Theme.Muted;
            title.AutoSize = true;
            title.Location = new Point(14, 10);
            card.Controls.Add(title);

            int y = 36;
            _nudPoll = AddNumeric(card, "更新間隔（秒）", 15, 900, _settings.PollSeconds, ref y);
            _nudWarn = AddNumeric(card, "通知する使用率（%）", 10, 100, _settings.WarnPercent, ref y);
            _nudWeekly = AddNumeric(card, "トレイを週次表示に切り替える使用率（%）", 10, 100,
                                    _settings.WeeklySwitchPercent, ref y);

            _chkWidget = new CheckBox();
            _chkWidget.Text = "タスクバーの上に数字を大きく表示する";
            _chkWidget.Checked = _settings.ShowTaskbarWidget;
            _chkWidget.AutoSize = true;
            _chkWidget.ForeColor = Theme.Text;
            _chkWidget.Location = new Point(16, y);
            _chkWidget.CheckedChanged += OnSettingChanged;
            card.Controls.Add(_chkWidget);

            Button reset = MakeButton("位置を戻す", 96);
            reset.Location = new Point(360, y - 4);
            reset.Click += delegate
            {
                if (_resetWidgetPosition != null) _resetWidgetPosition();
            };
            card.Controls.Add(reset);
            y += 30;

            _chkNotify = new CheckBox();
            _chkNotify.Text = "閾値超過とリセットをトースト通知する";
            _chkNotify.Checked = _settings.NotificationsEnabled;
            _chkNotify.AutoSize = true;
            _chkNotify.ForeColor = Theme.Text;
            _chkNotify.Location = new Point(16, y);
            _chkNotify.CheckedChanged += OnSettingChanged;
            card.Controls.Add(_chkNotify);
            y += 26;

            _chkStartup = new CheckBox();
            _chkStartup.Text = "Windows 起動時に自動で起動する";
            _chkStartup.Checked = Settings.GetStartWithWindows();
            _chkStartup.AutoSize = true;
            _chkStartup.ForeColor = Theme.Text;
            _chkStartup.Location = new Point(16, y);
            _chkStartup.CheckedChanged += OnStartupChanged;
            card.Controls.Add(_chkStartup);
            y += 26;

            Label note = new Label();
            note.Text = "※ タスクバー上の表示はドラッグで好きな位置に動かせます（位置は記憶します）。"
                      + "元データは Claude デスクトップアプリが約 5 分ごとに更新するため、"
                      + "更新間隔をこれより短くしても新しい値はその頻度でしか増えません。\n"
                      + Constants.Disclaimer;
            note.ForeColor = Theme.Muted;
            note.AutoSize = false;
            note.Location = new Point(16, y);
            note.Size = new Size(568, 62);
            card.Controls.Add(note);

            return card;
        }

        private NumericUpDown AddNumeric(Control parent, string label, int min, int max, int value, ref int y)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = Theme.Text;
            l.Location = new Point(16, y + 3);
            parent.Controls.Add(l);

            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Width = 76;
            n.Location = new Point(360, y);
            n.BackColor = Theme.PanelBack;
            n.ForeColor = Theme.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.ValueChanged += OnSettingChanged;
            parent.Controls.Add(n);

            y += 28;
            return n;
        }

        // ------------------------------------------------------------------
        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;
            _settings.PollSeconds = (int)_nudPoll.Value;
            _settings.WarnPercent = (int)_nudWarn.Value;
            _settings.WeeklySwitchPercent = (int)_nudWeekly.Value;
            _settings.NotificationsEnabled = _chkNotify.Checked;
            _settings.ShowTaskbarWidget = _chkWidget.Checked;
            _settings.Save();
            if (_settingsChanged != null) _settingsChanged();
            if (_snap != null) UpdateSnapshot(_snap);
        }

        private void OnStartupChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;
            bool want = _chkStartup.Checked;
            if (!Settings.SetStartWithWindows(want))
            {
                MessageBox.Show(this,
                    "自動起動の設定を変更できませんでした。",
                    Constants.AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _suppressEvents = true;
                _chkStartup.Checked = Settings.GetStartWithWindows();
                _suppressEvents = false;
            }
        }

        private void OnSaveDiagnostics(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "診断ログの保存先";
                    dlg.Filter = "テキストファイル (*.txt)|*.txt";
                    dlg.FileName = Constants.AppName + "-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                    dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    File.WriteAllText(dlg.FileName, DiagLog.Dump(_settings, _snap), System.Text.Encoding.UTF8);
                    MessageBox.Show(this, "診断ログを保存しました。\n" + dlg.FileName,
                        Constants.AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存に失敗しました。\n" + ex.Message,
                    Constants.AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------------
        public void UpdateSnapshot(UsageSnapshot snap)
        {
            _snap = snap;

            _suppressEvents = true;
            _chkStartup.Checked = Settings.GetStartWithWindows();
            _suppressEvents = false;

            _summary.SetData(snap, _settings.WarnPercent);
            _chart.SetData(snap == null ? null : snap.Samples);
            UpdateBanner(snap);
            UpdateOthers(snap);
            UpdateStatus(snap);
            UpdateWindowIcon(snap);
        }

        /// <summary>タイトルバーのアイコンもトレイと同じ数字にしておく。</summary>
        private void UpdateWindowIcon(UsageSnapshot snap)
        {
            try
            {
                string text;
                Color color;
                bool underline;
                IconState.Compute(snap, _settings, out text, out color, out underline);

                RenderedIcon fresh = TrayIconRenderer.Render(text, color, underline);
                RenderedIcon old = _winIcon;
                _winIcon = fresh;
                Icon = fresh.Icon;
                if (old != null) old.Dispose();
            }
            catch (Exception ex)
            {
                DiagLog.Write("ウィンドウアイコンの更新に失敗: {0}", ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _winIcon != null)
            {
                Icon = null;
                _winIcon.Dispose();
                _winIcon = null;
            }
            base.Dispose(disposing);
        }

        private void UpdateBanner(UsageSnapshot snap)
        {
            string msg = null;
            bool severe = false;

            if (snap == null)
            {
                msg = "使用量データをまだ読み取っていません。";
            }
            else if (!snap.Ok)
            {
                msg = snap.ErrorMessage;
                severe = true;
            }
            else if (snap.Stale)
            {
                msg = "Claude デスクトップアプリからの更新が止まっています。"
                    + "（最終更新 " + FormatAge(snap.AgeMinutes) + "／"
                    + (snap.LastSampleLocal.HasValue ? snap.LastSampleLocal.Value.ToString("M/d H:mm") : "?") + "）"
                    + "\nClaude デスクトップアプリが起動していないか、起動した直後です。"
                    + "起動したままにすれば数分以内に最新の値へ戻ります。";
                severe = true;
            }

            if (msg == null)
            {
                _banner.Visible = false;
                _banner.Height = 0;
            }
            else
            {
                _banner.Text = msg;
                _banner.ForeColor = severe ? Theme.LevelColor(UsageLevel.Warning) : Theme.Muted;
                _banner.Height = 48;
                _banner.Visible = true;
            }
        }

        private void UpdateOthers(UsageSnapshot snap)
        {
            _others.Controls.Clear();
            int shown = 0;

            if (snap != null && snap.Ok && snap.Latest != null)
            {
                List<string> keys = new List<string>();
                foreach (string k in Constants.GroupOrder)
                    if (k != Constants.KeySession && k != Constants.KeyWeekly && snap.Latest.ContainsKey(k))
                        keys.Add(k);
                foreach (string k in snap.Latest.Keys)
                    if (k != Constants.KeySession && k != Constants.KeyWeekly && !keys.Contains(k))
                        keys.Add(k);

                foreach (string k in keys)
                {
                    string label;
                    if (!Constants.GroupLabels.TryGetValue(k, out label)) label = k;
                    Label l = new Label();
                    l.AutoSize = true;
                    l.Margin = new Padding(0, 2, 22, 2);
                    l.ForeColor = Theme.Text;
                    l.Text = label + "  " + snap.Latest[k] + "%";
                    _others.Controls.Add(l);
                    shown++;
                }
            }

            if (shown == 0) _others.Controls.Add(_othersEmpty);
        }

        private void UpdateStatus(UsageSnapshot snap)
        {
            if (snap == null || !snap.Ok || !snap.LastSampleLocal.HasValue)
            {
                _status.Text = "最終更新: 取得できていません";
                _status.ForeColor = Theme.Muted;
                return;
            }
            _status.Text = "最終更新: " + FormatAge(snap.AgeMinutes)
                         + "（" + snap.LastSampleLocal.Value.ToString("M/d H:mm") + "）";
            _status.ForeColor = snap.Stale ? Theme.LevelColor(UsageLevel.Warning) : Theme.Muted;
        }

        public static string FormatAge(double minutes)
        {
            if (minutes < 1) return "1分未満前";
            if (minutes < 60) return (int)minutes + "分前";
            if (minutes < 60 * 24) return (int)(minutes / 60) + "時間前";
            return (int)(minutes / 1440) + "日前";
        }
    }
}
