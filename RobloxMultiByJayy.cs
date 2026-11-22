using System;



using System.IO;



using System.Collections.Generic;



using System.Diagnostics;



using System.Threading;



using System.Threading.Tasks;



using System.Windows;



using System.Windows.Controls;



using System.Windows.Input;



using System.Windows.Media;



using System.Windows.Media.Imaging;



using System.Windows.Shapes;



using System.Windows.Threading;



using System.Net;



using System.Net.NetworkInformation;







using System.Security.Cryptography;



using System.Runtime.InteropServices;



using System.Text;



using System.Text.RegularExpressions;



using Path = System.IO.Path;



namespace RobloxMultiByJayy



{



    public class AccountItem



    {



        public string Username { get; set; }



        public string Alias { get; set; }



        public string Cookie { get; set; }



        public string Password { get; set; }



        public long UserId { get; set; }



        public string CurrentGame { get; set; }



        public string Status { get; set; }



        public bool IsLaunching { get; set; }



        public DateTime LastLaunchTime { get; set; }



        public bool AutoReconnect { get; set; }



        public string CurrentPlaceId { get; set; }



        public string CurrentJobId { get; set; }



        public AccountItem()



        {



            AutoReconnect = true;



        }



    }



    public class LogEntry



    {



        public DateTime Timestamp { get; set; }



        public string Level { get; set; }



        public string Source { get; set; }



        public string Message { get; set; }



    }



    public class App : Application



    {



        [STAThread]



        public static void Main()



        {



            App app = new App();



            app.Run(new MainWindow());



        }



    }



    public partial class MainWindow : Window



    {



        // Variáveis de Controle e Estado (preservadas conforme especificação)











        private List<AccountItem> accounts = new List<AccountItem>();



        private AccountItem selectedAccount = null;



        private TextBox placeIdBox;



        private TextBox jobIdBox;




        private TextBlock statusText;



        private string appDir;
        private string dataDir;



        // Borda Estilizada Dark (RGB Removido)



        // Chromium Auto-Login Cancellation







        // Gerenciamento Dinâmico de Instâncias Ativas por Conta e Cache de Avatares



        private readonly Dictionary<string, int> runningAccountPids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);        // Sistema de Auto-Reconexão Passiva via Logs do Roblox (Sem DLL / Sem Injeção)



        private DispatcherTimer logWatcherTimer;



        private static DateTime lastInternetCheckTime = DateTime.MinValue;



        private static bool cachedInternetStatus = true;



        public static bool IsInternetConnected()



        {



            if ((DateTime.UtcNow - lastInternetCheckTime).TotalSeconds < 2.5)



            {



                return cachedInternetStatus;



            }



            bool status = CheckInternetDirect();



            cachedInternetStatus = status;



            lastInternetCheckTime = DateTime.UtcNow;



            return status;



        }



        private static bool CheckInternetDirect()



        {



            try



            {



                if (!NetworkInterface.GetIsNetworkAvailable()) return false;



                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");



                req.Timeout = 2000;



                req.ReadWriteTimeout = 2000;



                req.Method = "GET";



                req.UserAgent = "RobloxMultiByJayy";



                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())



                {



                    return (resp.StatusCode == HttpStatusCode.OK);



                }



            }



            catch



