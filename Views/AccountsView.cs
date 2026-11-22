using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private sealed class AccountLine
        {
            public Border Element;
            public CheckBox Selection;
            public CheckBox Reconnect;
            public Button SessionAction;
            public Path StartIcon;
            public Path StopIcon;
            public bool CanEndSession;
            public TextBlock Name;
            public TextBlock Status;
            public TextBlock Detail;
            public TextBlock Performance;
            public Ellipse Dot;
        }

        private readonly Dictionary<AccountItem, AccountLine> accountLines = new Dictionary<AccountItem, AccountLine>();
        private readonly Dictionary<AccountItem, AccountLine> reconnectLines = new Dictionary<AccountItem, AccountLine>();
        private readonly HashSet<AccountItem> checkedAccounts = new HashSet<AccountItem>();
        private readonly List<Button> filterButtons = new List<Button>();
        private StackPanel accountRows;
        private StackPanel reconnectRows;
        private TextBlock accountEmpty;
        private TextBlock selectionSummary;
        private TextBlock reconnectEmpty;
        private TextBox accountQuery;
        private Button launchChecked;
        private CheckBox checkVisible;
        private List<AccountItem> visibleAccounts = new List<AccountItem>();
        private int accountFilter;
        private bool updatingSelection;
        private bool launchingBatch;
        private readonly HashSet<AccountItem> pendingLaunches = new HashSet<AccountItem>();
        private int launchDelayMs = 2500;
        private bool confirmSessionClose = true;

        private UIElement BuildModernAccounts()
        {
            var page = Page("Contas", ActionButton("+ Adicionar conta", ShowAddAccountDialog));

            var destination = new Grid { Margin = new Thickness(12) };
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Cell(destination, 0, Label("Jogo ou servidor", "Muted"));
            placeIdBox = Field("Place ID");
            SetWatermark(placeIdBox, "Place ID");
            Describe(placeIdBox, "Identificador do lugar que o Roblox deve abrir.");
            placeIdBox.MaxLength = 40;
            Button games = null;
            games = ActionButton("⌄", () => OpenMenu(games));
            games.ToolTip = "Jogos rápidos";
            AutomationProperties.SetName(games, "Jogos rápidos");
            games.ContextMenu = MakeMenu();
            MenuAction(games.ContextMenu, "Roblox (sem jogo)", () => { placeIdBox.Text = "0"; jobIdBox.Clear(); });
            foreach (var game in new[] {
                new KeyValuePair<string, string>("Pets Universe", "74629631798007"),
                new KeyValuePair<string, string>("Blox Fruits", "2753915549"),
                new KeyValuePair<string, string>("Murder Mystery 2", "142823291"),
                new KeyValuePair<string, string>("Steal an Egg", "107778070777162"),
                new KeyValuePair<string, string>("Brookhaven", "4924922222"),
                new KeyValuePair<string, string>("Blade Ball", "13772394625"),
                new KeyValuePair<string, string>("Arsenal", "286090429")
            })
                MenuAction(games.ContextMenu, game.Key, () => { placeIdBox.Text = game.Value; jobIdBox.Clear(); });
            var place = new DockPanel { Margin = new Thickness(10, 0, 8, 0) };
            DockPanel.SetDock(games, Dock.Right);
            place.Children.Add(games);
            place.Children.Add(placeIdBox);
            Cell(destination, 1, place);

            jobIdBox = Field("Job ID");
            SetWatermark(jobIdBox, "Job ID");
            Describe(jobIdBox, "Identificador de um servidor específico. Deixe vazio para o Roblox escolher o servidor.");
            jobIdBox.MaxLength = 100;
            Cell(destination, 2, jobIdBox);
            launchChecked = ActionButton("Iniciar selecionadas", async () => await LaunchBatch(checkedAccounts), true);
            launchChecked.Margin = new Thickness(10, 0, 8, 0);
            Describe(launchChecked, "Abre as contas marcadas no destino escolhido, incluindo as que estão ocultas pelo filtro.");
            Cell(destination, 3, launchChecked);
            Button batch = null;
            batch = ActionButton("⋯", () => OpenMenu(batch));
            AutomationProperties.SetName(batch, "Ações em lote");
            Describe(batch, "Mostra ações para iniciar contas cadastradas ou encerrar sessões confirmadas em lote.");
            batch.ContextMenu = MakeMenu();
            MenuAction(batch.ContextMenu, "Iniciar todas", async () => await LaunchBatch(accounts));
            MenuAction(batch.ContextMenu, "Encerrar todas", CloseAllAccounts);
            Cell(destination, 4, batch);
            var launcher = Surface(destination);
            launcher.Margin = new Thickness(0, 0, 0, 14);
            DockTop(page, launcher);

            accountQuery = Field("Buscar conta", "", 220);
            SetWatermark(accountQuery, "Buscar conta");
            accountQuery.TextChanged += (sender, args) => UpdateModernAccounts();
            var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            selectionSummary = Label("", "Muted", 12);
            selectionSummary.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(selectionSummary, Dock.Right);
            tools.Children.Add(selectionSummary);
            var controls = Horizontal(accountQuery);
            tools.Children.Add(controls);
            string[] filters = { "Todas", "Ativas", "Inativas" };
            for (int index = 0; index < filters.Length; index++)
            {
                int selectedFilter = index;
                var button = ActionButton(filters[index], () => SetAccountFilter(selectedFilter));
                button.Margin = new Thickness(8, 0, 0, 0);
                filterButtons.Add(button);
                controls.Children.Add(button);
            }
            SetAccountFilter(0);
            DockTop(page, tools);

            var header = AccountGrid(false);
            checkVisible = Check("Selecionar contas visíveis", false, "SelectionBox");
            Describe(checkVisible, "Marca ou desmarca todas as contas exibidas pelo filtro atual.");
            checkVisible.Click += (sender, args) =>
            {
                foreach (var account in visibleAccounts)
                {
                    if (checkVisible.IsChecked == true) checkedAccounts.Add(account);
                    else checkedAccounts.Remove(account);
                }
                UpdateAccountSelection();
            };
            Cell(header, 0, checkVisible);
            Cell(header, 1, ColumnLabel("Conta"));
            Cell(header, 2, ColumnLabel("Status"));
            Cell(header, 3, ColumnLabel("Jogo atual"));
            accountRows = new StackPanel();
            accountEmpty = EmptyLabel("Nenhuma conta cadastrada.");
            page.Children.Add(AccountTable(header, accountRows, accountEmpty));
            return page;
        }

        private void SetAccountFilter(int filter)
        {
            accountFilter = filter;
            for (int i = 0; i < filterButtons.Count; i++)
            {
                filterButtons[i].Background = Palette(i == filter ? "Active" : "Surface");
                AutomationProperties.SetItemStatus(filterButtons[i], i == filter ? "Filtro ativo" : "");
            }
            UpdateModernAccounts();
        }

        private static void SetWatermark(TextBox box, string text) { box.Tag = text; }

        private TextBlock ColumnLabel(string text) { return Label(text, "Text", 13, FontWeights.SemiBold); }

        private TextBlock EmptyLabel(string text)
        {
            var label = Label(text, "Muted");
            label.Margin = new Thickness(16, 15, 16, 15);
            return label;
        }

        private Grid AccountGrid(bool reconnect)
        {
            var row = new Grid { Margin = new Thickness(14, 0, 20, 0), MinHeight = 39 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reconnect ? 0 : 38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reconnect ? 102 : 184) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reconnect ? 184 : 1, reconnect ? GridUnitType.Pixel : GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reconnect ? 135 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(reconnect ? 0 : 42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(39) });
            return row;
        }

        private UIElement AccountTable(Grid header, StackPanel rows, TextBlock empty)
        {
            var table = new DockPanel();
            header.Margin = new Thickness(header.Margin.Left, 0, header.Margin.Right + 12, 0);
            DockTop(table, new Border { Background = Palette("Raised"), Child = header });
            var content = new StackPanel();
            content.Children.Add(rows);
            content.Children.Add(empty);
            var scrolling = Scroll(content);
            scrolling.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            table.Children.Add(scrolling);
            return Surface(table, "Canvas");
        }

        private AccountLine CreateAccountLine(AccountItem account, bool reconnect)
        {
            var row = AccountGrid(reconnect);
            row.MinHeight = 62;
            var line = new AccountLine();
            line.Element = new Border { Child = row, BorderBrush = Palette("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };

            var names = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Name = Label(DisplayAccountName(account), "Text", 15, FontWeights.SemiBold);
            line.Name.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(line.Name);
            var username = Label("@" + account.Username, "Muted", 12);
            username.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(username);
            var identity = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            DockLeft(identity, AccountAvatar(account));
            identity.Children.Add(names);
            Cell(row, 1, identity);

            line.Dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Status = Label("");
            line.Detail = Label("—", "Muted");
            line.Detail.TextTrimming = TextTrimming.CharacterEllipsis;
            Cell(row, reconnect ? 3 : 2, Horizontal(line.Dot, line.Status));
            Cell(row, reconnect ? 4 : 3, line.Detail);

            if (reconnect)
            {
                line.Reconnect = Check("Reconexão de " + account.Username, account.AutoReconnect, "Switch");
                Describe(line.Reconnect, "Define se esta conta pode ser reconectada quando a reconexão geral está ligada.");
                line.Reconnect.Click += (sender, args) => SetAccountReconnect(account, line.Reconnect.IsChecked == true);
                Cell(row, 2, line.Reconnect);
            }
            else
            {
                line.Selection = Check("Selecionar " + account.Username, checkedAccounts.Contains(account), "SelectionBox");
                line.Selection.Click += (sender, args) =>
                {
                    if (updatingSelection) return;
                    if (line.Selection.IsChecked == true) checkedAccounts.Add(account);
                    else checkedAccounts.Remove(account);
                    UpdateAccountSelection();
                };
                Cell(row, 0, line.Selection);
                line.Performance = Label("", "Muted", 11);
                line.Performance.TextTrimming = TextTrimming.CharacterEllipsis;
                names.Children.Add(line.Performance);

                line.StartIcon = new Path
                {
                    Data = Geometry.Parse("M 0,0 L 11,6 L 0,12 Z"),
                    Fill = Palette("Accent"), Width = 12, Height = 13, Stretch = Stretch.Uniform
                };
                line.StopIcon = new Path
                {
                    Data = new RectangleGeometry(new Rect(0, 0, 11, 11), 1, 1),
                    Fill = Palette("Error"), Width = 11, Height = 11, Stretch = Stretch.Uniform
                };
                line.SessionAction = ActionButton(line.StartIcon, async () =>
                {
                    if (line.CanEndSession) RequestCloseAccountSession(account);
                    else await LaunchAccount(account);
                });
                line.SessionAction.Background = Palette("Active");
                line.SessionAction.Padding = new Thickness(0);
                line.SessionAction.Width = 34;
                line.SessionAction.VerticalAlignment = VerticalAlignment.Center;
                Cell(row, 5, line.SessionAction);
            }

            Button menu = null;
            menu = ActionButton("⋯", () => OpenMenu(menu));
            menu.Margin = new Thickness(2, 0, 0, 0);
            menu.BorderBrush = Brushes.Transparent;
            menu.Background = Brushes.Transparent;
            menu.Padding = new Thickness(5, 0, 5, 0);
            AutomationProperties.SetName(menu, "Opções de " + account.Username);
            Describe(menu, "Mostra as ações e as preferências desta conta.");
            menu.ContextMenu = MakeMenu();
            var launch = MenuAction(menu.ContextMenu, "Iniciar conta", async () => await LaunchAccount(account));
            var signIn = MenuAction(menu.ContextMenu, "Entrar novamente", ShowAddAccountDialog);
            signIn.Visibility = Visibility.Collapsed;
            var close = MenuAction(menu.ContextMenu, "Encerrar sessão", () => RequestCloseAccountSession(account));
            MenuAction(menu.ContextMenu, "Remover conta", () => ShowRemoveAccountDialog(account)).Foreground = Palette("Error");
            var toggle = MenuAction(menu.ContextMenu, "Reconexão automática", () => SetAccountReconnect(account, !account.AutoReconnect));
            var primary = MenuAction(menu.ContextMenu, "Marcar como conta principal", () =>
            {
                string value = IsPrimaryAccount(account) ? "" : account.Username;
                if (!SaveModernSettings(new Dictionary<string, string> { { "PrimaryPerformanceAccount", value } })) return;
                primaryAccount = value;
                SamplePlayerPerformance();
            });
            Describe(primary, "A conta principal mantém a prioridade original mesmo em segundo plano.");
            Describe(toggle, "Define se o launcher tenta reconectar esta conta após uma queda.");
            menu.ContextMenu.Opened += (sender, args) =>
            {
                bool running = IsAccountRunning(account);
                launch.IsEnabled = CanStartAccount(account, running);
                signIn.Visibility = account.Status == NeedsSignIn ? Visibility.Visible : Visibility.Collapsed;
                close.IsEnabled = running;
                primary.Header = IsPrimaryAccount(account) ? "Desmarcar conta principal" : "Marcar como conta principal";
                toggle.Header = account.AutoReconnect ? "Desativar reconexão" : "Ativar reconexão";
            };
            Cell(row, 6, menu);
            return line;
        }

        private void RequestCloseAccountSession(AccountItem account)
        {
            if (!IsAccountRunning(account))
            {
                UpdateModernAccounts();
                return;
            }
            if (!confirmSessionClose || BuildCloseSessionDialog(account).ShowDialog() == true)
                CloseAccountRoblox(account);
        }

        private UIElement AccountAvatar(AccountItem account)
        {
            string initial = string.IsNullOrEmpty(account.Username) ? "?" : account.Username.Substring(0, 1).ToUpperInvariant();
            var initials = Label(initial, "Accent", 17, FontWeights.SemiBold);
            initials.HorizontalAlignment = HorizontalAlignment.Center;
            var avatar = new Image { Width = 39, Height = 39, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed,
                Clip = new EllipseGeometry(new Point(19.5, 19.5), 19.5, 19.5) };
            var layer = new Grid();
            layer.Children.Add(initials);
            layer.Children.Add(avatar);
            LoadAccountAvatar(account, avatar, initials);
            return new Border { Width = 39, Height = 39, CornerRadius = new CornerRadius(20), Background = Palette("Raised"), Child = layer };
        }

        private static string DisplayAccountName(AccountItem account)
        {
            return string.IsNullOrWhiteSpace(account.Alias) ? account.Username ?? "" : account.Alias;
        }

        private void SetAccountReconnect(AccountItem account, bool enabled)
        {
            bool previous = account.AutoReconnect;
            account.AutoReconnect = enabled;
            if (!SaveAccounts()) account.AutoReconnect = previous;
            else AddLog("Reconexão de @" + account.Username + (enabled ? " ativada." : " desativada."), "INFO", "CONFIG");
            UpdateModernAccounts();
        }

        private void UpdateModernAccounts()
        {
            if (accountRows == null) return;
            var existing = new HashSet<AccountItem>(accounts);
            checkedAccounts.IntersectWith(existing);
            foreach (var key in accountLines.Keys.Where(key => !existing.Contains(key)).ToArray()) accountLines.Remove(key);
            foreach (var key in reconnectLines.Keys.Where(key => !existing.Contains(key)).ToArray()) reconnectLines.Remove(key);
            var running = accounts.Where(IsAccountRunning).ToList();
            var runningSet = new HashSet<AccountItem>(running);
            string query = accountQuery.Text.Trim();
            visibleAccounts = accounts.Where(account =>
                (accountFilter == 0 || (accountFilter == 1) == runningSet.Contains(account)) &&
                (DisplayAccountName(account).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (account.Username ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            if (currentPage == "Contas")
                UpdateAccountTable(accountRows, accountEmpty, accountLines, visibleAccounts, runningSet, false);
            if (currentPage == "Reconexão")
                UpdateAccountTable(reconnectRows, reconnectEmpty, reconnectLines, accounts, runningSet, true);
            accountTotal.Text = accounts.Count + (accounts.Count == 1 ? " conta" : " contas");
            runningTotal.Text = running.Count + " em jogo";
            UpdateAccountSelection();
            DisplayPlayerPerformance();
        }

        private void DisplayPlayerPerformance()
        {
            foreach (var entry in accountLines)
            {
                var label = entry.Value.Performance;
                ProcessPerformance sample;
                string metrics = IsPrimaryAccount(entry.Key) ? "Principal" : "";
                if (accountPerformance.TryGetValue(entry.Key, out sample))
                {
                    if (metrics.Length > 0) metrics += " · ";
                    metrics += "CPU " + (sample.CpuPercent.HasValue ? sample.CpuPercent.Value.ToString("0.0") + "%" : "—")
                        + " · RAM " + (sample.MemoryBytes / 1048576.0).ToString("N0") + " MB";
                    if (sample.IsEconomical) metrics += " · Econômico";
                }
                if (label.Text != metrics) label.Text = metrics;
                label.ToolTip = metrics.Length == 0 ? null : metrics +
                    "\nCPU indica o uso do processador. RAM indica a memória em uso pela sessão." +
                    (IsPrimaryAccount(entry.Key) ? "\nA conta principal mantém a prioridade original em segundo plano." : "") +
                    (accountPerformance.TryGetValue(entry.Key, out sample) && sample.IsEconomical
                        ? "\nCom prioridade reduzida, a sessão cede espaço quando outros programas precisam da CPU." : "");
                label.Visibility = metrics.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UpdateAccountTable(Panel panel, TextBlock empty, Dictionary<AccountItem, AccountLine> lines,
            IList<AccountItem> shown, HashSet<AccountItem> running, bool reconnect)
        {
            var ordered = new List<UIElement>(shown.Count);
            foreach (var account in shown)
            {
                AccountLine line;
                if (!lines.TryGetValue(account, out line))
                {
                    line = CreateAccountLine(account, reconnect);
                    lines.Add(account, line);
                }
                bool active = running.Contains(account);
                DateTime since;
                bool waiting;
                lock (accountSuspectedDisconnectTime)
                    waiting = accountSuspectedDisconnectTime.TryGetValue(account.Username, out since);
                waiting = waiting && autoReconnectEnabled && account.AutoReconnect;
                bool reconnecting;
                lock (reconnectingAccounts)
                    reconnecting = reconnectingAccounts.Contains(account.Username);
                bool starting = account.IsLaunching || pendingLaunches.Contains(account);
                string state = reconnecting ? "Reconectando" : waiting ? "Aguardando reconexão" :
                    starting ? "Iniciando" : active ? "Em jogo" : account.Status == NeedsSignIn || account.Status == UnconfirmedSession ?
                        account.Status : "Offline";
                bool needsAttention = state == NeedsSignIn || state == UnconfirmedSession;
                line.Dot.Fill = Palette(waiting || reconnecting || starting || needsAttention ? "Warn" : active ? "Good" : "Muted");
                line.Status.Text = state;
                line.Status.Foreground = Palette(waiting || reconnecting ? "Warn" : "Text");
                line.Name.Text = DisplayAccountName(account);
                line.Detail.Text = reconnect
                    ? (waiting ? Math.Max(0, (int)Math.Ceiling(autoReconnectGraceSec - (DateTime.UtcNow - since).TotalSeconds)) + " s" : "—")
                    : ((active || waiting || reconnecting) && !string.IsNullOrEmpty(account.CurrentGame) ? account.CurrentGame : "—");
                if (line.Reconnect != null) line.Reconnect.IsChecked = account.AutoReconnect;
                if (line.SessionAction != null)
                {
                    line.CanEndSession = active;
                    line.SessionAction.Content = active ? line.StopIcon : line.StartIcon;
                    line.SessionAction.IsEnabled = active || CanStartAccount(account, false);
                    string action = (active ? "Encerrar sessão de @" : "Iniciar @") + account.Username;
                    line.SessionAction.ToolTip = action;
                    AutomationProperties.SetName(line.SessionAction, action);
                }
                ordered.Add(line.Element);
            }

            if (!panel.Children.Cast<UIElement>().SequenceEqual(ordered))
            {
                panel.Children.Clear();
                foreach (var row in ordered) panel.Children.Add(row);
            }
            empty.Text = accounts.Count == 0 ? "Nenhuma conta cadastrada." : "Nenhuma conta encontrada.";
            empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateAccountSelection()
        {
            if (checkVisible == null) return;
            updatingSelection = true;
            foreach (var line in accountLines) line.Value.Selection.IsChecked = checkedAccounts.Contains(line.Key);
            int selected = visibleAccounts.Count(checkedAccounts.Contains);
            int hidden = checkedAccounts.Count - selected;
            selectionSummary.Text = hidden > 0 ? hidden + (hidden == 1 ? " oculta no filtro" : " ocultas no filtro") : "";
            selectionSummary.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            checkVisible.IsEnabled = visibleAccounts.Count > 0;
            checkVisible.IsChecked = selected > 0 && selected == visibleAccounts.Count;
            launchChecked.IsEnabled = !launchingBatch && checkedAccounts.Count > 0;
            launchChecked.Content = checkedAccounts.Count == 0 ? "Iniciar selecionadas" : "Iniciar selecionadas (" + checkedAccounts.Count + ")";
            updatingSelection = false;
        }

        private async Task LaunchBatch(IEnumerable<AccountItem> selection)
        {
            if (launchingBatch) return;
            var queued = selection.ToArray();
            if (queued.Length == 0) return;

            string place = placeIdBox.Text.Trim();
            string job = jobIdBox.Text.Trim();
            launchingBatch = true;
            UpdateModernAccounts();
            try
            {
                for (int i = 0; i < queued.Length && IsLoaded; i++)
                {
                    var account = queued[i];
                    bool started = await LaunchAccount(account, place, job);
                    if (started && i < queued.Length - 1) await Task.Delay(launchDelayMs);
                }
            }
            finally
            {
                launchingBatch = false;
                UpdateModernAccounts();
            }
        }

        private bool CanStartAccount(AccountItem account, bool running)
        {
            return !running && !account.IsLaunching && !pendingLaunches.Contains(account) && !launchingBatch;
        }

        private async Task<bool> LaunchAccount(AccountItem account, string place = null, string job = null)
        {
            if (!accounts.Contains(account) || account.IsLaunching || IsAccountRunning(account) ||
                !pendingLaunches.Add(account)) return false;
            UpdateModernAccounts();
            try
            {
                return await LaunchGame(account, place, job);
            }
            catch (Exception error)
            {
                SetStatus("Falha ao iniciar @" + account.Username + ".");
                AddLog("Falha ao iniciar @" + account.Username + ": " + error.GetType().Name, "ERROR", "ROBLOX");
                return false;
            }
            finally
            {
                pendingLaunches.Remove(account);
                if (IsLoaded) UpdateModernAccounts();
            }
        }

        private ContextMenu MakeMenu()
        {
            return new ContextMenu { Style = (Style)Resources["PopupActions"] };
        }

        private MenuItem MenuAction(ContextMenu menu, string name, Action command)
        {
            var item = new MenuItem { Header = name };
            item.Click += (sender, args) => command();
            menu.Items.Add(item);
            return item;
        }

        private static void OpenMenu(Button button)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
}
