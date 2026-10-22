using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Recorder _recorder;
        private readonly RecordingScheduler _scheduler;
        private readonly bool _startHidden;

        private readonly Panel _sidebar = new Panel { Dock = DockStyle.Left, BackColor = Theme.Sidebar };
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly NavButton[] _nav;
        private readonly Page[] _pages;
        private readonly HomePage _home;

        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ToolStripMenuItem _trayToggle = new ToolStripMenuItem();
        private readonly Icon _appIcon;
        private readonly Icon _idleIcon;
        private MouseHook _mouseHook;
        private bool _exiting;
        private bool _trayHintShown;

        public MainForm(AppSettings settings, Recorder recorder, RecordingScheduler scheduler, bool startHidden)
        {
            _settings = settings;
            _recorder = recorder;
            _scheduler = scheduler;
            _startHidden = startHidden;

            Text = "Scrlog";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Font(9f);
            BackColor = Theme.Background;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(860), Theme.S(540));

            _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            _idleIcon = MakeIdleIcon(_appIcon);
            Icon = _appIcon;

            _home = new HomePage(settings, recorder, scheduler);
            var journal = new JournalPage(settings);
            var settingsPage = new SettingsPage(settings);
            settingsPage.SettingsChanged += () =>
            {
                _home.RefreshStatus();
                ApplyMinimumSize();
            };
            _pages = new Page[] { _home, journal, settingsPage };
            foreach (var page in _pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _content.Controls.Add(page);
            }

            _nav = new[]
            {
                new NavButton(Theme.Glyph.Home, "Главный экран"),
                new NavButton(Theme.Glyph.List, "Журнал записей"),
                new NavButton(Theme.Glyph.Settings, "Настройки")
            };
            _sidebar.Width = Theme.S(194);
            _sidebar.Padding = new Padding(0, Theme.S(22), 0, 0);
            // Dock=Top складывает снизу вверх, поэтому добавляем в обратном порядке
            for (int i = _nav.Length - 1; i >= 0; i--)
            {
                int index = i;
                _nav[i].Dock = DockStyle.Top;
                _nav[i].Click += (s, e) => ShowPage(index);
                _sidebar.Controls.Add(_nav[i]);
            }

            var version = Theme.MakeLabel("V" + Application.ProductVersion.Split('+')[0], Theme.Font(9f), Theme.SidebarMuted);
            version.Dock = DockStyle.Bottom;
            version.AutoSize = false;
            version.Height = Theme.S(28);
            version.Padding = new Padding(Theme.S(4), 0, 0, 0);
            version.TextAlign = ContentAlignment.MiddleLeft;
            _sidebar.Controls.Add(version);

            Controls.Add(_content);
            Controls.Add(_sidebar);
            ShowPage(0);
            ApplyMinimumSize();

            BuildTray();
            _recorder.StateChanged += OnRecorderStateChanged;
        }

        /// <summary>Не даём сжать окно так, чтобы элементы страниц начали перекрываться.</summary>
        private void ApplyMinimumSize()
        {
            var min = new Size(
                _sidebar.Width + _pages.Max(p => p.MinimumContentWidth),
                Math.Max(Theme.S(460), _pages.Max(p => p.MinimumContentHeight)));
            MinimumSize = SizeFromClientSize(min);
            if (ClientSize.Width < min.Width || ClientSize.Height < min.Height)
                ClientSize = new Size(Math.Max(ClientSize.Width, min.Width), Math.Max(ClientSize.Height, min.Height));
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _nav[i].Selected = i == index;
                _pages[i].Visible = i == index;
            }
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Открыть Scrlog", null, (s, e) => RestoreWindow()).Font = Theme.Font(9f, FontStyle.Bold);
            menu.Items.Add(_trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => ExitApplication());
            _trayToggle.Click += async (s, e) =>
            {
                if (_recorder.IsRecording)
                    await Task.Run(() => _scheduler.StopManually());
                else
                    await Task.Run(() => _scheduler.StartManually());
            };

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => RestoreWindow();
            _tray.Visible = true;
            UpdateTray();
        }

        private void UpdateTray()
        {
            _tray.Icon = _recorder.IsRecording ? _appIcon : _idleIcon;
            _tray.Text = _recorder.IsRecording ? "Scrlog — идёт запись" : "Scrlog — запись остановлена";
            _trayToggle.Text = _recorder.IsRecording ? "Остановить запись" : "Начать запись";
        }

        private void OnRecorderStateChanged()
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)UpdateTray);
        }

        public void RestoreWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            _exiting = true;
            Close();
        }

        protected override void SetVisibleCore(bool value)
        {
            // Запуск с --minimized: окно создаётся, но остаётся скрытым (работаем из трея)
            if (_startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Цветной заголовок окна (Windows 11; на Windows 10 атрибут игнорируется)
            int caption = ColorToRef(Theme.TitleBar);
            int text = ColorToRef(Color.White);
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));

            _mouseHook = new MouseHook();
            _mouseHook.OnMouseDown += _recorder.RegisterClick;
            _mouseHook.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_exiting && _settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _tray.ShowBalloonTip(3000, "Scrlog работает в фоне",
                        "Запись продолжается. Чтобы открыть окно, дважды щёлкните по значку.", ToolTipIcon.Info);
                }
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing && _recorder.IsRecording &&
                MessageBox.Show(this, "Идёт запись. Остановить запись и выйти?", "Scrlog",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                _exiting = false;
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _recorder.StateChanged -= OnRecorderStateChanged;
            _mouseHook?.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowWindowMessage)
            {
                RestoreWindow();
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Серая версия иконки для трея, пока запись остановлена.</summary>
        private static Icon MakeIdleIcon(Icon source)
        {
            int size = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var color = new Icon(source, size, size).ToBitmap())
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix(new[]
                    {
                        new[] { 0.3f, 0.3f, 0.3f, 0, 0 },
                        new[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                        new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                        new[] { 0f, 0, 0, 1, 0 },
                        new[] { 0f, 0, 0, 0, 1 }
                    }));
                    g.DrawImage(color, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attributes);
                }
                // Icon.FromHandle не владеет хэндлом — клонируем, чтобы не держать GDI-ресурс
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(hIcon))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        private static int ColorToRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
