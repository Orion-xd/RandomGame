using UnityEngine;

/// <summary>
/// 移動しない敵（Enemy2 = 据え置き砲台）の発射ロジック。一定間隔でプレイヤーへ向けて弾を発射する。
/// 接触ダメージ・被ダメージなどプレイヤーとの当たり判定仕様は Enemy コンポーネント側が担当する
/// （このスクリプトは発射のみを担当し、Enemy と併用する）。
/// </summary>
public class EnemyShooter : MonoBehaviour
{
    [Header("発射")]
    [SerializeField] private Bullet bulletPrefab;
    [Tooltip("弾の発射位置。未設定ならこの GameObject の位置から発射する")]
    [SerializeField] private Transform firePoint;
    [Tooltip("発射の間隔（秒）")]
    [SerializeField] private float fireInterval = 2f;
    [Tooltip("弾の飛行速度")]
    [SerializeField] private float bulletSpeed = 6f;
    [Tooltip("発射の瞬間だけプレイヤーへホーミングするか。オフなら、プレイヤーがいる左右方向にまっすぐ発射する（上下は狙わない）")]
    [SerializeField] private bool homingOnFire = true;

    private float _timer;
    private SpriteRenderer _sr;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (DialoguePlayer.IsPlaying) return; // ストーリー再生中は敵を行動させない

        _timer += Time.deltaTime;
        if (_timer < fireInterval) return;
        _timer = 0f;

        // 画面外にいるときは発射しない（画面外からの理不尽な弾を防ぐ）
        if (_sr != null && !_sr.isVisible) return;

        Fire();
    }

    private void Fire()
    {
        if (bulletPrefab == null) return;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        Vector2 direction = transform.right;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)origin;
            if (homingOnFire)
            {
                if (toPlayer.sqrMagnitude > 0.0001f) direction = toPlayer.normalized;
            }
            else
            {
                // 上下は狙わず、プレイヤーがいる左右方向にだけまっすぐ飛ばす
                direction = toPlayer.x < 0f ? Vector2.left : Vector2.right;
            }
        }

        var bullet = Instantiate(bulletPrefab, origin, Quaternion.identity);
        bullet.Configure(direction, bulletSpeed, 1);
    }
}
