using UnityEngine;

/// <summary>
/// プロローグ用シーン（"Prologue"）に置く。ゲーム開始時（タイトルの「ゲームスタート」→ここ）に
/// プロローグ会話を再生し、読み終わったらステージ選択画面へ進む。
///
/// 会話データは既定で StageSet.asset の prologue を使う（sequenceOverride を入れればそちら優先）。
/// すでに既読（GameFlow.HasSeenPrologue）/ 会話が空 / DialoguePlayer が無い場合は、
/// そのままステージ選択へ抜ける。読み終えると既読フラグを立てる。
/// ※ 通常は GameFlow.StartGame() 側で既読ならこのシーン自体を読み込まないが、
///    直接このシーンを Play したときの保険としてここでも既読チェックする。
/// </summary>
public class PrologueRunner : MonoBehaviour
{
    [Tooltip("空なら Resources/StageSet.asset の prologue を使う")]
    [SerializeField] private DialogueSequence sequenceOverride;

    [Tooltip("空なら同シーンから自動取得")]
    [SerializeField] private DialoguePlayer player;

    private void Start()
    {
        if (GameFlow.HasSeenPrologue)
        {
            GameFlow.GoStageSelect();
            return;
        }

        var seq = sequenceOverride != null
            ? sequenceOverride
            : (GameFlow.Stages != null ? GameFlow.Stages.prologue : null);

        if (player == null) player = FindAnyObjectByType<DialoguePlayer>();

        if (player == null || seq == null || seq.Count == 0)
        {
            GameFlow.MarkPrologueSeen();
            GameFlow.GoStageSelect();
            return;
        }

        player.Play(seq, OnPrologueFinished);
    }

    private void OnPrologueFinished()
    {
        GameFlow.MarkPrologueSeen();
        GameFlow.GoStageSelect();
    }
}
