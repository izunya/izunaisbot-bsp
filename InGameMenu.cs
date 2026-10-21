using System;
using System.Collections;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using HMUI;
using UnityEngine;
using IPALogger = IPA.Logging.Logger;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 인게임 좌측 모드(Mods) 탭에 버튼 하나 등록 → 클릭하면 로컬 웹 UI 를 브라우저로 연다.
    ///
    /// BSML 의 MenuButtons 는 메뉴 씬(Zenject)이 로드된 뒤에야 접근 가능하다.
    /// OnEnable 시점(게임 시작)에는 "too early" 예외가 나므로,
    /// 영속 GameObject + 코루틴으로 준비될 때까지 1초 간격 재시도한다.
    /// 실패해도 웹 UI/브리지 기능엔 영향 없음. (인게임 UI 텍스트는 영어 고정)
    /// </summary>
    public static class InGameMenu
    {
        private static MenuButton _button;
        private static GameObject _helperGo;
        private static SettingsFlowCoordinator _flow;
        private static WebServer _web;
        private static IPALogger _log;

        /// <summary>
        /// 메인 메뉴 씬이 준비됐는지(= MenuButtons 접근 성공). 버튼 등록에 성공하면 true.
        /// VoiceIndicator 가 FloatingScreen 을 너무 일찍 만들어 NRE/좀비 레이캐스터를
        /// 남기지 않도록 생성 타이밍 게이트로 쓴다.
        /// </summary>
        internal static volatile bool MenuReady;

        public static void Register(WebServer webServer, IPALogger log)
        {
            _web = webServer;
            _log = log;
            try
            {
                if (_helperGo == null)
                {
                    _helperGo = new GameObject("izunaisbot-MenuButtonHelper");
                    UnityEngine.Object.DontDestroyOnLoad(_helperGo);
                    _helperGo.AddComponent<MenuButtonHelper>();
                }
            }
            catch (Exception err)
            {
                _log?.Warn("Menu helper 생성 실패: " + err.Message);
            }
        }

        /// <summary>메뉴가 준비됐으면 등록 성공(true). 아직이면 false → 재시도.</summary>
        internal static bool TryRegisterButton()
        {
            try
            {
                if (_button == null)
                {
                    _button = new MenuButton(
                        "izunaisbot",
                        "Open the izunaisbot Discord bridge settings",
                        OpenSettings);
                }
#if BSML_LEGACY
                MenuButtons.instance.RegisterButton(_button);   // BSML 1.6.x (PersistentSingleton)
#else
                MenuButtons.Instance.RegisterButton(_button);   // BSML 1.12.x (ZenjectSingleton)
#endif
                MenuReady = true;   // 메뉴 준비 완료 → VoiceIndicator 가 안전하게 FloatingScreen 생성 가능
                _log?.Info("In-game menu button registered.");
                return true;
            }
            catch (Exception)
            {
                return false; // MenuButtons 아직 준비 안 됨 → 재시도
            }
        }

        /// <summary>재시도 한도까지 등록에 실패했을 때 — 원인 파악용 로그 (기능은 웹 UI 로 대체 가능).</summary>
        internal static void OnRegisterGaveUp()
        {
            _log?.Warn("MenuButtons 등록을 5분 내에 하지 못했습니다 — 모드탭 버튼/음성 인디케이터가 표시되지 않습니다. "
                       + "웹 UI 는 정상 동작합니다.");
        }

        /// <summary>모드탭 버튼 클릭 → 정면에 설정 FlowCoordinator 표시.</summary>
        private static void OpenSettings()
        {
            try
            {
                if (_flow == null) _flow = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
                FlowCoordinator main = BeatSaberUI.MainFlowCoordinator;
                main.PresentFlowCoordinator(_flow);
            }
            catch (Exception err)
            {
                _log?.Warn("OpenSettings failed: " + err.Message);
            }
        }

        /// <summary>설정 패널의 'Web Open' 버튼 → 브라우저로 웹 UI 열기.</summary>
        internal static void OpenWeb()
        {
            try { _web?.OpenInBrowser(); }
            catch (Exception err) { _log?.Warn("OpenWeb failed: " + err.Message); }
        }

        public static void Unregister(IPALogger log)
        {
            try
            {
                if (_button != null)
#if BSML_LEGACY
                    MenuButtons.instance.UnregisterButton(_button);
#else
                    MenuButtons.Instance.UnregisterButton(_button);
#endif
            }
            catch (Exception err) { log?.Warn("Menu button unregister 실패: " + err.Message); }

            try { if (_helperGo != null) UnityEngine.Object.Destroy(_helperGo); }
            catch { }

            _button = null;
            _helperGo = null;
            MenuReady = false;
        }
    }

    /// <summary>MenuButtons 가 준비될 때까지 재시도하는 헬퍼 MonoBehaviour.</summary>
    internal class MenuButtonHelper : MonoBehaviour
    {
        private void Start() { StartCoroutine(Loop()); }

        private IEnumerator Loop()
        {
            // 메뉴 씬이 늦게 뜨는 환경(모드 많음/HDD)에서 60초는 모자랐다. 등록에 실패하면
            // 모드탭 버튼뿐 아니라 MenuReady 게이트에 걸린 VoiceIndicator 도 영영 안 뜬다.
            for (int i = 0; i < 300; i++)
            {
                if (InGameMenu.TryRegisterButton()) yield break;
                yield return new WaitForSeconds(1f);
            }
            InGameMenu.OnRegisterGaveUp();
        }
    }
}
