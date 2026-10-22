using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace scrlog.UI
{
    public class JournalPage : Page
    {
        private readonly AppSettings _settings;
        private readonly Card _card = new Card();
        private readonly Label _title;
        private readonly Label _summary = Theme.MakeLabel("", Theme.Font(10f), Theme.Muted);
        private readonly ActionButton _refresh = new ActionButton { Text = "Обновить", Glyph = Theme.Glyph.Refresh, FillColor = Theme.Card, TextColor = Theme.Text, GlyphSize = 10f, Font = Theme.Semibold(10f) };
        private readonly ActionButton _openFolder = new ActionButton { Text = "Открыть папку", Glyph = Theme.Glyph.Folder, FillColor = Theme.Accent, GlyphSize = 10f, Font = Theme.Semibold(10f) };
        private readonly ListView _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = Theme.Font(10f),
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };

        public JournalPage(AppSettings settings)
        {
            _settings = settings;

            _title = _card.AddTitle("Журнал записей");
            _summary.AutoSize = false;
            _summary.AutoEllipsis = true;

            _list.Columns.Add("Файл", Theme.S(240));
            _list.Columns.Add("Начало", Theme.S(130));
            _list.Columns.Add("Конец", Theme.S(70));
            _list.Columns.Add("Размер", Theme.S(80), HorizontalAlignment.Right);
            _list.Columns.Add("Сеть", Theme.S(60), HorizontalAlignment.Center);
            _list.ItemActivate += (s, e) => PlaySelected();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Воспроизвести", null, (s, e) => PlaySelected());
            menu.Items.Add("Показать в папке", null, (s, e) => ShowSelectedInFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Удалить", null, (s, e) => DeleteSelected());
            _list.ContextMenuStrip = menu;

            _refresh.Click += (s, e) => Reload();
            _openFolder.Click += (s, e) => OpenFolder(_settings.LocalFolder);

            _card.Controls.AddRange(new Control[] { _summary, _refresh, _openFolder, _list });
            Controls.Add(_card);
            FinishInit();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Reload();
        }

        public async void Reload()
        {
            string folder = _settings.LocalFolder;
            string network = _settings.NetworkFolder;
            _summary.Text = "Загрузка…";

            // Проверка блокировок и сетевой папки может быть медленной — не держим UI
            var rows = await Task.Run(() => FileManager.ListRecordings(folder).Select(f => new
            {
                File = f,
                Locked = FileManager.IsLocked(f),
                Copied = !string.IsNullOrWhiteSpace(network) && SafeIsCopied(f, network)
            }).ToList());

            if (IsDisposed) return;

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var row in rows)
            {
                var f = row.File;
                var item = new ListViewItem(f.Name) { Tag = f.FullName };
                item.SubItems.Add(f.CreationTime.ToString("dd.MM.yyyy HH:mm"));
                item.SubItems.Add(row.Locked ? "идёт…" : f.LastWriteTime.ToString("HH:mm"));
                item.SubItems.Add(FileManager.FormatSize(f.Length));
                item.SubItems.Add(string.IsNullOrWhiteSpace(network) ? "—" : row.Copied ? "✓" : "…");
                if (row.Locked) item.ForeColor = Theme.Green;
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            long total = rows.Sum(r => r.File.Length);
            _summary.Text = rows.Count == 0
                ? $"Нет записей в {folder}"
                : $"{rows.Count} файлов, {FileManager.FormatSize(total)} · {folder}";
        }

        private static bool SafeIsCopied(FileInfo file, string network)
        {
            try { return FileManager.IsCopied(file, network); }
            catch { return false; }
        }

        private IEnumerable<string> SelectedPaths => _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag);

        private void PlaySelected()
        {
            var path = SelectedPaths.FirstOrDefault();
            if (path == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"Не удалось открыть файл: {ex.Message}", "Scrlog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowSelectedInFolder()
        {
            var path = SelectedPaths.FirstOrDefault();
            if (path != null)
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        private void DeleteSelected()
        {
            var paths = SelectedPaths.ToList();
            if (paths.Count == 0) return;
            if (MessageBox.Show(FindForm(), $"Удалить выбранные записи ({paths.Count})?", "Scrlog",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (var path in paths)
            {
                try
                {
                    File.Delete(path);
                    Log.Write($"Запись удалена вручную: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), $"{Path.GetFileName(path)}: {ex.Message}", "Scrlog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Reload();
        }

        public static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Scrlog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public override int MinimumContentWidth => Pad * 2 + _title.Left + TextWidth(_title) + Theme.S(24) +
            ButtonWidth(_refresh) + Theme.S(8) + ButtonWidth(_openFolder) + Theme.S(16);

        public override int MinimumContentHeight => ContentTop + Theme.S(260) + Pad;

        protected override void LayoutContent()
        {
            _card.SetBounds(Pad, ContentTop, ClientSize.Width - Pad * 2, ClientSize.Height - ContentTop - Pad);

            int right = _card.Width - Theme.S(16);
            int openWidth = ButtonWidth(_openFolder);
            int refreshWidth = ButtonWidth(_refresh);
            _openFolder.SetBounds(right - openWidth, Theme.S(16), openWidth, Theme.S(34));
            _refresh.SetBounds(_openFolder.Left - Theme.S(8) - refreshWidth, Theme.S(16), refreshWidth, Theme.S(34));
            _summary.SetBounds(Theme.S(16), Theme.S(42), Math.Max(0, _card.Width - Theme.S(32)), Theme.S(20));

            int top = Theme.S(66);
            _list.SetBounds(Theme.S(12), top, _card.Width - Theme.S(24), _card.Height - top - Theme.S(12));
            if (_list.Columns.Count > 0)
            {
                int fixedWidth = _list.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width);
                _list.Columns[0].Width = Math.Max(Theme.S(160), _list.ClientSize.Width - fixedWidth - SystemInformation.VerticalScrollBarWidth);
            }
        }
    }
}
