using System;
using System.Collections.Generic;

namespace cfg
{
    /// <summary>
    /// 为配置表补充按配置名快速读取数值的能力。
    /// </summary>
    public partial class TbConfig
    {
        private Dictionary<string, Config> configMap;

        /// <summary>
        /// 尝试按配置名读取对应配置行。
        /// </summary>
        public bool TryGet(string configName, out Config config)
        {
            EnsureConfigMap();
            return configMap.TryGetValue(configName ?? string.Empty, out config);
        }

        /// <summary>
        /// 按配置名读取列表 index=0 的整型数值，不存在时返回兜底值。
        /// </summary>
        public int GetValueOrDefault(string configName, int defaultValue)
        {
            return GetIntAt(configName, 0, defaultValue);
        }

        /// <summary>
        /// 按配置名读取指定下标的整型数值。
        /// </summary>
        public int GetIntAt(string configName, int index, int defaultValue)
        {
            if (!TryGet(configName, out var config) || config?.Value == null || config.Value.Count == 0)
            {
                return defaultValue;
            }

            if (index < 0)
            {
                index = 0;
            }

            return index < config.Value.Count ? config.Value[index] : config.Value[config.Value.Count - 1];
        }

        /// <summary>
        /// 读取配置数值列表；不存在时返回 null。
        /// </summary>
        public IReadOnlyList<int> GetValueList(string configName)
        {
            return TryGet(configName, out var config) ? config.Value : null;
        }

        private void EnsureConfigMap()
        {
            if (configMap != null)
            {
                return;
            }

            configMap = new Dictionary<string, Config>(StringComparer.OrdinalIgnoreCase);
            if (_dataList == null)
            {
                return;
            }

            for (var index = 0; index < _dataList.Count; index++)
            {
                var config = _dataList[index];
                if (config == null || string.IsNullOrWhiteSpace(config.ConfigName))
                {
                    continue;
                }

                configMap[config.ConfigName] = config;
            }
        }
    }
}
