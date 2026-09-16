using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーの前方に一定時間だけ出現する攻撃判定。GameObject の有効/無効で ON/OFF する。
/// 有効化中に敵へ触れると 1 回だけダメージを与える（同じ敵を多重ヒットしない）。
/// 敵の弾（Bullet）に触れた場合は、体力の概念が無いので一撃で即座に破壊する。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AttackHitbox : MonoBehaviour
{
    private int _damage = 1;
    private readonly HashSet<Enemy> _hit = new HashSet<Enemy>();

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    /// <summary>
    /// 向きとダメージをセットし、前方へ配置する。
    /// 前方距離は Transform の localPosition.x の絶対値をそのまま使うので、
    /// インスペクターで Transform の X を直接調整すればそれが反映される。
    /// </summary>
    public void Configure(int facingSign, int damage)
    {
        _damage = damage;
        Vector3 p = transform.localPosition;
        p.x = Mathf.Abs(p.x) * (facingSign < 0 ? -1f : 1f);
        transform.localPosition = p;
    }

    private void OnEnable() => _hit.Clear();

    private void OnTriggerEnter2D(Collider2D other) => TryHit(other);
    private void OnTriggerStay2D(Collider2D other) => TryHit(other);

    private void TryHit(Collider2D other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            if (_hit.Contains(enemy)) return;
            _hit.Add(enemy);
            enemy.TakeDamage(_damage);
            return;
        }

        var bullet = other.GetComponentInParent<Bullet>();
        if (bullet != null) bullet.DestroyByAttack();
    }
}
