using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RobloxMultiByJayy
{
    internal static class WebViewLogin
    {
        private const string LoginUrl = "https://www.roblox.com/login";
        private const string CookieName = ".ROBLOSECURITY";
        private static readonly string[] OverrideVariables =
        {
            "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_USER_DATA_FOLDER",
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_RELEASE_CHANNELS",
            "WEBVIEW2_CHANNEL_SEARCH_KIND", "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE",
            "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER"
        };

        internal static bool IsAllowedNavigation(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
                (uri.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("auth.roblox.com", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsSessionCookie(string name, string domain, string path, bool secure, bool httpOnly)
        {
            return name == CookieName && path == "/" && secure && httpOnly &&
                (string.Equals(domain, ".roblox.com", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(domain, "roblox.com", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(domain, "www.roblox.com", StringComparison.OrdinalIgnoreCase));
        }

        internal static async Task<string> RunAsync(Window owner, string dataDir)
        {
            if (OverrideVariables.Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))))
                throw new InvalidOperationException("O WebView2 tem substituições de ambiente ativas.");

            string root = Path.GetFullPath(Path.Combine(dataDir, "WebViewLogin"));
            string profile = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var browser = new WebView2();
            var tip = new TextBlock
            {
                Text = "Entre na página oficial. O launcher guardará a sessão no cofre local.",
                Margin = new Thickness(14, 10, 14, 10), TextWrapping = TextWrapping.Wrap
            };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(10), Padding = new Thickness(14, 4, 14, 4) };
            var bar = new DockPanel();
            DockPanel.SetDock(cancel, Dock.Right);
            bar.Children.Add(cancel);
            bar.Children.Add(tip);
            var layout = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            layout.Children.Add(bar);
            layout.Children.Add(browser);
            var login = new Window
            {
                Title = "Entrar com o Roblox", Owner = owner, Content = layout, Width = 820, Height = 690,
                MaxWidth = SystemParameters.WorkArea.Width, MaxHeight = SystemParameters.WorkArea.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
                Background = owner.Background, Foreground = owner.Foreground
            };

            using (var stopped = new CancellationTokenSource())
            {
                stopped.CancelAfter(TimeSpan.FromMinutes(12));
                string cookie = null;
                Exception failure = null;
                Task work = null;
                var browserExited = new TaskCompletionSource<bool>();
                CoreWebView2Environment environment = null;
                login.Closed += (sender, args) => stopped.Cancel();
                login.PreviewKeyDown += (sender, args) =>
                {
                    if (args.Key == Key.Escape) { args.Handled = true; login.Close(); }
                };
                login.Loaded += (sender, args) =>
                {
                    if (work == null) work = WatchAsync(browser, profile, stopped.Token, value => cookie = value, error => failure = error,
                        env =>
                        {
                            environment = env;
                            env.BrowserProcessExited += (who, ev) => browserExited.TrySetResult(true);
                        },
                        () => { if (login.IsVisible) login.Close(); });
                };
                try
                {
                    login.ShowDialog();
                    if (work != null) await work;
                }
                catch (Exception error) { failure = error; }

                try
                {
                    var core = browser.CoreWebView2;
                    if (core != null)
                    {
                        core.Stop();
                        core.CookieManager.DeleteAllCookies();
                        var clear = core.Profile.ClearBrowsingDataAsync();
                        if (await Task.WhenAny(clear, Task.Delay(6000)) == clear) await clear;
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.Runtime.InteropServices.COMException) { }
                finally
                {
                    layout.Children.Remove(browser);
                    browser.Dispose();
                }

                if (environment != null && Directory.Exists(profile))
                    await Task.WhenAny(browserExited.Task, Task.Delay(10000));

                if (!await DeleteProfileAsync(root, profile))
                    throw new IOException("Não foi possível limpar o perfil temporário de login.");
                if (failure != null) throw failure;
                return cookie;
            }
        }

        private static async Task WatchAsync(WebView2 browser, string profile, CancellationToken stopped,
            Action<string> onCookie, Action<Exception> onError, Action<CoreWebView2Environment> onEnvironment, Action close)
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions
                {
                    ExclusiveUserDataFolderAccess = true,
                    AreBrowserExtensionsEnabled = false
                };
                var env = await CoreWebView2Environment.CreateAsync(null, profile, options);
                if (!string.Equals(Path.GetFullPath(env.UserDataFolder), profile, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("O WebView2 redirecionou a pasta de login.");
                stopped.ThrowIfCancellationRequested();

                onEnvironment(env);
                await browser.EnsureCoreWebView2Async(env);
                stopped.ThrowIfCancellationRequested();
                var core = browser.CoreWebView2;
                var settings = core.Settings;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsWebMessageEnabled = false;
                settings.AreHostObjectsAllowed = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                core.NavigationStarting += (sender, args) => args.Cancel = !IsAllowedNavigation(args.Uri);
                core.NewWindowRequested += (sender, args) => args.Handled = true;
                core.DownloadStarting += (sender, args) => args.Cancel = true;
                core.LaunchingExternalUriScheme += (sender, args) => args.Cancel = true;
                core.PermissionRequested += (sender, args) => args.State = CoreWebView2PermissionState.Deny;
                core.Navigate(LoginUrl);

                while (true)
                {
                    await Task.Delay(700, stopped);
                    var cookies = await core.CookieManager.GetCookiesAsync(LoginUrl);
                    stopped.ThrowIfCancellationRequested();
                    var session = cookies.FirstOrDefault(item => (item.IsSession || item.Expires.ToUniversalTime() > DateTime.UtcNow) && !string.IsNullOrEmpty(item.Value) &&
                        IsSessionCookie(item.Name, item.Domain, item.Path, item.IsSecure, item.IsHttpOnly));
                    if (session == null) continue;
                    onCookie(session.Value);
                    break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { onError(error); }
            finally { close(); }
        }

        internal static async Task<bool> DeleteProfileAsync(string root, string profile)
        {
            var location = Path.GetFullPath(profile);
            Guid id;
            if (!string.Equals(Path.GetDirectoryName(location), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(location), "N", out id))
                return false;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(location)) Directory.Delete(location, true);
                    return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                await Task.Delay(250);
            }
            return false;
        }
    }
}