            {



                try



                {



                    HttpWebRequest req2 = (HttpWebRequest)WebRequest.Create("https://www.roblox.com");



                    req2.Timeout = 2500;



                    req2.ReadWriteTimeout = 2500;



                    req2.Method = "HEAD";



                    req2.UserAgent = "Roblox/WinInet";



                    using (HttpWebResponse resp2 = (HttpWebResponse)req2.GetResponse())



                    {



                        return (resp2.StatusCode == HttpStatusCode.OK);



                    }



                }



                catch



                {



                    return false;



                }



            }



        }



        private bool autoReconnectEnabled = true;



        private int autoReconnectIntervalSec = 4;



        private int autoReconnectCooldownSec = 8;
        private int autoReconnectGraceSec = 20;
        private bool reconnectAllOnSingleDrop = true;
        private bool isReconnectingAll = false;
        private readonly Dictionary<string, DateTime> accountSuspectedDisconnectTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> accountSuspectedReason = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> accountLastJoinSuccessTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> accountLastTeleportLogTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);



        private readonly Dictionary<string, string> accountLogFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);



        private readonly Dictionary<string, long> accountLogOffsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);



        private readonly Dictionary<string, DateTime> accountLastTeleportTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);



        private readonly HashSet<string> manualClosedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



        private readonly HashSet<string> reconnectingAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim reconnectLaunchLock = new SemaphoreSlim(1, 1);



        private readonly Dictionary<string, ImageSource> avatarCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);



        private static readonly Regex RegexJoiningGame = new Regex(@"!\s*Joining game\s*'([^']+)'\s*place\s*(\d+)", RegexOptions.IgnoreCase);



        private static readonly Regex RegexGameJoinLoad = new Regex(@"placeid:(\d+).*?universeid:(\d+)", RegexOptions.IgnoreCase);



        private static readonly Regex RegexPlaceFallback = new Regex(@"placeid[:=\s]+(\d+)", RegexOptions.IgnoreCase);



        private readonly HashSet<string> resolvingPlaces = new HashSet<string>();



        private readonly Dictionary<string, string> placeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> placeThumbnails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> fetchingThumbnails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DiscordRpcManager discordRpc;
        private bool showDiscordPresence = true;
        private const string DefaultDiscordClientId = "1563760922189631623";
        private string discordClientId = DefaultDiscordClientId;
        private string activeGameSessionPlaceId = null;
        private DateTime? activeGameSessionStartTime = null;
        private int discordPresenceCounter = 0;



        // Histórico dos eventos do aplicativo.

        private readonly List<LogEntry> logsList = new List<LogEntry>();



        private DispatcherTimer resourceTimer;

        [StructLayout(LayoutKind.Sequential)]



        public struct MEMORYSTATUSEX



        {



            public uint dwLength;



            public uint dwMemoryLoad;



            public ulong ullTotalPhys;



            public ulong ullAvailPhys;



            public ulong ullTotalPageFile;



            public ulong ullAvailPageFile;



            public ulong ullTotalVirtual;



            public ulong ullAvailVirtual;



            public ulong ullAvailExtendedVirtual;



        }



        [DllImport("kernel32.dll", SetLastError = true)]



        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);



        public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox Multi by Jayy", "Public"))
        {
        }

        internal MainWindow(string dataFolder)
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            dataDir = dataFolder;

            var initialSettings = LoadSettingsFromIni();
            showDiscordPresence = ReadFlag(initialSettings, "ShowPresence", false);
            economicalSessions = ReadFlag(initialSettings, "EconomicalSessions", false);
            primaryAccount = ReadSetting(initialSettings, "PrimaryPerformanceAccount", "");
            confirmSessionClose = ReadFlag(initialSettings, "ConfirmSessionClose", true);
            int storedDelay;
            if (int.TryParse(ReadSetting(initialSettings, "AccountJoinDelayMS", ""), out storedDelay)
                && storedDelay >= 100 && storedDelay <= 60000)
                launchDelayMs = storedDelay;
            string storedClient = ReadSetting(initialSettings, "DiscordClientId", "");
            if (!string.IsNullOrEmpty(storedClient)) discordClientId = storedClient;
            if (showDiscordPresence)
            {
                SetDiscordStatus("Conectando ao Discord…", false);
                discordRpc = MakeDiscordManager();
            }



            Width = 1260;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;



            LoadAllAccounts();
            Content = BuildModernWindow();



            lock (placeNames)



            {



                placeNames[""] = "Roblox App";



                placeNames["0"] = "Roblox App";



                placeNames["74629631798007"] = "Pets Universe";



                placeNames["2753915549"] = "Blox Fruits";



                placeNames["142823291"] = "Murder Mystery 2";



                placeNames["107778070777162"] = "Steal an Egg";



                placeNames["4924922222"] = "Brookhaven";



                placeNames["13772394625"] = "Blade Ball";



                placeNames["286090429"] = "Arsenal";



            }



            autoReconnectEnabled = ReadFlag(initialSettings, "AutoReconnectEnabled", true);
            reconnectAllOnSingleDrop = ReadFlag(initialSettings, "ReconnectAllOnSingleDrop", false);
            int storedSeconds;
            if (int.TryParse(ReadSetting(initialSettings, "AutoReconnectIntervalSec", ""), out storedSeconds) && storedSeconds >= 1)
                autoReconnectIntervalSec = storedSeconds;
            if (int.TryParse(ReadSetting(initialSettings, "AutoReconnectCooldownSec", ""), out storedSeconds) && storedSeconds >= 1)
                autoReconnectCooldownSec = storedSeconds;
            if (int.TryParse(ReadSetting(initialSettings, "AutoReconnectGraceSec", ""), out storedSeconds) && storedSeconds >= 1)
                autoReconnectGraceSec = storedSeconds;

            LoadInitialLogs();
            StartResourceMonitor();



            StartAutoReconnectWatcher();



            // Registrar a inicialização nos Logs



            AddLog("Roblox Multi by Jayy inicializado com sucesso.", "INFO", "SYSTEM");
            if (!string.IsNullOrEmpty(accountStoreNotice))
            {
                SetStatus(accountStoreNotice);
                AddLog(accountStoreNotice, "WARN", "ACCOUNTS");
            }




            try
            {
                UpdateDiscordPresence();
            }
            catch { }



        }



        private void StartResourceMonitor()



        {



            resourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };



            resourceTimer.Tick += (s, e) => { UpdateResourceUsage(); SamplePlayerPerformance(); CheckVaultIdle(); };



            resourceTimer.Start();



            UpdateResourceUsage();



        }



        private void UpdateResourceUsage()



        {



            try



            {



                // 1. Uso de RAM real via Win32 API



                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();



                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));



                if (GlobalMemoryStatusEx(ref mem))



                {



                    double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                    memoryTotal.Text = string.Format("RAM do sistema {0:0.0} GB", usedGb);

                }



                // 2. Limpar processos finalizados e verificar contas ativas



                lock (runningAccountPids)



                {



                    List<string> dead = new List<string>();



                    foreach (var kvp in runningAccountPids)



                    {



                        if (!IsLiveSession(kvp.Key, kvp.Value)) dead.Add(kvp.Key);

                    }



                    foreach (string d in dead)



                    {



                        int deadPid = 0;



                        runningAccountPids.TryGetValue(d, out deadPid);



                        runningAccountPids.Remove(d);
                        sessionStartTimes.Remove(d);



                        AccountItem closedAcc = accounts.Find(x => x.Username.Equals(d, StringComparison.OrdinalIgnoreCase));



                        string closedGame = (closedAcc != null && !string.IsNullOrEmpty(closedAcc.CurrentGame)) ? closedAcc.CurrentGame : "Roblox";



                        bool wasInReconnect = false;



                        lock (reconnectingAccounts)



                        {



                            wasInReconnect = reconnectingAccounts.Contains(d);



                        }



                        // Se a conta já está no meio do processo de auto-reconexão, NÃO reseta nem interfere!



                        if (wasInReconnect)



                        {



                            continue;



                        }



                        bool wasManual = manualClosedAccounts.Contains(d);



                        if (wasManual)



                        {



                            manualClosedAccounts.Remove(d);



                            accountLogFiles.Remove(d);



                            accountLogOffsets.Remove(d);



                            accountLastTeleportTime.Remove(d);



                            if (closedAcc != null) closedAcc.CurrentGame = null;



                            AddLog(string.Format("Conta @{0} encerrou o Roblox pelo launcher (PID {1} | Jogo: {2})", d, deadPid, closedGame), "INFO", "ROBLOX");



                            continue;



                        }



                        // Checar se o encerramento foi causado por queda de internet, erro de conexão ou crash

                        bool isNetDown = !IsInternetConnected();

                        bool isStartupCrash = (closedAcc != null && (DateTime.UtcNow - closedAcc.LastLaunchTime).TotalSeconds < 25);

                        bool isDisconnectOrCrash = false;

                        string detectedReason = "Desconexão detectada nos logs";



                        string logPath = null;
                        lock (accountLogFiles)
                        {
                            accountLogFiles.TryGetValue(d, out logPath);
                        }


                        if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))

                        {

                            try

                            {

                                using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))

                                {

                                    long len = fs.Length;

                                    long rStart = Math.Max(0, len - 65536);

                                    fs.Seek(rStart, SeekOrigin.Begin);

                                    byte[] b = new byte[len - rStart];

                                    int r = fs.Read(b, 0, b.Length);

                                    if (r > 0)

                                    {

                                        string tail = Encoding.UTF8.GetString(b, 0, r);



                                        // Verificar padrões específicos de erro/desconexão

                                        if (tail.IndexOf("Lost connection", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Perda de conexão com o servidor (Lost connection)";

                                        }

                                        else if (tail.IndexOf("Disconnected from game", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Disconnected from server", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Client has been disconnected", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Sessão desconectada da partida";

                                        }

                                        else if (tail.IndexOf("1025", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("10051", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("HttpError: ConnectFail", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("HttpError: DnsResolve", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Recv failure", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Failed ws recv", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Could not resolve host", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Erro de conexão / Queda de rede (Erro 1025 / DNS)";

                                        }

                                        else if (tail.IndexOf("You have been kicked", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Jogador expulso da partida";

                                        }

                                        else if (tail.IndexOf("This game has been disconnected", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Desconexão detectada (Erro 264)";

                                        }

                                        else if (tail.IndexOf("Error code 277", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Error code 268", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Error code 279", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Erro de conexão detectado nos logs";

                                        }

                                        else if (tail.IndexOf("Client:Disconnect", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                 tail.IndexOf("Destroying MegaReplicator", StringComparison.OrdinalIgnoreCase) >= 0)

                                        {

                                            isDisconnectOrCrash = true;

                                            detectedReason = "Desconexão do cliente Roblox";

                                        }

                                        else

                                        {

                                            // Se NÃO houve erro de rede, verificar se foi fechamento limpo voluntário pelo usuário

                                            bool isCleanUserExit = tail.IndexOf("destroyLuaApp", StringComparison.OrdinalIgnoreCase) >= 0 ||

                                                                   tail.IndexOf("Tearing down.", StringComparison.OrdinalIgnoreCase) >= 0;

                                            if (!isCleanUserExit)

                                            {

                                                isDisconnectOrCrash = true;

                                                detectedReason = "Encerramento inesperado do Roblox (Crash)";

                                            }

                                        }

                                    }

                                }

                            }

                            catch { }

                        }

                        else

                        {

                            if (isNetDown)

                            {

                                isDisconnectOrCrash = true;

                                detectedReason = "Queda de conexão com a internet";

                            }

                        }



                        if (autoReconnectEnabled && closedAcc != null && closedAcc.AutoReconnect && (isNetDown || isStartupCrash || isDisconnectOrCrash))
                        {
                            lock (reconnectingAccounts)
                            {
                                if (!reconnectingAccounts.Contains(closedAcc.Username) && !isReconnectingAll)
                                {
                                    string reason = isNetDown ? "Queda de conexão com a internet detectada" :
                                                    (isStartupCrash ? "Falha na inicialização do Roblox" : detectedReason);

                                    lock (accountSuspectedDisconnectTime)
                                    {
                                        if (!accountSuspectedDisconnectTime.ContainsKey(closedAcc.Username))
                                        {
                                            accountSuspectedDisconnectTime[closedAcc.Username] = DateTime.UtcNow;
                                            accountSuspectedReason[closedAcc.Username] = reason;
                                            Dispatcher.Invoke(() =>
                                            {
                                                AddLog(string.Format("⏳ [AUTO-RECONNECT] Processo/queda do Roblox detectado na conta @{0} ({1}). Aguardando {2}s de tolerância para verificar se o jogo reconecta...", closedAcc.Username, reason, autoReconnectGraceSec), "WARN", "RECONNECT");
                                            });
                                        }
                                    }
                                }
                            }
                        }

                        else

                        {

                            accountLogFiles.Remove(d);

                            accountLogOffsets.Remove(d);

                            accountLastTeleportTime.Remove(d);

                            if (closedAcc != null) closedAcc.CurrentGame = null;



                            AddLog(string.Format("Roblox fechado na conta @{0} (PID {1} finalizado | Jogo: {2})", d, deadPid, closedGame), "INFO", "ROBLOX");

                        }

                    }

                }






                UpdateModernAccounts();


            }



            catch { }



        }



        private void AddLog(string message, string level = "INFO", string source = "SYSTEM")



        {



            LogEntry entry = new LogEntry



            {



                Timestamp = DateTime.Now,



                Level = level,



                Source = source,



                Message = VaultProtection.Redact(message)



            };



            lock (logsList)



            {



                logsList.Add(entry);



                if (logsList.Count > LogHistoryLimit) logsList.RemoveAt(0);



            }



            try



            {



                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] [{2}] {3}", entry.Timestamp, entry.Level, entry.Source, entry.Message);



                Directory.CreateDirectory(dataDir);
                File.AppendAllText(Path.Combine(dataDir, "log.txt"), line + Environment.NewLine);



            }



            catch { }



            if (Dispatcher.CheckAccess()) RefreshModernLogs();
            else Dispatcher.BeginInvoke(new Action(RefreshModernLogs));
        }


        private const int LogHistoryLimit = 600;
        private static readonly Regex SavedLogPattern = new Regex(@"^\[(?<time>[^\]]+)\] \[(?<level>[^\]]+)\] \[(?<source>[^\]]+)\]\s*(?<message>.*)$");

        private void LoadInitialLogs()
        {
            string path = Path.Combine(dataDir, "log.txt");
            try
            {
                if (!File.Exists(path)) return;
                var recent = new Queue<string>(LogHistoryLimit);
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (recent.Count == LogHistoryLimit) recent.Dequeue();
                    recent.Enqueue(line);
                }
                lock (logsList)
                {
                    foreach (string line in recent)
                    {
                        Match match = SavedLogPattern.Match(line);
                        DateTime timestamp;
                        if (!match.Success || !DateTime.TryParse(match.Groups["time"].Value, out timestamp))
                        {
                            logsList.Add(new LogEntry { Timestamp = DateTime.Now, Level = "INFO", Source = "SYSTEM", Message = VaultProtection.Redact(line) });
                            continue;
                        }

                        logsList.Add(new LogEntry
                        {
                            Timestamp = timestamp, Level = match.Groups["level"].Value,
                            Source = match.Groups["source"].Value, Message = VaultProtection.Redact(match.Groups["message"].Value)
                        });
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }




        private string GetGameName(string placeId)



        {



            if (string.IsNullOrEmpty(placeId) || placeId == "0") return "Roblox (Sem Jogo)";



            lock (placeNames)



            {



                string name;



                if (placeNames.TryGetValue(placeId, out name)) return name;



            }



            ResolveGameNameAsync(placeId, null, null);



            return "Jogo: " + placeId;



        }



        private void ResolveGameNameAsync(string placeId, string universeId = null, AccountItem targetAcc = null)



        {



            if (string.IsNullOrEmpty(placeId) || placeId == "0") return;



            lock (resolvingPlaces)



            {



                if (resolvingPlaces.Contains(placeId)) return;



                resolvingPlaces.Add(placeId);



            }



            Task.Run(async () =>



            {



                try



                {



                    string resolvedName = null;



                    using (WebClient wc = new WebClient())



                    {



                        wc.Encoding = Encoding.UTF8;



                        wc.Headers.Add("User-Agent", "Roblox/WinInet");



                        string targetUniv = universeId;



                        if (string.IsNullOrEmpty(targetUniv))



                        {



                            string uJson = await wc.DownloadStringTaskAsync("https://apis.roblox.com/universes/v1/places/" + placeId + "/universe");



                            int uIdx = uJson.IndexOf("\"universeId\":");



                            if (uIdx != -1)



                            {



                                int uStart = uIdx + 13;



                                int uEnd = uJson.IndexOfAny(new char[] { ',', '}' }, uStart);



                                if (uEnd != -1)



                                {



                                    targetUniv = uJson.Substring(uStart, uEnd - uStart).Trim();



                                }



                            }



                        }



                        if (!string.IsNullOrEmpty(targetUniv))



                        {



                            string gJson = await wc.DownloadStringTaskAsync("https://games.roblox.com/v1/games?universeIds=" + targetUniv);



                            int nIdx = gJson.IndexOf("\"name\":\"");



                            if (nIdx != -1)



                            {



                                int nStart = nIdx + 8;



                                int nEnd = gJson.IndexOf("\"", nStart);



                                if (nEnd != -1)



                                {



                                    resolvedName = gJson.Substring(nStart, nEnd - nStart);



                                    try



                                    {



                                        resolvedName = System.Text.RegularExpressions.Regex.Unescape(resolvedName);



                                    }



                                    catch { }



                                }



                            }



                        }



                    }



                    if (!string.IsNullOrEmpty(resolvedName))



                    {



                        lock (placeNames)



                        {



                            placeNames[placeId] = resolvedName;
                        FetchGameThumbnailAsync(placeId);



                        }



                        Dispatcher.Invoke(() =>



                        {



                            if (targetAcc != null && (targetAcc.CurrentPlaceId == placeId || string.IsNullOrEmpty(targetAcc.CurrentPlaceId)))



                            {



                                targetAcc.CurrentPlaceId = placeId;



                                targetAcc.CurrentGame = resolvedName;



                                AddLog(string.Format("🎮 [JOGO DETECTADO] Conta @{0} está jogando '{1}' (Place ID: {2})", targetAcc.Username, resolvedName, placeId), "INFO", "GAME");



                                SaveAccounts();



                            }



                            RefreshAccountsUI();



                        });



                    }



                }



                catch { }



                finally



                {



                    lock (resolvingPlaces)



                    {



                        resolvingPlaces.Remove(placeId);



                    }



                }



            });



        }







        // MONITORAMENTO DE STATUS E AVATARES REAIS DO ROBLOX



        // =========================================================================



        private void LoadAccountAvatar(AccountItem acc, Image avatarImg, TextBlock fallbackText)



        {



            if (acc == null || string.IsNullOrEmpty(acc.Username)) return;



            // 1. Cache volátil estritamente em memória RAM (zero arquivos no disco)



            lock (avatarCache)



            {



                ImageSource cached;



                if (avatarCache.TryGetValue(acc.Username, out cached))



                {



                    avatarImg.Source = cached;



                    avatarImg.Visibility = Visibility.Visible;



                    fallbackText.Visibility = Visibility.Collapsed;



                    return;



                }



            }



            // 2. Obter a foto diretamente da API do Roblox em segundo plano sem gravar nada no disco



            Task.Run(async () =>



            {



                try



                {



                    long targetUserId = acc.UserId;



                    // Se não tiver o UserId em cache, tentar extrair do Cookie



                    if (targetUserId <= 0 && !string.IsNullOrEmpty(acc.Cookie))



                    {



                        try



                        {



                            int uidIdx = acc.Cookie.IndexOf("uid", StringComparison.OrdinalIgnoreCase);



                            if (uidIdx != -1)



                            {



                                int numStart = -1;



                                for (int i = uidIdx + 3; i < Math.Min(acc.Cookie.Length, uidIdx + 45); i++)



                                {



                                    if (char.IsDigit(acc.Cookie[i]))



                                    {



                                        if (numStart == -1) numStart = i;



                                    }



                                    else if (numStart != -1)



                                    {



                                        long parsed;



                                        if (long.TryParse(acc.Cookie.Substring(numStart, i - numStart), out parsed) && parsed > 1000)



                                        {



                                            targetUserId = parsed;



                                            acc.UserId = parsed;



                                            break;



                                        }



                                        numStart = -1;



                                    }



                                }



                            }



                        }



                        catch { }



                    }



                    // Se ainda não tiver o UserId, buscar via API oficial de usuários



                    if (targetUserId <= 0)



                    {



                        using (WebClient wc = new WebClient())



                        {



                            wc.Headers[HttpRequestHeader.ContentType] = "application/json";



                            string reqBody = "{\"usernames\":[\"" + acc.Username + "\"],\"excludeBannedUsers\":false}";



                            string userJson = await wc.UploadStringTaskAsync("https://users.roblox.com/v1/usernames/users", "POST", reqBody);



                            int idIdx = userJson.IndexOf("\"id\":");



                            if (idIdx != -1)



                            {



                                int idStart = idIdx + 5;



                                int idEnd = userJson.IndexOfAny(new char[] { ',', '}' }, idStart);



                                if (idEnd != -1)



                                {



                                    long.TryParse(userJson.Substring(idStart, idEnd - idStart).Trim(), out targetUserId);



                                    acc.UserId = targetUserId;



                                }



                            }



                        }



                    }



                    if (targetUserId <= 0) return;



                    // Obter a URL da foto de perfil diretamente dos servidores CDN do Roblox



                    string thumbUrl = null;



                    using (WebClient wc = new WebClient())



                    {



                        wc.Encoding = Encoding.UTF8;



                        string thumbJson = await wc.DownloadStringTaskAsync(string.Format("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={0}&size=420x420&format=Png&isCircular=false", targetUserId));



                        int imgIdx = thumbJson.IndexOf("\"imageUrl\":\"");



                        if (imgIdx != -1)



                        {



                            int imgStart = imgIdx + 12;



                            int imgEnd = thumbJson.IndexOf("\"", imgStart);



                            if (imgEnd != -1)



                            {



                                thumbUrl = thumbJson.Substring(imgStart, imgEnd - imgStart);



                            }



                        }



                    }



                    if (string.IsNullOrEmpty(thumbUrl)) return;



                    // Carregar os bytes da imagem diretamente em memória RAM (MemoryStream) - ZERO arquivos salvos no sistema



                    byte[] imgBytes;



                    using (WebClient wc = new WebClient())



                    {



                        imgBytes = await wc.DownloadDataTaskAsync(new Uri(thumbUrl));



                    }



                    if (imgBytes != null && imgBytes.Length > 0)



                    {



                        Dispatcher.Invoke(() =>



                        {



                            try



                            {



                                using (MemoryStream ms = new MemoryStream(imgBytes))



                                {



                                    BitmapImage bi = new BitmapImage();



                                    bi.BeginInit();



                                    bi.StreamSource = ms;



                                    bi.CacheOption = BitmapCacheOption.OnLoad;



                                    bi.EndInit();



                                    bi.Freeze();



                                    lock (avatarCache)



                                    {



                                        avatarCache[acc.Username] = bi;



                                    }



                                    avatarImg.Source = bi;



                                    avatarImg.Visibility = Visibility.Visible;



                                    fallbackText.Visibility = Visibility.Collapsed;



                                }



                            }



                            catch { }



                        });



                    }



                }



                catch { }



            });



        }



        private void RefreshAccountsUI()
        {
            if (Dispatcher.CheckAccess()) UpdateModernAccounts();
            else Dispatcher.BeginInvoke(new Action(UpdateModernAccounts));
        }


        private void SetStatus(string msg)
        {
            if (Dispatcher.CheckAccess())
            {
                if (statusText != null)
                    statusText.Text = "● " + msg;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    if (statusText != null)
                        statusText.Text = "● " + msg;
                });
            }
        }



        protected override void OnClosed(EventArgs e)
        {
            if (resourceTimer != null) resourceTimer.Stop();
            RestorePlayerPerformance();
            if (logWatcherTimer != null) logWatcherTimer.Stop();
            if (accountStore != null) accountStore.Lock();
            foreach (var account in accounts) account.Cookie = null;
            if (discordRpc != null) discordRpc.Dispose();
            base.OnClosed(e);
        }


        private Dictionary<string, string> LoadSettingsFromIni()



        {



            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);



            try



            {



                string iniPath = Path.Combine(dataDir, "RAMSettings.ini");



                if (File.Exists(iniPath))



                {



                    foreach (string line in File.ReadAllLines(iniPath))



                    {



                        string trimmed = line.Trim();



                        if (trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.StartsWith("[")) continue;



                        int eq = trimmed.IndexOf('=');



                        if (eq > 0)



                        {



                            string key = trimmed.Substring(0, eq).Trim();



                            string val = trimmed.Substring(eq + 1).Trim();



                            dict[key] = val;



                        }



                    }



                }



            }



            catch { }



            return dict;



        }



        private bool SaveSettingsToIni(Dictionary<string, string> newSettings)
        {
            string ini = Path.Combine(dataDir, "RAMSettings.ini");
            string temporary = ini + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var pending = new Dictionary<string, string>(newSettings, StringComparer.OrdinalIgnoreCase);
                bool exists = File.Exists(ini);
                var lines = exists ? new List<string>(File.ReadAllLines(ini)) : new List<string> { "[General]" };
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator).Trim();
                    string value;
                    if (!newSettings.TryGetValue(key, out value)) continue;
                    lines[i] = key + "=" + value;
                    pending.Remove(key);
                }
                foreach (var entry in pending)
                    lines.Add(entry.Key + "=" + entry.Value);
                File.WriteAllLines(temporary, lines);
                if (exists) File.Replace(temporary, ini, null);
                else File.Move(temporary, ini);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }


        private static long ExtractUserIdFromCookie(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) return 0;
            try
            {
                int p1 = cookie.IndexOf("|_");
                if (p1 >= 0)
                {
                    string rest = cookie.Substring(p1 + 2);
                    int p2 = rest.IndexOf('.');
                    string b64 = (p2 >= 0) ? rest.Substring(0, p2) : rest;
                    while (b64.Length % 4 != 0) b64 += "=";
                    byte[] bytes = Convert.FromBase64String(b64);
                    string decoded = Encoding.UTF8.GetString(bytes);
                    MatchCollection matches = Regex.Matches(decoded, @"uid[\x00-\x1f:=]*(\d{4,12})");
                    if (matches.Count > 0)
                    {
                        for (int i = matches.Count - 1; i >= 0; i--)
                        {
                            long id;
                            if (long.TryParse(matches[i].Groups[1].Value, out id) && id > 1000)
                            {
                                return id;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        private static string CleanRoblosecurityCookie(string raw)



        {



            if (string.IsNullOrEmpty(raw)) return "";



            string cookie = raw.Trim();



            if (cookie.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))



            {



                cookie = cookie.Substring(15).Trim();



            }



            if (cookie.StartsWith(".ROBLOSECURITY ", StringComparison.OrdinalIgnoreCase))



            {



                cookie = cookie.Substring(15).Trim();



            }



            int warningIdx = cookie.IndexOf("_|WARNING:-DO-NOT-SHARE-THIS.");



            if (warningIdx >= 0)



            {



                cookie = cookie.Substring(warningIdx);



            }



            if (cookie.EndsWith(";")) cookie = cookie.Substring(0, cookie.Length - 1).Trim();



            if (cookie.StartsWith("\"") && cookie.EndsWith("\"") && cookie.Length > 2)



            {



                cookie = cookie.Substring(1, cookie.Length - 2).Trim();



            }



            return cookie;



        }



        private static string ResolveUsernameAndIdFromCookie(string cookie, out long userId)



        {



            userId = 0;



            if (string.IsNullOrEmpty(cookie)) return null;



            string cleanCookie = CleanRoblosecurityCookie(cookie);



            try



            {



                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;



                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://users.roblox.com/v1/users/authenticated");



                req.AllowAutoRedirect = false;
                req.Headers["Cookie"] = ".ROBLOSECURITY=" + cleanCookie;



                req.UserAgent = "Roblox/WinInet";



                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())



                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))



                {



                    string json = sr.ReadToEnd();



                    int idIdx = json.IndexOf("\"id\":");



                    if (idIdx >= 0)



                    {



                        int idValStart = idIdx + 5;



                        int idValEnd = json.IndexOfAny(new char[] { ',', '}', ' ' }, idValStart);



                        if (idValEnd > idValStart)



                        {



                            long parsedId;



                            if (long.TryParse(json.Substring(idValStart, idValEnd - idValStart).Trim(), out parsedId))



                            {



                                userId = parsedId;



                            }



                        }



                    }



                    string nameTag = "\"name\":\"";



                    int nStart = json.IndexOf(nameTag);



                    if (nStart > 0)



                    {



                        nStart += nameTag.Length;



                        int nEnd = json.IndexOf("\"", nStart);



                        if (nEnd > nStart)



                        {



                            return json.Substring(nStart, nEnd - nStart);



                        }



                    }



                }



            }



            catch { }



            return ResolveUsernameFromCookie(cleanCookie);



        }



        private static string ResolveUsernameFromCookie(string cookie)



        {



            if (string.IsNullOrEmpty(cookie)) return null;



            try



            {



                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;



                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://users.roblox.com/v1/users/authenticated");



                req.AllowAutoRedirect = false;
                req.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;



                req.UserAgent = "Roblox/WinInet";



                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())



                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))



                {



                    string json = sr.ReadToEnd();



                    string nameTag = "\"name\":\"";



                    int nStart = json.IndexOf(nameTag);



                    if (nStart > 0)



                    {



                        nStart += nameTag.Length;



                        int nEnd = json.IndexOf("\"", nStart);



                        if (nEnd > nStart)



                        {



                            return json.Substring(nStart, nEnd - nStart);



                        }



                    }



                }



            }



            catch { }



            try



            {



                HttpWebRequest req2 = (HttpWebRequest)WebRequest.Create("https://www.roblox.com/my/account/json");



                req2.AllowAutoRedirect = false;
                req2.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;



                req2.UserAgent = "Roblox/WinInet";



                using (HttpWebResponse resp2 = (HttpWebResponse)req2.GetResponse())



                using (StreamReader sr2 = new StreamReader(resp2.GetResponseStream(), Encoding.UTF8))



                {



                    string json2 = sr2.ReadToEnd();



                    string nameTag = "\"Name\":\"";



                    int nStart = json2.IndexOf(nameTag);



                    if (nStart == -1)



                    {



                        nameTag = "\"name\":\"";



                        nStart = json2.IndexOf(nameTag);



                    }



                    if (nStart > 0)



                    {



                        nStart += nameTag.Length;



                        int nEnd = json2.IndexOf("\"", nStart);



                        if (nEnd > nStart)



                        {



                            return json2.Substring(nStart, nEnd - nStart);



                        }



                    }



                }



            }



            catch { }



            return null;



        }



        private static string GetRobloxAuthTicket(string cookie, string placeId)



        {



            try



            {



                cookie = CleanRoblosecurityCookie(cookie);



                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;



                string refererUrl = (!string.IsNullOrEmpty(placeId) && placeId != "0")



                    ? ("https://www.roblox.com/games/" + placeId + "/game")



                    : "https://www.roblox.com/games/4924922222/Brookhaven-RP";



                string csrfToken = null;



                try



                {



                    HttpWebRequest csrfReq = (HttpWebRequest)WebRequest.Create("https://auth.roblox.com/v1/authentication-ticket/");



                    csrfReq.AllowAutoRedirect = false;
                    csrfReq.Method = "POST";



                    csrfReq.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;



                    csrfReq.Referer = refererUrl;



                    csrfReq.ContentLength = 0;



                    csrfReq.UserAgent = "Roblox/WinInet";



                    using (HttpWebResponse resp = (HttpWebResponse)csrfReq.GetResponse()) { }



                }



                catch (WebException wex)



                {



                    if (wex.Response is HttpWebResponse)



                    {



                        HttpWebResponse resp = (HttpWebResponse)wex.Response;



                        csrfToken = resp.Headers["x-csrf-token"];



                    }



                }



                if (string.IsNullOrEmpty(csrfToken)) return null;



                HttpWebRequest authReq = (HttpWebRequest)WebRequest.Create("https://auth.roblox.com/v1/authentication-ticket/");



                authReq.AllowAutoRedirect = false;
                authReq.Method = "POST";



                authReq.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;



                authReq.Referer = refererUrl;



                authReq.Headers["x-csrf-token"] = csrfToken;



                authReq.ContentType = "application/json";



                authReq.UserAgent = "Roblox/WinInet";



                byte[] body = Encoding.UTF8.GetBytes("{}");



                authReq.ContentLength = body.Length;



                using (Stream s = authReq.GetRequestStream())



                {



                    s.Write(body, 0, body.Length);



                }



                using (HttpWebResponse resp = (HttpWebResponse)authReq.GetResponse())



                {



                    return resp.Headers["rbx-authentication-ticket"];



                }



            }



            catch



            {



                return null;



            }



        }



        private void StartAutoReconnectWatcher()



        {



            logWatcherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, autoReconnectIntervalSec)) };



            logWatcherTimer.Tick += (s, e) => CheckRobloxLogsForDisconnections();



            logWatcherTimer.Start();



        }



        private long ResolveUserIdFromUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return 0;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://users.roblox.com/v1/usernames/users");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "Roblox/WinInet";
                string payload = string.Format("{{\"usernames\":[\"{0}\"],\"excludeBannedUsers\":false}}", username);
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (Stream s = req.GetRequestStream())
                {
                    s.Write(bytes, 0, bytes.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    int idIdx = json.IndexOf("\"id\":");
                    if (idIdx >= 0)
                    {
                        int valStart = idIdx + 5;
                        int valEnd = json.IndexOfAny(new char[] { ',', '}', ' ' }, valStart);
                        if (valEnd > valStart)
                        {
                            long uid;
                            if (long.TryParse(json.Substring(valStart, valEnd - valStart).Trim(), out uid))
                            {
                                return uid;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        private string FindRobloxLogForAccount(AccountItem acc)
        {
            try
            {
                if (acc == null) return null;

                // 0. Garantir resolucao de UserId caso ausente
                if (acc.UserId <= 0)
                {
                    if (!string.IsNullOrEmpty(acc.Cookie))
                    {
                        try
                        {
                            long uid = 0;
                            ResolveUsernameAndIdFromCookie(acc.Cookie, out uid);
                            if (uid > 0)
                            {
                                acc.UserId = uid;
                                SaveAccounts();
                            }
                        }
                        catch { }
                    }

                    if (acc.UserId <= 0 && !string.IsNullOrEmpty(acc.Username))
                    {
                        try
                        {
                            long resolvedUid = ResolveUserIdFromUsername(acc.Username);
                            if (resolvedUid > 0)
                            {
                                acc.UserId = resolvedUid;
                                SaveAccounts();
                            }
                        }
                        catch { }
                    }
                }

                // 1. Identificar o processo ativo da conta e seu timestamp de inicio
                int targetPid = 0;
                lock (runningAccountPids)
                {
                    runningAccountPids.TryGetValue(acc.Username, out targetPid);
                }

                DateTime procStartUtc = DateTime.MinValue;
                if (targetPid > 0)
                {
                    try
                    {
                        Process p = Process.GetProcessById(targetPid);
                        if (!p.HasExited)
                        {
                            procStartUtc = p.StartTime.ToUniversalTime();
                        }
                        else
                        {
                            targetPid = 0;
                        }
                    }
                    catch
                    {
                        targetPid = 0;
                    }
                }

                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\logs");
                if (!Directory.Exists(logDir)) return null;

                DirectoryInfo di = new DirectoryInfo(logDir);
                FileInfo[] files = di.GetFiles("*_Player_*_last.log");
                if (files == null || files.Length == 0) return null;

                // Se temos o processo rodando, NUNCA aceitar arquivos criados antes do processo nascer!
                DateTime cutoff = (procStartUtc != DateTime.MinValue) ? procStartUtc.AddSeconds(-15) : DateTime.UtcNow.AddMinutes(-30);

                List<FileInfo> candidateFiles = new List<FileInfo>();
                foreach (var f in files)
                {
                    if (!f.Name.Contains("CrashHandler") && f.CreationTimeUtc >= cutoff)
                    {
                        candidateFiles.Add(f);
                    }
                }

                if (candidateFiles.Count == 0) return null;
                candidateFiles.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));

                // Filtrar arquivos ja reclamados por OUTRAS contas ativas
                HashSet<string> claimedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                lock (accountLogFiles)
                {
                    foreach (var kvp in accountLogFiles)
                    {
                        if (!kvp.Key.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                        {
                            claimedFiles.Add(kvp.Value);
                        }
                    }
                }

                string targetUidPattern = (acc.UserId > 0) ? ("userid:" + acc.UserId) : null;

                // 2. CORRELACAO EXATA POR USERID NA SESSAO REAL DO JOGO:
                // [FLog::GameJoinLoadTime] Report game_join_loadtime: ... userid:<UserId>,
                if (!string.IsNullOrEmpty(targetUidPattern))
                {
                    foreach (var f in candidateFiles)
                    {
                        if (claimedFiles.Contains(f.FullName)) continue;

                        try
                        {
                            using (FileStream fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                int toRead = (int)Math.Min(262144, fs.Length);
                                if (toRead <= 0) continue;

                                byte[] buf = new byte[toRead];
                                int read = fs.Read(buf, 0, toRead);
                                if (read > 0)
                                {
                                    string text = Encoding.UTF8.GetString(buf, 0, read);
                                    int sIdx = 0;
                                    while ((sIdx = text.IndexOf("Report game_join_loadtime:", sIdx, StringComparison.OrdinalIgnoreCase)) >= 0)
                                    {
                                        int lineEnd = text.IndexOf('\n', sIdx);
                                        string joinLine = (lineEnd > sIdx) ? text.Substring(sIdx, lineEnd - sIdx) : text.Substring(sIdx);

                                        int uIdx = joinLine.IndexOf(targetUidPattern, StringComparison.OrdinalIgnoreCase);
                                        if (uIdx >= 0)
                                        {
                                            int afterPos = uIdx + targetUidPattern.Length;
                                            char nextCh = (afterPos < joinLine.Length) ? joinLine[afterPos] : '\0';
                                            if (nextCh == ',' || nextCh == ' ' || nextCh == '\r' || nextCh == '\0')
                                            {
                                                return f.FullName;
                                            }
                                        }
                                        sIdx += 26;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

            }
            catch { }
            return null;
        }

        private enum SessionLogSignal { None, Recovered, Teleport, Disconnected }

        private static SessionLogSignal GetLatestSessionLogSignal(string text, bool initialRead, out string reason)
        {
            reason = null;
            if (initialRead) return SessionLogSignal.None;

            // Each marker belongs to a single event. A later recovery supersedes an earlier error.
            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (HasSessionLogMarker(line, "Connection accepted from", "Replicator created for player",
                    "Entered play session", "Game joined!", "Report game_join_loadtime:"))
                    return SessionLogSignal.Recovered;

                if (HasSessionLogMarker(line, "! Joining game", "finishTeleportWithJoinScriptPayload",
                    "Teleport requested", "Sending disconnect for teleport", "TeleportStarted", "Initiating teleport"))
                    return SessionLogSignal.Teleport;

                if (HasSessionLogMarker(line, "Lost connection to the game server",
                    "Client has been disconnected with reason", "Disconnected from game, please reconnect",
                    "You have been kicked", "This game has been disconnected because you have joined") ||
                    Regex.IsMatch(line, @"\b(?:Error code\s+|Sending disconnect with reason:\s*)(?:264|268|273|277|279)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    reason = "Desconexão da partida registrada pelo Roblox";
                    return SessionLogSignal.Disconnected;
                }
            }
            return SessionLogSignal.None;
        }

        private static bool HasSessionLogMarker(string line, params string[] markers)
        {
            foreach (string marker in markers)
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }


        private void CheckRobloxLogsForDisconnections()
        {
            if (vaultLocked) return;
            if (++discordPresenceCounter % 3 == 0)
            {
                try { UpdateDiscordPresence(); } catch { }
            }

            var activeList = accounts.FindAll(account => autoReconnectEnabled && account.AutoReconnect &&
                !manualClosedAccounts.Contains(account.Username) && IsAccountRunning(account));

            foreach (var acc in activeList)
            {
                try
                {
                    if (manualClosedAccounts.Contains(acc.Username)) continue;
                    lock (reconnectingAccounts)
                    {
                        if (reconnectingAccounts.Contains(acc.Username) || isReconnectingAll) continue;
                    }

                    int pid = 0;
                    DateTime procStartUtc = DateTime.MinValue;
                    lock (runningAccountPids)
                    {
                        runningAccountPids.TryGetValue(acc.Username, out pid);
                    }
                    if (pid > 0)
                    {
                        try
                        {
                            Process p = Process.GetProcessById(pid);
                            if (p.HasExited) continue;
                            procStartUtc = p.StartTime.ToUniversalTime();
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    // 2. LOCALIZAR OU ATUALIZAR ARQUIVO DE LOG DA SESSAO ATUAL
                    string logPath = null;
                    lock (accountLogFiles)
                    {
                        accountLogFiles.TryGetValue(acc.Username, out logPath);
                    }

                    // Se temos um PID ativo, validar se o arquivo de log associado e valido para este processo
                    if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath) && procStartUtc != DateTime.MinValue)
                    {
                        FileInfo fiCheck = new FileInfo(logPath);
                        // Se o arquivo associado foi criado ANTES do processo nascer, e stale!
                        if (fiCheck.CreationTimeUtc < procStartUtc.AddSeconds(-15))
                        {
                            lock (accountLogFiles) accountLogFiles.Remove(acc.Username);
                            lock (accountLogOffsets) accountLogOffsets.Remove(acc.Username);
                            logPath = null;
                        }
                    }

                    if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                    {
                        logPath = FindRobloxLogForAccount(acc);
                        if (!string.IsNullOrEmpty(logPath))
                        {
                            lock (accountLogFiles)
                            {
                                accountLogFiles[acc.Username] = logPath;
                            }
                            lock (accountLogOffsets)
                            {
                                accountLogOffsets[acc.Username] = 0; // Initial read discovers game metadata without replaying disconnects.
                            }
                            Dispatcher.Invoke(() =>
                            {
                                AddLog(string.Format("🔄 Monitoramento ativo iniciado para @{0} (Log: {1})", acc.Username, Path.GetFileName(logPath)), "INFO", "AUTO-REC");
                            });
                        }
                    }

                    if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) continue;

                    long lastOffset = 0;
                    lock (accountLogOffsets)
                    {
                        accountLogOffsets.TryGetValue(acc.Username, out lastOffset);
                    }

                    using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length <= lastOffset)
                        {
                            continue;
                        }

                        fs.Seek(lastOffset, SeekOrigin.Begin);
                        int toRead = (int)(fs.Length - lastOffset);
                        byte[] chunk = new byte[toRead];
                        int read = fs.Read(chunk, 0, toRead);
                        int complete = read == 0 ? 0 : Array.LastIndexOf(chunk, (byte)10, read - 1) + 1;
                        if (complete == 0) continue;
                        lock (accountLogOffsets)
                        {
                            accountLogOffsets[acc.Username] = lastOffset + complete;
                        }

                        if (read > 0)
                        {
                            string text = Encoding.UTF8.GetString(chunk, 0, complete);

                            // A. Deteccao de Jogo / PlaceId / JobId
                            string detectedPlaceId = null;
                            string detectedJobId = null;
                            string detectedUniverseId = null;

                            MatchCollection mJoins = RegexJoiningGame.Matches(text);
                            if (mJoins.Count > 0)
                            {
                                Match lastJoin = mJoins[mJoins.Count - 1];
                                detectedJobId = lastJoin.Groups[1].Value.Trim();
                                detectedPlaceId = lastJoin.Groups[2].Value.Trim();
                            }

                            MatchCollection mLoads = RegexGameJoinLoad.Matches(text);
                            if (mLoads.Count > 0)
                            {
                                Match lastLoad = mLoads[mLoads.Count - 1];
                                if (string.IsNullOrEmpty(detectedPlaceId)) detectedPlaceId = lastLoad.Groups[1].Value.Trim();
                                detectedUniverseId = lastLoad.Groups[2].Value.Trim();
                            }

                            if (string.IsNullOrEmpty(detectedPlaceId))
                            {
                                MatchCollection mPlaces = RegexPlaceFallback.Matches(text);
                                if (mPlaces.Count > 0)
                                {
                                    detectedPlaceId = mPlaces[mPlaces.Count - 1].Groups[1].Value.Trim();
                                }
                            }

                            if (!string.IsNullOrEmpty(detectedPlaceId) && detectedPlaceId != "0")
                            {
                                accountLastTeleportTime[acc.Username] = DateTime.UtcNow;
                                if (acc.CurrentPlaceId != detectedPlaceId)
                                {
                                    acc.CurrentPlaceId = detectedPlaceId;
                                    if (!string.IsNullOrEmpty(detectedJobId)) acc.CurrentJobId = detectedJobId;

                                    string knownName = null;
                                    lock (placeNames)
                                    {
                                        placeNames.TryGetValue(detectedPlaceId, out knownName);
                                    }

                                    if (!string.IsNullOrEmpty(knownName))
                                    {
                                        if (acc.CurrentGame != knownName)
                                        {
                                            acc.CurrentGame = knownName;
                                            Dispatcher.Invoke(() =>
                                            {
                                                AddLog(string.Format("🎮 [JOGO DETECTADO] Conta @{0} está jogando '{1}' (Place ID: {2})", acc.Username, knownName, detectedPlaceId), "INFO", "GAME");
                                                RefreshAccountsUI();
                                                SaveAccounts();
                                            });
                                        }
                                    }
                                    else
                                    {
                                        ResolveGameNameAsync(detectedPlaceId, detectedUniverseId, acc);
                                    }
                                }
                                else if (!string.IsNullOrEmpty(detectedJobId) && acc.CurrentJobId != detectedJobId)
                                {
                                    acc.CurrentJobId = detectedJobId;
                                    SaveAccounts();
                                }
                            }

                            string reason;
                            var signal = GetLatestSessionLogSignal(text, lastOffset == 0, out reason);
                            if (signal == SessionLogSignal.Recovered || signal == SessionLogSignal.Teleport)
                            {
                                if (signal == SessionLogSignal.Recovered)
                                    accountLastJoinSuccessTime[acc.Username] = DateTime.UtcNow;
                                else
                                    accountLastTeleportTime[acc.Username] = DateTime.UtcNow;

                                bool pending;
                                lock (accountSuspectedDisconnectTime)
                                {
                                    pending = accountSuspectedDisconnectTime.Remove(acc.Username);
                                    accountSuspectedReason.Remove(acc.Username);
                                }
                                if (pending)
                                    AddLog("Reconexão automática cancelada para @" + acc.Username +
                                        ". O Roblox indicou entrada no jogo ou teleporte.", "INFO", "RECONNECT");
                            }
                            else if (signal == SessionLogSignal.Disconnected)
                            {
                                bool added = false;
                                lock (accountSuspectedDisconnectTime)
                                {
                                    if (!accountSuspectedDisconnectTime.ContainsKey(acc.Username))
                                    {
                                        accountSuspectedDisconnectTime[acc.Username] = DateTime.UtcNow;
                                        accountSuspectedReason[acc.Username] = reason;
                                        added = true;
                                    }
                                }
                                if (added)
                                    AddLog("Desconexão detectada para @" + acc.Username + ". Aguardando a tolerância configurada.",
                                        "WARN", "RECONNECT");
                            }
                        }
                    }
                }
                catch { }
            }
            if (autoReconnectEnabled)
            {
                List<string> suspectedUsers;
                lock (accountSuspectedDisconnectTime)
                {
                    suspectedUsers = new List<string>(accountSuspectedDisconnectTime.Keys);
                }

                foreach (var u in suspectedUsers)
                {
                    if (manualClosedAccounts.Contains(u))
                    {
                        lock (accountSuspectedDisconnectTime)
                        {
                            accountSuspectedDisconnectTime.Remove(u);
                            accountSuspectedReason.Remove(u);
                        }
                        continue;
                    }

                    AccountItem sAcc = accounts.Find(x => x.Username.Equals(u, StringComparison.OrdinalIgnoreCase));
                    if (sAcc == null || !sAcc.AutoReconnect) continue;

                    DateTime susTime;
                    bool hasSus;
                    lock (accountSuspectedDisconnectTime)
                    {
                        hasSus = accountSuspectedDisconnectTime.TryGetValue(u, out susTime);
                    }

                    if (hasSus)
                    {
                        double elapsed = (DateTime.UtcNow - susTime).TotalSeconds;
                        if (elapsed >= autoReconnectGraceSec)
                        {
                            string reason = "Desconexao confirmada (sem reconexao interna apos " + (int)elapsed + "s)";
                            lock (accountSuspectedDisconnectTime)
                            {
                                accountSuspectedReason.TryGetValue(u, out reason);
                                accountSuspectedDisconnectTime.Remove(u);
                                accountSuspectedReason.Remove(u);
                            }

                            Dispatcher.Invoke(() =>
                            {
                                AddLog("Desconexão confirmada para @" + u + " após a tolerância configurada.", "WARN", "RECONNECT");
                            });

                            TriggerAutoReconnect(sAcc, reason);
                            return; // Reinicio geral disparado
                        }
                    }
                }
            }

        }

        private bool CanReconnect(AccountItem account)
        {
            return !vaultLocked && account != null && accounts.Contains(account) && autoReconnectEnabled && account.AutoReconnect &&
                !manualClosedAccounts.Contains(account.Username) && account.Status != NeedsSignIn &&
                account.Status != UnconfirmedSession;
        }

        private async Task<bool> ReconnectSingleAccountSafeAsync(AccountItem account)
        {
            await reconnectLaunchLock.WaitAsync();
            try
            {
                var launch = Dispatcher.Invoke(() =>
                {
                    if (!CanReconnect(account) || account.IsLaunching || IsAccountRunning(account))
                        return Task.FromResult(false);
                    return LaunchGame(account, account.CurrentPlaceId ?? "", "", true);
                });
                return await launch;
            }
            finally { reconnectLaunchLock.Release(); }
        }

        private void TriggerAutoReconnect(AccountItem account, string reason)
        {
            if (!CanReconnect(account)) return;
            var targets = SelectReconnectTargets(account);
            lock (reconnectingAccounts)
            {
                if (isReconnectingAll || targets.Exists(target => reconnectingAccounts.Contains(target.Username))) return;
                isReconnectingAll = targets.Count > 1;
                foreach (var target in targets) reconnectingAccounts.Add(target.Username);
            }

            RunReconnectAsync(account, reason, targets);
        }

        private List<AccountItem> SelectReconnectTargets(AccountItem trigger)
        {
            return reconnectAllOnSingleDrop
                ? accounts.FindAll(candidate => CanReconnect(candidate) && (candidate == trigger || IsAccountRunning(candidate)))
                : new List<AccountItem> { trigger };
        }

        private async void RunReconnectAsync(AccountItem trigger, string reason, List<AccountItem> targets)
        {
            try
            {
                AddLog("Reconexão iniciada para @" + trigger.Username + ": " + (reason ?? "conexão interrompida"), "WARN", "RECONNECT");
                var restart = new List<AccountItem>();
                foreach (var target in targets)
                {
                    if (!CanReconnect(target) || target.IsLaunching) continue;
                    var previous = StopTrackedSession(target);
                    if (previous == StopResult.Stopped || (target == trigger && previous == StopResult.Untracked))
                        restart.Add(target);
                    else if (previous == StopResult.Failed)
                        AddLog("A sessão de @" + target.Username + " não foi encerrada. A reconexão foi ignorada.", "WARN", "RECONNECT");
                }
                ClearSessionLogTracking(trigger.Username);
                if (restart.Count == 0) return;

                while (!await Task.Run(() => CheckInternetDirect()))
                {
                    if (!restart.Exists(CanReconnect)) return;
                    await Task.Delay(1500);
                }

                await Task.Delay(TimeSpan.FromSeconds(autoReconnectCooldownSec));
                int dispatched = 0;
                foreach (var target in restart)
                {
                    if (await ReconnectSingleAccountSafeAsync(target))
                    {
                        dispatched++;
                        if (target != restart[restart.Count - 1]) await Task.Delay(launchDelayMs);
                    }
                }

                AddLog("Reconexão concluída. Solicitações enviadas: " + dispatched + " de " + restart.Count + ".", "INFO", "RECONNECT");
            }
            catch (Exception error)
            {
                AddLog("Falha durante a reconexão: " + error.GetType().Name + ".", "ERROR", "RECONNECT");
            }
            finally
            {
                lock (reconnectingAccounts)
                {
                    foreach (var target in targets) reconnectingAccounts.Remove(target.Username);
                    if (targets.Count > 1) isReconnectingAll = false;
                }
                RefreshAccountsUI();
            }
        }


        private AccountStore accountStore;
        private string accountStoreNotice;

        private void LoadAllAccounts()
        {
            accountStore = new AccountStore(dataDir, appDir);
            try
            {
                vaultConfigured = accountStore.RequiresPassword();
                vaultLocked = vaultConfigured;
                if (vaultLocked) return;
                accounts = accountStore.Load(out accountStoreNotice);
                selectedAccount = accounts.Count > 0 ? accounts[0] : null;
            }
            catch (InvalidDataException) { SetAccountLoadFailure(); }
            catch (IOException) { SetAccountLoadFailure(); }
            catch (UnauthorizedAccessException) { SetAccountLoadFailure(); }
            catch (CryptographicException) { SetAccountLoadFailure(); }
            catch (ArgumentException) { SetAccountLoadFailure(); }
            catch (InvalidOperationException) { SetAccountLoadFailure(); }
        }

        private void SetAccountLoadFailure()
        {
            accountStoreNotice = "Não foi possível abrir o arquivo de contas. Os dados existentes foram preservados.";
        }

        private bool SaveAccounts()
        {
            if (vaultLocked) return false;
            try
            {
                accountStore.Save(accounts);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (CryptographicException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            const string message = "Não foi possível salvar as contas. Confira a pasta do aplicativo ou se outra instância alterou a lista.";
            SetStatus(message);
            AddLog(message, "ERROR", "ACCOUNTS");
            return false;
        }

        private bool PersistLogin(AccountItem incoming)
        {
            if (vaultLocked) return false;
            var existing = accounts.Find(item => string.Equals(item.Username, incoming.Username, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Cookie, incoming.Cookie, StringComparison.Ordinal));
            if (existing == null)
            {
                accounts.Add(incoming);
                if (!SaveAccounts())
                {
                    accounts.Remove(incoming);
                    return false;
                }
                selectedAccount = incoming;
            }
            else
            {
                string cookie = existing.Cookie;
                long userId = existing.UserId;
                string alias = existing.Alias;
                existing.Cookie = incoming.Cookie;
                if (incoming.UserId > 0) existing.UserId = incoming.UserId;
                if (!string.IsNullOrEmpty(incoming.Alias)) existing.Alias = incoming.Alias;
                if (!SaveAccounts())
                {
                    existing.Cookie = cookie;
                    existing.UserId = userId;
                    existing.Alias = alias;
                    return false;
                }
                existing.Status = "Offline";
                selectedAccount = existing;
            }
            RefreshAccountsUI();
            return true;
        }

    }
}
