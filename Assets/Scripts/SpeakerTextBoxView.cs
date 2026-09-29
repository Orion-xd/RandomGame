using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 話者アイコン・名前欄・本文を持つテキストボックスの「見た目」だけを担当するビュー。
/// ストーリー会話（`DialoguePlayer`）とチュートリアルヒント（`TutorialHintUI`）は、両方とも
/// この同じプレハブ（`Assets/Prefabs/UI/SpeakerTextBox.prefab`）のインスタンスを使う。
///
/// アイコンの位置・サイズ、名前欄の位置・サイズ・色・フォント、本文のフォントサイズ・左右マージン、
/// 箱の背景色――こういった「見た目」を調整したいときは、必ずこのプレハブ自身を直接編集する
/// （Prefab Mode でダブルクリックして開けば、ゲームを実行しなくてもそのまま実際のレイアウトが
/// 表示される）。本文の左右マージンは、以前はアイコンのサイズから自動計算していたが、
/// インスペクターで見ながら直接調整したいという要望により、Bodyの`offsetMin`/`offsetMax`に
/// 直接設定した値をそのまま使う方式に変更した（呼び出し側はその値を一度だけ読み取って使い回す）。
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

    /// <summary>話者アイコン・名前欄の表示内容を反映する（本文の余白はこのプレハブのBodyに直接
    /// 設定されている値をそのまま使うので、ここでは一切触らない）。
    /// profile が null（ナレーション扱い）なら、アイコン・名前欄とも非表示にする。
    /// expression はそのキャラクターに用意が無ければ Normal のアイコンに自動でフォールバックする。</summary>
    public void SetSpeaker(SpeakerRegistry.Profile profile, SpeakerExpression expression = SpeakerExpression.Normal)
    {
        Sprite icon = profile?.GetIcon(expression);
        bool hasIcon = icon != null;
        bool hasName = profile != null && !string.IsNullOrEmpty(profile.displayName);

        if (speakerIcon != null)
        {
            speakerIcon.enabled = hasIcon;
            if (hasIcon) speakerIcon.sprite = icon;
        }
        if (speakerName != null)
        {
            speakerName.enabled = hasName;
            if (hasName) speakerName.text = profile.displayName;
        }
    }
}
