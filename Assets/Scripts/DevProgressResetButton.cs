using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発者用】ステージ選択画面に「ストーリー既読とステージクリア状況を全部リセット」ボタンを出す。
///
///  - 押すと <see cref="GameFlow.ResetStageProgress"/>：全ステージのクリアフラグと
///    ステージ開始会話の既読フラグを false にする。**タイトルのプロローグ既読は消えない**。
///  - 画面上のチェックボックス（<see cref="DevStageClearToggles"/> / <see cref="DevStorySeenToggles"/>）の
///    見た目も即座に全部外れる（対応する GameFlow 変数も false）。
///  - 「タイトルに戻る」ボタン（BackButton）の少し上に実行時生成。生成後 MenuNavigation にも登録する
///    （キーボードカーソルの移動対象になる）。
///  - 表示条件は <see cref="DeveloperSettings"/>.Active（エディタ内 かつ アセットの developerMode）。ビルドでは出ない。
/// </summary>
public class DevProgressResetButton : MonoBehaviour
{
    [Tooltip("未指定なら同じ Canvas の子 \"BackButton\" を使う")]
    [SerializeField] private RectTransform backButton;
    [Tooltip("未指定なら同じ階層 / シーンから取得")]
    [SerializeField] private StageSelectMenu stageSelectMenu;
    [Tooltip("未指定なら同じ階層 / シーンから取得。生成したボタンをカーソル対象に登録する")]
    [SerializeField] private MenuNavigation menuNavigation;

    [Header("見た目")]
    [SerializeField] private Vector2 size = new Vector2(300f, 60f);
    [SerializeField] private float gapAboveBackButton = 16f;
    [SerializeField] private Color buttonColor = new Color(0.5f, 0.28f, 0.28f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private int fontSize = 22;
    [SerializeField] private string label = "Reset story & clear";

    private bool _built;

    private void Start()
    {
        if (_built) return;
        if (!DeveloperSettings.Active) { enabled = false; return; }
        _built = true;

        if (stageSelectMenu == null) stageSelectMenu = GetComponentInParent<StageSelectMenu>();
        if (stageSelectMenu == null) stageSelectMenu = FindAnyObjectByType<StageSelectMenu>();
        if (menuNavigation == null) menuNavigation = GetComponentInParent<MenuNavigation>();
        if (menuNavigation == null) menuNavigation = FindAnyObjectByType<MenuNavigation>();
        if (backButton == null) backButton = transform.Find("BackButton") as RectTransform;
        if (backButton == null)
        {
            Debug.LogWarning("DevProgressResetButton: 基準にする BackButton が見つかりません。");
            return;
        }

        BuildButton();
    }

    private void BuildButton()
    {
        var go = new GameObject("DevResetProgressButton", typeof(RectTransform));
        go.transform.SetParent(backButton.parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = backButton.anchorMin;
        rt.anchorMax = backButton.anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = size;

        // BackButton の上端 + gap + このボタンの高さ半分（どちらも中心 pivot 前提の一般式）
        float topOfBack = backButton.anchoredPosition.y + backButton.sizeDelta.y * (1f - backButton.pivot.y);
        float centerX = backButton.anchoredPosition.x + backButton.sizeDelta.x * (0.5f - backButton.pivot.x);
        rt.anchoredPosition = new Vector2(centerX, topOfBack + gapAboveBackButton + size.y * 0.5f);

        var img = go.AddComponent<Image>();
        img.color = buttonColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav; // モジュールの自動ナビは切る
        btn.onClick.AddListener(OnResetClicked);

        // MenuNavigation のカーソル移動対象に加える（Awake でのボタン収集後に生成されるため）。
        if (menuNavigation != null) menuNavigation.AddButton(btn);

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 4f);
        trt.offsetMax = new Vector2(-8f, -4f);
        var txt = textGo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = textColor;
        txt.raycastTarget = false;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.text = label;
    }

    private void OnResetClicked()
    {
        // クリア状況 ＋ ステージ開始会話の既読を消す（プロローグ既読は残す）。
        GameFlow.ResetStageProgress();

        // 画面上のチェックボックスの見た目も揃える。
        foreach (var c in FindObjectsByType<DevStageClearToggles>(FindObjectsSortMode.None)) c.ResetAll();
        foreach (var c in FindObjectsByType<DevStorySeenToggles>(FindObjectsSortMode.None)) c.ResetAll();

        if (stageSelectMenu != null) stageSelectMenu.RefreshLocks();
    }
}
