using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// Town HUD 子面板共享上下文。
    /// </summary>
    public class TownHudPanelData : UIPanelData
    {
        public Transform HudRoot;
        public TownStatusBarPanelController RootController;
    }
}
