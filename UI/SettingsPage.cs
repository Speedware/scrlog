using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class SettingsPage : UserControl
    {
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
            BackColor = Theme.Background;

            BuildRecordCard();
            BuildAppCard();
            Controls.AddRange(new Control[] { _recordCard, _appCard });
            LoadValues();
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

            AddRow(c, "Имя ПК", _pcName, 50);
            AddRow(c, "Качество", _quality, 92);
            AddRow(c, "Кадров в секунду", _fps, 134);
            AddRow(c, "Длина файла, мин", _segment, 176);

            _qualityHint.Location = new Point(Theme.S(340), Theme.S(99));
            _clicks.Location = new Point(Theme.S(16), Theme.S(220));
            _applyHint.Location = new Point(Theme.S(16), Theme.S(252));
            c.Controls.AddRange(new Control[] { _qualityHint, _clicks, _applyHint });

            _pcName.SetBounds(Theme.S(150), Theme.S(50), Theme.S(260), Theme.S(32));
            _quality.SetBounds(Theme.S(150), Theme.S(94), Theme.S(180), Theme.S(32));
            _fps.SetBounds(Theme.S(150), Theme.S(134), Theme.S(74), Theme.S(32));
            _segment.SetBounds(Theme.S(150), Theme.S(176), Theme.S(74), Theme.S(32));

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
            _ffmpeg.Location = new Point(Theme.S(16), Theme.S(134));
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

        private static void AddRow(Card card, string label, Control input, int y)
        {
            var l = Theme.MakeLabel(label, Theme.Font(10.5f), Theme.Label);
            l.Location = new Point(Theme.S(14), Theme.S(y + 7));
            card.Controls.Add(l);
            card.Controls.Add(input);
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
            _autostartHint.Text = forAll ? "Включено установщиком для всех пользователей" : "";

            bool hasFfmpeg = File.Exists(Recorder.FfmpegPath);
            _ffmpeg.Text = hasFfmpeg ? "ffmpeg: найден" : "ffmpeg: не найден — запись невозможна";
            _ffmpeg.ForeColor = hasFfmpeg ? Theme.Label : Theme.Red;
            UpdateQualityHint();
        }

        private void Save()
        {
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(24);
            int width = ClientSize.Width - pad * 2;
            _recordCard.SetBounds(pad, Theme.S(22), width, Theme.S(280));
            _appCard.SetBounds(pad, _recordCard.Bottom + Theme.S(12), width, Theme.S(176));
            _openLog.SetBounds(Theme.S(16), Theme.S(126) + Theme.S(32), Theme.S(150), Theme.S(32));
            _openData.SetBounds(_openLog.Right + Theme.S(8), _openLog.Top, Theme.S(150), Theme.S(32));
        }
    }
}
