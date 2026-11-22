using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<string, UIElement> pages = new Dictionary<string, UIElement>();
        private ContentControl pageHost;
        private string currentPage;
        private TextBlock accountTotal;
        private TextBlock runningTotal;
        private TextBlock memoryTotal;

        private bool vaultConfigured;
        private bool vaultLocked;
        private int vaultEpoch;
        private long lastVaultInput;
        private static readonly long VaultIdleTicks = System.Diagnostics.Stopwatch.Frequency * 300L;
        private UIElement normalVaultView;
        private Grid lockedVaultView;
        private PasswordBox unlockPassword;
        private TextBlock unlockFeedback;
        private Button configureVault;
        private Button titleVaultLock;
        private Button recoveryButton;
        private Button changePasswordButton;
        private Button makeRecoveryButton;

        private UIElement BuildVaultLayer(UIElement normal)
        {
            normalVaultView = normal;
            var host = new Grid();
            host.Children.Add(normal);
            lockedVaultView = new Grid { Background = Palette("Canvas") };
            var form = new StackPanel { Width = 400, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            form.Children.Add(Label("Cofre bloqueado", "Text", 24, FontWeights.SemiBold));
            form.Children.Add(DialogCopy("Digite a senha mestra para acessar suas contas.", bottom: 12));
            unlockPassword = new PasswordBox { Style = (Style)Resources["SecretField"], MaxLength = 512 };
            AutomationProperties.SetName(unlockPassword, "Senha mestra");
            form.Children.Add(unlockPassword);
            unlockFeedback = DialogCopy("", top: 10);
            form.Children.Add(unlockFeedback);
            var unlock = ActionButton("Desbloquear", UnlockAccountVault, true);
            unlock.Margin = new Thickness(0, 10, 0, 0);
            form.Children.Add(unlock);
            recoveryButton = ActionButton("Usar código de recuperação", RecoverVault);
            recoveryButton.Margin = new Thickness(0, 8, 0, 0);
            form.Children.Add(recoveryButton);
            unlockPassword.KeyDown += (sender, args) =>
            {
                if (args.Key != System.Windows.Input.Key.Enter) return;
                args.Handled = true;
                UnlockAccountVault();
            };
            lockedVaultView.Children.Add(form);
            var close = ActionButton("Fechar launcher", Close);
            close.Margin = new Thickness(16);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            lockedVaultView.Children.Add(close);
            host.Children.Add(lockedVaultView);
            PreviewKeyDown += (sender, args) => MarkVaultInput();
            PreviewMouseDown += (sender, args) => MarkVaultInput();
            PreviewMouseWheel += (sender, args) => MarkVaultInput();
            MarkVaultInput();
            UpdateVaultLayer();
            return host;
        }

        private void MarkVaultInput()
        {
            lastVaultInput = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private void CheckVaultIdle()
        {
            if (vaultConfigured && !vaultLocked &&
                System.Diagnostics.Stopwatch.GetTimestamp() - lastVaultInput >= VaultIdleTicks)
                LockAccountVault();
        }

        private void UpdateVaultLayer()
        {
            if (normalVaultView == null) return;
            normalVaultView.Visibility = vaultLocked ? Visibility.Collapsed : Visibility.Visible;
            lockedVaultView.Visibility = vaultLocked ? Visibility.Visible : Visibility.Collapsed;
            if (titleVaultLock != null)
                titleVaultLock.Visibility = vaultConfigured ? Visibility.Visible : Visibility.Collapsed;
            if (configureVault != null)
            {
                configureVault.Content = vaultConfigured ? "Bloquear agora" : "Ativar senha mestra";
            }
            recoveryButton.Visibility = vaultLocked && accountStore.HasRecovery ? Visibility.Visible : Visibility.Collapsed;
            if (changePasswordButton != null) changePasswordButton.IsEnabled = vaultConfigured;
            if (makeRecoveryButton != null) makeRecoveryButton.IsEnabled = vaultConfigured;
            if (vaultLocked)
                Dispatcher.BeginInvoke(new Action(() => unlockPassword.Focus()));
        }

        private void LockAccountVault()
        {
            if (!vaultConfigured || vaultLocked) return;
            vaultLocked = true;
            vaultEpoch++;
            if (accountStore != null) accountStore.Lock();
            foreach (var account in accounts) account.Cookie = null;
            unlockPassword.Clear();
            unlockFeedback.Text = "";
            UpdateVaultLayer();
        }

        private void UnlockAccountVault()
        {
            if (!vaultLocked || unlockPassword.Password.Length == 0) return;
            string password = unlockPassword.Password;
            unlockPassword.Clear();
            try
            {
                string notice;
                var restored = accountStore.Unlock(password, out notice);
                OpenRecoveredVault(restored, notice);
            }
            catch (System.Security.Cryptography.CryptographicException)
            { unlockFeedback.Text = "Senha incorreta ou cofre inválido."; }
            catch (System.IO.IOException)
            { unlockFeedback.Text = "Não foi possível abrir o cofre. Tente novamente."; }
            catch (UnauthorizedAccessException)
            { unlockFeedback.Text = "Verifique o acesso à pasta de dados."; }
            catch (ArgumentException)
            { unlockFeedback.Text = "Os dados do cofre estão inválidos."; }
            catch (InvalidOperationException)
            { unlockFeedback.Text = "Não foi possível validar o cofre."; }
        }

        private void OpenRecoveredVault(List<AccountItem> restored, string notice)
        {
            var merged = new List<AccountItem>();
            foreach (var record in restored)
            {
                var live = accounts.Find(item => string.Equals(item.Username, record.Username, StringComparison.OrdinalIgnoreCase));
                if (live == null) merged.Add(record);
                else
                {
                    live.Cookie = record.Cookie;
                    live.Alias = record.Alias;
                    live.UserId = record.UserId;
                    live.AutoReconnect = record.AutoReconnect;
                    merged.Add(live);
                }
            }
            accounts = merged;
            if (selectedAccount == null || !accounts.Contains(selectedAccount))
                selectedAccount = accounts.Count > 0 ? accounts[0] : null;
            vaultLocked = false;
            MarkVaultInput();
            UpdateVaultLayer();
            UpdateModernAccounts();
            if (!string.IsNullOrEmpty(notice)) SetStatus(notice);
        }

        private void ChangeVaultPassword()
        {
            if (vaultLocked || !vaultConfigured) return;
            VaultPrompt("Trocar senha mestra", "A troca invalida o código de recuperação anterior.", "Senha atual", "Nova senha",
                (current, next) => accountStore.ChangePassword(current, next));
        }

        private void CreateVaultRecovery()
        {
            if (vaultLocked || !vaultConfigured) return;
            string code = null;
            if (!VaultPrompt("Gerar código de recuperação", "Guarde o novo código em local seguro. Ele substitui o código anterior.",
                "Senha mestra", null, (password, unused) => code = accountStore.CreateRecoveryCode(password))) return;
            StackPanel content;
            var dialog = CreateAccountDialog("Código de recuperação", 490, out content);
            content.Children.Add(DialogCopy("Anote este código agora. Ele permite definir uma nova senha neste cofre e só pode ser usado uma vez.", bottom: 12));
            var codeLabel = Label(code, "Text", 17, FontWeights.SemiBold);
            content.Children.Add(codeLabel);
            content.Children.Add(ActionButton("Concluir", dialog.Close, true));
            dialog.Closed += (sender, args) => codeLabel.Text = "";
            dialog.ShowDialog();
        }

        private void RecoverVault()
        {
            if (!vaultLocked || !accountStore.HasRecovery) return;
            List<AccountItem> restored = null;
            if (VaultPrompt("Recuperar acesso", "Use o código salvo e escolha uma nova senha mestra.", "Código de recuperação", "Nova senha",
                (code, next) => restored = accountStore.RecoverWithCode(code, next)))
                OpenRecoveredVault(restored, "Senha mestra atualizada. O código foi invalidado.");
        }

        private bool VaultPrompt(string title, string copy, string firstLabel, string newLabel, Action<string, string> apply)
        {
            StackPanel content;
            var dialog = CreateAccountDialog(title, 500, out content);
            content.Children.Add(DialogCopy(copy, bottom: 8));
            var first = firstLabel == null ? null : VaultField(content, firstLabel);
            PasswordBox next = null;
            PasswordBox again = null;
            if (newLabel != null)
            {
                next = VaultField(content, newLabel);
                again = VaultField(content, "Confirme a nova senha");
            }
            var feedback = DialogCopy("", top: 8);
            content.Children.Add(feedback);
            var confirm = ActionButton("Confirmar", () =>
            {
                string value = first == null ? null : first.Password;
                string proposed = next == null ? null : next.Password;
                if ((first != null && string.IsNullOrWhiteSpace(value)) || (next != null &&
                    (proposed.Length < 12 || string.IsNullOrWhiteSpace(proposed) || proposed != again.Password)))
                {
                    feedback.Text = "Preencha os campos. A nova senha precisa ter pelo menos 12 caracteres e ser repetida.";
                    return;
                }
                try
                {
                    apply(value, proposed);
                    dialog.DialogResult = true;
                }
                catch (System.Security.Cryptography.CryptographicException)
                { feedback.Text = "Senha ou código inválido, ou arquivo protegido danificado."; }
                catch (Exception)
                { feedback.Text = "Não foi possível concluir. Verifique o arquivo ou a pasta de dados e tente novamente."; }
            }, true);
            var cancel = ActionButton("Cancelar", dialog.Close);
            cancel.IsCancel = true;
            var actions = Horizontal(cancel, confirm);
            actions.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(actions);
            dialog.Loaded += (sender, args) => (first ?? next).Focus();
            dialog.Closed += (sender, args) =>
            {
                if (first != null) first.Clear();
                if (next != null) next.Clear();
                if (again != null) again.Clear();
                MarkVaultInput();
            };
            return dialog.ShowDialog() == true;
        }

        private PasswordBox VaultField(StackPanel content, string name)
        {
            content.Children.Add(DialogFieldLabel(name));
            var field = new PasswordBox { Style = (Style)Resources["SecretField"], MaxLength = 512 };
            AutomationProperties.SetName(field, name);
            content.Children.Add(field);
            return field;
        }

        private void ConfigureAccountVault()
        {
            if (vaultConfigured) { LockAccountVault(); return; }
            StackPanel content;
            var dialog = CreateAccountDialog("Ativar senha mestra", 440, out content);
            content.Children.Add(DialogCopy("Use pelo menos 12 caracteres. Sem a senha, o launcher não abre o cofre nem o backup.", bottom: 12));
            var password = new PasswordBox { Style = (Style)Resources["SecretField"], MaxLength = 512 };
            var confirmation = new PasswordBox { Style = (Style)Resources["SecretField"], MaxLength = 512 };
            AutomationProperties.SetName(password, "Senha mestra");
            AutomationProperties.SetName(confirmation, "Confirme a senha mestra");
            content.Children.Add(DialogFieldLabel("Senha mestra"));
            content.Children.Add(password);
            content.Children.Add(DialogFieldLabel("Repita a senha"));
            content.Children.Add(confirmation);
            var feedback = DialogCopy("", top: 8);
            content.Children.Add(feedback);
            var activate = ActionButton("Ativar proteção", () =>
            {
                string value = password.Password;
                if (value.Length < 12 || string.IsNullOrWhiteSpace(value) || value != confirmation.Password)
                {
                    feedback.Text = "Use pelo menos 12 caracteres e repita a mesma senha.";
                    return;
                }
                password.Clear();
                confirmation.Clear();
                try
                {
                    accountStore.EnablePassword(value, accounts);
                    vaultConfigured = true;
                    MarkVaultInput();
                    UpdateVaultLayer();
                    dialog.Close();
                }
                catch (Exception)
                {
                    feedback.Text = "Não foi possível concluir a proteção. Confira a pasta de dados ou outra instância aberta.";
                }
            }, true);
            activate.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(activate);
            dialog.Closed += (sender, args) => { password.Clear(); confirmation.Clear(); MarkVaultInput(); };
            dialog.ShowDialog();
        }


        private UIElement BuildModernWindow()
        {
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("RobloxMultiByJayy.Theme.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));

            Title = AppIdentity.Name;
            using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("RobloxMultiByJayy.AppIcon.ico"))
            {
                var icon = new System.Windows.Media.Imaging.IconBitmapDecoder(iconStream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Icon = icon.Frames[icon.Frames.Count - 1];
            }
            MinWidth = 900;
            MinHeight = 590;
            AllowsTransparency = false;
            Background = Palette("Canvas");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Foreground = Palette("Text");
            UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 39, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            var root = new DockPanel { Background = Palette("Canvas") };
            DockTop(root, BuildModernTitleBar());

            var footer = new DockPanel { Margin = new Thickness(18, 0, 18, 0), Height = 36 };
            accountTotal = Label("0 contas", "Muted", 12);
            statusText = Label("", "Muted", 12);
            statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            statusText.Margin = new Thickness(24, 0, 20, 0);
            runningTotal = Label("0 em jogo", "Muted", 12);
            memoryTotal = Label("RAM —", "Muted", 12);
            memoryTotal.Margin = new Thickness(18, 0, 0, 0);
            DockLeft(footer, accountTotal);
            DockPanel.SetDock(memoryTotal, Dock.Right);
            footer.Children.Add(memoryTotal);
            DockPanel.SetDock(runningTotal, Dock.Right);
            footer.Children.Add(runningTotal);
            footer.Children.Add(statusText);
            var footerSurface = new Border { Child = footer, Background = Palette("Surface"),
                BorderBrush = Palette("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(footerSurface, Dock.Bottom);
            root.Children.Add(footerSurface);

            var sidebar = new StackPanel { Margin = new Thickness(10, 18, 10, 0) };
            AddNavigation(sidebar, "Contas", "\uE716");
            AddNavigation(sidebar, "Reconexão", "\uE895");
            AddNavigation(sidebar, "Logs", "\uE7C3");
            AddNavigation(sidebar, "Configurações", "\uE713");
            DockLeft(root, new Border { Child = sidebar, Width = 192, Background = Palette("Surface"),
                BorderBrush = Palette("Line"), BorderThickness = new Thickness(0, 0, 1, 0) });

            pageHost = new ContentControl
            {
                Margin = new Thickness(24, 22, 24, 18),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            root.Children.Add(pageHost);
            OpenModernPage("Contas");
            return BuildVaultLayer(root);
        }

        private UIElement BuildModernTitleBar()
        {
            var titleBar = new DockPanel { Height = 39, Background = Palette("Surface") };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            WindowChrome.SetIsHitTestVisibleInChrome(actions, true);
            var maximize = ChromeButton("Maximizar", "\uE922", ToggleMaximized);
            StateChanged += (sender, args) =>
            {
                bool maximized = WindowState == WindowState.Maximized;
                maximize.Content = Glyph(maximized ? "\uE923" : "\uE922", 11);
                AutomationProperties.SetName(maximize, maximized ? "Restaurar" : "Maximizar");
                maximize.ToolTip = AutomationProperties.GetName(maximize);
            };
            titleVaultLock = ChromeButton("Bloquear cofre", "\uE72E", LockAccountVault);
            actions.Children.Add(titleVaultLock);
            actions.Children.Add(ChromeButton("Minimizar", "\uE921", () => SystemCommands.MinimizeWindow(this)));
            actions.Children.Add(maximize);
            actions.Children.Add(ChromeButton("Fechar", "\uE8BB", () => SystemCommands.CloseWindow(this)));
            DockPanel.SetDock(actions, Dock.Right);
            titleBar.Children.Add(actions);

            var mark = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            mark.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                Background = Palette("Active"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            mark.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                Background = Palette("Accent"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
            var brand = Horizontal(mark, Label(Title, "Text", 14, FontWeights.SemiBold));
            brand.Margin = new Thickness(16, 0, 0, 0);
            titleBar.Children.Add(brand);
            return new Border { Child = titleBar, BorderBrush = Palette("Line"),
                BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        private Button ChromeButton(string label, string glyph, Action command)
        {
            var button = ActionButton(Glyph(glyph, 11), command);
            button.Width = 46;
            button.Padding = new Thickness(0);
            button.BorderBrush = Brushes.Transparent;
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            return button;
        }

        private void ToggleMaximized()
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void AddNavigation(Panel sidebar, string title, string glyph)
        {
            var icon = Glyph(glyph, 17);
            icon.Margin = new Thickness(0, 0, 11, 0);
            var button = ActionButton(Horizontal(icon, Label(title)), () => OpenModernPage(title));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Height = 44;
            button.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetName(button, title);
            navigation.Add(title, button);
            sidebar.Children.Add(button);
        }

        private void OpenModernPage(string title)
        {
            if (!pages.ContainsKey(title))
            {
                switch (title)
                {
                    case "Contas": pages.Add(title, BuildModernAccounts()); break;
                    case "Reconexão": pages.Add(title, BuildModernReconnect()); break;
                    case "Logs": pages.Add(title, BuildModernLogs()); break;
                    case "Configurações": pages.Add(title, BuildModernSettings()); break;
                }
            }
            currentPage = title;
            pageHost.Content = pages[title];
            foreach (var item in navigation)
            {
                bool active = item.Key == title;
                item.Value.Background = active ? Palette("Active") : Brushes.Transparent;
                item.Value.BorderBrush = active ? Palette("Active") : Brushes.Transparent;
                AutomationProperties.SetItemStatus(item.Value, active ? "Aba atual" : "");
            }
            UpdateModernAccounts();
            if (title == "Logs") RefreshModernLogs();
        }

        private Brush Palette(string key) { return (Brush)Resources[key]; }

        private TextBlock Label(string text, string color = "Text", double size = 14, FontWeight? weight = null)
        {
            return new TextBlock { Text = text, Foreground = Palette(color), FontSize = size,
                FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }

        private TextBlock Glyph(string glyph, double size)
        {
            var icon = Label(glyph, "Text", size);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            return icon;
        }

        private Button ActionButton(object content, Action command, bool primary = false)
        {
            var button = new Button { Content = content, Style = (Style)Resources[primary ? "Primary" : "Action"] };
            button.Click += (sender, args) => command();
            return button;
        }

        private TextBox Field(string name, string value = "", double width = double.NaN)
        {
            var box = new TextBox { Text = value, Width = width, Style = (Style)Resources["Field"] };
            AutomationProperties.SetName(box, name);
            return box;
        }

        private CheckBox Check(string name, bool value, string style)
        {
            var box = new CheckBox { IsChecked = value, Style = (Style)Resources[style] };
            AutomationProperties.SetName(box, name);
            return box;
        }

        private DockPanel Page(string title, UIElement command = null)
        {
            var page = new DockPanel();
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            if (command != null)
            {
                DockPanel.SetDock(command, Dock.Right);
                heading.Children.Add(command);
            }
            heading.Children.Add(Label(title, "Text", 30, FontWeights.SemiBold));
            DockTop(page, heading);
            return page;
        }

        private Border Surface(UIElement child, string color = "Surface")
        {
            return new Border { Child = child, Background = Palette(color), BorderBrush = Palette("Line"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        }

        private static StackPanel Horizontal(params UIElement[] children)
        {
            var result = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in children) result.Children.Add(child);
            return result;
        }

        private static void DockTop(DockPanel parent, UIElement element)
        {
            DockPanel.SetDock(element, Dock.Top);
            parent.Children.Add(element);
        }

        private static void DockLeft(DockPanel parent, UIElement element)
        {
            DockPanel.SetDock(element, Dock.Left);
            parent.Children.Add(element);
        }

        private static void Cell(Grid row, int index, UIElement child)
        {
            Grid.SetColumn(child, index);
            row.Children.Add(child);
        }

        private static ScrollViewer Scroll(UIElement child)
        {
            return new ScrollViewer { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
    }
}
