using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 会話（DialogueSequence）の再生。スペース / エンターで1ページずつ送り、最後まで読むと onComplete を呼ぶ。
///
/// UI は実行時に自分で生成する（このプロジェクトの PlayerDebugBars / MenuNavigation / DevStageClearToggles と同じ流儀）。
/// シーン側は「空の GameObject にこのコンポーネントを付ける」だけでよい。Canvas も内部で作る。
///
/// 再生中は静的プロパティ IsPlaying が true。PlayerController / MainActionController はこれを見て
/// 入力を止める。ステージ会話では呼び出し側（StageManager）が Time.timeScale = 0 にして敵なども止める。
///
///  - CenteredOnBlack : 暗転＋中央テキスト（プロローグ前半）
///  - BottomTextbox   : 暗転＋一枚絵＋下部テキストボックス（プロローグ後半）。絵未指定なら仮イラスト。
///  - TopTextbox      : 暗転なし＋上部テキストボックス（ステージ開始時。ゲーム画面が見える）
/// </summary>
public class DialoguePlayer : MonoBehaviour
{
    /// <summary>いずれかの DialoguePlayer が会話を再生中か。</summary>
    public static bool IsPlaying { get; private set; }

    [Header("色 / サイズ")]
    [SerializeField] private Color blackColor = new Color(0.05f, 0.05f, 0.06f, 1f);
    [SerializeField] private Color boxColor = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color speakerColor = new Color(1f, 0.92f, 0.55f, 1f);
    [SerializeField] private int centerFontSize = 40;
    [SerializeField] private int bodyFontSize = 30;
    [SerializeField] private int speakerFontSize = 26;
    [SerializeField] private int hintFontSize = 18;
    [Tooltip("会話 Canvas の描画順。HUD(0) や結果画面(100) より前面に。")]
    [SerializeField] private int sortingOrder = 200;

    [Tooltip("会話（プロローグ / ステージ開始会話）が出てからこの秒数、送り入力を無効化する（連打で飛ばさないように）")]
    [SerializeField] private float inputLockDuration = 0.25f;

    private DialogueSequence _seq;
    private int _page;
    private Action _onComplete;
    private Sprite _lastImage;
    private Font _font;

    // 生成した UI
    private GameObject _root;
    private Image _bg;
    private Image _illust;
    private GameObject _placeholder;
    private Text _centerText;
    private RectTransform _boxRt;
    private Image _boxBg;
    private Text _speakerText;
    private Text _bodyText;
    private Text _hintText;

    private void Awake()
    {
        // 会話本文は日本語。ビルトインフォントは日本語グリフを持たず、エディタ/スタンドアロンでは
        // OS フォントへのフォールバックでたまたま表示できているだけ（WebGL では OS フォントに
        // アクセスできないため表示できない）。日本語グリフを内包した Noto Sans JP を明示的に使う。
        _font = Resources.Load<Font>("Fonts/NotoSansJP-Regular")
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
        _root.SetActive(false);
    }

    private void OnDisable()
    {
        // 会話中にシーンが変わった場合などに再生フラグを残さない
        if (IsPlaying) IsPlaying = false;
    }

    public bool HasContent(DialogueSequence seq) => seq != null && seq.Count > 0;

    /// <summary>会話を再生する。内容が無ければ即 onComplete。</summary>
    public void Play(DialogueSequence seq, Action onComplete)
    {
        _seq = seq;
        _onComplete = onComplete;
        _page = 0;
        _lastImage = null;

        if (!HasContent(seq))
        {
            Dispatch();
            return;
        }

        IsPlaying = true;
        InputLock.LockFor(inputLockDuration); // 会話が出た直後、しばらく送り入力を無効化
        _root.SetActive(true);
        Render();
    }

    private void Update()
    {
        if (!IsPlaying) return;
        if (!InputLock.InputAllowed) return; // 出た直後は連打の勢いで飛ばさない

        var k = Keyboard.current;
        bool byKey = k != null && (k.spaceKey.wasPressedThisFrame
            || k.enterKey.wasPressedThisFrame
            || k.numpadEnterKey.wasPressedThisFrame);

        // 画面のどこをクリックしても読み進められる（UI 経由ではなくデバイスを直接読むので位置は問わない）。
        var m = Mouse.current;
        bool byClick = m != null && m.leftButton.wasPressedThisFrame;

        if (byKey || byClick) Advance();
    }

    private void Advance()
    {
        _page++;
        if (_seq == null || _page >= _seq.Count)
        {
            Finish();
            return;
        }
        Render();
    }

    private void Finish()
    {
        IsPlaying = false;
        _root.SetActive(false);
        Dispatch();
    }

    private void Dispatch()
    {
        var cb = _onComplete;
        _onComplete = null;
        _seq = null;
        cb?.Invoke();
    }

    // ── 描画 ──────────────────────────────────────────────

    private void Render()
    {
        var p = _seq.PageAt(_page);
        if (p == null) { Finish(); return; }

        bool centered = p.layout == DialogueSequence.Layout.CenteredOnBlack;
        bool top = p.layout == DialogueSequence.Layout.TopTextbox;
        bool box = !centered;

        // 直前に指定された絵を継続（プロローグ後半で毎ページ貼り直さなくてよい）。
        if (p.image != null) _lastImage = p.image;
        Sprite img = centered ? null : _lastImage;

        // 背景暗転（上部テキストボックスのときはゲーム画面を見せるので暗転しない）。
        _bg.enabled = !top;
        _bg.color = blackColor;

        // 一枚絵 / 仮イラスト（BottomTextbox で絵が未指定のうちは仮を出す）。
        bool showRealImage = img != null && box;
        bool showPlaceholder = box && !top && img == null; // = BottomTextbox かつ絵未指定
        _illust.enabled = showRealImage;
        if (showRealImage) _illust.sprite = img;
        _placeholder.SetActive(showPlaceholder);

        // 中央テキスト（プロローグ前半）。
        _centerText.enabled = centered;
        if (centered) _centerText.text = p.text ?? "";

        // テキストボックス。
        _boxBg.enabled = box;
        _bodyText.enabled = box;
        bool hasSpeaker = box && !string.IsNullOrEmpty(p.speaker);
        _speakerText.enabled = hasSpeaker;
        if (box)
        {
            _bodyText.text = p.text ?? "";
            if (hasSpeaker) _speakerText.text = p.speaker;
            LayoutBox(top);
        }

        _hintText.enabled = true;
    }

