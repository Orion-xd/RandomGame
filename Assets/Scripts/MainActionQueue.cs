using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// メインアクションの予定を管理するキュー。アクションバーUIの元データ。
/// index 0 が「次に発動するアクション」。消費すると全体が繰り上がり、
/// 末尾に新しい抽選結果が1つ追加される（テトリスのNEXT表示と同じ挙動）。
/// </summary>
public class MainActionQueue : MonoBehaviour
{
    [Tooltip("先読み表示する手数")]
    [SerializeField] private int slotCount = 4;

    [Tooltip("抽選対象。重み付けしたいときは同じ値を複数入れる")]
    [SerializeField]
    private MainActionType[] lottery =
    {
        MainActionType.Jump,
        MainActionType.Dash,
        MainActionType.Attack,
    };

    private readonly List<MainActionType> _slots = new List<MainActionType>();

    /// <summary>キュー内容が変化したときに発火（UI更新用）。</summary>
    public event Action OnChanged;

    public int SlotCount => slotCount;

    private void Awake()
    {
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
        return lottery[UnityEngine.Random.Range(0, lottery.Length)];
    }
}
