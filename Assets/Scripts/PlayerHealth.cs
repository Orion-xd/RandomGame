using System;
using UnityEngine;

/// <summary>
/// プレイヤーの体力（カービィ風）。最小実装。
/// ダッシュ中（MainActionController.IsInvincible）と被弾直後の無敵時間中はダメージを受けない。
/// ※ ミス時のリザルト遷移・落下即ミスは今後のステップで追加予定。
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 3;
    [Tooltip("被弾後の無敵時間（同じ敵に毎フレーム削られるのを防ぐ）")]
    [SerializeField] private float invulnTime = 0.8f;

    private int _health;
    private float _invulnUntil;
    private bool _died;
    private MainActionController _mainAction;

    public int Health => _health;
    public int MaxHealth => maxHealth;

    /// <summary>体力が変化したとき（初期化時も含む）に発火。UI 更新用。</summary>
    public event Action OnHealthChanged;

    /// <summary>体力が0になったとき1回だけ発火。ステージ失敗の判定に使う。</summary>
    public event Action OnDied;

    private void Awake()
    {
        _health = maxHealth;
        _mainAction = GetComponent<MainActionController>();
    }

    private void Start()
    {
        OnHealthChanged?.Invoke();
    }

    /// <summary>ダメージを与える。実際に適用された（無敵・無敵時間中でなかった）ら true を返す。
    /// 敵側はこれを見てノックバックを与えるかどうかを判断する。</summary>
    public bool TakeDamage(int amount)
    {
        if (_health <= 0) return false;
        if (Time.time < _invulnUntil) return false;
        if (_mainAction != null && _mainAction.IsInvincible) return false; // ダッシュ中はすり抜け（無敵）

        _health -= amount;
        _invulnUntil = Time.time + invulnTime;
        OnHealthChanged?.Invoke();
        Debug.Log($"Player took {amount} dmg -> HP {_health}/{maxHealth}");

        if (_health <= 0 && !_died)
        {
            _died = true;
            Debug.Log("Player MISS (体力0)");
            OnDied?.Invoke();
        }

        return true;
    }
}
