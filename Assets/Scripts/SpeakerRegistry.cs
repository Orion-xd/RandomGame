using UnityEngine;

/// <summary>
/// 話者（<see cref="SpeakerId"/>）ごとの表示名・アイコンを一括管理するデータ（ScriptableObject）。
/// ストーリー会話（`DialoguePlayer`）・チュートリアルヒント（`TutorialHintUI`）など、話者アイコンを
/// 表示するシステム全てがこれを共有で参照する。「誰がどのアイコンで喋るか」の対応はシステムの
/// 種類によらず共通なので、ここ一箇所に一度だけ設定すればよい（セリフを増やすたびに個別設定不要）。
///
/// 置き場所: `Assets/Resources/SpeakerRegistry.asset`（`Resources.Load` で参照するため、名前・場所とも固定）。
/// </summary>
[CreateAssetMenu(fileName = "SpeakerRegistry", menuName = "RandomGame/Speaker Registry")]
public class SpeakerRegistry : ScriptableObject
{
    [System.Serializable]
    public class Profile
    {
        public SpeakerId id;
        [Tooltip("テキストボックスに表示する名前")]
        public string displayName;
        [Tooltip("話者アイコン（任意）。未設定ならアイコン欄は空のまま（名前だけ表示される）")]
        public Sprite icon;
    }

    [Tooltip("話者ごとの表示名・アイコン。None 分は呼び出し側がそもそも参照しないので設定不要")]
    public Profile[] profiles;

    private static SpeakerRegistry _instance;

    /// <summary>`Assets/Resources/SpeakerRegistry.asset` を読み込んだシングルトン。</summary>
    public static SpeakerRegistry Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<SpeakerRegistry>("SpeakerRegistry");
            return _instance;
        }
    }

    /// <summary>指定した話者のプロファイルを返す。見つからなければ null（呼び出し側は名前・アイコンとも
    /// 非表示にフォールバックすること）。</summary>
    public static Profile Get(SpeakerId id)
    {
        var inst = Instance;
        if (inst == null || inst.profiles == null) return null;
        foreach (var p in inst.profiles)
            if (p.id == id) return p;
        return null;
    }
}
