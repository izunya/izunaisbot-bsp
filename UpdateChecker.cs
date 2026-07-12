using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using IPALogger = IPA.Logging.Logger;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 모드 자체 최신 릴리스 체크. GitHub Releases API 의 latest 응답에서 tag_name 을
    /// 자기 버전과 비교 → 새 버전이 있으면 웹 UI 배너 + 시작 시 시스템 메시지로 알림.
    ///
    /// 폴링: 시작 시 1회 + 24시간 간격 (rate limit: 60/h unauth, 1/24h 안전).
    /// 실패해도 모드 동작에 영향 없음 — 단순 알림 기능.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesApi = "https://api.github.com/repos/izunya/izunaisbot-bsp/releases/latest";
        private const string UserAgent = "izunaisbot-bsp/self-update";
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static Timer _timer;
        private static IPALogger _log;
        private static readonly object _lock = new object();

        /// <summary>최신 릴리스 tag (예: "v0.5.3"), 아직 못 받았으면 null.</summary>
        public static string LatestTag { get; private set; }

        /// <summary>최신 릴리스 GitHub 페이지 URL.</summary>
        public static string LatestUrl { get; private set; }

        /// <summary>마지막으로 체크한 UTC 시각, 없으면 null.</summary>
        public static DateTime? LastCheckedUtc { get; private set; }

        /// <summary>자기 버전보다 최신이 있으면 true. 알 수 없으면 false.</summary>
        public static bool UpdateAvailable
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(LatestTag) || Plugin.Self?.Version == null) return false;
                    var latestStr = LatestTag.TrimStart('v', 'V');
                    try
                    {
                        var latest = new SemVer.Version(latestStr);
                        return latest > Plugin.Self.Version;
                    }
                    catch { return false; }
                }
            }
        }

        public static void Start(IPALogger log)
        {
            _log = log;
            // 시작 직후 1회 + 24시간마다 폴링. 첫 호출 지연 5초 — 게임 부팅 부담 줄임.
            _timer?.Dispose();
            _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(5), PollInterval);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private static async void Check()
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, ReleasesApi))
                {
                    req.Headers.UserAgent.ParseAdd(UserAgent);
                    req.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using (var res = await Http.SendAsync(req).ConfigureAwait(false))
                    {
                        LastCheckedUtc = DateTime.UtcNow;
                        if (!res.IsSuccessStatusCode)
                        {
                            _log?.Debug("UpdateChecker: HTTP " + (int)res.StatusCode);
                            return;
                        }
                        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var obj = JObject.Parse(body);
                        var tag = obj["tag_name"]?.ToString();
                        var url = obj["html_url"]?.ToString();
                        if (string.IsNullOrEmpty(tag)) return;
                        lock (_lock)
                        {
                            LatestTag = tag;
                            LatestUrl = url;
                        }
                        if (UpdateAvailable)
                        {
                            _log?.Info("새 버전 사용 가능: " + tag + " (현재 " + Plugin.Self?.Version + ")");
                        }
                    }
                }
            }
            catch (Exception err)
            {
                _log?.Debug("UpdateChecker 실패: " + err.Message);
            }
        }
    }
}
