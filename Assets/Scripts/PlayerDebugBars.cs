using UnityEngine;

/// <summary>
/// 【開発者用】デバッグ表示：プレイヤー頭上に2本のゲージを出す。
/// 表示条件は <see cref="DeveloperSettings"/>.Active（エディタ内 かつ developerMode）。
/// それ以外（ビルド含む）では Awake で GameObject ごと非アクティブにして何も出さない。
///  - コンボ受付ゲージ：1つ目のアクション発動後 comboGraceTime（0.8秒）かけて減少。
///    残っている間はコンボの追加入力を受け付ける。
///  - クールタイム残りゲージ：アクション発動後、そのクールタイムをかけて減少。
///    「これが残っている」かつ「コンボ受付ゲージが残っていない」＝アクション実行不可。
///    ゲージの空側（左端）に「先行入力ゾーン」を色付きで重ねて表示する（幅 = InputBufferZoneFraction01）。
///    この色付き区間にゲージの先端が入っている間は先行入力が可能で、実際に受付中は色が変わる。
/// ゲージは左端固定で縮む。fill の localScale.x / localPosition.x のみ書き換える。
/// 先行入力ゾーンのスプライトは実行時に cooldownFill の複製として自動生成する（シーン編集不要）。
/// </summary>
public class PlayerDebugBars : MonoBehaviour
{
    [SerializeField] private MainActionController controller;
    [SerializeField] private Transform comboFill;
    [SerializeField] private Transform cooldownFill;
    [SerializeField] private TextMesh comboLabel;
    [SerializeField] private TextMesh cooldownLabel;

    [Tooltip("ゲージ本体の横幅（ワールド単位）。BG の横スケールと合わせる")]
    [SerializeField] private float barWidth = 1.2f;

    [Header("先行入力ゾーン表示")]
    [Tooltip("先行入力できる区間の色（受付前）")]
    [SerializeField] private Color bufferZoneIdleColor = new Color(0.25f, 0.7f, 1f, 0.55f);
    [Tooltip("実際に先行入力を受付中の色")]
    [SerializeField] private Color bufferZoneActiveColor = new Color(0.35f, 1f, 0.45f, 0.95f);
    [Tooltip("区間が細くなりすぎないよう最低限確保する幅（ゲージ全体に対する割合）")]
    [SerializeField] private float minBufferZoneWidthFrac = 0.04f;

    private SpriteRenderer _cdBufferZone;
    private float _cdFillBaseScaleY = 1f;
    private float _cdFillBaseScaleZ = 1f;
    private Vector3 _cdFillBaseLocalPos;

    private void Reset()
    {
        controller = GetComponentInParent<MainActionController>();
    }

    private void Awake()
    {
        if (!DeveloperSettings.Active)
        {
            gameObject.SetActive(false); // 頭上ゲージ（ComboBar / CooldownBar）ごと隠す
            return;
        }

        if (cooldownFill == null) return;

        _cdFillBaseScaleY = cooldownFill.localScale.y;
        _cdFillBaseScaleZ = Mathf.Approximately(cooldownFill.localScale.z, 0f) ? 1f : cooldownFill.localScale.z;
        _cdFillBaseLocalPos = cooldownFill.localPosition;

        var src = cooldownFill.GetComponent<SpriteRenderer>();
        if (src == null) return;

        var go = new GameObject("CooldownBufferZone");
        go.layer = cooldownFill.gameObject.layer;
        go.transform.SetParent(cooldownFill.parent, false);

        _cdBufferZone = go.AddComponent<SpriteRenderer>();
        _cdBufferZone.sprite = src.sprite;
        _cdBufferZone.drawMode = src.drawMode;
        _cdBufferZone.sortingLayerID = src.sortingLayerID;
        _cdBufferZone.sortingOrder = src.sortingOrder + 1; // fill の上に描く
        _cdBufferZone.color = bufferZoneIdleColor;
        go.SetActive(false);
    }

    private void LateUpdate()
    {
        if (controller == null) return;

        float combo = controller.ComboGraceFraction01;
        float cd = controller.CooldownFraction01;

        SetFill(comboFill, combo);
        SetFill(cooldownFill, cd);

        UpdateBufferZone();

        if (comboLabel != null) comboLabel.text = "COMBO " + combo.ToString("0.00");
        if (cooldownLabel != null)
            cooldownLabel.text = "CD " + cd.ToString("0.00") + (controller.InInputBufferZone ? "  BUF" : "");
    }

    private void UpdateBufferZone()
    {
        if (_cdBufferZone == null) return;

        float f = controller.InputBufferZoneFraction01;
        if (f <= 0f)
        {
            if (_cdBufferZone.gameObject.activeSelf) _cdBufferZone.gameObject.SetActive(false);
            return;
        }

        f = Mathf.Clamp01(Mathf.Max(f, minBufferZoneWidthFrac));
        if (!_cdBufferZone.gameObject.activeSelf) _cdBufferZone.gameObject.SetActive(true);

        _cdBufferZone.transform.localScale = new Vector3(f * barWidth, _cdFillBaseScaleY, _cdFillBaseScaleZ);

        Vector3 p = _cdFillBaseLocalPos;
        p.x = -barWidth * 0.5f * (1f - f); // 左端を固定
        _cdBufferZone.transform.localPosition = p;

        _cdBufferZone.color = controller.InInputBufferZone ? bufferZoneActiveColor : bufferZoneIdleColor;
    }

    private void SetFill(Transform fill, float f)
    {
        if (fill == null) return;
        f = Mathf.Clamp01(f);

        Vector3 s = fill.localScale;
        s.x = f * barWidth;
        fill.localScale = s;

        Vector3 p = fill.localPosition;
        p.x = -barWidth * 0.5f * (1f - f); // 左端を固定して縮める
        fill.localPosition = p;
    }
}
