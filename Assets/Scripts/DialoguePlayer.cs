using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 会話（DialogueSequence）の再生。スペース / エンター / 左クリックで1ページずつ送り、最後まで読むと onComplete を呼ぶ。
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
///
/// ── 文字送り（タイプライター演出） ──
/// on/off・速さともに**ページ単位**（`DialogueSequence.Page.useTypewriterEffect` / `.typewriterCharsPerSecond`、
/// 既定 true / 30）で決める（シーン一律ではなく、テキストごとに個別設定できる）。true のページは
/// 本文を先頭から1文字ずつ表示していく（「こんにちは」→「こ」→「こん」→…）。
/// 表示し終わる前に発動入力（スペース / エンター / テンキー Enter / 左クリック）があれば、
/// その入力で「残りを一気に表示（強制的に全文表示）」するだけに留め、ページはまだ送らない。
/// 全文表示済みの状態で発動入力があれば、そこで初めて次のページへ送る。
/// そのページの `useTypewriterEffect` が false なら、常に開始時点で全文表示済みの状態から
/// スタートするので、発動入力は即ページ送りになる（＝従来の一括表示）。
/// アニメーション自体は `Time.unscaledDeltaTime` 基準で進む（ステージ開始会話は `Time.timeScale=0` の中で
/// 再生されるため、`Time.deltaTime` 基準だと文字が出てこなくなる）。またシーン遷移のフェード演出中
/// （`SceneTransition.Transitioning`）は経過時間を進めず待機する（フェードで隠れている間に文字送りが
/// 終わってしまい、明転後にはもう全文表示済み、という事態を防ぐ）。
/// 話者名（speaker）は対象外で常に即時表示（台詞本文だけがタイプライター対象）。
/// </summary>
public class DialoguePlayer : MonoBehaviour
{
    /// <summary>いずれかの DialoguePlayer が会話を再生中か。</summary>
    public static bool IsPlaying { get; private set; }

    [Header("色 / サイズ（中央テキスト・送り待ちヒントなど、共有テキストボックス以外の部分）")]
    [SerializeField] private Color blackColor = new Color(0.05f, 0.05f, 0.06f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private int centerFontSize = 40;
    [SerializeField] private int hintFontSize = 18;
    [Tooltip("会話 Canvas の描画順。HUD(0) や結果画面(100) より前面に。")]
    [SerializeField] private int sortingOrder = 200;

    [Header("テキストボックス（話者アイコン・名前欄・本文）")]
    [Tooltip("箱・話者アイコン・名前欄・本文の見た目本体。チュートリアルヒントと共有の " +
             "Assets/Prefabs/UI/SpeakerTextBox.prefab を指す。位置/色/フォントサイズ等の見た目を" +
             "調整したい場合は、このフィールドではなくそのプレハブ自身を直接編集すること" +
             "（Prefab Mode で開けばゲームを実行せずに実際のレイアウトを確認できる）")]
    [SerializeField] private SpeakerTextBoxView textBoxPrefab;

    [Tooltip("会話（プロローグ / ステージ開始会話）が出てからこの秒数、送り入力を無効化する（連打で飛ばさないように）")]
    [SerializeField] private float inputLockDuration = 0.25f;

    // 文字送り（タイプライター演出）の進行状態
    private Text _typewriterTarget;    // 現在アニメーション対象の Text（_centerText か _bodyText）
    private string _typewriterFullText = "";
    private float _typewriterElapsed;  // アニメーション開始からの経過秒（Time.unscaledDeltaTime 積算）
    private bool _typewriterActive;    // まだ全文表示し終えていないか
    private float _typewriterCharsPerSecond; // そのページの速さ（DialogueSequence.Page から渡される）

    /// <summary>現在のページの本文が最後まで表示し終えているか（タイプライター無効時は常に true）。</summary>
    private bool IsFullyRevealed => !_typewriterActive;

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
    private SpeakerTextBoxView _textBox;
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

        // 文字送りアニメーションは入力ロック中も進める（見た目だけの演出なので止める必要が無い）。
        UpdateTypewriter();

        if (!InputLock.InputAllowed) return; // 出た直後は連打の勢いで飛ばさない

        var k = Keyboard.current;
        bool byKey = k != null && (k.spaceKey.wasPressedThisFrame
            || k.enterKey.wasPressedThisFrame
            || k.numpadEnterKey.wasPressedThisFrame);

        // 画面のどこをクリックしても読み進められる（UI 経由ではなくデバイスを直接読むので位置は問わない）。
        var m = Mouse.current;
        bool byClick = m != null && m.leftButton.wasPressedThisFrame;

        if (!(byKey || byClick)) return;

        if (!IsFullyRevealed)
        {
            // 表示し終える前の入力：まず残りを一気に表示するだけ（ページはまだ送らない）。
            CompleteTypewriter();
        }
        else
        {
            // 全文表示済みでの入力：次のページへ。
            Advance();
        }
    }

