using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace DS3ConnectionInfo
{
    public sealed class SteamGameBadge
    {
        public string IconUri { get; }
        public string Text { get; }
        public string Detail { get; }
        public Visibility IconVisibility { get; }
        public Brush FrameBrush { get; }
        public bool ExceedsDs3 { get; }

        public SteamGameBadge(uint appId, SteamProfileInfo info, ulong steamId = 0)
        {
            IconUri = "pack://application:,,,/DS3ConnectionInfo;component/Resources/GameIcons/" + appId + ".jpg";
            long? minutes = null, ds3 = null;
            bool listed = info != null && info.Minutes.TryGetValue(appId, out minutes);
            if (info != null) info.Minutes.TryGetValue(374320, out ds3);
            IconVisibility = minutes > 0 ? Visibility.Visible : Visibility.Collapsed;
            ExceedsDs3 = minutes > 0 && ds3.HasValue && minutes.Value > ds3.Value;
            FrameBrush = ExceedsDs3 ? Brushes.Gold : Brushes.Transparent;
            Text = info == null ? "…" : minutes.HasValue
                ? (info.Approximate ? "~" : "") + (minutes.Value / 60.0).ToString("0.#", CultureInfo.InvariantCulture) + "h" : info.GamesFailed ? UiText.Current["Query" + info.GamesStatus] : "?";
            string key = appId == 1245620 ? "EldenRing" : appId == 2622380 ? "Nightreign" : "Wukong";
            Detail = UiText.Current[key] + ": " + (minutes.HasValue ? Text : UiText.Current[listed ? "PlaytimeHidden" : "GameUnknown"])
                + (ExceedsDs3 ? " — " + UiText.Current["MoreThanDs3"] : "")
                + (info == null ? "" : "\n" + info.Source + ": " + UiText.Current["Query" + info.GamesStatus]
                    + (info.GamesHttpStatus.HasValue ? " (HTTP " + info.GamesHttpStatus.Value + ")" : ""))
                + (steamId == 0 ? "" : "\nSteam ID: " + steamId.ToString(CultureInfo.InvariantCulture));
        }
    }

    public partial class Player
    {
        private static readonly SemaphoreSlim SteamQueries = new SemaphoreSlim(2, 2);
        private static SteamProfileApi steamApi;
        private bool steamQueryRunning;
        private DateTime nextSteamQueryUtc;
        private SteamProfileInfo steamInfo;
        public string SteamPrivacy => steamInfo == null ? "…" : steamInfo.VisibilityState == 3
            ? UiText.Current["PublicProfile"] : steamInfo.VisibilityState == 1 || steamInfo.VisibilityState == 2 ? "😎" : PrivacyUnknown;
        public string SteamPrivacyText => steamInfo == null ? "…" : steamInfo.VisibilityState == 3
            ? UiText.Current["PublicProfile"] : steamInfo.VisibilityState == 1 || steamInfo.VisibilityState == 2 ? UiText.Current["NonPublicProfile"] : PrivacyUnknown;
        private string PrivacyUnknown => steamInfo != null && steamInfo.ProfileFailed
            ? UiText.Current["Query" + steamInfo.ProfileStatus] : "?";
        public string SteamPrivacyDetail => UiText.Current["Description20"]
            + "\nSteam ID: " + SteamId64.ToString(CultureInfo.InvariantCulture)
            + (steamInfo == null ? "" : "\n" + steamInfo.Source + ": " + UiText.Current["Query" + steamInfo.ProfileStatus]
                + (steamInfo.ProfileHttpStatus.HasValue ? " (HTTP " + steamInfo.ProfileHttpStatus.Value + ")" : ""));
        public SteamGameBadge EldenRingBadge => new SteamGameBadge(1245620, steamInfo, SteamId64);
        public SteamGameBadge NightreignBadge => new SteamGameBadge(2622380, steamInfo, SteamId64);
        public SteamGameBadge WukongBadge => new SteamGameBadge(2358720, steamInfo, SteamId64);

        // Called only on the WPF Dispatcher thread; update the same Player instance after await.
        public async Task EnsureSteamInfoAsync()
        {
            if (steamQueryRunning || DateTime.UtcNow < nextSteamQueryUtc) return;
            steamQueryRunning = true;
            bool entered = false;
            try
            {
                if (steamApi == null) steamApi = new SteamProfileApi(Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY"));
                await SteamQueries.WaitAsync();
                entered = true;
                steamInfo = await steamApi.GetAsync(SteamId64);
                nextSteamQueryUtc = DateTime.UtcNow.AddMinutes(steamInfo.ProfileFailed || steamInfo.GamesFailed ? 1 : 10);
            }
            catch (Exception)
            {
                steamInfo = new SteamProfileInfo { ProfileFailed = true, GamesFailed = true, ProfileStatus = SteamQueryStatus.InvalidResponse, GamesStatus = SteamQueryStatus.InvalidResponse, Source = "Steam query" };
                nextSteamQueryUtc = DateTime.UtcNow.AddMinutes(1);
            }
            finally { if (entered) SteamQueries.Release(); steamQueryRunning = false; }
        }
    }
}
