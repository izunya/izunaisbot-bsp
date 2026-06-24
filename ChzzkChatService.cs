using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using CP_SDK.Animation;
using CP_SDK.Chat;
using CP_SDK.Chat.Interfaces;
using CP_SDK.Chat.Services;
using IPALogger = IPA.Logging.Logger;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WebSocketSharp;

namespace IzudisbotBSP
{
    /// <summary>
    /// 치지직(Chzzk) → BeatSaberPlus 채팅 브리지.
    ///
    /// 디스코드 브리지(DiscordChatService)와 마찬가지로 CP_SDK 의 external IChatService 로
    /// 등록되어, BSP_Chat / BSP_ChatRequest 의 ChatServiceMultiplexer 가 자동으로 픽업한다.
    /// → 치지직 시청자가 친 !bsr 등도 ChatRequest 가 그대로 처리한다.
    ///
    /// 봇 서버를 거치지 않고 네이버 치지직 공개 API/채팅 서버에 직접 붙는다 (익명 READ).
    /// 설정은 채널 ID 하나뿐 — 로컬 웹 UI 에서 입력. (참고: SieR-VR/ChatPlex.Chzzk)
    ///
    /// 흐름 (백그라운드 워커 스레드):
    ///   1) api.chzzk.naver.com 로 채널 정보 / 라이브 상태 폴링
    ///   2) 방송 중이면 chatChannelId + accessToken 발급
    ///   3) wss://kr-ssN.chat.naver.com/chat 접속 → cmd 100 connect (svcid=game, auth=READ)
    ///   4) cmd 93101(chat) 수신 → IChatMessage 생성 → m_OnTextMessageReceivedCallbacks.InvokeAll
    ///   5) cmd 0(ping) → cmd 10000(pong) 응답
    /// </summary>
    public class ChzzkChatService : ChatServiceBase, IChatService
    {
        public string DisplayName => "Chzzk";
        public Color AccentColor => new Color(0.031f, 1f, 0.651f);  // 치지직 그린 #08FFA6

        private readonly Config _config;
        private readonly IPALogger _log;
        private readonly object _lock = new object();
        private readonly Dictionary<string, ChzzkChatChannel> _channels = new Dictionary<string, ChzzkChatChannel>();

        // HTTP — 치지직 공개 API 는 브라우저 User-Agent 가 없으면 거부할 때가 있어 명시.
        private static readonly HttpClient Http = CreateHttp();

        private WebSocket _ws;
        private Thread _worker;
        private volatile bool _shouldRun;
        private volatile bool _connected;
        private volatile int _generation;
        // _stop: 워커 정지/재설정 신호 (백오프 Sleep 을 깨움). _wsClosed: WS 가 닫혔다는 신호.
        // 둘을 분리해야 WS 가 닫혀도 폴링 백오프가 무력화되지 않는다 (재접속 폭주 방지).
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly ManualResetEvent _wsClosed = new ManualResetEvent(false);

        // ---- 웹 UI 용 상태/로그 ----
        private readonly object _logLock = new object();
        private readonly LinkedList<LogEntry> _recentLog = new LinkedList<LogEntry>();
        private DateTime? _lastMessageUtc;
        private string _statusReason = "";
        private string _channelName = "";
        private string _liveTitle = "";
        private const int MaxLog = 200;
        // 치지직 채팅 서버 idle timeout(~60s) 회피용 클라 heartbeat 간격.
        private const int HeartbeatMs = 20000;

        public ChzzkChatService(Config config, IPALogger log)
        {
            _config = config;
            _log = log;
        }

        private static HttpClient CreateHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return c;
        }

        public class LogEntry
        {
            public string Time { get; set; }
            public string User { get; set; }
            public string Content { get; set; }
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
            _shouldRun = true;
            StartWorker();
        }

        public void Stop()
        {
            _shouldRun = false;
            StopWorker();
        }

        public bool IsConnectedAndLive() => _connected;
        public string PrimaryChannelName() => _channelName ?? "Chzzk";
        public void RecacheEmotes() { }

        // ================================================================
        // 웹 UI 지원
        // ================================================================

