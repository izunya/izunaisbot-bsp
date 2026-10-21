using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using CP_SDK.Chat;
using CP_SDK.Chat.Interfaces;
using CP_SDK.Chat.Services;
using IPALogger = IPA.Logging.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WebSocketSharp;

namespace IzunaisbotBSP
{
    /// <summary>
    /// Discord → BeatSaberPlus 채팅 브리지.
    ///
    /// 흐름:
    ///   1) Start() 호출 시 봇 WS 서버에 접속 (Config.Url + ?token=Config.Token)
    ///   2) 봇이 푸시하는 JSON 메시지 파싱 → IChatMessage 생성
    ///   3) m_OnTextMessageReceivedCallbacks.InvokeAll → ChatServiceMultiplexer 가
    ///      이 이벤트를 모든 구독자(BSP_Chat UI, BSP_ChatRequest 등) 에 전달
    ///
    /// 봇 측 페이로드 (utils/bspBridge.js 와 1:1):
    ///   { "type": "message",
    ///     "channel": { "id", "name", "guildId", "guildName" },
    ///     "user":    { "id", "name", "color", "avatar", "bot" },
    ///     "content": "!bsr abc",
    ///     "timestamp": "ISO8601" }
    /// </summary>
    public class DiscordChatService : ChatServiceBase, IChatService
    {
        public string DisplayName => "Discord";
        public Color AccentColor => new Color(0.345f, 0.396f, 0.949f);  // Discord blurple

        private readonly Config _config;
        private readonly IPALogger _log;
        private readonly object _lock = new object();
        private readonly Dictionary<string, DiscordChatChannel> _channels = new Dictionary<string, DiscordChatChannel>();

        private WebSocket _ws;
        private volatile bool _running;
        private volatile bool _connected;
        private Timer _reconnectTimer;

        // ---- 재접속 실패 추적 (무한 재접속 방지) ----
        // 접속 직후 곧바로(MinHealthySeconds 이내) 끊기거나 아예 못 열면 1회 "실패"로 센다.
        // 연속 MaxConsecutiveFailures 회 실패하거나, 서버가 4xxx(앱 정의) close 코드로 끊으면
        // → 토큰 무효/디스코드 미연동으로 보고 재접속을 멈추고 사용자에게 안내한다.
        private int _consecutiveFailures;
        // 현재 소켓이 열린 시각. 매 접속 시도 시작(Connect)마다 초기화된다 —
        // 여기에 이전 연결의 값이 남으면 "오래 유지됐다"로 오판해 실패 집계가 무력화된다.
        private DateTime _lastOpenUtc;
        private volatile bool _gaveUp;
        private string _statusReason = "";
        private const int MaxConsecutiveFailures = 5;
        private const int MinHealthySeconds = 15;

        // ---- 웹 UI 용 상태/로그/채널 통계 ----
        private readonly object _logLock = new object();
        private readonly LinkedList<LogEntry> _recentLog = new LinkedList<LogEntry>();
        private readonly Dictionary<string, ChannelStat> _channelStats = new Dictionary<string, ChannelStat>();
        private DateTime? _lastMessageUtc;
        private const int MaxLog = 200;

        // ---- !bsr → 디스코드 알림 추적 ----
        private static readonly Regex BsrRegex = new Regex(@"^!bsr\s+([0-9a-fA-F]{1,8})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly object _pendingLock = new object();

        // 게임 안 BSP_Chat 오버레이에 모듈 응답(!bsr/!queue/!oops 등의 결과)을 띄울 때 쓰는
        // 합성 발신자. ChatRequest/wipbot 응답을 플레이어가 게임 화면에서 직접 보도록 대리 표시한다.
        private static readonly DiscordChatUser BotUser = new DiscordChatUser("izunaisbot-bot", "izunaisbot", "#5865F2");
        private readonly Dictionary<string, PendingBsr> _pendingBsr = new Dictionary<string, PendingBsr>(StringComparer.OrdinalIgnoreCase);
        private Timer _pendingGcTimer;

        private class PendingBsr
        {
            public string Code;
            public string ChannelId;
            public string UserName;
            public DateTime ExpiresUtc;
        }

        // ---- 일반 명령 트래커 (!oops/!queue/!link/!block/!unblock/!wip 등) ----
        // BSP_ChatRequest / wipbot 등 IChatService 를 듣는 모든 플러그인이 응답하면
        // 그 응답을 봇으로 forward 해서 디스코드에 표시한다.
        // 응답은 1초 디바운스로 모아 multi-line (!queue 등) 도 한 번에 송신.
        private static readonly Regex CommandRegex = new Regex(@"^!(\w{1,16})(?:\s+(.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> TrackedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "oops", "wrong", "queue", "link", "np", "nowplaying",
            "skip", "remove", "unblock", "block", "who",
            "wip",   // wipbot
        };
        private readonly Dictionary<string, PendingCommand> _pendingCommands =
            new Dictionary<string, PendingCommand>(StringComparer.OrdinalIgnoreCase);

        private class PendingCommand
        {
            public string Command;
            public string Args;
            public string ChannelId;
            public string UserName;
            public DateTime ExpiresUtc;
            public List<string> Responses;
            public Timer FlushTimer;
        }

        public DiscordChatService(Config config, IPALogger log)
        {
            _config = config;
            _log = log;
        }

        // 웹 UI 로 넘기는 직렬화용 DTO (public 필드 → Newtonsoft 가 그대로 직렬화)
        public class ChannelInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public long Count { get; set; }
            public bool Enabled { get; set; }
        }

