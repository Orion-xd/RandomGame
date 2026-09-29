using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メインアクションの先読み表示。
/// 画面下部へ横一列に配置し、先頭のアクションを大きく表示する。
/// アクション消費時は、先頭が左へ抜け、残りが一つずつ左へ移動し、
/// 新しい抽選結果が右端から入るコンベア式のアニメーションを再生する。
/// </summary>
public class ActionBarUI : MonoBehaviour
{
    [Tooltip("表示対象のアクションキュー。未設定の場合はTag=Playerから自動取得します。")]
    [SerializeField] private MainActionQueue queue;
    [Tooltip("クールタイムとコンボ受付割合の取得元。未設定の場合はTag=Playerから自動取得します。")]
    [SerializeField] private MainActionController controller;
    [Tooltip("各スロットの暗色背景")]
    [SerializeField] private Image[] slotBackgrounds;
    [Tooltip("各スロットの明色オーバーレイ")]
    [SerializeField] private Image[] slotFills;
    [SerializeField] private Text[] slotLabels;
    [Tooltip("次に発動するアクション専用の枠")]
    [SerializeField] private Image nextActionBorder;

    [Header("アクション別カラー")]
    [SerializeField] private Color jumpColor = new Color(0.30f, 0.70f, 1f);
    [SerializeField] private Color dashColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private Color attackColor = new Color(1f, 0.40f, 0.40f);

    [Header("アクションカード画像")]
    [SerializeField] private Sprite attackIcon;
    [SerializeField] private Sprite dashIcon;
    [SerializeField] private Sprite jumpIcon;
    [SerializeField] private float iconSize = 68f;
    [SerializeField] private int actionFontSize = 28;
    [SerializeField] private int titleFontSize = 20;

    [Header("暗さ")]
    [Range(0f, 1f)]
    [SerializeField] private float dimAmount = 0.6f;

    [Header("下部レイアウト")]
    [Tooltip("有効にすると、シーン上のRectTransformをそのまま使用し、実行時に配置を上書きしません。")]
    [SerializeField] private bool useSceneLayout = true;
    [SerializeField] private float bottomMargin = -64f;
    [SerializeField] private float horizontalPadding = 0f;
    [SerializeField] private float slotGap = 0f;
    [SerializeField] private float barHeight = 154f;
    [Tooltip("カード画像の表示サイズ。0以下の軸はスロットの自動サイズを使用します。")]
    [SerializeField] private Vector2 cardSize = Vector2.zero;
    [Tooltip("全カード共通の表示位置オフセットです。")]
    [SerializeField] private Vector2 cardPositionOffset = Vector2.zero;
    [SerializeField] private float currentSlotScale = 1.22f;
    [SerializeField] private float queuedSlotScale = 1f;

    [Header("コンベアアニメーション")]
    [Min(0.05f)]
    [SerializeField] private float moveDuration = 0.32f;
    [SerializeField] private float usePopScale = 1.32f;
    [SerializeField] private float incomingStartScale = 0.35f;

    private RectTransform _root;
    private RectTransform[] _slotRects;
    private Image[] _slotIcons;
    private Vector2[] _slotPositions;
    private Vector3[] _sceneSlotScales;
    private MainActionType?[] _shownActions;
    private Coroutine _conveyorRoutine;
    private bool _animating;
    private float _lastParentWidth;

    private void Awake()
    {
        ResolvePlayerReferences();
        CacheAndApplyLayout();
    }

    private void OnEnable()
    {
        if (queue != null) queue.OnChanged += HandleQueueChanged;
        RefreshImmediate();
    }

    private void OnDisable()
    {
        if (queue != null) queue.OnChanged -= HandleQueueChanged;
        if (_conveyorRoutine != null) StopCoroutine(_conveyorRoutine);
        _conveyorRoutine = null;
        _animating = false;
    }

    private void Update()
    {
        float parentWidth = ParentWidth();
        if (!useSceneLayout && !_animating && Mathf.Abs(parentWidth - _lastParentWidth) > 1f)
        {
            CacheAndApplyLayout();
            RefreshImmediate();
        }

        if (!_animating) RefreshReadinessOnly();
    }

    private void ResolvePlayerReferences()
    {
        if (queue != null && controller != null) return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("ActionBarUI: Tag=Playerのオブジェクトが見つかりません。", this);
            return;
        }

