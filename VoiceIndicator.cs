using System;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using CP_SDK.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 디스코드 음성채널 인원수를 표시하는 floating UI.
    ///
    /// 동작:
    ///   0.3.0 의 독립 FloatingScreen(렌더링 검증됨)을 그대로 쓰되, 매 프레임 BSP_Chat
    ///   모듈의 패널 위치를 따라가게(follow) 한다. 우선순위:
    ///     1) 시청자 수(Status) 패널(<c>m_StatusFloatingPanel</c>)이 보이면 → 그 패널 "바로 위"
    ///        ("Show viewer count" 옵션이 켜져 오른쪽에 인원수 표시가 떠 있을 때)
    ///     2) Status 패널이 없으면(옵션 꺼짐) → 채팅 패널(<c>m_ChatFloatingPanel</c>)의 "오른쪽 아래"
    ///     3) 둘 다 없으면 → 기본 위치(ScreenPos)
    ///   패널은 모두 <see cref="CFloatingPanel"/> 타입이고, 런타임 리플렉션으로 찾는다.
    ///   → 채팅창을 VR 컨트롤러로 옮기면 음성 인원수도 같이 따라 움직인다.
    ///
    ///   ※ 패널 캔버스에 자식으로 직접 넣지 않는 이유: 그 캔버스의 커브드 머티리얼/클리핑
    ///   때문에 일반 TMP 가 안 보이게 렌더된다. 그래서 별도 FloatingScreen 을 월드 공간에서
    ///   패널 위로 좌표만 맞춘다.
    ///
    /// ⚠ 스레드 주의: Push() 는 WebSocket 수신 스레드에서 호출되므로 Unity 객체를
    /// 직접 만들면 "can only be called from the main thread" 예외가 난다.
    /// 따라서 Push() 는 값만 저장하고, 실제 UI 생성/갱신/추적은 Unity 메인 스레드의
    /// Update() 에서 수행한다.
    /// </summary>
    public class VoiceIndicator : MonoBehaviour
    {
        private static VoiceIndicator _instance;

        private FloatingScreen _screen;
        private TextMeshProUGUI _text;

        // Push() 는 임의 스레드에서 호출됨 → lock 으로 보호, Update() 가 메인 스레드에서 소비.
        private static readonly object StateLock = new object();
        private static int _pendingCount;
        private static string _pendingChannel;
        private static bool _dirty;

        // 메인 스레드에서 마지막으로 소비한 값
        private int _count;
        private string _channel;
        private bool _hasValue;

        // 따라가기 상태
        private bool _wasFollowing;            // 직전 프레임에 패널을 따라가고 있었는지
        private bool _placedDefault;           // 기본 위치에 한 번이라도 놓았는지

        // FloatingScreen 생성 재시도 제어 (NRE 시 매 프레임 누수 방지)
        private float _nextCreateAttempt;      // Time.unscaledTime 기준 다음 시도 시각
        private int _createAttempts;
        private const int MaxCreateAttempts = 30;

        // 패널과 우리 화면 사이 추가 여백(월드 단위).
        private const float FollowWorldGap = 0.02f;

        // ---- FloatingScreen 위치/크기 (패널을 전혀 못 찾을 때의 기본값, 0.3.0 동작 유지) ----
        private static readonly Vector2 ScreenSize = new Vector2(22f, 12f);
        private static readonly Vector3 ScreenPos = new Vector3(-2.4f, 3.2f, 2.0f);
        private static readonly Quaternion ScreenRot = Quaternion.Euler(0, -30, 0);

        // ---- 캐시된 리플렉션 (BSP_Chat 는 우리 뒤에 로드되므로 늦게 해석될 수 있음) ----
        private static Type _chatType;
        private static PropertyInfo _instanceProp;
        private static FieldInfo _statusPanelField;
        private static FieldInfo _chatPanelField;

        /// <summary>메인 스레드(Plugin.OnEnable)에서 호출 — 영속 GameObject 생성.</summary>
        public static void Init()
        {
            if (_instance != null) return;
            try
            {
                var go = new GameObject("izunaisbot-VoiceIndicator");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<VoiceIndicator>();
            }
            catch (Exception err)
            {
                Plugin.Log?.Warn("VoiceIndicator init 실패: " + err.Message);
            }
        }

        /// <summary>어떤 스레드에서든 안전. 값만 저장하고 Update() 가 메인 스레드에서 반영.</summary>
        public static void Push(int count, string voiceChannelName)
        {
            if (count < 0) count = 0;
            lock (StateLock)
            {
                _pendingCount = count;
                _pendingChannel = voiceChannelName;
                _dirty = true;
            }
        }

        /// <summary>메인 스레드(Plugin.OnDisable)에서 호출 — UI 제거.</summary>
        public static void Shutdown()
        {
            var inst = _instance;
            _instance = null;
            if (inst == null) return;
            try { if (inst.gameObject != null) Destroy(inst.gameObject); }
            catch { }
        }

        private void Update()
        {
            lock (StateLock)
            {
                if (_dirty)
                {
                    _dirty = false;
                    _count = _pendingCount;
                    _channel = _pendingChannel;
                    _hasValue = true;
                }
            }

            // 음성 카운트가 한 번이라도 와야 화면을 만들고 추적한다.
            // (게임 초기 로딩 씬에서는 FloatingScreen 인프라가 아직 준비되지 않아
            //  CreateFloatingScreen 이 NRE 를 내며 부분 생성된 캔버스가 매 프레임
            //  누적 → VR 포인터 클릭을 가로막는다. 0.3.0 의 생성 타이밍을 복원.)
            if (!_hasValue) return;

            EnsureCreated();
            if (_screen == null) return;

            // 우선순위에 따라 따라갈 패널 선택
            CFloatingPanel target = null;
            bool bottomRight = false;
            try
            {
                if (EnsureReflection())
                {
                    var inst = _instanceProp.GetValue(null, null);
                    if (inst != null)
                    {
                        var status = AsUsable(_statusPanelField.GetValue(inst) as CFloatingPanel);
                        if (status != null)
                        {
                            target = status;            // 시청자 수 패널 위
                        }
                        else
                        {
                            var chat = AsUsable(_chatPanelField.GetValue(inst) as CFloatingPanel);
                            if (chat != null)
                            {
                                target = chat;          // 옵션 꺼짐 → 채팅 패널 오른쪽 아래
                                bottomRight = true;
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Plugin.Log?.Warn("VoiceIndicator: 패널 조회 실패: " + err.Message);
                target = null;
            }

            if (target != null)
            {
                if (bottomRight) PositionBottomRight(target);
                else PositionAboveTop(target);
                _placedDefault = false;
            }
            else if (_wasFollowing || !_placedDefault)
            {
                // 패널을 잃었거나 최초 → 기본 위치로 한 번 놓는다.
                _screen.transform.position = ScreenPos;
                _screen.transform.rotation = ScreenRot;
                _placedDefault = true;
            }
            _wasFollowing = target != null;

            if (_text != null) _text.text = FormatText();
        }

        private string FormatText()
        {
            // Discord blurple #5865F2 색 점 + 숫자
            if (_hasValue && _count > 0 && !string.IsNullOrEmpty(_channel))
                return "<color=#5865F2>●</color> " + _count;
            return "<color=#5865F2>●</color> <color=#888>—</color>";
        }

        /// <summary>패널이 존재하고 화면에 활성화돼 있으면 그대로 반환, 아니면 null.</summary>
        private static CFloatingPanel AsUsable(CFloatingPanel panel)
        {
            if (panel == null) return null;   // Unity-null(파괴됨) 포함
            try
            {
                var go = panel.gameObject;
                if (go == null || !go.activeInHierarchy) return null;   // 옵션 꺼짐 → 숨김
            }
            catch { return null; }
            return panel;
        }

        /// <summary>패널의 상단 중앙 바로 위로 우리 FloatingScreen 의 위치/회전을 맞춘다.</summary>
        private void PositionAboveTop(CFloatingPanel panel)
        {
            var rt = panel.RTransform;
            if (rt == null) return;

            // 패널의 월드 4코너 (0=좌하,1=좌상,2=우상,3=우하) — pivot/scale/rotation 모두 반영.
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 topCenter = (corners[1] + corners[2]) * 0.5f;
            Vector3 up = corners[1] - corners[0];
            Vector3 upN = up.sqrMagnitude > 1e-8f ? up.normalized : rt.up;

            float ourHalfHeight = ScreenSize.y * 0.5f * Mathf.Abs(_screen.transform.lossyScale.y);

            _screen.transform.position = topCenter + upN * (ourHalfHeight + FollowWorldGap);
            _screen.transform.rotation = rt.rotation;
        }

        /// <summary>패널의 오른쪽 아래(우하단)로 우리 FloatingScreen 의 위치/회전을 맞춘다.</summary>
        private void PositionBottomRight(CFloatingPanel panel)
        {
            var rt = panel.RTransform;
            if (rt == null) return;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 bottomLeft = corners[0];
            Vector3 topLeft = corners[1];
            Vector3 bottomRight = corners[3];

            Vector3 right = bottomRight - bottomLeft;
            Vector3 rightN = right.sqrMagnitude > 1e-8f ? right.normalized : rt.right;
            Vector3 up = topLeft - bottomLeft;
            Vector3 upN = up.sqrMagnitude > 1e-8f ? up.normalized : rt.up;

            float ourHalfW = ScreenSize.x * 0.5f * Mathf.Abs(_screen.transform.lossyScale.x);
            float ourHalfH = ScreenSize.y * 0.5f * Mathf.Abs(_screen.transform.lossyScale.y);

            // 오른쪽 가장자리를 패널 오른쪽에 맞추고, 바닥 아래로 살짝 내려 띄운다.
            _screen.transform.position =
                bottomRight - rightN * ourHalfW - upN * (ourHalfH + FollowWorldGap);
            _screen.transform.rotation = rt.rotation;
        }

        // ================================================================
        // BSP_Chat 패널 조회 (리플렉션)
        // ================================================================

        /// <summary>ChatPlexMod_Chat.Chat 타입/멤버를 한 번 해석해 캐시. 아직 로드 전이면 false (다음에 재시도).</summary>
        private static bool EnsureReflection()
        {
            if (_instanceProp != null && _statusPanelField != null && _chatPanelField != null)
                return true;

            var t = FindType("ChatPlexMod_Chat.Chat");
            if (t == null) return false;   // BSP_Chat 아직 미로딩 → 다음 주기에 재시도
            _chatType = t;

            // Instance 는 베이스 CP_SDK.ModuleBase<Chat> 에 선언된 public static 프로퍼티
            for (var b = t; b != null && _instanceProp == null; b = b.BaseType)
            {
                _instanceProp = b.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            const BindingFlags ff = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // 오른쪽 "시청자/인원수" 표시 = Status 패널, 옵션 꺼지면 채팅 패널로 폴백.
            _statusPanelField = t.GetField("m_StatusFloatingPanel", ff);
            _chatPanelField = t.GetField("m_ChatFloatingPanel", ff);

            return _instanceProp != null && _statusPanelField != null && _chatPanelField != null;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        // ================================================================
        // FloatingScreen 생성
        // ================================================================

        private void EnsureCreated()
        {
            if (_screen != null) return;
            // 메뉴 씬이 준비되기 전엔 BSML FloatingScreen 인프라가 없어 CreateFloatingScreen 이
            // NRE 를 내며, 부분 생성된 FloatingScreen 의 망가진 VRGraphicRaycaster 가 남아
            // EventSystem.RaycastAll 을 매 프레임 터뜨려 모든 버튼 클릭을 막는다. → 준비 후에만 생성.
            if (!InGameMenu.MenuReady) return;
            if (Time.unscaledTime < _nextCreateAttempt) return;     // 시도 간격 최소 1초
            _nextCreateAttempt = Time.unscaledTime + 1f;

            // ── 좀비 레이캐스터 차단 (메뉴 버튼 클릭 막힘 원인) ──────────────────
            // CreateFloatingScreen 은 내부에서 BeatSaberUI.PhysicsRaycasterWithCache 에
            // 접근하는데, VR 포인터/PhysicsRaycaster 가 아직 없으면 Enumerable.First 가
            // "Sequence contains no elements" 로 던진다. 그 예외가 CreateFloatingScreen
            // *도중* 터지면 BSML 이 이미 만든 FloatingScreen(+망가진 VRGraphicRaycaster)이
            // 우리 _screen 에 안 담긴 채 씬에 좀비로 남아 EventSystem.RaycastAll 을 매
            // 프레임 터뜨려 모든 버튼 클릭을 막는다(catch 에서 _screen 이 null 이라 정리 불가).
            // → 먼저 캐시를 워밍해서, 준비됐을 때만 실제 생성을 호출한다.
            //   준비 전이면 조용히 대기(시도 횟수 소모 안 함 → 나중에 준비돼도 인디케이터 표시 가능).
            try
            {
                if (BeatSaberUI.PhysicsRaycasterWithCache == null) return;
            }
            catch
            {
                return;   // 아직 미준비 → 다음 주기 재시도(좀비 생성하지 않음)
            }

            if (_createAttempts >= MaxCreateAttempts) return;       // 준비 후에도 계속 실패하면 포기 (스팸 방지)
            _createAttempts++;
            try
            {
                _screen = FloatingScreen.CreateFloatingScreen(ScreenSize, false, ScreenPos, ScreenRot);
                DontDestroyOnLoad(_screen.gameObject);

                // 인디케이터는 표시 전용 — 입력에 절대 참여하지 않게 한다.
                // (레이캐스터를 남겨두면 우리 화면의 VRGraphicRaycaster 가 깨질 경우
                //  EventSystem.RaycastAll 전체가 죽어 모든 버튼이 클릭 불가가 됨.)
                MakeNonInteractive(_screen.gameObject);

                // 반투명 배경
                var bg = _screen.gameObject.GetComponentInChildren<Image>();
                if (bg != null) bg.color = new Color(0f, 0f, 0f, 0.5f);

                var textGo = new GameObject("VoiceCountText");
                textGo.transform.SetParent(_screen.transform, false);
                _text = textGo.AddComponent<TextMeshProUGUI>();
                _text.fontSize = 6.5f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.richText = true;
                _text.text = FormatText();
                var rt = _text.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            catch (Exception err)
            {
                // 부분 생성된 FloatingScreen 이 남아 클릭을 가로막지 않도록 정리.
                Plugin.Log?.Warn("VoiceIndicator create 실패(" + _createAttempts + "/" + MaxCreateAttempts + "): " + err);
                try { if (_screen != null) Destroy(_screen.gameObject); } catch { }
                _screen = null;
                _text = null;
            }
        }

        /// <summary>FloatingScreen 을 입력 비참여로 만든다 — 레이캐스터 제거 + CanvasGroup 차단.</summary>
        private static void MakeNonInteractive(GameObject root)
        {
            try
            {
                // GraphicRaycaster / VRGraphicRaycaster 모두 BaseRaycaster 파생 → 한 번에 제거.
                foreach (var rc in root.GetComponentsInChildren<UnityEngine.EventSystems.BaseRaycaster>(true))
                    Destroy(rc);

                var cg = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            catch (Exception err)
            {
                Plugin.Log?.Warn("VoiceIndicator: 입력 비참여 설정 실패: " + err.Message);
            }
        }

        private void OnDestroy()
        {
            try { if (_screen != null) Destroy(_screen.gameObject); }
            catch { }
            _screen = null;
            _text = null;
        }
    }
}
