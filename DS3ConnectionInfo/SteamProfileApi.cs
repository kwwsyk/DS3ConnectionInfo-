using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace DS3ConnectionInfo
{
    public enum SteamQueryStatus { Unknown, Available, Timeout, NetworkError, LoginRequired, HttpError, InvalidResponse }

    public sealed class SteamProfileInfo
    {
        public int? VisibilityState { get; set; }
        public SteamQueryStatus ProfileStatus { get; set; }
        public SteamQueryStatus GamesStatus { get; set; }
        public int? ProfileHttpStatus { get; set; }
        public int? GamesHttpStatus { get; set; }
        public string Source { get; set; }
        public bool ProfileFailed { get; set; }
        public bool GamesFailed { get; set; }
        public bool GamesVisible { get; set; }
        public bool Approximate { get; set; }
        public Dictionary<uint, long?> Minutes { get; } = new Dictionary<uint, long?>();
    }

    // Injected transport allows offline fixture tests; production uses one shared client.
    public sealed class SteamProfileApi
    {
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 8 * 1024 * 1024
        };
        private readonly HttpClient http;
        private readonly string key;
        public SteamProfileApi(string apiKey, HttpClient client = null)
        {
            key = (apiKey ?? "").Trim();
            http = client ?? SharedHttp;
        }

        public async Task<SteamProfileInfo> GetAsync(ulong steamId, CancellationToken token = default(CancellationToken))
        {
            if (steamId == 0) throw new ArgumentException("Invalid Steam ID.");
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            string profile = "https://steamcommunity.com/profiles/" + id + "/";
            var result = new SteamProfileInfo { Approximate = key.Length == 0, Source = key.Length == 0 ? "Steam Community XML" : "Steam Web API" };
            try
            {
                if (key.Length == 0)
                {
                    XElement xml = await XmlAsync(profile + "?xml=1&l=english", "profile", id, token).ConfigureAwait(false);
                    int state;
                    if (int.TryParse((string)xml.Element("visibilityState"), out state) && state >= 1 && state <= 3)
                        result.VisibilityState = state;
                    else
                    {
                        string privacy = ((string)xml.Element("privacyState") ?? "").Trim().ToLowerInvariant();
                        if (privacy == "public") result.VisibilityState = 3;
                        else if (privacy == "private") result.VisibilityState = 1;
                        else if (privacy == "friendsonly" || privacy == "friends only") result.VisibilityState = 2;
                    }
                }
                else
                {
                    JObject json = await JsonAsync("ISteamUser/GetPlayerSummaries/v2/?steamids=" + id, token).ConfigureAwait(false);
                    JObject player = (json["response"]?["players"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["steamid"] == id);
                    if (player == null) throw new InvalidDataException("Missing player.");
                    int? state = (int?)player["communityvisibilitystate"];
                    if (state >= 1 && state <= 3) result.VisibilityState = state;
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); result.ProfileFailed = true; result.ProfileStatus = SteamQueryStatus.Timeout; }
            catch (Exception ex) when (ReadError(ex))
            {
                result.ProfileFailed = true;
                result.ProfileStatus = ErrorStatus(ex);
                result.ProfileHttpStatus = (ex as SteamReadException)?.HttpStatus;
            }
            if (result.VisibilityState.HasValue) result.ProfileStatus = SteamQueryStatus.Available;

            try
            {
                if (key.Length == 0)
                {
                    XElement xml = await XmlAsync(profile + "games/?tab=all&xml=1&l=english", "gamesList", id, token).ConfigureAwait(false);
                    XElement games = xml.Element("games");
                    result.GamesVisible = games != null;
                    if (games != null) foreach (XElement game in games.Elements("game"))
                    {
                        uint app;
                        if (!uint.TryParse((string)game.Element("appID"), out app) || !Wanted(app)) continue;
                        decimal hours;
                        long? minutes = null;
                        if (decimal.TryParse((string)game.Element("hoursOnRecord"), NumberStyles.Number, CultureInfo.InvariantCulture, out hours)
                            && hours >= 0 && hours <= long.MaxValue / 60m)
                            minutes = decimal.ToInt64(decimal.Round(hours * 60m, 0, MidpointRounding.AwayFromZero));
                        result.Minutes[app] = minutes;
                    }
                }
                else
                {
                    var input = new JObject { ["steamid"] = id, ["include_appinfo"] = false, ["include_played_free_games"] = true };
                    JObject json = await JsonAsync("IPlayerService/GetOwnedGames/v1/?input_json="
                        + Uri.EscapeDataString(input.ToString(Newtonsoft.Json.Formatting.None)), token).ConfigureAwait(false);
                    var response = json["response"] as JObject;
                    if (response == null) throw new InvalidDataException("Missing response.");
                    var games = response["games"] as JArray;
                    result.GamesVisible = games != null || response["game_count"] != null;
                    if (games != null) foreach (JObject game in games.OfType<JObject>())
                    {
                        uint? app = (uint?)game["appid"];
                        if (!app.HasValue || !Wanted(app.Value)) continue;
                        long? minutes = (long?)game["playtime_forever"];
                        result.Minutes[app.Value] = minutes >= 0 ? minutes : null;
                    }
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); result.GamesFailed = true; result.GamesStatus = SteamQueryStatus.Timeout; }
            catch (Exception ex) when (ReadError(ex))
            {
                result.GamesFailed = true;
                result.GamesStatus = ErrorStatus(ex);
                result.GamesHttpStatus = (ex as SteamReadException)?.HttpStatus;
            }
            if (result.GamesVisible) result.GamesStatus = SteamQueryStatus.Available;
            return result;
        }

        private sealed class SteamReadException : IOException
        {
            internal SteamQueryStatus Status { get; }
            internal int? HttpStatus { get; }
            internal SteamReadException(SteamQueryStatus status, int? httpStatus = null)
            { Status = status; HttpStatus = httpStatus; }
        }
        private static SteamQueryStatus ErrorStatus(Exception ex) => ex is SteamReadException error ? error.Status
            : ex is HttpRequestException ? SteamQueryStatus.NetworkError : SteamQueryStatus.InvalidResponse;
        private static void CheckHttp(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
                throw new SteamReadException(SteamQueryStatus.HttpError, (int)response.StatusCode);
        }

        private static bool Wanted(uint app) => app == 374320 || app == 1245620 || app == 2622380 || app == 2358720;
        private static bool ReadError(Exception ex) => ex is HttpRequestException || ex is IOException || ex is XmlException
            || ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException;

        private async Task<JObject> JsonAsync(string path, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.steampowered.com/" + path))
            {
                request.Headers.Add("x-webapi-key", key);
                using (var response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    CheckHttp(response);
                    return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
        private async Task<XElement> XmlAsync(string url, string rootName, string id, CancellationToken token)
        {
            using (var response = await http.GetAsync(url, token).ConfigureAwait(false))
            {
                CheckHttp(response);
                Uri final = response.RequestMessage?.RequestUri;
                if (final != null && (final.Host != "steamcommunity.com" || final.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase)))
                    throw new SteamReadException(final.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
                        ? SteamQueryStatus.LoginRequired : SteamQueryStatus.InvalidResponse);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 };
                using (var input = new StringReader(body))
                using (var reader = XmlReader.Create(input, settings))
                {
                    XElement root = XDocument.Load(reader).Root;
                    if (root == null || root.Name != XName.Get(rootName) || root.Element("error") != null
                        || ((string)root.Element("steamID64"))?.Trim() != id)
                        throw new InvalidDataException("Unexpected community XML.");
                    return root;
                }
            }
        }
    }
}
