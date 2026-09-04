using UnityEngine;

/// <summary>
/// 敵キャラ。通常は体力1で一撃。ボス等は maxHealth を 2 以上にすると頭上に体力ゲージを表示する。
/// プレイヤーに触れると（プレイヤーが無敵/ダッシュ中でなければ）接触ダメージを与える。物理的には貫通する。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private int maxHealth = 1;
    [SerializeField] private int contactDamage = 1;
    [Tooltip("頭上の体力ゲージ。maxHealth >= 2 のときだけ表示される")]
    [SerializeField] private EnemyHealthBar healthBar;

    private int _health;

    public int Health => _health;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        _health = maxHealth;
        GetComponent<Collider2D>().isTrigger = true; // 貫通させるため常にトリガー

        if (healthBar != null)
        {
            healthBar.gameObject.SetActive(maxHealth >= 2);
            healthBar.Set(_health, maxHealth);
        }
    }

    /// <summary>攻撃判定などから呼ばれる。</summary>
    public void TakeDamage(int amount)
    {
        if (_health <= 0) return;

        _health -= amount;
        if (healthBar != null && maxHealth >= 2) healthBar.Set(Mathf.Max(_health, 0), maxHealth);

        if (_health <= 0) Die();
    }

    private void Die()
    {
        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other) => TryTouchPlayer(other);
    private void OnTriggerStay2D(Collider2D other) => TryTouchPlayer(other);

    private void TryTouchPlayer(Collider2D other)
    {
        // プレイヤー「本体」のコライダーにだけ反応する。プレイヤーの攻撃判定には反応しない。
        // GetComponentInParent にすると、Player の子である AttackHitbox が触れたときも
        // 親の PlayerHealth を拾ってしまい、攻撃するたびに自分がダメージを受けるので、
        // 普通にGetComponentを使って取得する。
        var health = other.GetComponent<PlayerHealth>();
        if (health != null) health.TakeDamage(contactDamage);
    }
}
