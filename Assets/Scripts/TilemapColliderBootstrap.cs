using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// スクリプト（エディタ拡張 / MCP eval）で <c>SetTile</c> して作った Tilemap は、
/// Play 開始時に <see cref="TilemapCollider2D"/> / <see cref="CompositeCollider2D"/> が
/// コリジョン形状を生成しないことがある（タイル変更イベントが記録されず、
/// <c>CompositeCollider2D.pathCount</c> が 0 のまま＝地面がすり抜ける）。
///
/// このコンポーネントを Tilemap と同じ GameObject に付けておくと、
/// <see cref="Awake"/> でタイルを一括で貼り直し、コライダーの再生成を促す。
/// 通常のタイルパレットで塗ったマップに付けても、同じ内容を貼り直すだけなので実害はない。
///
/// エディタで普通に塗ったマップならそもそも不要。ここでは地面を eval で生成しているため必要。
/// </summary>
[RequireComponent(typeof(Tilemap))]
[RequireComponent(typeof(TilemapCollider2D))]
public class TilemapColliderBootstrap : MonoBehaviour
{
    private void Awake()
    {
        var tilemap = GetComponent<Tilemap>();
        var tilemapCollider = GetComponent<TilemapCollider2D>();
        var composite = GetComponent<CompositeCollider2D>();

        // 現在のタイルを丸ごと取得 → 一度消して → 貼り直す。
        // 「消す」「貼る」がそれぞれ本物のタイル変更として登録されるので、
        // コライダーが形状を作り直す。
        var bounds = tilemap.cellBounds;
        var tiles = tilemap.GetTilesBlock(bounds);

        tilemap.ClearAllTiles();
        tilemapCollider.ProcessTilemapChanges();

        tilemap.SetTilesBlock(bounds, tiles);
        tilemapCollider.ProcessTilemapChanges();

        if (composite != null) composite.GenerateGeometry();
    }
}
