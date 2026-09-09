using UnityEngine;

/// <summary>
/// シーン遷移のフェード演出と、その前後の入力ロック時間の設定（インスペクターで調整）。
/// `Assets/Resources/SceneTransitionSettings.asset` に1つ置き、<see cref="SceneTransition"/> が読む。
/// </summary>
[CreateAssetMenu(fileName = "SceneTransitionSettings", menuName = "RandomGame/Scene Transition Settings")]
public class SceneTransitionSettings : ScriptableObject
{
    [Tooltip("暗転（黒を被せる）にかける秒数")]
    public float fadeOutSeconds = 0.5f;

    [Tooltip("明転（黒を晴らす）にかける秒数")]
    public float fadeInSeconds = 0.5f;

    [Tooltip("明転しきってから、さらに入力を無効化する秒数（連打対策）")]
    public float postFadeInLockSeconds = 0.5f;

    private static SceneTransitionSettings _instance;

    public static SceneTransitionSettings Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
            return _instance;
        }
    }
}
