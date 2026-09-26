using System;
using UnityEngine;

/// <summary>
/// Animation Event（AnimationClip に仕込んだ、特定のフレームでメソッドを呼ぶ仕組み）を
/// C# のイベントに変換して中継するだけの汎用コンポーネント。
/// Animator と同じ GameObject に置き、Animation Event 側からは常に RaiseEvent(string) だけを呼ぶ。
/// 何が起きたときに何をするか（コンボ受付を閉じる、攻撃判定を消す、等）は、このクラスは一切関知しない
/// ＝キャラクターごとの反応ロジックとは切り離してあるので、敵キャラなど他のキャラクターでも
/// （それぞれが必要とする別のイベント名で）そのまま使い回せる。
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    /// <summary>Animation Event で指定した名前（例："DashEnd"）を、そのまま引数として渡して発火する。</summary>
    public event Action<string> OnAnimationEvent;

    /// <summary>Animation Event から呼ばれる想定のメソッド（AnimationEvent の stringParameter に名前を入れて指定する）。</summary>
    public void RaiseEvent(string eventName)
    {
        OnAnimationEvent?.Invoke(eventName);
    }
}
