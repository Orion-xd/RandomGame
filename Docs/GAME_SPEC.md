# RandomGame 仕様・実装まとめ

最終更新: 2026-09-11 / 対象ブランチ: `feature/tilemap`（日本語フォント対応・高台の Tilemap 化も同ブランチで実施）
Unity 6000.3.11f1 / URP / 2D / 入力は **新 Input System のみ**（`Input.GetAxis` は不可、`UnityEngine.InputSystem.Keyboard.current` を使う）

このドキュメントは「後日、続きの作業をするとき」に現状を把握するためのもの。
プレイ可能なプロトタイプ（移動・メインアクション・敵・地形・体力）まで実装済み。ゲームの一連の流れ（タイトル→ステージ選択→リザルト）は未実装。

---

## 1. ゲーム全体のコンセプト（企画）

2D 横スクロールアクション。プレイヤーはステージ奥のゴールを目指す。

- **操作は2系統**
  - **通常操作**: 左右移動のみ（A/D または ←/→）。
  - **メインアクション**: スペースキー。ジャンプ / ダッシュ / 攻撃 のいずれかが発動する。
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
- **攻撃 (`DoAttack` コルーチン + `AttackHitbox`)**: 前方 `forwardOffset`（0.9）に子オブジェクトの当たり判定を `attackDuration`（**0.4 秒**、2026-09-11 に 0.2→0.4 変更、全ステージ共通）だけ有効化。触れた敵に `attackDamage`（1）。同じ敵を多重ヒットしない（`HashSet<Enemy>`、有効化時にクリア）。攻撃判定はトリガー。

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

- **`inputBufferTime` = 0.1 秒（約6フレーム。0 で無効）**。クールタイム終了のこの秒数前から、スペースキー押下を「先行入力」として記憶する。
- **キーを離していても**、クールタイムが明けた瞬間（`IsReady`）に次のアクションが自動発動する。「クールタイム明けにすぐ次を出す」操作をやりやすくするため。
- 実装（`MainActionController`）:
  - `Update()` でスペース押下時、まず `TryTrigger()`（`void`→`bool` に変更、発動できたか返す）。**出せなかった & `InInputBufferZone`** なら `_bufferedInput = true`（`_bufferedInputExpiry = Time.time + 0.4`＝`BufferedInputMaxLife` で失効させる保険つき）。
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

- `maxHealth`（既定 1 = 一撃）。`maxHealth >= 2` で頭上に体力ゲージ + 数値（`EnemyHealthBar`、ボス用）。
- **当たり判定は実体（非トリガー）**。`Rigidbody2D` は無い（`Ground_*` と同じ「静的コライダー」）。
- 毎 `FixedUpdate`、`Physics2D.IgnoreCollision(playerCollider, ownCollider, playerMainAction.IsDashing)` をトグル → **ダッシュ中だけすり抜け**、それ以外は物理衝突。
- `OnCollisionEnter2D` / `Stay2D` でプレイヤー本体（`other.GetComponent<PlayerHealth>()`、`InParent` にしない = 子の `AttackHitbox` に反応しない）に接触したら:
  - `PlayerHealth.TakeDamage(contactDamage=1)` → 実際に入ったら `PlayerController.ApplyKnockback`（敵の反対方向へ `knockbackSpeed`=8、上向き `knockbackUpSpeed`=4、`knockbackDuration`=0.25 秒）。
- 攻撃を受ける経路は `AttackHitbox`（トリガー）側の `OnTriggerEnter2D` → `Enemy.TakeDamage`。敵コライダーが非トリガーでも、当たった相手（AttackHitbox）がトリガーなのでトリガー通知は届く。
- `EnemyPatrol`: `Rigidbody` を使わず transform を直接動かしてスポーン地点中心に左右往復（`speed` ゆっくりめ、`range` 片側距離）。進行方向に `flipX`。
- シーンには `Enemy_A`（HP1、x=-4）と `Enemy_Boss`（HP 複数・ゲージ付き、x=15）。

