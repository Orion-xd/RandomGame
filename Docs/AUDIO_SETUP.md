# Audio setup

`GameAudio` is generated automatically. Scene and prefab setup is not required.
Stage BGM fades in for one second after scene transitions, opening dialogue, and the initial input lock have finished.
If a scene has no active `AudioListener` (such as `Prologue`), `GameAudio` enables its fallback listener automatically.

## BGM

Put BGM files in `Assets/Resources/Audio/BGM/`. Current assignments:

- `Forest.mp3`: `Stage1`, `Stage2_test`
- `Cave.mp3`: `Stage3_test`
- `Boss.mp3`: `Stage4_new`

`Title`, `Prologue`, and `StageSelect` do not have BGM assigned yet.

Recommended import settings: Streaming, Vorbis, stereo.

## Sound effects

The following sound effects are connected:

- `Jump.mp3`: jump
- `Dash.mp3`: dash
- `Attack.mp3`: attack
- `Slash.mp3`: player damage
- `Dead.mp3`: player death
- `Fall.mp3`: falling game over
- `Clear.mp3`: stage clear
- `DialogueText.mp3`: loops while typewriter dialogue text is being revealed, including the game-start prologue
- `Button.mp3`: all Unity UI button clicks and keyboard submits
- `SlimeAttacked.mp3`: Enemy2 projectile attack

Stage BGM stops immediately when the stage is cleared so the clear sound plays on its own.

These imported clips are intentionally not connected yet because their owners overlap other work:

- `WalkCave.mp3`, `WalkForest.mp3`: movement or ambience

Recommended import settings: Decompress On Load, ADPCM or PCM, mono.

The extension can be `.wav`, `.mp3`, `.ogg`, or `.aiff`. Missing files are treated as silence, so clips can be added one at a time.
