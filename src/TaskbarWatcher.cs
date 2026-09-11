using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    /// <summary>
    /// Windows は通知領域を作り直したとき、すべてのトップレベルウィンドウに
    /// "TaskbarCreated" をブロードキャストする。これを受けるためだけの不可視ウィンドウ。
    ///
    /// ログオン直後は explorer より先にこのアプリが起動することがあり、その場合
    /// トレイアイコンの登録は失敗する。explorer が立ち上がった通知を受けて登録し直す。
    /// explorer がクラッシュして再起動した場合にも同じ経路で復帰できる。
    /// </summary>
    internal sealed class TaskbarWatcher : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int RegisterWindowMessage(string message);

        private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        private readonly Action _onTaskbarCreated;

        public TaskbarWatcher(Action onTaskbarCreated)
        {
            _onTaskbarCreated = onTaskbarCreated;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);

            // 表示はしないが、ブロードキャストを受け取るにはハンドルが要る
            CreateHandle();
        }

        /// <summary>誤って表示されないようにしておく。</summary>
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0 && _onTaskbarCreated != null)
            {
                try
                {
                    _onTaskbarCreated();
                }
                catch (Exception ex)
                {
                    DiagLog.WriteToFile("TaskbarCreated の処理で例外: " + ex.Message);
                }
            }
            base.WndProc(ref m);
        }
    }
}