    /// <summary>文字送りアニメーションを1フレーム進める（Time.unscaledDeltaTime 基準）。</summary>
    private void UpdateTypewriter()
    {
        if (!_typewriterActive) return;

        // シーン遷移のフェード演出中（暗転〜読み込み〜明転）は画面が見えていないので、
        // ここで経過時間を進めない＝待機させる。プロローグ / ステージ開始会話は Start() から
        // 即座に Play() されるため、これが無いとフェードで隠れている間に文字送りが終わってしまい、
        // 明転して画面が見えたときには既に全文表示済み、という演出が台無しの状態になる。
        // 明転が完了して SceneTransition.Transitioning が false になった瞬間から文字送りが始まる。
        if (SceneTransition.Transitioning) return;

        if (_typewriterCharsPerSecond <= 0f) { CompleteTypewriter(); return; } // 0以下は即全文表示（保険）

        _typewriterElapsed += Time.unscaledDeltaTime;
        int revealCount = Mathf.FloorToInt(_typewriterElapsed * _typewriterCharsPerSecond);

        if (revealCount >= _typewriterFullText.Length) { CompleteTypewriter(); return; }
        _typewriterTarget.text = _typewriterFullText.Substring(0, revealCount);
    }

    /// <summary>本文を強制的に全文表示の状態にする。</summary>
    private void CompleteTypewriter()
    {
        if (_typewriterTarget != null) _typewriterTarget.text = _typewriterFullText;
        _typewriterActive = false;
    }

    /// <summary>対象の Text にページ本文をセットする。`useTypewriter`/`charsPerSecond`（そのページの設定）に応じて
    /// 1文字ずつ表示するか即時表示するかを切り替える。</summary>
    private void SetPageText(Text target, string fullText, bool useTypewriter, float charsPerSecond)
    {
        fullText ??= "";
        _typewriterTarget = target;
        _typewriterFullText = fullText;
        _typewriterElapsed = 0f;
        _typewriterCharsPerSecond = charsPerSecond;

        if (useTypewriter && fullText.Length > 0)
        {
            _typewriterActive = true;
            target.text = "";
        }
        else
        {
            _typewriterActive = false;
            target.text = fullText;
        }
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
        if (centered)
        {
            CenterTextHorizontally(_centerText, 0f, 0f, p.text ?? ""); // 全文表示時に中央へ来る位置へ左端を固定（文字送り中の左右ブレ防止）
            SetPageText(_centerText, p.text, p.useTypewriterEffect, p.typewriterCharsPerSecond);
        }

        // テキストボックス（箱・話者アイコン・名前欄・本文の見た目は共有ビュー SpeakerTextBoxView に委譲）。
        _textBox.Background.enabled = box;
        _textBox.Body.enabled = box;
        // ナレーション＝None のときは話者アイコン・名前欄とも出さない。表示名・アイコンは
        // SpeakerRegistry から解決する（誰がどのアイコンかはここでは決めない）。
        bool hasSpeaker = box && p.speaker != SpeakerId.None;
        var profile = hasSpeaker ? SpeakerRegistry.Get(p.speaker) : null;
        _textBox.SetSpeaker(profile);
        if (box)
        {
            LayoutBox(top);
            // 話者の有無によらず、本文の左右は常に同じだけ空けてアイコン欄ぶんのスペースを確保する
            // （ユーザー指定）。中央揃えの見た目を保ったまま文字送りしてもブレないよう、
            // CenterTextHorizontally で「全文表示時にちょうど収まる位置」へあらかじめ左端を固定してから
            // 文字送りを始める。
            float inset = _textBox.GetBodyInset();
            CenterTextHorizontally(_textBox.Body, inset, inset, p.text ?? "");
            SetPageText(_textBox.Body, p.text, p.useTypewriterEffect, p.typewriterCharsPerSecond); // 話者名は対象外（上で即時表示済み）、台詞本文だけ文字送りする
        }

        _hintText.enabled = true;
    }

