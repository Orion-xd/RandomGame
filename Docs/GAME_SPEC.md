# RandomGame 仕様・実装まとめ

最終更新: 2026-09-12 / 対象ブランチ: `feature/action-assist`（地面・高台の Tilemap 化、日本語フォント対応は `feature/tilemap` ブランチで実施済み・マージ済み。Player/Enemy/Goal の Prefab 化は §15）
Unity 6000.3.11f1 / URP / 2D / 入力は **新 Input System のみ**（`Input.GetAxis` は不可、`UnityEngine.InputSystem.Keyboard.current` を使う）

このドキュメントは「後日、続きの作業をするとき」に現状を把握するためのもの。
プレイ可能なプロトタイプ（移動・メインアクション・敵・地形・体力）まで実装済み。ゲームの一連の流れ（タイトル→ステージ選択→リザルト）は未実装。

---

## 1. ゲーム全体のコンセプト（企画）

2D 横スクロールアクション。プレイヤーはステージ奥のゴールを目指す。

- **操作は2系統**
  - **通常操作**: 左右移動のみ（A/D または ←/→）。
  - **メインアクション**: スペース / エンター / テンキー Enter / 左クリック（2026-09-12、会話送りやメニュー決定と操作系を統一するため追加）。ジャンプ / ダッシュ / 攻撃 のいずれかが発動する。
    ※ 名称は必ず「メインアクション」。以前「特殊操作」と呼んでいたが変更済み。
- **体力**: カービィ風。敵に接触で 1 ダメージ、3 回でミス（→ リザルト画面。未実装）。
- **落下 = 即ミス**（未実装）。
- **ステージ数**: 全 5 ステージ（企画確定）。
- **画面遷移**: タイトル →（Game Start）→ プロローグ会話 → ステージ選択 → ステージ（初回入場時は開始会話）→ リザルト（クリア / ミス）→「次のステージ」/「リトライ」。
- **会話 / プロローグ**（2026-09-07, §9-2）: ゲーム開始時のプロローグと、各ステージ初回入場時の会話。データは `DialogueSequence`（ScriptableObject）。スペース / エンターで送る一本道。

### 企画側からの仕様確認（2026-09-05 回答ぶん。これが優先）

- **ダッシュ以外での敵接触はすり抜けない**。実体としてぶつかり、ノックバック + 1 ダメージ。すり抜けるのはダッシュ中だけ（ダッシュは無敵）。
- **地面は Tilemap で作る**（2026-09-09 に移行済み。§7 / §9-1）。1 セル = 1 ワールドユニット。仮タイル素材で実装済みで、最終的に 90×90px（PPU 90）の地面タイルに差し替える想定。**高台も 2026-09-11 に Tilemap 化済み**（§7「一方通行の高台」）、現在は Stage3 のみに配置。参考記事: https://zenn.dev/sasshi_i/articles/bcc55419a1af9d
- **ゴールに全敵撃破は不要**。雑魚は回避して進んでよい。**ボスは撃破必須**（何かが進行を塞ぐ想定）。ゴール判定自体は未実装。
- **空中ジャンプ不可**（ただし後述のコヨーテタイムとダッシュ→ジャンプ特例あり）。
- **アクションの並び**: コンボは 2 つまで。同じアクションは 2 連続で並ばない。**ステージ全体で並びは固定**（ゲームオーバーして最初からやり直しても同じ順番）。

---

## 2. メインアクション システム（中核）

関連スクリプト: `MainActionType` / `MainActionQueue` / `MainActionController` / `AttackHitbox` / `ActionBarUI`
すべて Player GameObject（`AttackHitbox` はその子、`ActionBarUI` は HUD_Canvas 側）にある。

### 2-1. アクションの並び（`MainActionQueue`）

- 先読みスロット数 `slotCount = 4`。index 0 が「次に発動するアクション」。
- 発動すると先頭を 1 つ消費し、全体が繰り上がり、末尾に 1 つ抽選して追加（テトリスの NEXT と同じ）。
- **並びは決定的**: `stageSeed`（既定 12345）を種にした専用 `System.Random` で生成。`Awake` で毎回作り直すので、リトライしても同じ並びが再現される。`UnityEngine.Random` はグローバル状態を他と共有するため使わない。
- **連続同一禁止**: 直前と同じ結果が出たら振り直す（`Roll()`）。振り直しループは `lottery.Length > 1` のときだけ回る＝**抽選対象が1種類なら振り直しはスキップ**（無限ループ・エラーなし）。上限 20 回。`lottery` 未設定時は Jump をフォールバック。
- `lottery` 配列で抽選対象を指定（同じ値を複数入れると重み付けになる）。既定は Jump / Dash / Attack 各 1。
- **ステージごとの使用可能アクション（2026-09-09）**: `StageSet.stages[i].allowedActions`（`MainActionType[]`, インスペクター）が設定されていれば、`MainActionQueue.Awake` が `lottery` をそれで**上書き**する。ステージ判定は `GameFlow.ActiveStageIndex`（アクティブシーン名 → `StageSet` の index。フロー経由でも直接 Play でも正しい）。
  - **現在の設定**: Stage1 = `Dash` のみ ／ Stage2 = `Dash`, `Attack` ／ Stage3〜5 = 3 つ全部（空でもシーン既定が 3 つなので同じ）。
  - Stage1 は抽選対象が Dash だけ → 上記のとおり連続 Dash が並ぶ（想定どおり、バグではない）。
- `event Action OnChanged` を発火 → `ActionBarUI` が購読して表示更新。

### 2-2. アクションバー UI（`ActionBarUI`, HUD_Canvas/ActionBar）

- `Slot0..3` の背景 Image 色とラベル Text を `queue.Peek(i)` で更新。色: Jump=青 / Dash=黄 / Attack=赤。

### 2-3. 各アクションの挙動（`MainActionController`）

- **ジャンプ (`DoJump`)**: `rb.linearVelocity.y = jumpForce`（既定 12）。
- **ダッシュ (`StartDash` + `FixedUpdate`)**: 発動時に向いている方向へ `dashSpeed`（既定 18）で即最高速度。継続 `dashDuration`（既定 0.5 秒）。
  - ダッシュ中は `gravityScale = 0`、Y 速度 0 固定 → **落下しない**（落とし穴・敵をまたげる）。
  - **前半**（`dashLockFraction` = 0.5 → 最初の半分）: 最高速度固定、**移動入力を完全に無視**、完全無敵。
  - **後半**: 速度はキープ。入力があれば反映。
    - 前方入力: そのまま進みつつ緩やかに減速（`dashForwardDecel` = 40 u/s²）。
    - 後方入力: **急ブレーキ**（`dashBrakeDecel` = 160 u/s²）＋**無敵解除**（`_dashInvBroken` ラッチ。一度解除したらこのダッシュ中は戻らない）。
  - `IsDashing` が true の間、`Enemy` 側がプレイヤーとの物理衝突を `Physics2D.IgnoreCollision` で無視（すり抜け）。
  - `OverridesMovement` が true の間、`PlayerController` は速度・向きを書かない（ダッシュが制御）。
- **攻撃 (`DoAttack` コルーチン + `AttackHitbox`)**: 前方に子オブジェクトの当たり判定を `attackDuration`（**0.4 秒**、2026-09-11 に 0.2→0.4 変更、全ステージ共通）だけ有効化。触れた敵に `attackDamage`（1）。同じ敵を多重ヒットしない（`HashSet<Enemy>`、有効化時にクリア）。攻撃判定はトリガー。前方距離は**専用の `forwardOffset` フィールドを廃止**し（2026-09-13）、`AttackHitbox` の `Transform.localPosition.x` の絶対値をそのまま使う方式に変更。`Configure()` は毎回その絶対値を向き（`facingSign`）に応じて符号だけ反転させる。これにより、インスペクターで Transform の X を直接編集すればそのまま前後距離の調整として反映される（Y座標・Scaleは元々スクリプトで一切触られないので常に直接編集可能）。

### 2-4. クールタイム

- **ダッシュ**: `dashCooldown` = 2 秒（固定）。
- **攻撃**: `attackCooldown` = 2 秒（固定）。
- **ジャンプ**: 時間ではなく **「着地するまで」**。着地すれば滞空時間に関係なくクールタイム終了。
  - 保険として、地面を離れてから `jumpAirCooldownCap`（3 秒）で強制解除。
  - 実装: `_jumpCdActive` フラグ + `UpdateJumpCooldown()`。発動後にまず「実際に地面を離れたか」（`_jumpCdLeftGround`）を確認 → その後 `IsGrounded` が再び true になった瞬間に解除。発動しても `JumpLiftoffGrace`（0.25 秒 const）以内に浮かなければ「着地済み」とみなして解除。
- `IsReady => Time.time >= _nextReadyTime && !_jumpCdActive`。

### 2-5. コンボ（連続発動）

- **ステージごとに無効化できる（2026-09-09）**: `StageSet.stages[i].disableCombos` が true、または `allowedActions` が実質1種類のステージでは、`MainActionController._combosEnabled = false` になる（`Awake` で `GameFlow.ActiveStageIndex` から判定）。無効時は `comboContinuation` が常に false ＝ **1 回発動したら、そのアクションのクールタイムが明けるまで次は出せない**（猶予は完全に無意味）。**現在 Stage1 が該当**（Dash のみ）。
- **受付猶予**: `comboGraceTime` = 0.8 秒。1 つ目の発動からこの秒数以内にもう一度発動すると「2 つ目」として受け付ける。**クールタイムとは完全に独立したパラメータ**。組み合わせによらず一定。
- **最大 2 連続**。2 つ目を使うと `_comboStep` が 0 に戻り、以降は通常のクールタイム待ち（3 連目の早押しはブロック）。
- **効果の合成**: 特別な合成処理はなく「2 つのアクションを続けて発動するだけ」。
  - ジャンプ + 攻撃 → ジャンプの上昇中に攻撃判定が出る。
  - ジャンプ + ダッシュ → ジャンプ直後、ダッシュが Y 速度を 0 にして水平ダッシュへ移行。
  - ダッシュ + ジャンプ → 後述の特例。
- **コンボ後のクールタイム**
  - ジャンプを**含まない**組み合わせ → 2 つ目のアクションのクールタイムだけ見ればよい（1 つ目のクールタイムは必ず先に明けるため）。実装は `_nextReadyTime` を 2 つ目のもので上書きするだけ。
  - ジャンプを**含む**組み合わせ（Dash→Jump / Jump→Dash / Jump→Attack など）→ **着地するまで**がクールタイム（上限 3 秒）。もう片方（ダッシュ / 攻撃）の時間ベースのクールタイムは無視。実装は `jumpInvolved` 判定で `StartJumpCooldown()` を呼び `_nextReadyTime = Time.time`。

### 2-6. 先行入力（バッファ）

- **`inputBufferTime` = 0.1 秒（約6フレーム。0 で無効）**。クールタイム終了のこの秒数前から、発動入力（スペース / エンター / テンキー Enter / 左クリック）を「先行入力」として記憶する。
- **入力を離していても**、クールタイムが明けた瞬間（`IsReady`）に次のアクションが自動発動する。「クールタイム明けにすぐ次を出す」操作をやりやすくするため。
- 実装（`MainActionController`）:
  - `Update()` で発動入力押下時、まず `TryTrigger()`（`void`→`bool` に変更、発動できたか返す）。**出せなかった & `InInputBufferZone`** なら `_bufferedInput = true`（`_bufferedInputExpiry = Time.time + 0.4`＝`BufferedInputMaxLife` で失効させる保険つき）。
  - 毎フレーム、`_bufferedInput` かつ `IsReady` になったら消費して `TryTrigger()`。ライブ入力で発動できたときは残っていた記憶を破棄。`OnDisable` でもクリア。
- **受付区間（`InInputBufferZone`）と CD ゲージ上の割合（`InputBufferZoneFraction01`、空側の端から測った 0..1）を公開** → `PlayerDebugBars` が色付き表示に使う（§8）。
  - **ダッシュ / 攻撃**（時間ベース）: 残り `<= inputBufferTime` で受付。割合 = `inputBufferTime / _lastCooldownDuration`（例: CD 2 秒なら 0.05）。
  - **ジャンプ**（着地ベースで時間が不定）: `PlayerController.TryPredictLandingTime()` で「着地まで `<= inputBufferTime` 秒」と予測できたときだけ受付。予測不可（上昇中・真下に地面なし）なら受け付けない。ゲージ割合は `inputBufferTime / jumpAirCooldownCap`（≈0.033）の**目安表示**にとどめる（ゲージ自体は上限基準で減るので厳密には対応しない）。
- **`PlayerController.TryPredictLandingTime(out float seconds)`**: 足元中央から真下へレイ 1 本 → 距離 `d` と `vy`・重力（`_baseGravityScale * Physics2D.gravity.y`）から `d = v0·t + ½g·t²` の正の根で着地秒数を出す簡易予測。呼ぶのは「ジャンプ CD 中かつ非上昇」のときだけなので負荷は無視できる。台の端などは誤差あり。

---

## 3. ★重要な設計判断（必ず把握しておくこと）

### 3-1. 空中ジャンプは原則不可。ただし条件付きで許容

`MainActionController.TryTrigger` のジャンプ発動チェック:
```
jumpGrounded   = _player.IsGrounded || _player.InCoyoteTime;
jumpGroundBypass = comboContinuation && next==Jump && _comboFirstAction==Dash;
if (next==Jump && !jumpGroundBypass && !jumpGrounded) return;  // 発動そのものを受け付けない
```

- **原則**: 空中では「ジャンプを発動できない」。キューも消費されず、クールタイムも発生しない（＝「発動はするが不発」ではなく「発動を受け付けない」）。地面に戻れば再挑戦できる。
- **例外 A: コヨーテタイム**（`PlayerController.InCoyoteTime`）: 地面を離れて `coyoteJumpGrace`（0.18 秒）以内なら空中でもジャンプ可。少し落下し始めていても跳べる（縁からのジャンプを寛容にする）。
- **例外 B: ダッシュ → ジャンプのコンボ**（`jumpGroundBypass`）: コンボの 1 つ目がダッシュのときだけ、**完全に空中でも**ジャンプを許可する。ダッシュで落とし穴の上に飛び出した先からジャンプで脱出できるようにするための特例。
  - このとき `DoJump` は、ダッシュがまだ継続中なら `InterruptDashMovement()` で**ダッシュの「移動」だけ中断**してから跳ぶ（放置するとダッシュの `FixedUpdate` が毎フレーム Y 速度を 0 に戻してジャンプが不発になる）。

### 3-2. ダッシュ → ジャンプでも無敵は途切れない

- 無敵は `_dashInvTimeLeft` という**専用タイマー**で管理（発動時に `dashDuration` ぶんセット。ダッシュの移動処理とは独立して毎 `FixedUpdate` 減少）。
- `IsInvincible = _dashInvTimeLeft > 0 && !_dashInvBroken`。
- ダッシュ → ジャンプのコンボでジャンプに移っても（`InterruptDashMovement` は `_dashInvTimeLeft` に触れない）、無敵は**通常のダッシュ効果時間ぶん継続**。その間の左右入力は移動に反映されるが無敵は切れない。
- **無敵が早期に切れる唯一の条件**: 「ダッシュ効果時間の後半に、後方への左右入力をした」とき（`_dashInvBroken` ラッチ）。
- 効果時間が満了すれば必ず無敵解除。

### 3-3. コンボ受付猶予 = クールタイムとは別物

過去に「受付猶予 = クールタイム」で実装していたが破棄。現在は `comboGraceTime`（0.8 秒）という独立パラメータ。

### 3-4. 落下しない猶予 と コヨーテタイム は別々の窓（2 つの独立パラメータ）

`PlayerController.FixedUpdate` 内、共通条件 `!IsGrounded && ノックバック中でない && vy <= 0.01（非上昇）`:

- `noFallGrace`（**0.1 秒**）: この間は落下しない（`gravityScale = 0` + 下向き速度を 0 にクランプ）。コンボの 2 つ目入力が少し遅れても高度を失わないようにするため。
- `coyoteJumpGrace`（**0.18 秒**、noFall より長い）: この間はジャンプを受け付ける（`InCoyoteTime = true`）。0.1〜0.18 秒の間は**普通に重力落下しているがジャンプは可**（意図的にそうしている）。
- どちらも `vy <= 0.01` 条件があるので上昇中（ジャンプの弧）には効かず、コヨーテジャンプの多重発動も起きない。
- `IsGrounded` 自体はスティッキーにしていない（すると `UpdateJumpCooldown` の離陸・着地検出が壊れる）。
- `gravityScale` はダッシュ非制御中、`PlayerController` が毎フレーム権威を持って書き戻す（元の値は `_baseGravityScale` を `Awake` で取得）。ダッシュ終了時に一瞬だけ古い値が残ることがあるが次フレームで自己修正。

---

## 4. プレイヤー（`PlayerController`, Player GameObject）

