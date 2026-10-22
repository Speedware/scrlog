using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class HomePage : UserControl
    {
        private readonly AppSettings _settings;
        private readonly Recorder _recorder;
        private readonly RecordingScheduler _scheduler;

        private readonly Card _statusCard = new Card();
        private readonly Card _machineCard = new Card { FillColor = Theme.Background };
        private readonly Card _scheduleCard = new Card();
        private readonly Card _storageCard = new Card();

        private readonly StatusDot _dot = new StatusDot();
        private readonly Label _statusText = Theme.MakeLabel("", Theme.Font(15f, FontStyle.Bold), Theme.Green);
        private readonly Label _elapsed = Theme.MakeLabel("00:00:00", Theme.Font(17f, FontStyle.Bold), Theme.Text);
        private readonly Label _fileText = Theme.MakeLabel("", Theme.Semibold(10f), Theme.Text);
        private readonly FlatCheckBox _scheduleEnabled = new FlatCheckBox("Автозапуск по расписанию");
        private readonly SpinBox _retention = new SpinBox { Minimum = 1, Maximum = 365 };

        private readonly KeyValueLabel _pcName = new KeyValueLabel("PC");
        private readonly KeyValueLabel _resolution = new KeyValueLabel("Разрешение");
        private readonly KeyValueLabel _quality = new KeyValueLabel("Качество");
        private readonly ActionButton _toggle = new ActionButton();

        private readonly SpinBox _start = SpinBox.ForTime();
        private readonly SpinBox _end = SpinBox.ForTime();

        private readonly InputBox _localFolder = new InputBox();
        private readonly InputBox _networkFolder = new InputBox();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly Timer _timer = new Timer { Interval = 500 };
        private bool _busy;

        public HomePage(AppSettings settings, Recorder recorder, RecordingScheduler scheduler)
        {
            _settings = settings;
            _recorder = recorder;
            _scheduler = scheduler;

            BackColor = Theme.Background;
            DoubleBuffered = true;

            BuildStatusCard();
            BuildScheduleCard();
            BuildStorageCard();
            Controls.AddRange(new Control[] { _statusCard, _scheduleCard, _storageCard });

            LoadValues();

            _recorder.StateChanged += OnStateChanged;
            _scheduler.NetworkStateChanged += OnStateChanged;
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();
            RefreshStatus();
        }

        private void BuildStatusCard()
        {
            var c = _statusCard;
            c.AddTitle("Статус");

            _dot.SetBounds(Theme.S(16), Theme.S(42), Theme.S(26), Theme.S(26));
            _statusText.Location = new Point(Theme.S(50), Theme.S(38));
            _elapsed.Location = new Point(Theme.S(50), Theme.S(66));
            _fileText.Location = new Point(Theme.S(52), Theme.S(100));
            _fileText.AutoSize = false;
            _fileText.AutoEllipsis = true;

            _scheduleEnabled.Location = new Point(Theme.S(16), Theme.S(140));
            _scheduleEnabled.CheckedChanged += (s, e) =>
            {
                _settings.ScheduleEnabled = _scheduleEnabled.Checked;
                _settings.Save();
                RefreshStatus();
            };

            var keep = Theme.MakeLabel("Хранить видео", Theme.Font(10.5f), Theme.Label);
            keep.Location = new Point(Theme.S(14), Theme.S(181));
            _retention.SetBounds(Theme.S(122), Theme.S(174), Theme.S(70), Theme.S(32));
            _retention.Formatter = v => v.ToString();
            _retention.ValueChanged += (s, e) =>
            {
                _settings.RetentionDays = _retention.ValueInt;
                _settings.Save();
            };
            var days = Theme.MakeLabel("дней", Theme.Font(10.5f), Theme.Label);
            days.Location = new Point(Theme.S(198), Theme.S(181));

            c.Controls.AddRange(new Control[] { _dot, _statusText, _elapsed, _fileText, _scheduleEnabled, keep, _retention, days, _machineCard });

            var m = _machineCard;
            var monitor = Theme.MakeLabel(Theme.Glyph.Monitor, Theme.Icon(36f), Theme.Text);
            monitor.Location = new Point(Theme.S(22), Theme.S(28));
            _pcName.SetBounds(Theme.S(106), Theme.S(36), Theme.S(170), Theme.S(22));
            _resolution.SetBounds(Theme.S(106), Theme.S(58), Theme.S(170), Theme.S(22));
            _quality.SetBounds(Theme.S(106), Theme.S(80), Theme.S(170), Theme.S(22));
            _toggle.Click += async (s, e) => await ToggleRecording();
            m.Controls.AddRange(new Control[] { monitor, _pcName, _resolution, _quality, _toggle });
        }

        private void BuildScheduleCard()
        {
            var c = _scheduleCard;
            c.AddTitle("Расписание");

            AddRow(c, "Начало записи", _start, Theme.S(50));
            AddRow(c, "Конец записи", _end, Theme.S(90));
            _start.SetBounds(Theme.S(118), Theme.S(50), Theme.S(74), Theme.S(32));
            _end.SetBounds(Theme.S(118), Theme.S(90), Theme.S(74), Theme.S(32));

            _start.ValueChanged += (s, e) => SaveSchedule();
            _end.ValueChanged += (s, e) => SaveSchedule();
        }

        private void BuildStorageCard()
        {
            var c = _storageCard;
            c.AddTitle("Сохранение видео");

            AddRow(c, "Локальная папка", _localFolder, Theme.S(50));
            AddRow(c, "Сетевая папка", _networkFolder, Theme.S(90));

            _localFolder.Committed += (s, e) =>
            {
                var path = _localFolder.Value.Trim();
                if (path.Length == 0)
                {
                    _localFolder.Value = _settings.LocalFolder;
                    return;
                }
                if (path == _settings.LocalFolder) return;
                _settings.LocalFolder = path;
                _settings.Save();
            };
            _networkFolder.Committed += (s, e) =>
            {
                var path = _networkFolder.Value.Trim();
                if (path == _settings.NetworkFolder) return;
                _settings.NetworkFolder = path;
                _settings.Save();
                Task.Run(() => _scheduler.RunMaintenance());
            };

            var browseLocal = new IconButton(Theme.Glyph.Folder) { Tag = _localFolder };
            var browseNetwork = new IconButton(Theme.Glyph.Folder) { Tag = _networkFolder };
            browseLocal.Click += Browse;
            browseNetwork.Click += Browse;
            c.Controls.Add(browseLocal);
            c.Controls.Add(browseNetwork);

            c.Resize += (s, e) =>
            {
                int btn = Theme.S(32);
                int x = Theme.S(136);
                int right = c.Width - Theme.S(16);
                browseLocal.SetBounds(right - btn, Theme.S(50), btn, btn);
                browseNetwork.SetBounds(right - btn, Theme.S(90), btn, btn);
                _localFolder.SetBounds(x, Theme.S(50), right - btn - Theme.S(8) - x, btn);
                _networkFolder.SetBounds(x, Theme.S(90), right - btn - Theme.S(8) - x, btn);
            };
        }

        private static void AddRow(Card card, string label, Control input, int y)
        {
            var l = Theme.MakeLabel(label, Theme.Font(10.5f), Theme.Label);
            l.Location = new Point(Theme.S(14), y + Theme.S(7));
            card.Controls.Add(l);
            card.Controls.Add(input);
        }

        private void Browse(object sender, EventArgs e)
        {
            var box = (InputBox)((Control)sender).Tag;
            using (var dialog = new FolderBrowserDialog { SelectedPath = box.Value, ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                box.Value = dialog.SelectedPath;
                box.Commit();
            }
        }

        private void SaveSchedule()
        {
            _settings.ScheduleStart = AppSettings.FormatTime(TimeSpan.FromMinutes(_start.ValueInt));
            _settings.ScheduleEnd = AppSettings.FormatTime(TimeSpan.FromMinutes(_end.ValueInt));
            _settings.Save();
            RefreshStatus();
        }

        public void LoadValues()
        {
            _scheduleEnabled.Checked = _settings.ScheduleEnabled;
            _retention.ValueInt = _settings.RetentionDays;
            _start.ValueInt = (int)_settings.StartTime.TotalMinutes;
            _end.ValueInt = (int)_settings.EndTime.TotalMinutes;
            _localFolder.Value = _settings.LocalFolder;
            _networkFolder.Value = _settings.NetworkFolder;
            RefreshStatus();
        }

        private async Task ToggleRecording()
        {
            if (_busy) return;
            _busy = true;
            _toggle.Enabled = false;
            try
            {
                if (_recorder.IsRecording)
                    await Task.Run(() => _scheduler.StopManually());
                else
                    await Task.Run(() => _scheduler.StartManually());

                if (_recorder.LastError != null)
                    MessageBox.Show(FindForm(), _recorder.LastError, "Scrlog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
                _toggle.Enabled = true;
                RefreshStatus();
            }
        }

        private void OnStateChanged()
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)RefreshStatus);
        }

        public void RefreshStatus()
        {
            if (_recorder.IsRecording)
            {
                _dot.DotColor = Theme.Green;
                _statusText.ForeColor = Theme.Green;
                _statusText.Text = "Идёт запись";
                var elapsed = _recorder.Elapsed;
                _elapsed.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                var file = _recorder.CurrentFile;
                _fileText.Text = file != null ? $"Файл: {file}" : "Файл: создаётся…";
            }
            else
            {
                bool error = _recorder.LastError != null;
                _dot.DotColor = error ? Theme.Red : Theme.Gray;
                _statusText.ForeColor = error ? Theme.Red : Theme.Muted;
                _statusText.Text = error ? "Ошибка записи" : "Запись остановлена";
                _elapsed.Text = "00:00:00";
                _fileText.Text = error ? _recorder.LastError : NextRunText();
            }

            if (!_busy)
            {
                _toggle.Text = _recorder.IsRecording ? "Остановить запись" : "Начать запись";
                _toggle.Glyph = _recorder.IsRecording ? Theme.Glyph.Stop : Theme.Glyph.Record;
                _toggle.FillColor = _recorder.IsRecording ? Theme.Red : Theme.Green;
            }

            _pcName.Value = _settings.PcName;
            var screen = Recorder.ScreenSize;
            _resolution.Value = $"{screen.Width} x {screen.Height}";
            _quality.Value = SettingsPage.QualityName(_settings.Quality);

            var networkDown = _scheduler.NetworkAvailable == false;
            _networkFolder.BorderColor = networkDown ? Theme.Red : Theme.InputBorder;
            _toolTip.SetToolTip(_networkFolder, networkDown ? "Сетевая папка недоступна — файлы будут скопированы позже" : null);
        }

        private string NextRunText()
        {
            if (!_settings.ScheduleEnabled)
                return "Автозапуск по расписанию выключен";
            if (_settings.IsScheduledNow())
                return $"Остановлено вручную до {AppSettings.FormatTime(_settings.EndTime)}";
            var now = DateTime.Now.TimeOfDay;
            var day = now < _settings.StartTime ? "сегодня" : "завтра";
            return $"Следующий запуск: {day} в {AppSettings.FormatTime(_settings.StartTime)}";
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(24);
            int gap = Theme.S(12);
            int width = ClientSize.Width - pad * 2;

            _statusCard.SetBounds(pad, Theme.S(22), width, Theme.S(222));
            int machineWidth = Theme.S(290);
            _machineCard.SetBounds(_statusCard.Width - machineWidth - Theme.S(14), Theme.S(14), machineWidth, _statusCard.Height - Theme.S(28));
            _toggle.SetBounds(Theme.S(20), _machineCard.Height - Theme.S(62), machineWidth - Theme.S(40), Theme.S(44));
            _fileText.SetBounds(_fileText.Left, _fileText.Top, _machineCard.Left - _fileText.Left - Theme.S(12), Theme.S(22));

            int top = _statusCard.Bottom + gap;
            int scheduleWidth = Theme.S(202);
            _scheduleCard.SetBounds(pad, top, scheduleWidth, Theme.S(140));
            _storageCard.SetBounds(pad + scheduleWidth + gap, top, width - scheduleWidth - gap, Theme.S(140));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _toolTip.Dispose();
                _recorder.StateChanged -= OnStateChanged;
                _scheduler.NetworkStateChanged -= OnStateChanged;
            }
            base.Dispose(disposing);
        }
    }
}
