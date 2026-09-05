using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤーの残り体力をアイコン（赤丸）の個数で表示する。画面左上の HUD。
/// あらかじめ最大体力ぶんの Image を並べておき、現在値に応じて表示/非表示を切り替える。
/// </summary>
public class HealthUI : MonoBehaviour
{
    [SerializeField] private PlayerHealth playerHealth;
    [Tooltip("左から順に並べた体力アイコン。要素数 >= 最大体力にしておく")]
    [SerializeField] private Image[] icons;

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