- 左右移動: `moveSpeed`（6）。速度制御（`rb.linearVelocity.x = _moveInput * moveSpeed`）。
- 向き `FacingSign`（+1/-1）: 最後に動いた方向。`SpriteRenderer.flipX` を切り替え（スプライトは右向き基準の非対称な矢印 `PlayerArrow.png`）。ダッシュ中（`OverridesMovement`）は向き固定。
- `MoveInput`（-1/0/+1）: ダッシュ中も更新され続ける（ダッシュ後半の入力反映に使う）。
- 接地判定 `IsGrounded`: 足元直下を `Physics2D.OverlapBox`（幅 = コライダー幅 × 0.9、高さ `groundCheckDistance` = 0.1）で `groundLayer`（"Ground" レイヤー = index 8）に対して判定。**加えて「上昇中でない」条件（`vy <= 0.05`）を AND する** — `OverlapBox` は `IgnoreCollision`（すり抜け設定）を無視するので、一方通行の高台を下から突き抜ける瞬間に誤検知する。その対策。
- `LastGroundedTime`: 最後に接地していた `Time.time`。ジャンプのクールタイム上限と各種猶予の基準。
- ノックバック `ApplyKnockback(velocity, duration)`: 指定速度を与え、`duration` 秒間は入力で `v.x` を上書きしない（物理に任せる）。`Enemy` が接触時に呼ぶ。
- `Rigidbody2D`: `gravityScale = 3`、`sharedMaterial = PlayerNoFriction`（摩擦 0。壁に張り付かないようにするため。移動は速度駆動なので摩擦 0 で問題なし）。
- `BoxCollider2D` 1×1（半径 0.5）。非トリガー。

---

## 5. 体力・ダメージ（`PlayerHealth` + `HealthUI`）

- `maxHealth = 3`、`invulnTime = 0.8`（被弾後の無敵時間。同じ敵に毎フレーム削られない）。
- `TakeDamage(int) -> bool`: 実際にダメージが入ったら true（`Enemy` はこれを見てノックバックするか決める）。
  - `MainActionController.IsInvincible`（ダッシュ由来）中 / i フレーム中 は無効。
- `event OnHealthChanged` → `HealthUI` が赤丸アイコン（`Circle.png`）の表示個数を更新。HUD_Canvas/HealthPanel、HP0..2。
- ミス時は現状 `Debug.Log` のみ。**リザルト遷移・落下即ミスは未実装**。

---

## 6. 敵（`Enemy` + `EnemyPatrol` + `EnemyHealthBar`）

敵キャラは3種類（Enemy1/Enemy2/Enemy3）を計画中（2026-09-13、企画側の仕様確定）。**Enemy1・Enemy2・Enemy3すべて実装済み**（2026-09-16、Enemy3=ラスボスとしてStage5に配置）。

### 6-0. 共通の`Enemy`コンポーネント（`Assets/Scripts/Enemy.cs`）

- `maxHealth`（既定 1 = 一撃）。`maxHealth >= 2` で頭上に体力ゲージ + 数値（`EnemyHealthBar`、ボス用）。
- **当たり判定は実体（非トリガー）**。`Rigidbody2D` は無い（`Ground_*` と同じ「静的コライダー」）。
- 毎 `FixedUpdate`、`Physics2D.IgnoreCollision(playerCollider, ownCollider, playerMainAction.IsDashing)` をトグル → **ダッシュ中だけすり抜け**、それ以外は物理衝突。
- `OnCollisionEnter2D` / `Stay2D` でプレイヤー本体（`other.GetComponent<PlayerHealth>()`、`InParent` にしない = 子の `AttackHitbox` に反応しない）に接触したら:
  - `PlayerHealth.TakeDamage(contactDamage=1)` → 実際に入ったら `PlayerController.ApplyKnockback`（敵の反対方向へ `knockbackSpeed`=8、上向き `knockbackUpSpeed`=4、`knockbackDuration`=0.25 秒）。
- 攻撃を受ける経路は `AttackHitbox`（トリガー）側の `OnTriggerEnter2D` → `Enemy.TakeDamage`。敵コライダーが非トリガーでも、当たった相手（AttackHitbox）がトリガーなのでトリガー通知は届く。
- 弾（`Bullet.cs`、Enemy2/Enemy3共通）も同じ考え方で実装（実体コライダー + ダッシュ中IgnoreCollision + AttackHitboxのトリガーで即破壊）。ただし弾には体力の概念が無く、`AttackHitbox`に触れたら`DestroyByAttack()`で無条件消滅する（`AttackHitbox.TryHit`は`Enemy`が見つからなければ`Bullet`を探して破壊する、という順で処理）。
- **ステージクリアの新経路（2026-09-13追加）**: `Enemy`に`clearStageOnDeath`（既定false）を追加。trueの敵は`Die()`時に`StageManager.Clear()`を直接呼ぶ。Goalオブジェクトが無いステージ（Stage4/5想定）のボス撃破クリア用。

### 6-1. Enemy1（実装済み） — 単純な左右パトロール

- スクリプト: `Enemy`（共通） + `EnemyPatrol`。**2026-09-13 に `Enemy.prefab` → `Enemy1.prefab` へリネーム**（既存のプレハブが元々このEnemy1仕様と完全一致していたため、新規実装ではなくリネームのみで対応。`EnemyBoss.prefab` はこの `Enemy1.prefab` の Prefab Variant のままGUID経由でリンク維持）。
- `EnemyPatrol`: `Rigidbody` を使わず transform を直接動かしてスポーン地点中心に左右往復（`speed` ゆっくりめ既定1.5、`range` 片側距離既定3。共にインスペクターから調整可）。進行方向に `flipX`。
- 接触ダメージ・攻撃一撃死・ジャンプで飛び越え・ダッシュですり抜け、は6-0の共通挙動そのまま（`maxHealth=1`, `contactDamage=1`, プレイヤー`attackDamage=1`で一致）。
- **2026-09-14**: `DialoguePlayer.IsPlaying`（ストーリー再生中）は`EnemyPatrol.Update()`が早期returnし、一切移動しない（理不尽な行動を防ぐため）。

### 6-2. Enemy2（実装済み、2026-09-13）— 据え置き砲台

- スクリプト: `Enemy`（共通、`EnemyPatrol`は付けない＝移動しない） + `EnemyShooter`（発射のみ担当）。プレハブ: `Assets/Prefabs/Enemy2.prefab`（紫色のSpriteRendererでEnemy1と区別）。
  - **バグ修正（2026-09-14）**: プレハブ作成時にスプライト未割り当ての状態で`BoxCollider2D`を追加したため`size=(0,0)`になっており、プレイヤーの攻撃（AttackHitbox）が一切当たらなかった。`(1,1)`に修正済み（Stage3/Stage4のインスタンス側でコライダーを上書きしていなかったため、プレハブ修正だけで両方に反映された）。
- `EnemyShooter`: `fireInterval`秒ごとに`Bullet.prefab`を`firePoint`（未設定なら自身の位置）から発射。`homingOnFire`（既定true）がtrueなら発射位置からプレイヤー方向へのベクトルを発射時に一度だけ計算しその方向へ直進。falseなら**上下は狙わず、プレイヤーが左右どちらにいるかだけを見て`Vector2.left`/`Vector2.right`へ水平に発射**（2026-09-14修正。以前はプレイヤーの位置によらず常に`transform.right`固定で右にしか飛ばなかったバグがあった）。プレイヤーが見つからない場合のみ`transform.right`にフォールバック。`bulletSpeed`・`fireInterval`・`homingOnFire`はいずれもインスペクターから調整可。
- `Bullet.cs`: `Configure(direction, speed, damage)`で方向・速度・威力を受け取り、毎フレーム`transform.position`を直進させる。**当たり判定はトリガー**（`Awake()`で`isTrigger = true`を強制。2026-09-17に非トリガーから変更、理由は下記バグ修正参照）。プレイヤー本体に触れるとダメージ（既定1）+ 弾自身は消滅、ダッシュ中は`Physics2D.IgnoreCollision`ですり抜け（Enemyと同じFixedUpdateパターン。トリガーでも`IgnoreCollision`は効く）、Ground レイヤーのコライダーに触れると消滅、`maxLifetime`（既定6秒）経過でも自動消滅（画面外に飛び続けて残留するのを防止。仕様上の要求ではなく安全策として追加）。**2026-09-17**: この時間経過による自動消滅は**追尾弾（`homingTurnSpeed > 0`）には適用しない**よう`Start()`で分岐した。追尾弾は「プレイヤーに命中／地面に接触／ボスが攻撃を受ける」のいずれかが起こるまで永久に飛び続ける仕様のため（Enemy2の直進弾・Enemy3の②放射弾は従来通り`maxLifetime`で消える）。
  - **一連のバグ修正（2026-09-17、地面すり抜け→トリガー化で最終解決）**: 「地面に接触しても消えず、そのまま地面の中を飛んでいく」という不具合をユーザーが発見（デバッグログで`HandleCollision`自体が呼ばれていないことまで特定してもらった）。当初の原因は`Bullet`に`Rigidbody2D`が一切付いていなかったこと。**非トリガーの物理衝突**はUnityのPhysics2Dでは`Rigidbody2D`の`BodyType`の組み合わせで判定の有無が決まり、`Dynamic`が絡まない組み合わせ（Static-Static, Static-Kinematic, Kinematic-Kinematic）は衝突イベントが一切発生しない。`Rigidbody2D`が無いコライダーは実質Staticとして扱われるため、地面（`Static`）と弾（Rigidbody2D無し＝実質Static）の組み合わせでは`OnCollisionEnter2D`/`Stay2D`が発火しなかった（プレイヤーへの命中は、プレイヤー側が`Dynamic`のため正常に動いていた）。
    - 1回目の修正で`Rigidbody2D`（Body Type = Kinematic）を追加したが、**Kinematic vs Staticも衝突判定が発生しない組み合わせ**であるため、これでも直っていなかった（ユーザーが実機で確認して発覚）。
    - 2回目の修正で`Rigidbody2D`を**Dynamic**（`gravityScale=0`）に変更し、移動も`linearVelocity`経由にしたところ、今度は「発射直後、弾が生成された位置から動かなくなる」という新しい不具合が発生。原因は、弾が発射元の敵自身の位置（＝当たり判定が重なった状態）で生成されるため、Dynamic化によって**発射元自身の当たり判定と物理的に衝突**してしまっていたこと（発射元の敵は`Rigidbody2D`が無い＝実質Staticで、Dynamic vs Staticは衝突判定される組み合わせのため）。
    - **3回目の修正（採用・最終形）**: ユーザーの指摘により、そもそも弾は「プレイヤーを物理的に押し返す必要が無く、接触の検知だけできればよい」ため、**`isTrigger = true`にする**のが正しい解決だったと判明。トリガー判定は「どちらか一方がトリガーで、どちらかの側に`Rigidbody2D`があればよい」という、非トリガーの物理衝突よりずっと緩いルールで発生するため、`BodyType`の組み合わせを一切気にする必要が無くなる。`transform.position`直接書き換えの単純な移動方法に戻し、`OnCollisionEnter2D`/`Stay2D`を`OnTriggerEnter2D`/`Stay2D`に変更。発射元との衝突が問題にならなくなったため、2回目の修正で追加した`IgnoreCollisionWithShooter`は不要になり削除（`HandleTrigger`内の「`Enemy`に触れたら何もせず貫通する」という分岐だけで、発射元を含むあらゆる敵をすり抜けるようになる）。
    - **`Rigidbody2D`の要否について（2026-09-17、ユーザーが実機で最終確認）**: 一旦`Bullet.prefab`から`Rigidbody2D`を削除したが、それだと地面接触の判定が働かず、弾が地面をすり抜けたままだった（ユーザーが発見）。トリガー判定の「どちらかの側に`Rigidbody2D`があればよい」という条件を満たすため、**`Bullet.prefab`に`Rigidbody2D`（Body Type = Kinematic）を付け直す必要がある**とユーザーが判断し、実際に付け直したことで正しく動作するようになった。それ以外（`isTrigger = true`、`transform.position`直接移動、`IgnoreCollisionWithShooter`を使わない構成）はそのままで問題なし。
- 敵本体（`Enemy`側）の接触ダメージ／攻撃一撃死／ジャンプ飛び越え／ダッシュすり抜けは6-0の共通挙動そのまま。
- **2026-09-14**: `EnemyShooter`は`DialoguePlayer.IsPlaying`中はタイマーの加算ごと止まる（ストーリー中は発射しない）。また`SpriteRenderer.isVisible`（＝いずれかのカメラに映っているか、Unity標準のカリング判定）が false のときは発射をスキップする（画面外からの理不尽な弾を防ぐ）。どちらもタイマー自体は発射のたびに0にリセットされる通常のロジックのままなので、条件を満たさない間は単に「その回の発射を見送る」だけで、条件が揃った次の周期でまた判定される。

### 6-3. Enemy3（実装済み、2026-09-16）— 3行動のラスボス

- スクリプト: `Enemy`（共通、`EnemyPatrol`は付けない） + `Enemy3AI`（移動＋発射の状態機械）。プレハブ: `Assets/Prefabs/Enemy3.prefab`（`EnemyBoss.prefab`の構造を流用して作成＝`HealthBar`子をそのまま持つ。`maxHealth=8`、`contactDamage=1`、`clearStageOnDeath=true`。SpriteRendererは黒に近い暗赤 `(0.15, 0.05, 0.08)` でEnemy1/2と区別）。
- `Enemy3AI`の状態機械（`State` enum: `Retreating` / `CoolingDown` / `WaitingForHomingBullet`）:
  - **①離脱移動（`Retreating`）**: `moveSpeed`でプレイヤーと反対方向へ移動。プレイヤーとの距離が`retreatDistance`（既定5）以上になったら終了。**2026-09-17、ユーザー修正**: `SpriteRenderer.isVisible`（画面外）判定は`UpdateRetreating()`の一番最初で行うよう変更（画面外にいる間は移動そのものも一切行わない。以前は距離判定の後、発射の可否だけを画面外判定していたため、画面外にいる間もずっと移動し続け、ボスが気づかないうちに遠くまで移動してしまう不具合があった）。画面外の間は移動も攻撃選択もせず、その場で待機する。visibleに戻ったら再開し、距離条件を満たしていれば`radialChance`（既定0.5、**2026-09-17追加、インスペクターで調整可**）の確率で②、残りの確率で③を発動する（以前はコード中に`0.5f`を直接埋め込んでいた）。
  - **②放射弾**: `radialBulletCount`（既定8）個の弾を、`360°/個数`で均等な角度に同時発射（ホーミング無し、`Bullet.Configure`のhomingパラメータ省略＝0のまま）。発射した瞬間に`radialCooldown`（既定3秒）の`CoolingDown`へ。
  - **③追尾弾（`WaitingForHomingBullet`）**: 発射直後のプレイヤー方向へ`Bullet`を1発発射し、`Bullet.Configure`の第4引数`homingTurnSpeed`（既定90度/秒＝ホーミングの強度）を渡して継続追尾を有効化。発射した弾への参照を保持し、それが破壊される（`== null`になる）まで`WaitingForHomingBullet`のまま何もしない。破壊された瞬間に`homingCooldown`（既定3秒）の`CoolingDown`へ移行する（弾が生きている間はクールタイムが進まないという仕様通り）。
    - **2026-09-16追加**: ボス自身がプレイヤーの攻撃を受けた瞬間、追尾弾を発射中であれば**その弾を問答無用で消滅させ、即座にクールタイムへ移行する**。`Enemy.cs`に`public event Action OnDamaged`を追加し（`TakeDamage`で体力が実際に減るたびに発火）、`Enemy3AI`がこれを購読して`_state == WaitingForHomingBullet`のときだけ弾を`Destroy`してクールタイムへ切り替える。それ以外の状態（離脱移動中・クールタイム中）で攻撃を受けても、この処理は何もしない（ダメージ自体は6-0の通常経路でそのまま入る）。
    - **2026-09-17**: 追尾弾は`Bullet.maxLifetime`による時間経過での自動消滅の対象から外した。**「プレイヤーに命中／地面に接触／ボスが攻撃を受ける」の3つのいずれかが起こるまで永久に飛び続ける**仕様（`Bullet.Start()`で`homingTurnSpeed > 0`のときは`Destroy(gameObject, maxLifetime)`を呼ばないよう分岐）。
  - **`CoolingDown`**: タイマーが0になったら`Retreating`へ戻り、①からループする。
  - `DialoguePlayer.IsPlaying`中は`Update()`が早期returnし、状態機械ごと完全に停止する（Enemy1/2と同じ理不尽防止ルール）。
- **`Bullet.cs`の拡張（2026-09-16）**: `Configure(direction, speed, damage, homingTurnSpeedDegPerSec = 0)`に第4引数を追加。0（既定、Enemy2はこのまま）なら従来通り発射時の方向に直進するだけ。0より大きいと、`Update()`毎に`Vector3.RotateTowards`で現在の進行方向をプレイヤー方向へ最大`homingTurnSpeedDegPerSec`度/秒だけ回転させ続ける「継続ホーミング」になる（＝仕様の「ホーミングし続ける弾」「ホーミングの強度」に対応）。Enemy2の弾は第4引数を渡さないため影響を受けない。
- 弾は地面/壁（壁は未実装）に当たると消滅する。既存の`Bullet.cs`の地面判定・攻撃での即破壊・ダッシュすり抜けをそのまま利用（6-0参照）。
- 敵本体の接触ダメージ／攻撃1発＝1ダメージ（一撃死ではない）／ジャンプ飛び越え／ダッシュすり抜けは6-0の共通挙動そのまま。頭上体力バーは`EnemyHealthBar`（`EnemyBoss.prefab`と同じ仕組み）。
- Play で検証済み: 放射弾は指定個数ぶん均等な8方向（45度間隔）に飛ぶこと、追尾弾は実際にプレイヤー方向へ`RotateTowards`で旋回すること、体力0でボスを倒すと`clearStageOnDeath`経由でクリアパネルが表示される（`Time.timeScale=0`になる）ことを確認。
- **未指定だった数値はこちらで判断して実装**（後からInspectorで自由に調整可能）: 最大体力8、離脱移動速度2、離脱終了距離5、②③のクールタイムは各3秒、放射弾8個、ホーミング旋回速度90度/秒、弾速5。ゲームバランスとして違和感があれば調整してほしい。

