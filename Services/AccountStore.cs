using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace RobloxMultiByJayy
{
    internal sealed class AccountStore
    {
        private const string MigratedMarker = "roblox_multi_accounts.secure";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Roblox Multi by Jayy / accounts / v1");
        private readonly string folder;
        private readonly string primary;
        private readonly string backup;
        private readonly string legacyFolder;
        private static JavaScriptSerializer NewSerializer() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
        private byte[] revision;
        private VaultProtection protection;
        private bool loaded;
        private bool recoveredFromBackup;

        internal AccountStore(string folder, string legacyFolder)
        {
            this.folder = folder;
            primary = Path.Combine(folder, MigratedMarker);
            backup = primary + ".bak";
            this.legacyFolder = legacyFolder;
        }

        internal List<AccountItem> Load(out string notice)
        {
            Directory.CreateDirectory(folder);
            notice = null;
            loaded = false;
            recoveredFromBackup = false;
            if (File.Exists(primary) || File.Exists(backup))
            {
                List<AccountItem> items;
                using (LockStore())
                {
                    if (protection == null && RequiresPassword()) throw new CryptographicException();
                    revision = File.Exists(primary) ? File.ReadAllBytes(primary) : null;
                    try
                    {
                        if (revision == null) throw new InvalidDataException();
                        items = ReadProtected(revision);
                    }
                    catch (CryptographicException) { items = Recover(); }
                    catch (InvalidDataException) { items = Recover(); }
                    catch (ArgumentException) { items = Recover(); }
                    catch (InvalidOperationException) { items = Recover(); }
                }
                loaded = true;
                if (recoveredFromBackup) notice = "Contas recuperadas da cópia protegida.";
                return items;
            }

            // Migrate only the encrypted vault from an older installation in this folder.
            if (string.IsNullOrEmpty(legacyFolder) || string.Equals(Path.GetFullPath(folder), Path.GetFullPath(legacyFolder),
                StringComparison.OrdinalIgnoreCase))
            {
                loaded = true;
                return new List<AccountItem>();
            }
            var source = new AccountStore(legacyFolder, null);
            if (!File.Exists(source.primary) && !File.Exists(source.backup))
            {
                loaded = true;
                return new List<AccountItem>();
            }

            string ignored;
            var accounts = source.Load(out ignored);
            loaded = true;
            try
            {
                Save(accounts);
                if (!File.Exists(backup)) throw new IOException("A cópia protegida não foi criada.");
                using (LockStore())
                {
                    ReadProtected(File.ReadAllBytes(primary));
                    ReadProtected(File.ReadAllBytes(backup));
                }
            }
            catch
            {
                loaded = false;
                throw;
            }
            notice = "Contas importadas para a pasta de dados local. O cofre anterior foi preservado.";
            return accounts;
        }

        internal bool RequiresPassword()
        {
            return (File.Exists(primary) && VaultProtection.IsProtected(File.ReadAllBytes(primary))) ||
                (File.Exists(backup) && VaultProtection.IsProtected(File.ReadAllBytes(backup)));
        }

        internal List<AccountItem> Unlock(string password, out string notice)
        {
            var candidate = File.Exists(primary) ? File.ReadAllBytes(primary) : null;
            var recovery = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            if (!VaultProtection.HasHeader(candidate)) return UnlockWithHeader(password, recovery, out notice);
            try { return UnlockWithHeader(password, candidate, out notice); }
            catch (CryptographicException)
            {
                if (!VaultProtection.HasHeader(recovery) || VaultProtection.SameSalt(candidate, recovery)) throw;
                return UnlockWithHeader(password, recovery, out notice);
            }
        }

        private List<AccountItem> UnlockWithHeader(string password, byte[] candidate, out string notice)
        {
            if (!VaultProtection.HasHeader(candidate)) throw new CryptographicException();
            Lock();
            protection = new VaultProtection(password, candidate);
            try { return Load(out notice); }
            catch { Lock(); throw; }
        }

        internal void EnablePassword(string password, IEnumerable<AccountItem> accounts)
        {
            if (!loaded || protection != null) throw new InvalidOperationException();
            using (LockStore())
            {
                CheckRevision();
                ReplacePasswordLocked(password, accounts);
            }
        }

        private string RecoveryPath { get { return primary + ".recovery"; } }

        internal bool HasRecovery { get { return File.Exists(RecoveryPath); } }

        internal void ChangePassword(string current, string next)
        {
            using (LockStore())
            {
                CheckRevision();
                if (!loaded || protection == null) throw new InvalidOperationException();
                var accounts = ReadWithPassword(current);
                ReplacePasswordLocked(next, accounts);
            }
        }

        internal string CreateRecoveryCode(string current)
        {
            using (LockStore())
            {
                CheckRevision();
                if (!loaded || protection == null) throw new InvalidOperationException();
                ReadWithPassword(current);
                var raw = new byte[24];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(raw);
                string code = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_');
                Array.Clear(raw, 0, raw.Length);
                byte[] keys = protection.ExportKeys();
                try
                {
                    using (var wrapping = new VaultProtection(code))
                    {
                        var sealedKeys = wrapping.Protect(keys);
                        WriteAtomically(RecoveryPath, sealedKeys, null);
                    }
                }
                finally { Array.Clear(keys, 0, keys.Length); }
                return code;
            }
        }

        internal List<AccountItem> RecoverWithCode(string code, string next)
        {
            using (LockStore())
            {
                if (!HasRecovery) throw new CryptographicException();
                var candidate = File.ReadAllBytes(RecoveryPath);
                if (!VaultProtection.HasHeader(candidate)) throw new CryptographicException();
                using (var wrapping = new VaultProtection(code.Trim(), candidate))
                {
                    var keys = wrapping.Unprotect(candidate);
                    try
                    {
                        using (var recovered = VaultProtection.FromKeys(keys))
                        {
                            var accounts = ReadCurrent(recovered);
                            ReplacePasswordLocked(next, accounts);
                            return accounts;
                        }
                    }
                    finally { Array.Clear(keys, 0, keys.Length); }
                }
            }
        }

        private List<AccountItem> ReadWithPassword(string password)
        {
            var header = revision;
            if (recoveredFromBackup || !VaultProtection.HasHeader(header))
                header = File.ReadAllBytes(backup);
            if (!VaultProtection.HasHeader(header)) throw new CryptographicException();
            using (var key = new VaultProtection(password, header)) return ReadCurrent(key);
        }

        private List<AccountItem> ReadCurrent(VaultProtection key)
        {
            if (File.Exists(primary))
            {
                try { return ReadProtected(File.ReadAllBytes(primary), key); }
                catch (CryptographicException) { }
                catch (InvalidDataException) { }
            }
            if (!File.Exists(backup)) throw new CryptographicException();
            return ReadProtected(File.ReadAllBytes(backup), key);
        }

        private void InvalidateRecovery()
        {
            // New vault files use different keys; a leftover recovery file cannot open them.
            try { File.Delete(RecoveryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void ReplacePasswordLocked(string password, IEnumerable<AccountItem> accounts)
        {
            var candidate = new VaultProtection(password);
            try
            {
                byte[] sealedData = Encode(accounts, candidate);
                ReadProtected(sealedData, candidate);
                WriteAtomically(backup, sealedData, null);
                WriteAtomically(primary, sealedData, null);
                if (protection != null) protection.Dispose();
                protection = candidate;
                revision = sealedData;
                recoveredFromBackup = false;
                loaded = true;
                InvalidateRecovery();
            }
            catch { candidate.Dispose(); throw; }
        }


        internal void Lock()
        {
            if (protection != null) protection.Dispose();
            protection = null;
            loaded = false;
            revision = null;
        }

        private void CheckRevision()
        {
            var current = File.Exists(primary) ? File.ReadAllBytes(primary) : null;
            if (!(current == null && revision == null) &&
                (current == null || revision == null || !current.SequenceEqual(revision)))
                throw new IOException("A lista de contas mudou em outra instância.");
        }

        internal static byte[] SerializeAccounts(IEnumerable<AccountItem> accounts)
        {
            var records = accounts.Select(account => new
            {
                account.Username, account.Alias, account.Cookie, account.UserId, account.AutoReconnect,
                account.CurrentPlaceId, account.CurrentJobId
            }).ToArray();
            return Encoding.UTF8.GetBytes(NewSerializer().Serialize(records));
        }

        private byte[] Encode(IEnumerable<AccountItem> accounts, VaultProtection encryption)
        {
            var plain = SerializeAccounts(accounts);
            try
            {
                var dpapi = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                return encryption == null ? dpapi : encryption.Protect(dpapi);
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }


        internal void Save(IEnumerable<AccountItem> accounts)
        {
            if (!loaded) throw new InvalidOperationException();
            using (LockStore())
            {
                CheckRevision();
                byte[] encrypted = Encode(accounts, protection);

                WriteAtomically(primary, encrypted, recoveredFromBackup ? null : backup);
                revision = encrypted;
                recoveredFromBackup = false;
                if (!File.Exists(backup))
                {
                    try { WriteAtomically(backup, encrypted, null); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private List<AccountItem> Recover()
        {
            if (!File.Exists(backup)) throw new InvalidDataException("A cópia protegida não pode ser lida.");
            var accounts = ReadProtected(File.ReadAllBytes(backup));
            recoveredFromBackup = true;
            return accounts;
        }

        private FileStream LockStore()
        {
            return new FileStream(primary + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private List<AccountItem> ReadProtected(byte[] encrypted)
        {
            return ReadProtected(encrypted, protection);
        }

        private List<AccountItem> ReadProtected(byte[] encrypted, VaultProtection encryption)
        {
            if (VaultProtection.IsProtected(encrypted) != (encryption != null))
                throw new CryptographicException();
            var dpapi = encryption == null ? encrypted : encryption.Unprotect(encrypted);
            var plain = ProtectedData.Unprotect(dpapi, Entropy, DataProtectionScope.CurrentUser);
            try { return ReadJson(Utf8Text(plain)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        private static string Utf8Text(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }

        internal static List<AccountItem> ReadJson(string contents)
        {
            var root = NewSerializer().DeserializeObject(contents.TrimStart('\uFEFF'));
            var wrapper = root as IDictionary<string, object>;
            if (wrapper != null) root = Value(wrapper, "Accounts");
            var entries = root as object[];
            if (entries == null) throw new InvalidDataException("Formato de contas inválido.");

            var accounts = new List<AccountItem>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var record = entry as IDictionary<string, object>;
                if (record == null) throw new InvalidDataException("Registro de conta inválido.");
                string username = Text(record, "Username").Trim();
                if (username.Length == 0) throw new InvalidDataException("Conta sem nome de usuário.");
                if (!names.Add(username)) continue;
                string alias = Text(record, "Alias");
                long userId;
                long.TryParse(Text(record, "UserId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
                bool reconnect;
                bool parsed = bool.TryParse(Text(record, "AutoReconnect"), out reconnect);
                string cookie = Text(record, "Cookie");
                if (cookie.Length == 0) cookie = Text(record, "SecurityToken");

                accounts.Add(new AccountItem
                {
                    Username = username,
                    Alias = alias,
                    Cookie = cookie,
                    UserId = userId,
                    AutoReconnect = !parsed || reconnect,
                    Status = "Offline",
                    CurrentPlaceId = Text(record, "CurrentPlaceId"),
                    CurrentJobId = Text(record, "CurrentJobId")
                });
            }
            return accounts;
        }

        private static object Value(IDictionary<string, object> record, string key)
        {
            foreach (var field in record)
                if (field.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return field.Value;
            return null;
        }

        private static string Text(IDictionary<string, object> record, string key)
        {
            return Convert.ToString(Value(record, key), CultureInfo.InvariantCulture) ?? "";
        }

        internal static void WriteAtomically(string destination, byte[] bytes, string recovery)
        {
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temp, destination, recovery);
                else File.Move(temp, destination);
            }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
