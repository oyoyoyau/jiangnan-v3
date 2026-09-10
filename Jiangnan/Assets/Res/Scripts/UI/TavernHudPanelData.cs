using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// Tavern HUD 子面板共享上下文。
    /// </summary>
    public class TavernHudPanelData : UIPanelData
    {
        public Transform HudRoot;
        public TavernStatusBarPanelController RootController;
    }
}
