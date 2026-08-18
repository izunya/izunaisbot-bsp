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

        /// <summary>HttpListener 가 실제로 떠 있는지 (포트 충돌 등으로 실패했을 수 있음).</summary>
        public bool IsRunning => _listener != null;

        /// <summary>기본 브라우저로 웹 UI 를 연다 (인게임 버튼 / 실행 시 자동열기 공용).</summary>
        public void OpenInBrowser()
        {
            // 서버가 안 떴는데 열면 브라우저에 '연결할 수 없음' 페이지만 뜬다 → 원인을 로그로 안내.
            if (!IsRunning)
            {
                _log?.Warn("웹 UI 서버가 실행 중이 아니라 브라우저를 열지 않습니다 "
                           + (_config.WebUIEnabled
                               ? "(포트 " + _config.WebUIPort + " 시작 실패 — 포트 충돌 확인)"
                               : "(WebUIEnabled=false)"));
                return;
            }
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
                _running = false;
                // listener 는 떴는데 그 뒤(스레드 시작)에서 실패했을 수 있다 → 포트를 물고 있지 않게 정리.
                try { _listener?.Stop(); } catch { }
                try { _listener?.Close(); } catch { }
                _listener = null;
                Port = 0;   // 안 뜬 서버의 주소를 유효한 것처럼 노출하지 않는다
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

                // 다른 사이트가 브라우저를 통해 이 로컬 API 를 호출하지 못하게 한다.
                // (body/커스텀 헤더 없는 POST 는 CORS preflight 없이 그냥 나가는 'simple request'라
                //  /api/clearlog · /api/test-bridge 같은 라우트가 외부 페이지에서 호출될 수 있었다.)
                if (method == "POST" && !OriginAllowed(req))
                {
                    _log?.Warn("외부 Origin 의 POST 거부: " + req.Headers["Origin"] + " → " + path);
                    res.StatusCode = 403;
                    WriteText(res, "forbidden origin");
                    return;
                }

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
            catch (Exception err)
            {
                // 잘못된/깨진 JSON body 등 → 예전엔 예외가 그대로 올라가 본문 없는 200 이 나갔다.
                _log?.Warn("Web 요청 실패 (" + method + " " + path + "): " + err.Message);
                try
                {
                    res.StatusCode = 400;
                    WriteText(res, "bad request");
                }
                catch { /* 이미 응답을 쓰기 시작했으면 무시 */ }
            }
            finally
            {
                try { res.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Origin 헤더 검사. 페이지 자신의 fetch 는 http://localhost:{port} 를 보내고,
        /// 비브라우저 클라이언트는 Origin 을 안 보낸다. 그 외(다른 사이트)는 거부.
        /// </summary>
        private bool OriginAllowed(HttpListenerRequest req)
        {
            var origin = req.Headers["Origin"];
            if (string.IsNullOrEmpty(origin)) return true;
            return origin == "http://localhost:" + Port
                || origin == "http://127.0.0.1:" + Port;
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
