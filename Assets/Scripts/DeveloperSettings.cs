using UnityEngine;

/// <summary>
/// 開発者機能の総合スイッチ（インスペクターで切り替え）。
/// `Assets/Resources/DeveloperSettings.asset` を1つ置き、その `developerMode` を編集する。
/// `DevStageClearToggles` / `DevStorySeenToggles` / `DevProgressResetButton` がこれに従う。
///
/// エディタ外のビルドでは `developerMode` の値に関係なく **常に無効**（`Active` が `#if UNITY_EDITOR` ガード）。
/// ＝ アセットを true のままビルドしてもプレイヤーには一切出ない。書き換え・再コンパイル不要。
/// </summary>
[CreateAssetMenu(fileName = "DeveloperSettings", menuName = "RandomGame/Developer Settings")]
public class DeveloperSettings : ScriptableObject
{
    [Tooltip("エディタ内で開発者機能（ステージ選択のチェックボックス・リセットボタン等）を表示するか")]
    public bool developerMode = true;

    private static DeveloperSettings _instance;

    private static DeveloperSettings Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<DeveloperSettings>("DeveloperSettings");
            return _instance;
        }
    }

    /// <summary>いま開発者機能を表示・実行してよいか（エディタ内 かつ アセットの developerMode が true）。</summary>
    public static bool Active
    {
        get
        {
#if UNITY_EDITOR
            var s = Instance;
            return s != null && s.developerMode;
#else
            return false;
#endif
        }
    }
}
