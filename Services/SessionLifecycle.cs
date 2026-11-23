using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        // Protected by the same lock as the PID map. A reused PID is never sufficient.
        private readonly Dictionary<string, DateTime> sessionStartTimes =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private bool restoredPlayerSessions;

        private string PlayerSessionDirectory { get { return Path.Combine(dataDir, "PlayerSessions"); } }

        private static string PlayerSessionAccountKey(AccountItem account)
        {
            if (account.UserId > 0) return "id:" + account.UserId.ToString(CultureInfo.InvariantCulture);
            return PlayerSessionUsernameKey(account);
        }

        private static string PlayerSessionUsernameKey(AccountItem account)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(account.Username.ToUpperInvariant())));
        }

        private string PlayerSessionPath(int pid, DateTime started)
        {
            return Path.Combine(PlayerSessionDirectory, string.Format(CultureInfo.InvariantCulture,
                "{0:D19}-{1}.session", started.Ticks, pid));
        }

        private void SavePlayerSession(AccountItem account, int pid, DateTime started)
        {
            string path = PlayerSessionPath(pid, started);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(PlayerSessionDirectory);
                File.WriteAllText(temporary, PlayerSessionAccountKey(account));
                File.Move(temporary, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { TryDeletePlayerSessionFile(temporary); }
        }

        private static void TryDeletePlayerSessionFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Call under the same lock used to add a confirmed process.
        private void ForgetPlayerSession(string username, int pid)
        {
            DateTime started;
            if (sessionStartTimes.TryGetValue(username, out started))
                TryDeletePlayerSessionFile(PlayerSessionPath(pid, started));
            runningAccountPids.Remove(username);
            sessionStartTimes.Remove(username);
        }

        private void RestorePlayerSessions()
        {
            if (restoredPlayerSessions || vaultLocked || accounts.Count == 0) return;
            string[] paths;
            try
            {
                paths = Directory.Exists(PlayerSessionDirectory)
                    ? Directory.GetFiles(PlayerSessionDirectory, "*.session") : new string[0];
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            var accountKeys = new Dictionary<string, AccountItem>(StringComparer.Ordinal);
            foreach (var account in accounts)
            {
                if (string.IsNullOrEmpty(account.Username)) continue;
                accountKeys[PlayerSessionAccountKey(account)] = account;
                accountKeys[PlayerSessionUsernameKey(account)] = account;
            }

            lock (runningAccountPids)
            {
                foreach (string path in paths.OrderByDescending(value => value, StringComparer.Ordinal))
                {
                    string[] identity = Path.GetFileNameWithoutExtension(path).Split('-');
                    long ticks;
                    int pid;
                    if (identity.Length != 2 ||
                        !long.TryParse(identity[0], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
                        ticks <= 0 || ticks > DateTime.MaxValue.Ticks ||
                        !int.TryParse(identity[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || pid <= 0)
                        continue;

                    var start = new DateTime(ticks, DateTimeKind.Utc);
                    using (var process = OpenRobloxProcess(pid, start))
                    {
                        if (process == null)
                        {
                            TryDeletePlayerSessionFile(path);
                            continue;
                        }
                    }

                    string key;
                    try { key = File.ReadAllText(path); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    AccountItem account;
                    if (!accountKeys.TryGetValue(key, out account) ||
                        runningAccountPids.ContainsKey(account.Username) || runningAccountPids.ContainsValue(pid))
                        continue;

                    runningAccountPids[account.Username] = pid;
                    sessionStartTimes[account.Username] = start;
                    account.LastLaunchTime = start;
                    account.Status = "Online";
                }
            }
            restoredPlayerSessions = true;
        }

        private int CountUnidentifiedPlayers(IEnumerable<AccountItem> running)
        {
            var known = new HashSet<int>();
            lock (runningAccountPids)
            {
                foreach (var account in running)
                {
                    int pid;
                    if (runningAccountPids.TryGetValue(account.Username, out pid)) known.Add(pid);
                }
            }

            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            try { return processes.Count(process => !known.Contains(process.Id)); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        private const string NeedsSignIn = "Precisa entrar novamente";
        private const string UnconfirmedSession = "Sessão não confirmada";


        private readonly Dictionary<int, ProcessPerformance> playerPerformance = new Dictionary<int, ProcessPerformance>();
        private readonly Dictionary<AccountItem, ProcessPerformance> accountPerformance = new Dictionary<AccountItem, ProcessPerformance>();
        private bool economicalSessions;
        private string primaryAccount = "";

        private bool IsPrimaryAccount(AccountItem account)
        {
            return !string.IsNullOrEmpty(primaryAccount) &&
                string.Equals(primaryAccount, account.Username, StringComparison.OrdinalIgnoreCase);
        }

        private void SamplePlayerPerformance()
        {
            uint foreground = ProcessPerformance.ForegroundPid();
            var current = new HashSet<int>();
            accountPerformance.Clear();
            foreach (var account in accounts)
            {
                int pid;
                Process opened;
                lock (runningAccountPids)
                {
                    if (!runningAccountPids.TryGetValue(account.Username, out pid)) continue;
                    opened = OpenTrackedProcess(account.Username, pid);
                }
                if (opened == null) continue;

                ProcessPerformance sample;
                if (playerPerformance.TryGetValue(pid, out sample) && sample.Started != opened.StartTime)
                {
                    sample.Dispose();
                    playerPerformance.Remove(pid);
                }
                if (!playerPerformance.TryGetValue(pid, out sample))
                {
                    sample = new ProcessPerformance(opened);
                    playerPerformance.Add(pid, sample);
                }
                else
                {
                    opened.Dispose();
                }

                try
                {
                    sample.Update(ProcessPerformance.ShouldEconomize(economicalSessions, foreground, pid, IsPrimaryAccount(account)));
                    current.Add(pid);
                    accountPerformance.Add(account, sample);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            foreach (int pid in playerPerformance.Keys.Where(pid => !current.Contains(pid)).ToArray())
            {
                playerPerformance[pid].Dispose();
                playerPerformance.Remove(pid);
            }
            DisplayPlayerPerformance();
        }

        private void RestorePlayerPerformance()
        {
            foreach (var sample in playerPerformance.Values) sample.Dispose();
            playerPerformance.Clear();
            accountPerformance.Clear();
        }

        private enum StopResult { Untracked, Stopped, Failed }

        private bool IsAccountRunning(AccountItem account)
        {
            if (account == null || string.IsNullOrEmpty(account.Username)) return false;
            lock (runningAccountPids)
            {
                int pid;
                if (!runningAccountPids.TryGetValue(account.Username, out pid)) return false;
                return IsLiveSession(account.Username, pid);
            }
        }

        private bool IsLiveSession(string username, int pid)
        {
            using (var process = OpenTrackedProcess(username, pid))
                return process != null;
        }

        private Process OpenTrackedProcess(string username, int pid)
        {
            DateTime start;
            return sessionStartTimes.TryGetValue(username, out start) ? OpenRobloxProcess(pid, start) : null;
        }

        private static Process OpenRobloxProcess(int pid, DateTime start)
        {
            Process process = null;
            try
            {
                process = Process.GetProcessById(pid);
                if (process.Handle != IntPtr.Zero && !process.HasExited && process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)
                    && process.StartTime.ToUniversalTime() == start)
                    return process;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            if (process != null) process.Dispose();
            return null;
        }

        private bool TrackStartedPlayer(AccountItem account, Process process, DateTime requestedUtc)
        {
            if (process == null) return false;
            try
            {
                if (process.HasExited || !process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                    return false;
                DateTime started = process.StartTime.ToUniversalTime();
                if (started < requestedUtc.AddSeconds(-1)) return false;
                lock (runningAccountPids)
                {
                    if (runningAccountPids.Any(entry => entry.Value == process.Id &&
                        !entry.Key.Equals(account.Username, StringComparison.OrdinalIgnoreCase))) return false;
                    runningAccountPids[account.Username] = process.Id;
                    sessionStartTimes[account.Username] = started;
                    SavePlayerSession(account, process.Id, started);
                }
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private StopResult StopTrackedSession(AccountItem account)
        {
            lock (runningAccountPids)
            {
                int pid;
                if (!runningAccountPids.TryGetValue(account.Username, out pid)) return StopResult.Untracked;
                using (var process = OpenTrackedProcess(account.Username, pid))
                {
                    if (process == null) return StopResult.Untracked;
                    try
                    {
                        process.Kill();
                        if (!process.WaitForExit(1000)) return StopResult.Failed;
                    }
                    catch (InvalidOperationException) { return StopResult.Failed; }
                    catch (System.ComponentModel.Win32Exception) { return StopResult.Failed; }
                }
                ForgetPlayerSession(account.Username, pid);
            }
            ClearSessionLogTracking(account.Username);
            account.CurrentGame = null;
            account.Status = "Offline";
            return StopResult.Stopped;
        }

        private void ClearSessionLogTracking(string username)
        {
            lock (accountLogFiles) accountLogFiles.Remove(username);
            lock (accountLogOffsets) accountLogOffsets.Remove(username);
            lock (accountLastTeleportTime) accountLastTeleportTime.Remove(username);
            lock (accountSuspectedDisconnectTime)
            {
                accountSuspectedDisconnectTime.Remove(username);
                accountSuspectedReason.Remove(username);
            }
        }

        private void CloseAccountRoblox(AccountItem account)
        {
            if (account == null) return;
            var result = StopTrackedSession(account);
            if (result != StopResult.Failed)
            {
                manualClosedAccounts.Add(account.Username);
                ClearSessionLogTracking(account.Username);
            }
            string status = result == StopResult.Stopped ? "Sessão encerrada para @" + account.Username + "." :
                result == StopResult.Untracked ? "Nenhuma sessão confirmada para @" + account.Username + "." :
                "Falha ao encerrar a sessão de @" + account.Username + ".";
            SetStatus(status);
            AddLog(status, result == StopResult.Stopped ? "SUCCESS" : "WARN", "PROCESS");
            RefreshAccountsUI();
        }

        private void CloseAllAccounts()
        {
            var targets = accounts.Where(IsAccountRunning).ToArray();
            if (targets.Length == 0)
            {
                SetStatus("Nenhuma sessão confirmada para encerrar.");
                return;
            }
            if (confirmSessionClose && System.Windows.MessageBox.Show(this,
                "Encerrar as " + targets.Length + " sessões vinculadas a este aplicativo?", "Encerrar sessões",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;
            foreach (var account in targets) CloseAccountRoblox(account);
        }

        private async Task<bool> LaunchGame(AccountItem account, string targetPlace, string targetJob, bool reconnect = false)
        {
            if (vaultLocked) return false;
            int authorization = vaultEpoch;
            string place = targetPlace ?? placeIdBox.Text.Trim();
            if (string.IsNullOrEmpty(place)) place = account.CurrentPlaceId ?? "";
            bool appOnly = place.Length == 0 || place == "0";
            string job = targetJob ?? jobIdBox.Text.Trim();
            if (appOnly) place = "";
            long numericPlace;
            if (!appOnly && (!long.TryParse(place, NumberStyles.None, CultureInfo.InvariantCulture, out numericPlace) || numericPlace <= 0))
            {
                SetStatus("Informe um Place ID numérico válido.");
                return false;
            }

            account.IsLaunching = true;
            RefreshAccountsUI();
            try
            {
                SetStatus("Autenticando @" + account.Username + "…");
                string cookie = account.Cookie;
                string ticket = string.IsNullOrEmpty(cookie) ? null : await Task.Run(() => GetRobloxAuthTicket(cookie, place));
                if (vaultLocked || authorization != vaultEpoch || !accounts.Contains(account) || (reconnect && !CanReconnect(account))) return false;
                if (string.IsNullOrEmpty(ticket))
                {
                    account.Status = NeedsSignIn;
                    string message = "Não foi possível autenticar @" + account.Username + ". Entre novamente ou verifique a conexão.";
                    SetStatus(message);
                    AddLog(message, "WARN", "ROBLOX");
                    return false;
                }

                long time = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                int tracker = new Random().Next(100000000, 999999999);
                string protocol = string.Format("roblox-player:1+launchmode:{0}+gameinfo:{1}+launchtime:{2}",
                    appOnly ? "app" : "play", ticket, time);
                if (!appOnly)
                {
                    string url = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=" +
                        (string.IsNullOrEmpty(job) ? "RequestGame" : "RequestGameJob") +
                        "&browserTrackerId=" + tracker + "&placeId=" + place + "&isPlayTogetherGame=false";
                    if (!string.IsNullOrEmpty(job)) url += "&gameId=" + Uri.EscapeDataString(job);
                    protocol += "+placelauncherurl:" + Uri.EscapeDataString(url);
                }
                protocol += "+browsertrackerid:" + tracker + "+robloxLocale:pt_br+gameLocale:pt_br+channel:+LaunchExp:InApp";

                DateTime requested = DateTime.UtcNow;
                bool tracked;
                using (var process = Process.Start(new ProcessStartInfo(protocol) { UseShellExecute = true }))
                    tracked = TrackStartedPlayer(account, process, requested);

                account.LastLaunchTime = requested;
                account.CurrentGame = GetGameName(place);
                account.CurrentPlaceId = place;
                account.CurrentJobId = appOnly ? "" : job;
                account.Status = tracked ? "Online" : UnconfirmedSession;
                manualClosedAccounts.Remove(account.Username);
                ClearSessionLogTracking(account.Username);
                bool saved = SaveAccounts();

                string status = tracked ? "Sessão vinculada a @" + account.Username + "." :
                    "Solicitação enviada para @" + account.Username + ". O processo ainda não foi confirmado.";
                if (saved) SetStatus(status);
                AddLog(status, tracked ? "SUCCESS" : "WARN", "ROBLOX");
                return true;
            }
            finally
            {
                account.IsLaunching = false;
                RefreshAccountsUI();
            }
        }
    }
}