---

## 7. ステージ要素

- **地面（Tilemap, 2026-09-09）**: 各ステージシーンに `Grid`（cell size 1×1）＋子 `Ground`（layer=Ground）。`Ground` に `Tilemap` / `TilemapRenderer`（sortingOrder -10）/ `Rigidbody2D`(Static) / `TilemapCollider2D`（`compositeOperation = Merge`）/ `CompositeCollider2D`（`geometryType = Polygons`）/ **`TilemapColliderBootstrap`**（後述）。タイル 1 個 = 1 ワールドユニット。
  - **`TilemapColliderBootstrap`（`Ground` に付ける・必須）**: eval で `SetTile` して作った Tilemap は Play 開始時にコライダー形状を生成せず（`CompositeCollider2D.pathCount = 0` のまま＝**地面がすり抜けて落下する**）、`Awake` でタイルを一括で貼り直して（`GetTilesBlock` → `ClearAllTiles` → `SetTilesBlock` → `ProcessTilemapChanges` → `GenerateGeometry`）形状の再生成を促す。これが無いと 5 シーンとも地面に当たり判定が付かない。通常のタイルパレットで塗ったマップなら不要。
  - タイルアセット: `Assets/Art/Tiles/GroundTile.asset`（`UnityEngine.Tilemaps.Tile`、`colliderType = Grid`、sprite = `Assets/Art/GroundTile.png`）。仮素材。**最終的に GroundTile.png を 90×90px の本番絵で上書きし、PPU を 90 に保てば 1 セル = 1 ユニットのまま差し替わる**（`Assets/Art/GroundTile.png` の現状: 90×90 の茶色ベタ＋縁＋斑点、Sprite / Single / PPU 90 / Point / 無圧縮 / FullRect / pivot Center）。
  - **塗り範囲**: 天面 y=-2（＝セル行 y=-3 が一番上、そこから y=-8 まで 6 行）。
    - **Stage1, Stage3**（2026-09-11、Stage3 も Stage1 と同一構成に変更）: x セル [-19,7) と [10,30) を塗り、x セル 7〜9 を空にして **落とし穴（x≈7〜10、幅 3）**。`CompositeCollider2D.pathCount = 2`（左右で分離）。
    - **Stage2, Stage4, Stage5**: x セル [-19,27) を連続で塗り、落とし穴なし。`pathCount = 1`。
  - `PlayerController.IsGrounded` は `CompositeCollider2D` を `Physics2D.OverlapBox` で検出できる（Play で確認済み: Stage1 は左地面/穴/右地面、Stage2〜5 は連続、天面 y=-2）。
  - **タイルパレット（`Assets/Tilemaps/Palettes/GroundPalette.prefab`, 2026-09-11）**: `Window > 2D > Tile Palette` で開いて手作業編集するための Unity 標準パレット。`GroundTile` と高台の 6 タイル（下記）を収録済み。使い方: シーンを開く → Tile Palette ウィンドウで `GroundPalette` を選択 → Active Tilemap がそのシーンの対象 Tilemap（`Grid/Ground` または `Grid/Platform`）になっていることを確認 → Paint/Erase/Box Fill 等でシーンビュー上を直接編集 → Ctrl+S で保存。当たり判定は Play 開始時に `TilemapColliderBootstrap` が自動で作り直すので、手で塗っても特別な後処理は不要。
