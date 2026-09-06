using UnityEngine;

/// <summary>
/// ステージ最奥のゴール。プレイヤー本体が触れるとステージクリア。
/// ただしステージにボス（Enemy で MaxHealth &gt;= 2）が生存している間はクリアにならない。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Goal : MonoBehaviour
{
    [SerializeField] private StageManager stageManager;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
    }

    private void OnTriggerEnter2D(Collider2D other) => TryReach(other);
    private void OnTriggerStay2D(Collider2D other) => TryReach(other);

    private void TryReach(Collider2D other)
    {
        if (stageManager == null || !other.CompareTag("Player")) return;
        if (AnyBossAlive()) return; // ボスを倒すまでゴールできない
        stageManager.Clear();
    }

    private static bool AnyBossAlive()
    {
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (e != null && e.MaxHealth >= 2) return true;
        return false;
    }
}