    private void LayoutBox(bool top)
    {
        // 左右 8% マージン、高さ画面の約 26%。上 or 下に寄せる。
        var rt = _textBox.RectTransform;
        rt.anchorMin = new Vector2(0.08f, top ? 0.71f : 0.06f);
        rt.anchorMax = new Vector2(0.92f, top ? 0.97f : 0.32f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 中央揃え（`TextAnchor.MiddleCenter`）のまま文字送りすると、文字数が増えるたびに中央の基準が
    /// ズレて左右にブレて見える問題がある（`CenteredOnBlack` の `_centerText` で最初に発覚）。
    ///
    /// 対策：全文をあらかじめ測って必要な横幅を求め、確保領域（`leftBound`/`rightBound` を追加で
    /// 差し引いた残り）の中でその横幅ぶんだけ中央寄せした位置に「左端」を固定する
    /// （`offsetMin`/`offsetMax` で領域自体を狭める）。揃えは `MiddleLeft` に変更し、その固定された
    /// 左端から文字を生やしていく。全文表示時にちょうど中央に収まるので、見た目は変えずに
    /// 文字送り中のブレだけを無くせる。全文が確保領域より広い（改行が要る）場合は `pad=0` になり、
    /// 確保領域いっぱいを使う左揃えにフォールバックする。
    /// `leftBound`/`rightBound` は話者アイコン欄ぶんなど、中央寄せ計算の前に確保しておきたい
    /// 追加の左右マージン（px）。`_centerText`（アイコン無し）なら 0/0 を渡す。
    /// </summary>
    private static void CenterTextHorizontally(Text target, float leftBound, float rightBound, string fullText)
    {
        var rt = (RectTransform)target.transform;

        // 一旦 leftBound/rightBound だけの inset に戻し、その残り領域の横幅を測る。
        rt.offsetMin = new Vector2(leftBound, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-rightBound, rt.offsetMax.y);
        float availableWidth = rt.rect.width;

        // 全文の横幅を測る（Text.preferredWidth は改行を無視した「1行に並べた場合」の幅）。
        target.text = fullText;
        float textWidth = Mathf.Min(target.preferredWidth, availableWidth);

        float pad = Mathf.Max(0f, (availableWidth - textWidth) / 2f);
        rt.offsetMin = new Vector2(leftBound + pad, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-(rightBound + pad), rt.offsetMax.y);
        target.alignment = TextAnchor.MiddleLeft;
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

        // テキストボックス（箱・話者アイコン・名前欄・本文）：チュートリアルヒントと共有のプレハブを
        // インスタンス化するだけ。見た目そのものはこのプレハブ側が持つので、ここでは色/サイズ等を
        // 一切設定しない（統一前は DialoguePlayer 側にも同じ内容のフィールドが重複していた）。
        _textBox = Instantiate(textBoxPrefab, _root.transform);
        LayoutBox(false);
        // シーン上に直接置いたインスタンスは日本語フォント修正の対象外なので、念のためここでも
        // 明示的に設定する（プレハブ自体に既に設定済みだが、二重の安全策として）。
        if (_textBox.SpeakerName != null) _textBox.SpeakerName.font = _font;
        if (_textBox.Body != null) _textBox.Body.font = _font;

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