- **一方通行の高台（Tilemap 版, 2026-09-11）**: `Grid` の子 `Platform`（`Ground` と同じ Grid・同じ 1×1 セル。現在 **Stage3 のみ**に配置。Stage1 の旧 GameObject 版は削除済み、Stage2/4/5 はもともと無し）。
  - **見た目**: 天面（乗れる面）3 種＋柱（乗れない・当たり判定も無い）3 種、計 6 枚のタイルで構成。実際の並びは天面 左/中央/右 の 3 マス＋その真下に柱 左/中央/右 の 3 マスの計 3×2 マス。柱は地面の天面（y=-2）にちょうど接し、「地面から生えた柱の上に台がある」見た目になる。左右は端用、中央は繰り返し用の想定（今は仮素材のため天面 3 種・柱 3 種はそれぞれほぼ同じ見た目で左右にわずかな縁のアクセントがある程度だが、本番素材に差し替えれば区別できるようになる設計）。
    - タイル: `Assets/Art/Tiles/PlatformTopLeft` / `PlatformTopCenter` / `PlatformTopRight`（`colliderType = Grid`）、`PlatformPillarLeft` / `PlatformPillarCenter` / `PlatformPillarRight`（`colliderType = None`）。元画像は `Assets/Art/PlatformTop*.png` / `PlatformPillar*.png`（90×90, PPU90 の仮素材。天面はオパーク、柱は半透明のグレー＝当たり判定が無いことを視覚的に示す仮の意匠）。
  - **当たり判定**: `Platform` の `TilemapCollider2D`(`compositeOperation=Merge`) + `CompositeCollider2D` は `colliderType=None` の柱タイルからは形状を作らないため、**天面タイルだけが合成された 1 つの当たり判定**になる（柱部分は完全にすり抜け＝当たり判定自体が存在しない。天面部分は実体の当たり判定）。
  - 一方通行のロジックは `OneWayPlatform` コンポーネントをそのまま流用（`Platform` GameObject に付ける）。下から上へは常にすり抜け。上から下へは抜けられない（着地できる）。
  - ただし **プレイヤーの横幅のうち `requiredOverlap`（0.5、インスペクター調整可）以上が天面に重なっている**ときだけ着地判定を有効化。端に少し引っかかっただけでは乗れない。
  - 実装は `PlatformEffector2D` ではなく、毎 `FixedUpdate` で `Physics2D.IgnoreCollision(player, platform, !solid)` をトグル。`solid = 足が天面より上（`topTolerance` 0.05） && 下降中 && 重なり率 >= requiredOverlap`。単体 BoxCollider2D でも Tilemap の CompositeCollider2D でも動くよう、`Awake` は `CompositeCollider2D` を優先して `_col` に採用する（2026-09-11 追加）。
  - `overlapX = min(右端どうし) - max(左端どうし)`、重なり率 = `overlapX / プレイヤー横幅`。ソース内に具体例つきの長いコメントあり。
  - Play で確認済み: `CompositeCollider2D.pathCount=1`（天面3マス分が1つに合成、bounds が天面3マス分の範囲と一致）、柱範囲は `OverlapBox` で完全に無反応、着地条件を満たすと `IgnoreCollision` が解除されソリッドになる（横に外れる／下から上昇中は再びすり抜け）ことを確認。