        public class LogEntry
        {
            public string Time { get; set; }
            public string Channel { get; set; }
            public string User { get; set; }
            public string Content { get; set; }
            public bool Forwarded { get; set; }
        }

        private class ChannelStat
        {
            public string Id;
            public string Name;
            public long Count;
        }

        public ReadOnlyCollection<(IChatService, IChatChannel)> Channels
        {
            get
            {
                lock (_lock)
                {
                    return _channels.Values
                        .Select(c => ((IChatService)this, (IChatChannel)c))
                        .ToList()
                        .AsReadOnly();
                }
            }
        }

        // ================================================================
        // IChatService — lifecycle
        // ================================================================

        public void Start()
        {
            _running = true;
            ResetFailureState();
            Connect();
        }

        /// <summary>재접속 실패 카운터/포기 상태 초기화 (시작·수동 재접속 시).</summary>
        private void ResetFailureState()
        {
            _consecutiveFailures = 0;
            _lastOpenUtc = default(DateTime);
            _gaveUp = false;
            SetStatus("");
        }

        private void SetStatus(string reason)
        {
            lock (_lock) { _statusReason = reason ?? ""; }
        }

        public void Stop()
        {
            _running = false;
            DisposeSocket();
        }

        public bool IsConnectedAndLive() => _connected;
        public string PrimaryChannelName() => "Discord";
        public void RecacheEmotes() { }

        // ================================================================
        // 로컬 웹 UI 지원 — 상태 조회 / 로그 / 채널·필터 제어
        // ================================================================

        public bool Connected => _connected;
        public string CurrentUrl => _config.Url;
        public bool TokenSet => !string.IsNullOrEmpty(_config.Token);

        /// <summary>연속 실패/인증거부로 자동 재접속을 포기한 상태인지 (웹 UI 표시용).</summary>
        public bool GaveUp => _gaveUp;

        /// <summary>마지막 상태 사유 메시지 (연결 끊김/포기 안내, 웹 UI 표시용).</summary>
        public string StatusReason { get { lock (_lock) { return _statusReason; } } }

        public DateTime? LastMessageUtc
        {
            get { lock (_logLock) { return _lastMessageUtc; } }
        }

        // ---- Bridge test (게임 → 봇 → 등록 채널에 ping 임베드) ----
        private readonly object _testLock = new object();
        private DateTime? _lastTestSentUtc;
        private DateTime? _lastTestAckUtc;
        private bool? _lastTestOk;
        private string _lastTestDetail;
        private string _lastTestChannelId;

        public DateTime? LastTestSentUtc { get { lock (_testLock) { return _lastTestSentUtc; } } }
        public DateTime? LastTestAckUtc { get { lock (_testLock) { return _lastTestAckUtc; } } }
        public bool? LastTestOk { get { lock (_testLock) { return _lastTestOk; } } }
        public string LastTestDetail { get { lock (_testLock) { return _lastTestDetail; } } }
        public string LastTestChannelId { get { lock (_testLock) { return _lastTestChannelId; } } }

        /// <summary>
        /// 봇으로 bridge_test 메시지 전송 — 봇이 등록 채널 중 첫번째에 ping 임베드 게시.
        /// 결과는 bridge_test_ack 로 비동기 도착 → LastTestOk/Detail 로 폴링.
        /// </summary>
        public bool SendBridgeTest()
        {
            var ws = _ws;   // 스냅샷 — 재접속으로 교체돼도 NRE 없이 진행
            if (ws == null || !_connected) return false;
            var obj = new JObject { ["type"] = "bridge_test" };
            try
            {
                ws.Send(obj.ToString(Formatting.None));
                lock (_testLock)
                {
                    _lastTestSentUtc = DateTime.UtcNow;
                    _lastTestAckUtc = null;
                    _lastTestOk = null;
                    _lastTestDetail = null;
                    _lastTestChannelId = null;
                }
                return true;
            }
            catch (Exception err)
            {
                _log?.Warn("bridge_test send 실패: " + err.Message);
                return false;
            }
        }

        /// <summary>채널 목록 + 누적 수신 수 + 음소거 여부 (수신 많은 순).</summary>
        public List<ChannelInfo> GetChannels()
        {
            lock (_lock)
            {
                var muted = _config.DisabledChannels ?? new List<string>();
                return _channelStats.Values
                    .Select(s => new ChannelInfo
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Count = s.Count,
                        Enabled = !muted.Contains(s.Id)
                    })
                    .OrderByDescending(c => c.Count)
                    .ToList();
            }
        }

