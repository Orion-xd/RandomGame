using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>タイトルロゴ演出とリリース式スタートボタンを制御する。</summary>
public sealed class TitleMenu : MonoBehaviour
{
    [Header("タイトル演出")]
    [SerializeField] private RectTransform heroLogo;
    [SerializeField] private RectTransform sibariLogo;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private float heroDropDistance = 900f;
    [SerializeField] private float sibariSlideDistance = 1300f;
    [SerializeField] private float heroMoveDuration = 0.7f;
    [SerializeField] private float sibariMoveDuration = 0.65f;
    [SerializeField] private float elasticDuration = 0.34f;
    [SerializeField] private float logoInterval = 0.12f;
    [SerializeField] private float revealFadeDuration = 0.55f;

    [Header("スタートボタン")]
    [SerializeField] private Button startButton;
    [SerializeField] private Image startButtonImage;
    [SerializeField] private Sprite startNormalSprite;
    [SerializeField] private Sprite startHoverSprite;
    [Range(0.5f, 1f)] [SerializeField] private float pressedScale = 0.92f;
    [SerializeField] private float buttonScaleDuration = 0.09f;

    private RectTransform _startRect;
    private Image _whiteOverlay;
    private Vector2 _heroFinalPosition;
    private Vector2 _sibariFinalPosition;
    private Vector3 _heroFinalScale;
    private Vector3 _sibariFinalScale;
    private Vector3 _startFinalScale;
    private Coroutine _buttonScaleRoutine;
    private bool _skipRequested;
    private bool _ready;
    private bool _startPressed;
    private bool _starting;
    private float _inputUnlockTime;