- **カメラ**（`CameraFollow`, Main Camera）: 横方向のみ `Mathf.SmoothDamp`（`smoothTime` 0.15）で追従。Y/Z は開始時の値で固定（縦追従なし）。ortho size 6、位置 (0,-0.5,-10)。
- **レイヤー**: user layer 8 = "Ground"。`Grid/Ground`（Tilemap）/ `Grid/Platform`（Tilemap, Stage3 のみ）に設定。`Enemy` はわざと外している（敵の上に乗ってもジャンプが回復しないように）。

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
  - 時間は `Assets/Resources/SceneTransitionSettings.asset` で調整：`fadeOutSeconds`(0.5) / `fadeInSeconds`(0.5) / `postFadeInLockSeconds`(0.5)。`Time.unscaledDeltaTime` 基準（結果画面など `timeScale=0` から遷移しても動く）。
  - **明転（フェードイン）のヒッチ対策（2026-09-08）**: シーンロード直後は 1 フレームの delta が大きく荒れるため、そのままだと明転が 1〜2 フレームで終わって「いきなり明るくなる」ように見える。対策として ①ロード後に 2 フレーム捨ててから明転開始 ②`Fade` の 1 フレームあたりの進行量に上限（`max(1/60, duration*0.15)` 秒）を設ける。
  - 演出中は `SceneTransition.Transitioning == true` → `InputLock.InputAllowed` が常に false（＝すべての入力を無効化）。明転しきったあと `InputLock.LockFor(postFadeInLockSeconds)` を呼び、**さらに 0.5 秒**入力を止める（連打対策）。合計：暗転 0.5 ＋ 読み込み ＋ 明転 0.5 ＋ 0.5。
  - **暗転中は `Time.timeScale = 0`（2026-09-08）**: 暗転が始まってから `LoadSceneAsync` 完了までは遷移元シーンを完全停止させる。これが無いと、クリアパネルのボタンを押してから遷移するまでの短い間だけ通常進行に戻り、`CameraFollow` の追従（`SmoothDamp`）が再開して「プレイヤーは操作していないのにカメラだけ少し動く」違和感が出る。読み込み後に `timeScale = 1` に戻す（開始会話があれば `StageManager` が再度 0 にする）。フェード自体は `Time.unscaledDeltaTime` なので timeScale 0 でも進む。
  - **ステージクリア / 失敗のパネル表示はシーン遷移ではないのでフェードしない**。代わりに結果パネルの `MenuNavigation.inputLockDuration` を **1.0 秒**にしてある（他画面は 0.5 秒）。この間はキーボードに加え `GraphicRaycaster` を無効化してマウスクリックも止める。
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
- **Stage2, Stage4, Stage5**: Stage1 を複製して敵・高台を削除し、地面を**落とし穴なしの連続 Tilemap**（x セル [-19,27)）にしただけの**プレースホルダー**（Stage4/5 は Stage3 を複製した時点のもの。以下の Stage3 変更後も追随していない）。中身の設計は未着手。`stageSeed` は 22222 / 44444 / 55555。各シーンに `EventSystem` / `StageFlow`(`StageManager`+`ResultCanvas`) / `Goal`(@x25) / 結果パネルの `MenuNavigation` を含む（Stage1 と同構成）。
- **Stage3（2026-09-11、地形・敵配置を Stage1 と同一化 → 同日中に高台を Tilemap 版へ置き換え）**: 地面・敵の配置を Stage1 と一致させた。地面 Tilemap は Stage1 と同じ x セル [-19,7) ＋ [10,30)（落とし穴 x≈7〜10 あり）。`Enemy_A`（HP1 @x≈-4）、`Enemy_Boss`（HP5 @x≈15, `HealthBar` 子付き）を Stage1 から複製して配置。高台は当初 Stage1 から GameObject 版 `OneWayPlatform` を複製していたが、同日中に **Tilemap 版の `Grid/Platform`**（x セル 2,3,4・天面行 y=-1、直下に柱行 y=-2）へ置き換えた（詳細は §7「一方通行の高台」）。`Goal`（@x25）・`stageSeed`（33333）はそのまま変更していない。
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
  - **入力ロック（`InputLock`, 2026-09-08）**：`OnEnable` に `InputLock.LockFor(inputLockDuration)` を呼び、その間は入力（移動・決定）を無視する。`InputLock.InputAllowed` = `!SceneTransition.Transitioning && Time.unscaledTime >= 解除時刻`。`Time.unscaledTime` 基準。**ロックが明けたら通常どおり**（意図的な連打はそのまま通す）。ロック中は `GraphicRaycaster` も無効化してマウスクリックも止める（`OnDisable` で復帰）。
    - シーン遷移では `SceneTransition` が「暗転〜明転〜さらに 0.5 秒」を管理するので `OnEnable` の `LockFor` は実質冗長（害はない）。**結果パネル**（`ClearPanel` / `FailPanel`）はシーン遷移ではないので、この `LockFor` が効く時間（`inputLockDuration` = **1.0 秒**）が本番。他画面の `MenuNavigation` は 0.5 秒。
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
- **入力ロック（`InputLock`, 2026-09-08）**: `Play()` の直後に `InputLock.LockFor(inputLockDuration)`（既定 0.5 秒）。その間は送り入力（Space/Enter/左クリック）を無視。プロローグ／開始会話へは `SceneTransition` 経由で入るので、実際には「暗転〜明転〜さらに 0.5 秒」ずっと送り入力は不可（`InputLock.InputAllowed` が `SceneTransition.Transitioning` も見るため）。明転後に会話が現れ、ロックが明ければ通常どおり送れる。ステージ開始会話は会話終了時（`StageManager.OnIntroFinished`）にも `LockFor(0.5)`（送り切った勢いでアクションが出ないように）。
- 再生中は静的 `DialoguePlayer.IsPlaying == true`。`PlayerController.Update` と `MainActionController.Update` は先頭でこれを見て**入力を無視**（スペースが会話送りに食われる／移動しない）。

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