    private void LayoutBox(bool top)
    {
        // 左右 8% マージン、高さ画面の約 26%。上 or 下に寄せる。
        _boxRt.anchorMin = new Vector2(0.08f, top ? 0.71f : 0.06f);
        _boxRt.anchorMax = new Vector2(0.92f, top ? 0.97f : 0.32f);
        _boxRt.offsetMin = Vector2.zero;
        _boxRt.offsetMax = Vector2.zero;
    }

    // ── UI 構築（実行時生成） ─────────────────────────────

    private void BuildUI()
    {
        var canvasGo = new GameObject("DialogueCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _root = NewRect("DialogueRoot", canvasGo.transform);
        Stretch((RectTransform)_root.transform);

        _bg = NewImage("Background", _root.transform);
        Stretch((RectTransform)_bg.transform);
        _bg.color = blackColor;

        _illust = NewImage("Illustration", _root.transform);
        FillRect((RectTransform)_illust.transform, 0.12f, 0.30f, 0.88f, 0.96f);
        _illust.preserveAspect = true;
        _illust.color = Color.white;

        _placeholder = BuildPlaceholder(_root.transform);

        _centerText = NewText("CenterText", _root.transform, centerFontSize, TextAnchor.MiddleCenter);
        FillRect((RectTransform)_centerText.transform, 0.12f, 0.20f, 0.88f, 0.80f);

        // テキストボックス
        var boxGo = NewRect("Textbox", _root.transform);
        _boxRt = (RectTransform)boxGo.transform;
        LayoutBox(false);
        _boxBg = boxGo.AddComponent<Image>();
        _boxBg.color = boxColor;
        _boxBg.raycastTarget = false;

        _speakerText = NewText("Speaker", boxGo.transform, speakerFontSize, TextAnchor.UpperLeft);
        _speakerText.color = speakerColor;
        _speakerText.fontStyle = FontStyle.Bold;
        var srt = (RectTransform)_speakerText.transform;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0f, 1f);
        srt.anchoredPosition = new Vector2(30f, -14f);
        srt.sizeDelta = new Vector2(-60f, 40f);

        _bodyText = NewText("Body", boxGo.transform, bodyFontSize, TextAnchor.UpperLeft);
        var brt = (RectTransform)_bodyText.transform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(30f, 22f);
        brt.offsetMax = new Vector2(-30f, -60f); // 上に話者名ぶんの余白

        _hintText = NewText("Hint", _root.transform, hintFontSize, TextAnchor.LowerRight);
        _hintText.text = "Click / Space / Enter ▶";
        _hintText.color = new Color(1f, 1f, 1f, 0.65f);
        FillRect((RectTransform)_hintText.transform, 0.5f, 0.02f, 0.965f, 0.06f);
    }

    private GameObject BuildPlaceholder(Transform parent)
    {
        var go = NewRect("IllustrationPlaceholder", parent);
        FillRect((RectTransform)go.transform, 0.12f, 0.30f, 0.88f, 0.96f);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.16f, 0.17f, 0.2f, 1f);

        var caption = NewText("Caption", go.transform, 20, TextAnchor.UpperCenter);
        caption.text = "(仮イラスト)"; // (仮イラスト)
        caption.color = new Color(1f, 1f, 1f, 0.5f);
        FillRect((RectTransform)caption.transform, 0.0f, 0.9f, 1.0f, 1.0f);

        var heroBox = NewImage("HeroBox", go.transform);
        heroBox.color = new Color(1f, 1f, 1f, 0.10f);
        FillRect((RectTransform)heroBox.transform, 0.06f, 0.12f, 0.34f, 0.5f);
        var hero = NewText("HeroLabel", heroBox.transform, 28, TextAnchor.MiddleCenter);
        hero.text = "主人公"; // 主人公
        Stretch((RectTransform)hero.transform);

        var dgBox = NewImage("DungeonBox", go.transform);
        dgBox.color = new Color(1f, 1f, 1f, 0.10f);
        FillRect((RectTransform)dgBox.transform, 0.6f, 0.3f, 0.94f, 0.72f);
        var dungeon = NewText("DungeonLabel", dgBox.transform, 28, TextAnchor.MiddleCenter);
        dungeon.text = "ダンジョン"; // ダンジョン
        Stretch((RectTransform)dungeon.transform);

        go.SetActive(false);
        return go;
    }

    // ── 小道具 ──────────────────────────────────────────

    private static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private Image NewImage(string name, Transform parent)
    {
        var go = NewRect(name, parent);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(string name, Transform parent, int size, TextAnchor align)
    {
        var go = NewRect(name, parent);
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = align;
        t.color = textColor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>正規化アンカー (minX,minY)-(maxX,maxY) にぴったり合わせる。</summary>
    private static void FillRect(RectTransform rt, float minX, float minY, float maxX, float maxY)
    {
        rt.anchorMin = new Vector2(minX, minY);
        rt.anchorMax = new Vector2(maxX, maxY);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
