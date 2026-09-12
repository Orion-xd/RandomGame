using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メインアクションの先読み表示（アクションバー）。一番上が「次に発動するアクション」。
/// 各スロットは背景 Image とラベル Text を持ち、MainActionQueue.OnChanged で更新される。
///
/// `queue` は Player（別 GameObject）への直接参照のため、Player を削除して作り直す
/// （Prefab 化に伴う差し替えなど）と参照が外れて中身が更新されなくなる事故が起きやすい。
/// 対策として、未設定（null）なら Tag="Player" から自動解決する。
/// </summary>
public class ActionBarUI : MonoBehaviour
{
    [Tooltip("表示対象のアクションキュー。未設定なら Tag=Player の MainActionQueue から自動取得")]
    [SerializeField] private MainActionQueue queue;
    [SerializeField] private Image[] slotBackgrounds;
    [SerializeField] private Text[] slotLabels;

    [Header("アクション別の色")]
    [SerializeField] private Color jumpColor = new Color(0.30f, 0.70f, 1f);
    [SerializeField] private Color dashColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private Color attackColor = new Color(1f, 0.40f, 0.40f);

    private void Awake()
    {
        if (queue == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) queue = p.GetComponent<MainActionQueue>();
            else Debug.LogWarning("ActionBarUI: queue が未設定で、Tag=Player のオブジェクトも見つかりません。", this);
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

    private void Refresh()
    {
        if (queue == null || slotBackgrounds == null || slotLabels == null) return;

        int n = Mathf.Min(slotBackgrounds.Length, slotLabels.Length);
        for (int i = 0; i < n; i++)
        {
            MainActionType? a = queue.Peek(i);
            if (a == null)
            {
                slotBackgrounds[i].color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
                slotLabels[i].text = "-";
                continue;
            }

            slotBackgrounds[i].color = ColorFor(a.Value);
            slotLabels[i].text = LabelFor(a.Value);
        }
    }

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