        /// <summary>최근 수신 메시지 (신규순).</summary>
        public List<LogEntry> GetRecentLog(int max = 100)
        {
            lock (_logLock)
            {
                return _recentLog.Take(Math.Max(1, max)).ToList();
            }
        }

        public void ClearLog()
        {
            lock (_logLock) { _recentLog.Clear(); }
        }

        /// <summary>채널 음소거/해제 후 저장.</summary>
        public void SetChannelEnabled(string channelId, bool enabled)
        {
            if (string.IsNullOrEmpty(channelId)) return;
            if (_config.DisabledChannels == null) _config.DisabledChannels = new List<string>();
            if (enabled) _config.DisabledChannels.RemoveAll(x => x == channelId);
            else if (!_config.DisabledChannels.Contains(channelId)) _config.DisabledChannels.Add(channelId);
            Config.Save();
        }

        /// <summary>웹에서 설정 변경 후 호출 — 저장 + 즉시 재접속.</summary>
        public void SaveAndReconnect()
        {
            Config.Save();
            _log?.Info("Config updated via local web → reconnecting: " + _config.Url);
            DisposeSocket();
            ResetFailureState();   // 사용자가 설정을 바꿨으니 포기 상태 해제하고 새로 시도
            Connect();
        }

        // ================================================================
        // IChatService — web UI
        //   BSP 웹 설정 페이지에 폼을 띄워, 게임 재시작 없이 실시간으로
        //   Url/Token 등을 수정 → 저장 즉시 재접속.
        // ================================================================

        public string WebPageHTMLForm()
        {
            return
                "<div class='form-group'>" +
                "  <label>Token</label>" +
                "  <input type='text' class='form-control' name='Token' value='" + Escape(_config.Token) + "' placeholder='bsp_...'>" +
                "</div>" +
                "<div class='form-group'>" +
                "  <label>Reconnect interval (sec)</label>" +
                "  <input type='number' min='1' class='form-control' name='ReconnectIntervalSec' value='" + _config.ReconnectIntervalSec + "'>" +
                "</div>" +
                "<div class='form-check'>" +
                "  <input type='checkbox' class='form-check-input' id='izunaisbot-autoreconnect' name='AutoReconnect' " + (_config.AutoReconnect ? "checked" : "") + ">" +
                "  <label class='form-check-label' for='izunaisbot-autoreconnect'>Auto reconnect</label>" +
                "</div>";
        }

        public string WebPageHTML() => "";
        public string WebPageJS() => "";
        public string WebPageJSValidate() => "";

        public void WebPageOnPost(Dictionary<string, string> postData)
        {
            if (postData == null) return;

            if (postData.TryGetValue("Url", out var url))
                _config.Url = (url ?? "").Trim();
            if (postData.TryGetValue("Token", out var token))
                _config.Token = (token ?? "").Trim();

            // 체크박스: 값이 false/0/off 가 아니면 켜진 것으로 본다. 키 자체가 없으면 꺼짐.
            _config.AutoReconnect = postData.TryGetValue("AutoReconnect", out var ar)
                && (ar == null || (ar != "false" && ar != "0" && ar != "off"));

            if (postData.TryGetValue("ReconnectIntervalSec", out var ivStr) && int.TryParse(ivStr, out var iv))
                _config.ReconnectIntervalSec = Math.Max(1, iv);

            Config.Save();
            _log?.Info("Config updated via web → reconnecting: " + _config.Url);

            // 재시작 없이 새 Url/Token 으로 즉시 재접속
            DisposeSocket();
            ResetFailureState();
            Connect();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&#39;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");
        }

        // ================================================================
        // IChatService — outbound (BSP → Discord) — 향후 확장
        // ================================================================

        public void SendTextMessage(IChatChannel channel, string message)
        {
            // BSP_ChatRequest / wipbot 등 다른 모듈이 응답을 우리 service 의 채널로 보내면 여기로 들어옴.
            // 1) !bsr 매칭 → bsr_request 이벤트 (기존)
            // 2) 일반 명령 (!oops/!link/!queue/!wip 등) 매칭 → bsp_command 이벤트 (디바운스 후 송신)
            TryMatchBsrResponse(message);
            TryMatchCommandResponse(message);
            // 3) 게임 안 BSP_Chat 오버레이에도 응답을 띄워 VR 플레이어가 직접 보게 한다.
            EchoToOverlay(channel, message);
        }

