using UnityEngine;

/// <summary>
/// 敵をスポーン地点を中心に左右へゆっくり往復させる。
/// Rigidbody を使わず Transform を直接動かす簡易版。向き（スプライトの flipX）も進行方向に合わせる。
/// </summary>
public class EnemyPatrol : MonoBehaviour
{
    [Tooltip("移動速度（ゆっくりめ）")]
    [SerializeField] private float speed = 1.5f;
    [Tooltip("スポーン地点から左右それぞれへ動く距離")]
    [SerializeField] private float range = 2f;

    private float _originX;
    private int _dir = 1;
    private SpriteRenderer _sr;

    private void Start()
    {
        _originX = transform.position.x;
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        Vector3 p = transform.position;
        p.x += _dir * speed * Time.deltaTime;

        if (p.x > _originX + range) { p.x = _originX + range; _dir = -1; }
        else if (p.x < _originX - range) { p.x = _originX - range; _dir = 1; }

        transform.position = p;
        if (_sr != null) _sr.flipX = _dir < 0;
    }
}
