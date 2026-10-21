using IPA;
using IPA.Loader;
using IPALogger = IPA.Logging.Logger;

namespace IzunaisbotBSP
{
    /// <summary>
    /// BSIPA 진입점.
    /// CP_SDK 의 Chat.Service 에 DiscordChatService 를 external service 로 등록 →
    /// BSP Chat / ChatRequest 모듈의 ChatServiceMultiplexer 가 자동으로 픽업.
    ///
    /// 등록 타이밍:
    ///   manifest.json 의 loadBefore 로 BSP Chat 모듈보다 먼저 로드되도록 보장.
    ///   OnEnable 시점에 RegisterExternalService 호출 → 이후 BSP 모듈이 Acquire 할 때 포함됨.
    /// </summary>
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        public static IPALogger Log { get; private set; }
        /// <summary>이 플러그인의 BSIPA metadata (버전 / id) — hello payload 등에 사용.</summary>
        public static PluginMetadata Self { get; private set; }
        private DiscordChatService _service;
        private ChzzkChatService _chzzk;
        private WebServer _webServer;

        [Init]
        public Plugin(IPALogger logger, PluginMetadata metadata)
        {
            Log = logger;
            Self = metadata;
            Log.Info("izunaisbot-bsp v" + (metadata?.Version?.ToString() ?? "?") + " loaded");
        }

        [OnEnable]
        public void OnEnable()
        {
            Config.Load();
            _service = new DiscordChatService(Config.Current, Log);
            CP_SDK.Chat.Service.RegisterExternalService(_service);
            Log.Info("DiscordChatService registered → " + (Config.Current.Url ?? "(unconfigured)"));

            // 치지직 브리지도 같은 CP_SDK external service 로 등록 (활성화는 Config.ChzzkEnabled).
            _chzzk = new ChzzkChatService(Config.Current, Log);
            CP_SDK.Chat.Service.RegisterExternalService(_chzzk);
            Log.Info("ChzzkChatService registered → " + (Config.Current.ChzzkEnabled
                ? "enabled (" + Config.Current.ChzzkChannelId + ")" : "disabled"));

            // 로컬 웹 UI / 설정 메뉴는 BSP Chat 상태와 무관하게 항상 띄운다 (설정·페어링용).
            _webServer = new WebServer(_service, _chzzk, Config.Current, Log);
            _webServer.Start();

            if (Config.Current.WebUIEnabled && Config.Current.OpenWebOnLaunch)
                _webServer.OpenInBrowser();

            InGameMenu.Register(_webServer, Log);

            // 디스코드 브리지(WS) + 음성 인디케이터는 BeatSaberPlus_Chat 이 켜져 있을 때만 동작.
            // BSP Chat 이 꺼지면 함께 멈추고, 다시 켜지면 재개한다. (현재 상태를 즉시 반영)
            BspChatGate.Init(StartBridge, StopBridge, Log);

            // GitHub Releases 폴링 — 24h 마다 새 모드 버전 체크 (웹 UI 배너로 안내)
            UpdateChecker.Start(Log);
        }

        /// <summary>BSP Chat 활성 → 디스코드 브리지 + 음성 인디케이터 시작 (메인 스레드).</summary>
        private void StartBridge()
        {
            _service?.Start();
            _chzzk?.Start();         // 치지직 브리지도 함께 (내부에서 ChzzkEnabled 체크)
            VoiceIndicator.Init();   // 메인 스레드에서 GameObject 생성해야 함
        }

        /// <summary>BSP Chat 비활성 → 브리지 + 음성 인디케이터 정지 (메인 스레드).</summary>
        private void StopBridge()
        {
            _service?.Stop();
            _chzzk?.Stop();
            try { VoiceIndicator.Shutdown(); } catch { /* 종료/씬 전환 중 무시 */ }
        }

        [OnDisable]
        public void OnDisable()
        {
            UpdateChecker.Stop();
            BspChatGate.Shutdown();
            InGameMenu.Unregister(Log);
            _webServer?.Stop();
            _webServer = null;
            _service?.Stop();
            _service = null;
            _chzzk?.Stop();
            _chzzk = null;
            try { VoiceIndicator.Shutdown(); } catch { }
            Log?.Info("izunaisbot-bsp disabled");
        }
    }
}