        /// <summary>
        /// 모듈이 SendTextMessage 로 보낸 응답을 게임 안 BSP_Chat 오버레이에 표시한다.
        /// 오버레이는 수신(OnTextMessageReceived) 메시지만 그리므로, 응답을 합성 봇 발신자로
        /// 되울려(InvokeAll) 띄운다. 구독자(BSP_Chat/ChatRequest)에게만 가고 우리 HandleMessage
        /// 로는 안 돌아오므로 루프 없음. 응답 텍스트는 '!' 로 시작하지 않아 명령 재실행도 없음.
        /// </summary>
        private void EchoToOverlay(IChatChannel channel, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            try
            {
                var ch = channel ?? AnyChannel();
                if (ch == null) return;
                var msg = new DiscordChatMessage(
                    id: Guid.NewGuid().ToString(),
                    text: message,
                    sender: BotUser,
                    channel: ch
                );
                m_OnTextMessageReceivedCallbacks?.InvokeAll(this, msg);
            }
            catch (Exception err) { _log?.Warn("in-game echo 실패: " + err.Message); }
        }

        /// <summary>발신 채널이 안 주어졌을 때 쓸 임의의 알려진 채널 (없으면 null).</summary>
        private DiscordChatChannel AnyChannel()
        {
            lock (_lock)
            {
                foreach (var c in _channels.Values) return c;
            }
            return null;
        }

        // ================================================================
        // IChatService — temp channels (디스코드에선 의미 없음 → no-op)
        // ================================================================

        public void JoinTempChannel(string groupIdentifier, string channelName, string prefix, bool canSendMessage) { }
        public void LeaveTempChannel(string channelName) { }
        public bool IsInTempChannel(string channelName) => false;
        public void LeaveAllTempChannel(string groupIdentifier) { }

        // ================================================================
        // WebSocket
        // ================================================================

        private void Connect()
        {
            if (!_running) return;
            if (string.IsNullOrEmpty(_config.Url) || string.IsNullOrEmpty(_config.Token))
            {
                m_OnSystemMessageCallbacks?.InvokeAll(
                    (IChatService)this,
                    "Discord: URL/Token 미설정. UserData\\izunaisbot-bsp.json 편집 후 게임 재시작."
                );
                _log?.Warn("Url 또는 Token 비어있음");
                return;
            }

            try
            {
                DisposeSocket();
                // 이번 시도는 아직 안 열렸다 → 직전 연결의 값 제거 (실패 집계 정확도)
                _lastOpenUtc = default(DateTime);

                // 토큰을 Cookie 헤더로 전송 (URL query 대신 — Cloudflare access log 노출 회피)
                var ws = new WebSocket(_config.Url);
                _ws = ws;
                ws.SetCookie(new WebSocketSharp.Net.Cookie("bsp_token", _config.Token));

                // 모든 핸들러는 "자기 소켓이 아직 현재 소켓인지" 먼저 확인한다.
                // 의도적 종료(DisposeSocket)나 재접속으로 교체된 옛 소켓의 늦은 이벤트가
                // 새 연결의 _connected 를 덮어쓰거나 실패로 집계되는 것을 막는다.
                ws.OnOpen += (s, e) =>
                {
                    if (!IsCurrent(ws)) return;
                    _connected = true;
                    _lastOpenUtc = DateTime.UtcNow;
                    SetStatus("connected");
                    _log?.Info("Connected: " + _config.Url);
                    SendHello();   // 봇이 모드/BS/관련 플러그인 버전 알 수 있도록 자기 소개
                    m_OnSystemMessageCallbacks?.InvokeAll((IChatService)this, "Discord: connected");
                    m_OnLoginCallbacks?.InvokeAll((IChatService)this);
                };
                ws.OnMessage += (s, e) => { if (IsCurrent(ws)) HandleMessage(e.Data); };
                ws.OnError += (s, e) => { if (IsCurrent(ws)) _log?.Warn("WS error: " + e.Message); };
                ws.OnClose += (s, e) =>
                {
                    if (!IsCurrent(ws))   // 의도적 종료 / 교체된 옛 소켓 → 집계·재접속 안 함
                    {
                        _log?.Info("Closed (stale or intentional): code=" + e.Code);
                        return;
                    }
                    _connected = false;
                    _log?.Info("Closed: code=" + e.Code + " reason=" + e.Reason);
                    m_OnSystemMessageCallbacks?.InvokeAll(
                        (IChatService)this,
                        "Discord: disconnected (" + e.Code + ")"
                    );
                    HandleDisconnect(e.Code, e.Reason);
                };
                ws.ConnectAsync();
            }
            catch (Exception err)
            {
                _log?.Error("Connect failed: " + err);
                ScheduleReconnect();
            }
        }

        /// <summary>이 소켓이 아직 "현재" 소켓인지 (교체된 옛 소켓의 늦은 콜백 무시용).</summary>
        private bool IsCurrent(WebSocket ws) => ReferenceEquals(ws, _ws);

