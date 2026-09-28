using UnityEngine;

/// <summary>
/// ボス等の頭上に出す体力ゲージ + 残り体力の数値。
/// 子に「塗り(fill)」の Transform と TextMesh を持つ想定。fill は横スケール 0..1 で増減する。
///
/// fill のスプライトの pivot は中心にあるため、スケールを変えるだけだと中央を基準に
/// 両側から均等に縮んでしまう。左端(満タン時の左端)を固定し、右側からだけ減っていくように
/// 見せるため、スケールで詰まった分だけ中心位置(localPosition.x)を左へずらして補正している。
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Tooltip("横スケールを 0..1 で変化させるバーの塗り")]
    [SerializeField] private Transform fill;
    [Tooltip("残り体力の数値表示")]
    [SerializeField] private TextMesh label;

    private SpriteRenderer _fillSprite;
    private float _fillBaseX;
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized || fill == null) return;
        _initialized = true;
        _fillSprite = fill.GetComponent<SpriteRenderer>();
        _fillBaseX = fill.localPosition.x; // 満タン時(ratio=1)の中心位置＝左端の基準
    }

    public void Set(int current, int max)
    {
        EnsureInitialized();
        float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (fill != null)
        {
            Vector3 s = fill.localScale;
            s.x = ratio;
            fill.localScale = s;

            float halfWidth = _fillSprite != null ? _fillSprite.sprite.bounds.extents.x : 0f;
            var p = fill.localPosition;
            p.x = _fillBaseX - halfWidth * (1f - ratio);
            fill.localPosition = p;
        }
        if (label != null) label.text = current + " / " + max;
    }
}