---

## 7. ステージ要素

- **地面（Tilemap, 2026-09-09）**: 各ステージシーンに `Grid`（cell size 1×1）＋子 `Ground`（layer=Ground）。`Ground` に `Tilemap` / `TilemapRenderer`（sortingOrder -10）/ `Rigidbody2D`(Static) / `TilemapCollider2D`（`compositeOperation = Merge`）/ `CompositeCollider2D`（`geometryType = Polygons`）/ **`TilemapColliderBootstrap`**（後述）。タイル 1 個 = 1 ワールドユニット。
  - **`TilemapColliderBootstrap`（`Ground` に付ける・必須）**: eval で `SetTile` して作った Tilemap は Play 開始時にコライダー形状を生成せず（`CompositeCollider2D.pathCount = 0` のまま＝**地面がすり抜けて落下する**）、`Awake` でタイルを一括で貼り直して（`GetTilesBlock` → `ClearAllTiles` → `SetTilesBlock` → `ProcessTilemapChanges` → `GenerateGeometry`）形状の再生成を促す。これが無いと 5 シーンとも地面に当たり判定が付かない。通常のタイルパレットで塗ったマップなら不要。
  - タイルアセット: `Assets/Art/Tiles/GroundTile.asset`（`UnityEngine.Tilemaps.Tile`、`colliderType = Grid`、sprite = `Assets/Art/GroundTile.png`）。仮素材。**最終的に GroundTile.png を 90×90px の本番絵で上書きし、PPU を 90 に保てば 1 セル = 1 ユニットのまま差し替わる**（`Assets/Art/GroundTile.png` の現状: 90×90 の茶色ベタ＋縁＋斑点、Sprite / Single / PPU 90 / Point / 無圧縮 / FullRect / pivot Center）。
  - **塗り範囲**: 天面 y=-2（＝セル行 y=-3 が一番上、そこから y=-8 まで 6 行）。
    - **Stage1, Stage3**（2026-09-11、Stage3 も Stage1 と同一構成に変更）: x セル [-19,7) と [10,30) を塗り、x セル 7〜9 を空にして **落とし穴（x≈7〜10、幅 3）**。`CompositeCollider2D.pathCount = 2`（左右で分離）。
    - **Stage2, Stage4, Stage5**: x セル [-19,27) を連続で塗り、落とし穴なし。`pathCount = 1`。
  - `PlayerController.IsGrounded` は `CompositeCollider2D` を `Physics2D.OverlapBox` で検出できる（Play で確認済み: Stage1 は左地面/穴/右地面、Stage2〜5 は連続、天面 y=-2）。
  - **タイルパレット（`Assets/Tilemaps/Palettes/GroundPalette.prefab`, 2026-09-11）**: `Window > 2D > Tile Palette` で開いて手作業編集するための Unity 標準パレット。`GroundTile` と高台の 6 タイル（下記）を収録済み。使い方: シーンを開く → Tile Palette ウィンドウで `GroundPalette` を選択 → Active Tilemap がそのシーンの対象 Tilemap（`Grid/Ground` または `Grid/HighGround`）になっていることを確認 → Paint/Erase/Box Fill 等でシーンビュー上を直接編集 → Ctrl+S で保存。当たり判定は Play 開始時に `TilemapColliderBootstrap` が自動で作り直すので、手で塗っても特別な後処理は不要。
- **一方通行の高台＝HighGround（Tilemap 版, 2026-09-11）**: `Grid` の子 `HighGround`（`Ground` と同じ Grid・同じ 1×1 セル。現在 Stage3 に1基、Stage4 に2基。Stage1 の旧 GameObject 版は削除済み、Stage2/5 はもともと無し）。**「高台」の英訳が"high ground"であるため、2026-09-16にGameObject・アセット名を`Platform`系から`HighGround`系へ統一した**（経緯は §9-1 Stage4 参照）。
  - **見た目**: 天面（乗れる面）3 種＋柱（乗れない・当たり判定も無い）3 種、計 6 枚のタイルで構成。実際の並びは天面 左/中央/右 の 3 マス＋その真下に柱 左/中央/右 の 3 マスの計 3×2 マス。柱は地面の天面（y=-2）にちょうど接し、「地面から生えた柱の上に台がある」見た目になる。左右は端用、中央は繰り返し用の想定（今は仮素材のため天面 3 種・柱 3 種はそれぞれほぼ同じ見た目で左右にわずかな縁のアクセントがある程度だが、本番素材に差し替えれば区別できるようになる設計）。
    - タイル: `Assets/Art/Tiles/HighGroundTopLeft` / `HighGroundTopCenter` / `HighGroundTopRight`（`colliderType = Grid`）、`HighGroundPillarLeft` / `HighGroundPillarCenter` / `HighGroundPillarRight`（`colliderType = None`）。元画像は `Assets/Art/HighGroundTop*.png` / `HighGroundPillar*.png`（90×90, PPU90 の仮素材。天面はオパーク、柱は半透明のグレー＝当たり判定が無いことを視覚的に示す仮の意匠）。
  - **当たり判定**: `HighGround` の `TilemapCollider2D`(`compositeOperation=Merge`) + `CompositeCollider2D` は `colliderType=None` の柱タイルからは形状を作らないため、**天面タイルだけが合成された 1 つの当たり判定**になる（柱部分は完全にすり抜け＝当たり判定自体が存在しない。天面部分は実体の当たり判定）。
  - 一方通行のロジックは `OneWayPlatform` コンポーネントをそのまま流用（`HighGround` GameObject に付ける）。下から上へは常にすり抜け。上から下へは抜けられない（着地できる）。
  - ただし **プレイヤーの横幅のうち `requiredOverlap`（0.5、インスペクター調整可）以上が天面に重なっている**ときだけ着地判定を有効化。端に少し引っかかっただけでは乗れない。
  - 実装は `PlatformEffector2D` ではなく、毎 `FixedUpdate` で `Physics2D.IgnoreCollision(player, platform, !solid)` をトグル。`solid = 足が天面より上（`topTolerance` 0.05） && 下降中 && 重なり率 >= requiredOverlap`。単体 BoxCollider2D でも Tilemap の CompositeCollider2D でも動くよう、`Awake` は `CompositeCollider2D` を優先して `_col` に採用する（2026-09-11 追加）。
  - **端の引っかかり不具合（2026-09-16、対応を試みたが未解決、コードは元に戻した）**: 高台の端ギリギリで降りるときに一瞬引っかかる不具合をユーザーが報告。以下2案を実装しては撤回した。
    1. 乗る条件（`requiredOverlap`）と外れる条件（より小さい`releaseOverlap`）を分けるヒステリシス方式 → Playでの確認では意図通りの状態遷移をしていたが、実際のゲームプレイでは解消しなかった。
    2. 「横に外れる／沈み込みすぎて離れたら、他のGroundレイヤーの何かに触れるまで完全に乗れなくする」ロック方式（ジャンプでその場を離れるだけの場合は対象外にする分岐つき） → こちらも解消しなかった。
    - **2026-09-16、ユーザーの指示で`OneWayPlatform.cs`は直前のコミット（`0341de48`）の状態へ`git checkout`で復元済み**（＝上記いずれの変更も残っていない、元の単一閾値のみのロジック）。この不具合自体は未解決のまま。次に着手するときは、上記2案がどちらも効かなかったという前提から検討し直す必要がある（詳細はメモリの`tilemap-platform-oneway`参照）。
  - `overlapX = min(右端どうし) - max(左端どうし)`、重なり率 = `overlapX / プレイヤー横幅`。ソース内に具体例つきの長いコメントあり。
  - Play で確認済み: `CompositeCollider2D.pathCount=1`（天面3マス分が1つに合成、bounds が天面3マス分の範囲と一致）、柱範囲は `OverlapBox` で完全に無反応、着地条件を満たすと `IgnoreCollision` が解除されソリッドになる（横に外れる／下から上昇中は再びすり抜け）ことを確認。

  ### HighGround（高台）の使い方ガイド（他プランナー向け、2026-09-16）

  **① 既存のHighGroundの高さ・形を調整したいだけの場合**
  シーンを開く → `Window > 2D > Tile Palette` で `GroundPalette` を選択 → Active Tilemap を調整したい`HighGround`（or `HighGround2`など）に切り替える → あとは`Ground`と全く同じ感覚で、Paint/Eraseでシーンビュー上を直接編集するだけでよい。**プレハブなどを意識する必要は無い。**
  **ただし「調整」は、あくまで今ある1つの塊（ひとつながりのタイル）の形・高さを変えることを指す。** 同じTilemapの離れた場所に別の塊を新しく描き足すのは②の「新しい高台」に該当する（2026-09-16、テストシーンで実際にこの混同が起きた。注意点も参照）。

  **② 全く新しい、独立した高台を追加したい場合**
  既存のどのHighGroundとも接触・隣接しない位置に新しい高台を作るときは、**専用の新しいTilemap GameObjectが必要**（理由は下記「注意点」参照）。`Assets/Prefabs/HighGroundTilemap.prefab`（当たり判定などの設定を済ませた空のTilemap）を`Grid`の子としてシーンにドラッグ＆ドロップし、分かりやすい名前（`HighGround3`など）に変える → Tile Paletteでそれをアクティブにして①と同じようにペイントする。この最初の1回だけ開発側（Claude）に頼んでもよい。

  **注意点**
  - **天面（乗れる部分）と柱（乗れない部分）は必ずセットで使う必要は無い**。柱タイルは`colliderType = None`＝当たり判定を一切持たない、純粋に見た目だけのパーツ（「地面から生えている」ように見せるための飾り）。**天面タイルだけを単独で使っても、一方通行の機能としては完全に正常に動作する**（見た目が宙に浮いて見えるだけで、機能面の問題は無い）。逆に柱タイルだけを天面無しで使うことも可能（その場合はただの、乗れない・すり抜けるだけの飾りになる）。
  - **左・中央・右のタイルに機能的な違いは無い**（天面同士、柱同士でそれぞれ`colliderType`は完全に同じ）。差は見た目だけで、タイルセットとしての繋がりを綺麗に見せるための区別（左右は端、中央は繰り返し用）。今は仮素材でほぼ見分けが付かないが、**どれをどこに置いても当たり判定は変わらない**ので、動作確認中は気にせず好きなものを使ってよい。
  - **高台1基＝Tilemap 1つを厳守**。**性質の異なる（＝離れた場所にある）高台を同じTilemapに同居させない**こと。`OneWayPlatform`はTilemap全体の当たり判定の外接矩形を基準に着地判定を計算しているため、1つのTilemapに複数の離れた高台を混在させると、この範囲が全部をまたぐ巨大な範囲になり、判定が壊れて常にすり抜けるようになる（2026-09-16にStage4で実際に発生した不具合）。
  - **高台タイルは`Ground`（普通の地面用Tilemap）には絶対に描かない**こと。`Ground`には`OneWayPlatform`が付いていないため、一方通行にならず、ただの通常ブロックになってしまう（2026-09-14・2026-09-16に実際に発生した不具合）。Tile Paletteで作業する際は、必ず「Active Tilemap」がどのGameObjectを指しているか確認すること。
  - コンポーネント名は`OneWayPlatform`のまま（GameObject名の`HighGround`とは一致しない）。Inspectorで見たときに戸惑わないよう、この対応関係も共有しておくとよい。

- **カメラ**（`CameraFollow`, Main Camera）: `Mathf.SmoothDamp`（`smoothTime` 0.15）で追従。**2026-09-13 に `followHorizontal`/`followVertical`（インスペクターのbool）を追加**し、横方向・縦方向を個別にオン/オフできるようにした（既定は横=true・縦=false＝これまで通りの横スクロール）。オフにした方向は`Start()`時点の位置で固定。Stage1〜3・5は既定のまま（横のみ）、**Stage4のみ横=false・縦=true**（縦スクロール、詳細は§9-1）。ortho size 6、位置 (0,-0.5,-10)（Stage4は別途§9-1参照）。
- **レイヤー**: user layer 8 = "Ground"。`Grid/Ground`（Tilemap）/ `Grid/HighGround`（Tilemap, Stage3 のみ）に設定。`Enemy` はわざと外している（敵の上に乗ってもジャンプが回復しないように）。

---

## 8. デバッグ機能（`PlayerDebugBars`, Player/DebugBars）

プレイヤー頭上にワールド空間のゲージ 2 本（左端固定で伸縮）+ 数値ラベル。
**開発者用**（2026-09-08）: `Awake` で `!DeveloperSettings.Active` なら `DebugBars` GameObject ごと `SetActive(false)`。＝ エディタ内で `developerMode` が true のときだけ表示。ビルドでは常に非表示（`CooldownBufferZone` も生成されない）。

- **COMBO バー（シアン）**: `MainActionController.ComboGraceFraction01`。1 つ目のアクション発動後 `comboGraceTime`（0.8 秒）かけて減少。残っている間はコンボの追加入力を受け付ける。
- **CD バー（オレンジ）**: `MainActionController.CooldownFraction01`。「これが残っている」かつ「COMBO バーが空」= アクション実行不可。
  - ダッシュ / 攻撃 → `(_nextReadyTime - Time.time) / _lastCooldownDuration`。
  - ジャンプ → 着地ベースで時間が不定なので `jumpAirCooldownCap`（3 秒）を基準に減少。接地中は満タン、離陸後は 3 秒に向けて減り、着地で 0。
- **先行入力ゾーン**（CD バーの空側の端に重ねた色付き区間）: 幅 = `MainActionController.InputBufferZoneFraction01`（最小 `minBufferZoneWidthFrac` = 4%）。`cooldownFill` の SpriteRenderer を複製したスプライトを**実行時に自動生成**（`CooldownBufferZone`、sortingOrder = fill+1。シーン編集不要）。
  - 受付前は半透明シアン（`bufferZoneIdleColor`）、**実際に受付中（`InInputBufferZone`）は明るい緑**（`bufferZoneActiveColor`）。CD ラベルに `BUF` を付す。
  - CD バーの先端がこの色付き区間に入っている ≒ 先行入力できる、という見た目。ジャンプは §2-6 のとおり区間位置は目安（受付判定は着地予測）。

---

## 9. 画面の流れ / シーン構成

### 9-0. ゲームの流れ（最小実装、2026-09-06 / プロローグ追加 2026-09-07）

`Title` →（Game Start）→ `Prologue`（プロローグ会話）→ `StageSelect` →（Stage 1〜5）→ ステージ（初回のみ開始会話）→（クリア / 失敗）→ 結果画面 → 各ボタンで遷移。

- **遷移はすべて `GameFlow`（static クラス）→ `SceneTransition.Go(シーン名)`**。`CurrentStageIndex` だけ static で保持（「次のステージへ」「もう一度」に使う）。
- **フェード遷移（`SceneTransition`, 2026-09-08）**: **どのシーン遷移でも**「暗転 → 読み込み → 明転」を行う。実行時に自動生成（`DontDestroyOnLoad`）、全面真っ黒 Image の alpha を上下させるだけ（上下左右の動きなし）。`sortingOrder 32760`（会話・結果画面より前面）、`raycastTarget=true` で演出中はマウスも遮断。
  - 時間は `Assets/Resources/SceneTransitionSettings.asset` で調整：`fadeOutSeconds`(0.5) / `fadeInSeconds`(0.5) / `postFadeInLockSeconds`(**0.25**、2026-09-12 に 0.5→0.25 へ半減)。`Time.unscaledDeltaTime` 基準（結果画面など `timeScale=0` から遷移しても動く）。
  - **明転（フェードイン）のヒッチ対策（2026-09-08）**: シーンロード直後は 1 フレームの delta が大きく荒れるため、そのままだと明転が 1〜2 フレームで終わって「いきなり明るくなる」ように見える。対策として ①ロード後に 2 フレーム捨ててから明転開始 ②`Fade` の 1 フレームあたりの進行量に上限（`max(1/60, duration*0.15)` 秒）を設ける。
  - 演出中は `SceneTransition.Transitioning == true` → `InputLock.InputAllowed` が常に false（＝すべての入力を無効化）。明転しきったあと `InputLock.LockFor(postFadeInLockSeconds)` を呼び、**さらに 0.25 秒**入力を止める（連打対策。2026-09-12 に 0.5→0.25 へ半減）。合計：暗転 0.5 ＋ 読み込み ＋ 明転 0.5 ＋ 0.25。
  - **暗転中は `Time.timeScale = 0`（2026-09-08）**: 暗転が始まってから `LoadSceneAsync` 完了までは遷移元シーンを完全停止させる。これが無いと、クリアパネルのボタンを押してから遷移するまでの短い間だけ通常進行に戻り、`CameraFollow` の追従（`SmoothDamp`）が再開して「プレイヤーは操作していないのにカメラだけ少し動く」違和感が出る。読み込み後に `timeScale = 1` に戻す（開始会話があれば `StageManager` が再度 0 にする）。フェード自体は `Time.unscaledDeltaTime` なので timeScale 0 でも進む。
  - **ステージクリア / 失敗のパネル表示はシーン遷移ではないのでフェードしない**。代わりに結果パネルの `MenuNavigation.inputLockDuration` で連打対策をしている（**2026-09-12 に 1.0→0.5 秒へ半減**、現在は他画面と同じ 0.5 秒）。この間はキーボードに加え `GraphicRaycaster` を無効化してマウスクリックも止める。