        /// <summary>
        /// 연결이 끊겼을 때 호출 — 실패를 집계하고, 한계를 넘으면 재접속을 포기한다.
        /// </summary>
        private void HandleDisconnect(ushort code, string reason)
        {
            // 4000~4999 = 애플리케이션 정의 close 코드. 서버(#1)가 인증 실패/토큰 거부 등
            // "재접속해도 소용없는" 영구 거부를 알릴 때 이 범위를 쓰기로 약속한다. → 즉시 포기.
            bool fatal = code >= 4000 && code <= 4999;

            // 접속을 충분히 오래(MinHealthySeconds 이상) 유지했었다면 정상 운영 중 끊김으로 보고
            // 카운터를 리셋. 그렇지 않으면(곧바로 끊김 / 아예 못 열림) 실패로 누적.
            var held = _lastOpenUtc == default(DateTime)
                ? 0.0
                : (DateTime.UtcNow - _lastOpenUtc).TotalSeconds;
            if (_lastOpenUtc != default(DateTime) && held >= MinHealthySeconds)
                _consecutiveFailures = 0;
            else
                _consecutiveFailures++;

            if (fatal || _consecutiveFailures >= MaxConsecutiveFailures)
            {
                _gaveUp = true;
                var msg = fatal
                    ? FatalCloseMessage(code)
                    : "Discord: 연결에 " + _consecutiveFailures + "회 연속 실패했습니다. 토큰/디스코드 연동을 확인하세요. 자동 재접속을 멈춥니다 (웹 UI 에서 저장하면 다시 시도).";
                SetStatus(msg);
                _log?.Warn("Auto-reconnect 중단: code=" + code + " fails=" + _consecutiveFailures + " fatal=" + fatal);
                m_OnSystemMessageCallbacks?.InvokeAll((IChatService)this, msg);
                return;   // 재접속 스케줄하지 않음
            }

            SetStatus("disconnected (code " + code + "), 재접속 시도 중…");
            ScheduleReconnect();
        }

        /// <summary>
        /// 봇 서버가 보내는 4xxx(앱 정의) close 코드별 안내 메시지.
        ///   4000 bad URL       — WS 주소가 잘못됨(설정 문제)
        ///   4001 unauthorized  — 토큰이 거부됨(무효/잘못된 토큰)  ← 핵심: 즉시 포기
        ///   4002 token revoked — 대시보드에서 토큰이 해지됨
        /// </summary>
        private static string FatalCloseMessage(ushort code)
        {
            switch (code)
            {
                case 4000:
                    return "Discord: 서버 주소(URL)가 잘못됐습니다 (code 4000). 웹 UI 에서 URL 설정을 확인하세요.";
                case 4001:
                    return "Discord: 토큰이 거부됐습니다 (code 4001). 토큰이 무효하거나 잘못됐어요. 웹 UI 에서 다시 페어링하세요.";
                case 4002:
                    return "Discord: 토큰이 해지됐습니다 (code 4002). 대시보드에서 토큰을 revoke 했어요. 웹 UI 에서 새 토큰으로 다시 페어링하세요.";
                default:
                    return "Discord: 봇 서버가 연결을 거부했습니다 (code " + code + "). 토큰이 무효/만료됐거나 디스코드 연동이 해제됐을 수 있어요. 웹 UI 에서 다시 페어링하세요.";
            }
        }

        private void ScheduleReconnect()
        {
            if (!_running || !_config.AutoReconnect || _gaveUp) return;
            _reconnectTimer?.Dispose();
            // 연속 실패가 쌓이면 간격을 늘려 서버를 덜 두드린다 (1×~6× 백오프).
            var baseMs = Math.Max(1, _config.ReconnectIntervalSec) * 1000;
            var interval = baseMs * Math.Min(_consecutiveFailures + 1, 6);
            _reconnectTimer = new Timer(_ =>
            {
                try { Connect(); } catch { }
            }, null, interval, Timeout.Infinite);
        }