        if (queue == null) queue = player.GetComponent<MainActionQueue>();
        if (controller == null) controller = player.GetComponent<MainActionController>();
    }

    private void CacheAndApplyLayout()
    {
        _root = transform as RectTransform;
        if (_root == null || slotBackgrounds == null) return;

        int count = slotBackgrounds.Length;
        if (_slotRects == null || _slotRects.Length != count) _slotRects = new RectTransform[count];
        if (_slotPositions == null || _slotPositions.Length != count) _slotPositions = new Vector2[count];
        if (_slotIcons == null || _slotIcons.Length != count) _slotIcons = new Image[count];
        if (_sceneSlotScales == null || _sceneSlotScales.Length != count) _sceneSlotScales = new Vector3[count];
        if (_shownActions == null || _shownActions.Length != count) _shownActions = new MainActionType?[count];

        if (useSceneLayout)
        {
            _lastParentWidth = ParentWidth();
            for (int i = 0; i < count; i++)
            {
                RectTransform rect = slotBackgrounds[i] != null ? slotBackgrounds[i].rectTransform : null;
                _slotRects[i] = rect;
                _slotPositions[i] = rect != null ? rect.anchoredPosition : Vector2.zero;
                _sceneSlotScales[i] = rect != null ? rect.localScale : Vector3.one;
                _slotIcons[i] = null;
            }

            RectTransform sceneTitle = transform.Find("Title") as RectTransform;
            if (sceneTitle != null)
            {
                sceneTitle.SetAsLastSibling();
            }

            ResetSlotTransforms();
            return;
        }

        _root.anchorMin = new Vector2(0f, 0f);
        _root.anchorMax = new Vector2(1f, 0f);
        _root.pivot = new Vector2(0.5f, 0f);
        _root.anchoredPosition = new Vector2(0f, bottomMargin);
        _root.sizeDelta = new Vector2(0f, barHeight);

        Canvas.ForceUpdateCanvases();
        float rootWidth = Mathf.Max(1f, _root.rect.width);
        _lastParentWidth = ParentWidth();
        float availableWidth = Mathf.Max(1f, rootWidth - horizontalPadding * 2f);
        float slotSpacing = availableWidth / count;
        Vector2 automaticSlotSize = new Vector2(Mathf.Max(1f, slotSpacing - slotGap), barHeight);
        Vector2 calculatedSlotSize = new Vector2(
            cardSize.x > 0f ? cardSize.x : automaticSlotSize.x,
            cardSize.y > 0f ? cardSize.y : automaticSlotSize.y);

        for (int i = 0; i < count; i++)
        {
            RectTransform rect = slotBackgrounds[i] != null ? slotBackgrounds[i].rectTransform : null;
            _slotRects[i] = rect;
            float x = -rootWidth * 0.5f + horizontalPadding + slotSpacing * (i + 0.5f);
            _slotPositions[i] = new Vector2(x, 0f) + cardPositionOffset;
            if (rect == null) continue;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = calculatedSlotSize;
            ConfigureSlotContents(i, rect, calculatedSlotSize);
        }

        RectTransform title = transform.Find("Title") as RectTransform;
        if (title != null)
        {
            title.anchorMin = new Vector2(0.5f, 0.5f);
            title.anchorMax = new Vector2(0.5f, 0.5f);
            title.pivot = new Vector2(0.5f, 0.5f);
            title.anchoredPosition = _slotPositions[0] + new Vector2(0f, barHeight * currentSlotScale + 22f);
            title.sizeDelta = new Vector2(calculatedSlotSize.x, 24f);
            Text titleText = title.GetComponent<Text>();
            if (titleText != null) titleText.fontSize = titleFontSize;
            title.SetAsLastSibling();
        }

        ResetSlotTransforms();
    }

    private float ParentWidth()
    {
        RectTransform parent = _root != null ? _root.parent as RectTransform : null;
        return parent != null ? parent.rect.width : 0f;
    }

    private void ConfigureSlotContents(int index, RectTransform slotRect, Vector2 calculatedSlotSize)
    {
        if (slotRect == null) return;

        Transform existing = slotRect.Find("ActionIcon");
        Image icon;
        if (existing != null)
        {
            icon = existing.GetComponent<Image>();
        }
        else
        {
            var iconObject = new GameObject("ActionIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(slotRect, false);
            icon = iconObject.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
        }

        _slotIcons[index] = icon;
        icon.gameObject.SetActive(false);
        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.27f, 0.5f);
        iconRect.anchorMax = new Vector2(0.27f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        float size = Mathf.Min(iconSize, calculatedSlotSize.y * 0.78f);
        iconRect.sizeDelta = new Vector2(size, size);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.SetAsLastSibling();

        if (slotLabels != null && index < slotLabels.Length && slotLabels[index] != null)
        {
            slotLabels[index].gameObject.SetActive(false);
            RectTransform labelRect = slotLabels[index].rectTransform;
            labelRect.anchorMin = new Vector2(0.36f, 0f);
            labelRect.anchorMax = new Vector2(0.96f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            slotLabels[index].resizeTextForBestFit = false;
            slotLabels[index].fontSize = actionFontSize;
        }
    }

    private void HandleQueueChanged()
    {
        if (!isActiveAndEnabled || !IsConveyorShift())
        {
            RefreshImmediate();
            return;
        }

        if (_conveyorRoutine != null) StopCoroutine(_conveyorRoutine);
        _conveyorRoutine = StartCoroutine(AnimateConveyor());
    }

    private bool IsConveyorShift()
    {
        if (queue == null || _shownActions == null || _shownActions.Length < 2) return false;
        for (int i = 0; i < _shownActions.Length - 1; i++)
        {
            if (_shownActions[i + 1] != queue.Peek(i)) return false;
        }
        return true;
    }

    private IEnumerator AnimateConveyor()
    {
        _animating = true;

        int last = _slotRects.Length - 1;
        float conveyorSpacing = _slotPositions.Length > 1
            ? _slotPositions[1].x - _slotPositions[0].x
            : _root.rect.width;
        RectTransform oldLastGhost = Instantiate(_slotRects[last].gameObject, _root).transform as RectTransform;
        oldLastGhost.name = "移動中スロット";
        oldLastGhost.SetSiblingIndex(_slotRects[last].GetSiblingIndex());
        oldLastGhost.anchoredPosition = _slotPositions[last];
        oldLastGhost.localScale = useSceneLayout ? _sceneSlotScales[last] : SlotScale(queuedSlotScale);

        // 実体の末尾スロットは、新しい抽選結果として右側から登場させる。
        ApplySlot(last, queue.Peek(last), 1f);
        _slotRects[last].anchoredPosition = _slotPositions[last] + Vector2.right * conveyorSpacing;
        _slotRects[last].localScale = useSceneLayout
            ? Vector3.Scale(_sceneSlotScales[last], SlotScale(incomingStartScale))
            : SlotScale(incomingStartScale);

        Vector2 firstStart = _slotPositions[0];
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = t * t * (3f - 2f * t);

            // 使用したアクションは一度大きく反応してから左へ抜ける。
            float pop = t < 0.28f
                ? Mathf.Lerp(currentSlotScale, usePopScale, t / 0.28f)
                : Mathf.Lerp(usePopScale, 0.15f, (t - 0.28f) / 0.72f);
            _slotRects[0].anchoredPosition = Vector2.Lerp(firstStart, firstStart - Vector2.right * conveyorSpacing, eased);
            _slotRects[0].localScale = useSceneLayout
                ? Vector3.Scale(_sceneSlotScales[0], SlotScale(pop / Mathf.Max(currentSlotScale, 0.001f)))
                : SlotScale(pop);
            _slotRects[0].localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -12f, eased));

            for (int i = 1; i < last; i++)
            {
                _slotRects[i].anchoredPosition = Vector2.Lerp(_slotPositions[i], _slotPositions[i - 1], eased);
                if (useSceneLayout)
                {
                    _slotRects[i].localScale = Vector3.Lerp(_sceneSlotScales[i], _sceneSlotScales[i - 1], eased);
                }
                else
                {
                    float targetScale = i == 1 ? currentSlotScale : queuedSlotScale;
                    _slotRects[i].localScale = SlotScale(Mathf.Lerp(queuedSlotScale, targetScale, eased));
                }
            }

            oldLastGhost.anchoredPosition = Vector2.Lerp(_slotPositions[last], _slotPositions[last - 1], eased);
            oldLastGhost.localScale = useSceneLayout
                ? Vector3.Lerp(_sceneSlotScales[last], _sceneSlotScales[last - 1], eased)
                : SlotScale(queuedSlotScale);

            _slotRects[last].anchoredPosition = Vector2.Lerp(
                _slotPositions[last] + Vector2.right * conveyorSpacing,
                _slotPositions[last], eased);
            _slotRects[last].localScale = useSceneLayout
                ? Vector3.Scale(_sceneSlotScales[last], SlotScale(Mathf.Lerp(incomingStartScale, 1f, eased)))
                : SlotScale(Mathf.Lerp(incomingStartScale, queuedSlotScale, eased));

            yield return null;
        }

        Destroy(oldLastGhost.gameObject);
        RefreshImmediate();
        _animating = false;
        _conveyorRoutine = null;
    }

    private void RefreshImmediate()
    {
        if (queue == null || slotBackgrounds == null || slotLabels == null || slotFills == null) return;

        int count = Mathf.Min(Mathf.Min(slotBackgrounds.Length, slotLabels.Length), slotFills.Length);
        for (int i = 0; i < count; i++)
        {
            MainActionType? action = queue.Peek(i);
            ApplySlot(i, action, i == 0 ? CurrentBrightFraction() : 1f);
            if (_shownActions != null && i < _shownActions.Length) _shownActions[i] = action;
        }

        ResetSlotTransforms();
        UpdateBorder();
    }

    private void RefreshReadinessOnly()
    {
        if (slotFills == null || slotFills.Length == 0 || slotFills[0] == null) return;
        slotFills[0].fillAmount = CurrentBrightFraction();
        UpdateBorder();
    }

    private void ApplySlot(int index, MainActionType? action, float brightFraction)
    {
        if (index < 0 || index >= slotBackgrounds.Length || index >= slotFills.Length || index >= slotLabels.Length) return;
        if (slotBackgrounds[index] == null || slotFills[index] == null || slotLabels[index] == null) return;

        if (action == null)
        {
            slotBackgrounds[index].sprite = null;
            slotFills[index].sprite = null;
            slotBackgrounds[index].color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
            slotFills[index].fillAmount = 0f;
            slotLabels[index].text = "-";
            if (_slotIcons != null && index < _slotIcons.Length && _slotIcons[index] != null)
                _slotIcons[index].enabled = false;
            return;
        }

        Sprite card = IconFor(action.Value);
        slotBackgrounds[index].sprite = card;
        slotBackgrounds[index].preserveAspect = true;
        slotBackgrounds[index].color = Dim(Color.white);
        slotFills[index].sprite = card;
        slotFills[index].preserveAspect = true;
        slotFills[index].color = Color.white;
        slotFills[index].fillAmount = brightFraction;
        slotLabels[index].text = LabelFor(action.Value);
        if (_slotIcons != null && index < _slotIcons.Length && _slotIcons[index] != null)
        {
            _slotIcons[index].sprite = IconFor(action.Value);
            _slotIcons[index].enabled = _slotIcons[index].sprite != null;
        }
    }

    private float CurrentBrightFraction()
    {
        float comboFraction = controller != null ? controller.ComboGraceFraction01 : 0f;
        if (comboFraction > 0f) return comboFraction;

        bool jumpCooling = controller != null && controller.IsJumpCooldownActive;
        float cooldownFraction = controller != null ? controller.CooldownFraction01 : 0f;
        if (jumpCooling) return 0f;
        if (cooldownFraction > 0f) return 1f - cooldownFraction;
        return 1f;
    }

    private void UpdateBorder()
    {
        if (nextActionBorder == null) return;
        float comboFraction = controller != null ? controller.ComboGraceFraction01 : 0f;
        bool jumpCooling = controller != null && controller.IsJumpCooldownActive;
        float cooldownFraction = controller != null ? controller.CooldownFraction01 : 0f;
        bool cooling = comboFraction <= 0f && (cooldownFraction > 0f || jumpCooling);
        nextActionBorder.gameObject.SetActive(!cooling || comboFraction > 0f);
    }

    private void ResetSlotTransforms()
    {
        if (_slotRects == null || _slotPositions == null) return;
        for (int i = 0; i < _slotRects.Length; i++)
        {
            if (_slotRects[i] == null) continue;
            _slotRects[i].anchoredPosition = _slotPositions[i];
            _slotRects[i].localRotation = Quaternion.identity;
            _slotRects[i].localScale = useSceneLayout
                ? _sceneSlotScales[i]
                : SlotScale(i == 0 ? currentSlotScale : queuedSlotScale);
        }
    }

    private static Vector3 SlotScale(float scale) => new Vector3(scale, scale, 1f);

    private Color Dim(Color color) => Color.Lerp(color, Color.black, dimAmount);

    private Color ColorFor(MainActionType action)
    {
        switch (action)
        {
            case MainActionType.Jump: return jumpColor;
            case MainActionType.Dash: return dashColor;
            case MainActionType.Attack: return attackColor;
            default: return Color.gray;
        }
    }

    private Sprite IconFor(MainActionType action)
    {
        switch (action)
        {
            case MainActionType.Jump: return jumpIcon;
            case MainActionType.Dash: return dashIcon;
            case MainActionType.Attack: return attackIcon;
            default: return null;
        }
    }

    private static string LabelFor(MainActionType action)
    {
        switch (action)
        {
            case MainActionType.Jump: return "JUMP";
            case MainActionType.Dash: return "DASH";
            case MainActionType.Attack: return "ATTACK";
            default: return "?";
        }
    }
}