        public bool Enabled => _config.ChzzkEnabled;
        public bool Connected => _connected;
        public string ChannelName { get { lock (_lock) { return _channelName; } } }
        public string LiveTitle { get { lock (_lock) { return _liveTitle; } } }
        public string StatusReason { get { lock (_lock) { return _statusReason; } } }
        public DateTime? LastMessageUtc { get { lock (_logLock) { return _lastMessageUtc; } } }

        public List<LogEntry> GetRecentLog(int max = 100)
        {
            lock (_logLock) { return _recentLog.Take(Math.Max(1, max)).ToList(); }
        }

        public void ClearLog()
        {
            lock (_logLock) { _recentLog.Clear(); }
        }

        /// <summary>웹에서 설정(채널 ID / 사용 여부) 변경 시 호출 — 현재 게이트가 켜져 있으면 재시작.</summary>
        public void Reconfigure()
        {
            Config.Save();
            if (!_shouldRun) return;
            _log?.Info("Chzzk reconfigured → restarting (" + _config.ChzzkChannelId + ")");
            StopWorker();
            StartWorker();
        }

        // ================================================================
        // IChatService — web form / temp channels (치지직에선 불필요 → no-op)
        // ================================================================

        public string WebPageHTMLForm() => "";
        public string WebPageHTML() => "";
        public string WebPageJS() => "";
        public string WebPageJSValidate() => "";
        public void WebPageOnPost(Dictionary<string, string> postData) { }

        public void SendTextMessage(IChatChannel channel, string message) { }
        public void JoinTempChannel(string groupIdentifier, string channelName, string prefix, bool canSendMessage) { }
        public void LeaveTempChannel(string channelName) { }
        public bool IsInTempChannel(string channelName) => false;
        public void LeaveAllTempChannel(string groupIdentifier) { }

        // ================================================================
        // 워커 스레드 — 폴링 + WS 수명주기
        // ================================================================

        private void StartWorker()
        {
            if (!_config.ChzzkEnabled)
            {
                SetStatus("disabled");
                return;
            }
            if (string.IsNullOrEmpty((_config.ChzzkChannelId ?? "").Trim()))
            {
                SetStatus("채널 ID 미설정");
                return;
            }
            if (_worker != null && _worker.IsAlive) return;
            _stop.Reset();
            int gen = ++_generation;
            _worker = new Thread(() => WorkerLoop(gen)) { IsBackground = true, Name = "izudisbot-chzzk" };
            _worker.Start();
        }

        private void StopWorker()
        {
            // _generation 을 올려 현재 워커를 "구세대"로 만든다 → 워커가 스스로 빠져나온다.
            // (Reconfigure 시엔 _shouldRun/ChzzkEnabled 가 계속 true 라 이 토큰 없이는 안 죽음)
            _generation++;
            _stop.Set();
            CloseSocket();
            var w = _worker;
            _worker = null;
            try { if (w != null && w.IsAlive && w != Thread.CurrentThread) w.Join(3000); } catch { }
        }

        /// <summary>이 세대(gen)의 워커가 계속 돌아야 하는지.</summary>
        private bool Active(int gen) => _shouldRun && _config.ChzzkEnabled && gen == _generation;

