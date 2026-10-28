# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A Formula racer in the style of Namco's 1982 arcade game Pole Position, in C# on .NET 10 and Avalonia 12, drawn
through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in
it is original: the circuits and the names on the boards are this game's own, the road and cars are drawn in
code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPolePosition.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPolePosition.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAPolePosition          # play it; --no-audio for silence
POLE_SHOTS=$PWD/Images dotnet test tests/AVAPolePosition.Tests --filter ScreenshotTests   # refresh the README's pictures
POLE_PROBE=1 dotnet test tests/AVAPolePosition.Tests --filter DriverProbe   # how the computer driver gets on (for tuning)
python3 tools/make_sounds.py                      # rebuild src/AVAPolePosition/Sounds (standard-library Python only)
```

`DriverProbe` does nothing unless `POLE_PROBE` is set (to anything).

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPolePosition/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the race meeting's phases:
    the lights, the qualifying lap, the grid and the race.
  - `GameEngine.Play.cs` steps the driving: speed, the two gears, steering against the bends, the other cars,
    crashes, laps and the clock.
  - `Track.cs` builds each circuit as a loop of road segments, with billboards, trees and chevrons beside it.
  - `Entities.cs` has the `Controls` record, the `Field` measures and fixed step, the road `Segment` and the cars.
  - `Driver.cs` is the computer driver, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPolePosition/Views/`: `GameCanvas` (split over partial files) projects the road, draws the frame and
  runs the 60 Hz timer.
- `src/AVAPolePosition/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAPolePosition.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and `DriverProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so tests can drive whole laps deterministically from a seed. Draw random numbers from the engine's
  seeded `_rng`, never from `Random.Shared`. A circuit's scenery comes from a `Random` seeded by its name, so
  it is the same every time.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it. Tests that want an empty road set the internal `Calm` flag.
- **Input goes through `InputHandler`.** Keys and stick boil down to `Controls(Steer, Gas, Brake, Shift)`:
  negative `Steer` is left, up (negative `dy`) is the accelerator and down is the brake. Test new controls
  through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAPolePosition`); tests
  load the table from a temporary folder (`TempFolder()`) and run silent (`Silent = true`).
- **Line endings.** Keep each file's existing line endings (`app.manifest` is CRLF) so diffs show only real
  changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list, the project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
