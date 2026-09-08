using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発者用】ステージ選択画面で、各ステージボタンの左隣にクリア状況のチェックボックスを出す。
///
///  - チェック = そのステージがクリア済み（GameFlow に保存される）。
///  - ステージをクリアすると次にこの画面へ来たとき自動でチェックが入っている。
///  - 開発者はチェックを自由に付け外しでき、変更は即座にステージボタンの解放状態へ反映される
///    （Stage1 と 3 だけチェック、のような非現実的な状態も許容。特に整合はとらない）。
///
/// 表示条件は <see cref="DeveloperSettings"/>.Active（エディタ内 かつ アセットの developerMode）。
/// エディタ外のビルドでは常に無効。トグルは実行時生成なのでシーンには何も残らない。
/// プレイヤーがクリア状況を書き換える経路はここだけ。
/// </summary>
public class DevStageClearToggles : MonoBehaviour
{
    [Tooltip("未指定なら同じ Canvas の StageSelectMenu から取得")]
    [SerializeField] private StageSelectMenu stageSelectMenu;
    [Tooltip("未指定なら stageSelectMenu.StageButtons を使う。index 順（Stage1, Stage2, …）")]
    [SerializeField] private Button[] stageButtons;

    [Header("見た目")]
    [SerializeField] private float boxSize = 44f;
    [Tooltip("ボタン左端とチェックボックスの間隔")]
    [SerializeField] private float gap = 28f;
    [SerializeField] private Color boxColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color checkColor = new Color(0.35f, 1f, 0.45f, 1f);
    [Tooltip("列ラベル『clear』の文字")]
    [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.8f);
    [Tooltip("列ラベルのフォントサイズ")]
    [SerializeField] private int labelFontSize = 24;
    [Tooltip("列ラベルとチェックボックスの隙間 (px)")]
    [SerializeField] private float labelGap = 8f;

    private Toggle[] _toggles;
    private bool _built;

    private void Start()
    {
        if (_built) return;
        if (!DeveloperSettings.Active) { enabled = false; return; }
        _built = true;

        if (stageSelectMenu == null) stageSelectMenu = GetComponentInParent<StageSelectMenu>();
        var buttons = (stageButtons != null && stageButtons.Length > 0)
            ? stageButtons
            : stageSelectMenu != null ? stageSelectMenu.StageButtons : null;
        if (buttons == null || buttons.Length == 0) return;

        _toggles = new Toggle[buttons.Length];
        bool horizontal = IsHorizontalRow(buttons);
        Toggle first = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            var brt = (RectTransform)buttons[i].transform;
            // 縦並びボタン → ボックスはボタンの左 / 横並びボタン → ボックスはボタンの上。
            Vector2 offset = horizontal
                ? new Vector2(0f, brt.sizeDelta.y * 0.5f + gap + boxSize * 0.5f)
                : new Vector2(-(brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f), 0f);
            _toggles[i] = BuildToggle(i, buttons[i], offset);
            if (first == null) first = _toggles[i]; // 先頭（Stage1）のトグル
        }

        // ラベルは1つだけ。縦並び → 列の一番上、横並び → 行の一番左。
        if (first != null) BuildGroupLabel((RectTransform)first.transform, "clear", leftSide: horizontal);
    }

    /// <summary>ステージボタンが横一列に並んでいるか（縦の差より横の差が大きいか）。</summary>
    private static bool IsHorizontalRow(Button[] buttons)
    {
        if (buttons == null || buttons.Length < 2 || buttons[0] == null || buttons[1] == null) return false;
        Vector2 a = ((RectTransform)buttons[0].transform).anchoredPosition;
        Vector2 b = ((RectTransform)buttons[1].transform).anchoredPosition;
        return Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
    }

    /// <summary>先頭のチェックボックスに寄せて、グループラベルを1つだけ置く（各ボックスには付けない）。</summary>
    private void BuildGroupLabel(RectTransform firstBoxRt, string text, bool leftSide)
    {
        var go = new GameObject("GroupLabel_" + text, typeof(RectTransform));
        go.transform.SetParent(firstBoxRt, false);
        var rt = (RectTransform)go.transform;
        if (leftSide)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);              // ボックス左辺の外側へ
            rt.anchoredPosition = new Vector2(-labelGap, 0f);
            rt.sizeDelta = new Vector2(120f, labelFontSize + 8f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);              // ボックス上辺の外側へ
            rt.anchoredPosition = new Vector2(0f, labelGap);
            rt.sizeDelta = new Vector2(160f, labelFontSize + 8f);
        }

        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = labelFontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = leftSide ? TextAnchor.MiddleRight : TextAnchor.LowerCenter;
        t.color = labelColor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
    }

    private Toggle BuildToggle(int index, Button stageButton, Vector2 offset)
    {
        var brt = (RectTransform)stageButton.transform;

        var go = new GameObject("DevClearToggle_" + (index + 1), typeof(RectTransform));
        go.transform.SetParent(brt.parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = brt.anchorMin;
        rt.anchorMax = brt.anchorMax;
        rt.pivot = brt.pivot;
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(boxSize, boxSize);
        rt.anchoredPosition = brt.anchoredPosition + offset;

        var bg = go.AddComponent<Image>();
        bg.color = boxColor;

        var checkGo = new GameObject("Checkmark", typeof(RectTransform));
        checkGo.transform.SetParent(go.transform, false);
        var crt = (RectTransform)checkGo.transform;
        crt.anchorMin = new Vector2(0.15f, 0.15f);
        crt.anchorMax = new Vector2(0.85f, 0.85f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        var checkImg = checkGo.AddComponent<Image>();
        checkImg.color = checkColor;

        var tog = go.AddComponent<Toggle>();
        tog.transition = Selectable.Transition.ColorTint;
        tog.targetGraphic = bg;
        tog.graphic = checkImg;
        tog.isOn = GameFlow.IsStageCleared(index);
        // 生成直後は Toggle がチェックマークの表示状態をまだ反映していないことがあるので明示的に合わせる。
        checkImg.canvasRenderer.SetAlpha(tog.isOn ? 1f : 0f);

        int captured = index;
        tog.onValueChanged.AddListener(v => OnToggleChanged(captured, v));
        return tog;
    }

    private void OnToggleChanged(int index, bool isOn)
    {
        GameFlow.SetStageCleared(index, isOn);
        if (stageSelectMenu != null) stageSelectMenu.RefreshLocks();
    }

    /// <summary>全チェックを外す（見た目＋GameFlow のクリアフラグ）。開発者用リセットボタンから呼ぶ。</summary>
    public void ResetAll()
    {
        if (_toggles == null) return;
        for (int i = 0; i < _toggles.Length; i++)
        {
            var tog = _toggles[i];
            if (tog == null) continue;
            tog.isOn = false; // onValueChanged 経由で GameFlow.SetStageCleared(i, false) も走る
            if (tog.graphic != null) tog.graphic.canvasRenderer.SetAlpha(0f);
            GameFlow.SetStageCleared(i, false); // 念のため直接も
        }
        if (stageSelectMenu != null) stageSelectMenu.RefreshLocks();
    }
}
