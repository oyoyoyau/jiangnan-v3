using UnityEngine;

namespace JN.Client
{
    /// <summary>
    /// 提供带颜色的调试日志输出工具。
    /// </summary>
    public static class DebugLogUtility
    {
        private const string PureGreenColor = "#00FF00";

        /// <summary>
        /// 以纯绿色输出普通日志。
        /// </summary>
        /// <param name="message">要输出的日志内容。</param>
        public static void LogSuccess(object message)
        {
            Debug.Log(WrapWithColor(message, PureGreenColor));
        }

        /// <summary>
        /// 把日志内容包装为带颜色的富文本。
        /// </summary>
        /// <param name="message">原始日志内容。</param>
        /// <param name="colorHex">颜色十六进制值。</param>
        /// <returns>带颜色标签的日志文本。</returns>
        public static string WrapWithColor(object message, string colorHex)
        {
            var text = message == null ? "null" : message.ToString();
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return text;
            }

            return $"<color={colorHex}>{text}</color>";
        }
    }
}
