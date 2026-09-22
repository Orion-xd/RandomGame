using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// チュートリアルの「そのカテゴリのヒントをもう習得したか」をステージ単位で一元管理する（2026-09-22）。
/// カテゴリ単位（個々のオブジェクト単位ではない）で管理する: 例えば複数の敵に DashPastEnemy を
/// 割り当てていても、そのうちどれか1体ででも成功すれば、以後は全ての DashPastEnemy ヒントが
/// 二度と表示されなくなる。ステージシーンに1つ置く。
/// </summary>
public class TutorialProgress : MonoBehaviour
{
    private readonly HashSet<TutorialHintCategory> _learned = new();

    public bool IsLearned(TutorialHintCategory category) => _learned.Contains(category);

    public void MarkLearned(TutorialHintCategory category) => _learned.Add(category);
}
