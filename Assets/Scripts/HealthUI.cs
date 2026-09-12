using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤーの残り体力をアイコン（赤丸）の個数で表示する。画面左上の HUD。
/// あらかじめ最大体力ぶんの Image を並べておき、現在値に応じて表示/非表示を切り替える。
///
/// `playerHealth` は Player（別 GameObject）への直接参照のため、Player を削除して作り直す
/// （Prefab 化に伴う差し替えなど）と参照が外れて更新されなくなる事故が起きやすい。
/// 対策として、未設定（null）なら Tag="Player" から自動解決する。
/// </summary>
public class HealthUI : MonoBehaviour
{
    [Tooltip("表示対象の体力。未設定なら Tag=Player の PlayerHealth から自動取得")]
    [SerializeField] private PlayerHealth playerHealth;
    [Tooltip("左から順に並べた体力アイコン。要素数 >= 最大体力にしておく")]
    [SerializeField] private Image[] icons;

    private void Awake()
    {
        if (playerHealth == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerHealth = p.GetComponent<PlayerHealth>();
            else Debug.LogWarning("HealthUI: playerHealth が未設定で、Tag=Player のオブジェクトも見つかりません。", this);
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null) playerHealth.OnHealthChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (playerHealth != null) playerHealth.OnHealthChanged -= Refresh;
    }

    private void Refresh()
    {
        if (icons == null) return;
        int hp = playerHealth != null ? playerHealth.Health : 0;
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] != null) icons[i].enabled = i < hp;
        }
    }
}
