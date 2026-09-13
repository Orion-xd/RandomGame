using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// メニュー系 UI（タイトルの「ゲームスタート」、ステージ選択のボタン群、結果画面パネルの
/// 「次のステージへ / リトライ / ステージ選択画面へ」）をキーボードでも操作できるようにする。
///
///  - カーソル移動は画面上の位置ベース（2D）:
///      W / ↑ … 上のボタン    S / ↓ … 下のボタン    A / ← … 左のボタン    D / → … 右のボタン
///    横並びのボタン列（ステージ 1〜5）は左右で移動し、端は反対の端へラップする。
///    縦方向も同様に、上端／下端でラップする（ステージ行 ⇔ リセット/タイトルに戻る）。
///    横入力は「同じ行」の中でのみ移動する（行が違えば動かない）。
///  - スペース / Enter で決定（そのボタンの onClick を呼ぶ ＝ クリックと同じ）。
///  - 選択中（キーボードカーソル or マウスホバー）のボタンに色付きの枠を表示（実行時生成）。
///  - マウスホバーは「マウスを実際に動かしたとき」だけカーソルに反映する。シーン遷移直後や
///    パネル表示直後にマウスが据え置かれているだけでは反映しない（意図しないカーソル移動を防ぐ）。
///  - 画面 / パネルが出たら <see cref="InputLock"/>.LockFor(inputLockDuration) を呼び、その間は決定
///    （Space/Enter/テンキー Enter・マウスクリック）だけ無視する（前の画面での連打の勢いで「次のステージへ」
///    等を誤選択しないように）。カーソル移動（WASD/矢印キー・マウスホバー）はこの猶予タイマーの対象外で、
///    猶予中でも常に反映される（2026-09-12。「下へ移動したい」という意図した操作までブロックしていた
///    問題の修正）。さらに、カーソル移動が実際に成立した（＝そのパネルで意味のある操作だった）瞬間に
///    <see cref="InputLock.Unlock"/> を呼び、決定のロックも即座に解除する（2026-09-12。キーで操作し
///    始めた時点で「連打の勢い」ではなく「意図した操作」と判断できるため）。
///    ただし <see cref="SceneTransition"/> のフェード演出中（<see cref="InputLock.NavigationAllowed"/> が
///    false の間）はカーソル移動・マウスホバーも含めて完全にブロックする（2026-09-12。フェード中はまだ
///    画面が見えていない・遷移中であり、猶予タイマーの対象外＝常時受け付け、とは別の話）。
///
/// EventSystem / InputSystemUIInputModule のナビゲーション・Submit と二重処理にならないよう、
/// 対象ボタンの navigation を None にし、毎フレーム選択状態をクリアする。マウスのクリック・ホバー着色は
/// モジュール側のまま。対象ボタンは子から自動収集（buttonsOverride を指定すればそちら優先）。
/// 実行時に足されるボタン（開発者用リセットボタン等）は <see cref="AddButton"/> で登録する。
/// </summary>
public class MenuNavigation : MonoBehaviour
{
    /// <summary>SetInitialFocus() の指定が無いときの初期カーソル位置。</summary>
    private enum InitialCursor { FirstUsable, LastUsable }

    [Tooltip("空なら子から Button を自動収集（階層順）")]
    [SerializeField] private Button[] buttonsOverride;

    [Tooltip("初期カーソル位置。SetInitialFocus() で明示指定があればそちらが優先")]
    [SerializeField] private InitialCursor initialCursor = InitialCursor.FirstUsable;

