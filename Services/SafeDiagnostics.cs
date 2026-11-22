using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace RobloxMultiByJayy
{
    internal static class SafeDiagnostics
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Sensitive = new Regex(
            @"\broblosecurity\b|\broblox-player:|\bgameinfo:|\b(?:authorization|bearer|password|senha|webhook|joinscript|cookie|rbx-authentication-ticket|x-csrf-token)\s*[:=]|discord(?:app)?\.com/api/webhooks|\[segredo removido\]",
            Options);
        private static readonly Regex Addresses = new Regex(
            @"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?|(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.%]+", Options);
        private static readonly Regex Identifiers = new Regex(
            @"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b|\b(?:user\s*id|job\s*id|session\s*id|browsertrackerid|sid)\s*[:=]\s*[\w-]+",
            Options);
        private static readonly Regex Paths = new Regex(@"(?:[a-z]:[\\/]+Users[\\/]+)[^\\/\r\n""<>]+", Options);
        private static readonly Regex Handles = new Regex(@"(?<![\w@])@[\w.]+", Options);
        private static readonly Regex Email = new Regex(@"\b[^\s<>@]+@[\w.-]+\.[a-z]{2,}\b", Options);
        private static readonly Regex LongToken = new Regex(@"(?<![a-z0-9])[a-z0-9_+/=-]{48,}(?![a-z0-9])", Options);

        internal static string Redact(string line, IEnumerable<string> names)
        {
            if (string.IsNullOrEmpty(line) || Sensitive.IsMatch(line) || VaultProtection.Redact(line) != line)
                return null;
            line = Paths.Replace(line, "[pasta pessoal]");
            line = Email.Replace(line, "[e-mail]");
            line = Handles.Replace(line, "@[conta]");
            line = Addresses.Replace(line, match =>
            {
                IPAddress address;
                var ipv4Port = match.Value.Contains(".") && match.Value.Count(character => character == ':') == 1;
                var raw = ipv4Port ? match.Value.Split(':')[0] : match.Value;
                return IPAddress.TryParse(raw, out address) ? "[IP]" : match.Value;
            });
            line = Identifiers.Replace(line, "[identificador]");
            line = LongToken.Replace(line, "[valor longo]");
            foreach (string name in names)
                if (!string.IsNullOrWhiteSpace(name))
                    line = Regex.Replace(line, @"(?<![\p{L}\p{N}_])" + Regex.Escape(name) +
                        @"(?![\p{L}\p{N}_])", "[conta]", Options);
            return line;
        }
    }
}
