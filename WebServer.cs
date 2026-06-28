using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using IPALogger = IPA.Logging.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 모드 자체 로컬 웹 설정 UI.
    /// System.Net.HttpListener 로 http://localhost:{port}/ 에 서빙 (로컬 전용, 관리자 권한 불필요).
    ///
    /// 라우트:
    ///   GET  /              → HTML 페이지
    ///   GET  /api/state     → 현재 상태(JSON: 연결/설정/채널/로그)
    ///   POST /api/config    → 설정 변경 + 저장 + 재접속
    ///   POST /api/channel   → 채널 음소거/해제
    ///   POST /api/clearlog  → 로그 비우기
    /// </summary>
    public class WebServer
    {
        private readonly DiscordChatService _service;
        private readonly ChzzkChatService _chzzk;
        private readonly Config _config;
        private readonly IPALogger _log;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public int Port { get; private set; }

        /// <summary>웹 UI 의 로컬 주소. 서버가 시작되지 않았으면 설정 포트 기준.</summary>
        public string Url => "http://localhost:" + (Port > 0 ? Port : _config.WebUIPort) + "/";

        /// <summary>기본 브라우저로 웹 UI 를 연다 (인게임 버튼 / 실행 시 자동열기 공용).</summary>
        public void OpenInBrowser()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Url) { UseShellExecute = true });
                _log?.Info("브라우저로 웹 UI 열기: " + Url);
            }
            catch (Exception err)
            {
                _log?.Warn("브라우저 열기 실패: " + err.Message);
            }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public WebServer(DiscordChatService service, ChzzkChatService chzzk, Config config, IPALogger log)
        {
            _service = service;
            _chzzk = chzzk;
            _config = config;
            _log = log;
        }

        public void Start()
        {
            if (!_config.WebUIEnabled)
            {
                _log?.Info("Local web UI 비활성화됨 (WebUIEnabled=false)");
                return;
            }

            Port = _config.WebUIPort;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:" + Port + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "izunaisbot-webui" };
                _thread.Start();
                _log?.Info("Local web UI: http://localhost:" + Port + "/");
            }
            catch (Exception err)
            {
                _log?.Error("Local web UI 시작 실패 (포트 " + Port + "): " + err.Message);
                _listener = null;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) break; else continue; }

                try { Handle(ctx); }
                catch (Exception err) { _log?.Warn("Web 요청 처리 실패: " + err.Message); }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            var path = req.Url.AbsolutePath;
            var method = req.HttpMethod;

            try
            {
                res.Headers["Cache-Control"] = "no-store";

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    WriteHtml(res, WebUiPage.Html);
                    return;
                }
                if (method == "GET" && path == "/api/state")
                {
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/config")
                {
                    HandleConfig(ReadBody(req));
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/channel")
                {
                    HandleChannel(ReadBody(req));
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/clearlog")
                {
                    _service.ClearLog();
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/chzzk/config")
                {
                    HandleChzzkConfig(ReadBody(req));
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/chzzk/clearlog")
                {
                    _chzzk?.ClearLog();
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/pair-receive")
                {
                    // 모드 웹 UI 가 봇 페어링 API 에서 받은 raw 토큰을 여기로 전달.
                    // 이 라우트는 localhost 전용이라 origin 검증 없음 (HttpListener 가 이미 localhost 만 바인딩).
                    HandlePairReceive(ReadBody(req));
                    WriteJson(res, BuildState());
                    return;
                }
                if (method == "POST" && path == "/api/test-bridge")
                {
                    var sent = _service.SendBridgeTest();
                    WriteJson(res, new { sent });
                    return;
                }

                res.StatusCode = 404;
                WriteText(res, "not found");
            }
            finally
            {
                try { res.OutputStream.Close(); } catch { }
            }
        }

        private object BuildState()
        {
            return new
            {
                connected = _service.Connected,
                gaveUp = _service.GaveUp,
                statusReason = _service.StatusReason,
                chatModuleEnabled = BspChatGate.ChatEnabled,
                url = _config.Url,
                token = _config.Token,
                tokenSet = _service.TokenSet,
                autoReconnect = _config.AutoReconnect,
                reconnectIntervalSec = _config.ReconnectIntervalSec,
                forwardOnlyCommands = _config.ForwardOnlyCommands,
                openWebOnLaunch = _config.OpenWebOnLaunch,
                port = Port,
                botApiBase = _config.BotApiBase,
                modVersion = Plugin.Self?.Version?.ToString(),
                latestVersion = UpdateChecker.LatestTag,
                latestUrl = UpdateChecker.LatestUrl,
                updateAvailable = UpdateChecker.UpdateAvailable,
                lastMessageUtc = _service.LastMessageUtc?.ToString("o"),
                channels = _service.GetChannels(),
                log = _service.GetRecentLog(120),

                // ---- 치지직(Chzzk) ----
                chzzkEnabled = _config.ChzzkEnabled,
                chzzkChannelId = _config.ChzzkChannelId,
                chzzkConnected = _chzzk?.Connected ?? false,
                chzzkChannelName = _chzzk?.ChannelName,
                chzzkLiveTitle = _chzzk?.LiveTitle,
                chzzkStatus = _chzzk?.StatusReason,
                chzzkLastMessageUtc = _chzzk?.LastMessageUtc?.ToString("o"),
                chzzkLog = _chzzk?.GetRecentLog(80),

                // ---- Bridge test ----
                bridgeTest = new
                {
                    sentUtc = _service.LastTestSentUtc?.ToString("o"),
                    ackUtc = _service.LastTestAckUtc?.ToString("o"),
                    ok = _service.LastTestOk,
                    detail = _service.LastTestDetail,
                    channelId = _service.LastTestChannelId,
                }
            };
        }

        private void HandleConfig(string body)
        {
            var j = string.IsNullOrEmpty(body) ? new JObject() : JObject.Parse(body);

            if (j["url"] != null) _config.Url = (j["url"].ToString() ?? "").Trim();
            if (j["token"] != null) _config.Token = (j["token"].ToString() ?? "").Trim();
            if (j["autoReconnect"] != null) _config.AutoReconnect = j["autoReconnect"].ToObject<bool>();
            if (j["reconnectIntervalSec"] != null) _config.ReconnectIntervalSec = Math.Max(1, j["reconnectIntervalSec"].ToObject<int>());
            if (j["forwardOnlyCommands"] != null) _config.ForwardOnlyCommands = j["forwardOnlyCommands"].ToObject<bool>();
            if (j["openWebOnLaunch"] != null) _config.OpenWebOnLaunch = j["openWebOnLaunch"].ToObject<bool>();
            if (j["botApiBase"] != null) _config.BotApiBase = (j["botApiBase"].ToString() ?? "").Trim();

            _service.SaveAndReconnect();
        }

        /// <summary>치지직 설정(채널 ID / 사용 여부) 변경 — 저장 + 재시작.</summary>
        private void HandleChzzkConfig(string body)
        {
            var j = string.IsNullOrEmpty(body) ? new JObject() : JObject.Parse(body);

            if (j["chzzkEnabled"] != null) _config.ChzzkEnabled = j["chzzkEnabled"].ToObject<bool>();
            if (j["chzzkChannelId"] != null) _config.ChzzkChannelId = (j["chzzkChannelId"].ToString() ?? "").Trim();

            _chzzk?.Reconfigure();
        }

        private void HandlePairReceive(string body)
        {
            try
            {
                var j = JObject.Parse(body);
                var token = j["token"]?.ToString();
                var wsUrl = j["wsUrl"]?.ToString();
                if (string.IsNullOrEmpty(token)) return;
                _config.Token = token.Trim();
                if (!string.IsNullOrEmpty(wsUrl)) _config.Url = wsUrl.Trim();
                _service.SaveAndReconnect();
                _log?.Info("Paired token received via /api/pair-receive");
            }
            catch (Exception err)
            {
                _log?.Warn("pair-receive failed: " + err.Message);
            }
        }

        private void HandleChannel(string body)
        {
            var j = JObject.Parse(body);
            var id = j["id"]?.ToString();
            var enabled = j["enabled"]?.ToObject<bool>() ?? true;
            _service.SetChannelEnabled(id, enabled);
        }

        // ---- helpers ----

        private static string ReadBody(HttpListenerRequest req)
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static void WriteHtml(HttpListenerResponse res, string html)
        {
            res.ContentType = "text/html; charset=utf-8";
            WriteBytes(res, Encoding.UTF8.GetBytes(html));
        }

        private static void WriteJson(HttpListenerResponse res, object obj)
        {
            res.ContentType = "application/json; charset=utf-8";
            WriteBytes(res, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, JsonSettings)));
        }

        private static void WriteText(HttpListenerResponse res, string text)
        {
            res.ContentType = "text/plain; charset=utf-8";
            WriteBytes(res, Encoding.UTF8.GetBytes(text));
        }

        private static void WriteBytes(HttpListenerResponse res, byte[] bytes)
        {
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