    private void Awake()
    {
        if (heroLogo == null) heroLogo = transform.Find("TitleLogo") as RectTransform;
        if (sibariLogo == null) sibariLogo = transform.Find("TitleLogo (1)") as RectTransform;
        if (backgroundImage == null)
        {
            Transform item = transform.Find("Title");
            if (item != null) backgroundImage = item.GetComponent<Image>();
        }
        if (startButton == null)
        {
            Transform item = transform.Find("StartButton");
            if (item != null) startButton = item.GetComponent<Button>();
        }
        if (startButtonImage == null && startButton != null) startButtonImage = startButton.targetGraphic as Image;

        _startRect = startButton != null ? startButton.transform as RectTransform : null;
        if (startNormalSprite == null && startButtonImage != null) startNormalSprite = startButtonImage.sprite;
        if (heroLogo != null)
        {
            _heroFinalPosition = heroLogo.anchoredPosition;
            _heroFinalScale = heroLogo.localScale;
        }
        if (sibariLogo != null)
        {
            _sibariFinalPosition = sibariLogo.anchoredPosition;
            _sibariFinalScale = sibariLogo.localScale;
        }
        if (_startRect != null) _startFinalScale = _startRect.localScale;

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.transition = Selectable.Transition.None;
            startButton.interactable = false;
            startButton.enabled = false;
        }
        CreateWhiteOverlay();
    }

    private void Start()
    {
        _inputUnlockTime = Time.unscaledTime + 0.15f;
        StartCoroutine(PlayIntro());
    }

    private void Update()
    {
        if (!_ready)
        {
            if (Time.unscaledTime >= _inputUnlockTime && AnySkipButtonPressed()) _skipRequested = true;
            return;
        }
        UpdateStartButtonInput();
    }

    private IEnumerator PlayIntro()
    {
        if (backgroundImage != null) backgroundImage.enabled = true;
        if (startButton != null) startButton.gameObject.SetActive(false);
        if (heroLogo != null)
        {
            heroLogo.gameObject.SetActive(true);
            heroLogo.anchoredPosition = _heroFinalPosition + Vector2.up * heroDropDistance;
            heroLogo.localScale = _heroFinalScale;
        }
        if (sibariLogo != null)
        {
            sibariLogo.gameObject.SetActive(false);
            sibariLogo.anchoredPosition = _sibariFinalPosition + Vector2.right * sibariSlideDistance;
            sibariLogo.localScale = _sibariFinalScale;
        }

        yield return MoveLogo(heroLogo, _heroFinalPosition, heroMoveDuration);
        if (!_skipRequested) yield return ElasticLogo(heroLogo, _heroFinalScale, false);
        if (!_skipRequested) yield return WaitOrSkip(logoInterval);
        if (!_skipRequested && sibariLogo != null)
        {
            sibariLogo.gameObject.SetActive(true);
            yield return MoveLogo(sibariLogo, _sibariFinalPosition, sibariMoveDuration, true);
            if (!_skipRequested) yield return ElasticLogo(sibariLogo, _sibariFinalScale, true);
        }
        yield return RevealTitle();
    }

    private IEnumerator MoveLogo(RectTransform target, Vector2 destination, float duration, bool keepSpeed = false)
    {
        if (target == null) yield break;
        Vector2 start = target.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));
            float movement = keepSpeed ? t : 1f - Mathf.Pow(1f - t, 3f);
            target.anchoredPosition = Vector2.LerpUnclamped(start, destination, movement);
            yield return null;
        }
        target.anchoredPosition = destination;
    }

    private IEnumerator ElasticLogo(RectTransform target, Vector3 finalScale, bool horizontal)
    {
        if (target == null) yield break;
        Vector2 finalPosition = target.anchoredPosition;
        Vector3 squash = horizontal
            ? new Vector3(finalScale.x * 0.62f, finalScale.y, finalScale.z)
            : new Vector3(finalScale.x, finalScale.y * 0.62f, finalScale.z);
        Vector3 overshoot = horizontal
            ? new Vector3(finalScale.x * 1.08f, finalScale.y, finalScale.z)
            : new Vector3(finalScale.x, finalScale.y * 1.08f, finalScale.z);

        // Heroは接地面を残して下方向へ潰し、Sibariは左方向へ押し潰す。
        float squashOffset = horizontal
            ? target.rect.width * finalScale.x * (1f - 0.62f) * 0.5f
            : target.rect.height * finalScale.y * (1f - 0.62f) * 0.5f;
        float overshootOffset = horizontal
            ? target.rect.width * finalScale.x * (1.08f - 1f) * 0.5f
            : target.rect.height * finalScale.y * (1.08f - 1f) * 0.5f;
        Vector2 squashPosition = finalPosition + (horizontal ? Vector2.left : Vector2.down) * squashOffset;
        Vector2 overshootPosition = finalPosition + (horizontal ? Vector2.right : Vector2.up) * overshootOffset;

        yield return ScaleLogo(target, finalScale, squash, finalPosition, squashPosition, elasticDuration * 0.28f);
        if (_skipRequested) yield break;
        yield return ScaleLogo(target, squash, overshoot, squashPosition, overshootPosition, elasticDuration * 0.38f);
        if (_skipRequested) yield break;
        yield return ScaleLogo(target, overshoot, finalScale, overshootPosition, finalPosition, elasticDuration * 0.34f);
        target.localScale = finalScale;
        target.anchoredPosition = finalPosition;
    }

    private IEnumerator ScaleLogo(
        RectTransform target,
        Vector3 fromScale,
        Vector3 toScale,
        Vector2 fromPosition,
        Vector2 toPosition,
        float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));
            t = t * t * (3f - 2f * t);
            target.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
            target.anchoredPosition = Vector2.LerpUnclamped(fromPosition, toPosition, t);
            yield return null;
        }
    }

    private IEnumerator WaitOrSkip(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator RevealTitle()
    {
        RestoreLogo(heroLogo, _heroFinalPosition, _heroFinalScale);
        RestoreLogo(sibariLogo, _sibariFinalPosition, _sibariFinalScale);
        GameAudio.PlayBgm("Forest");
        if (_whiteOverlay != null)
        {
            _whiteOverlay.transform.SetAsLastSibling();
            SetOverlayAlpha(1f);
        }
        if (startButton != null)
        {
            startButton.gameObject.SetActive(true);
            startButton.interactable = true;
        }
        SetStartPressedVisual(false, true);

        float elapsed = 0f;
        while (elapsed < revealFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(revealFadeDuration, 0.01f));
            t = t * t * (3f - 2f * t);
            SetOverlayAlpha(1f - t);
            yield return null;
        }
        if (_whiteOverlay != null) _whiteOverlay.gameObject.SetActive(false);
        _ready = true;
    }

    private static void RestoreLogo(RectTransform logo, Vector2 position, Vector3 scale)
    {
        if (logo == null) return;
        logo.gameObject.SetActive(true);
        logo.anchoredPosition = position;
        logo.localScale = scale;
    }

    private void UpdateStartButtonInput()
    {
        bool inside = IsPointerInsideStartButton();
        if (PointerPressedThisFrame() && inside)
        {
            _startPressed = true;
            SetStartPressedVisual(true, false);
        }
        if (PointerReleasedThisFrame() && _startPressed)
        {
            _startPressed = false;
            SetStartPressedVisual(false, false);
            if (inside) StartGameOnRelease();
        }
        if (SubmitPressedThisFrame())
        {
            _startPressed = true;
            SetStartPressedVisual(true, false);
        }
        if (SubmitReleasedThisFrame() && _startPressed)
        {
            _startPressed = false;
            SetStartPressedVisual(false, false);
            StartGameOnRelease();
        }
    }

    private void SetStartPressedVisual(bool pressed, bool immediate)
    {
        if (startButtonImage != null)
            startButtonImage.sprite = pressed && startHoverSprite != null ? startHoverSprite : startNormalSprite;
        if (_startRect == null) return;
        Vector3 target = pressed ? _startFinalScale * pressedScale : _startFinalScale;
        if (_buttonScaleRoutine != null) StopCoroutine(_buttonScaleRoutine);
        if (immediate)
        {
            _startRect.localScale = target;
            _buttonScaleRoutine = null;
        }
        else _buttonScaleRoutine = StartCoroutine(AnimateButtonScale(target));
    }

    private IEnumerator AnimateButtonScale(Vector3 target)
    {
        Vector3 start = _startRect.localScale;
        float elapsed = 0f;
        while (elapsed < buttonScaleDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(buttonScaleDuration, 0.01f));
            _startRect.localScale = Vector3.LerpUnclamped(start, target, 1f - Mathf.Pow(1f - t, 3f));
            yield return null;
        }
        _startRect.localScale = target;
        _buttonScaleRoutine = null;
    }

    private void StartGameOnRelease()
    {
        if (_starting) return;
        _starting = true;
        // このボタンは独自の生入力判定（UpdateStartButtonInput）でクリックを検知しており、
        // Button.onClick を一度も発火させない。GameAudio側の「シーン内の全Buttonのonclickに
        // クリック音を自動で仕込む」仕組み（WireButtonsAfterSceneLoad）の対象外になるため、
        // ここで明示的にクリック音を鳴らす（他のボタンと同じSfx.Button）。
        GameAudio.PlaySfx(GameAudio.Sfx.Button);
        GameFlow.StartGame();
    }

    private void CreateWhiteOverlay()
    {
        var item = new GameObject("白フェード", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        item.transform.SetParent(transform, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _whiteOverlay = item.GetComponent<Image>();
        _whiteOverlay.color = Color.white;
        _whiteOverlay.raycastTarget = true;
        item.transform.SetSiblingIndex(1);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (_whiteOverlay == null) return;
        Color color = _whiteOverlay.color;
        color.a = Mathf.Clamp01(alpha);
        _whiteOverlay.color = color;
    }

    private bool IsPointerInsideStartButton()
    {
        if (_startRect == null) return false;
        Vector2 position;
        // マウスを優先する。ブラウザ（WebGLビルド）では、実際にはマウスしか使っていなくても
        // Touchscreen.current がnullでなくなることがあり、タッチを優先すると使われていない
        // （初期値のままの）座標を見てしまい、ボタン内判定が常に失敗する問題があった
        // （Unity Editor上では再現しないため、ビルドしてブラウザで再生したときだけ発生していた）。
        if (Mouse.current != null) position = Mouse.current.position.ReadValue();
        else if (Touchscreen.current != null) position = Touchscreen.current.primaryTouch.position.ReadValue();
        else return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_startRect, position, null);
    }

    private static bool PointerPressedThisFrame() =>
        (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);

    private static bool PointerReleasedThisFrame() =>
        (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
        || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame);

    private static bool SubmitPressedThisFrame() =>
        (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
        || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

    private static bool SubmitReleasedThisFrame() =>
        (Keyboard.current != null && (Keyboard.current.enterKey.wasReleasedThisFrame || Keyboard.current.spaceKey.wasReleasedThisFrame))
        || (Gamepad.current != null && Gamepad.current.buttonSouth.wasReleasedThisFrame);

    private static bool AnySkipButtonPressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame)) return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
        if (Gamepad.current == null) return false;
        return Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.buttonNorth.wasPressedThisFrame
            || Gamepad.current.buttonEast.wasPressedThisFrame || Gamepad.current.buttonWest.wasPressedThisFrame
            || Gamepad.current.startButton.wasPressedThisFrame || Gamepad.current.selectButton.wasPressedThisFrame
            || Gamepad.current.leftShoulder.wasPressedThisFrame || Gamepad.current.rightShoulder.wasPressedThisFrame;
    }
}
