using UnityEngine;

/// <summary>
/// 一方通行の高台。
///  - 下から上へは常にすり抜ける
///  - 上から下へは抜けられない（着地できる）
///  - ただし「プレイヤーの横幅のうち requiredOverlap 以上が天面に重なっている」ときだけ着地判定を有効化する。
///    端にわずかに引っかかっただけで乗れてしまう問題を防ぐ。
///
/// PlatformEffector2D は重なり量を条件にできないため、毎物理フレーム
/// Physics2D.IgnoreCollision でプレイヤー本体との当たりを切り替える方式にしている。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class OneWayPlatform : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("プレイヤーの横幅のうち、この割合以上が天面に重なっていれば着地できる")]
    [SerializeField] private float requiredOverlap = 0.5f;

    [Tooltip("足がこの余裕（ワールド単位）だけ天面より上にあれば『上にいる』とみなす")]
    [SerializeField] private float topTolerance = 0.05f;

    [Tooltip("未設定なら Tag=Player から自動取得")]
    [SerializeField] private Collider2D playerCollider;

    private Collider2D _col;
    private Rigidbody2D _playerRb;
    private bool _ignored;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
    }

    private void Start()
    {
        if (playerCollider == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerCollider = p.GetComponent<Collider2D>();
        }
        if (playerCollider != null) _playerRb = playerCollider.attachedRigidbody;

        SetIgnored(true); // 既定はすり抜け。着地条件が揃ったときだけ固くする。
    }

    private void FixedUpdate()
    {
        if (playerCollider == null || _playerRb == null) return;

        Bounds pb = playerCollider.bounds;
        Bounds tb = _col.bounds;

        bool feetAboveSurface = pb.min.y >= tb.max.y - topTolerance;
        bool movingDown = _playerRb.linearVelocity.y <= 0.001f;

        // ── overlapX: プレイヤーと高台を横軸(X)に投影したときの「重なりの幅」 ──
        // pb / tb は AABB。.min.x が左端、.max.x が右端。
        // 2区間 [pb.min.x, pb.max.x] と [tb.min.x, tb.max.x] が重なる部分は
        //   右端 = 2つの右端のうち小さいほう  = Mathf.Min(pb.max.x, tb.max.x)
        //   左端 = 2つの左端のうち大きいほう  = Mathf.Max(pb.min.x, tb.min.x)
        //   幅   = 右端 - 左端
        // 重なっていない場合はこの値が負になる（後段の Mathf.Max(overlapX, 0f) で 0 に丸める）。
        //
        // 具体例（高台の天面が x 1.0〜5.0、プレイヤーの横幅は 1.0）:
        //
        //   A. 完全に上に乗っている（プレイヤー中心 x=3.0 → プレイヤーは 2.5〜3.5）
        //        右端 = min(3.5, 5.0) = 3.5 ,  左端 = max(2.5, 1.0) = 2.5
        //        overlapX = 3.5 - 2.5 = 1.0   → 体の 100% が乗っている
        //
        //   B. 右端にギリギリ乗っている（中心 x=4.8 → プレイヤーは 4.3〜5.3）
        //        右端 = min(5.3, 5.0) = 5.0 ,  左端 = max(4.3, 1.0) = 4.3
        //        overlapX = 5.0 - 4.3 = 0.7   → 体の 70%
        //
        //   C. ほとんど乗っていない（中心 x=5.2 → プレイヤーは 4.7〜5.7）
        //        右端 = min(5.7, 5.0) = 5.0 ,  左端 = max(4.7, 1.0) = 4.7
        //        overlapX = 5.0 - 4.7 = 0.3   → 体の 30%（requiredOverlap 0.5 未満なのですり抜ける）
        //
        //   D. 完全に外れている（中心 x=6.0 → プレイヤーは 5.5〜6.5）
        //        右端 = min(6.5, 5.0) = 5.0 ,  左端 = max(5.5, 1.0) = 5.5
        //        overlapX = 5.0 - 5.5 = -0.5  → 負。Mathf.Max(..., 0f) で 0 に丸める
        //
        // ratio = 重なりの幅 / プレイヤーの横幅 = 「体の何割が天面に乗っているか」
        // （上の例で A=1.0, B=0.7, C=0.3, D=0.0）。これが requiredOverlap 以上なら着地できる。
        float overlapX = Mathf.Min(pb.max.x, tb.max.x) - Mathf.Max(pb.min.x, tb.min.x);
        float ratio = pb.size.x > 0f ? Mathf.Max(overlapX, 0f) / pb.size.x : 0f;

        bool solid = feetAboveSurface && movingDown && ratio >= requiredOverlap;
        SetIgnored(!solid);
    }

    private void SetIgnored(bool ignore)
    {
        if (ignore == _ignored || playerCollider == null) return;
        Physics2D.IgnoreCollision(playerCollider, _col, ignore);
        _ignored = ignore;
    }
}