        private void DisposeSocket()
        {
            // _ws 를 먼저 비운다 → 이 소켓의 이후 OnClose 는 IsCurrent 실패로 무시된다.
            var old = Interlocked.Exchange(ref _ws, null);
            _connected = false;
            // Close() 는 동기라 호출 스레드(Stop/OnDisable = Unity 메인 스레드)를 수 초 붙잡을
            // 수 있다 → 비동기 종료. 늦게 오는 OnClose 는 위 IsCurrent 가드로 무해하다.
            try { old?.CloseAsync(); } catch { }
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        // ================================================================
        // 메시지 파싱
        // ================================================================

        private void HandleMessage(string raw)
        {
            try
            {
                var obj = JObject.Parse(raw);
                var type = obj["type"]?.ToString();

                if (type == "system")
                {
                    var msg = obj["message"]?.ToString() ?? "";
                    m_OnSystemMessageCallbacks?.InvokeAll((IChatService)this, "Discord: " + msg);
                    return;
                }

                if (type == "pong") return;
                if (type == "bridge_test_ack")
                {
                    var ok = obj["ok"]?.ToObject<bool?>() ?? false;
                    var detail = obj["detail"]?.ToString();
                    var channelId = obj["channelId"]?.ToString();
                    lock (_testLock)
                    {
                        _lastTestAckUtc = DateTime.UtcNow;
                        _lastTestOk = ok;
                        _lastTestDetail = detail;
                        _lastTestChannelId = channelId;
                    }
                    return;
                }
                if (type == "voice_count")
                {
                    var count = obj["count"]?.ToObject<int?>() ?? 0;
                    var vc = obj["voiceChannel"] as JObject;
                    var vcName = vc?["name"]?.ToString();
                    ApplyVoiceCount(count, vcName);
                    return;
                }
                if (type != "message") return;

                var channelObj = obj["channel"] as JObject;
                var userObj = obj["user"] as JObject;
                if (channelObj == null || userObj == null) return;

                // 봇 메시지는 봇 측에서 이미 필터하지만 한 번 더 방어
                var isBot = userObj["bot"]?.ToObject<bool?>() ?? false;
                if (isBot) return;

                var channel = GetOrCreateChannel(channelObj);
                var user = new DiscordChatUser(
                    id: userObj["id"]?.ToString() ?? "",
                    userName: userObj["name"]?.ToString() ?? "Unknown",
                    color: userObj["color"]?.ToString()
                );
                var content = obj["content"]?.ToString() ?? "";

                // ---- 필터 판단 ----
                var chId = channelObj["id"]?.ToString() ?? "unknown";
                var muted = _config.DisabledChannels?.Contains(chId) ?? false;
                var isCommand = content.StartsWith("!");
                var forward = !muted && (!_config.ForwardOnlyCommands || isCommand);

                // ---- 통계/로그 기록 (필터와 무관하게 전부 — 웹 미리보기용) ----
                RecordIncoming(chId, channel.Name, user.UserName, content, forward);

                if (!forward) return;

                // !bsr 명령 — BSP_ChatRequest 응답을 추적하기 위해 pending 등록
                var bsrMatch = BsrRegex.Match(content);
                if (bsrMatch.Success)
                {
                    TrackPendingBsr(user.UserName, bsrMatch.Groups[1].Value, chId);
                }
                else
                {
                    // !bsr 외의 명령 — !oops/!link/!queue/!wip 등 화이트리스트 매칭 시 추적
                    var cmdMatch = CommandRegex.Match(content);
                    if (cmdMatch.Success)
                    {
                        var cmd = cmdMatch.Groups[1].Value.ToLowerInvariant();
                        if (TrackedCommands.Contains(cmd))
                        {
                            TrackPendingCommand(user.UserName, cmd, cmdMatch.Groups[2].Value, chId);
                        }
                    }
                }

                var emotes = ParseEmotes(obj["emotes"] as JArray);
                var message = new DiscordChatMessage(
                    id: Guid.NewGuid().ToString(),
                    text: content,
                    sender: user,
                    channel: channel,
                    emotes: emotes
                );
                m_OnTextMessageReceivedCallbacks?.InvokeAll((IChatService)this, message);
            }
            catch (Exception err)
            {
                _log?.Warn("HandleMessage failed: " + err.Message);
            }
        }

        private static IChatEmote[] ParseEmotes(JArray arr)
        {
            if (arr == null || arr.Count == 0) return new IChatEmote[0];
            var list = new System.Collections.Generic.List<IChatEmote>(arr.Count);
            foreach (var item in arr)
            {
                if (!(item is JObject e)) continue;
                var id = e["id"]?.ToString() ?? "";
                var name = e["name"]?.ToString() ?? "";
                var uri = e["uri"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(uri)) continue;
                var startIndex = e["startIndex"]?.ToObject<int?>() ?? 0;
                var endIndex = e["endIndex"]?.ToObject<int?>() ?? 0;
                var animated = (e["animation"]?.ToString() ?? "none").ToLower() == "gif";
                list.Add(new DiscordChatEmote(id, name, uri, startIndex, endIndex, animated));
            }
            return list.ToArray();
        }

        private void RecordIncoming(string chId, string chName, string user, string content, bool forwarded)
        {
            lock (_lock)
            {
                if (_channelStats.TryGetValue(chId, out var st)) { st.Count++; st.Name = chName; }
                else _channelStats[chId] = new ChannelStat { Id = chId, Name = chName, Count = 1 };
            }
            lock (_logLock)
            {
                _lastMessageUtc = DateTime.UtcNow;
                _recentLog.AddFirst(new LogEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Channel = chName,
                    User = user,
                    Content = content,
                    Forwarded = forwarded
                });
                while (_recentLog.Count > MaxLog) _recentLog.RemoveLast();
            }
        }

