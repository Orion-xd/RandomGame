using UnityEngine;

/// <summary>
/// ボス等の頭上に出す体力ゲージ + 残り体力の数値。
/// 子に「塗り(fill)」の Transform と TextMesh を持つ想定。fill は横スケール 0..1 で増減する。
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Tooltip("横スケールを 0..1 で変化させるバーの塗り")]
    [SerializeField] private Transform fill;
    [Tooltip("残り体力の数値表示")]
    [SerializeField] private TextMesh label;

    public void Set(int current, int max)
    {
        float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (fill != null)
        {
            Vector3 s = fill.localScale;
            s.x = ratio;
            fill.localScale = s;
        }
        if (label != null) label.text = current + " / " + max;
    }
}
