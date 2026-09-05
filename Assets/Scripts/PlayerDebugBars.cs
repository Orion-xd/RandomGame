using UnityEngine;

/// <summary>
/// デバッグ表示：プレイヤー頭上に2本のゲージを出す。
///  - コンボ受付ゲージ：1つ目のアクション発動後 comboGraceTime（0.8秒）かけて減少。
///    残っている間はコンボの追加入力を受け付ける。
///  - クールタイム残りゲージ：アクション発動後、そのクールタイムをかけて減少。
///    「これが残っている」かつ「コンボ受付ゲージが残っていない」＝アクション実行不可。
/// ゲージは左端固定で縮む。fill の localScale.x / localPosition.x のみ書き換える。
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

    private void Reset()
    {
        controller = GetComponentInParent<MainActionController>();
    }

    private void LateUpdate()
    {
        if (controller == null) return;

        float combo = controller.ComboGraceFraction01;
        float cd = controller.CooldownFraction01;

        SetFill(comboFill, combo);
        SetFill(cooldownFill, cd);

        if (comboLabel != null) comboLabel.text = "COMBO " + combo.ToString("0.00");
        if (cooldownLabel != null) cooldownLabel.text = "CD " + cd.ToString("0.00");
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
