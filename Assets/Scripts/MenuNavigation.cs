using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// メニュー系 UI（タイトルの「ゲームスタート」、ステージ選択のボタン、結果画面パネルの
/// 「次のステージへ / リトライ / ステージ選択画面へ」）をキーボードでも操作できるようにする。
///
///  - WASD / 矢印キーでボタン間をカーソル移動（縦並びリスト想定。上・左 = 前、下・右 = 次。端でラップ）。
///  - スペース / Enter で決定（そのボタンの onClick を呼ぶ ＝ クリックと同じ）。
///  - 選択中（キーボードカーソル or マウスホバー）のボタンに色付きの枠を表示。
///    枠オブジェクトは実行時に自動生成するのでシーン側の作業は不要。
///
/// Canvas 直下に付ければそのメニュー全体、パネル（ClearPanel / FailPanel）に付ければ
/// そのパネルがアクティブな間だけ働く。対象ボタンは子から自動収集（階層順）。
/// buttonsOverride を指定すればそちらを優先。
///
/// EventSystem / InputSystemUIInputModule のナビゲーション・Submit とは二重処理にならないよう、
/// 対象ボタンの navigation を None にし、毎フレーム選択状態をクリアして、このスクリプトだけが
/// キーボード操作を担当する。マウスのクリック・ホバー表示はモジュール側のまま。
/// </summary>
public class MenuNavigation : MonoBehaviour
{
    /// <summary>メニューを開いたとき最初にカーソルを置く場所。</summary>
    private enum InitialCursor
    {
        /// <summary>先頭（＝一番上）の有効なボタン。タイトルや結果画面（「次のステージへ」「リトライ」）向け。</summary>
        FirstUsable,
        /// <summary>末尾側の有効なボタン。ステージ選択で「今挑戦できる一番先のステージ」に合わせる用。</summary>
        LastUsable,
    }

    [Tooltip("空なら子から Button を自動収集（階層順）")]
    [SerializeField] private Button[] buttonsOverride;

    [Tooltip("メニューを開いたとき最初にカーソルを置く位置")]
    [SerializeField] private InitialCursor initialCursor = InitialCursor.FirstUsable;

    [Header("選択枠")]
    [SerializeField] private Color frameColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("ボタンの周囲にはみ出す枠の太さ (px)")]
    [SerializeField] private float framePadding = 8f;

    private Button[] _buttons;
    private int _index = -1;
    private int _appliedIndex = -1;
    private RectTransform _frame;
    private Image _frameImage;
    private Vector2 _lastMousePos;
    private bool _haveMouse;

    private void Awake()
    {
        _buttons = (buttonsOverride != null && buttonsOverride.Length > 0)
            ? buttonsOverride
            : GetComponentsInChildren<Button>(true);

        foreach (var b in _buttons)
        {
            if (b == null) continue;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None; // モジュール側の自動ナビ／Submit を抑止
            b.navigation = nav;
        }

        CreateFrame();
    }

    private void OnEnable()
    {
        // 実際の初期カーソルは Update 側で決める（OnEnable の時点ではボタンの
        // interactable がまだ設定されていないことがあるため。例: StageSelectMenu.Start()）。
        _index = -1;
        _appliedIndex = -1;
        _haveMouse = false;
        RefreshFrame();
    }

    private void OnDisable()
    {
        if (_frame != null) _frame.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_buttons == null || _buttons.Length == 0) return;

        // カーソルが未設定 / 範囲外 / 選択先が無効になったら、初期位置を選び直す。
        if (_index < 0 || _index >= _buttons.Length || !IsUsable(_buttons[_index]))
            _index = PickInitial();

        // モジュールが（マウスクリック時などに）選択したオブジェクトを毎フレーム解除し、
        // Enter の二重発火（モジュールの Submit ＋ 下の手動 Submit）を防ぐ。
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

        HandleMouseHover();
        HandleKeyboardNav();
        HandleSubmit();
        RefreshFrame();
    }

    // ── 入力 ───────────────────────────────────────────────

    private void HandleMouseHover()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mp = mouse.position.ReadValue();
        if (_haveMouse && (mp - _lastMousePos).sqrMagnitude < 4f) return; // ほぼ動いていない
        _lastMousePos = mp;
        _haveMouse = true;

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

        bool prev = k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame
                 || k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame;
        bool next = k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame
                 || k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame;

        if (prev == next) return; // 両方押し / どちらも無し
        Move(next ? 1 : -1);
    }

    private void HandleSubmit()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (!(k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
            return;

        if (_index < 0 || _index >= _buttons.Length) return;
        var b = _buttons[_index];
        if (IsUsable(b)) b.onClick.Invoke();
    }

    private void Move(int dir)
    {
        int n = _buttons.Length;
        int start = _index < 0 ? (dir > 0 ? -1 : 0) : _index;
        for (int step = 1; step <= n; step++)
        {
            int i = ((start + dir * step) % n + n) % n;
            if (IsUsable(_buttons[i])) { _index = i; return; }
        }
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

        bool valid = _index >= 0 && _index < _buttons.Length && IsUsable(_buttons[_index]);
        if (!valid)
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
            _frame.pivot = new Vector2(0.5f, 0.5f); // 枠は常に中心 pivot。ボタン側の pivot はどうでもよい
            _frame.localScale = Vector3.one;
            _frame.localRotation = Quaternion.identity;
        }

        // ボタンの pivot が中心でなくても正しく囲めるよう、ボタン矩形の「中心」に枠を合わせる。
        // （pivot 位置 → 中心 への補正。中心 pivot のボタンでは補正 0 になり従来と同じ挙動。）
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
