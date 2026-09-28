using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 話者アイコン・名前欄・本文を持つテキストボックスの「見た目」だけを担当するビュー。
/// ストーリー会話（`DialoguePlayer`）とチュートリアルヒント（`TutorialHintUI`）は、両方とも
/// この同じプレハブ（`Assets/Prefabs/UI/SpeakerTextBox.prefab`）のインスタンスを使う。
///
/// アイコンの位置・サイズ、名前欄の位置・サイズ・色・フォント、本文のフォントサイズ、箱の背景色
/// ――こういった「見た目」を調整したいときは、必ずこのプレハブ自身を直接編集する（Prefab Mode で
/// ダブルクリックして開けば、ゲームを実行しなくてもそのまま実際のレイアウトが表示される）。
/// ストーリー会話・チュートリアルヒントいずれかの個別スクリプト側には、この見た目に関するフィールドは
/// 一切持たせない（統一前は `DialoguePlayer` と `TutorialHintUI` の両方が独自にアイコン位置等の
/// フィールドを持っていて、直す場所が2箇所に分かれてしまっていた反省による設計）。
///
/// 文字送り（タイプライター演出）やページ送りといった「振る舞い」はここには一切持たせない。
/// あくまで見た目の入れ物であり、内容の反映は呼び出し側（`DialoguePlayer`/`TutorialHintUI`）が行う。
/// </summary>
public class SpeakerTextBoxView : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image speakerIcon;
    [SerializeField] private Text speakerName;
    [SerializeField] private Text body;

    public RectTransform RectTransform => (RectTransform)transform;
    public Image Background => background;
    public Image SpeakerIcon => speakerIcon;
    public Text SpeakerName => speakerName;
    public Text Body => body;

    /// <summary>話者アイコン・名前欄の表示内容を反映する（本文の余白は別途 GetBodyInset/ApplyBodyInset で扱う）。
    /// profile が null（ナレーション扱い）なら、アイコン・名前欄とも非表示にする。</summary>
    public void SetSpeaker(SpeakerRegistry.Profile profile)
    {
        bool hasIcon = profile != null && profile.icon != null;
        bool hasName = profile != null && !string.IsNullOrEmpty(profile.displayName);

        if (speakerIcon != null)
        {
            speakerIcon.enabled = hasIcon;
            if (hasIcon) speakerIcon.sprite = profile.icon;
        }
        if (speakerName != null)
        {
            speakerName.enabled = hasName;
            if (hasName) speakerName.text = profile.displayName;
        }
    }

    /// <summary>アイコン欄ぶんのスペースを踏まえた、本文の左右マージン(px)を計算するだけ（実際に反映は
    /// しない）。**話者の有無によらず常に同じ値**を返す（ユーザー指定：話者がいてもいなくても本文の
    /// 左右余白は同じだけ取る。以前はナレーション時だけ狭いマージンにしていたが、システムごとに
    /// その既定値がバラバラ＝30px/20pxになってしまっていたため、区別自体を廃止した）。
    /// アイコン・名前欄の「実際の」サイズ・位置をその都度読むので、値を別のフィールドへ複製する必要が
    /// 無く、このプレハブ上でアイコン・名前欄を動かす/リサイズするだけで自動的に追従する。
    /// 呼び出し側が独自の文字送り対策（中央揃えブレ防止など）を行いたい場合は、この戻り値をそのまま
    /// 渡せばよい。</summary>
    public float GetBodyInset()
    {
        float margin = speakerIcon != null ? ((RectTransform)speakerIcon.transform).anchoredPosition.x : 16f;
        float iconWidth = speakerIcon != null ? ((RectTransform)speakerIcon.transform).sizeDelta.x : 0f;
        float nameWidth = speakerName != null ? ((RectTransform)speakerName.transform).sizeDelta.x : 0f;
        float columnWidth = Mathf.Max(iconWidth, nameWidth);
        return margin * 2f + columnWidth;
    }

    /// <summary>本文の左右マージンを直接反映する（文字送りなど追加の対策が要らない側＝`TutorialHintUI`用）。
    /// `DialoguePlayer`のように中央揃えブレ防止の追加計算が要る場合は、`GetBodyInset`だけを使って
    /// 呼び出し側で本文の`offsetMin`/`offsetMax`を独自に設定すること。</summary>
    public void ApplyBodyInset()
    {
        if (body == null) return;
        float inset = GetBodyInset();
        var rt = (RectTransform)body.transform;
        rt.offsetMin = new Vector2(inset, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-inset, rt.offsetMax.y);
    }
}
