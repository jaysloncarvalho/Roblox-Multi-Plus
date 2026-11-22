using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private sealed class LogRow
        {
            public LogEntry Entry { get; set; }
            public string Level { get; set; }
            public Brush LevelBrush { get; set; }
        }

        private DataGrid modernLogGrid;
        private TextBox logQuery;
        private ComboBox levelFilter;
        private ComboBox sourceFilter;
        private TextBlock logEmpty;
        private readonly ObservableCollection<LogRow> displayedLogs = new ObservableCollection<LogRow>();
        private bool refreshingLogs;
        private static readonly string[] LogLevels = { "Todos os níveis", "Erro", "Aviso", "Sucesso", "Info" };
        private const string AllLogSources = "Todas as origens";

        private UIElement BuildModernLogs()
        {
            var page = Page("Logs", Describe(ActionButton("Abrir histórico", OpenLogHistory),
                "Abre o arquivo de log salvo no computador."));
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var actions = Horizontal(
                Describe(ActionButton("Copiar", CopyVisibleLogs), "Copia os eventos exibidos com filtro de privacidade."),
                Describe(ActionButton("Limpar logs", ClearVisibleHistory), "Limpa a lista da sessão atual e preserva o histórico salvo em disco."));
            ((FrameworkElement)actions.Children[1]).Margin = new Thickness(8, 0, 0, 0);
            var safeReport = Describe(ActionButton("Exportar diagnóstico", ExportSafeDiagnostics),
                "Salva os eventos exibidos com filtros de privacidade.");
            safeReport.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(safeReport);
            DockPanel.SetDock(actions, Dock.Right);
            toolbar.Children.Add(actions);

            logQuery = Field("Buscar evento");
            SetWatermark(logQuery, "Buscar evento");
            var filters = new StackPanel { Orientation = Orientation.Horizontal };
            levelFilter = LogFilter("Nível", 129);
            levelFilter.ItemsSource = LogLevels;
            levelFilter.SelectedIndex = 0;
            sourceFilter = LogFilter("Origem", 148);
            Describe(logQuery, "Busca eventos registrados na lista.");
            Describe(levelFilter, "Filtra eventos por nível, como erro, aviso ou sucesso.");
            Describe(sourceFilter, "Filtra eventos pelo recurso do launcher que os registrou.");
            filters.Children.Add(levelFilter);
            filters.Children.Add(sourceFilter);
            DockPanel.SetDock(filters, Dock.Right);
            toolbar.Children.Add(filters);
            toolbar.Children.Add(logQuery);
            DockTop(page, toolbar);

            modernLogGrid = new DataGrid
            {
                ItemsSource = displayedLogs, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                CanUserDeleteRows = false, CanUserReorderColumns = false, CanUserSortColumns = false,
                EnableRowVirtualization = true, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                Style = (Style)Resources["LogTable"]
            };
            modernLogGrid.Columns.Add(LogColumn("Hora", "Entry.Timestamp", 92, "{0:HH:mm:ss}", true));
            modernLogGrid.Columns.Add(LogColumn("Nível", "Level", 95, null, false, true));
            modernLogGrid.Columns.Add(LogColumn("Origem", "Entry.Source", 117));
            modernLogGrid.Columns.Add(LogColumn("Evento", "Entry.Message", 0));

            var table = new Grid();
            table.Children.Add(modernLogGrid);
            logEmpty = EmptyLabel("Nenhum evento encontrado.");
            logEmpty.VerticalAlignment = VerticalAlignment.Center;
            logEmpty.HorizontalAlignment = HorizontalAlignment.Center;
            logEmpty.IsHitTestVisible = false;
            table.Children.Add(logEmpty);
            page.Children.Add(table);

            logQuery.TextChanged += (sender, args) => RefreshModernLogs();
            levelFilter.SelectionChanged += (sender, args) => RefreshModernLogs();
            sourceFilter.SelectionChanged += (sender, args) =>
            {
                if (sourceFilter.SelectedItem != null) RefreshModernLogs();
            };
            return page;
        }

        private ComboBox LogFilter(string name, double width)
        {
            var combo = new ComboBox { Width = width, MinHeight = 36, Margin = new Thickness(8, 0, 8, 0),
                Style = (Style)Resources["LogFilter"] };
            AutomationProperties.SetName(combo, name);
            return combo;
        }

        private DataGridTextColumn LogColumn(string heading, string path, double width, string format = null,
            bool monospaced = false, bool colored = false)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, colored ? (object)new Binding("LevelBrush") : Palette("Text")));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path) { StringFormat = format }));
            if (monospaced) style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
            return new DataGridTextColumn
            {
                Header = heading, Binding = new Binding(path) { StringFormat = format },
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = style
            };
        }

        private void RefreshModernLogs()
        {
            if (modernLogGrid == null || currentPage != "Logs" || refreshingLogs) return;
            refreshingLogs = true;
            try
            {
                LogEntry[] entries;
                lock (logsList) entries = logsList.ToArray();
                var sources = new[] { AllLogSources }.Concat(entries.Select(entry => entry.Source).Distinct().OrderBy(source => source)).ToArray();
                var currentSources = sourceFilter.ItemsSource as string[];
                if (currentSources == null || !currentSources.SequenceEqual(sources))
                {
                    string selectedSource = sourceFilter.SelectedItem as string;
                    sourceFilter.ItemsSource = sources;
                    sourceFilter.SelectedItem = sources.Contains(selectedSource) ? selectedSource : AllLogSources;
                }

                string query = logQuery.Text.Trim();
                string level = levelFilter.SelectedItem as string;
                string origin = sourceFilter.SelectedItem as string;
                var desired = entries.Reverse().Where(entry =>
                    (level == LogLevels[0] || level == LogLevel(entry)) &&
                    (origin == AllLogSources || origin == entry.Source) &&
                    (entry.Message.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        entry.Source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();

                var keep = new HashSet<LogEntry>(desired);
                for (int i = displayedLogs.Count - 1; i >= 0; i--)
                    if (!keep.Contains(displayedLogs[i].Entry)) displayedLogs.RemoveAt(i);
                for (int i = 0; i < desired.Length; i++)
                    if (i == displayedLogs.Count || displayedLogs[i].Entry != desired[i])
                        displayedLogs.Insert(i, ToLogRow(desired[i]));
                logEmpty.Visibility = displayedLogs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { refreshingLogs = false; }
        }

        private static string LogLevel(LogEntry entry)
        {
            switch (entry.Level)
            {
                case "ERROR": return "Erro";
                case "WARN": return "Aviso";
                case "SUCCESS": return "Sucesso";
                default: return "Info";
            }
        }

        private LogRow ToLogRow(LogEntry entry)
        {
            string color = entry.Level == "ERROR" ? "Error" : entry.Level == "WARN" ? "Warn" :
                entry.Level == "SUCCESS" ? "Good" : "Muted";
            return new LogRow { Entry = entry, Level = LogLevel(entry), LevelBrush = Palette(color) };
        }

        private void OpenLogHistory()
        {
            try
            {
                string history = Path.Combine(dataDir, "log.txt");
                if (!File.Exists(history))
                {
                    SetStatus("O histórico de logs ainda está vazio.");
                    return;
                }
                using (Process.Start(new ProcessStartInfo(history) { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                SetStatus("Não foi possível abrir o histórico de logs.");
            }
        }

        private void ExportSafeDiagnostics()
        {
            var file = new Microsoft.Win32.SaveFileDialog { FileName = "diagnostico", DefaultExt = ".txt",
                Filter = "Documento de texto|*.txt", AddExtension = true, OverwritePrompt = true };
            if (file.ShowDialog(this) != true ||
                !string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(file.FileName), Path.GetFullPath(Path.Combine(dataDir, "log.txt")), StringComparison.OrdinalIgnoreCase))
                return;
            var lines = SafeVisibleLogLines();
            try
            {
                var report = new[] { "Diagnóstico do " + AppIdentity.Name + " " + AppIdentity.Version,
                    "Eventos filtrados. Revise o arquivo antes de compartilhar.", "" }.Concat(lines);
                AccountStore.WriteAtomically(file.FileName,
                    new System.Text.UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine, report)), null);
                SetStatus("Diagnóstico exportado.");
            }
            catch (IOException) { SetStatus("Não foi possível gravar o diagnóstico."); }
            catch (UnauthorizedAccessException) { SetStatus("Sem acesso para gravar o diagnóstico."); }
        }


        private string[] SafeVisibleLogLines()
        {
            var names = accounts.SelectMany(account => new[] { account.Username, account.Alias })
                .Concat(new[] { Environment.UserName }).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(name => name.Length).ToArray();
            return displayedLogs.Select(row => SafeDiagnostics.Redact(
                string.Format("[{0:HH:mm:ss}] [{1}] [{2}] {3}", row.Entry.Timestamp, row.Level,
                    row.Entry.Source, row.Entry.Message), names)).Where(line => line != null).ToArray();
        }

        private void CopyVisibleLogs()
        {
            var lines = SafeVisibleLogLines();
            if (lines.Length == 0) { SetStatus("Nenhum evento disponível após o filtro de privacidade."); return; }
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
                SetStatus("Eventos copiados com filtro de privacidade.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetStatus("A área de transferência está ocupada. Tente novamente.");
            }
        }


        private void ClearVisibleHistory()
        {
            if (MessageBox.Show(this, "Limpar todos os eventos desta sessão? O arquivo de log será preservado.",
                    "Limpar logs", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            lock (logsList) logsList.Clear();
            RefreshModernLogs();
            SetStatus("Eventos desta sessão limpos.");
        }
    }
}
