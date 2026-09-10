using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JN.Client.Manager;
using JN.Client.Scene;

public class TableCleanButtonUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image glowImage;
    [SerializeField] private Image progressImage;
    [SerializeField] private Button button;
    [SerializeField] private Image progressBroom;
    [SerializeField] private float rotationValue = 25f;

    private TableArea boundTable;
    private Coroutine cleanRoutine;
    private Tween rotationTween;

    /// <summary>
    /// 初始化组件引用和运行时状态。
    /// </summary>
    private void Awake()
    {
        if (button != null)
        {
            button.onClick.AddListener(OnClick);
        }

        ResetVisuals();
    }

    /// <summary>
    /// 销毁时释放监听、协程和运行时缓存。
    /// </summary>
    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClick);
        }

        rotationTween?.Kill();
    }

    /// <summary>
    /// 初始化模块依赖和默认状态。
    /// </summary>
    /// <param name="table">桌位对象。</param>
    /// <param name="cleanDuration">持续时间。</param>
    public void Init(TableArea table, float cleanDuration)
    {
        boundTable = table;
        ResetVisuals();
        cleanRoutine = StartCoroutine(CleaningRoutine(cleanDuration));
    }

    /// <summary>
    /// 重置按钮图标、文字和特效显示。
    /// </summary>
    public void ResetVisuals()
    {
        if (cleanRoutine != null)
        {
            StopCoroutine(cleanRoutine);
            cleanRoutine = null;
        }

        rotationTween?.Kill();
        rotationTween = null;

        if (glowImage != null)
        {
            glowImage.gameObject.SetActive(true);
        }

        if (progressImage != null)
        {
            progressImage.fillAmount = 0f;
            if (progressImage.transform.parent != null)
            {
                progressImage.transform.parent.gameObject.SetActive(true);
            }
        }

        if (progressBroom != null)
        {
            progressBroom.gameObject.SetActive(true);
            progressBroom.transform.localRotation = Quaternion.identity;
        }

        var timerText = transform.Find("TimeTXT")?.GetComponent<TMP_Text>();
        if (timerText != null)
        {
            timerText.gameObject.SetActive(false);
            timerText.text = string.Empty;
        }
    }

    /// <summary>
    /// 按进度播放清扫协程。
    /// </summary>
    /// <param name="duration">持续时间。</param>
    /// <returns>协程迭代器。</returns>
    private IEnumerator CleaningRoutine(float duration)
    {
        duration = Mathf.Max(0.1f, duration);
        var remaining = duration;

        if (glowImage != null)
        {
            glowImage.gameObject.SetActive(false);
        }

        var timerText = transform.Find("TimeTXT")?.GetComponent<TMP_Text>();
        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
        }

        if (progressBroom != null)
        {
            rotationTween = progressBroom.transform
                .DORotate(new Vector3(0f, 0f, rotationValue), 0.5f, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Yoyo);
        }

        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;

            if (progressImage != null)
            {
                progressImage.fillAmount = Mathf.Clamp01(1f - (remaining / duration));
            }

            if (timerText != null)
            {
                timerText.text = $"{Mathf.CeilToInt(remaining)}s";
            }

            yield return null;
        }

        cleanRoutine = null;
    }

    /// <summary>
    /// 处理按钮点击事件。
    /// </summary>
    private void OnClick()
    {
        GameAudioManager.PlayButtonClick();
        boundTable?.HandleActionButtonClick();
    }
}
