using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private UIElement BuildModernReconnect()
        {
            var automatic = Check("Reconexão automática", autoReconnectEnabled, "Switch");
            var grace = NumberField("Tempo de tolerância (s)", autoReconnectGraceSec);
            var together = Check("Reconectar todas", reconnectAllOnSingleDrop, "Switch");
            var interval = NumberField("Intervalo de monitoramento (s)", autoReconnectIntervalSec);
            var cooldown = NumberField("Tempo entre reconexões (s)", autoReconnectCooldownSec);
            var save = ActionButton("Salvar alterações", () =>
            {
                int graceSeconds, intervalSeconds, cooldownSeconds;
                if (!TryNumber(grace, 1, 3600, out graceSeconds) ||
                    !TryNumber(interval, 1, 3600, out intervalSeconds) ||
                    !TryNumber(cooldown, 1, 3600, out cooldownSeconds)) return;

                var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AutoReconnectEnabled", SettingValue(automatic) },
                    { "ReconnectAllOnSingleDrop", SettingValue(together) },
                    { "AutoReconnectGraceSec", graceSeconds.ToString(CultureInfo.InvariantCulture) },
                    { "AutoReconnectIntervalSec", intervalSeconds.ToString(CultureInfo.InvariantCulture) },
                    { "AutoReconnectCooldownSec", cooldownSeconds.ToString(CultureInfo.InvariantCulture) }
                };
                if (!SaveModernSettings(changes)) return;
                autoReconnectEnabled = automatic.IsChecked == true;
                reconnectAllOnSingleDrop = together.IsChecked == true;
                autoReconnectGraceSec = graceSeconds;
                autoReconnectIntervalSec = intervalSeconds;
                autoReconnectCooldownSec = cooldownSeconds;
                if (logWatcherTimer != null) logWatcherTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
                UpdateModernAccounts();
                AddLog("Configurações de reconexão salvas.", "SUCCESS", "CONFIG");
            }, true);

            var page = Page("Reconexão", save);
            DockTop(page, SettingsGroup(
                SettingRow("Reconexão automática", automatic, "Tenta voltar ao jogo quando uma sessão cai, nas contas com reconexão ativada."),
                SettingRow("Tempo de tolerância (s)", grace, "Espera esse tempo após detectar uma queda para dar chance ao Roblox de reconectar sozinho."),
                SettingRow("Reconectar todas", together, "Quando uma conta cai, reinicia também as outras sessões confirmadas que têm reconexão ativada.")));
            var advanced = Advanced(SettingsGroup(
                SettingRow("Intervalo de monitoramento (s)", interval, "Define de quanto em quanto tempo o launcher procura sinais de queda nos logs do Roblox."),
                SettingRow("Tempo entre reconexões (s)", cooldown, "Define o intervalo mínimo entre tentativas de reconexão.")));
            advanced.Expanded += (sender, args) => advanced.BringIntoView();
            DockTop(page, advanced);

            var heading = new DockPanel { Margin = new Thickness(0, 9, 0, 10) };
            var activate = Describe(ActionButton("Ativar em todas", () => SetReconnectForAll(true)),
                "Ativa a preferência de reconexão em todas as contas cadastradas.");
            var deactivate = Describe(ActionButton("Desativar em todas", () => SetReconnectForAll(false)),
                "Desativa a preferência de reconexão em todas as contas cadastradas.");
            deactivate.Margin = new Thickness(8, 0, 0, 0);
            var batch = Horizontal(activate, deactivate);
            DockPanel.SetDock(batch, Dock.Right);
            heading.Children.Add(batch);
            heading.Children.Add(Label("Por conta", "Text", 19, FontWeights.SemiBold));
            DockTop(page, heading);

            var header = AccountGrid(true);
            Cell(header, 1, ColumnLabel("Conta"));
            Cell(header, 2, ColumnLabel("Reconexão"));
            Cell(header, 3, ColumnLabel("Status"));
            Cell(header, 4, ColumnLabel("Próxima tentativa"));
            reconnectRows = new StackPanel();
            reconnectEmpty = EmptyLabel("Nenhuma conta cadastrada.");
            page.Children.Add(AccountTable(header, reconnectRows, reconnectEmpty));
            return page;
        }

        private void SetReconnectForAll(bool enabled)
        {
            var changed = accounts.Where(account => account.AutoReconnect != enabled).ToArray();
            if (changed.Length == 0) return;
            foreach (var account in changed) account.AutoReconnect = enabled;
            if (!SaveAccounts())
                foreach (var account in changed) account.AutoReconnect = !enabled;
            else
                AddLog(enabled ? "Reconexão ativada em todas as contas." : "Reconexão desativada em todas as contas.", "INFO", "CONFIG");
            UpdateModernAccounts();
        }

        private UIElement BuildModernSettings()
        {
            var stored = LoadSettingsFromIni();
            var delay = Field("Intervalo entre lançamentos (s)",
                (launchDelayMs / 1000m).ToString("0.###", CultureInfo.CurrentCulture), 120);
            delay.MaxLength = 8;
            var confirm = Check("Confirmar antes de encerrar sessões", confirmSessionClose, "Switch");
            var presence = Check("Exibir atividade no Discord", showDiscordPresence, "Switch");
            presence.Click += (sender, args) => ChangeDiscordPresence(presence);
            var client = Field("ID do aplicativo", discordClientId, 210);
            client.MaxLength = 20;
            discordStatusLabel = Label(discordStatus, discordAccepted ? "Good" : "Muted", 13);
            var unlock = Check("Destravar FPS", ReadFlag(stored, "UnlockFPS", false), "Switch");
            var fps = Field("Limite de FPS", ReadSetting(stored, "MaxFPSValue", "144"), 120);
            var refresh = Check("Renovar cookies de sessão", ReadFlag(stored, "AutoCookieRefresh", true), "Switch");
            var passwords = Check("Salvar senhas", ReadFlag(stored, "SavePasswords", true), "Switch");
            var updates = Check("Verificar atualizações", ReadFlag(stored, "CheckForUpdates", true), "Switch");
            var aging = Check("Ocultar alerta de idade", ReadFlag(stored, "DisableAgingAlert", true), "Switch");
            var port = Field("Porta do servidor local", ReadSetting(stored, "WebServerPort", "7963"), 120);
            var save = ActionButton("Salvar alterações", () =>
            {
                decimal seconds;
                if (!decimal.TryParse(delay.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out seconds)
                    || seconds < 0.1m || seconds > 60m)
                {
                    ShowFieldError(delay, "Informe um intervalo de 0,1 a 60 segundos.");
                    return;
                }
                int maxFps, serverPort;
                if (!TryNumber(fps, 1, 10000, out maxFps) || !TryNumber(port, 1, 65535, out serverPort)) return;
                string clientId = client.Text.Trim();
                ulong parsedId;
                if (clientId.Length > 0 && (!ulong.TryParse(clientId, NumberStyles.None, CultureInfo.InvariantCulture, out parsedId) || parsedId == 0))
                {
                    ShowFieldError(client, "Informe um ID numérico do Discord ou deixe o campo vazio para usar o padrão.");
                    return;
                }

                int delayMs = (int)Math.Round(seconds * 1000);
                var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AccountJoinDelayMS", delayMs.ToString(CultureInfo.InvariantCulture) },
                    { "ConfirmSessionClose", SettingValue(confirm) },
                    { "ShowPresence", SettingValue(presence) },
                    { "DiscordClientId", clientId },
                    { "UnlockFPS", SettingValue(unlock) },
                    { "MaxFPSValue", maxFps.ToString(CultureInfo.InvariantCulture) },
                    { "AutoCookieRefresh", SettingValue(refresh) },
                    { "SavePasswords", SettingValue(passwords) },
                    { "CheckForUpdates", SettingValue(updates) },
                    { "DisableAgingAlert", SettingValue(aging) },
                    { "WebServerPort", serverPort.ToString(CultureInfo.InvariantCulture) }
                };
                if (!SaveModernSettings(changes)) return;
                launchDelayMs = delayMs;
                confirmSessionClose = confirm.IsChecked == true;
                ApplyDiscordSettings(showDiscordPresence, clientId);
                AddLog("Configurações gerais salvas.", "SUCCESS", "CONFIG");
            }, true);

            var page = Page("Configurações", save);
            var groups = new StackPanel();
            AddSettingsSection(groups, "Sessões", SettingsGroup(
                SettingRow("Intervalo entre lançamentos (s)", delay, "Define a pausa entre os lançamentos de contas em lote."),
                SettingRow("Confirmar antes de encerrar sessões", confirm, "Pede confirmação antes de fechar sessões pelo launcher.")));
            var economy = Check("Modo econômico em segundo plano", economicalSessions, "Switch");
            economy.Click += (sender, args) =>
            {
                if (SaveModernSettings(new Dictionary<string, string> { { "EconomicalSessions", SettingValue(economy) } }))
                {
                    economicalSessions = economy.IsChecked == true;
                    SamplePlayerPerformance();
                }
                else economy.IsChecked = economicalSessions;
            };
            AddSettingsSection(groups, "Desempenho", SettingsGroup(
                SettingRow("Modo econômico em segundo plano", economy,
                    "As sessões em segundo plano cedem prioridade quando outros programas precisam da CPU. " +
                    "Elas continuam abertas e recuperam a prioridade ao voltar ao foco. " +
                    "A conta principal mantém a prioridade original. A RAM é apenas monitorada.")));
            configureVault = ActionButton(vaultConfigured ? "Bloquear agora" : "Ativar senha mestra", ConfigureAccountVault);
            changePasswordButton = ActionButton("Trocar senha", ChangeVaultPassword);
            makeRecoveryButton = ActionButton("Gerar código", CreateVaultRecovery);
            changePasswordButton.IsEnabled = makeRecoveryButton.IsEnabled = vaultConfigured;
            AddSettingsSection(groups, "Segurança", SettingsGroup(
                SettingRow("Cofre de contas", configureVault,
                    "Protege o cofre com senha mestra e bloqueia após cinco minutos sem interação no launcher."),
                SettingRow("Senha mestra", changePasswordButton, "Troca a senha do cofre e invalida o código de recuperação anterior."),
                SettingRow("Código de recuperação", makeRecoveryButton, "Gera um código de uso único para redefinir a senha neste Windows.")));
            AddSettingsSection(groups, "Discord", SettingsGroup(
                SettingRow("Exibir atividade no Discord", presence, "Publica sua atividade no perfil do Discord enquanto o aplicativo de desktop estiver conectado."),
                SettingRow("ID do aplicativo", client, "Identifica o aplicativo usado para exibir a atividade no Discord. Deixe vazio para usar o padrão."),
                SettingRow("Conexão", discordStatusLabel, "Mostra se o Discord confirmou a atividade ou se há um problema na conexão.")));
            AddSettingsSection(groups, "Sobre", SettingsGroup(
                SettingRow(AppIdentity.Name, Label("Versão " + AppIdentity.Version, "Muted"))));
            groups.Children.Add(Advanced(SettingsGroup(
                SettingRow("Destravar FPS", unlock, "Preferência herdada. Esta versão salva o ajuste, mas não destrava o FPS do Roblox."),
                SettingRow("Limite de FPS", fps, "Valor salvo como preferência. Ajuste o limite efetivo dentro do cliente oficial."),
                SettingRow("Renovar cookies de sessão", refresh, "Preferência herdada. Este controle não aciona renovação automática nesta versão."),
                SettingRow("Salvar senhas", passwords, "Preferência herdada. Este controle não muda o armazenamento das contas nesta versão."),
                SettingRow("Verificar atualizações", updates, "Preferência herdada. Este controle não aciona uma busca por atualizações nesta versão."),
                SettingRow("Ocultar alerta de idade", aging, "Preferência herdada. Este controle não altera os alertas do Roblox nesta versão."),
                SettingRow("Porta do servidor local", port, "Porta salva por compatibilidade. Este controle não inicia um servidor nesta versão."))));
            page.Children.Add(Scroll(groups));
            return page;
        }

        private void ChangeDiscordPresence(CheckBox toggle)
        {
            bool enabled = toggle.IsChecked == true;
            if (!enabled) ApplyDiscordSettings(false, discordClientId);
            if (!SaveSettingsToIni(new Dictionary<string, string> { { "ShowPresence", SettingValue(toggle) } }))
            {
                toggle.IsChecked = showDiscordPresence;
                string message = enabled ? "Não foi possível ativar a presença. A preferência não foi gravada."
                    : "Presença desligada nesta sessão. Não foi possível gravar a preferência.";
                SetStatus(message);
                AddLog(message, "ERROR", "DISCORD");
                return;
            }

            if (enabled) ApplyDiscordSettings(true, discordClientId);
            SetStatus(enabled ? "Presença no Discord ativada." : "Presença no Discord desativada.");
        }

        private void ApplyDiscordSettings(bool enabled, string clientId)
        {
            showDiscordPresence = enabled;
            string effectiveId = string.IsNullOrEmpty(clientId) ? DefaultDiscordClientId : clientId;
            bool cleared = true;
            if (!enabled || effectiveId != discordClientId)
            {
                var previous = discordRpc;
                discordRpc = null;
                if (previous != null) cleared = previous.Stop();
            }
            discordClientId = effectiveId;
            if (!enabled)
            {
                SetDiscordStatus(cleared ? "Desativado" : "Desativado, sem confirmação do Discord", false);
                return;
            }
            if (discordRpc == null)
            {
                SetDiscordStatus("Conectando ao Discord…", false);
                discordRpc = MakeDiscordManager();
            }
            UpdateDiscordPresence();
        }

        private DiscordRpcManager MakeDiscordManager()
        {
            DiscordRpcManager instance = null;
            instance = new DiscordRpcManager(discordClientId, (message, accepted) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(discordRpc, instance) || !showDiscordPresence || Dispatcher.HasShutdownStarted)
                        return;
                    SetDiscordStatus(message, accepted);
                    AddLog(message, accepted ? "SUCCESS" : "WARN", "DISCORD");
                }));
            });
            return instance;
        }

        private bool SaveModernSettings(Dictionary<string, string> changes)
        {
            if (!SaveSettingsToIni(changes))
            {
                MessageBox.Show(this, "Verifique se a pasta do aplicativo permite gravação e tente novamente.",
                    "Não foi possível salvar", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            SetStatus("Configurações salvas.");
            return true;
        }

        private TextBox NumberField(string name, int value)
        {
            var field = Field(name, value.ToString(CultureInfo.InvariantCulture), 120);
            field.MaxLength = 8;
            return field;
        }

        private bool TryNumber(TextBox box, int min, int max, out int value)
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
                return true;
            ShowFieldError(box, "Informe um número inteiro entre " + min + " e " + max + ".");
            return false;
        }

        private void ShowFieldError(TextBox box, string message)
        {
            for (DependencyObject item = box; item != null; item = LogicalTreeHelper.GetParent(item))
            {
                var advanced = item as Expander;
                if (advanced != null) advanced.IsExpanded = true;
            }
            MessageBox.Show(this, message, System.Windows.Automation.AutomationProperties.GetName(box),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
        }

        private static string ReadSetting(Dictionary<string, string> stored, string key, string fallback)
        {
            string value;
            return stored.TryGetValue(key, out value) ? value : fallback;
        }

        private static bool ReadFlag(Dictionary<string, string> stored, string key, bool fallback)
        {
            bool value;
            return bool.TryParse(ReadSetting(stored, key, ""), out value) ? value : fallback;
        }

        private static string SettingValue(CheckBox box) { return box.IsChecked == true ? "true" : "false"; }

        private Border SettingsGroup(params UIElement[] children)
        {
            var rows = new StackPanel();
            for (int i = 0; i < children.Length; i++)
                rows.Children.Add(new Border { Child = children[i], BorderBrush = Palette("Line"),
                    BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0) });
            return Surface(rows);
        }

        private static T Describe<T>(T control, string description) where T : FrameworkElement
        {
            control.ToolTip = description;
            ToolTipService.SetShowOnDisabled(control, true);
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description);
            return control;
        }

        private DockPanel SettingRow(string label, FrameworkElement control, string description = null)
        {
            var row = new DockPanel { MinHeight = 49, Margin = new Thickness(14, 0, 14, 0) };
            control.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(control, Dock.Right);
            row.Children.Add(control);
            if (description == null)
                row.Children.Add(Label(label));
            else
            {
                var text = new StackPanel { Margin = new Thickness(0, 10, 14, 10), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(Label(label));
                var hint = Label(description, "Muted", 12);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Margin = new Thickness(0, 4, 0, 0);
                text.Children.Add(hint);
                row.Children.Add(text);
                System.Windows.Automation.AutomationProperties.SetHelpText(control, description);
            }
            return row;
        }

        private void AddSettingsSection(Panel parent, string title, UIElement group)
        {
            var label = Label(title, "Text", 19, FontWeights.SemiBold);
            label.Margin = new Thickness(0, 0, 0, 9);
            parent.Children.Add(label);
            var wrapper = new Border { Child = group, Margin = new Thickness(0, 0, 0, 16) };
            parent.Children.Add(wrapper);
        }

        private Expander Advanced(UIElement content)
        {
            return new Expander { Style = (Style)Resources["AdvancedSettings"], Header = "Opções avançadas", Foreground = Palette("Muted"),
                Content = content, Margin = new Thickness(0, 6, 0, 8) };
        }
    }
}
