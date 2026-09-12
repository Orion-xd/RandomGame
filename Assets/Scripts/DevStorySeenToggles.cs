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
/// 表示条件は <see cref="DeveloperSettings"/>.Active（エディタ内 かつ アセットの developerMode）。
/// エディタ外のビルドでは常に無効。トグルは実行時生成なのでシーンには何も残らない。
/// </summary>
public class DevStorySeenToggles : MonoBehaviour
{
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
    private readonly System.Collections.Generic.List<Toggle> _stageToggles =
        new System.Collections.Generic.List<Toggle>(); // StageSelect モードのときだけ埋まる

    private void Start()
    {
        if (_built) return;
        if (!DeveloperSettings.Active) { enabled = false; return; }
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

        bool horizontal = IsHorizontalRow(buttons);
        Toggle first = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            int idx = i;
            var brt = (RectTransform)buttons[i].transform;
            // クリア状況チェックの基準距離。その更に stageExtraLeftGap ぶん外側（縦並び=左 / 横並び=上）。
            float clearMain = brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f;
            Vector2 offset = horizontal
                ? new Vector2(0f, brt.sizeDelta.y * 0.5f + gap + boxSize * 0.5f + stageExtraLeftGap)
                : new Vector2(-(clearMain + stageExtraLeftGap), 0f);
            var tog = BuildToggle("DevIntroSeen_" + (idx + 1), brt, offset,
                GameFlow.HasSeenIntro(idx),
                v => GameFlow.SetIntroSeen(idx, v));
            _stageToggles.Add(tog);
            if (first == null) first = tog; // 先頭（Stage1）のトグル
        }

        // ラベルは1つだけ。縦並び → 列の一番上、横並び → 行の一番左。
        if (first != null) BuildGroupLabel((RectTransform)first.transform, "story", leftSide: horizontal);
    }

    /// <summary>ステージボタンが横一列に並んでいるか（縦の差より横の差が大きいか）。</summary>
    private static bool IsHorizontalRow(Button[] buttons)
    {
        if (buttons == null || buttons.Length < 2 || buttons[0] == null || buttons[1] == null) return false;
        Vector2 a = ((RectTransform)buttons[0].transform).anchoredPosition;
        Vector2 b = ((RectTransform)buttons[1].transform).anchoredPosition;
        return Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
    }

    private void BuildPrologueToggle()
    {
        var btn = prologueAnchorButton != null ? prologueAnchorButton : FindAnyObjectByType<Button>();
        if (btn == null) return;
        var brt = (RectTransform)btn.transform;
        float offsetX = -(brt.sizeDelta.x * 0.5f + gap + boxSize * 0.5f);
        var tog = BuildToggle("DevPrologueSeen", brt, new Vector2(offsetX, 0f),
            GameFlow.HasSeenPrologue,
            v => GameFlow.SetPrologueSeen(v));
        BuildGroupLabel((RectTransform)tog.transform, "story", leftSide: false);
    }

    private Toggle BuildToggle(string name, RectTransform anchorBtn, Vector2 offset,
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
        rt.anchoredPosition = anchorBtn.anchoredPosition + offset;

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

    /// <summary>StageSelect の既読チェックを全て外す（見た目＋GameFlow の既読フラグ）。
    /// 開発者用リセットボタンから呼ぶ。Title のプロローグ用トグルは対象外（_stageToggles が空なので何もしない）。</summary>
    public void ResetAll()
    {
        for (int i = 0; i < _stageToggles.Count; i++)
        {
            var tog = _stageToggles[i];
            if (tog == null) continue;
            tog.isOn = false; // onValueChanged 経由で GameFlow.SetIntroSeen(i, false) も走る
            if (tog.graphic != null) tog.graphic.canvasRenderer.SetAlpha(0f);
            GameFlow.SetIntroSeen(i, false); // 念のため直接も
        }
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
}