- **ステージの並び・シーン名は `StageSet`（ScriptableObject, `Assets/Resources/StageSet.asset`, 2026-09-06）に外出し**。インスペクターで `stages[]`（`displayName` + `sceneName`）を編集する。`GameFlow` が `Resources.Load<StageSet>("StageSet")` で読む（コード内にシーン名を書かない）。**ステージ追加はコード変更不要** → ①シーン作成＋Build Settings 追加 ②`StageSet.stages` に要素追加 ③StageSelect にボタン追加＋`StageSelectMenu.stageButtons` に同 index で割当。`StageSelectMenu` は `displayName` があればボタンのラベル（子 `Text`）へ反映する。
- **現在 5 ステージ**（2026-09-07 に 3 → 5 へ拡張、企画どおり）。`StageSet` に Stage1〜Stage5 を登録。
- **ビルド設定のシーン順**: `[0] Title, [1] Prologue, [2] StageSelect, [3] Stage1, [4] Stage2, [5] Stage3, [6] Stage4, [7] Stage5`（`SampleScene` は `Stage1.unity` にリネーム済み）。※シーンは名前でロードするので順番自体は動作に影響しない。
- **Title**（`Assets/Scenes/Title.unity`）: Camera + EventSystem + Canvas（"勇者しばりちゃん" タイトル + "Game Start" ボタン）+ `TitleMenu`。ボタン `onClick` → `TitleMenu.OnStartClicked()` → `GameFlow.StartGame()`（Prologue シーンが Build Settings に無ければ StageSelect へ直行するフォールバックつき）。**タイトルの `Text`（"勇者しばりちゃん"）はシーンに直接置かれた唯一の日本語 UI**（他はすべて `DialoguePlayer` が実行時生成）で、§9-2 の日本語フォント対応時に見落としていた（`m_Font` がビルトインの Arial のまま）。2026-09-11 に `NotoSansJP-Regular` を明示的に割り当てて修正済み。**シーンに直接置いた Text に日本語を入れる場合は、フォントを手動で `NotoSansJP-Regular` に差し替える必要がある**（`DialoguePlayer` 経由なら自動）。
- **Prologue**（`Assets/Scenes/Prologue.unity`, 2026-09-07）: Camera + `DialoguePlayer`（会話 UI は実行時生成）+ `PrologueRunner`。`PrologueRunner` が `StageSet.prologue` を再生し、読み終わると `GameFlow.GoStageSelect()`。会話が空なら即 StageSelect へ。
- **StageSelect**（`StageSelect.unity`）: Camera + EventSystem + Canvas（"SELECT STAGE" 見出し + `Stage1Btn`〜`Stage5Btn` + `BackButton`）+ `StageSelectMenu` + `MenuNavigation` + `DevStageClearToggles` + `DevStorySeenToggles` + `DevProgressResetButton`。各ステージボタン `onClick` → `StageSelectMenu.LoadStage(i)`（i=0..4, 永続 int リスナー）。
  - **ステージボタンの並び（2026-09-08 変更）**: **横一列**。`Stage{i}Btn` は anchor/pivot (0.5,0.5)・**230×230 の正方形**・`anchoredPosition = ((i-2) * 280, -60)`（中心そろえ・間隔 280px）。ラベルは fontSize 34。以前は 560×100 の縦 5 段だった。
  - **`BackButton`（2026-09-07）**: 画面**左下**（anchor (0,0), pivot (0.5,0.5), anchoredPos (190,80) ＝左端・下端から 40px, 300×80、Stage ボタンと同スタイル）。`onClick` → `StageSelectMenu.BackToTitle()` → `GameFlow.GoTitle()`。`MenuNavigation` のカーソル対象（ステージ行の下、リセットボタンの下）。
  - **`DevProgressResetButton`（開発者用, 2026-09-08）**: `BackButton` の少し上（`gapAboveBackButton` 16px, 300×60, 暗赤）に実行時生成する「Reset story & clear」ボタン。押すと `GameFlow.ResetStageProgress()`（全ステージのクリアフラグ＋ステージ開始会話の既読を false。**プロローグ既読は残す**）＋ 画面上の全チェックボックス（`DevStageClearToggles.ResetAll()` / `DevStorySeenToggles.ResetAll()`）の見た目もリセット ＋ `RefreshLocks()`。表示条件は `DeveloperSettings.Active`。生成後 `MenuNavigation.AddButton()` で**カーソル対象に登録**（ステージ行の下、`BackButton` の上）。
- **ステージ解放（2026-09-06）**: 最初は Stage1 のみ。あるステージをクリアすると次が解放される。
  - クリア状況は**ステージごとの bool**（`GameFlow`、`PlayerPrefs` キー `RandomGame.ClearedStagesMask` にビットマスクで保存＝アプリ再起動後も維持）。`IsStageCleared(i)` / `SetStageCleared(i,b)` / `MarkStageCleared(i)`（= Set true。クリア時 `StageManager.Clear()` から呼ぶ）/ `ResetProgress()`（テスト用に全消去）。
  - **解放判定**: `IsStageUnlocked(i)` = `i==0 || IsStageCleared(i-1)`。`UnlockedStageIndex` は「連続して解放されている最大 index」（表示・カーソル初期位置の目安、派生値）。
  - `GameFlow.LoadStage` はロック中の index を無視する（多重防御）。
  - `StageSelectMenu`（`stageButtons[]` を index 順に割当、`StageButtons` で公開）が `Start`/`RefreshLocks()` で未解放ステージのボタンを `interactable = false` にする（今は Unity 既定のグレーアウト表示のみ）。
- **開発者用クリア状況トグル（`DevStageClearToggles`, StageSelect/Canvas, 2026-09-06）**: 各ステージボタンの左隣に、実行時生成のチェックボックス（uGUI `Toggle`）を5つ出す。列の一番上に "clear" ラベル1つ（2026-09-07、`DevStorySeenToggles` と同方式）。
  - チェック = そのステージがクリア済み（`GameFlow.SetStageCleared`）。クリア済みステージは自動でチェック済み。開発者が自由に付け外しでき、変更で即 `RefreshLocks()`。
  - 「Stage1 と 3 だけチェック」のような非現実的状態も許容（整合はとらない。トグルは `IsStageCleared` を素直に反映、ボタン解放は `IsStageUnlocked` ルール由来なので、その場合 Stage3 は「チェック済みだがロック」になる）。
  - **表示条件は `DeveloperSettings.Active`** ＝「エディタ内 かつ `Assets/Resources/DeveloperSettings.asset` の `developerMode == true`」（2026-09-08：ScriptableObject 化。インスペクターでその 1 アセットの bool を切り替えるだけ、再コンパイル不要）。エディタ外ビルドでは値に関係なく常に無効（`Active` が `#if UNITY_EDITOR` ガード）。開発者用3コンポーネント（`DevStageClearToggles` / `DevStorySeenToggles` / `DevProgressResetButton`）はこの1スイッチだけに従う。プレイヤーがクリア状況を書き換える経路はここだけ。
- **ビルド版の初回起動リセット（`FreshBuildGuard`, 2026-09-07）**: エディタ**外**のビルドで、起動時（`RuntimeInitializeOnLoadMethod` / `BeforeSceneLoad`）にスタンプ文字列を `PlayerPrefs` キー `RandomGame.BuildStamp` と照合し、違えば `GameFlow.ResetProgress()` ＋記録し直す。**エディタ内は無効**（`#if UNITY_EDITOR`）。unityroom（WebGL）想定＝PlayerPrefs はブラウザの IndexedDB にページ URL 単位で保存。
  - スタンプの作り方は `FreshBuildGuard.Policy`（コード内 `const`）で切り替える:
    - `OnEveryBuild`（**現在の設定**・開発用）: スタンプ = `"build:" + Application.buildGUID`（buildGUID は毎ビルド自動採番）→ **ビルドし直すたびに全プレイヤーの進行が消える**。開発中の PlayerPrefs 残りがビルドに紛れ込む事故を防ぐのが目的。
    - `OnTokenChange`（リリース用）: スタンプ = `"token:" + ResetToken`（コード内 `const` 文字列）→ **バージョン更新・ビルドし直しでは消えない**。`ResetToken` を書き換えたときだけ次回起動で1回リセット。
    - `Disabled`: 自動リセットなし。
  - **事故防止**: `Policy = OnEveryBuild` のまま **Development Build 以外**をビルドしようとすると、`FreshBuildGuardBuildCheck`（`IPreprocessBuildWithReport`, `Assets/Scripts/Editor/`）が確認ダイアログを出してビルドを止める（バッチモードでは `BuildFailedException`）。「毎ビルド全消し」仕様を忘れたまま配布するのを防ぐ。リリース時は `Policy` を `OnTokenChange` / `Disabled` に変える。
- **ボタンの `onClick` はすべて永続 UnityEvent リスナー**（Inspector に表示される。`UnityEventTools.AddPersistentListener` で設定済み）。結果画面の各ボタンも同様に `StageManager` の `OnNextStage`/`OnRetry`/`OnStageSelect` を指す。EventSystem は `InputSystemUIInputModule` + `Assets/InputSystem_Actions.inputactions`。
- **Stage1（2026-09-13、敵配置を変更）**: 地面は x セル [-19,7) ＋ [10,30)（落とし穴 x≈7〜10 あり）。敵は **`Enemy1` を2体のみ**（@x≈-4, @x≈15。旧`Enemy_Boss`(HP5)は Enemy1(HP1) に置き換え済み）。Goal（@x28）は HP2以上の敵（ボス扱い）が生存しているとゴール不可（`Goal.AnyBossAlive()`）という既存仕様があるため、**Stage1に一撃で倒せないボスを置いてはいけない**（Stage1は`allowedActions`が`Dash`のみで攻撃自体ができないため、以前の`Enemy_Boss`配置だとゴール不可能なバグになっていた。今回の置き換えで解消）。
- **Stage2（2026-09-13、Stage1と同一構成化）**: 地面・敵配置ともに Stage1 と完全に同じ（x セル [-19,7)＋[10,30)、`Enemy1`を@x≈-4,@x≈15の2体、Goal@x28）。詳細なレベルデザインは別プランナーが今後担当する前提の暫定構成。`stageSeed`は22222のまま。
- **Stage3（2026-09-11、地形・敵配置を Stage1 と同一化 → 高台を Tilemap 版へ置き換え → 2026-09-13、敵をEnemy1+Enemy2に変更）**: 地面 Tilemap は Stage1 と同じ x セル [-19,7) ＋ [10,30)（落とし穴あり）。高台は **Tilemap 版の `Grid/HighGround`**（x セル 2,3,4・天面行 y=-1、直下に柱行 y=-2）（詳細は §7「一方通行の高台」）。敵は `Enemy1`（HP1 @x≈-4）と `Enemy2`（据え置き砲台 @x≈15、旧`Enemy_Boss`から置き換え）。`Goal`（@x25）・`stageSeed`（33333）は変更なし。
- **Stage4（2026-09-13、縦スクロールステージとして再構築）**: **Goal オブジェクトは無い**。地面 Tilemap（`Grid/Ground`）を、下部の床（xセル-5〜5, yセル-8〜-4）＋そこから上へ2ユニット間隔で交互に積んだ1マス厚の足場6段（surface y=0,2,4,6,8,10。タイル行はそれぞれの1つ下）に作り直した（プレイヤーの最大ジャンプ高さ ≈2.45 なので2ユニット間隔なら届く）。`Player`初期位置は`(0,-1.5,0)`に変更（横方向はカメラが追従しないため、床の中央に合わせた）。`Main Camera`は`CameraFollow.followHorizontal=false / followVertical=true`（x=0固定・y追従）、初期位置`(0,-1.5,-10)`。敵は`Enemy1`(@-3,2.5)・`Enemy2`(@-3,6.5)・**ボス`Enemy1_Boss`**(@0,10.5、一番上の足場)を配置。ボスは`Enemy1`プレハブのインスタンスに、シーン側オーバーライドで`Enemy.maxHealth=3`・`Enemy.clearStageOnDeath=true`を設定したもの（`EnemyPatrol`はそのまま残しているので足場の幅ぴったりで往復する）。**まだ地形・敵配置ともに「縦スクロールが正しく動くかを試すための簡易版」であり、正式なレベルデザインではない。** ボスを倒すと`Enemy.Die()`から`StageManager.Clear()`が直接呼ばれてクリアになる（Goalに触れた場合と同じ扱い）。
  - **2026-09-14、高台（一方通行）を1セット追加**: ユーザーが自分でジャンプ力調整のために高台を試そうとしたが、`Grid/Ground`（`OneWayPlatform`が付いていない普通の地面Tilemap）に直接タイルを描いてしまい機能しなかった（天面が下から通り抜けられない＝一方通行ではなくただの全方向ブロックになっていた）ため、Stage3と同じ構造の`Grid/HighGround`（Tilemap + TilemapRenderer(order -9) + Rigidbody2D(Static) + TilemapCollider2D(Merge) + CompositeCollider2D(Polygons) + TilemapColliderBootstrap + OneWayPlatform、layer=Ground）を新規作成し、タイルをそちらへ移設して修正。位置は天面 x=2,3,4 / y=-2（左右で正しく左/中央/右のアセットを使うよう修正）、柱 x=2,3,4 / y=-3。**一方通行の高台を機能させるには、タイルの colliderType 設定だけでなく、必ず`OneWayPlatform`付きの専用Tilemap GameObjectに置く必要がある**（既存の`Grid/Ground`に描いても一方通行にはならない）。
  - **2026-09-16、ユーザーが高台をさらに2セット追加→2つとも機能せず、再修正**: 症状は「右側の低い高台が完全にただのブロックになる」「左側の高い高台が全方向すり抜けてしまう」の2つ。原因はそれぞれ別:
    1. 右側: `Grid/HighGround`にある既存の正しい高台（x=2-4,y=-2/-3）と**全く同じ座標に**`Grid/Ground`側にも`HighGroundTopCenter`タイルが重ねて置かれていた（Active Tilemapを`Ground`のまま塗ってしまったミス）。`Ground`側の当たり判定は`OneWayPlatform`の管理外＝常に実体のままなので、`HighGround`側がすり抜け設定にしても`Ground`側で必ずブロックされていた。該当タイルを`Ground`から削除して解決。
    2. 左側: 新しく足された高台（x=-4〜-2の柱+天面、および真ん中の`GroundTile`の島 x=2-4,y=2）が、**全部既存の`Grid/HighGround`という1つのTilemapに同居**してしまっていた。`OneWayPlatform`は自分の`_col.bounds`（合成コライダー全体の外接矩形）を基準に重なり率を計算するため、離れた複数の高台が1つのTilemapに混在すると bounds が全部をまたぐ巨大な範囲になり、判定が破綻して常にすり抜けになっていた（GAME_SPEC既知の制限どおり）。
    - **修正**: `Grid/HighGround`（低い方、x=2-4）はそのまま。左側の高台を`Grid/HighGround2`（柱 x=-4〜-2 y=-3〜-1 + 天面 y=0）という**独立したTilemap**に分割。真ん中の`GroundTile`の島（x=2-4,y=2）は一方通行にする意図が無い普通の地面なので`Grid/Ground`へ移設。天面のみ・柱なしで空中に浮いていた`PlatformTallUpper`（y=4）は、ユーザーが「適当に置いただけ」と確認したため削除済み。Play で残った高台がそれぞれ個別に`pathCount=1`・独立したboundsになっていること、`HighGround2`で「上に乗って下降中はソリッド」「柱の中を上昇中はすり抜け」を確認済み。
    - **命名の整理（2026-09-16）**: 「高台」の英訳が"high ground"であることから、`Platform`という名前を使っていたGameObject・アセット群をすべて`HighGround`系の名前へ統一（`Grid/Platform`→`Grid/HighGround`、`PlatformTop*`/`PlatformPillar*`タイル→`HighGroundTop*`/`HighGroundPillar*`、プレハブ→`HighGroundTilemap.prefab`）。スクリプト名`OneWayPlatform.cs`自体は当たり判定の挙動を表す技術的な名前として変更していない（GameObject名とコンポーネント名が一致しなくなる点は他プランナーへの説明で明記が必要）。
    - **高台を追加する際の運用（2026-09-16、ユーザー方針）**: `HighGroundTilemap.prefab`は「毎回使うもの」ではなく、**新しく独立した高台を1つ増やす最初の1回だけ**使う（あるいは開発側が用意する）もの。一度その専用Tilemapがシーンに存在すれば、以後はTile Paletteで`Ground`と全く同じ感覚で直接ペイント/消去して高さ・形を調整してよい（詳細な使い方ガイドは別途、他プランナー向けに整理）。