（下記に加えて `EventSystem`、`StageFlow`（`StageManager` + `ResultCanvas`）、`Goal`（@x28, 縦長トリガー, 緑）を追加済み）

```
Main Camera            [Camera, CameraFollow]  ortho size 6 @ (0,-0.5,-10)
Global Light 2D
Player                 @ (-10,-1.5)  [SpriteRenderer(PlayerArrow), BoxCollider2D, Rigidbody2D(grav 3, PlayerNoFriction),
                                      PlayerController, MainActionQueue, MainActionController, PlayerHealth]
  AttackHitbox         [SpriteRenderer, BoxCollider2D(trigger), AttackHitbox]  通常は非アクティブ
  DebugBars            [PlayerDebugBars]
    ComboBar / CooldownBar  各 BG(SpriteRenderer) + Fill(SpriteRenderer) + Label(TextMesh)
Enemy_A                @ (-4,-1.5)  [SpriteRenderer, BoxCollider2D, Enemy(HP1), EnemyPatrol]
Enemy_Boss             @ (15,-1.25) [SpriteRenderer, BoxCollider2D, Enemy(HP複数), EnemyPatrol]
  HealthBar            [EnemyHealthBar] → BG / Fill / Label(TextMesh)
HUD_Canvas             [Canvas, CanvasScaler, GraphicRaycaster]
  ActionBar            [ActionBarUI] → Title(Text) + Slot0..3 (Image + 子 Label(Text))
  HealthPanel          [HealthUI] → HP0..2 (Image, 赤丸)
Grid                   @ (0,0)  [Grid] cell size (1,1)
  Ground               layer=Ground  [Tilemap, TilemapRenderer(order -10), Rigidbody2D(Static),
                                      TilemapCollider2D(compositeOperation Merge), CompositeCollider2D(Polygons),
                                      TilemapColliderBootstrap]
                                      天面 y=-2。Stage1 は落とし穴あり（pathCount 2）
  Platform             （Stage3 のみ）layer=Ground  [Tilemap, TilemapRenderer(order -9), Rigidbody2D(Static),
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
| `MainActionController` | Player | メインアクションの発動・クールタイム・コンボ・先行入力・ダッシュ処理・無敵。会話中／画面切り替え直後は入力停止。`StageSet.disableCombos` のステージでは `_combosEnabled=false`（コンボ無効） |
| `PlayerController` | Player | 左右移動・向き・接地判定・コヨーテ/落下猶予・ノックバック受け・着地時間予測（`TryPredictLandingTime`）。会話中／画面切り替え直後（`InputLock`）は入力停止 |
| `PlayerHealth` | Player | 体力・被弾・無敵時間。`TakeDamage -> bool`、`OnHealthChanged` |
| `AttackHitbox` | Player/AttackHitbox | 前方の一時的な攻撃判定（トリガー） |
| `PlayerDebugBars` | Player/DebugBars | 頭上のデバッグゲージ 2 本。`Awake` で `!DeveloperSettings.Active` なら GameObject ごと非アクティブ（開発者用） |
| `Enemy` | Enemy_A, Enemy_Boss | 体力・接触ダメージ + ノックバック・ダッシュ中すり抜け |
| `EnemyPatrol` | Enemy_A, Enemy_Boss | 左右往復（transform 直接移動） |
| `EnemyHealthBar` | Enemy_Boss/HealthBar | ボスの体力ゲージ + 数値 |
| `OneWayPlatform` | Stage3/`Grid/Platform`（Tilemap の CompositeCollider2D） | 一方通行 + 重なり率での着地判定。単体 Collider2D でも Tilemap の CompositeCollider2D でも動く（`Awake` が CompositeCollider2D を優先） |
| `TilemapColliderBootstrap` | 各ステージ `Grid/Ground` | `Awake` でタイルを貼り直し、`TilemapCollider2D`/`CompositeCollider2D` の形状を再生成させる（eval 生成 Tilemap が Play 開始時に当たり判定を持たない問題の対策）。§7 |
| `CameraFollow` | Main Camera | 横方向のみ追従 |
| `ActionBarUI` | HUD_Canvas/ActionBar | アクション先読み表示 |
| `HealthUI` | HUD_Canvas/HealthPanel | 体力アイコン表示 |
| `StageSet` | ScriptableObject（`Assets/Resources/StageSet.asset`） | ステージの並び。`stages[]` = `displayName` + `sceneName` + `intro`（会話）+ `allowedActions`（そのステージの抽選対象）+ `disableCombos`。全体の `prologue`。`AllowedActionsAt`/`DisableCombosAt`/`IndexOfScene`。GameFlow が Resources.Load |
| `GameFlow` | (static クラス) | 画面遷移（`SceneTransition.Go` 経由）+ ステージ解放 + 会話既読。`Stages`（StageSet）、`StageCount`、`StartGame`（未読ならPrologue経由）/`LoadStage`/`RetryStage`/`NextStage`/`GoStageSelect`/`GoTitle`、`CurrentStageIndex`、`ActiveStageIndex`（アクティブシーン名から StageSet index を解決）、クリア状況（`IsStageCleared`/`SetStageCleared`/`MarkStageCleared`、PlayerPrefs ビットマスク）、`IsStageUnlocked`/`UnlockedStageIndex`、ステージ会話既読（`HasSeenIntro`/`SetIntroSeen`/`MarkIntroSeen`、ビットマスク）、プロローグ既読（`HasSeenPrologue`/`SetPrologueSeen`/`MarkPrologueSeen`、0/1）、`ResetStageProgress`（クリア＋ステージ既読の2キー消去、プロローグは残す）、`ResetProgress`（3キー消去） |
| `DeveloperSettings` | ScriptableObject（`Assets/Resources/DeveloperSettings.asset`） | 開発者機能の総合スイッチ。`developerMode` bool をインスペクター編集。静的 `Active` = エディタ内 かつ `developerMode`（`#if UNITY_EDITOR` ガード。ビルドでは常に false）。`DevStageClearToggles` / `DevStorySeenToggles` / `DevProgressResetButton` が従う |
| `DialogueSequence` | ScriptableObject（`Assets/Dialogue/*.asset`） | 会話 1 本。`pages[]` = `speaker` + `text` + `image` + `layout` |
| `DialoguePlayer` | Prologue シーン, 各ステージシーン | 会話再生（UI は実行時生成）。`Play(seq, onComplete)`、静的 `IsPlaying`。送り＝Space/Enter/左クリック（画面任意位置）。`Play()` で `InputLock.LockFor(inputLockDuration=0.5)`。日本語表示用に `Resources.Load<Font>("Fonts/NotoSansJP-Regular")` を使用（§9-2） |
| `PrologueRunner` | Prologue シーン | `StageSet.prologue` を再生 → `GameFlow.GoStageSelect()` |
| `TitleMenu` | Title/Canvas | `OnStartClicked()` → `GameFlow.StartGame()`（プロローグ経由でステージ選択）（ボタン onClick から） |
| `StageSelectMenu` | StageSelect/Canvas | `LoadStage(int)` → `GameFlow.LoadStage(i)`、`BackToTitle()` → `GameFlow.GoTitle()`（左下 BackButton）。`Start` で `RefreshLocks()`（未解放ボタン無効化）＋ `MenuNavigation.SetInitialFocus`（初期カーソル＝一番先の解放ステージ）。`StageButtons` を公開 |
| `DevStageClearToggles` | StageSelect/Canvas | 【開発者用】各ステージボタンにクリア状況チェックボックス＋"clear" ラベルを実行時生成。`public ResetAll()`。表示は `DeveloperSettings.Active` |
| `DevStorySeenToggles` | StageSelect/Canvas ＋ Title/Canvas | 【開発者用】会話の既読トグル＋"story" ラベルを実行時生成。StageSelect＝各ステージの開始会話、Title＝プロローグ。`public ResetAll()`（StageSelect 側のみ実効）。表示は `DeveloperSettings.Active` |
| `DevProgressResetButton` | StageSelect/Canvas | 【開発者用】BackButton の少し上に「Reset story & clear」ボタンを実行時生成し `MenuNavigation.AddButton` で登録。`GameFlow.ResetStageProgress()` ＋ 両トグルの `ResetAll()`。表示は `DeveloperSettings.Active` |
| `FreshBuildGuard` | (static, `RuntimeInitializeOnLoadMethod`) | ビルド版のみ。`Policy`（OnEveryBuild / OnTokenChange / Disabled）に応じて起動時に `GameFlow.ResetProgress()`。エディタ内は無効 |
| `FreshBuildGuardBuildCheck` | (`Assets/Scripts/Editor/`, `IPreprocessBuildWithReport`) | `Policy = OnEveryBuild` のまま非開発ビルドを作ろうとしたら確認ダイアログでビルドを止める |
| `StageManager` | 各ステージ/StageFlow | クリア/失敗判定・結果画面表示・結果ボタン処理・落下死判定（killY）・初回入場時の開始会話（`TryPlayIntro`, `Time.timeScale=0`）・ステージ開始時 / 会話終了時に `InputLock.LockFor(inputLockDuration=0.5)` |
| `Goal` | 各ステージ/Goal | 右端トリガー。ボス全滅後にプレイヤーが触れると `StageManager.Clear()` |
| `MenuNavigation` | Title/StageSelect の Canvas, 各ステージの ClearPanel/FailPanel | メニュー UI のキーボード操作。位置ベース 2D 移動（W/S=上下・最近傍＋端ループ、A/D=同じ行内＋端ループ）、Space/Enter で決定、選択枠の自動生成。マウスホバーは移動時のみ反映。`OnEnable` で `InputLock.LockFor(inputLockDuration)`（メニュー 0.5s / 結果パネル 1.0s）、ロック中は `GraphicRaycaster` も無効化。`AddButton()` / `SetInitialFocus()` |
| `InputLock` | (static クラス) | 入力ロック。`InputAllowed` = `!SceneTransition.Transitioning && Time.unscaledTime >= 解除時刻`。`LockFor(秒)` で一定時間 false に（一番遅い解除時刻を採用）。`MenuNavigation`/`DialoguePlayer`/`StageManager`/`SceneTransition` が LockFor、`MenuNavigation`/`DialoguePlayer`/`MainActionController`/`PlayerController` が参照 |
| `SceneTransition` | (実行時生成, `DontDestroyOnLoad`) | 全シーン遷移で暗転→読み込み→明転。`Go(シーン名)`。演出中 `Transitioning=true`（＝入力全無効）、明転後 `InputLock.LockFor(postFadeInLockSeconds)`。全面黒 Image（sortingOrder 32760, raycastTarget）でマウスも遮断 |
| `SceneTransitionSettings` | ScriptableObject（`Assets/Resources/SceneTransitionSettings.asset`） | `fadeOutSeconds` / `fadeInSeconds` / `postFadeInLockSeconds`（各 0.5）。インスペクター調整 |

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
| 攻撃 | forwardOffset（判定の前方オフセット） | 0.9 | AttackHitbox |
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
| 高台 | requiredOverlap / topTolerance | 0.5 / 0.05 | OneWayPlatform |
| カメラ | smoothTime | 0.15 | CameraFollow |
| キュー | slotCount / stageSeed | 4 / 12345（Stage2〜5: 22222 / 33333 / 44444 / 55555） | MainActionQueue |
| 物理 | Rigidbody2D.gravityScale | 3 | Player |
| ステージ | killY（落下死ライン） | -12 | StageManager |
| メニュー操作 | frameColor / framePadding（選択枠） | 黄 / 8px | MenuNavigation |
| メニュー操作 | rowTolerance（横入力で同じ行とみなす縦ズレ） | 40px | MenuNavigation |
| 入力ロック | inputLockDuration（画面 / パネルが出てからこの秒数、入力を無効化） | 0.5 秒（結果パネルの MenuNavigation のみ 1.0 秒） | MenuNavigation / DialoguePlayer / StageManager |
| フェード遷移 | fadeOutSeconds / fadeInSeconds / postFadeInLockSeconds | 各 0.5 秒 | SceneTransitionSettings（`Assets/Resources/`） |
| 会話 | blackColor / boxColor（暗転・ボックス） | ほぼ黒 / 黒 78% | DialoguePlayer |
| 会話 | centerFontSize / bodyFontSize / speakerFontSize | 40 / 30 / 26 | DialoguePlayer |
| 会話 | sortingOrder（会話 Canvas） | 200 | DialoguePlayer |

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
- **Stage2, Stage4, Stage5 の中身**（今は落とし穴なしの連続 Tilemap 地面のみ。敵・地形・ゴール配置など）。**Stage3 は Stage1 と同一構成に変更済み**（2026-09-11）だが、レベルデザインとして意図されたものではなく暫定。
- **会話テキストは全部仮**（`DialogueSequence` アセットの中身）。プロローグの一枚絵も未準備（仮イラスト表示中）。本番の絵は主人公＝左 / ダンジョン＝右の構図で用意予定。
- **会話 UI の体裁**（日本語表示自体は 2026-09-11 に対応済み — §9-2「日本語フォント」。文字送り演出（1 文字ずつ表示など）は無し。常用漢字外の漢字は現状のフォントサブセットに無いので表示できない）。
- **UI の日本語化**（メニューは今は英語のまま。日本語にする場合、`Text` はレンダリングだけなら §9-2 のフォントを流用できるが、見た目を作り込むなら TMP 移行も検討）。
- 結果画面 / メニューの見た目（配置・色は最小限）。
- **ロック中ステージの見た目**は Unity 既定のグレーアウトのみ（「LOCKED」表記や鍵アイコンは未実装）。クリア進捗のセーブは `PlayerPrefs` の 1 キーだけ（スロット/複数セーブ無し）。
- `StageManager.nextButton` の表示可否は `GameFlow.CurrentStageIndex` 依存。エディタでステージシーンを直接 Play すると index=0 扱いになる（フロー経由なら正しい）。
- ボスの行動（今の `Enemy_Boss` は HP が多いだけの巡回。攻撃パターン無し）。1 発 1 ダメージ・攻撃CT 2秒なので撃破は単調。
- **地面 / 高台 Tilemap の本番タイル素材**（今は仮の `GroundTile.png` と `PlatformTop*.png`/`PlatformPillar*.png`。90×90px / PPU 90 を保って上書きすれば差し替わる。高台は天面・柱それぞれ左/中央/右で見た目を区別できる本番絵を想定）。
- **高台（Tilemap 版）は現状 Stage3 に 1 基のみ**。複数配置する場合、`OneWayPlatform` の着地判定が `_col.bounds`（＝合成コライダー全体の外接矩形）ベースなので、同じ `Platform` Tilemap 上に離れた高台を複数置くと bounds が全体を覆ってしまい正しく判定できない。複数基必要になったら高台ごとに別の `Platform` GameObject（別 Tilemap）に分けること。
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