    [Header("選択枠")]
    [SerializeField] private Color frameColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("ボタンの周囲にはみ出す枠の太さ (px)")]
    [SerializeField] private float framePadding = 8f;

    [Tooltip("横入力で『同じ行』とみなす縦方向のズレ許容 (px)")]
    [SerializeField] private float rowTolerance = 40f;

    [Tooltip("この画面 / パネルが出てからこの秒数、入力を無効化する（連打の勢いでの誤操作防止）")]
    [SerializeField] private float inputLockDuration = 0.5f;

    private Button[] _buttons;
    private int _index = -1;
    private int _appliedIndex = -1;
    private RectTransform _frame;
    private Image _frameImage;
    private Vector2 _lastMousePos;
    private bool _mousePosCaptured;
    private Button _initialFocus; // 場面ごとに外から指定する初期カーソル（優先）
    private GraphicRaycaster _raycaster; // 入力ロック中はマウスの UI クリックも止める

    private void Awake()
    {
        _buttons = (buttonsOverride != null && buttonsOverride.Length > 0)
            ? buttonsOverride
            : GetComponentsInChildren<Button>(true);
        foreach (var b in _buttons) DisableModuleNav(b);

        _raycaster = GetComponentInParent<GraphicRaycaster>();
        CreateFrame();
    }

    private static void DisableModuleNav(Button b)
    {
        if (b == null) return;
        var nav = b.navigation;
        nav.mode = Navigation.Mode.None; // モジュール側の自動ナビ／Submit を抑止
        b.navigation = nav;
    }

    /// <summary>実行時に生成されたボタン（例: 開発者用リセットボタン）をカーソル対象に加える。</summary>
    public void AddButton(Button b)
    {
        if (b == null || _buttons == null) return;
        if (System.Array.IndexOf(_buttons, b) >= 0) return;
        var list = new System.Collections.Generic.List<Button>(_buttons) { b };
        _buttons = list.ToArray();
        DisableModuleNav(b);
    }

    /// <summary>この場面で最初にカーソルを合わせたいボタンを指定する（initialCursor より優先）。
    /// StageSelectMenu が「今挑戦できる一番先のステージ」を渡す。</summary>
    public void SetInitialFocus(Button b) => _initialFocus = b;

    private void OnEnable()
    {
        _index = -1;
        _appliedIndex = -1;
        InputLock.LockFor(inputLockDuration); // この画面/パネルが出た直後、しばらく入力を無効化
        // マウス位置を「据え置き」として覚えるだけ。動かすまではホバー反映しない。
        var m = Mouse.current;
        if (m != null) { _lastMousePos = m.position.ReadValue(); _mousePosCaptured = true; }
        else _mousePosCaptured = false;
        RefreshFrame();
    }

    private void OnDisable()
    {
        if (_frame != null) _frame.gameObject.SetActive(false);
        if (_raycaster != null) _raycaster.enabled = true; // 復帰
    }

    private void Update()
    {
        if (_buttons == null || _buttons.Length == 0) return;

        // カーソルが未設定 / 範囲外 / 選択先が無効になったら、初期位置を選び直す。
        if (!HasValidIndex()) _index = PickInitial();

        // モジュールが選択したオブジェクトを毎フレーム解除し、Enter の二重発火を防ぐ。
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

        // 入力ロック中はマウスの UI クリックも止める（結果パネルの 1 秒など）。カーソル移動（キーボード /
        // マウスホバー）は「決定」ではないので決定用の猶予タイマーの対象外（2026-09-12。以前はロック中
        // カーソル移動もできず、「下矢印キーで移動したい」という意図した操作までブロックしていた）。
        // ただし SceneTransition のフェード演出中（NavigationAllowed が false）はカーソル移動も含めて
        // 完全にブロックする（2026-09-12。フェード中は画面がまだ見えていない・遷移中なので、決定以外の
        // 入力も一律止める。猶予タイマーだけを対象外にしたい、フェードそのものは対象外にしない）。
        if (_raycaster != null) _raycaster.enabled = InputLock.InputAllowed;

        if (InputLock.NavigationAllowed)
        {
            HandleMouseHover(); // マウスホバー（動かしたときのみ反映）
            HandleKeyboardNav(); // カーソル移動は決定の猶予中でも受け付ける（フェード中は除く）
        }

        if (InputLock.InputAllowed) HandleSubmit(); // 決定（Space/Enter/テンキー Enter）だけロック対象

        RefreshFrame(); // カーソル枠の表示はロック中も更新する
    }

    // ── 入力 ───────────────────────────────────────────────

    private void HandleMouseHover()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mp = mouse.position.ReadValue();
        if (!_mousePosCaptured) { _lastMousePos = mp; _mousePosCaptured = true; return; }
        if ((mp - _lastMousePos).sqrMagnitude < 4f) return; // 動いていない（据え置きも含む）
        _lastMousePos = mp;

        for (int i = 0; i < _buttons.Length; i++)
        {
            if (!IsUsable(_buttons[i])) continue;
            var rt = (RectTransform)_buttons[i].transform;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, mp, CanvasCamera(rt)))
            {
                _index = i;
                return;
            }
        }
    }

    private void HandleKeyboardNav()
    {
        var k = Keyboard.current;
        if (k == null) return;

        int before = _index;
        if (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame) MoveVertical(+1);
        else if (k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame) MoveVertical(-1);
        else if (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame) MoveHorizontal(-1);
        else if (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame) MoveHorizontal(+1);

        // カーソルが実際に動いた＝このパネルで意味のある操作だった（例: 結果パネルでは上下だけが該当し、
        // 何もしない左右は該当しない）。連打の勢いではなく意図した操作だと判断できるので、決定の
        // ロックも合わせて解除する（2026-09-12）。
        if (_index != before) InputLock.Unlock();
    }

    private void HandleSubmit()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (!(k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
            return;
        if (!HasValidIndex()) return;
        _buttons[_index].onClick.Invoke();
    }

    // ── カーソル移動（画面位置ベース） ─────────────────────

    /// <summary>左右移動。sign: +1 = 右, -1 = 左。同じ行の中で移動し、端は反対端へラップ。</summary>
    private void MoveHorizontal(int sign)
    {
        if (!HasValidIndex()) { _index = PickInitial(); return; }
        Vector2 c = ButtonCenter(_index);

        int best = -1;
        float bestX = 0f;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == _index || !IsUsable(_buttons[i])) continue;
            Vector2 p = ButtonCenter(i);
            if (Mathf.Abs(p.y - c.y) > rowTolerance) continue;   // 同じ行だけ
            float dx = p.x - c.x;
            if (sign > 0 ? dx <= 1f : dx >= -1f) continue;        // 進行方向だけ
            if (best < 0 || (sign > 0 ? p.x < bestX : p.x > bestX)) { bestX = p.x; best = i; }
        }
        if (best >= 0) { _index = best; return; }

        // 進行方向に候補なし → 同じ行の反対端へラップ
        int wrap = -1;
        float ext = 0f;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == _index || !IsUsable(_buttons[i])) continue;
            Vector2 p = ButtonCenter(i);
            if (Mathf.Abs(p.y - c.y) > rowTolerance) continue;
            if (wrap < 0 || (sign > 0 ? p.x < ext : p.x > ext)) { ext = p.x; wrap = i; }
        }
        if (wrap >= 0) _index = wrap;
    }

    /// <summary>上下移動。sign: +1 = 上, -1 = 下。上/下にある中で最も近いボタンへ。
    /// 進行方向に候補が無ければ反対の端の行へラップ（左右のラップと同じ挙動）。</summary>
    private void MoveVertical(int sign)
    {
        if (!HasValidIndex()) { _index = PickInitial(); return; }
        Vector2 c = ButtonCenter(_index);

        // 1) 進行方向にある最近傍
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == _index || !IsUsable(_buttons[i])) continue;
            Vector2 p = ButtonCenter(i);
            float dy = p.y - c.y;
            if (sign > 0 ? dy <= 1f : dy >= -1f) continue;        // 上/下だけ
            float d2 = (p - c).sqrMagnitude;
            if (d2 < bestDist) { bestDist = d2; best = i; }
        }
        if (best >= 0) { _index = best; return; }

        // 2) 進行方向に候補なし → 反対の端の行へラップ（上入力→一番下の行 / 下入力→一番上の行）。
        //    その行の中では x が現在に一番近いボタンを選ぶ。
        float extremeY = sign > 0 ? float.MaxValue : float.MinValue;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == _index || !IsUsable(_buttons[i])) continue;
            float y = ButtonCenter(i).y;
            if (sign > 0 ? y < extremeY : y > extremeY) extremeY = y;
        }
        int wrap = -1;
        float bestDx = float.MaxValue;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == _index || !IsUsable(_buttons[i])) continue;
            Vector2 p = ButtonCenter(i);
            if (Mathf.Abs(p.y - extremeY) > rowTolerance) continue; // その端の行だけ
            float dx = Mathf.Abs(p.x - c.x);
            if (dx < bestDx) { bestDx = dx; wrap = i; }
        }
        if (wrap >= 0) _index = wrap;
    }

    private bool HasValidIndex() =>
        _buttons != null && _index >= 0 && _index < _buttons.Length && IsUsable(_buttons[_index]);

    private Vector2 ButtonCenter(int i)
    {
        Vector3 wp = _buttons[i].transform.position; // ScreenSpaceOverlay では画面ピクセル
        return new Vector2(wp.x, wp.y);
    }

    // ── 選択枠 ─────────────────────────────────────────────

    private void CreateFrame()
    {
        var go = new GameObject("SelectionFrame", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _frame = (RectTransform)go.transform;
        _frameImage = go.AddComponent<Image>();
        _frameImage.color = frameColor;
        _frameImage.raycastTarget = false;
        go.SetActive(false);
    }

    private void RefreshFrame()
    {
        if (_frame == null) return;

        if (!HasValidIndex())
        {
            if (_frame.gameObject.activeSelf) _frame.gameObject.SetActive(false);
            _appliedIndex = -1;
            return;
        }

        var brt = (RectTransform)_buttons[_index].transform;
        if (!_frame.gameObject.activeSelf) _frame.gameObject.SetActive(true);

        if (_appliedIndex != _index)
        {
            _appliedIndex = _index;
            _frameImage.color = frameColor;
            _frame.SetParent(brt.parent, false); // ボタンと同じ座標空間へ
            _frame.SetAsFirstSibling();          // 全ボタンより後ろに描画 → はみ出した分が枠に見える
            _frame.anchorMin = brt.anchorMin;
            _frame.anchorMax = brt.anchorMax;
            _frame.pivot = new Vector2(0.5f, 0.5f); // 枠は常に中心 pivot
            _frame.localScale = Vector3.one;
            _frame.localRotation = Quaternion.identity;
        }

        // ボタンの pivot が中心でなくても正しく囲めるよう、ボタン矩形の「中心」に枠を合わせる。
        Vector2 centerOffset = new Vector2(
            brt.sizeDelta.x * (0.5f - brt.pivot.x),
            brt.sizeDelta.y * (0.5f - brt.pivot.y));
        _frame.anchoredPosition = brt.anchoredPosition + centerOffset;
        _frame.sizeDelta = brt.sizeDelta + new Vector2(framePadding * 2f, framePadding * 2f);
    }

    // ── ヘルパー ───────────────────────────────────────────

    private int PickInitial()
    {
        int n = _buttons?.Length ?? 0;
        if (n == 0) return -1;

        if (_initialFocus != null)
        {
            int fi = System.Array.IndexOf(_buttons, _initialFocus);
            if (fi >= 0 && IsUsable(_buttons[fi])) return fi;
        }

        if (initialCursor == InitialCursor.LastUsable)
        {
            for (int i = n - 1; i >= 0; i--)
                if (IsUsable(_buttons[i])) return i;
        }
        for (int i = 0; i < n; i++)
            if (IsUsable(_buttons[i])) return i;
        return -1;
    }

    private static bool IsUsable(Button b) => b != null && b.isActiveAndEnabled && b.interactable;

    private static Camera CanvasCamera(RectTransform rt)
    {
        var c = rt.GetComponentInParent<Canvas>();
        if (c == null) return null;
        c = c.rootCanvas;
        return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
    }
}
