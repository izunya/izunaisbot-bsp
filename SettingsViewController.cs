using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 인게임 설정 패널의 뷰. UI/settings.bsml 을 임베디드 리소스로 로드.
    /// 탭 구조라 나중에 &lt;tab&gt; 을 추가해 기능을 확장하면 된다. (영어 고정)
    /// </summary>
    internal class SettingsViewController : BSMLResourceViewController
    {
        public override string ResourceName => "IzunaisbotBSP.UI.settings.bsml";

        [UIAction("web-open")]
        private void WebOpen() => InGameMenu.OpenWeb();
    }
}
