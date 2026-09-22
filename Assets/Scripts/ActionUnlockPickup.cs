using UnityEngine;

/// <summary>
/// 触れるとメインアクションを1つ解放するアイテム（チュートリアル用）。
/// 拾う操作は不要で、プレイヤー本体のコライダーに接触した瞬間に解放される。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ActionUnlockPickup : MonoBehaviour
{
    [Tooltip("これに触れると解放されるアクション")]
    [SerializeField] private MainActionType action;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryPickup(other);
    private void OnTriggerStay2D(Collider2D other) => TryPickup(other);

    private void TryPickup(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var queue = other.GetComponent<MainActionQueue>();
        if (queue == null) return;

        queue.UnlockAction(action);
        Destroy(gameObject);
    }
}
