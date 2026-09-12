using UnityEngine;

/// <summary>
/// 画面が切り替わった直後、一定時間「決定系」の入力を無効化するための共有ロック。
/// 連打しすぎた勢いで、遷移先で意図しない操作（ボタン決定・会話送り・メインアクション発動）が
/// 起きるのを防ぐ。
///
///  - 画面 / パネルが出たら <see cref="LockFor"/>(秒) を呼ぶ。
///  - 入力を読む側は毎フレーム <see cref="InputAllowed"/> を見て、false の間は入力を無視する。
///  - 他の有効な操作が行われたら <see cref="Unlock"/> でロックを即座に解除できる。
///
/// **対象は「決定系」の入力だけ**（スペース / エンター / テンキー Enter / 左クリックが引き金になる操作 ―
/// メインアクション発動・メニュー決定・会話送り）。2026-09-12 に「連打で誤って決定してしまう」ことと
/// 「操作しようとした意図した入力がブロックされる」ことを分離し、次の2パターンに整理した:
///
///  - **メニュー画面（Title / StageSelect / 結果パネル、`MenuNavigation`）**: カーソル移動（WASD / 矢印キー・
///    マウスホバー）は <see cref="SceneTransition"/> のフェード演出中（<see cref="NavigationAllowed"/> が
///    false の間）だけブロックし、フェードが終わっていればこの `InputLock` の決定用タイマーは無視して常に
///    受け付ける。加えて、カーソル移動が実際に成立した（＝そのシーンで意味のある操作だった）瞬間に
///    <see cref="Unlock"/> を呼び、決定のロックも即座に解除する（キー入力で画面を操作し始めた時点で
///    「連打の勢い」ではなく「意図した操作」だと判断できるため）。
///      ＝ 状態は3段階: ① フェード中は何もかもブロック ② フェード終了〜決定ロック解除までは決定だけ
///        ブロック（カーソル移動は可能、成立すれば即ブロック解除）③ 両方明けたら通常どおり。
///  - **ステージ画面（Player の移動・メインアクション発動）**: 両方ともこの `InputLock` の対象のまま
///    （2026-09-12、一度は移動だけ対象外にしたが「動けるのにアクションが出せない」という不自然さの方が
///    問題だったため、移動も含めて元の「両方ブロック」に戻した）。ステージ画面にはメニューのカーソル
///    移動に相当する操作が無いため、`Unlock` による早期解除もしない。
///
/// 時間は `Time.unscaledTime` 基準（`Time.timeScale = 0` の結果画面・会話中でも進む）。
/// 複数箇所から LockFor されても、一番遅い解除時刻が採用される。
/// また <see cref="SceneTransition"/> のフェード演出中は `Unlock` を呼んでも <see cref="InputAllowed"/> は
/// 常に false のまま（そちらは別条件。フェード自体の長さを短縮する仕組みではない）。
/// </summary>
public static class InputLock
{
    private static float _unlockAtUnscaled;

    /// <summary>今から seconds 秒、決定系の入力を無効化する。</summary>
    public static void LockFor(float seconds)
    {
        float t = Time.unscaledTime + Mathf.Max(0f, seconds);
        if (t > _unlockAtUnscaled) _unlockAtUnscaled = t;
    }

    /// <summary>他の有効な操作（メニューのカーソル移動が実際に成立したときなど）が行われたら、
    /// 決定系のロックを今すぐ解除する。フェード演出中は無効（<see cref="InputAllowed"/> はフェードが
    /// 終わるまでどのみち false のまま）。</summary>
    public static void Unlock()
    {
        _unlockAtUnscaled = Time.unscaledTime;
    }

    /// <summary>今、決定系の入力（メインアクション発動・メニュー決定・会話送りなど）を受け付けてよいか
    /// （フェード演出中も、その後の猶予中も false）。</summary>
    public static bool InputAllowed =>
        !SceneTransition.Transitioning && Time.unscaledTime >= _unlockAtUnscaled;

    /// <summary>今、移動・カーソル移動など「決定以外」の入力を受け付けてよいか。
    /// <see cref="SceneTransition"/> のフェード演出中だけ false（決定用の猶予タイマーは見ない）。
    /// メニュー画面のカーソル移動・マウスホバーはこちらを見る（<see cref="InputAllowed"/> ではない）。</summary>
    public static bool NavigationAllowed => !SceneTransition.Transitioning;
}
