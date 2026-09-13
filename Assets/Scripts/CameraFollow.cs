using UnityEngine;

/// <summary>
/// カメラをプレイヤーに追従させる。横スクロールのみ（Y は開始時の高さで固定）。
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

    private float _fixedY;
    private float _fixedZ;
    private float _velX;

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
        _fixedY = transform.position.y;
        _fixedZ = transform.position.z;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float targetX = target.position.x + xOffset;
        float x = Mathf.SmoothDamp(transform.position.x, targetX, ref _velX, smoothTime);
        transform.position = new Vector3(x, _fixedY, _fixedZ); // 縦追従なし
    }
}
