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
/// 製品版（エディタ外のビルド）では、developerMode の設定に関係なく **常に無効**（下記 IsEnabled）。
/// ビルド時に false へ戻し忘れても安全。エディタ内ではインスペクターの developerMode に従う。
/// トグルは実行時生成なのでシーンには何も残らない。プレイヤーがクリア状況を書き換える経路はここだけ。
/// </summary>
public class DevStageClearToggles : MonoBehaviour
{
    [Tooltip("エディタ内での有効/無効。エディタ外のビルドでは常に無効（この設定は無視）")]
    [SerializeField] private bool developerMode = true;

    /// <summary>実際に有効か。エディタ外では常に false（ビルドへ絶対に出さない）。</summary>
    private bool IsEnabled
    {
        get
        {
#if UNITY_EDITOR
            return developerMode;
#else
            return false;
#endif
        }
    }

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
        if (!IsEnabled) { enabled = false; return; }
        _built = true;

        if (stageSelectMenu == null) stageSelectMenu = GetComponentInParent<StageSelectMenu>();
        var buttons = (stageButtons != null && stageButtons.Length > 0)
            ? stageButtons
            : stageSelectMenu != null ? stageSelectMenu.StageButtons : null;
        if (buttons == null || buttons.Length == 0) return;

        _toggles = new Toggle[buttons.Length];
        Toggle topToggle = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            _toggles[i] = BuildToggle(i, buttons[i]);
            if (topToggle == null) topToggle = _toggles[i]; // 一番上（Stage1）のトグル
        }

        // 列の一番上に「clear」ラベルを1つだけ（各ボックスには付けない）。
        if (topToggle != null) BuildColumnLabel((RectTransform)topToggle.transform, "clear");
    }

    /// <summary>列の一番上のチェックボックスの真上に、列ラベルを1つだけ置く（x はボックス列と揃う）。</summary>
    private void BuildColumnLabel(RectTransform topBoxRt, string text)
    {
        var go = new GameObject("ColumnLabel_" + text, typeof(RectTransform));
        go.transform.SetParent(topBoxRt, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, labelGap);
        rt.sizeDelta = new Vector2(160f, labelFontSize + 8f);

        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = labelFontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.LowerCenter;
        t.color = labelColor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
    }

    private Toggle BuildToggle(int index, Button stageButton)
    {
        var brt = (RectTransform)stageButton.transform;
        float offsetX = -(brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f);

        var go = new GameObject("DevClearToggle_" + (index + 1), typeof(RectTransform));
        go.transform.SetParent(brt.parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = brt.anchorMin;
        rt.anchorMax = brt.anchorMax;
        rt.pivot = brt.pivot;
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(boxSize, boxSize);
        rt.anchoredPosition = brt.anchoredPosition + new Vector2(offsetX, 0f);

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
}
