using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace IzudisbotBSP
{
    /// <summary>
    /// 브리지 설정 — UserData/izudisbot-bsp.json 에 보관.
    /// 첫 실행 시 기본값으로 자동 생성. 게임 종료 후 메모장으로 직접 수정.
    /// </summary>
    public class Config
    {
        /// <summary>WebSocket URL. 기본값으로 고정 사용 (고급 사용자만 JSON 으로 변경).</summary>
        public string Url { get; set; } = "wss://bsp.izunya.dev/bsp";

        /// <summary>대시보드에서 발급받은 bsp_xxx 토큰</summary>
        public string Token { get; set; } = "";

        /// <summary>끊겼을 때 자동 재접속</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>재접속 간격 (초)</summary>
        public int ReconnectIntervalSec { get; set; } = 10;

        // ---- 로컬 웹 UI ----

        /// <summary>모드 자체 로컬 웹 설정 UI 사용 여부</summary>
        public bool WebUIEnabled { get; set; } = true;

        /// <summary>로컬 웹 UI 포트 (localhost 전용). 변경 시 게임 재시작 필요.</summary>
        public int WebUIPort { get; set; } = 9001;

        /// <summary>비트세이버 실행 시 웹 UI 를 기본 브라우저로 자동으로 열지 여부</summary>
        public bool OpenWebOnLaunch { get; set; } = true;

        /// <summary>봇 대시보드 / 페어링 API 의 베이스 URL. 모드 웹 UI 가 직접 호출함.</summary>
        public string BotApiBase { get; set; } = "https://izudisbot.izunya.dev";

        // ---- 치지직(Chzzk) 연동 ----

        /// <summary>치지직 채팅을 게임(BSP Chat/ChatRequest)으로 함께 연동할지 여부.</summary>
        public bool ChzzkEnabled { get; set; } = false;

        /// <summary>치지직 채널 ID (채널 URL 의 32자리 hex). 예: chzzk.naver.com/&lt;여기&gt;</summary>
        public string ChzzkChannelId { get; set; } = "";

        // ---- 필터 ----

        /// <summary>게임으로 전달하지 않을(음소거) 채널 ID 목록</summary>
        public List<string> DisabledChannels { get; set; } = new List<string>();

        /// <summary>true 면 '!' 로 시작하는 명령어 메시지만 게임으로 전달</summary>
        public bool ForwardOnlyCommands { get; set; } = false;

        public static Config Current { get; private set; } = new Config();

        public static string FilePath => Path.Combine(
            Environment.CurrentDirectory, "UserData", "izudisbot-bsp.json"
        );

        public static void Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Current = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                }
                else
                {
                    Save();  // 디폴트 파일 생성 → 사용자가 편집할 수 있게
                }
            }
            catch (Exception err)
            {
                Plugin.Log?.Warn("Config load failed: " + err.Message);
                Current = new Config();
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
            }
            catch (Exception err)
            {
                Plugin.Log?.Warn("Config save failed: " + err.Message);
            }
        }
    }
}