- **Stage5（2026-09-16、ラスボス戦として最小構成）**: 地面はそれまでの連続Tilemapのまま変更なし。**Goalオブジェクトは削除済み**（Stage4と同じくボス撃破でクリア）。`Enemy3`（ラスボス、詳細は§6-3）を地面中央付近 `(4, -1.5)` に配置。`Player`(@x-10)はそのまま。まだ「地面があってプレイヤーとラスボスがいるだけ」の最小構成で、正式なレベルデザインではない。
- **使用可能アクション（`StageSet.stages[i].allowedActions`, 2026-09-09）**: Stage1 = `Dash` のみ（`disableCombos` も実質 on）／ Stage2 = `Dash`+`Attack` ／ Stage3〜5 = `Jump`+`Dash`+`Attack`。`StageSet.asset` で編集。
- **結果画面**は各ステージシーン内の `StageFlow/ResultCanvas`（`ClearPanel` / `FailPanel`、`sortingOrder 100`、通常は非アクティブ）。`StageManager` が表示と遷移を管理。
  - 表示中は `Time.timeScale = 0`、`PlayerController` / `MainActionController` を無効化。
  - **「もう一度」= シーンの再読み込み**なので、アクションの並びも含めて完全初期化される（＝リトライで同じ並びになる仕様を自動で満たす）。
  - クリア画面: `Next Stage`（次が無ければ非表示 → 実質 Stage Select）/ `Retry` / `Stage Select`。
  - 失敗画面: `Retry` / `Stage Select`。
- **クリア条件**: `Goal`（ステージ右端のトリガー）にプレイヤー本体が触れる。ただし `Enemy` で `MaxHealth >= 2`（＝ボス）が生存中は無効。
- **失敗条件**: `PlayerHealth.OnDied`（体力 0、1 回だけ発火）または `StageManager` の `player.y < killY`（既定 -12）。
- **キーボード操作（`MenuNavigation`, 2026-09-06 / 2D 化 2026-09-08）**: メニュー系 UI をキーボードでも操作可能。
  - 付いている場所: `Title/Canvas`、`StageSelect/Canvas`、各ステージの `StageFlow/ResultCanvas/ClearPanel` と `FailPanel`（パネルに付けてあるので、そのパネルが表示された瞬間だけ働く）。対象ボタンは子から階層順で自動収集。実行時生成ボタンは `AddButton()` で追加（`DevProgressResetButton` が使用）。
  - **カーソル移動は画面位置ベースの 2D**（`ButtonCenter` = `transform.position`、ScreenSpaceOverlay なので画面ピクセル）:
    - **W / ↑・S / ↓** → 上/下にあるボタンのうち最も近いもの（ユークリッド距離）。進行方向に候補が無ければ**反対の端の行へラップ**（その行の中で x が現在に一番近いもの）。
    - **A / ←・D / →** → **同じ行**（縦ズレ `rowTolerance` 40px 以内）の中で進行方向の隣。行内に候補が無ければ**反対端へラップ**。
    - ＝ ステージ 1〜5（横一列）は A/D で左右ループ。縦は「ステージ行 ⇔ リセット ⇔ タイトルに戻る」で、上端／下端もループする（ステージから ↑ で「タイトルに戻る」へ、「タイトルに戻る」から ↓ でステージ行へ）。非表示/無効ボタンはスキップ。
  - **スペース / Enter** で決定（`Button.onClick.Invoke()`）。
  - **初期カーソル位置**: `SetInitialFocus(Button)` で明示指定があればそれ（`StageSelectMenu` が「今挑戦できる一番先のステージ」＝`GameFlow.UnlockedStageIndex` のボタンを渡す）。無ければ `initialCursor` enum：`FirstUsable`（既定。Title / 結果画面）。実際の確定は Update 側（`OnEnable` 時点では他スクリプトの `Start` 未実行のことがあるため）。
  - **マウスホバー**：**マウスを実際に動かしたときだけ**カーソルに反映（`OnEnable` で現在のマウス位置を基準として覚え、そこから 2px 以上動くまでホバー無効）。シーン遷移直後や結果パネル表示直後にマウスが据え置かれているだけでは、初期カーソル位置が上書きされない（2026-09-08 修正。例：ステージ2クリア→ステージ選択でカーソルはステージ3のまま、マウスがステージ1上にあっても動かさない限り動かない）。
  - **入力ロック（`InputLock`, 2026-09-08 / 対象を決定系のみに限定 2026-09-12、§16）**：`OnEnable` に `InputLock.LockFor(inputLockDuration)` を呼び、その間は**決定**（Space/Enter/テンキー Enter・マウスクリック＝`HandleSubmit()` と `GraphicRaycaster`）だけを無視する。`InputLock.InputAllowed` = `!SceneTransition.Transitioning && Time.unscaledTime >= 解除時刻`。`Time.unscaledTime` 基準。**カーソル移動（`HandleKeyboardNav()`・マウスホバー）はこの猶予タイマー中でも常に反映される**（以前は移動も含めて全部止めていたため、「下矢印キーでカーソルを動かしたい」という意図した操作までブロックしてしまう問題があった。決定だけを対象にすることで解消）。ただし `SceneTransition` の**フェード演出中**（`InputLock.NavigationAllowed` = `!SceneTransition.Transitioning` が false の間）はカーソル移動・マウスホバーも含めて完全にブロックする（フェード中はまだ画面が見えていないため。猶予タイマーの対象外＝常時受付、とは別の話）。さらに、フェードが終わった後にカーソル移動が実際に成立した（＝そのパネルで意味のある操作だった）瞬間に `InputLock.Unlock()` を呼び、**決定のロックも即座に解除する**（キー操作で動かし始めた時点で「連打の勢い」ではなく「意図した操作」と判断できるため）。**ロックが明けたら決定も通常どおり**（意図的な連打はそのまま通す）。
    - シーン遷移では `SceneTransition` が「暗転〜明転〜さらに 0.25 秒」を管理するので `OnEnable` の `LockFor` は実質冗長（害はない）。**結果パネル**（`ClearPanel` / `FailPanel`）はシーン遷移ではないので、この `LockFor` が効く時間（`inputLockDuration`）が本番。2026-09-12 に 1.0→0.5 秒へ半減し、現在は他画面の `MenuNavigation` と同じ 0.5 秒。
  - 選択中（キーボードカーソル or マウスホバー）のボタンに**色付きの枠**（`SelectionFrame`、実行時生成の黄色い矩形を背面に置き、`framePadding`(8px) ぶんはみ出させて枠に見せる）。
    - 枠の位置合わせ（2026-09-07 修正）: 枠は常に pivot (0.5,0.5) にし、ボタンの pivot が中心でなくても `sizeDelta*(0.5 - pivot)` で矩形中心へ補正して合わせる。以前はボタンの pivot をそのままコピーしていたため、中心 pivot でないボタン（左下配置の BackButton など）で枠が片側に寄っていた。中心 pivot のボタンでは補正 0 で従来と同じ。
  - `EventSystem`/`InputSystemUIInputModule` の自動ナビゲーションとは二重処理にならないよう、対象ボタンの `Navigation.mode = None` にし、毎フレーム `EventSystem` の選択を解除している。マウスのクリック・ホバー着色はモジュール側のまま。
- **将来メモ**: ステージ開始演出は §9-2 の開始会話で最小実装済み（`StageManager.Start` → 会話 → 操作解禁）。`GameFlow` は遷移のみ。メニュー UI テキストは日本語フォント未整備のため**英語**（会話本文は日本語。`Assets/Resources/Fonts/NotoSansJP-Regular.ttf` を埋め込んで表示、詳細は §9-2「日本語フォント」）。

### 9-2. プロローグ / 会話システム（`DialogueSequence` + `DialoguePlayer`、2026-09-07）

**データ**: 会話 1 本 = `DialogueSequence`（ScriptableObject, `[CreateAssetMenu] RandomGame/Dialogue Sequence`, 置き場 `Assets/Dialogue/`）。分岐なしの一本道。現状は仮テキストで `Prologue` / `Stage1Intro`〜`Stage5Intro` の 6 本。
- `pages[]`: 1 ページ = スペース / エンター 1 回で送る単位。`speaker`（空でナレーション）/ `text`（`[TextArea]`）/ `image`（`Sprite`, 任意。未指定なら直前の絵を継続）/ `layout`。
- `layout`: `CenteredOnBlack`（暗転＋中央テキスト＝プロローグ前半）/ `BottomTextbox`（暗転＋一枚絵＋下部ボックス＝プロローグ後半）/ `TopTextbox`（暗転なし＋上部ボックス＝ステージ開始会話）。
- **一枚絵が未準備のうちは仮イラスト**を自動表示（`BottomTextbox` で `image` 未指定のとき。グレー面に「主人公」＝左下 /「ダンジョン」＝右 のラベル）。`Sprite` を割り当てれば置き換わる。
- 割り当て先は `Assets/Resources/StageSet.asset`: 新フィールド `prologue`（全体で 1 本）と、各 `stages[i].intro`（ステージごと）。

**再生**: `DialoguePlayer`（`Assets/Scripts/DialoguePlayer.cs`）。
- UI（Canvas 含む）は**実行時に自分で生成**する。シーン側は空 GameObject にコンポーネントを付けるだけ。Canvas は `sortingOrder 200`（HUD / 結果画面より前面）。
- `Play(DialogueSequence, System.Action onComplete)` で再生。**スペース / エンター / テンキー Enter / 画面のどこでも左クリック**で送り、最後まで読むと `onComplete`。内容が空なら即 `onComplete`。クリックは UI 経由ではなく `Mouse.current.leftButton` を直接読むので、テキストボックス上でも位置を問わず送れる（会話 UI の Image は `raycastTarget=false`）。
- **入力ロック（`InputLock`, 2026-09-08）**: `Play()` の直後に `InputLock.LockFor(inputLockDuration)`（既定 **0.25 秒**、2026-09-12 に 0.5→0.25 へ半減）。その間は送り入力（Space/Enter/左クリック）を無視。プロローグ／開始会話へは `SceneTransition` 経由で入るので、実際には「暗転〜明転〜さらに 0.25 秒」ずっと送り入力は不可（`InputLock.InputAllowed` が `SceneTransition.Transitioning` も見るため）。明転後に会話が現れ、ロックが明ければ通常どおり送れる。ステージ開始会話は会話終了時（`StageManager.OnIntroFinished`）にも `LockFor(0.25)`（送り切った勢いでアクションが出ないように）。
- 再生中は静的 `DialoguePlayer.IsPlaying == true`。`PlayerController.Update` と `MainActionController.Update` は先頭でこれを見て**入力を無視**（スペースが会話送りに食われる／移動しない）。
- **文字送り（タイプライター演出、2026-09-12。on/off・速さともに同日中にシーン単位→ページ単位へ変更）**: `DialogueSequence.Page.useTypewriterEffect`（`[SerializeField] bool`、**既定 true**）と `.typewriterCharsPerSecond`（`[SerializeField] float`、**既定 30**）が**ページ（テキスト）ごとに個別設定**できる。true のページは、本文（`_centerText` または `_bodyText`。**話者名は対象外で常に即時表示**）を先頭からそのページの速さ（1秒あたりの表示文字数）で1文字ずつ表示する（「こんにちは」→「こ」→「こん」→…）。
  - **表示し終わる前**に発動入力（Space/Enter/テンキー Enter/左クリック）があると、その入力は**残りを一気に表示するだけ**でページはまだ送らない（`CompleteTypewriter()`）。
  - **表示し終わった状態**での発動入力で、初めて次のページへ送る（`Advance()`）。＝1文字ずつ表示中は「1回目の入力で全文表示、2回目の入力で次へ」という2段階。
  - そのページの `useTypewriterEffect` が false なら、常に開始時点で全文表示済み（`IsFullyRevealed`）から始まるため、発動入力は毎回即ページ送りになる＝従来の一括表示（この場合 `typewriterCharsPerSecond` は無視される）。
  - アニメーションは `Time.unscaledDeltaTime` 基準で進む（ステージ開始会話は `StageManager` が `Time.timeScale=0` にするため、`Time.deltaTime` 基準だと文字が出てこなくなる）。
  - 実装は `_typewriterActive`/`_typewriterFullText`/`_typewriterElapsed`/`_typewriterTarget`（アニメーション対象の `Text`）/`_typewriterCharsPerSecond`（そのページの速さを保持）で状態管理。`SetPageText(Text target, string fullText, bool useTypewriter, float charsPerSecond)` が `Render()` から `p.useTypewriterEffect`/`p.typewriterCharsPerSecond` を渡して呼ばれる。リッチテキストタグ（`<color=...>` 等）を本文に含めた場合、単純な文字数カットのため表示途中でタグが割れる可能性がある（現状のダイアログデータはプレーンテキストのみなので未対応・未検証）。
  - **インスペクターでの設定場所**: **`Assets/Dialogue/*.asset`（各 `DialogueSequence`）の `pages[]` 内、ページごとの `Use Typewriter Effect` / `Typewriter Chars Per Second`**（例: `Stage1Intro.asset` を選択 → `Pages` を展開 → 各要素の中。`DialoguePlayer` 側にはこれらの設定は無い）。**既存の `.asset` は再保存していなくても、この bool/float フィールドは新規追加分も含めて全ページ既定値（true / 30）で読み込まれることを確認済み**（Prologue / Stage1Intro で検証）。
  - **`CenteredOnBlack` の中央表示ブレ対策（2026-09-12）**: `_centerText` は元々 `TextAnchor.MiddleCenter` で、文字送り中に文字数が増えるたびに中央揃えの基準がズレて左右にブレて見えていた。対策として `LayoutCenteredText(string)` を新設：ページ描画時にまず確保領域（画面の 12%〜88%）の実横幅を測り、`Text.preferredWidth` で全文の必要横幅を求め、`Mathf.Min` で確保領域幅にクランプ（収まらない＝改行が要る場合は確保領域いっぱいにフォールバック）。その横幅ぶんだけ確保領域の中央に来るよう `offsetMin`/`offsetMax` で領域自体を狭め、揃えを `TextAnchor.MiddleLeft` に変更した上でその固定された左端から文字を生やす。全文表示時にちょうど元の中央位置へ収まるため、見た目を変えずに文字送り中のブレだけを解消できる。`useTypewriterEffect=false` のページでも同じ計算を通すが、この場合は最初から全文表示のため実害はない（計算結果のボックス幅＝全文の幅になり、Left と Center が一致する）。
  - **フェード演出中はアニメーションを止める（2026-09-12）**: プロローグ／ステージ開始会話は `Start()` から即座に `Play()` されるため、シーン遷移の暗転〜明転（`SceneTransition.Transitioning`）の間に文字送りが進んでしまい、明転して画面が見えたときには既に全文表示済み、という問題があった。対策として `UpdateTypewriter()` の先頭で `if (SceneTransition.Transitioning) return;`（経過時間を進めない＝待機）を追加。明転が完了して `Transitioning` が false になった瞬間から文字送りが始まるため、プレイヤーは実際にアニメーションを見られる。Play で確認済み：`Transitioning=true` の間は3回 `Update()` を回しても `_typewriterElapsed`/表示テキストが変化しない。`Transitioning=false` にした瞬間から進み始める。

