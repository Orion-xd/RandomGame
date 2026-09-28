using UnityEngine;

/// <summary>
/// カメラスクロールが無い(1画面固定)ステージ用。画面の左右端に、プレイヤー(や敵)が画面外へ
/// 出られないようにする物理的な壁を自動配置する。
///
/// Start 時にカメラの現在の表示範囲（<see cref="ScreenBounds2D"/>、orthographicSize×aspect）から
/// 壁の位置・サイズを計算して leftWall / rightWall（実体のある Collider2D）へ反映するので、
/// カメラの位置やサイズを後で調整しても、壁を手動で置き直す必要が無い。
/// <see cref="Enemy3AI"/> の「画面端にいるか」の判定も同じ ScreenBounds2D を参照しているため、
/// 見た目の壁とAIの判定が常にズレなく一致する。
///
/// leftWall / rightWall はシーン上に実体として置いた Collider2D（isTrigger オフ、Rigidbody2D 不要。
/// プレイヤー側の Rigidbody2D が押し返しを処理するので、地面と同じ要領で機能する）。
/// </summary>
public class ScreenEdgeWalls : MonoBehaviour
{
    [Tooltip("未設定なら Camera.main を使う")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("画面左端に置く壁")]
    [SerializeField] private BoxCollider2D leftWall;
    [Tooltip("画面右端に置く壁")]
    [SerializeField] private BoxCollider2D rightWall;
    [Tooltip("壁の厚み")]
    [SerializeField] private float thickness = 2f;
    [Tooltip("壁の高さ（縦方向を十分覆う値にする）")]
    [SerializeField] private float height = 40f;

    private void Start()
    {
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (!ScreenBounds2D.TryGetWorldXRange(cam, out float left, out float right))
        {
            Debug.LogWarning("ScreenEdgeWalls: カメラが見つからない、または Orthographic ではありません。", this);
            return;
        }

        if (leftWall != null)
        {
            var p = leftWall.transform.position;
            p.x = left - thickness * 0.5f;
            leftWall.transform.position = p;
            leftWall.size = new Vector2(thickness, height);
        }

        if (rightWall != null)
        {
            var p = rightWall.transform.position;
            p.x = right + thickness * 0.5f;
            rightWall.transform.position = p;
            rightWall.size = new Vector2(thickness, height);
        }
    }
}
