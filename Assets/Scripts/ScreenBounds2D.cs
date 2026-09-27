using UnityEngine;

/// <summary>
/// カメラスクロールが無い(1画面固定)ステージで、画面の左右端のワールドX座標を求めるための共通処理。
/// カメラの現在の orthographicSize / aspect / position から毎回計算するので、カメラの設定を
/// 後から調整しても、参照する側（<see cref="ScreenEdgeWalls"/>・<see cref="Enemy3AI"/>など）は
/// 何も直し直す必要が無い。
///
/// 前提: 対象カメラが Orthographic で、かつそのステージ内では位置が動かない(スクロールしない)こと。
/// スクロールするステージでは呼び出さないこと（画面端という概念自体が意味を持たないため）。
/// </summary>
public static class ScreenBounds2D
{
    public static bool TryGetWorldXRange(Camera cam, out float left, out float right)
    {
        if (cam == null || !cam.orthographic)
        {
            left = 0f;
            right = 0f;
            return false;
        }

        float halfWidth = cam.orthographicSize * cam.aspect;
        left = cam.transform.position.x - halfWidth;
        right = cam.transform.position.x + halfWidth;
        return true;
    }
}
