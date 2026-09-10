using UnityEngine;

namespace JN.Client.Messages
{
    [System.Serializable]
    public class BuildingInfo
    {
        [Header("基础信息")] public int playerId;
        public string name;
        public int tileId;

        [Header("建筑信息")] public int buildingId;
        public int buildingLevel;
        public int buildingTime;

        /// <summary>
        /// 处理状态相关逻辑。
        /// </summary>
        public int status;

        public int value;
        public int celebrationTime;

        /// <summary>
        /// 城镇展示用成就 Id；自家酒楼仍优先读全局 displayedAchievementId。
        /// </summary>
        public int displayedAchievementId;
    }
}
