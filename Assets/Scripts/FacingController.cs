using UnityEngine;

/// <summary>
/// 敵キャラの「向き」（+1=右, -1=左）を一元管理する小さなヘルパー。SpriteRenderer.flipX（見た目の反転）と、
/// firePoint など「向きに応じてローカルX座標を反転させたい子オブジェクト」を同時に更新する。
/// 子オブジェクトの反転は、現在のローカルX座標の絶対値に向きの符号を付け直すだけ
/// （<see cref="AttackHitbox.Configure"/>と同じ考え方）なので、初期配置がどちら向きで
/// 作られていても、実行時に最初に向きが決まった瞬間に正しい側へ揃う。
///
/// 「向きを変えられない期間」（例: Enemy3AIの突進攻撃中）に対応するため Lock/Unlock を持つ。
/// ロック中に SetDesiredSign が呼ばれても実際の向きはまだ変えず、「今どちらを向きたいか」だけを
/// 覚えておく（呼ばれるたびに上書きするので、常に最新の希望が残る）。Unlock 時に、その最新の希望が
/// 現在の向きと違っていれば、そこで初めて向きを変える。
/// </summary>
[System.Serializable]
public class FacingController
{
    [Tooltip("見た目のスプライト。未設定でも動くが、見た目の反転はされない")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("向きに応じてローカルX座標を反転させたい子オブジェクト（例: FirePoint）。複数可・任意")]
    [SerializeField] private Transform[] mirroredChildren;
    [Tooltip("元のイラスト素材が右向きに描かれているか。false にすると、素材が左向きに描かれている前提で" +
             "flipXの向きを逆にする（イラスト素材自体は直さず、コード側だけで正しい向きに補正するため）。" +
             "プレイヤーの素材は右向きだが、敵キャラの素材は左向きで描かれているため、敵キャラ側は false にする")]
    [SerializeField] private bool artFacesRightByDefault = true;

    private int _sign = 1;
    private bool _locked;
    private bool _hasPending;
    private int _pendingSign;

    /// <summary>現在の向き（+1=右, -1=左）。</summary>
    public int Sign => _sign;

    /// <summary>「この向きを向きたい」という希望を伝える。ロック中は実際には反映されず、
    /// 最新の希望として覚えておくだけになる（sign==0は無視）。</summary>
    public void SetDesiredSign(int sign)
    {
        if (sign == 0) return;
        if (_locked)
        {
            _pendingSign = sign;
            _hasPending = true;
            return;
        }
        Apply(sign);
    }

    /// <summary>向きをこの瞬間に強制確定させた上で、以後の SetDesiredSign を保留扱いにする
    /// （例: 突進攻撃の開始時、突進方向へ即座に向けてからロックする）。</summary>
    public void LockTo(int sign)
    {
        Apply(sign);
        _locked = true;
        _hasPending = false;
    }

    /// <summary>ロックを解除する。ロック中に希望があれば（＝最後に SetDesiredSign で伝えられた向きが
    /// 現在と違えば）、ここで向きを変える。</summary>
    public void Unlock()
    {
        _locked = false;
        if (_hasPending)
        {
            _hasPending = false;
            Apply(_pendingSign);
        }
    }

    private void Apply(int sign)
    {
        if (sign == _sign) return;
        _sign = sign;

        if (spriteRenderer != null)
            spriteRenderer.flipX = artFacesRightByDefault ? sign < 0 : sign > 0;

        if (mirroredChildren != null)
        {
            foreach (var t in mirroredChildren)
            {
                if (t == null) continue;
                var p = t.localPosition;
                p.x = Mathf.Abs(p.x) * sign;
                t.localPosition = p;
            }
        }
    }
}