**日本語フォント（2026-09-11）**: 会話本文は日本語だが、以前は `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`（ビルトインの Arial 系、日本語グリフ無し）をそのまま使っていた。エディタ / Windows スタンドアロンでは欠けているグリフを **OS インストール済みフォントへフォールバック**して表示できていたが、**WebGL ビルド（unityroom）は OS フォントにアクセスできないため、そのフォールバックが効かず日本語が表示されない**という問題があった。
- 対策: `Assets/Resources/Fonts/NotoSansJP-Regular.ttf` を追加し、`DialoguePlayer.Awake()` で `Resources.Load<Font>("Fonts/NotoSansJP-Regular")`（見つからなければビルトインへフォールバック）を使うようにした。`DialoguePlayer` が生成する全ての `Text`（`_centerText` / `_speakerText` / `_bodyText` / `_hintText` / 仮イラストの `主人公`・`ダンジョン`・`(仮イラスト)` ラベル）はこの `_font` を共有しているので、この 1 箇所の変更で会話まわりの日本語表示すべてに効く。`Text.dynamic` 方式（`TrueTypeFontImporter`: `fontRenderingMode=Smooth`, `fontTextureCase=Dynamic`, `includeFontData=True`）なので、フォントの実データがビルドに同梱され、OS に頼らず自前でグリフを描画する（WebGL でも動く）。
- **文字セットは「常用漢字レベル」にサブセット化済み**（Google Fonts 配布の可変フォント `NotoSansJP[wght].ttf` から `fonttools varLib.instancer` で Regular(wght=400) の静的インスタンスを書き出し → `fonttools subset` で以下の文字だけに絞った）: ASCII 印字可能文字（0x20–0x7E）／CJK 記号・句読点（U+3000–303F）／ひらがな（U+3040–309F）／カタカナ（U+30A0–30FF）／全角英数・記号の一部（U+FF01–FF5E, U+FFE0–FFE5）／**常用漢字 2136 字**（[hoffmannjp/joyo-json](https://github.com/hoffmannjp/joyo-json) の `joyo_kanji.json` から取得）／その他少数の記号（¥ ° × ÷ – — …）。合計ユニーク文字数 2594、フォントファイルは **約960KB**（元の可変フォントは 9.5MB、静的 Regular 単体でも 5.8MB）。
- **常用漢字に無い固有名詞用の漢字（人名・地名など難読字）は現状表示できない。** 会話テキストに常用漢字外の漢字を使う場合は、後述の手順でフォントを作り直して文字を追加する必要がある。ライセンスは SIL Open Font License 1.1（`Assets/Fonts/NotoSansJP-OFL-LICENSE.txt`、ビルドには含まれない参照用）。
- **文字を追加してフォントを作り直す手順**: ①追加したい文字を集めたテキストファイルを用意（既存の常用漢字リストに足す形でも可）②Google Fonts の `ofl/notosansjp/NotoSansJP[wght].ttf`（可変フォント）を取得 ③`python -m fontTools.varLib.instancer --update-name-table -o Regular-full.ttf NotoSansJP[wght].ttf wght=400` で Regular の静的フォントを書き出す ④`python -m fontTools.subset Regular-full.ttf --text-file=charset.txt --output-file=NotoSansJP-Regular.ttf --layout-features='*' --glyph-names --symbol-cmap --legacy-cmap --notdef-glyph --notdef-outline --recommended-glyphs --name-IDs='*' --name-legacy --name-languages='*'` でサブセット化 ⑤`Assets/Resources/Fonts/NotoSansJP-Regular.ttf` を上書きしてインポートし直す（既存の GUID を維持するため、ファイルの中身だけ差し替える）。

**プロローグ**: `Title`「Game Start」→ `GameFlow.StartGame()` → `Prologue` シーン → `PrologueRunner` が `StageSet.prologue` を再生 → 終了で `GameFlow.GoStageSelect()`。`StartGame()` は Prologue が Build Settings に無ければ StageSelect へ直行。

**ステージ開始会話**: `StageManager.Start()` → `TryPlayIntro()`。
- そのステージを**初めて開いたときだけ**再生。既読は `GameFlow` が `PlayerPrefs` キー `RandomGame.SeenIntroMask`（ビットマスク）で保持。`HasSeenIntro(i)` / `SetIntroSeen(i,b)` / `MarkIntroSeen(i)`。`GameFlow.ResetProgress()` はクリア状況・プロローグ既読と一緒にこれも消す。
- 会話中は `Time.timeScale = 0`（プレイヤーも敵も停止）。読み終わると `OnIntroFinished()` で `MarkIntroSeen` → `timeScale = 1`。会話が無い / `DialoguePlayer` が居ないステージは即「既読」にしてスキップ。
- `intro` が未設定のステージは会話なしで通常開始。

**プロローグの既読フラグ**（2026-09-07）: `GameFlow.HasSeenPrologue` / `SetPrologueSeen(b)` / `MarkPrologueSeen()`（`PlayerPrefs` キー `RandomGame.SeenPrologue`、0/1）。
- **既読ならプロローグをスキップ**: `GameFlow.StartGame()` は未読のときだけ `Prologue` シーンを読み込む（既読なら StageSelect へ直行）。`PrologueRunner` も冒頭で既読チェック（直接 Play 時の保険）。プロローグを最後まで読むと `MarkPrologueSeen()`。

**開発者用の既読トグル（`DevStorySeenToggles`, 2026-09-07）**: `DevStageClearToggles`（クリア状況）と同じ流儀の実行時生成 `Toggle`。**表示条件は `DeveloperSettings.Active`**（エディタ内 かつ `Resources/DeveloperSettings.asset` の `developerMode`。`DevStageClearToggles` / `DevProgressResetButton` と共通）。`Start()` で `!DeveloperSettings.Active` なら自身を `enabled = false` にして何も生成しない。同じ階層に `StageSelectMenu` があるかで動作を自動判定:
- **StageSelect/Canvas に付けたとき**: 各ステージボタンの左、クリア状況チェックの**更に左**（`stageExtraLeftGap` = 既定 72px）に「そのステージの開始会話を既読か」トグルを5つ。色は青系でクリアの緑と区別。`isOn` ⇄ `GameFlow.HasSeenIntro/SetIntroSeen`。
- **Title/Canvas に付けたとき**: 「Game Start」ボタン（`prologueAnchorButton`）の左に「プロローグを既読か」トグル1つ。`isOn` ⇄ `GameFlow.HasSeenPrologue/SetPrologueSeen`。
- **トグルの位置（2026-09-08、横並びレイアウト対応）**: `DevStageClearToggles` / `DevStorySeenToggles` はステージボタンの並びを自動判定（`IsHorizontalRow` = 隣り合うボタンの x 差 ≥ y 差）。
  - **横並び（現在）**: 各ステージボタンの**真上**に clear トグル、その更に上に story トグル（`stageExtraLeftGap` ぶん）。ラベル（"story"/"clear"）は先頭（Stage1）のトグルの**左**に1つずつ（右寄せ）。
  - 縦並び（旧）: ボタンの左に縦一列、ラベルは列の一番上。
- **ラベル（2026-09-07〜）**: 各ボックスにキャプションを付けず、グループにつき1つだけ（`BuildGroupLabel`、`labelFontSize` 既定 24, bold）。Title の単独プロローグトグルはその上に "story"。
- `Toggle` なので `MenuNavigation` は拾わない（マウス専用のデバッグ UI）。

### 9-1. ステージ1 = `Assets/Scenes/Stage1.unity`（旧 `SampleScene.unity`）

（下記に加えて `EventSystem`、`StageFlow`（`StageManager` + `ResultCanvas`）、`Goal`（Prefab インスタンス, @x28, 縦長トリガー, 緑）を追加済み）

```
Main Camera            [Camera, CameraFollow]  ortho size 6 @ (0,-0.5,-10)
Global Light 2D
Player（Prefab インスタンス） @ (-10,-1.5)  [SpriteRenderer(PlayerArrow), BoxCollider2D, Rigidbody2D(grav 3, PlayerNoFriction),
                                      PlayerController, MainActionQueue, MainActionController, PlayerHealth]
  AttackHitbox         [SpriteRenderer, BoxCollider2D(trigger), AttackHitbox]  通常は非アクティブ
  DebugBars            [PlayerDebugBars]
    ComboBar / CooldownBar  各 BG(SpriteRenderer) + Fill(SpriteRenderer) + Label(TextMesh)
Enemy1（Prefab インスタンス, 旧名 Enemy_A） @ (-4,-1.5)  [SpriteRenderer, BoxCollider2D, Enemy(HP1), EnemyPatrol]
Enemy1_B（Prefab インスタンス, 2026-09-13にEnemy_Bossから置き換え） @ (15,-1.5) [SpriteRenderer, BoxCollider2D, Enemy(HP1), EnemyPatrol]
HUD_Canvas（Prefab インスタンス） [Canvas, CanvasScaler, GraphicRaycaster]
  ActionBar            [ActionBarUI] → Title(Text) + Slot0..3 (Image + 子 Label(Text))
  HealthPanel          [HealthUI] → HP0..2 (Image, 赤丸)
StageFlow（Prefab インスタンス） [StageManager]
  ResultCanvas         [Canvas, CanvasScaler, GraphicRaycaster] → ClearPanel/FailPanel（各 [MenuNavigation] + Title + ボタン群）
DialogueSystem（Prefab インスタンス, Prologue を除く） [DialoguePlayer]
Grid                   @ (0,0)  [Grid] cell size (1,1)
  Ground               layer=Ground  [Tilemap, TilemapRenderer(order -10), Rigidbody2D(Static),
                                      TilemapCollider2D(compositeOperation Merge), CompositeCollider2D(Polygons),
                                      TilemapColliderBootstrap]
                                      天面 y=-2。Stage1 は落とし穴あり（pathCount 2）
  HighGround           （Stage1には無い。Stage3に1基、Stage4に2基。高台=high ground。2026-09-16に`Platform`から改名）layer=Ground  [Tilemap, TilemapRenderer(order -9), Rigidbody2D(Static),
                                      TilemapCollider2D(compositeOperation Merge), CompositeCollider2D(Polygons),
                                      TilemapColliderBootstrap, OneWayPlatform]
                                      天面セル x=2,3,4 / y=-1（colliderType Grid）、柱セル同 x / y=-2（colliderType None）
```
（Stage1 に以前あった GameObject 版 `OneWayPlatform` は 2026-09-11 に削除済み。§7 参照）

生成アセット（`Assets/Art/`）: `WhiteSquare.png`（32px, PPU32）、`PlayerArrow.png`（左右非対称の矢印）、`Circle.png`（体力アイコン）、`PlayerNoFriction.physicsMaterial2D`（摩擦 0）、`GroundTile.png`（90×90, PPU 90 の仮地面タイル。本番絵で上書き予定）、`Tiles/GroundTile.asset`（`Tile`, colliderType Grid）。

---

## 10. スクリプト一覧（`Assets/Scripts/`）

| スクリプト | 付いている場所 | 役割 |
|---|---|---|
| `MainActionType` | (enum) | Jump / Dash / Attack |
| `MainActionQueue` | Player | アクションの並び（決定的・連続禁止）。`Peek` / `Consume` / `OnChanged`。`StageSet.allowedActions` があれば `lottery` を上書き |
| `MainActionController` | Player | メインアクションの発動・クールタイム・コンボ・先行入力・ダッシュ処理・無敵。発動入力はスペース / エンター / テンキー Enter / 左クリック（2026-09-12、会話送り・メニュー決定と統一）。会話中／画面切り替え直後は入力停止。`StageSet.disableCombos` のステージでは `_combosEnabled=false`（コンボ無効） |
| `PlayerController` | Player | 左右移動・向き・接地判定・コヨーテ/落下猶予・ノックバック受け・着地時間予測（`TryPredictLandingTime`）。会話中／画面切り替え直後（`InputLock`）は入力停止（§16-2） |
| `PlayerHealth` | Player | 体力・被弾・無敵時間。`TakeDamage -> bool`、`OnHealthChanged` |
| `AttackHitbox` | Player/AttackHitbox | 前方の一時的な攻撃判定（トリガー）。`Enemy`にヒットすればダメージ、`Bullet`にヒットすれば`DestroyByAttack()`で即消滅（2026-09-13） |
| `PlayerDebugBars` | Player/DebugBars | 頭上のデバッグゲージ 2 本。`Awake` で `!DeveloperSettings.Active` なら GameObject ごと非アクティブ（開発者用） |
| `Enemy` | Enemy1, Enemy2, Enemy3, Enemy_Boss, Stage4のEnemy1_Boss | 体力・接触ダメージ + ノックバック・ダッシュ中すり抜け。`clearStageOnDeath`（既定false、2026-09-13追加）trueなら`Die()`時に`StageManager.Clear()`を呼ぶ（Goal無しステージのボス用）。`OnDamaged`イベント（2026-09-16追加、`TakeDamage`で体力が減るたびに発火）を`Enemy3AI`が購読し、追尾弾発射中に被弾したら即座にクールタイムへ移行する処理に使用 |
| `EnemyPatrol` | Enemy1, Enemy_Boss, Stage4のEnemy1_Boss | 左右往復（transform 直接移動）。**2026-09-14**: `DialoguePlayer.IsPlaying`中は移動しない |
| `EnemyShooter` | Enemy2（2026-09-13追加） | 一定間隔で`Bullet`を発射。発射の瞬間だけプレイヤーへホーミングするオプション付き。**2026-09-14**: `SpriteRenderer.isVisible`が false（画面外）のときは発射しない、`DialoguePlayer.IsPlaying`中はタイマーごと停止（行動しない）、ホーミング無効時はプレイヤーがいる左右方向へ発射（以前は常に右固定になっていたバグを修正） |
| `Bullet` | Enemy2/Enemy3の弾（2026-09-13追加） | 発射時に設定した方向へ直進する弾。`transform.position`直接移動、当たり判定は**トリガー**（2026-09-17に非トリガーから変更）。`Rigidbody2D`（Body Type = Kinematic）付き（トリガー判定の成立に必要。地面側にも`Rigidbody2D`があるが弾側にも付けておくことで確実にする）。プレイヤー接触ダメージ・ダッシュ中すり抜け・地面接触/攻撃で消滅・`maxLifetime`で自動消滅。**2026-09-16**: `Configure`に`homingTurnSpeedDegPerSec`（既定0）を追加、0より大きいと`Update()`毎に`RotateTowards`でプレイヤー方向へ継続的に旋回する追尾弾になる（Enemy3の③用、Enemy2は使わず従来通り）。**2026-09-17**: 追尾弾（`homingTurnSpeed > 0`）は`maxLifetime`による自動消滅の対象から除外。**2026-09-18**: 敵キャラに触れた場合、発射直後の`selfHitGraceTime`（既定0.2秒、発射元自身との重なり対策）を過ぎていれば`enemyDamage`（既定2）を与えて消滅する（それまでは常にすり抜けだった。追尾弾をラスボスへ誘導してヒットさせる攻略に対応）。
  - **反転モード（2026-09-17追加）**: ダッシュで弾をすり抜けられた直後など、現在の速度ベクトルとプレイヤー方向のなす角が`reversalAngleThreshold`（C#側の既定値は170度。**`Bullet.prefab`ではユーザーが150度に変更済み**）を超えたときは、`RotateTowards`による回転ではなく、**速度ベクトルの大きさを`Vector2.MoveTowards`で直線的に減速→0→反対向きへ加速**させることで向きを変える（`_reversing`フラグで目標速度に達するまで維持）。理由: ほぼ180度の回転は回転軸の計算が数値的に不安定になり、回転の途中で地面/壁方向を意図せず向いてしまうことがあった。速度ベクトルを直線で繋ぐ方式なら、常に元の進行方向の延長線上を通るため横方向を向かない。反転にかかる速さは、`homingTurnSpeed`から自動計算される基準値（＝同じ`homingTurnSpeed`で180度回転するのと同じ時間で反転が完了する速さ）に、`reversalRateMultiplier`（既定1、インスペクターで調整可）を掛けたもの。Playで、なす角が閾値を超えた瞬間から速度のY成分（横方向）が終始0のまま、X成分だけが直線的に反転することを確認済み。
  - **敵キャラへのヒット（2026-09-18追加）**: 追尾弾をラスボスへ誘導してヒットさせられるように、`HandleTrigger`で敵キャラ（`Enemy`）に触れた場合の扱いを変更。以前は敵キャラには一切反応せず常にすり抜けていたが、**発射から`selfHitGraceTime`（既定0.2秒）が経過していれば、`enemyDamage`（既定2、インスペクターで調整可）のダメージを与えて弾自身も消滅する**ようにした。猶予時間は、弾が発射元の敵自身の位置（＝重なった状態）で生成されるため、発射直後に発射元自身へ即座にヒットしてしまうのを防ぐためのもの（`_age`という経過時間カウンタで管理）。猶予時間内は今まで通り完全に無視する。Playで、猶予時間内は無反応・経過後はダメージが入って消滅することを確認済み。
| `Enemy3AI` | Enemy3（2026-09-16追加） | ①離脱移動→②放射弾/③追尾弾を抽選→クールタイム→①…の状態機械。`DialoguePlayer.IsPlaying`中・画面外での発射禁止はEnemy1/2と同じルール |
| `EnemyHealthBar` | Enemy_Boss/HealthBar, Enemy3/HealthBar | ボスの体力ゲージ + 数値 |
| `OneWayPlatform` | Stage3/`Grid/HighGround`（Tilemap の CompositeCollider2D） | 一方通行 + 重なり率での着地判定。単体 Collider2D でも Tilemap の CompositeCollider2D でも動く（`Awake` が CompositeCollider2D を優先） |
| `TilemapColliderBootstrap` | 各ステージ `Grid/Ground` | `Awake` でタイルを貼り直し、`TilemapCollider2D`/`CompositeCollider2D` の形状を再生成させる（eval 生成 Tilemap が Play 開始時に当たり判定を持たない問題の対策）。§7 |
| `CameraFollow` | Main Camera | 追従。`target`（Player の Transform）は未設定なら Tag=Player から自動取得（2026-09-12）。`followHorizontal`/`followVertical`（各既定true/false、2026-09-13追加）で横縦を個別にオン/オフでき、オフの方向は開始位置で固定（Stage4のみ縦追従に設定） |
| `ActionBarUI` | HUD_Canvas/ActionBar | アクション先読み表示。`queue`（Player の MainActionQueue）は未設定なら Tag=Player から自動取得（2026-09-12） |
| `HealthUI` | HUD_Canvas/HealthPanel | 体力アイコン表示。`playerHealth`（Player の PlayerHealth）は未設定なら Tag=Player から自動取得（2026-09-12） |
| `StageSet` | ScriptableObject（`Assets/Resources/StageSet.asset`） | ステージの並び。`stages[]` = `displayName` + `sceneName` + `intro`（会話）+ `allowedActions`（そのステージの抽選対象）+ `disableCombos`。全体の `prologue`。`AllowedActionsAt`/`DisableCombosAt`/`IndexOfScene`。GameFlow が Resources.Load |
| `GameFlow` | (static クラス) | 画面遷移（`SceneTransition.Go` 経由）+ ステージ解放 + 会話既読。`Stages`（StageSet）、`StageCount`、`StartGame`（未読ならPrologue経由）/`LoadStage`/`RetryStage`/`NextStage`/`GoStageSelect`/`GoTitle`、`CurrentStageIndex`、`ActiveStageIndex`（アクティブシーン名から StageSet index を解決）、クリア状況（`IsStageCleared`/`SetStageCleared`/`MarkStageCleared`、PlayerPrefs ビットマスク）、`IsStageUnlocked`/`UnlockedStageIndex`、ステージ会話既読（`HasSeenIntro`/`SetIntroSeen`/`MarkIntroSeen`、ビットマスク）、プロローグ既読（`HasSeenPrologue`/`SetPrologueSeen`/`MarkPrologueSeen`、0/1）、`ResetStageProgress`（クリア＋ステージ既読の2キー消去、プロローグは残す）、`ResetProgress`（3キー消去） |
| `DeveloperSettings` | ScriptableObject（`Assets/Resources/DeveloperSettings.asset`） | 開発者機能の総合スイッチ。`developerMode` bool をインスペクター編集。静的 `Active` = エディタ内 かつ `developerMode`（`#if UNITY_EDITOR` ガード。ビルドでは常に false）。`DevStageClearToggles` / `DevStorySeenToggles` / `DevProgressResetButton` が従う |
| `DialogueSequence` | ScriptableObject（`Assets/Dialogue/*.asset`） | 会話 1 本。`pages[]` = `speaker` + `text` + `image` + `layout` + `useTypewriterEffect`（既定 true）+ `typewriterCharsPerSecond`（既定 30）（2026-09-12 追加。ページごとに文字送り演出の on/off と速さ） |
| `DialoguePlayer` | Prologue シーン, 各ステージシーン | 会話再生（UI は実行時生成）。`Play(seq, onComplete)`、静的 `IsPlaying`。送り＝Space/Enter/左クリック（画面任意位置）。`Play()` で `InputLock.LockFor(inputLockDuration=0.25)`（2026-09-12 に 0.5→0.25 へ半減）。日本語表示用に `Resources.Load<Font>("Fonts/NotoSansJP-Regular")` を使用（§9-2）。1文字ずつの文字送り演出（on/off・速さともにページ単位＝`DialogueSequence.Page.useTypewriterEffect`/`.typewriterCharsPerSecond`、2026-09-12、§9-2） |
| `PrologueRunner` | Prologue シーン | `StageSet.prologue` を再生 → `GameFlow.GoStageSelect()` |
| `TitleMenu` | Title/Canvas | `OnStartClicked()` → `GameFlow.StartGame()`（プロローグ経由でステージ選択）（ボタン onClick から） |
| `StageSelectMenu` | StageSelect/Canvas | `LoadStage(int)` → `GameFlow.LoadStage(i)`、`BackToTitle()` → `GameFlow.GoTitle()`（左下 BackButton）。`Start` で `RefreshLocks()`（未解放ボタン無効化）＋ `MenuNavigation.SetInitialFocus`（初期カーソル＝一番先の解放ステージ）。`StageButtons` を公開 |
| `DevStageClearToggles` | StageSelect/Canvas | 【開発者用】各ステージボタンにクリア状況チェックボックス＋"clear" ラベルを実行時生成。`public ResetAll()`。表示は `DeveloperSettings.Active` |
| `DevStorySeenToggles` | StageSelect/Canvas ＋ Title/Canvas | 【開発者用】会話の既読トグル＋"story" ラベルを実行時生成。StageSelect＝各ステージの開始会話、Title＝プロローグ。`public ResetAll()`（StageSelect 側のみ実効）。表示は `DeveloperSettings.Active` |
| `DevProgressResetButton` | StageSelect/Canvas | 【開発者用】BackButton の少し上に「Reset story & clear」ボタンを実行時生成し `MenuNavigation.AddButton` で登録。`GameFlow.ResetStageProgress()` ＋ 両トグルの `ResetAll()`。表示は `DeveloperSettings.Active` |
| `FreshBuildGuard` | (static, `RuntimeInitializeOnLoadMethod`) | ビルド版のみ。`Policy`（OnEveryBuild / OnTokenChange / Disabled）に応じて起動時に `GameFlow.ResetProgress()`。エディタ内は無効 |
| `FreshBuildGuardBuildCheck` | (`Assets/Scripts/Editor/`, `IPreprocessBuildWithReport`) | `Policy = OnEveryBuild` のまま非開発ビルドを作ろうとしたら確認ダイアログでビルドを止める |
| `StageManager` | 各ステージ/StageFlow | クリア/失敗判定・結果画面表示・結果ボタン処理・落下死判定（killY）・初回入場時の開始会話（`TryPlayIntro`, `Time.timeScale=0`）・ステージ開始時 / 会話終了時に `InputLock.LockFor(inputLockDuration=0.25)`（2026-09-12 に 0.5→0.25 へ半減） |
| `Goal` | 各ステージ/Goal | 右端トリガー。ボス全滅後にプレイヤーが触れると `StageManager.Clear()` |
| `MenuNavigation` | Title/StageSelect の Canvas, 各ステージの ClearPanel/FailPanel | メニュー UI のキーボード操作。位置ベース 2D 移動（W/S=上下・最近傍＋端ループ、A/D=同じ行内＋端ループ）、Space/Enter で決定、選択枠の自動生成。マウスホバーは移動時のみ反映。`OnEnable` で `InputLock.LockFor(inputLockDuration)`（全画面共通 0.5s、2026-09-12 に結果パネルの 1.0s を統一）。カーソル移動・マウスホバーは `InputLock.NavigationAllowed`（フェード中だけ false）を見る、決定（`HandleSubmit()`/`GraphicRaycaster`）は `InputLock.InputAllowed`（フェード中 or 猶予中は false）を見る——猶予中でもカーソル移動は効き、実際に成立したら `InputLock.Unlock()` で決定の猶予も即解除する（2026-09-12、§16-1）。`AddButton()` / `SetInitialFocus()` |
| `InputLock` | (static クラス) | 入力ロック。`InputAllowed` = `!SceneTransition.Transitioning && Time.unscaledTime >= 解除時刻`。`LockFor(秒)` で一定時間 false に（一番遅い解除時刻を採用）。`MenuNavigation`/`DialoguePlayer`/`StageManager`/`SceneTransition` が LockFor、`MenuNavigation`/`DialoguePlayer`/`MainActionController`/`PlayerController` が参照 |
| `SceneTransition` | (実行時生成, `DontDestroyOnLoad`) | 全シーン遷移で暗転→読み込み→明転。`Go(シーン名)`。演出中 `Transitioning=true`（＝入力全無効）、明転後 `InputLock.LockFor(postFadeInLockSeconds)`。全面黒 Image（sortingOrder 32760, raycastTarget）でマウスも遮断 |
| `SceneTransitionSettings` | ScriptableObject（`Assets/Resources/SceneTransitionSettings.asset`） | `fadeOutSeconds`(0.5) / `fadeInSeconds`(0.5) / `postFadeInLockSeconds`(**0.25**、2026-09-12 に 0.5→0.25 へ半減)。インスペクター調整 |

---

## 11. 主要パラメータ一覧（既定値）

| 分類 | パラメータ | 値 | 置き場所 |
|---|---|---|---|
| 移動 | moveSpeed | 6 | PlayerController |
| ジャンプ | jumpForce | 12 | MainActionController |
| ジャンプ | jumpAirCooldownCap（滞空クールタイム上限） | 3 秒 | MainActionController |
| ジャンプ | JumpLiftoffGrace（離陸猶予, const） | 0.25 秒 | MainActionController |
| ダッシュ | dashSpeed / dashDuration | 18 / 0.5 秒 | MainActionController |
| ダッシュ | dashLockFraction（前半ロック割合） | 0.5 | MainActionController |
| ダッシュ | dashForwardDecel / dashBrakeDecel | 40 / 160 u/s² | MainActionController |
| ダッシュ | dashCooldown | 2 秒 | MainActionController |
| 攻撃 | attackDuration / attackDamage | **0.4 秒**（2026-09-11 変更） / 1 | MainActionController |
| 攻撃 | attackCooldown | 2 秒 | MainActionController |
| 攻撃 | 判定の前方オフセット | Transform.localPosition.x の絶対値（既定 0.9、専用フィールドは廃止 2026-09-13） | AttackHitbox |
| コンボ | comboGraceTime（受付猶予） | 0.8 秒 | MainActionController |
| 先行入力 | inputBufferTime（CD 明け前の受付） | 0.1 秒（≒6フレーム） | MainActionController |
| 先行入力 | BufferedInputMaxLife（記憶の失効, const） | 0.4 秒 | MainActionController |
| 先行入力 | bufferZoneIdle/ActiveColor・minBufferZoneWidthFrac | シアン/緑・0.04 | PlayerDebugBars |
| 空中補助 | noFallGrace（落下しない猶予） | 0.1 秒 | PlayerController |
| 空中補助 | coyoteJumpGrace（ジャンプ受付猶予） | 0.18 秒 | PlayerController |
| 接地 | groundCheckDistance | 0.1 | PlayerController |
| 体力 | maxHealth / invulnTime | 3 / 0.8 秒 | PlayerHealth |
| 敵 | maxHealth / contactDamage | 1 / 1 | Enemy |
| 敵 | knockbackSpeed / knockbackUpSpeed / knockbackDuration | 8 / 4 / 0.25 秒 | Enemy |
| ラスボス | moveSpeed / retreatDistance | 2 / 5 | Enemy3AI |
| ラスボス | radialChance（②を選ぶ確率、2026-09-17追加） | 0.5 | Enemy3AI |
| ラスボス | radialBulletCount / radialCooldown | 8 / 3 秒 | Enemy3AI |
| ラスボス | homingTurnSpeed / homingCooldown | 90 度/秒 / 3 秒 | Enemy3AI |
| 弾 | reversalAngleThreshold / reversalRateMultiplier（反転モード、2026-09-17追加） | 170 度（Bullet.prefabでは150） / 1 | Bullet |
| 弾 | enemyDamage / selfHitGraceTime（敵キャラへのヒット、2026-09-18追加） | 2 / 0.2 秒 | Bullet |
| ラスボス | bulletSpeed | 5 | Enemy3AI |
| 敵 | clearStageOnDeath（倒すと即クリア） | false（Stage4のボスだけtrue） | Enemy |
| 砲台 | fireInterval / bulletSpeed / homingOnFire | 2 秒 / 6 / true | EnemyShooter |
| 弾 | damage / maxLifetime | 1 / 6 秒 | Bullet |
| 高台 | requiredOverlap / topTolerance | 0.5 / 0.05 | OneWayPlatform |
| カメラ | smoothTime | 0.15 | CameraFollow |
| カメラ | followHorizontal / followVertical | true / false（Stage4のみ false / true） | CameraFollow |
| キュー | slotCount / stageSeed | 4 / 12345（Stage2〜5: 22222 / 33333 / 44444 / 55555） | MainActionQueue |
| 物理 | Rigidbody2D.gravityScale | 3 | Player |
| ステージ | killY（落下死ライン） | -12 | StageManager |
| メニュー操作 | frameColor / framePadding（選択枠） | 黄 / 8px | MenuNavigation |
| メニュー操作 | rowTolerance（横入力で同じ行とみなす縦ズレ） | 40px | MenuNavigation |
| 入力ロック | inputLockDuration（`MenuNavigation`：画面 / パネルが出てからこの秒数、入力を無効化） | 0.5 秒（全画面共通。結果パネルも 2026-09-12 に 1.0→0.5 秒へ統一） | MenuNavigation |
| 入力ロック | inputLockDuration（`DialoguePlayer`/`StageManager`：会話・ステージ開始/終了直後の入力無効化） | **0.25 秒**（2026-09-12 に 0.5→0.25 へ半減） | DialoguePlayer / StageManager |
| フェード遷移 | fadeOutSeconds / fadeInSeconds | 各 0.5 秒 | SceneTransitionSettings（`Assets/Resources/`） |
| フェード遷移 | postFadeInLockSeconds | **0.25 秒**（2026-09-12 に 0.5→0.25 へ半減） | SceneTransitionSettings（`Assets/Resources/`） |
| 会話 | blackColor / boxColor（暗転・ボックス） | ほぼ黒 / 黒 78% | DialoguePlayer |
| 会話 | centerFontSize / bodyFontSize / speakerFontSize | 40 / 30 / 26 | DialoguePlayer |
| 会話 | sortingOrder（会話 Canvas） | 200 | DialoguePlayer |
| 会話 | useTypewriterEffect（1文字ずつ表示するか、ページ単位） | true（2026-09-12 追加、同日中にシーン単位→ページ単位へ変更） | DialogueSequence.Page |
| 会話 | typewriterCharsPerSecond（文字送りの速さ、ページ単位） | 30 文字/秒（2026-09-12 追加、同日中にシーン単位→ページ単位へ変更） | DialogueSequence.Page |

---

## 12. 技術メモ・制約

- **新 Input System 専用**（`activeInputHandler: 1`）。`Keyboard.current` を使う。
- **MCP 経由の Play モードはループが凍結する**（OS フォーカスが無いと `Time.time` / `Time.frameCount` がフレーム 2 付近で止まる）。ロジック検証は `component.SendMessage("FixedUpdate")` などを手動で回して状態を見る。位置を書き換えたら `Physics2D.SyncTransforms()` を呼ぶ。手触りは実機（エディタ）で確認する。
- **ドメインリロードが遅い**（20〜60 秒超）。MCP の `recompile` がタイムアウトしても実際は成功していることが多い。`recompile` / `RequestScriptReload` を連打しない（そのたびに最初からやり直しになる）。詰まったらエディタウィンドウをクリックしてもらう。
- 物理: `FixedUpdate` は物理積分の「前」に走る。ダッシュ移動を `FixedUpdate` で書くのはこのため（`WaitForFixedUpdate` コルーチンだと積分後になり、1 ステップぶん落下してしまう）。
- 静的コライダー（`Enemy`, 旧 `Ground_*`）は `Rigidbody2D` なしでプレイヤー（動的 RB）と物理衝突する。`transform` 直接移動でも衝突判定は機能する。
- `Physics2D.IgnoreCollision` を多用（一方通行高台、ダッシュ中の敵すり抜け）。
- **地面は Tilemap 化済み**（2026-09-09。§7 / §9-1）。`TilemapCollider2D` + `CompositeCollider2D`（Merge / Polygons）で 1 本の外周コライダーにまとめる。`OverlapBox` での接地判定も効く。仮タイルなので本番は 90×90px 素材で `GroundTile.png` を上書きするだけ。
- **エディタ MCP eval の注意**: `EditorSceneManager.OpenScene(...)` と同じ eval 内で `Tilemap.SetTile(...)` を呼ぶと無音で失敗する（1 エディタティック必要）。シーンを開く eval と塗る eval を分ける。
- **eval で `SetTile` して作った Tilemap は Play 開始時にコライダー形状を生成しない**（`CompositeCollider2D.pathCount = 0` のまま＝地面がすり抜ける）。`ProcessTilemapChanges()` / `GenerateGeometry()` / コライダーの enable トグル / `RefreshAllTiles()` だけでは直らない。**タイルの「変更イベント」が必要** → タイルを一度消して貼り直す（`GetTilesBlock` → `ClearAllTiles` → `SetTilesBlock` → `ProcessTilemapChanges` → `GenerateGeometry`）と再生成される。これを `Awake` で毎回やる `TilemapColliderBootstrap` を各 `Ground` に付けてある（§7）。編集モードではそもそも物理形状が作られないので `pathCount` は 0 のまま（＝ シーンには形状が保存されない。実行時に Bootstrap が作る）。

---

## 13. 未実装 / TODO

**画面の流れは最小実装で通ったが、以下は未着手 / 仮:**
- **Stage2, Stage4, Stage5 の中身**。Stage2は2026-09-13にStage1と完全同一構成化（暫定、別プランナーが詳細設計予定）。Stage4は2026-09-13に縦スクロール構成へ作り直したが「スクロールが動くかの簡易確認用」で正式なレベルデザインではない。Stage5は2026-09-16にラスボス(Enemy3)を配置したが「地面とプレイヤーとラスボスがいるだけ」の最小構成で、正式なレベルデザインではない。Stage3はStage1と同一構成（2026-09-11）＋Enemy2追加（2026-09-13）。
- **会話テキストは全部仮**（`DialogueSequence` アセットの中身）。プロローグの一枚絵も未準備（仮イラスト表示中）。本番の絵は主人公＝左 / ダンジョン＝右の構図で用意予定。
- **会話 UI の体裁**（日本語表示自体は 2026-09-11 に対応済み — §9-2「日本語フォント」。文字送り演出は 2026-09-12 対応済み — §9-2。常用漢字外の漢字は現状のフォントサブセットに無いので表示できない）。
- **UI の日本語化**（メニューは今は英語のまま。日本語にする場合、`Text` はレンダリングだけなら §9-2 のフォントを流用できるが、見た目を作り込むなら TMP 移行も検討）。
- 結果画面 / メニューの見た目（配置・色は最小限）。
- **ロック中ステージの見た目**は Unity 既定のグレーアウトのみ（「LOCKED」表記や鍵アイコンは未実装）。クリア進捗のセーブは `PlayerPrefs` の 1 キーだけ（スロット/複数セーブ無し）。
- `StageManager.nextButton` の表示可否は `GameFlow.CurrentStageIndex` 依存。エディタでステージシーンを直接 Play すると index=0 扱いになる（フロー経由なら正しい）。
- ボスの行動（`Enemy_Boss` / Stage4の`Enemy1_Boss` は今のところ HP が多いだけの巡回。攻撃パターン無し）。
- Enemy1・Enemy2・Enemy3（ラスボス）すべて実装済み（§6-1〜§6-3）。Enemy3の各種数値（体力8、クールタイム3秒など）は未指定だったため仮に決めたもの。バランス調整はこれから。
- **Stage4のボス`Enemy1_Boss`は仮**。ユーザー指定により「Enemy1のHPを3にしただけ」の暫定実装（頭上体力バー無し。`Enemy1.prefab`に体力バーの子オブジェクトが無いため）。本番のボス仕様が決まり次第差し替え予定。
- **地面 / 高台 Tilemap の本番タイル素材**（今は仮の `GroundTile.png` と `HighGroundTop*.png`/`HighGroundPillar*.png`。90×90px / PPU 90 を保って上書きすれば差し替わる。高台は天面・柱それぞれ左/中央/右で見た目を区別できる本番絵を想定）。
- **HighGround（高台）の使い方・注意点は §7 に使い方ガイドとしてまとめてある**（1基=1Tilemap厳守、Groundに描かない、天面/柱・左中央右の機能差の有無、など）。他プランナーへの説明はそちらを参照。
- **高台の端で降りるときに一瞬引っかかる不具合が未解決**（2026-09-16）。ヒステリシス方式・「他の地面に着地するまでロック」方式の2つを試したが、いずれも実際のゲームプレイでは解消せず、`OneWayPlatform.cs`は元の単一閾値ロジックに戻した（詳細はメモリ`tilemap-platform-oneway`参照）。
- ステージのカメラ左右クランプ、スポーン地点の明示。
- 体力 UI アイコンは `enabled` 切り替えのみ / `EnemyHealthBar` の fill は中央アンカー。
- ハート型など体力 UI の見た目（現状は赤丸で確定・OK）。

---

## 14. ★重要な設計判断・要約（再掲）

1. **空中ジャンプは原則不可**（発動そのものを受け付けない。キュー消費もクールタイムも無し）。
   - 例外 A: **コヨーテタイム**（地面を離れて 0.18 秒以内）。少し落下していても跳べる。
   - 例外 B: **ダッシュ → ジャンプのコンボ**のときだけ、完全な空中でもジャンプ可。ダッシュ移動を中断して跳ぶ。
2. **落下しない猶予（0.1 秒）とジャンプ受付猶予（0.18 秒）は別パラメータ**。後者が長い。
3. **ダッシュ → ジャンプでも無敵は途切れない**。無敵はダッシュ効果時間ぶんの専用タイマーで管理し、ジャンプ移行でも減り続ける。無敵が早期に切れるのは「ダッシュ後半に後方入力」したときだけ。
4. **クールタイム**: ダッシュ 2 秒 / 攻撃 2 秒（固定）、ジャンプは「着地まで」（上限 3 秒）。
   - コンボにジャンプを含めば「着地まで」だけを見る。含まなければ 2 つ目のクールタイムだけ見る。相方のクールタイムは常に無視。
5. **コンボ受付猶予（0.8 秒）はクールタイムと独立**。最大 2 連続。
6. **アクションの並びはステージ固定・決定的**（seed ベース）。同じアクションは 2 連続しない。
7. **ダッシュ以外の敵接触はすり抜けない**（ノックバック + ダメージ）。すり抜けるのはダッシュ中のみ。
8. **敵接触ダメージは本体コライダーのみ**（`GetComponent`、`GetComponentInParent` を使わない。子の攻撃判定で自傷しないため）。

---

## 15. 共通オブジェクトの Prefab 化（2026-09-12）

全ステージで共通の `Player` / `Enemy_A` / `Enemy_Boss` / `Goal` を Prefab 化した。`Assets/Prefabs/` に置き、各ステージシーンには**直接配置した Prefab インスタンス**（実行時に動的生成する仕組みは無い・不要）。

- **`Assets/Prefabs/Player.prefab`**: Stage1 の `Player`（子の `AttackHitbox` / `DebugBars` ツリーごと）をそのまま Prefab 化。Stage2〜5 は元の GameObject を削除し、このプレハブをインスタンス化して差し替えた。
  - **ステージごとに残す上書き（インスタンス側の override）**: `Transform.position`（全ステージ実は (-10,-1.5,0) で共通）、`MainActionQueue.stageSeed`（Stage1〜5 = 12345 / 22222 / 33333 / 44444 / 55555）。他のフィールドはプレハブ側の値がそのまま使われる。
- **`Assets/Prefabs/Enemy.prefab`**（**2026-09-13 に `Enemy1.prefab` へリネーム**、詳細は §6-1）: Stage1 の `Enemy_A`（HP1 の雑魚。`Enemy` + `EnemyPatrol`）を Prefab 化。Stage3 の `Enemy_A` はこのプレハブのインスタンスに差し替え（Stage2/4/5 に敵は無し、元々の仕様どおり）。
- **`Assets/Prefabs/EnemyBoss.prefab`**: `Enemy.prefab`（現 `Enemy1.prefab`）の **Prefab Variant**（`PrefabUtility` の Variant 機構。ベースの差分だけを持つ）。差分は `Enemy.maxHealth`（1→5）、`EnemyPatrol`（speed 1.5→1 / range 3→2）、そして `HealthBar` 子（`EnemyHealthBar` + BG/Fill/Label）の追加。Stage1 の元の `Enemy_Boss` から `HealthBar` 子をそのまま移設して作成したので、見た目・参照とも作り直しではなく既存資産の再利用。Stage3 の `Enemy_Boss` もこのプレハブのインスタンスに差し替え。
  - 今後 HP や巡回範囲を変えたいときは `EnemyBoss.prefab` 自体を編集すれば、Stage1・Stage3 両方の `Enemy_Boss` に自動反映される（**Play で実証済み**: `Enemy.prefab` の `contactDamage` を一時的に 1→2 に変えて、Stage1 のシーンファイルには一切触れずに `Enemy_A` インスタンス側が 2 を返すことを確認 → 1 に戻した）。
- **`Assets/Prefabs/Goal.prefab`**: Stage1 の `Goal` を Prefab 化。`stageManager` フィールドは未設定のままにしてある（`Awake` で `FindAnyObjectByType<StageManager>()` に自動解決するので、シーンをまたいだ参照を持たせる必要が無い＝そのままプレハブ化しても安全）。Stage2〜5 の `Goal` もこのプレハブのインスタンスに差し替え。位置はステージごとに override（Stage1 = x28、Stage2〜5 = x25）。
- **`Assets/Prefabs/HUD_Canvas.prefab`**（2026-09-12 追加分）: Stage1 の `HUD_Canvas`（`ActionBarUI` + `HealthPanel`）を Prefab 化。Stage2〜5 もこのプレハブのインスタンスに差し替え。override は無し（全ステージ完全に同一構成）。
- **`Assets/Prefabs/StageFlow.prefab`**（2026-09-12 追加分）: Stage1 の `StageFlow`（`StageManager` + `ResultCanvas` の `ClearPanel`/`FailPanel`/`MenuNavigation` 一式）を Prefab 化。`StageManager.clearPanel`/`failPanel`/`nextButton`、各ボタンの `onClick` persistent listener は全部同じ Prefab 階層の内部参照なので、そのまま正しく維持される。Stage2〜5 もこのプレハブのインスタンスに差し替え。override は無し。
- **`Assets/Prefabs/DialogueSystem.prefab`**（2026-09-12 追加分）: `DialoguePlayer` 単体を Prefab 化（**Stage1〜5 のみ**）。**Prologue シーンの `DialogueSystem` は対象外**（`DialoguePlayer` に加えて `PrologueRunner` が付いており、他のどのシーンとも構成が違う一点物なので、共通化のメリットが無く従来どおりの通常 GameObject のまま）。

**Prefab 化のあと確認したこと**: `HUD_Canvas`/`StageFlow`/`DialogueSystem` を差し替える前に、それぞれの内部（`ActionBarUI`/`HealthUI`/`StageManager`/`DialoguePlayer`/`MenuNavigation`/`PrologueRunner`）の `[SerializeField]` を全スクリプト横断で洗い出し、**外部の別オブジェクトから直接参照されている箇所が無いこと**を確認してから実施した（§15-1 の教訓を踏まえた事前チェック）。唯一の外部参照だった `Goal.stageManager` は元々 auto-resolve 済みで無害。差し替え後、Stage1〜5 すべてで Play 確認（コンソールエラー・警告 0、`ActionBarUI`/`HealthUI`/`CameraFollow` の自動解決も正常、`StageManager.Clear()` 呼び出しも正常動作）。

**Prefab に関する重要な訂正（ユーザーの認識との相違）**: 「プレハブを直置きすると、あとでプレハブ本体を変更しても反映されない」というのは誤り。**プレハブインスタンスはシーンに直接置いても元のプレハブアセットとリンクしたままで、プレハブ側を編集すれば全インスタンスへ自動反映される**（Unity の Prefab の中核機能）。反映されないのは「そのインスタンスだけ個別に上書き（override）した項目」のみ。よって、今回のような「あとで調整しやすくしたい」という目的には、シーンに直接プレハブインスタンスを置くだけで十分であり、**実行時の動的生成（`Instantiate`）は実装していない・不要**。動的生成が要るのは、敵の湧き方をランダムにしたい／オブジェクトプールしたいなど別の目的が出てきたときで、現状の仕様には無い。

### 15-1. 副作用バグとその修正（2026-09-12、Prefab 化の直後に発生）

Player を「削除 → 新しい Prefab インスタンスを配置」という手順で差し替えた際、**Player とは別の GameObject が Player 側のコンポーネントを直接参照（Inspector 上のオブジェクト参照）していた箇所**が、古い Player の破棄と同時にすべて null になった（Tag 検索ではなく直接参照なので、Unity が自動的に新しいインスタンスへ繋ぎ直してはくれない）。影響は Stage2〜5（Stage1 は既存の Player をそのまま `SaveAsPrefabAssetAndConnect` したため同一インスタンスが継続し無事）。

判明した被害（Stage2〜5 全部）:
- **`CameraFollow.target`**（Main Camera）→ カメラがプレイヤーに追従しない。
- **`ActionBarUI.queue`**（HUD_Canvas/ActionBar）→ アクションバーの4枠が空のまま更新されない。
- **`HealthUI.playerHealth`**（HUD_Canvas/HealthPanel）→ 被弾しても体力アイコンが減らない（ユーザー未報告・調査で発見）。**Stage2 はカメラのみユーザーが手動修正済みだったため、これと ActionBarUI はまだ壊れていた**。

**対応**: 各シーンで3つの参照を Player の実インスタンスへ張り直して保存。加えて**再発防止**として、`CameraFollow` / `ActionBarUI` / `HealthUI` の3スクリプトに `Awake()` を追加し、参照が未設定（null）なら `GameObject.FindGameObjectWithTag("Player")` から自動解決するようにした（`Goal.stageManager` や `OneWayPlatform.playerCollider` と同じ、このプロジェクトで既に使われている流儀）。見つからなければ `Debug.LogWarning` を出す。Play で意図的に3つとも null にした状態から検証し、自動解決が効いて正しいインスタンスに繋がることを確認済み。

**教訓 / 次に Prefab 化する（HUD_Canvas・StageFlow・DialogueSystem）ときの注意**: GameObject を「削除して Prefab インスタンスに差し替える」操作をするときは、**その GameObject を Inspector 上で直接参照している他のスクリプトが無いか事前に確認する**（`grep -n "SerializeField" *.cs` で該当型のフィールドを洗い出す、など）。Tag/Find 経由の参照は無事だが、直接参照は同じ壊れ方をする。今回のように参照する側に自動解決フォールバックを仕込んでおくと、今後同種の差し替えをしても壊れない。

**Prefab 化を見送ったもの（理由つき）**:
- `Grid/Ground`・`Grid/HighGround`（Tilemap 地形）: コンポーネント構成は共通だが、塗ったタイルのデータ自体はステージごとに意図的に異なる（レベルデザインの本体）。Tilemap のタイルデータを Prefab の override として持たせるのは大きめのデータになり扱いにくいため、今回は見送り。コンポーネント構成を変える（例: `TilemapColliderBootstrap` にロジックを足す）ときは、既存の 5+1 シーンへ手作業で反映する必要がある点は変わらず。
- `EventSystem` / `Main Camera`: 単純な定型オブジェクトで、共通化のメリットが薄いため見送り。

**`HUD_Canvas` / `StageFlow` / `DialogueSystem`（Prologue 除く）も 2026-09-12 中に Prefab 化済み**（上記参照）。UnityEvent の persistent listener 配線や Canvas の入れ子構造は、いずれも Prefab 階層の内部参照だったため Player/Enemy/Goal のときと同様に問題なく維持された。

---

## 16. 入力ロックの対象を「決定系」だけに限定（2026-09-12、同日中に3段階で調整）

**問題**: `InputLock` は元々「画面が切り替わった直後、一定時間**すべての**入力を無効化する」仕組みだった。目的は「連打の勢いで意図せず決定・発動してしまう」ことの防止だが、これは同時に「意図した操作」まで塞いでしまっていた。具体例: 結果パネル（クリア/失敗）が表示された直後、下矢印キーでカーソルをもう1つ下の選択肢へ動かそうとしても、`MenuNavigation` の入力ロック（0.5秒）がカーソル移動そのものをブロックしていたため反応しなかった。

**考え方の整理**: 「連打で誤爆する」のは**決定系（スペース / エンター / テンキー Enter / マウス左クリックが引き金になる、1回きりの操作）**だけで、**移動・カーソル移動のような連続的な操作**は「押しっぱなし」で誤って進んでしまうような性質のものではなく、意図して行った操作をそのまま反映してよい。この2つを区別せず一律ブロックしていたのが問題だった。ただし画面によって適切な対応が異なる：

### 16-1. メニュー画面（Title / StageSelect / 結果パネル）: 3段階の状態

シーン遷移でメニュー画面に入る場合、状態は次の3段階になる（結果パネルはシーン遷移を伴わないので実質②③のみ）:

1. **`SceneTransition` のフェード演出中**（`InputLock.NavigationAllowed` が false）: カーソル移動・マウスホバーも含めて**いかなる入力も受け付けない**。画面がまだ遷移中で見えていないため。
2. **フェードは終わったが、決定の猶予（`InputLock.LockFor` の残り時間）がまだ残っている**: **決定だけ**を無視する。カーソル移動（WASD/矢印キー・マウスホバー）は受け付け、実際に操作に反映する。さらに、カーソル移動が**実際に成立した**（＝そのパネルで意味のある操作だった）瞬間に決定の猶予も即座に解除する。
3. **両方明けている**: 通常どおりすべて受け付ける。

実装:
- `InputLock.NavigationAllowed`（新設） = `!SceneTransition.Transitioning`。決定用の猶予タイマーは見ず、フェード中かどうかだけを見る。
- `MenuNavigation.Update()`: `HandleMouseHover()` と `HandleKeyboardNav()` を `InputLock.NavigationAllowed` で包み、フェード中は両方まとめてスキップする（②③でのみ実行）。`HandleSubmit()`（決定）と `GraphicRaycaster` の有効/無効（マウスクリック止め）は従来どおり `InputLock.InputAllowed`（フェード中 or 猶予中の両方で false）のまま。
- **カーソル移動が実際に成立したら決定のロックも即座に解除する**（②→③への早期遷移）: `HandleKeyboardNav()` は移動の前後で `_index` の変化を見て、変わっていれば `InputLock.Unlock()`（`_unlockAtUnscaled` を現在時刻にして即座に解除）を呼ぶ。「そのパネルで意味のある操作」だけが対象になる点がポイント: ステージ選択の横一列（Stage1〜5）なら WASD/矢印キー全部が該当しうるが、結果パネル（縦一列、左右移動は無効）では W/S・上下矢印キーだけが実際に `_index` を変え、A/D・左右矢印キーは候補が無く何も起きないので該当しない。個々のキーをハードコードして判定するのではなく「実際に動いたか」だけを見ているので、パネルごとの対応キーの違いを自動的に反映できる。
- Play で3段階すべて確認済み: `Transitioning=true` 時は S キーを押しても `_index` 不変・`NavigationAllowed=False`。`Transitioning=false` に切り替えた直後（決定の猶予はまだ残っている想定）に S キーを押すと `_index` が変わり、同時に `InputAllowed` も即座に True へ。

### 16-2. ステージ画面（Player の移動・メインアクション発動）: 両方ブロックのまま、早期解除も無し

- **`PlayerController.Update()`**: 一度は移動（A/D・矢印キー）を `InputLock` の対象から外したが、「動けるのになぜメインアクションは出せないのか」という不自然さの方が問題だったため、同日中に差し戻した。現在は元の仕様どおり、会話中（`DialoguePlayer.IsPlaying`）**または**画面切り替え直後（`!InputLock.InputAllowed`）の両方で移動を止める。
- `MainActionController`（メインアクション発動）はそのまま変更なし（元々ロック対象）。
- ステージ画面にはメニューのカーソル移動に相当する「常に許可したい連続入力」が無いため、16-1 のような早期解除の仕組みは実装していない。「すべてブロックし続ける」ことで、ステージ選択や結果画面で連打した勢いのままステージに入って誤発動する事故を防ぐ。

`DialoguePlayer`（会話送り）は元々「決定系の入力しか扱っていない」ため今回も変更不要。

**Play で確認済み**: 結果パネル表示直後（ロック中、`InputLock.InputAllowed == false`）に下キーを押すとカーソルが実際に移動すること、その状態で Enter を押してもボタンの `onClick` が発火しないこと、ロックが明けてから Enter を押すと発火することを確認。また、ロック中でも `PlayerController` の移動入力（D キー）が `_moveInput` に反映されることを確認。
