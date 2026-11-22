using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxMultiByJayy
{
    internal sealed class VaultProtection : IDisposable
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RMJVAULT2");
        private const int SaltSize = 16, IvSize = 16, TagSize = 32;
        private const int KdfIterations = 600000;
        private readonly byte[] salt;
        private readonly byte[] encryptionKey;
        private readonly byte[] authenticationKey;

        internal VaultProtection(string password, byte[] envelope = null)
        {
            salt = envelope == null ? RandomBytes(SaltSize) : Slice(envelope, Magic.Length, SaltSize);
            using (var kdf = new Rfc2898DeriveBytes(password, salt, KdfIterations, HashAlgorithmName.SHA256))
            {
                encryptionKey = kdf.GetBytes(32);
                authenticationKey = kdf.GetBytes(32);
            }
        }

        private VaultProtection(byte[] keys)
        {
            if (keys == null || keys.Length != SaltSize + 64) throw new CryptographicException();
            salt = Slice(keys, 0, SaltSize);
            encryptionKey = Slice(keys, SaltSize, 32);
            authenticationKey = Slice(keys, SaltSize + 32, 32);
        }

        internal static VaultProtection FromKeys(byte[] keys) { return new VaultProtection(keys); }

        internal byte[] ExportKeys() { return salt.Concat(encryptionKey).Concat(authenticationKey).ToArray(); }

        internal static bool IsProtected(byte[] data)
        {
            return data != null && data.Length >= Magic.Length && Magic.SequenceEqual(data.Take(Magic.Length));
        }

        internal static bool HasHeader(byte[] data)
        {
            return IsProtected(data) && data.Length >= Magic.Length + SaltSize + IvSize + 16 + TagSize;
        }

        internal static bool SameSalt(byte[] a, byte[] b)
        {
            return HasHeader(a) && HasHeader(b) &&
                Slice(a, Magic.Length, SaltSize).SequenceEqual(Slice(b, Magic.Length, SaltSize));
        }

        internal byte[] Protect(byte[] dpapi)
        {
            var iv = RandomBytes(IvSize);
            byte[] ciphertext;
            using (var aes = CreateAes(iv))
            using (var encryptor = aes.CreateEncryptor())
                ciphertext = encryptor.TransformFinalBlock(dpapi, 0, dpapi.Length);
            var body = Magic.Concat(salt).Concat(iv).Concat(ciphertext).ToArray();
            using (var hmac = new HMACSHA256(authenticationKey)) return body.Concat(hmac.ComputeHash(body)).ToArray();
        }

        internal byte[] Unprotect(byte[] envelope)
        {
            if (!HasHeader(envelope)) throw new CryptographicException();
            int end = envelope.Length - TagSize;
            byte[] tag;
            using (var hmac = new HMACSHA256(authenticationKey)) tag = hmac.ComputeHash(envelope, 0, end);
            int difference = 0;
            for (int i = 0; i < TagSize; i++) difference |= tag[i] ^ envelope[end + i];
            if (difference != 0) throw new CryptographicException();
            int offset = Magic.Length + SaltSize;
            using (var aes = CreateAes(Slice(envelope, offset, IvSize)))
            using (var decryptor = aes.CreateDecryptor())
                return decryptor.TransformFinalBlock(envelope, offset + IvSize, end - offset - IvSize);
        }

        private Aes CreateAes(byte[] iv)
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.IV = iv;
            return aes;
        }

        private static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        public void Dispose()
        {
            Array.Clear(encryptionKey, 0, encryptionKey.Length);
            Array.Clear(authenticationKey, 0, authenticationKey.Length);
        }

        private static readonly Regex SecretPattern = new Regex(
            @"_\|WARNING:-DO-NOT-SHARE-THIS\.[^\s""<>]*|(?:\.ROBLOSECURITY\s*[:=]|rbx-authentication-ticket\s*[:=]|x-csrf-token\s*[:=]|\bgameinfo:)\s*(?:""[^""]*""|[^\s;,+""<>]+)|roblox-player:[^\s""<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string Redact(string text)
        {
            return text == null ? null : SecretPattern.Replace(text, "[segredo removido]");
        }
    }
}
