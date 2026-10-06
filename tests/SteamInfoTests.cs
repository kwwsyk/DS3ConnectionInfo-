using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DS3ConnectionInfo;

internal static class SteamInfoTests
{
    private const string Id = "76561197960435530";
    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, HttpResponseMessage> Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Reply(request)); }
    }
    private static HttpResponseMessage Response(HttpRequestMessage request, string body)
    { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body), RequestMessage = request }; }
    internal static void Run(Action<bool, string> check)
    {
        UiText.Current.SelectLanguage("zh-CN");
        var handler = new FixtureHandler();
        handler.Reply = request =>
        {
            check(!request.Headers.Contains("x-webapi-key"), "XML fallback never sends a key");
            return Response(request, request.RequestUri.AbsolutePath.Contains("/games/")
                ? "<gamesList><steamID64>"+Id+"</steamID64><games><game><appID>374320</appID><hoursOnRecord>10.0</hoursOnRecord></game><game><appID>1245620</appID><hoursOnRecord>1,200.5</hoursOnRecord></game><game><appID>2622380</appID><hoursOnRecord>0</hoursOnRecord></game></games></gamesList>"
                : "<profile><steamID64>"+Id+"</steamID64><privacyState>private</privacyState></profile>");
        };
        using (var client = new HttpClient(handler))
        {
            var data = new SteamProfileApi("  ", client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(data.VisibilityState==1 && data.Approximate && data.Minutes[1245620]==72030, "XML privacy and localized hours parsed");
            var badge = new SteamGameBadge(1245620,data);
            check(badge.IconVisibility==Visibility.Visible && badge.ExceedsDs3, "positive playtime exceeding DS3 has icon and gold frame");
            check(new SteamGameBadge(2622380,data).IconVisibility==Visibility.Collapsed, "zero playtime hides icon");
            check(new SteamGameBadge(2358720,data).Text=="?", "missing game stays unknown");
            data.Minutes[1245620]=600;
            check(!new SteamGameBadge(1245620,data).ExceedsDs3, "equal playtime does not highlight");
            data.Minutes.Remove(374320);
            check(!new SteamGameBadge(1245620,data).ExceedsDs3, "unknown DS3 playtime does not highlight");
            data.Minutes[1245620]=null;
            check(new SteamGameBadge(1245620,data).IconVisibility==Visibility.Collapsed, "hidden playtime hides icon");

            handler.Reply = request => Response(request, request.RequestUri.AbsolutePath.Contains("/games/")
                ? "<html><body>Sign In</body></html>"
                : "<profile><steamID64>"+Id+"</steamID64><visibilityState>3</visibilityState></profile>");
            data = new SteamProfileApi(null,client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(data.VisibilityState==3 && data.GamesFailed && !data.Minutes.ContainsKey(1245620), "login HTML preserves public profile and unknown games");
            handler.Reply = request =>
            {
                if (request.RequestUri.AbsolutePath.Contains("/games/"))
                {
                    var response = Response(request, "");
                    response.RequestMessage = new HttpRequestMessage(HttpMethod.Get,"https://steamcommunity.com/login/?redir=games");
                    return response;
                }
                return Response(request,"<profile><steamID64>"+Id+"</steamID64><visibilityState>3</visibilityState></profile>");
            };
            data = new SteamProfileApi(null,client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(data.ProfileStatus==SteamQueryStatus.Available && data.GamesStatus==SteamQueryStatus.LoginRequired,
                "login redirect has explicit reason independent of public profile");
            check(new SteamGameBadge(1245620,data).Text==UiText.Current["QueryLoginRequired"],"login restriction is not an unexplained question mark");
            handler.Reply = request => { throw new HttpRequestException("fixture network failure"); };
            data = new SteamProfileApi(null,client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(data.ProfileStatus==SteamQueryStatus.NetworkError && data.GamesStatus==SteamQueryStatus.NetworkError,
                "network failures have explicit status");
            handler.Reply = request => new HttpResponseMessage((HttpStatusCode)429) { Content=new StringContent(""), RequestMessage=request };
            data = new SteamProfileApi(null,client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(data.ProfileStatus==SteamQueryStatus.HttpError && data.ProfileHttpStatus==429 && data.GamesHttpStatus==429,
                "HTTP status retained without leaking request credentials");
            handler.Reply = request => Response(request, "<profile><steamID64>1</steamID64><visibilityState>1</visibilityState></profile>");
            data = new SteamProfileApi(null,client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(!data.VisibilityState.HasValue && data.ProfileFailed, "wrong account XML is rejected");

            handler.Reply = request =>
            {
                check(request.RequestUri.Host=="api.steampowered.com" && request.Headers.Contains("x-webapi-key"), "defined key selects official API");
                return Response(request, request.RequestUri.AbsolutePath.Contains("GetOwnedGames")
                    ? "{\"response\":{\"game_count\":2,\"games\":[{\"appid\":374320,\"playtime_forever\":60},{\"appid\":1245620,\"playtime_forever\":61}]}}"
                    : "{\"response\":{\"players\":[{\"steamid\":\""+Id+"\",\"communityvisibilitystate\":3}]}}");
            };
            data = new SteamProfileApi("fixture-key",client).GetAsync(ulong.Parse(Id)).GetAwaiter().GetResult();
            check(!data.Approximate && new SteamGameBadge(1245620,data).ExceedsDs3, "API minute comparison uses exact values");
        }
    }
}
