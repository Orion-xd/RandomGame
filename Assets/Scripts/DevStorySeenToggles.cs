using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発者用】ストーリー会話の既読フラグをチェックボックスで表示・変更する。
/// <see cref="DevStageClearToggles"/>（クリア状況）と同じ流儀の、実行時生成の Toggle。
///
///  - StageSelect の Canvas に付けたとき: 各ステージボタンの左（クリア状況チェックの更に左）に
///    「そのステージの開始会話を既読か」のチェックボックスを1つずつ。
///  - Title の Canvas に付けたとき: 「Game Start」ボタンの左に「プロローグを既読か」のチェックボックス1つ。
///    （どちらのモードかは、同じ階層に StageSelectMenu があるかどうかで自動判定する）
///
/// チェック = 既読（＝その会話は次回スキップされる）。開発者が自由に付け外しできる。
/// 会話をちゃんと読み終えたときにも自動でチェックが入る（GameFlow 側で既読フラグを立てる）。
///
/// 製品版（エディタ外ビルド）では developerMode の値に関係なく **常に無効**（下記 IsEnabled）。
/// ビルド時に false へ戻し忘れても安全。トグルは実行時生成なのでシーンには何も残らない。
/// </summary>
public class DevStorySeenToggles : MonoBehaviour
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

    [Header("StageSelect 用（未指定なら同じ階層 / シーンから取得）")]
    [SerializeField] private StageSelectMenu stageSelectMenu;
    [Tooltip("未指定なら stageSelectMenu.StageButtons を使う。index 順（Stage1, Stage2, …）")]
    [SerializeField] private Button[] stageButtons;

    [Header("Title 用（未指定なら最初に見つかった Button ＝ Game Start）")]
    [SerializeField] private Button prologueAnchorButton;

    [Header("見た目")]
    [SerializeField] private float boxSize = 44f;
    [Tooltip("ボタン左端とチェックボックスの基本間隔")]
    [SerializeField] private float gap = 28f;
    [Tooltip("StageSelect: クリア状況チェックの更に左へずらす量（既定 = boxSize + gap ぶん）")]
    [SerializeField] private float stageExtraLeftGap = 72f;
    [SerializeField] private Color boxColor = new Color(1f, 1f, 1f, 0.18f);
    [Tooltip("クリア状況チェック（緑）と区別できる色にしてある")]
    [SerializeField] private Color checkColor = new Color(0.45f, 0.72f, 1f, 1f);
    [Tooltip("列ラベル『story』の文字")]
    [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.8f);
    [Tooltip("列ラベルのフォントサイズ")]
    [SerializeField] private int labelFontSize = 24;
    [Tooltip("列ラベルとチェックボックスの隙間 (px)")]
    [SerializeField] private float labelGap = 8f;

    private bool _built;

    private void Start()
    {
        if (_built) return;
        if (!IsEnabled) { enabled = false; return; }
        _built = true;

        if (stageSelectMenu == null) stageSelectMenu = GetComponentInParent<StageSelectMenu>();
        if (stageSelectMenu == null) stageSelectMenu = FindAnyObjectByType<StageSelectMenu>();

        if (stageSelectMenu != null) BuildStageIntroToggles();
        else BuildPrologueToggle();
    }

    private void BuildStageIntroToggles()
    {
        var buttons = (stageButtons != null && stageButtons.Length > 0)
            ? stageButtons
            : stageSelectMenu.StageButtons;
        if (buttons == null || buttons.Length == 0) return;

        Toggle topToggle = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            int idx = i;
            var brt = (RectTransform)buttons[i].transform;
            // クリア状況チェックの位置: -(w/2 + gap + boxSize/2)。その更に stageExtraLeftGap 左へ。
            float clearOffsetX = -(brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f);
            float offsetX = clearOffsetX - stageExtraLeftGap;
            var tog = BuildToggle("DevIntroSeen_" + (idx + 1), brt, offsetX,
                GameFlow.HasSeenIntro(idx),
                v => GameFlow.SetIntroSeen(idx, v));
            if (topToggle == null) topToggle = tog; // 一番上（Stage1）のトグル
        }

        // 列の一番上に「story」ラベルを1つだけ（各ボックスには付けない）。
        if (topToggle != null) BuildColumnLabel((RectTransform)topToggle.transform, "story");
    }

    private void BuildPrologueToggle()
    {
        var btn = prologueAnchorButton != null ? prologueAnchorButton : FindAnyObjectByType<Button>();
        if (btn == null) return;
        var brt = (RectTransform)btn.transform;
        float offsetX = -(brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f);
        var tog = BuildToggle("DevPrologueSeen", brt, offsetX,
            GameFlow.HasSeenPrologue,
            v => GameFlow.SetPrologueSeen(v));
        BuildColumnLabel((RectTransform)tog.transform, "story");
    }

    private Toggle BuildToggle(string name, RectTransform anchorBtn, float offsetX,
        bool initialOn, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(anchorBtn.parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorBtn.anchorMin;
        rt.anchorMax = anchorBtn.anchorMax;
        rt.pivot = anchorBtn.pivot;
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(boxSize, boxSize);
        rt.anchoredPosition = anchorBtn.anchoredPosition + new Vector2(offsetX, 0f);

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
        tog.isOn = initialOn;
        // 生成直後は Toggle がチェックマークの表示状態をまだ反映していないことがあるので明示的に合わせる。
        checkImg.canvasRenderer.SetAlpha(tog.isOn ? 1f : 0f);
        tog.onValueChanged.AddListener(onChanged);
        return tog;
    }

    /// <summary>列の一番上のチェックボックスの真上に、列ラベルを1つだけ置く（x はボックス列と揃う）。</summary>
    private void BuildColumnLabel(RectTransform topBoxRt, string text)
    {
        var go = new GameObject("ColumnLabel_" + text, typeof(RectTransform));
        go.transform.SetParent(topBoxRt, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);   // ボックス上辺の中央
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);       // 下辺を基準に上へ伸ばす
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
}
