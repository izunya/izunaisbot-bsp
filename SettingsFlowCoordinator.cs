using BeatSaberMarkupLanguage;
using HMUI;

namespace IzunaisbotBSP
{
    /// <summary>
    /// 모드탭 버튼 클릭 시 정면에 뜨는 설정 FlowCoordinator.
    /// 탭 뷰(SettingsViewController) 하나를 메인으로 제공. 뒤로가기 시 닫힘.
    /// </summary>
    internal class SettingsFlowCoordinator : FlowCoordinator
    {
        private SettingsViewController _viewController;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("izunaisbot", ViewController.AnimationType.In);
                showBackButton = true;
                _viewController = BeatSaberUI.CreateViewController<SettingsViewController>();
            }
            ProvideInitialViewControllers(_viewController, null, null, null, null);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}
