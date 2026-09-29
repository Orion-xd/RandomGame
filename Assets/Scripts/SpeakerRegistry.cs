using UnityEngine;

/// <summary>
/// 話者（<see cref="SpeakerId"/>）ごとの表示名・表情アイコンを一括管理するデータ（ScriptableObject）。
/// ストーリー会話（`DialoguePlayer`）・チュートリアルヒント（`TutorialHintUI`）など、話者アイコンを
/// 表示するシステム全てがこれを共有で参照する。「誰がどのアイコンで喋るか」の対応はシステムの
/// 種類によらず共通なので、ここ一箇所に一度だけ設定すればよい（セリフを増やすたびに個別設定不要）。
///
/// キャラクター1人につきアイコンは1枚ではなく、表情（<see cref="SpeakerExpression"/>）ごとに
/// 複数登録できる。セリフ側（DialogueSequence.Page / TutorialHint）は話者を選んだあと、続けて
/// 表情も選ぶ。そのキャラクターにまだ用意していない表情が選ばれた場合は、Normal のアイコンに
/// 自動でフォールバックする（Normal 自体も無ければアイコンなし扱い）。
///
/// 置き場所: `Assets/Resources/SpeakerRegistry.asset`（`Resources.Load` で参照するため、名前・場所とも固定）。
/// </summary>
[CreateAssetMenu(fileName = "SpeakerRegistry", menuName = "RandomGame/Speaker Registry")]
public class SpeakerRegistry : ScriptableObject
{
    [System.Serializable]
    public class ExpressionIcon
    {
        public SpeakerExpression expression;
        public Sprite icon;
    }

    [System.Serializable]
    public class Profile
    {
        public SpeakerId id;
        [Tooltip("テキストボックスに表示する名前")]
        public string displayName;
        [Tooltip("表情ごとのアイコン。用意していない表情が指定されたときは Normal のアイコンに" +
                 "自動でフォールバックする（Normal も無ければアイコン欄は空のまま）")]
        public ExpressionIcon[] expressions;

        /// <summary>指定した表情のアイコンを返す。無ければ Normal にフォールバックし、それも無ければ null。</summary>
        public Sprite GetIcon(SpeakerExpression expression)
        {
            if (expressions == null) return null;

            foreach (var e in expressions)
                if (e.expression == expression) return e.icon;

            if (expression != SpeakerExpression.Normal)
                foreach (var e in expressions)
                    if (e.expression == SpeakerExpression.Normal) return e.icon;

            return null;
        }
    }

    [Tooltip("話者ごとの表示名・表情アイコン。None 分は呼び出し側がそもそも参照しないので設定不要")]
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

    /// <summary>指定した話者・表情のアイコンを返す（Get(id) と Profile.GetIcon をまとめた便利メソッド）。</summary>
    public static Sprite GetIcon(SpeakerId id, SpeakerExpression expression) => Get(id)?.GetIcon(expression);
}
