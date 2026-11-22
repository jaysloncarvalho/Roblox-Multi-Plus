using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private void ShowAddAccountDialog()
        {
            BuildAddAccountDialog().ShowDialog();
        }

        private Window BuildAddAccountDialog()
        {
            StackPanel content;
            var dialog = CreateAccountDialog("Adicionar conta", 490, out content);

            var manual = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
            manual.Children.Add(DialogFieldLabel("Cookie .ROBLOSECURITY"));
            var cookie = new PasswordBox { Style = (Style)Resources["SecretField"] };
            AutomationProperties.SetName(cookie, "Cookie .ROBLOSECURITY");
            manual.Children.Add(DialogCopy("Vincula uma sessão existente à conta. Cole apenas o cookie da sua própria conta e mantenha esse valor em sigilo.", bottom: 8));
            manual.Children.Add(cookie);
            manual.Children.Add(DialogFieldLabel("Apelido (opcional)"));
            var alias = Field("Apelido");
            SetWatermark(alias, "Ex.: Conta principal");
            Describe(alias, "Nome exibido apenas neste launcher para você identificar a conta.");
            manual.Children.Add(alias);

            var feedback = DialogCopy("", top: 10);
            feedback.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
            manual.Children.Add(feedback);

            Button save = null;
            save = ActionButton("Adicionar conta", async () =>
            {
                string clean = CleanRoblosecurityCookie(cookie.Password.Trim());
                if (string.IsNullOrWhiteSpace(clean))
                {
                    ShowDialogFeedback(feedback, "Cole o cookie da conta para continuar.", true);
                    cookie.Focus();
                    return;
                }

                save.IsEnabled = false;
                try { await LinkAccountSessionAsync(dialog, feedback, clean, alias.Text.Trim()); }
                finally
                {
                    save.IsEnabled = true;
                }
            }, true);
            save.HorizontalAlignment = HorizontalAlignment.Right;
            save.Margin = new Thickness(0, 14, 0, 0);
            manual.Children.Add(save);

            var browserPanel = new StackPanel { Margin = new Thickness(16) };
            browserPanel.Children.Add(DialogCopy(
                "Entre na página oficial em uma janela separada. Depois do login, o launcher valida e guarda a sessão no cofre local.",
                bottom: 10));
            var browserFeedback = DialogCopy("", top: 10);
            browserFeedback.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(browserFeedback, AutomationLiveSetting.Polite);
            Button browser = null;
            browser = ActionButton("Entrar com o Roblox", async () =>
            {
                browser.IsEnabled = false;
                save.IsEnabled = false;
                try
                {
                    ShowDialogFeedback(browserFeedback, "Abrindo o login oficial…", false);
                    string session = await WebViewLogin.RunAsync(dialog, dataDir);
                    if (!dialog.IsVisible) return;
                    if (string.IsNullOrEmpty(session))
                        ShowDialogFeedback(browserFeedback, "Login interrompido ou expirado. Tente novamente.", false);
                    else
                        await LinkAccountSessionAsync(dialog, browserFeedback, session, alias.Text.Trim());
                }
                catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
                {
                    ShowDialogFeedback(browserFeedback, "Instale o Runtime Microsoft Edge WebView2 e tente novamente.", true);
                }
                catch (System.IO.IOException)
                {
                    ShowDialogFeedback(browserFeedback, "Não foi possível limpar o perfil temporário. A conta não foi vinculada.", true);
                }
                catch (Exception)
                {
                    ShowDialogFeedback(browserFeedback, "Não foi possível usar a janela de login. Tente novamente ou use o vínculo manual.", true);
                }
                finally
                {
                    browser.IsEnabled = true;
                    save.IsEnabled = true;
                }
            }, true);
            browser.HorizontalAlignment = HorizontalAlignment.Left;
            browserPanel.Children.Add(browser);
            browserPanel.Children.Add(browserFeedback);
            content.Children.Add(Surface(browserPanel));
            dialog.Closed += (sender, args) => cookie.Clear();
            content.Children.Add(Surface(manual));
            dialog.Loaded += (sender, args) => browser.Focus();
            return dialog;
        }

        private async Task LinkAccountSessionAsync(Window dialog, TextBlock feedback, string session, string alias)
        {
            ShowDialogFeedback(feedback, "Verificando a conta…", false);
            var account = await Task.Run(() =>
            {
                long userId;
                string username = ResolveUsernameAndIdFromCookie(session, out userId);
                return new AccountItem { Username = username, UserId = userId, Cookie = session, Alias = alias };
            });
            if (!dialog.IsVisible) return;
            if (string.IsNullOrEmpty(account.Username) || account.UserId <= 0)
            {
                ShowDialogFeedback(feedback, "O Roblox não confirmou a conta. Verifique a sessão e tente novamente.", true);
                return;
            }
            if (!PersistLogin(account))
            {
                ShowDialogFeedback(feedback, "Não foi possível gravar no cofre local. Confira o acesso à pasta de dados.", true);
                return;
            }
            dialog.Close();
            SetStatus("Conta @" + account.Username + " adicionada.");
        }


        private void ShowRemoveAccountDialog(AccountItem account)
        {
            if (accounts.Contains(account)) BuildRemoveAccountDialog(account).ShowDialog();
        }

        private Window BuildCloseSessionDialog(AccountItem account)
        {
            StackPanel content;
            var dialog = CreateAccountDialog("Encerrar sessão?", 420, out content);
            content.Children.Add(DialogCopy("A janela do Roblox de @" + account.Username + " será fechada."));

            var cancel = ActionButton("Cancelar", dialog.Close);
            cancel.IsCancel = true;
            var close = ActionButton("Encerrar sessão", () => dialog.DialogResult = true);
            close.Foreground = Palette("Error");
            close.BorderBrush = Palette("Error");
            close.Margin = new Thickness(8, 0, 0, 0);
            var actions = Horizontal(cancel, close);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 18, 0, 0);
            content.Children.Add(actions);
            dialog.Loaded += (sender, args) => cancel.Focus();
            return dialog;
        }


        private Window BuildRemoveAccountDialog(AccountItem account)
        {
            StackPanel content;
            var dialog = CreateAccountDialog("Remover conta?", 420, out content);
            string description = "A conta @" + account.Username + " sairá da lista deste gerenciador. Sua conta no Roblox continuará existindo.";
            if (IsAccountRunning(account)) description += " A sessão vinculada também será encerrada.";
            content.Children.Add(DialogCopy(description));

            var feedback = DialogCopy("", top: 10);
            feedback.Visibility = Visibility.Collapsed;
            content.Children.Add(feedback);

            var cancel = ActionButton("Cancelar", dialog.Close);
            cancel.IsCancel = true;
            var remove = ActionButton("Remover conta", () =>
            {
                int index = accounts.IndexOf(account);
                if (index < 0)
                {
                    dialog.Close();
                    return;
                }
                accounts.RemoveAt(index);
                if (!SaveAccounts())
                {
                    accounts.Insert(index, account);
                    ShowDialogFeedback(feedback, "Não foi possível gravar a remoção. A conta continua na lista.", true);
                    return;
                }

                if (IsAccountRunning(account)) CloseAccountRoblox(account);
                manualClosedAccounts.Add(account.Username);
                ClearSessionLogTracking(account.Username);
                lock (avatarCache) avatarCache.Remove(account.Username);
                if (selectedAccount == account) selectedAccount = accounts.Count == 0 ? null : accounts[0];
                RefreshAccountsUI();
                dialog.Close();
                SetStatus("Conta @" + account.Username + " removida.");
                AddLog("Conta @" + account.Username + " removida do gerenciador.", "INFO", "ACCOUNTS");
            });
            remove.Foreground = Palette("Error");
            remove.BorderBrush = Palette("Error");
            remove.Margin = new Thickness(8, 0, 0, 0);
            var actions = Horizontal(cancel, remove);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 18, 0, 0);
            content.Children.Add(actions);
            dialog.Loaded += (sender, args) => cancel.Focus();
            return dialog;
        }

        private Window CreateAccountDialog(string title, double width, out StackPanel content)
        {
            var dialog = new Window
            {
                Owner = this, Title = title, Width = width, SizeToContent = SizeToContent.Height,
                MaxHeight = SystemParameters.WorkArea.Height - 32, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                ShowInTaskbar = false, Background = Brushes.Transparent, Foreground = Palette("Text"),
                FontFamily = FontFamily, FontSize = FontSize, UseLayoutRounding = true
            };
            foreach (var dictionary in Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);

            var frame = new DockPanel { Margin = new Thickness(20) };
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var close = ActionButton(Glyph("\uE8BB", 11), dialog.Close);
            close.Width = 34;
            close.Padding = new Thickness(0);
            close.Background = Brushes.Transparent;
            close.BorderBrush = Brushes.Transparent;
            close.ToolTip = "Fechar";
            AutomationProperties.SetName(close, "Fechar");
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            var heading = Label(title, "Text", 22, FontWeights.SemiBold);
            heading.MouseLeftButtonDown += (sender, args) => dialog.DragMove();
            header.Children.Add(heading);
            DockTop(frame, header);

            content = new StackPanel();
            frame.Children.Add(Scroll(content));
            dialog.Content = Surface(frame, "Canvas");
            dialog.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                dialog.Close();
            };
            return dialog;
        }

        private TextBlock DialogFieldLabel(string text)
        {
            var label = Label(text, "Text", 13, FontWeights.SemiBold);
            label.Margin = new Thickness(0, 10, 0, 5);
            return label;
        }

        private TextBlock DialogCopy(string text, double top = 0, double bottom = 0)
        {
            var label = Label(text, "Muted", 13);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, top, 0, bottom);
            return label;
        }

        private void ShowDialogFeedback(TextBlock feedback, string text, bool error)
        {
            feedback.Text = text;
            feedback.Foreground = Palette(error ? "Error" : "Muted");
            feedback.Visibility = Visibility.Visible;
        }
    }
}
