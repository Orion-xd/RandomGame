using UnityEngine;

/// <summary>
/// 画面が切り替わった直後、一定時間すべての入力を無効化するための共有ロック。
/// 連打しすぎた勢いで、遷移先で意図しない操作（ボタン決定・会話送り・アクション発動）が
/// 起きるのを防ぐ。
///
///  - 画面 / パネルが出たら <see cref="LockFor"/>(秒) を呼ぶ。
///  - 入力を読む側は毎フレーム <see cref="InputAllowed"/> を見て、false の間は入力を無視する。
///
/// 時間は `Time.unscaledTime` 基準（`Time.timeScale = 0` の結果画面・会話中でも進む）。
/// 複数箇所から LockFor されても、一番遅い解除時刻が採用される。
/// また <see cref="SceneTransition"/> のフェード演出中も常に無効（そちらが解除時刻を管理する）。
/// </summary>
public static class InputLock
{
    private static float _unlockAtUnscaled;

    /// <summary>今から seconds 秒、すべての入力を無効化する。</summary>
    public static void LockFor(float seconds)
    {
        float t = Time.unscaledTime + Mathf.Max(0f, seconds);
        if (t > _unlockAtUnscaled) _unlockAtUnscaled = t;
    }

    /// <summary>今、入力を受け付けてよいか（フェード演出中は常に false）。</summary>
    public static bool InputAllowed =>
        !SceneTransition.Transitioning && Time.unscaledTime >= _unlockAtUnscaled;
}
