using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Controls;

namespace RobloxMultiByJayy
{
    public partial class MainWindow
    {
        private TextBlock discordStatusLabel;
        private string discordStatus = "Desativado";
        private bool discordAccepted;

        private void UpdateDiscordPresence()
        {
            if (!showDiscordPresence || discordRpc == null) return;

            var active = accounts.Where(IsAccountRunning).ToList();
            var game = active.FirstOrDefault(account => IsPlaceId(account.CurrentPlaceId));
            string place = game == null ? null : game.CurrentPlaceId;
            string name = game == null ? "Roblox" : game.CurrentGame;
            if (game != null && string.IsNullOrWhiteSpace(name))
            {
                name = GetGameName(place);
                ResolveGameNameAsync(place, null, null);
            }
            if (place != null) FetchGameThumbnailAsync(place);

            if (active.Count == 0)
            {
                activeGameSessionPlaceId = null;
                activeGameSessionStartTime = null;
            }
            else if (!activeGameSessionStartTime.HasValue || activeGameSessionPlaceId != place)
            {
                activeGameSessionPlaceId = place;
                activeGameSessionStartTime = DateTime.UtcNow;
            }

            string cover;
            placeThumbnails.TryGetValue(place ?? "", out cover);
            long? started = activeGameSessionStartTime.HasValue
                ? new DateTimeOffset(activeGameSessionStartTime.Value).ToUnixTimeSeconds() : (long?)null;
            discordRpc.SetActivity(CreatePresenceActivity(accounts.Count, active.Count, name, place, started, cover));
        }

        private static string CreatePresenceActivity(int saved, int active, string gameName, string place,
            long? started, string cover)
        {
            var activity = new Dictionary<string, object> { { "type", 0 } };
            if (active == 0)
            {
                activity["details"] = "No menu principal";
                activity["state"] = "Gerenciando " + saved + (saved == 1 ? " conta" : " contas");
            }
            else
            {
                string details = "Jogando: " + (string.IsNullOrWhiteSpace(gameName) ? "Roblox" : gameName);
                activity["details"] = details.Length <= 128 ? details : details.Substring(0, 127) + "…";
                activity["state"] = active + (active == 1 ? " conta em jogo" : " contas em jogo");
                if (started.HasValue && started.Value > 0)
                    activity["timestamps"] = new { start = started.Value };
            }
            var github = new { label = "Abrir GitHub", url = "https://github.com/jaysloncarvalho/Roblox-Multi-Plus" };
            activity["buttons"] = active > 0 && IsPlaceId(place)
                ? new[] { new { label = "Abrir página do jogo", url = "https://www.roblox.com/games/" + place }, github }
                : new[] { github };
            if (active > 0 && !string.IsNullOrEmpty(cover))
                activity["assets"] = new { large_image = cover };
            return new JavaScriptSerializer().Serialize(activity);
        }

        private static bool IsPlaceId(string place)
        {
            long id;
            return long.TryParse(place, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        private void FetchGameThumbnailAsync(string place)
        {
            if (placeThumbnails.ContainsKey(place) || !fetchingThumbnails.Add(place)) return;
            Task.Run(async () =>
            {
                string image = null;
                try
                {
                    using (var client = new WebClient())
                    {
                        string url = "https://thumbnails.roblox.com/v1/places/gameicons?placeIds=" + place + "&size=512x512&format=Png&isCircular=false";
                        string json = await client.DownloadStringTaskAsync(url);
                        var response = new JavaScriptSerializer().Deserialize<ThumbnailResponse>(json);
                        string candidate = response == null || response.data == null ? null
                            : response.data.Select(item => item == null ? null : item.imageUrl).FirstOrDefault(IsHttpsImage);
                        image = candidate;
                    }
                }
                catch (WebException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                await Dispatcher.InvokeAsync(new Action(() =>
                {
                    fetchingThumbnails.Remove(place);
                    // Cache an unavailable cover too. Discord will use the application's icon.
                    placeThumbnails[place] = image;
                    if (image != null) UpdateDiscordPresence();
                }));
            });
        }

        private static bool IsHttpsImage(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        private sealed class ThumbnailResponse
        {
            public ThumbnailItem[] data { get; set; }
        }

        private sealed class ThumbnailItem
        {
            public string imageUrl { get; set; }
        }

        private void SetDiscordStatus(string text, bool accepted)
        {
            discordStatus = text;
            discordAccepted = accepted;
            if (discordStatusLabel != null)
            {
                discordStatusLabel.Text = text;
                discordStatusLabel.Foreground = Palette(accepted ? "Good" : "Muted");
            }
        }
    }
}
