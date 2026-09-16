using UnityEngine;

/// <summary>
/// カメラをプレイヤーに追従させる。横方向・縦方向それぞれ個別に追従のオン/オフを切り替えられる
/// （横スクロールのステージは横のみ、縦スクロールのステージは縦のみ、という使い分けを想定）。
/// オフにした方向は開始時の位置で固定される。
///
/// `target` は他 GameObject（Player）への直接参照のため、Player を削除して作り直す
/// （Prefab 化に伴う差し替えなど）と参照が外れてカメラが追従しなくなる事故が起きやすい。
/// 対策として、未設定（null）なら Tag="Player" から自動解決する（Goal/OneWayPlatform と同じ流儀）。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("カメラの追従対象。プレイヤーなど。未設定なら Tag=Player から自動取得")]
    [SerializeField] private Transform target;
    [Tooltip("追従の滑らかさ。小さいほど機敏に追う")]
    [SerializeField] private float smoothTime = 0.15f;
    [Tooltip("プレイヤーからの水平オフセット")]
    [SerializeField] private float xOffset = 0f;
    [Tooltip("プレイヤーからの垂直オフセット")]
    [SerializeField] private float yOffset = 0f;

    [Header("追従方向")]
    [Tooltip("横方向に追従するか。オフなら開始時の X で固定（縦スクロールのステージ用）")]
    [SerializeField] private bool followHorizontal = true;
    [Tooltip("縦方向に追従するか。オフなら開始時の Y で固定（横スクロールのステージ用）")]
    [SerializeField] private bool followVertical = false;

    private float _fixedX;
    private float _fixedY;
    private float _fixedZ;
    private float _velX;
    private float _velY;

    private void Awake()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
            else Debug.LogWarning("CameraFollow: 追従対象が未設定で、Tag=Player のオブジェクトも見つかりません。", this);
        }
    }

    private void Start()
    {
        _fixedX = transform.position.x;
        _fixedY = transform.position.y;
        _fixedZ = transform.position.z;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float x = _fixedX;
        if (followHorizontal)
        {
            float targetX = target.position.x + xOffset;
            x = Mathf.SmoothDamp(transform.position.x, targetX, ref _velX, smoothTime);
        }

        float y = _fixedY;
        if (followVertical)
        {
            float targetY = target.position.y + yOffset;
            y = Mathf.SmoothDamp(transform.position.y, targetY, ref _velY, smoothTime);
        }

        transform.position = new Vector3(x, y, _fixedZ);
    }
}
