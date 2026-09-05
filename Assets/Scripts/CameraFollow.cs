using UnityEngine;

/// <summary>
/// カメラをプレイヤーに追従させる。横スクロールのみ（Y は開始時の高さで固定）。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("カメラの追従対象。プレイヤーなど")]
    [SerializeField] private Transform target;
    [Tooltip("追従の滑らかさ。小さいほど機敏に追う")]
    [SerializeField] private float smoothTime = 0.15f;
    [Tooltip("プレイヤーからの水平オフセット")]
    [SerializeField] private float xOffset = 0f;

    private float _fixedY;
    private float _fixedZ;
    private float _velX;

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
