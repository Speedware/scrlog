using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class HomePage : Page
    {
        private static readonly string[] StatusTexts = { "Идёт запись", "Запись остановлена", "Ошибка записи" };
        private static readonly int StatusHeight = Theme.S(222);
        private static readonly int BottomHeight = Theme.S(140);
        private static readonly int MachineTextX = Theme.S(100);
        private static readonly int SpinWidth = Theme.S(76);

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
        private readonly Label _keepLabel = Theme.MakeLabel("Хранить видео", Theme.Font(10.5f), Theme.Label);
        private readonly SpinBox _retention = new SpinBox { Minimum = 1, Maximum = 365 };
        private readonly Label _daysLabel = Theme.MakeLabel("дней", Theme.Font(10.5f), Theme.Label);

        private readonly Label _monitor = Theme.MakeLabel(Theme.Glyph.Monitor, Theme.Icon(36f), Theme.Text);
        private readonly KeyValueLabel _pcName = new KeyValueLabel("PC");
        private readonly KeyValueLabel _resolution = new KeyValueLabel("Разрешение");
        private readonly KeyValueLabel _quality = new KeyValueLabel("Качество");
        private readonly ActionButton _toggle = new ActionButton();

        private readonly SpinBox _start = SpinBox.ForTime();
        private readonly SpinBox _end = SpinBox.ForTime();
        private Label _startLabel;
        private Label _endLabel;

        private readonly InputBox _localFolder = new InputBox();
        private readonly InputBox _networkFolder = new InputBox();
        private readonly IconButton _browseLocal = new IconButton(Theme.Glyph.Folder);
        private readonly IconButton _browseNetwork = new IconButton(Theme.Glyph.Folder);
        private Label _localLabel;
        private Label _networkLabel;

        private readonly ToolTip _toolTip = new ToolTip();
        private readonly Timer _timer = new Timer { Interval = 500 };
        private bool _busy;

        public HomePage(AppSettings settings, Recorder recorder, RecordingScheduler scheduler)
        {
            _settings = settings;
            _recorder = recorder;
            _scheduler = scheduler;

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
            _fileText.AutoSize = false;
            _fileText.AutoEllipsis = true;
            _scheduleEnabled.Location = new Point(Theme.S(16), Theme.S(140));
            _keepLabel.Left = Theme.S(14);

            _scheduleEnabled.CheckedChanged += (s, e) =>
            {
                _settings.ScheduleEnabled = _scheduleEnabled.Checked;
                _settings.Save();
                RefreshStatus();
            };
            _retention.ValueChanged += (s, e) =>
            {
                _settings.RetentionDays = _retention.ValueInt;
                _settings.Save();
            };

            c.Controls.AddRange(new Control[] { _dot, _statusText, _elapsed, _fileText, _scheduleEnabled, _keepLabel, _retention, _daysLabel, _machineCard });

            _monitor.Location = new Point(Theme.S(20), Theme.S(28));
            _toggle.Click += async (s, e) => await ToggleRecording();
            _machineCard.Controls.AddRange(new Control[] { _monitor, _pcName, _resolution, _quality, _toggle });
        }

        private void BuildScheduleCard()
        {
            var c = _scheduleCard;
            c.AddTitle("Расписание");
            _startLabel = AddRow(c, "Начало записи", _start);
            _endLabel = AddRow(c, "Конец записи", _end);
            _start.ValueChanged += (s, e) => SaveSchedule();
            _end.ValueChanged += (s, e) => SaveSchedule();
        }

        private void BuildStorageCard()
        {
            var c = _storageCard;
            c.AddTitle("Сохранение видео");
            _localLabel = AddRow(c, "Локальная папка", _localFolder);
            _networkLabel = AddRow(c, "Сетевая папка", _networkFolder);

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

            _browseLocal.Tag = _localFolder;
            _browseNetwork.Tag = _networkFolder;
            _browseLocal.Click += Browse;
            _browseNetwork.Click += Browse;
            c.Controls.Add(_browseLocal);
            c.Controls.Add(_browseNetwork);
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
                _statusText.Text = StatusTexts[0];
                var elapsed = _recorder.Elapsed;
                _elapsed.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                var file = _recorder.CurrentFile;
                SetFileText(file != null ? $"Файл: {file}" : "Файл: создаётся…");
            }
            else
            {
                bool error = _recorder.LastError != null;
                _dot.DotColor = error ? Theme.Red : Theme.Gray;
                _statusText.ForeColor = error ? Theme.Red : Theme.Muted;
                _statusText.Text = error ? StatusTexts[2] : StatusTexts[1];
                _elapsed.Text = "00:00:00";
                SetFileText(error ? _recorder.LastError : NextRunText());
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

        private void SetFileText(string text)
        {
            if (_fileText.Text == text) return;
            _fileText.Text = text;
            // Строка может обрезаться многоточием в узком окне — полный текст в подсказке
            _toolTip.SetToolTip(_fileText, text);
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

        // ---------- Раскладка ----------

        private int RetentionX => _keepLabel.Left + TextWidth(_keepLabel) + Theme.S(8);

        private int DaysX => RetentionX + Theme.S(70) + Theme.S(8);

        /// <summary>Ширина левой части карточки «Статус» (до блока с ПК).</summary>
        private int StatusLeftWidth => new[]
        {
            _statusText.Left + StatusTexts.Max(t => TextWidth(t, _statusText.Font)),
            _elapsed.Left + TextWidth("000:00:00", _elapsed.Font),
            _scheduleEnabled.Right,
            DaysX + TextWidth(_daysLabel),
            Theme.S(52) + Theme.S(180) // строка «Файл: …» дальше ужимается с многоточием
        }.Max() + Theme.S(16);

        private int ToggleWidth =>
            TextWidth("Остановить запись", _toggle.Font) + TextWidth(Theme.Glyph.Stop, Theme.Icon(_toggle.GlyphSize)) + Theme.S(14 + 36);

        private int MachineMinWidth => Math.Max(
            MachineTextX + Math.Max(_resolution.TextWidth, _quality.TextWidth) + Theme.S(14),
            ToggleWidth + Theme.S(40));

        private int MachinePreferredWidth => Math.Max(MachineMinWidth,
            Math.Min(Theme.S(360), Math.Max(Theme.S(290), MachineTextX + _pcName.TextWidth + Theme.S(14))));

        private int ScheduleInputX => InputColumn(_startLabel, _endLabel);

        private int ScheduleWidth => Math.Max(ScheduleInputX + SpinWidth, Theme.S(16) + TextWidth("Расписание", Theme.Semibold(11f))) + Theme.S(16);

        private int StorageInputX => InputColumn(_localLabel, _networkLabel);

        private int StorageMinWidth => StorageInputX + Theme.S(140) + Theme.S(8) + RowHeight + Theme.S(16);

        public override int MinimumContentWidth => Pad * 2 + Math.Max(
            StatusLeftWidth + MachineMinWidth + Theme.S(14),
            ScheduleWidth + Gap + StorageMinWidth);

        public override int MinimumContentHeight => ContentTop + StatusHeight + Gap + BottomHeight + Pad;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int width = ClientSize.Width - Pad * 2;

            // Статус: блок с ПК справа сжимается до минимума, левая часть не перекрывается
            _statusCard.SetBounds(Pad, ContentTop, width, StatusHeight);
            int machineWidth = Math.Max(MachineMinWidth, Math.Min(MachinePreferredWidth, width - StatusLeftWidth - Theme.S(14)));
            _machineCard.SetBounds(width - machineWidth - Theme.S(14), Theme.S(14), machineWidth, StatusHeight - Theme.S(28));
            int kvWidth = machineWidth - MachineTextX - Theme.S(12);
            _pcName.SetBounds(MachineTextX, Theme.S(36), kvWidth, Theme.S(22));
            _resolution.SetBounds(MachineTextX, Theme.S(58), kvWidth, Theme.S(22));
            _quality.SetBounds(MachineTextX, Theme.S(80), kvWidth, Theme.S(22));
            _toggle.SetBounds(Theme.S(20), _machineCard.Height - Theme.S(62), machineWidth - Theme.S(40), Theme.S(44));

            _fileText.SetBounds(Theme.S(52), Theme.S(100), Math.Max(0, _machineCard.Left - Theme.S(52) - Theme.S(12)), Theme.S(22));
            PlaceRow(_keepLabel, _retention, RetentionX, Theme.S(174), Theme.S(70));
            _daysLabel.Location = new Point(DaysX, _keepLabel.Top);

            // Нижний ряд: расписание по содержимому, папки забирают остальное
            int top = _statusCard.Bottom + Gap;
            int scheduleWidth = ScheduleWidth;
            _scheduleCard.SetBounds(Pad, top, scheduleWidth, BottomHeight);
            PlaceRow(_startLabel, _start, ScheduleInputX, Theme.S(50), SpinWidth);
            PlaceRow(_endLabel, _end, ScheduleInputX, Theme.S(90), SpinWidth);

            _storageCard.SetBounds(Pad + scheduleWidth + Gap, top, width - scheduleWidth - Gap, BottomHeight);
            int x = StorageInputX;
            int right = _storageCard.Width - Theme.S(16);
            int pathWidth = right - RowHeight - Theme.S(8) - x;
            PlaceRow(_localLabel, _localFolder, x, Theme.S(50), pathWidth);
            PlaceRow(_networkLabel, _networkFolder, x, Theme.S(90), pathWidth);
            _browseLocal.SetBounds(right - RowHeight, Theme.S(50), RowHeight, RowHeight);
            _browseNetwork.SetBounds(right - RowHeight, Theme.S(90), RowHeight, RowHeight);
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
