using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// メインアクションの予定を管理するキュー。アクションバーUIの元データ。
/// index 0 が「次に発動するアクション」。消費すると全体が繰り上がり、
/// 末尾に新しい抽選結果が1つ追加される（テトリスのNEXT表示と同じ挙動）。
///
/// 仕様：アクションの並びはランダムではなく、ステージごとに固定（同じステージなら
/// ゲームオーバーしてやり直しても毎回同じ並びになる）。これは stageSeed を種にした
/// 専用の乱数列（System.Random）で生成することで実現している
/// （UnityEngine.Random は他の抽選处理ともグローバル状態を共有するため使わない）。
/// また、連続して同じアクションが並ばないように、直前と同じ結果が出たら振り直す。
///
/// 抽選対象は `StageSet.allowedActions`（そのステージに設定があれば）で上書きされる。
/// ステージ1のように抽選対象が1種類（Dash のみ）の場合、「連続禁止」は自動的に無効化される
/// （振り直しループが `lottery.Length > 1` 条件でスキップされるため、無限ループにならない）。
/// </summary>
public class MainActionQueue : MonoBehaviour
{
    [Tooltip("先読み表示する手数")]
    [SerializeField] private int slotCount = 4;

    [Tooltip("このステージのアクション列を決める種。同じ値なら何度やり直しても同じ並びになる")]
    [SerializeField] private int stageSeed = 12345;

    [Tooltip("抽選対象（既定）。重み付けしたいときは同じ値を複数入れる。" +
             "StageSet.allowedActions がそのステージに設定されていれば、そちらで上書きされる")]
    [SerializeField]
    private MainActionType[] lottery =
    {
        MainActionType.Jump,
        MainActionType.Dash,
        MainActionType.Attack,
    };

    private readonly List<MainActionType> _slots = new List<MainActionType>();
    private System.Random _rng;
    private MainActionType? _lastRolled;

    /// <summary>キュー内容が変化したときに発火（UI更新用）。</summary>
    public event Action OnChanged;

    public int SlotCount => slotCount;

    private void Awake()
    {
        // ステージごとに使えるアクションが違う場合は、抽選対象を差し替える。
        // （StageSet.allowedActions が未設定なら、シーンの lottery をそのまま使う）
        var allowed = GameFlow.Stages != null
            ? GameFlow.Stages.AllowedActionsAt(GameFlow.ActiveStageIndex)
            : null;
        if (allowed != null && allowed.Length > 0) lottery = allowed;

        // 毎回 stageSeed から作り直すので、リトライしても同じ並びが再現される。
        _rng = new System.Random(stageSeed);
        _lastRolled = null;

        _slots.Clear();
        for (int i = 0; i < slotCount; i++) _slots.Add(Roll());
    }

    private void Start()
    {
        OnChanged?.Invoke();
    }

    /// <summary>index 番目（0 = 次）の予定を覗く。範囲外なら null。</summary>
    public MainActionType? Peek(int index)
    {
        if (index < 0 || index >= _slots.Count) return null;
        return _slots[index];
    }

    /// <summary>先頭を1つ消費して返す。全体を繰り上げ、末尾に新規抽選を追加する。</summary>
    public MainActionType Consume()
    {
        MainActionType head = _slots[0];
        _slots.RemoveAt(0);
        _slots.Add(Roll());
        OnChanged?.Invoke();
        return head;
    }

    private MainActionType Roll()
    {
        if (lottery == null || lottery.Length == 0) return MainActionType.Jump; // 未設定時のフォールバック

        MainActionType result = lottery[_rng.Next(lottery.Length)];

        // 直前と同じ結果は振り直す（連続禁止）。
        // ただし lottery が実質1種類だと永久に抜けられないので、試行回数に上限を設ける
        // （超えたら連続を許容する。重み付け目的で同じ値を複数入れても固まらないように）。
        for (int i = 0; i < 20 && lottery.Length > 1 && result == _lastRolled; i++)
        {
            result = lottery[_rng.Next(lottery.Length)];
        }

        _lastRolled = result;
        return result;
    }
}