        private DiscordChatChannel GetOrCreateChannel(JObject obj)
        {
            var id = obj["id"]?.ToString() ?? "unknown";
            lock (_lock)
            {
                if (_channels.TryGetValue(id, out var existing)) return existing;
                var rawName = obj["name"]?.ToString() ?? id;
                var ch = new DiscordChatChannel(id, "#" + rawName);
                _channels[id] = ch;
                m_OnJoinRoomCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch);
                m_OnRoomStateUpdatedCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch);
                m_OnLiveStatusUpdatedCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch, true, 0);
                return ch;
            }
        }

        // ================================================================
        // !bsr 신청 추적 — 신청 직후 BSP_ChatRequest 응답을 매칭해 봇에
        // bsr_request (queued / rejected / queue_closed) 이벤트를 보낸다.
        // 응답이 6초 안에 안 오면 queue_closed 로 간주 (모듈 비활성 / 큐 닫힘).
        // ================================================================

        private void TrackPendingBsr(string userName, string code, string channelId)
        {
            if (string.IsNullOrEmpty(userName)) return;
            lock (_pendingLock)
            {
                _pendingBsr[userName] = new PendingBsr
                {
                    Code = code,
                    ChannelId = channelId,
                    UserName = userName,
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(6),
                };
                EnsurePendingGcTimer();
            }
        }

        private void EnsurePendingGcTimer()
        {
            if (_pendingGcTimer != null) return;
            _pendingGcTimer = new Timer(_ => GcPending(), null, 2000, Timeout.Infinite);
        }

        private void GcPending()
        {
            var now = DateTime.UtcNow;
            List<PendingBsr> expired = null;
            bool hasRemaining;
            lock (_pendingLock)
            {
                foreach (var key in _pendingBsr.Keys.ToList())
                {
                    var p = _pendingBsr[key];
                    if (p.ExpiresUtc <= now)
                    {
                        (expired ?? (expired = new List<PendingBsr>())).Add(p);
                        _pendingBsr.Remove(key);
                    }
                }
                // 응답 없이 만료된 일반 명령은 그냥 폐기 (알림 없음 — !oops 가 무응답이면
                // BSP_ChatRequest 가 모듈 비활성이거나 큐에 신청이 없는 경우).
                foreach (var key in _pendingCommands.Keys.ToList())
                {
                    var c = _pendingCommands[key];
                    if (c.ExpiresUtc <= now)
                    {
                        try { c.FlushTimer?.Dispose(); } catch { }
                        _pendingCommands.Remove(key);
                    }
                }
                hasRemaining = _pendingBsr.Count > 0 || _pendingCommands.Count > 0;
                _pendingGcTimer?.Dispose();
                _pendingGcTimer = null;
                if (hasRemaining) EnsurePendingGcTimer();
            }
            if (expired == null) return;
            foreach (var p in expired)
            {
                SendBsrEvent(p, "queue_closed", null, null);
            }
        }

        private void TryMatchBsrResponse(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            PendingBsr matched = null;
            lock (_pendingLock)
            {
                foreach (var kv in _pendingBsr)
                {
                    var name = kv.Value.UserName;
                    if (!string.IsNullOrEmpty(name) && message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = kv.Value;
                        _pendingBsr.Remove(kv.Key);
                        break;
                    }
                }
            }
            if (matched == null) return;

            string status;
            string reason = null;
            string songName = null;
            if (IsClosedResponse(message))
            {
                status = "queue_closed";
            }
            else if (IsQueuedResponse(message))
            {
                status = "queued";
                songName = ExtractSongInfo(message);
            }
            else
            {
                status = "rejected";
                reason = message.Length > 256 ? message.Substring(0, 256) : message;
            }
            SendBsrEvent(matched, status, reason, songName);
        }

        private static bool IsQueuedResponse(string msg)
        {
            return msg.IndexOf("added to queue", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsClosedResponse(string msg)
        {
            return msg.IndexOf("queue is closed", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("queue is currently closed", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("ChatRequest is disabled", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("requests are not", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractSongInfo(string msg)
        {
            // 응답 형식 예: "@user (key) Song Name by Mapper - Diff (key) was added to queue!"
            var idx = msg.IndexOf("was added to queue", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var prefix = msg.Substring(0, idx).Trim();
            var firstClose = prefix.IndexOf(')');
            var lastOpen = prefix.LastIndexOf('(');
            if (firstClose < 0 || lastOpen < 0 || lastOpen <= firstClose + 1) return null;
            return prefix.Substring(firstClose + 1, lastOpen - firstClose - 1).Trim();
        }

        private void SendBsrEvent(PendingBsr p, string status, string reason, string songName)
        {
            var ws = _ws;
            if (ws == null || !_connected) return;
            var obj = new JObject
            {
                ["type"] = "bsr_request",
                ["code"] = p.Code,
                ["status"] = status,
                ["channelId"] = p.ChannelId,
                ["requestedBy"] = new JObject { ["name"] = p.UserName },
            };
            if (!string.IsNullOrEmpty(songName)) obj["songName"] = songName;
            if (!string.IsNullOrEmpty(reason)) obj["reason"] = reason;
            try { ws.Send(obj.ToString(Formatting.None)); }
            catch (Exception err) { _log?.Warn("bsr_request send 실패: " + err.Message); }
        }

        // ================================================================
        // 일반 명령 (!oops/!queue/!link/!block/!unblock/!wip 등) — 응답을 1초 디바운스로
        // 모은 뒤 bsp_command 이벤트로 봇에 송신.
        // ================================================================

        private void TrackPendingCommand(string userName, string command, string args, string channelId)
        {
            if (string.IsNullOrEmpty(userName)) return;
            lock (_pendingLock)
            {
                // 같은 사용자가 새 명령을 치면 이전 pending 폐기 (응답 매칭 충돌 방지)
                if (_pendingCommands.TryGetValue(userName, out var prev))
                {
                    try { prev.FlushTimer?.Dispose(); } catch { }
                }
                _pendingCommands[userName] = new PendingCommand
                {
                    Command = command,
                    Args = args ?? "",
                    ChannelId = channelId,
                    UserName = userName,
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(8),
                    Responses = new List<string>(),
                };
                EnsurePendingGcTimer();
            }
        }

        /// <summary>
        /// BSP_ChatRequest / wipbot 응답이 들어오면 매칭되는 pending 명령에 누적.
        /// 첫 응답 시 1초 디바운스 타이머 시작 → multi-line (!queue 등) 도 합쳐서 한 번에 송신.
        /// </summary>
        private void TryMatchCommandResponse(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (_pendingLock)
            {
                PendingCommand matched = null;
                foreach (var kv in _pendingCommands)
                {
                    var name = kv.Value.UserName;
                    if (!string.IsNullOrEmpty(name) && message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = kv.Value;
                        break;
                    }
                }
                if (matched == null) return;
                matched.Responses.Add(message);
                if (matched.FlushTimer == null)
                {
                    var key = matched.UserName;
                    matched.FlushTimer = new Timer(_ => FlushPendingCommand(key), null, 1000, Timeout.Infinite);
                }
            }
        }

        private void FlushPendingCommand(string userName)
        {
            PendingCommand fire = null;
            lock (_pendingLock)
            {
                if (_pendingCommands.TryGetValue(userName, out var p))
                {
                    fire = p;
                    _pendingCommands.Remove(userName);
                    try { p.FlushTimer?.Dispose(); } catch { }
                }
            }
            if (fire != null) SendCommandEvent(fire);
        }

        private void SendCommandEvent(PendingCommand p)
        {
            var ws = _ws;
            if (ws == null || !_connected) return;
            var resp = new JArray();
            foreach (var line in p.Responses) resp.Add(line);
            var obj = new JObject
            {
                ["type"] = "bsp_command",
                ["command"] = p.Command,
                ["args"] = p.Args,
                ["channelId"] = p.ChannelId,
                ["requestedBy"] = new JObject { ["name"] = p.UserName },
                ["responses"] = resp,
            };
            try { ws.Send(obj.ToString(Formatting.None)); }
            catch (Exception err) { _log?.Warn("bsp_command send 실패: " + err.Message); }
        }

        // ================================================================
        // hello — ws 접속 직후 봇에 모드/게임/관련 플러그인 버전을 알린다.
        // 봇은 이 정보로 대시보드에 표시하거나 호환성 진단에 사용.
        // ================================================================

        /// <summary>관련 BSIPA 플러그인의 버전을 조회 (없으면 null). hello payload 용.</summary>
        private static readonly string[] TrackedPlugins = new[]
        {
            "BeatSaberPlus_Chat",
            "BeatSaberPlus_ChatRequest",
            "BeatSaberPlus_ChatEmoteRain",
            "BeatSaberPlus_ChatIntegrations",
            "wipbot",
        };

        private void SendHello()
        {
            var ws = _ws;
            if (ws == null || !_connected) return;
            try
            {
                var plugins = new JObject();
                foreach (var id in TrackedPlugins)
                {
                    var p = IPA.Loader.PluginManager.GetPluginFromId(id);
                    plugins[id] = p?.Version?.ToString();   // 없으면 null
                }
                var obj = new JObject
                {
                    ["type"] = "hello",
                    ["modVersion"] = Plugin.Self?.Version?.ToString() ?? "unknown",
                    ["bsVersion"] = UnityEngine.Application.version,
                    ["plugins"] = plugins,
                };
                ws.Send(obj.ToString(Formatting.None));
            }
            catch (Exception err) { _log?.Warn("hello send 실패: " + err.Message); }
        }

        // ================================================================
        // 음성채널 인원수 — 봇이 voice_count 페이로드를 보낼 때마다
        // VoiceIndicator (별도 floating UI) 로 전달.
        // BSP_Chat 의 기존 사람 아이콘 + 시청자 수 표시는 건드리지 않음.
        // ================================================================

        private void ApplyVoiceCount(int count, string voiceChannelName)
        {
            // Push 는 값만 저장 → 실제 UI 갱신은 VoiceIndicator.Update (Unity 메인 스레드).
            try { VoiceIndicator.Push(count, voiceChannelName); }
            catch (Exception err) { _log?.Warn("VoiceIndicator update 실패: " + err.Message); }
        }
    }
}
