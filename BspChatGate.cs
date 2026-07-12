using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using IPALogger = IPA.Logging.Logger;

namespace IzunaisbotBSP
{
    /// <summary>
    /// BeatSaberPlus_Chat 모듈의 활성 상태를 감시한다.
    ///
    /// BSP Chat 이 꺼져 있으면 izunaisbot 의 디스코드 브리지는 메시지를 넘길 소비자가 없어
    /// 의미가 없고, 불필요한 WS 연결/재접속만 돌게 된다. → BSP Chat 이 꺼지면 브리지(+음성
    /// 인디케이터)도 함께 멈추고, 다시 켜지면 자동으로 재개한다. (로컬 웹 UI/설정 메뉴는
    /// 계속 살아있다 — 사용자가 설정을 보거나 페어링할 수 있어야 하므로.)
    ///
    /// 판단 근거: UserData/BeatSaberPlus/Chat/Config.json 의 "Enabled" 플래그.
    /// FileSystemWatcher 는 백그라운드 스레드라 Unity 객체를 못 만지므로, 메인 스레드
    /// MonoBehaviour 가 3초 간격으로 폴링해 변화 시 콜백(메인 스레드)을 호출한다.
    /// </summary>
    public static class BspChatGate
    {
        private static GameObject _go;
        private static Action _onEnabled;
        private static Action _onDisabled;
        private static IPALogger _log;
        private static bool _lastEnabled = true;
        private static bool _started;

        /// <summary>BSP Chat 모듈 설정 파일 (게임 설치 폴더 기준).</summary>
        private static string ConfigPath => Path.Combine(
            Environment.CurrentDirectory, "UserData", "BeatSaberPlus", "Chat", "Config.json");

        /// <summary>마지막으로 확인된 BSP Chat 활성 여부 (웹 UI 표시용).</summary>
        public static bool ChatEnabled => _lastEnabled;

        /// <summary>
        /// 감시 시작. <paramref name="onEnabled"/>/<paramref name="onDisabled"/> 는 메인 스레드에서
        /// 호출된다. 현재 상태를 즉시 한 번 반영한다(=꺼져 있으면 onDisabled, 켜져 있으면 onEnabled).
        /// </summary>
        public static void Init(Action onEnabled, Action onDisabled, IPALogger log)
        {
            _onEnabled = onEnabled;
            _onDisabled = onDisabled;
            _log = log;
            _lastEnabled = ReadEnabled();
            _started = false;

            try
            {
                if (_go == null)
                {
                    _go = new GameObject("izunaisbot-BspChatGate");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _go.AddComponent<Ticker>();
                }
            }
            catch (Exception err) { _log?.Warn("BspChatGate init 실패: " + err.Message); }

            // 초기 상태 즉시 적용
            _started = true;
            try
            {
                if (_lastEnabled)
                {
                    _onEnabled?.Invoke();
                }
                else
                {
                    _log?.Info("BeatSaberPlus_Chat 비활성 → izunaisbot 브리지 대기 상태로 시작");
                    _onDisabled?.Invoke();
                }
            }
            catch (Exception err) { _log?.Warn("BspChatGate 초기 적용 실패: " + err.Message); }
        }

        /// <summary>메인 스레드 폴링 — 변화 감지 시에만 콜백.</summary>
        internal static void Poll()
        {
            if (!_started) return;
            bool now = ReadEnabled();
            if (now == _lastEnabled) return;
            _lastEnabled = now;
            try
            {
                if (now)
                {
                    _log?.Info("BeatSaberPlus_Chat 활성화 감지 → izunaisbot 브리지 시작");
                    _onEnabled?.Invoke();
                }
                else
                {
                    _log?.Info("BeatSaberPlus_Chat 비활성화 감지 → izunaisbot 브리지 정지");
                    _onDisabled?.Invoke();
                }
            }
            catch (Exception err) { _log?.Warn("BspChatGate 상태 전환 실패: " + err.Message); }
        }

        /// <summary>파일이 없거나 못 읽으면 기본 true(=막지 않음 — 과도하게 끄지 않도록).</summary>
        private static bool ReadEnabled()
        {
            try
            {
                var p = ConfigPath;
                if (!File.Exists(p)) return true;
                var j = JObject.Parse(File.ReadAllText(p));
                var v = j["Enabled"];
                return v == null || v.ToObject<bool>();
            }
            catch { return true; }
        }

        public static void Shutdown()
        {
            try { if (_go != null) UnityEngine.Object.Destroy(_go); } catch { /* 종료 중 무시 */ }
            _go = null;
            _started = false;
            _onEnabled = null;
            _onDisabled = null;
        }

        /// <summary>3초 간격 메인 스레드 폴링용 MonoBehaviour.</summary>
        private class Ticker : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 3f;
                Poll();
            }
        }
    }
}
