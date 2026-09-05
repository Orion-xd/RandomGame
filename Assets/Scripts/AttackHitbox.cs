using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーの前方に一定時間だけ出現する攻撃判定。GameObject の有効/無効で ON/OFF する。
/// 有効化中に敵へ触れると 1 回だけダメージを与える（同じ敵を多重ヒットしない）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AttackHitbox : MonoBehaviour
{
    [Tooltip("キャラ中心から前方へのオフセット")]
    [SerializeField] private float forwardOffset = 0.9f;

    private int _damage = 1;
    private readonly HashSet<Enemy> _hit = new HashSet<Enemy>();

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    /// <summary>向きとダメージをセットし、前方へ配置する。</summary>
    public void Configure(int facingSign, int damage)
    {
        _damage = damage;
        Vector3 p = transform.localPosition;
        p.x = forwardOffset * (facingSign < 0 ? -1f : 1f);
        transform.localPosition = p;
    }

    private void OnEnable() => _hit.Clear();

    private void OnTriggerEnter2D(Collider2D other) => TryHit(other);
    private void OnTriggerStay2D(Collider2D other) => TryHit(other);

    private void TryHit(Collider2D other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        if (enemy == null || _hit.Contains(enemy)) return;
        _hit.Add(enemy);
        enemy.TakeDamage(_damage);
    }
}
