using cfg;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// 汇总 HUD 弹层常用资源路径和静态业务常量。
    /// </summary>
    internal static class HudOverlayAssetCatalog
    {
        public const string RecruitTabNormalSpritePath = "Assets/Res/Resources/Textures/UI/Panel/Recruit/panel_normal.png";
        public const string RecruitTabSelectedSpritePath = "Assets/Res/Resources/Textures/UI/Panel/Recruit/panel_light.png";
        public const string FeatureUnlockBannerSpritePath =
            "Assets/Res/Resources/Textures/UI/Panel/NewFeatureOpenTableLV2Panel/xgnkq.png";
        public const string TableLv2UpgradeUnlockContentSpritePath =
            "Assets/Res/Resources/Textures/UI/Panel/NewFeatureOpenTableLV2Panel/sjzz.png";
        public const string AchievementEntryUnlockContentSpritePath =
            "Assets/Res/Textures/UI/Panel/Bottom/img_Facility_Btn.png";
        public const string TechEntryUnlockContentSpritePath =
            "Assets/Res/Textures/UI/Panel/Bottom/img_Staff_Btn.png";
        public const int RecruitChefStaffId = 4;
        public const int RecruitWaiterStaffId = 5;
        public const int MaxTableLevel = 3;
        public const int TableUpgradeBaseCost = 800;

        private const string TechTreeIconPathFormat = "Assets/Res/Resources/Textures/UI/TechTree/{0}{1}.png";

        private const string RecruitShopkeeperPortraitPath = "Assets/Res/Resources/Textures/UI/Common/halfPic/zhanggui.png";

        private static readonly string[] TableLevelIconPaths =
        {
            "Assets/Res/Resources/Textures/UI/Icons 1/Furnitures/tableLvl1.png",
            "Assets/Res/Resources/Textures/UI/Icons 1/Furnitures/tableLvl2.png",
            "Assets/Res/Resources/Textures/UI/Icons 1/Furnitures/tableLvl3.png"
        };

        private static readonly string[] TableLevelDisplayNames =
        {
            "木桌",
            "雕花桌",
            "鎏金桌"
        };

        private static readonly string[] RecruitChefPortraitPaths =
        {
            "Assets/Res/Resources/Textures/UI/Common/halfPic/chushi1.png",
            "Assets/Res/Resources/Textures/UI/Common/halfPic/chushi2.png",
            "Assets/Res/Resources/Textures/UI/Common/halfPic/chushi3.png"
        };

        private static readonly string[] RecruitWaiterPortraitPaths =
        {
            "Assets/Res/Resources/Textures/UI/Common/halfPic/xiaoer1.png",
            "Assets/Res/Resources/Textures/UI/Common/halfPic/xiaoer2.png",
            "Assets/Res/Resources/Textures/UI/Common/halfPic/xiaoer3.png"
        };

        /// <summary>
        /// 统一从资源仓库加载 HUD 所需图片。
        /// </summary>
        public static Sprite LoadSprite(string path)
        {
            return GameplayResourceStore.LoadAsset<Sprite>(path);
        }

        /// <summary>
        /// 科技树节点图标：icon 基名 + 变体序号（1=常态，2=未完成/研究中）。
        /// </summary>
        public static Sprite LoadTechTreeIcon(string iconBase, int variant = 1)
        {
            if (string.IsNullOrWhiteSpace(iconBase))
            {
                return null;
            }

            return LoadSprite(string.Format(TechTreeIconPathFormat, iconBase, variant));
        }

        /// <summary>
        /// 取得桌子等级对应的展示名称。
        /// </summary>
        public static string GetTableLevelDisplayName(int level)
        {
            var index = Mathf.Clamp(level, 1, MaxTableLevel) - 1;
            return TableLevelDisplayNames[index];
        }

        /// <summary>
        /// 取得桌子等级对应的图标路径。
        /// </summary>
        public static string GetTableIconPath(int level)
        {
            var index = Mathf.Clamp(level, 1, MaxTableLevel) - 1;
            return TableLevelIconPaths[index];
        }

        /// <summary>
        /// 计算桌子升级目标等级的花费。
        /// </summary>
        public static int GetTableUpgradeCost(int targetLevel)
        {
            return TableUpgradeBaseCost * Mathf.Max(2, targetLevel);
        }

        /// <summary>
        /// 将招募角色枚举转成展示名称。
        /// </summary>
        public static string GetRecruitRoleName(RecruitPanelRole role)
        {
            return role == RecruitPanelRole.Chef ? "厨师" : "小二";
        }

        /// <summary>
        /// 获取招募角色对应的 staffId。
        /// </summary>
        public static int GetRecruitStaffId(RecruitPanelRole role)
        {
            return role == RecruitPanelRole.Chef ? RecruitChefStaffId : RecruitWaiterStaffId;
        }

        /// <summary>
        /// 获取招募角色对应的员工类型。
        /// </summary>
        public static StaffRole GetRecruitStaffRole(RecruitPanelRole role)
        {
            return role == RecruitPanelRole.Chef ? StaffRole.Chef : StaffRole.Waiter;
        }

        /// <summary>
        /// 为已入职员工解析半身像（与招聘列表共用 halfPic 资源池）。
        /// </summary>
        public static Sprite ResolveStaffPortrait(Staff staff)
        {
            if (staff == null)
            {
                return null;
            }

            switch (staff.Position)
            {
                case StaffPosition.Shopkeeper:
                    return LoadSprite(RecruitShopkeeperPortraitPath);
                case StaffPosition.Chef:
                    return ResolveRecruitListPortrait(
                        RecruitPanelRole.Chef,
                        GetStaffPortraitVariantIndex(staff),
                        null);
                case StaffPosition.Waiter:
                    return ResolveRecruitListPortrait(
                        RecruitPanelRole.Waiter,
                        GetStaffPortraitVariantIndex(staff),
                        null);
                default:
                    return null;
            }
        }

        private static int GetStaffPortraitVariantIndex(Staff staff)
        {
            return Mathf.Abs(staff.Id) % 3;
        }

        /// <summary>
        /// 为招募列表项选择合适的半身像。
        /// </summary>
        public static Sprite ResolveRecruitListPortrait(RecruitPanelRole role, int index, Sprite fallbackPortrait)
        {
            var portraitPaths = role == RecruitPanelRole.Chef ? RecruitChefPortraitPaths : RecruitWaiterPortraitPaths;
            if (index >= 0 && index < portraitPaths.Length)
            {
                var sprite = LoadSprite(portraitPaths[index]);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return fallbackPortrait;
        }

        /// <summary>
        /// 为单人招募确认面板选择角色半身像。
        /// </summary>
        public static Sprite ResolveSingleRecruitPortrait(string roleText, Sprite fallbackPortrait)
        {
            if (!string.IsNullOrWhiteSpace(roleText))
            {
                if (roleText.Contains("掌柜"))
                {
                    var shopkeeperPortrait = LoadSprite(RecruitShopkeeperPortraitPath);
                    if (shopkeeperPortrait != null)
                    {
                        return shopkeeperPortrait;
                    }
                }

                if (roleText.Contains("厨师"))
                {
                    var chefPortrait = LoadSprite(RecruitChefPortraitPaths[0]);
                    if (chefPortrait != null)
                    {
                        return chefPortrait;
                    }
                }

                if (roleText.Contains("小二"))
                {
                    var waiterPortrait = LoadSprite(RecruitWaiterPortraitPaths[0]);
                    if (waiterPortrait != null)
                    {
                        return waiterPortrait;
                    }
                }
            }

            return fallbackPortrait;
        }

        /// <summary>
        /// 将文本转换成逐字换行的竖排展示格式。
        /// </summary>
        public static string ToVerticalText(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            return string.Join("\n", content.ToCharArray());
        }

        /// <summary>
        /// 判断提示语是否属于铜钱不足类型。
        /// </summary>
        public static bool IsCoinShortageMessage(string message)
        {
            return !string.IsNullOrWhiteSpace(message)
                   && (message.Contains("金币不足") || message.Contains("铜钱不足"));
        }
    }
}
