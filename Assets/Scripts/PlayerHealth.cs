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
    private MainActionController _mainAction;

    public int Health => _health;
    public int MaxHealth => maxHealth;

    /// <summary>体力が変化したとき（初期化時も含む）に発火。UI 更新用。</summary>
    public event Action OnHealthChanged;

    private void Awake()
    {
        _health = maxHealth;
        _mainAction = GetComponent<MainActionController>();
    }

    private void Start()
    {
        OnHealthChanged?.Invoke();
    }

    public void TakeDamage(int amount)
    {
        if (_health <= 0) return;
        if (Time.time < _invulnUntil) return;
        if (_mainAction != null && _mainAction.IsInvincible) return; // ダッシュ中はすり抜け（無敵）

        _health -= amount;
        _invulnUntil = Time.time + invulnTime;
        OnHealthChanged?.Invoke();
        Debug.Log($"Player took {amount} dmg -> HP {_health}/{maxHealth}");

        if (_health <= 0)
        {
            Debug.Log("Player MISS (体力0) -> TODO: リザルト画面へ");
        }
    }
}