        private void WorkerLoop(int gen)
        {
            int notLiveBackoff = 0;
            int wsFailBackoff = 0;
            while (Active(gen))
            {
                var channelId = (_config.ChzzkChannelId ?? "").Trim();
                if (string.IsNullOrEmpty(channelId)) { SetStatus("채널 ID 미설정"); break; }

                try
                {
                    var channel = GetChannelInfo(channelId);
                    if (channel == null)
                    {
                        SetStatus("채널을 찾을 수 없음 (" + channelId + ")");
                        m_OnSystemMessageCallbacks?.InvokeAll(this,
                            "<color=orange><b>Chzzk: 채널을 찾을 수 없습니다 (" + channelId + "). 채널 ID 를 확인하세요.</b></color>");
                        if (!Sleep(gen, 15000)) break;
                        continue;
                    }
                    SetChannelName(channel.Name);

                    var live = GetLiveStatus(channelId);
                    if (live == null)
                    {
                        notLiveBackoff = Math.Min(notLiveBackoff + 2, 20);
                        SetStatus("방송 대기 중 — " + channel.Name);
                        if (!Sleep(gen, (10 + notLiveBackoff) * 1000)) break;
                        continue;
                    }
                    notLiveBackoff = 0;

                    var accessToken = GetAccessToken(live.ChatChannelId);
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        SetStatus("채팅 토큰 발급 실패");
                        if (!Sleep(gen, 8000)) break;
                        continue;
                    }

                    SetLiveTitle(live.LiveTitle);
                    bool opened = ConnectAndListen(live.ChatChannelId, accessToken, channel.Name, live.LiveTitle);
                    // 연결이 한 번이라도 열렸으면 정상 재폴링(3s). 한 번도 못 열렸으면(TLS/네트워크 등)
                    // 지수적 백오프로 폭주를 막는다.
                    if (opened)
                    {
                        wsFailBackoff = 0;
                        if (!Sleep(gen, 3000)) break;
                    }
                    else
                    {
                        wsFailBackoff = Math.Min(wsFailBackoff + 1, 12);
                        SetStatus("채팅 서버 연결 실패 — 재시도 대기");
                        if (!Sleep(gen, (5 + wsFailBackoff * 5) * 1000)) break;
                    }
                }
                catch (Exception err)
                {
                    _log?.Warn("Chzzk worker 오류: " + err.Message);
                    SetStatus("오류: " + err.Message);
                    if (!Sleep(gen, 8000)) break;
                }
            }
            _connected = false;
            if (gen == _generation) SetStatus(_config.ChzzkEnabled ? "정지됨" : "disabled");
            _log?.Info("Chzzk worker 종료 (gen " + gen + ")");
        }

        /// <summary>정지 신호(_stop)에 즉시 반응하는 대기. 계속 진행해야 하면 true.</summary>
        private bool Sleep(int gen, int ms)
        {
            if (!Active(gen)) return false;
            // _stop 이 set 되면 즉시 깨어난다 (Stop/Reconfigure 시). WS close 와는 분리되어 있어
            // 채팅이 끊겨도 이 백오프가 단축되지 않는다.
            _stop.WaitOne(ms);
            return Active(gen);
        }

        // ================================================================
        // WebSocket (WebSocketSharp)
        // ================================================================

        /// <summary>WS 에 붙어 닫힐 때까지 블록. 한 번이라도 OnOpen 됐으면 true 반환.</summary>
        private bool ConnectAndListen(string chatChannelId, string accessToken, string channelName, string liveTitle)
        {
            CloseSocket();
            _wsClosed.Reset();
            bool opened = false;

            var id = Math.Abs(Guid.NewGuid().GetHashCode()) % 5 + 1;   // kr-ss1..5
            var uri = "wss://kr-ss" + id + ".chat.naver.com/chat";
            _ws = new WebSocket(uri);
            // websocket-sharp 는 기본이 옛 SSL → 네이버 채팅 서버가 핸드셰이크를 거부(close 1015).
            // TLS1.2 를 명시하고, 비트세이버 Mono 의 불완전한 루트 CA 스토어 때문에 인증서 검증도 통과시킨다.
            _ws.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            _ws.SslConfiguration.ServerCertificateValidationCallback = (s, c, ch, e) => true;
            _ws.OnOpen += (s, e) =>
            {
                opened = true;
                _connected = true;
                SetStatus("연결됨 — " + (string.IsNullOrEmpty(liveTitle) ? channelName : liveTitle));
                _log?.Info("Chzzk WS 연결: " + uri);
                SendConnect(chatChannelId, accessToken);
                EnsureChannel(chatChannelId, channelName);
                m_OnSystemMessageCallbacks?.InvokeAll(this,
                    "<color=#08FFA6><b>Chzzk: \"" + (liveTitle ?? channelName) + "\" 채팅 연결됨</b></color>");
            };
            _ws.OnMessage += (s, e) => { try { HandleWsMessage(e.Data, chatChannelId, channelName); } catch (Exception err) { _log?.Warn("Chzzk 메시지 처리 실패: " + err.Message); } };
            _ws.OnError += (s, e) => _log?.Warn("Chzzk WS error: " + e.Message);
            _ws.OnClose += (s, e) =>
            {
                _connected = false;
                _log?.Info("Chzzk WS 닫힘: code=" + e.Code);
                _wsClosed.Set();
            };

            try { _ws.Connect(); }
            catch (Exception err) { _log?.Warn("Chzzk WS 연결 실패: " + err.Message); CloseSocket(); return opened; }

            // 닫히거나(또는 정지) 까지 블록하되, HeartbeatMs 마다 PING(cmd 0)을 보낸다.
            // 치지직 채팅 서버는 클라 heartbeat 가 없으면 ~60초 idle timeout 으로 끊는다(close 1006).
            var handles = new WaitHandle[] { _wsClosed, _stop };
            while (WaitHandle.WaitAny(handles, HeartbeatMs) == WaitHandle.WaitTimeout)
            {
                SafeSend("{\"ver\":\"3\",\"cmd\":0}");
            }
            CloseSocket();
            return opened;
        }

        private void SendConnect(string chatChannelId, string accessToken)
        {
            var bdy = new JObject
            {
                ["uid"] = JValue.CreateNull(),
                ["devType"] = 2001,
                ["accTkn"] = accessToken,
                ["auth"] = "READ",
            };
            var obj = new JObject
            {
                ["ver"] = "3",
                ["cmd"] = 100,
                ["svcid"] = "game",
                ["cid"] = chatChannelId,
                ["bdy"] = bdy,
                ["tid"] = 1,
            };
            SafeSend(obj.ToString(Newtonsoft.Json.Formatting.None));
        }

        private void SafeSend(string msg)
        {
            try { if (_ws != null && _ws.ReadyState == WebSocketState.Open) _ws.Send(msg); }
            catch (Exception err) { _log?.Warn("Chzzk send 실패: " + err.Message); }
        }

        private void CloseSocket()
        {
            var ws = _ws;
            _ws = null;
            _connected = false;
            if (ws != null)
            {
                try { ws.Close(); } catch { }
            }
        }

        // ================================================================
        // 수신 메시지 파싱
        // ================================================================

        private void HandleWsMessage(string raw, string chatChannelId, string channelName)
        {
            var obj = JObject.Parse(raw);
            var cmd = obj["cmd"]?.ToObject<int?>() ?? -1;

            if (cmd == 0)   // PING → PONG
            {
                SafeSend("{\"ver\":\"3\",\"cmd\":10000}");
                return;
            }
            if (cmd != 93101) return;   // 93101 = 일반 채팅만 (도네이션/시스템 제외)

            var bdy = obj["bdy"] as JArray;
            if (bdy == null) return;

            var channel = EnsureChannel(chatChannelId, channelName);
            foreach (var item in bdy)
            {
                if (!(item is JObject chat)) continue;
                ProcessChat(chat, channel, chatChannelId);
            }
        }

        private void ProcessChat(JObject chat, ChzzkChatChannel channel, string chatChannelId)
        {
            var profileStr = chat["profile"]?.ToString();
            if (string.IsNullOrEmpty(profileStr) || profileStr == "null") return;   // 시스템/익명 → 스킵

            JObject profile;
            try { profile = JObject.Parse(profileStr); } catch { return; }

            var content = chat["msg"]?.ToString() ?? "";
            var uid = chat["uid"]?.ToString() ?? "";
            var nickname = profile["nickname"]?.ToString() ?? "Unknown";
            var roleCode = profile["userRoleCode"]?.ToString() ?? "";
            var color = ResolveColor(profile, uid, chatChannelId);

            var user = new ChzzkChatUser(
                id: uid,
                userName: nickname,
                color: color,
                isBroadcaster: roleCode == "streamer",
                isModerator: roleCode == "streaming_chat_manager" || roleCode == "streaming_channel_manager"
            );

            var emotes = ParseEmotes(chat["extras"]?.ToString(), content);

            var msgId = chat["msgTime"]?.ToString() ?? Guid.NewGuid().ToString();
            var message = new ChzzkChatMessage(msgId, content, user, channel, emotes);

            RecordIncoming(nickname, content);
            m_OnTextMessageReceivedCallbacks?.InvokeAll((IChatService)this, message);
        }

        /// <summary>치지직 닉네임 색: title.color 가 있으면 그것, 없으면 userIdHash 기반 팔레트.</summary>
        private static string ResolveColor(JObject profile, string uid, string chatChannelId)
        {
            var titleColor = profile["title"]?["color"]?.ToString();
            if (!string.IsNullOrEmpty(titleColor)) return titleColor;

            var seed = (profile["userIdHash"]?.ToString() ?? uid ?? "") + (chatChannelId ?? "");
            int sum = 0;
            foreach (var c in seed) sum += c;
            return NameColors[sum % NameColors.Length];
        }

        private static IChatEmote[] ParseEmotes(string extrasStr, string message)
        {
            if (string.IsNullOrEmpty(extrasStr)) return new IChatEmote[0];
            JObject extras;
            try { extras = JObject.Parse(extrasStr); } catch { return new IChatEmote[0]; }

            var emojis = extras["emojis"] as JObject;
            if (emojis == null || emojis.Count == 0) return new IChatEmote[0];

            var list = new List<IChatEmote>();
            foreach (var kv in emojis)
            {
                var key = kv.Key;
                var uri = kv.Value?.ToString();
                if (string.IsNullOrEmpty(uri)) continue;
                var token = "{:" + key + ":}";
                int idx = 0;
                while ((idx = message.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
                {
                    list.Add(new ChzzkChatEmote("chzzk-" + key, token, uri, idx, idx + token.Length - 1));
                    idx += token.Length;
                }
            }
            // 뒤에서부터 적용되도록 StartIndex 내림차순 (디스코드 emote 처리와 동일한 관례)
            list.Sort((a, b) => b.StartIndex.CompareTo(a.StartIndex));
            return list.ToArray();
        }

        private ChzzkChatChannel EnsureChannel(string id, string name)
        {
            lock (_lock)
            {
                if (_channels.TryGetValue(id, out var existing)) return existing;
                var ch = new ChzzkChatChannel(id, string.IsNullOrEmpty(name) ? id : name);
                _channels[id] = ch;
                m_OnJoinRoomCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch);
                m_OnRoomStateUpdatedCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch);
                m_OnLiveStatusUpdatedCallbacks?.InvokeAll((IChatService)this, (IChatChannel)ch, true, 0);
                return ch;
            }
        }

        private void RecordIncoming(string user, string content)
        {
            lock (_logLock)
            {
                _lastMessageUtc = DateTime.UtcNow;
                _recentLog.AddFirst(new LogEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    User = user,
                    Content = content,
                });
                while (_recentLog.Count > MaxLog) _recentLog.RemoveLast();
            }
        }

        // ================================================================
        // 치지직 공개 API (HTTP)
        // ================================================================

        private class ChannelInfoDto { public string Id; public string Name; public bool OpenLive; }
        private class LiveDto { public string ChatChannelId; public string LiveTitle; }

        private ChannelInfoDto GetChannelInfo(string channelId)
        {
            var json = HttpGet("https://api.chzzk.naver.com/service/v1/channels/" + channelId);
            if (json == null) return null;
            var content = json["content"];
            var id = content?["channelId"]?.ToString();
            if (string.IsNullOrEmpty(id)) return null;
            return new ChannelInfoDto
            {
                Id = id,
                Name = content["channelName"]?.ToString() ?? "(unknown)",
                OpenLive = content["openLive"]?.ToObject<bool?>() ?? false,
            };
        }

        /// <summary>방송 중이면 (chatChannelId, liveTitle), 아니면 null.</summary>
        private LiveDto GetLiveStatus(string channelId)
        {
            var json = HttpGet("https://api.chzzk.naver.com/polling/v3.1/channels/" + channelId + "/live-status");
            var content = json?["content"];
            if (content == null) return null;

            var pollingJson = content["livePollingStatusJson"]?.ToString();
            if (!string.IsNullOrEmpty(pollingJson))
            {
                try
                {
                    var status = JObject.Parse(pollingJson)["status"]?.ToString();
                    if (status != "STARTED") return null;
                }
                catch { }
            }

            var chatChannelId = content["chatChannelId"]?.ToString();
            if (string.IsNullOrEmpty(chatChannelId)) return null;   // 성인방 등 — 못 받음

            return new LiveDto
            {
                ChatChannelId = chatChannelId,
                LiveTitle = content["liveTitle"]?.ToString() ?? "(unknown)",
            };
        }

        private string GetAccessToken(string chatChannelId)
        {
            var json = HttpGet("https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId="
                               + chatChannelId + "&chatType=STREAMING");
            return json?["content"]?["accessToken"]?.ToString();
        }

        private JObject HttpGet(string url)
        {
            try
            {
                var body = Http.GetStringAsync(url).GetAwaiter().GetResult();
                return JObject.Parse(body);
            }
            catch (Exception err)
            {
                _log?.Debug("Chzzk HTTP GET 실패 (" + url + "): " + err.Message);
                return null;
            }
        }

        // ================================================================
        // 상태 setter
        // ================================================================

        private void SetStatus(string s) { lock (_lock) { _statusReason = s ?? ""; } }
        private void SetChannelName(string s) { lock (_lock) { _channelName = s ?? ""; } }
        private void SetLiveTitle(string s) { lock (_lock) { _liveTitle = s ?? ""; } }

        // 치지직 다크테마 닉네임 색 팔레트 (참고 구현과 동일)
        private static readonly string[] NameColors =
        {
            "#EEA05D","#EAA35F","#E98158","#E97F58","#E76D53","#E66D5F","#E16490","#E481AE",
            "#E481AE","#D25FAC","#D263AE","#D66CB4","#D071B6","#AF71B5","#A96BB2","#905FAA",
            "#B38BC2","#9D78B8","#8D7AB8","#7F68AE","#9F99C8","#717DC6","#7E8BC2","#5A90C0",
            "#628DCC","#81A1CA","#ADD2DE","#83C5D6","#8BC8CB","#91CBC6","#83C3BB","#7DBFB2",
            "#AAD6C2","#84C194","#92C896","#94C994","#9FCE8E","#A6D293","#ABD373","#BFDE73",
        };
    }

    // ====================================================================
    // IChat* 모델 구현 — 치지직 전용
    // ====================================================================

    public class ChzzkChatChannel : IChatChannel
    {
        public string Id { get; }
        public string Name { get; }
        public bool IsTemp => false;
        public string Prefix => "";
        public bool CanSendMessages => false;
        public bool Live => true;
        public int ViewerCount => 0;

        public ChzzkChatChannel(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class ChzzkChatUser : IChatUser
    {
        public string Id { get; }
        public string UserName { get; }
        public string DisplayName { get; }
        public string PaintedName { get; }
        public string Color { get; }
        public bool IsBroadcaster { get; }
        public bool IsModerator { get; }
        public bool IsSubscriber => false;
        public bool IsVip => false;
        public IChatBadge[] Badges { get; } = new IChatBadge[0];

        public ChzzkChatUser(string id, string userName, string color, bool isBroadcaster = false, bool isModerator = false)
        {
            Id = id;
            UserName = userName ?? id;
            DisplayName = UserName;
            PaintedName = UserName;
            Color = string.IsNullOrEmpty(color) ? "#FFFFFF" : color;
            IsBroadcaster = isBroadcaster;
            IsModerator = isModerator;
        }
    }

    public class ChzzkChatMessage : IChatMessage
    {
        public string Id { get; }
        public bool IsSystemMessage => false;
        public bool IsActionMessage => false;
        public bool IsHighlighted => false;
        public bool IsGiganticEmote => false;
        public bool IsPing => false;
        public string Message { get; }
        public IChatUser Sender { get; }
        public IChatChannel Channel { get; }
        public IChatEmote[] Emotes { get; }

        public ChzzkChatMessage(string id, string text, IChatUser sender, IChatChannel channel, IChatEmote[] emotes = null)
        {
            Id = id;
            Message = text ?? "";
            Sender = sender;
            Channel = channel;
            Emotes = emotes ?? new IChatEmote[0];
        }
    }

    public class ChzzkChatEmote : IChatEmote
    {
        public string Id { get; }
        public string Name { get; }
        public string Uri { get; }
        public int StartIndex { get; }
        public int EndIndex { get; }
        public EAnimationType Animation { get; }

        public ChzzkChatEmote(string id, string name, string uri, int startIndex, int endIndex)
        {
            Id = id;
            Name = name;
            Uri = uri;
            StartIndex = startIndex;
            EndIndex = endIndex;
            // 치지직 이모지는 정적/움짤 혼재 → SDK 자동 판별에 맡긴다.
            Animation = EAnimationType.AUTODETECT;
        }
    }
}
