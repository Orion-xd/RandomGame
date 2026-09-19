using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メインアクションの先読み表示（アクションバー）。一番上（Slot0）が「次に発動するアクション」。
/// 各スロットは背景 Image（常に「暗い」色）とオーバーレイ Fill Image（常に「明るい＝デフォルト」色、
/// Image.type=Filled/Vertical/Bottom で下から fillAmount 分だけ表示）を重ねて持つ。
/// fillAmount を 0〜1 で動かすことで「下側だけ明るい／全体明るい／全体暗い」を同じ仕組みで表現する。
///
/// 状態によって明るさが変わるのは Slot0（次に発動するアクション）だけ。Slot1以降は状況によらず常に fillAmount = 1（全体明るい）。
///
/// Slot0 の fillAmount（＝どれだけ明るく見えるか）:
///  - 通常時（コンボ待ちでもクールタイム中でもない）: 1（＝今まで通りの見た目）
///  - コンボ受付中（MainActionController.ComboGraceFraction01 > 0）:
///      受付時間の「残り割合」をそのまま fillAmount に（時間経過とともに下から暗くなっていく）
///  - クールタイム中（コンボ待ちではないが発動不可）:
///      クールタイムの「経過割合」を fillAmount に（時間経過とともに下から明るくなっていく）
///      ただしジャンプ由来のクールタイムは着地時刻が不定で経過割合を安定して出せないため、
///      着地するまで常に 0（＝全体暗いまま）として扱う（MainActionController.IsJumpCooldownActive）。
///
/// Slot0 の枠（nextActionBorder, 2026-09-19追加）: 「クールタイムがほぼ明けている状態」と「完全に発動可能な状態」が
/// 見た目上ほぼ同じ明るさになって区別しづらい問題への対策。発動可能（＝待機中、またはコンボ受付中）の間だけ
/// 表示し、クールタイム中（ジャンプ含む）は非表示にする。
///
/// `queue`/`controller` は Player（別 GameObject）への直接参照のため、Player を削除して作り直す
/// （Prefab 化に伴う差し替えなど）と参照が外れて中身が更新されなくなる事故が起きやすい。
/// 対策として、未設定（null）なら Tag="Player" から自動解決する。
/// </summary>
public class ActionBarUI : MonoBehaviour
{
    [Tooltip("表示対象のアクションキュー。未設定なら Tag=Player の MainActionQueue から自動取得")]
    [SerializeField] private MainActionQueue queue;
    [Tooltip("クールタイム/コンボ受付の割合取得元。未設定なら Tag=Player の MainActionController から自動取得")]
    [SerializeField] private MainActionController controller;
    [Tooltip("各スロットの背景（常に「暗い」側の色を表示する）")]
    [SerializeField] private Image[] slotBackgrounds;
    [Tooltip("各スロットのオーバーレイ（常に「明るい＝デフォルト」色。fillAmount で表示割合を制御する）")]
    [SerializeField] private Image[] slotFills;
    [SerializeField] private Text[] slotLabels;
    [Tooltip("Slot0（次のアクション）専用の枠。発動可能（待機中/コンボ受付中）の間だけ表示する")]
    [SerializeField] private Image nextActionBorder;

    [Header("アクション別の色（＝「明るい」側の色。今までのデフォルト表示と同じ）")]
    [SerializeField] private Color jumpColor = new Color(0.30f, 0.70f, 1f);
    [SerializeField] private Color dashColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private Color attackColor = new Color(1f, 0.40f, 0.40f);

    [Header("暗さの調整")]
    [Range(0f, 1f)]
    [Tooltip("「暗い」側の色を、明るい色からどれだけ黒に寄せるか（0=暗くしない＝明るい色と同じ、1=真っ黒）。" +
             "値を大きくするほど暗い部分がはっきり黒っぽくなり、明暗のコントラストが強くなる。" +
             "値を小さくするほど暗い部分が元の色に近づき、コントラストが弱く目立たなくなる")]
    [SerializeField] private float dimAmount = 0.6f;

    private void Awake()
    {
        if (queue == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) queue = p.GetComponent<MainActionQueue>();
            else Debug.LogWarning("ActionBarUI: queue が未設定で、Tag=Player のオブジェクトも見つかりません。", this);
        }
        if (controller == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) controller = p.GetComponent<MainActionController>();
        }
    }

    private void OnEnable()
    {
        if (queue != null) queue.OnChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (queue != null) queue.OnChanged -= Refresh;
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (queue == null || slotBackgrounds == null || slotLabels == null || slotFills == null) return;

        int n = Mathf.Min(Mathf.Min(slotBackgrounds.Length, slotLabels.Length), slotFills.Length);

        float comboFrac = controller != null ? controller.ComboGraceFraction01 : 0f;
        bool comboPending = comboFrac > 0f;
        bool jumpCooling = controller != null && controller.IsJumpCooldownActive;
        float cdFrac = controller != null ? controller.CooldownFraction01 : 0f; // 残り割合（1→0）
        bool cooling = !comboPending && (cdFrac > 0f || jumpCooling);

        if (nextActionBorder != null) nextActionBorder.gameObject.SetActive(comboPending || !cooling);

        for (int i = 0; i < n; i++)
        {
            MainActionType? a = queue.Peek(i);
            if (a == null)
            {
                slotBackgrounds[i].color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
                slotFills[i].fillAmount = 0f;
                slotLabels[i].text = "-";
                continue;
            }

            Color normal = ColorFor(a.Value);
            float brightFraction = 1f; // Slot1以降は常に全体明るい

            if (i == 0)
            {
                if (comboPending) brightFraction = comboFrac;                 // 残り割合ぶん、下から明るい
                else if (cooling) brightFraction = jumpCooling ? 0f : (1f - cdFrac); // 経過割合ぶん、下から明るい（ジャンプ中は常に0）
                else brightFraction = 1f;                                     // 通常時：全体明るい
            }

            slotBackgrounds[i].color = Dim(normal);
            slotFills[i].color = normal;
            slotFills[i].fillAmount = brightFraction;
            slotLabels[i].text = LabelFor(a.Value);
        }
    }

    private Color Dim(Color c) => Color.Lerp(c, Color.black, dimAmount);

    private Color ColorFor(MainActionType a)
    {
        switch (a)
        {
            case MainActionType.Jump: return jumpColor;
            case MainActionType.Dash: return dashColor;
            case MainActionType.Attack: return attackColor;
            default: return Color.gray;
        }
    }

    private static string LabelFor(MainActionType a)
    {
        switch (a)
        {
            case MainActionType.Jump: return "JUMP";
            case MainActionType.Dash: return "DASH";
            case MainActionType.Attack: return "ATTACK";
            default: return "?";
        }
    }
}
