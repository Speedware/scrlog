using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class SettingsPage : Page
    {
        private const string AutostartHintText = "Включено установщиком для всех пользователей";
        private const string FfmpegMissingText = "ffmpeg: не найден — запись невозможна";
        private static readonly int RowStep = Theme.S(42);

        private readonly AppSettings _settings;

        private readonly Card _recordCard = new Card();
        private readonly Card _appCard = new Card();

        private readonly InputBox _pcName = new InputBox();
        private readonly ComboBox _quality = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Font(10.5f),
            BackColor = Theme.InputFill
        };
        private readonly Label _qualityHint = Theme.MakeLabel("", Theme.Font(9f), Theme.Muted);
        private readonly SpinBox _fps = new SpinBox { Minimum = 1, Maximum = 30 };
        private readonly SpinBox _segment = new SpinBox { Minimum = 1, Maximum = 240, Increment = 5 };
        private Label _pcNameLabel;
        private Label _qualityLabel;
        private Label _fpsLabel;
        private Label _segmentLabel;
        private readonly FlatCheckBox _clicks = new FlatCheckBox("Подсвечивать клики мыши");
        private readonly Label _applyHint = Theme.MakeLabel("Качество, частота кадров и длина файла применяются при следующем запуске записи.",
            Theme.Font(9f), Theme.Muted);

        private readonly FlatCheckBox _tray = new FlatCheckBox("Сворачивать в трей при закрытии окна");
        private readonly FlatCheckBox _autostart = new FlatCheckBox("Запускать при входе в Windows");
        private readonly Label _autostartHint = Theme.MakeLabel("", Theme.Font(9f), Theme.Muted);
        private readonly Label _ffmpeg = Theme.MakeLabel("", Theme.Font(10f), Theme.Label);
        private readonly ActionButton _openLog = new ActionButton { Text = "Журнал работы", FillColor = Theme.Card, TextColor = Theme.Text, Font = Theme.Semibold(10f) };
        private readonly ActionButton _openData = new ActionButton { Text = "Папка данных", FillColor = Theme.Card, TextColor = Theme.Text, Font = Theme.Semibold(10f) };

        public event Action SettingsChanged;

        public SettingsPage(AppSettings settings)
        {
            _settings = settings;
            // Если окно ниже содержимого, страница прокручивается, а не обрезается
            AutoScroll = true;

            BuildRecordCard();
            BuildAppCard();
            Controls.AddRange(new Control[] { _recordCard, _appCard });
            LoadValues();
            FinishInit();
        }

        public static string QualityName(VideoQuality quality)
        {
            switch (quality)
            {
                case VideoQuality.Low: return "Низкое";
                case VideoQuality.High: return "Высокое";
                default: return "Стандартное";
            }
        }

        private void BuildRecordCard()
        {
            var c = _recordCard;
            c.AddTitle("Запись");

            _pcNameLabel = AddRow(c, "Имя ПК", _pcName);
            _qualityLabel = AddRow(c, "Качество", _quality);
            _fpsLabel = AddRow(c, "Кадров в секунду", _fps);
            _segmentLabel = AddRow(c, "Длина файла, мин", _segment);

            _clicks.Location = new Point(Theme.S(16), Theme.S(50) + RowStep * 4);
            _applyHint.Location = new Point(Theme.S(16), _clicks.Bottom + Theme.S(10));
            c.Controls.AddRange(new Control[] { _qualityHint, _clicks, _applyHint });

            foreach (VideoQuality q in Enum.GetValues(typeof(VideoQuality)))
                _quality.Items.Add(QualityName(q));

            _pcName.Committed += (s, e) =>
            {
                var name = _pcName.Value.Trim();
                _settings.PcName = name.Length > 0 ? name : Environment.MachineName;
                _pcName.Value = _settings.PcName;
                Save();
            };
            _quality.SelectedIndexChanged += (s, e) =>
            {
                _settings.Quality = (VideoQuality)_quality.SelectedIndex;
                UpdateQualityHint();
                Save();
            };
            _fps.ValueChanged += (s, e) => { _settings.Fps = _fps.ValueInt; Save(); };
            _segment.ValueChanged += (s, e) => { _settings.SegmentMinutes = _segment.ValueInt; Save(); };
            _clicks.CheckedChanged += (s, e) => { _settings.HighlightClicks = _clicks.Checked; Save(); };
        }

        private void BuildAppCard()
        {
            var c = _appCard;
            c.AddTitle("Программа");

            _tray.Location = new Point(Theme.S(16), Theme.S(50));
            _autostart.Location = new Point(Theme.S(16), Theme.S(80));
            _autostartHint.Location = new Point(Theme.S(44), Theme.S(104));
            _ffmpeg.Location = new Point(Theme.S(16), Theme.S(132));
            c.Controls.AddRange(new Control[] { _tray, _autostart, _autostartHint, _ffmpeg, _openLog, _openData });

            _tray.CheckedChanged += (s, e) => { _settings.MinimizeToTray = _tray.Checked; Save(); };
            _autostart.CheckedChanged += (s, e) =>
            {
                if (_autostart.Checked != Autostart.EnabledForCurrentUser)
                    Autostart.SetForCurrentUser(_autostart.Checked);
            };
            _openLog.Click += (s, e) =>
            {
                var log = Path.Combine(AppSettings.DataFolder, "scrlog.log");
                if (File.Exists(log))
                    Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
                else
                    MessageBox.Show(FindForm(), "Журнал работы пока пуст.", "Scrlog");
            };
            _openData.Click += (s, e) => JournalPage.OpenFolder(AppSettings.DataFolder);
        }

        private void LoadValues()
        {
            _pcName.Value = _settings.PcName;
            _quality.SelectedIndex = (int)_settings.Quality;
            _fps.ValueInt = _settings.Fps;
            _segment.ValueInt = _settings.SegmentMinutes;
            _clicks.Checked = _settings.HighlightClicks;
            _tray.Checked = _settings.MinimizeToTray;
            UpdateQualityHint();
        }

        private void UpdateQualityHint()
        {
            var size = Recorder.GetOutputSize(_settings.Quality);
            _qualityHint.Text = $"видео {size.Width} x {size.Height}";
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) return;

            bool forAll = Autostart.EnabledForAllUsers;
            _autostart.Checked = forAll || Autostart.EnabledForCurrentUser;
            _autostart.Enabled = !forAll;
            _autostartHint.Text = forAll ? AutostartHintText : "";

            bool hasFfmpeg = File.Exists(Recorder.FfmpegPath);
            _ffmpeg.Text = hasFfmpeg ? "ffmpeg: найден" : FfmpegMissingText;
            _ffmpeg.ForeColor = hasFfmpeg ? Theme.Label : Theme.Red;
            UpdateQualityHint();
        }

        private void Save()
        {
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        // ---------- Раскладка ----------

        private static readonly int ComboWidth = Theme.S(170);

        private int InputX => InputColumn(_pcNameLabel, _qualityLabel, _fpsLabel, _segmentLabel);

        private int QualityHintWidth => Enum.GetValues(typeof(VideoQuality)).Cast<VideoQuality>()
            .Max(q => { var size = Recorder.GetOutputSize(q); return TextWidth($"видео {size.Width} x {size.Height}", _qualityHint.Font); });

        public override int MinimumContentWidth => Pad * 2 + new[]
        {
            InputX + ComboWidth + Theme.S(12) + QualityHintWidth + Theme.S(16),
            _clicks.Right + Theme.S(16),
            _tray.Right + Theme.S(16),
            _autostart.Right + Theme.S(16),
            _autostartHint.Left + TextWidth(AutostartHintText, _autostartHint.Font) + Theme.S(16),
            _ffmpeg.Left + TextWidth(FfmpegMissingText, _ffmpeg.Font) + Theme.S(16),
            Theme.S(16) + ButtonWidth(_openLog) + Theme.S(8) + ButtonWidth(_openData) + Theme.S(16)
        }.Max();

        // Страница прокручивается, поэтому высоту окна не ограничивает
        public override int MinimumContentHeight => 0;

        protected override void LayoutContent()
        {
            int width = ClientSize.Width - Pad * 2;
            int offset = AutoScrollPosition.Y;

            // Запись
            int x = InputX;
            int y = Theme.S(50);
            PlaceRow(_pcNameLabel, _pcName, x, y, Math.Max(Theme.S(140), Math.Min(Theme.S(320), width - x - Theme.S(16))));
            PlaceRow(_qualityLabel, _quality, x, y += RowStep, ComboWidth);
            _qualityHint.Location = new Point(_quality.Right + Theme.S(12), _qualityLabel.Top + Theme.S(2));
            PlaceRow(_fpsLabel, _fps, x, y += RowStep, Theme.S(76));
            PlaceRow(_segmentLabel, _segment, x, y += RowStep, Theme.S(76));
            // Длинная подсказка переносится по ширине карточки
            _applyHint.MaximumSize = new Size(Math.Max(Theme.S(100), width - Theme.S(32)), 0);
            _recordCard.SetBounds(Pad, ContentTop + offset, width, _applyHint.Bottom + Theme.S(16));

            // Программа
            int buttonsTop = _ffmpeg.Bottom + Theme.S(14);
            _openLog.SetBounds(Theme.S(16), buttonsTop, ButtonWidth(_openLog), RowHeight);
            _openData.SetBounds(_openLog.Right + Theme.S(8), buttonsTop, ButtonWidth(_openData), RowHeight);
            _appCard.SetBounds(Pad, _recordCard.Bottom + Gap, width, _openLog.Bottom + Theme.S(16));

            var scrollSize = new Size(0, _appCard.Bottom - offset + Pad);
            if (AutoScrollMinSize != scrollSize)
                AutoScrollMinSize = scrollSize;
        }
    }
}
