# Training the ARGMAX commander

The commander is a macro policy: every second of game time it observes `CommanderObs` (303 floats: economy,
armies, threats, metaprogram sensors and 8×8 value maps of both swarms, the Noise and scrap, mirrored so both
seats see "me at the bottom left") and picks a `MetaDecision`: **produce** (7) × **spawn** (4) × **beacon**
(7) × **army** (7). Those are the same four knobs an Argot metaprogram or Jev turns, so a trained model can
replace either.

It trains in the gym: N arenas of self-play on the **same simulation the server runs** (bit-identical, see
the golden-hash tests). What it learns offline is what it faces online.

## Setup (once)

```bash
ml/setup.sh
```

This creates `ml/.venv` with Python 3.10.12, ML-Agents release_23 (matches the Unity package 4.1.0) and torch
2.8. The PyPI `mlagents==1.1.0` pins a grpcio with no Apple-silicon wheel, which is why it installs from the
release tag instead.

## Train

Headless, no window:

```bash
scripts/train.sh argmax-v2 60 8
```

The arguments are the run id, the minutes to train, and the number of arenas.

That starts `mlagents-learn ml/config/commander.yaml`, then a batch-mode Unity editor running the gym
(`ArgmaxTools.Train`). When time is up the trainer exports `ml/results/<run>/Commander.onnx`, and the script
copies it to `client/Assets/Argmax/Resources/Argmax/Brains/Commander.onnx`.

To watch it train instead, start the trainer and press GYM in the game:

```bash
ml/.venv/bin/mlagents-learn ml/config/commander.yaml --run-id=argmax-v2 --results-dir=ml/results
```

Follow the progress with TensorBoard:

```bash
ml/.venv/bin/tensorboard --logdir ml/results
```

## Rewards: the genie is the point

`GymSession.RewardSpec`:

| term | default | |
|---|---|---|
| `Win` | ±1 | terminal, zero-sum |
| `Trade` | 0.0005 × (value killed − value lost) | per decision interval |
| `Mining` | 0.0003 × scrap mined | per decision interval |
| `Lead` | ±0.5 | at the 6-minute cap, to the side with more total value |

Push `Mining` up and you will breed an economy that never fights. Push `Trade` and you get kill-farming
around the wells. That's reward hacking, the genie problem, and the gym is where you get to watch it happen.

## What a short run looks like

The first run on this machine (10 minutes, 8 arenas, about 105k steps, CPU) went end to end: trainer
connected (`Commander?team=0/1`), self-play swaps, checkpoints exported. The early policy mostly learns economy
shaping, and every episode timed out as a draw. Expect hours, not minutes, before it beats OVERMIND. Bootstrap
with behavioral cloning from OVERMIND demonstrations (see the commented block in the config) to skip the
flailing phase.

## Using the model

In the menu choose *brain: ML-Agents*. In a skirmish your client claims the AI seat (`SetPilot`) and drives it
with the model through the Issue reducer. In the sandbox it drives the local world. Without a model file the
pilot falls back to the OVERMIND heuristic, so it always plays.
