# Fish movement

Only fish movement and visibility change. Player input, capture-box size/speed, capture containment,
progress gain/loss and catch/escape thresholds remain in `FightController` as before.

## Tuning

Edit `Data/Fishing/profiles.json`, under each fight profile's `movement` object. Every fish in
`fish_species.json` now has an explicit `fightProfileId`. Both natural encounters (including variants)
and scripted tutorial encounters carry a copy of that profile's settings into the fight.
Stable, Active, Sprint and Heavy are initial presets, not final balance decisions.

Distances and speeds are measured against the fishing panel's **shorter dimension**. A cruise speed
of 0.4 moves 40% of that dimension per second. Angles are degrees and durations are seconds.

| Parameter | Meaning |
| --- | --- |
| CruiseSpeed | Normal target swimming speed. |
| SpeedVariation | Fractional speed variation, 0–1, sampled at steering decisions. |
| Acceleration | Maximum speed change per second, also used when slowing after a burst. |
| MaxTurnRate | Normal turning limit in degrees/second. |
| SteeringInterval | Typical time between swimming intentions, randomized by ±30%. |
| WanderAngle | Maximum heading change requested by a wandering decision. |
| EdgeAvoidanceDistance | Distance at which anticipatory inward steering takes effect. |
| BurstSpeedMultiplier | Multiplier on current cruise target during a burst. |
| BurstDuration | Typical burst duration, randomized by ±30%; acceleration is included in this time. |
| BurstInterval | Typical normal-swimming time between bursts, randomized by ±30%; 0 disables bursts. |
| BurstTurnRate | Turning limit during a burst; turns and acceleration happen together. |
| DiveRate | Expected dive starts per minute of **visible** fighting; 0 disables dives. |
| DiveDuration | Fixed invisible time per dive; 0 disables dives. |
| AvoidanceStrength | Blend weight, 0–1, of nearby box-exit steering into wandering. |
| ReactionDelay | Age of the box position used by normal avoidance (0–5 seconds). |
| BurstCounterTurnChance | Probability, 0–1, that a burst turns opposite the box's previous-frame movement. |

There is no recovery phase. The fish returns toward cruise speed after each burst.
Normal avoidance favours a nearby edge of the box with angular variation rather than always running
directly away from its centre. It only updates at steering decisions. Counter-turns commit to the
opposite of the previous frame's box velocity with ±15 degrees of variation; a stationary box does
not provide a counter direction. Steering limits prevent instantaneous reversals.

Edge steering takes priority over other intentions and can turn at up to 240 degrees/second when
needed. Position clamping is a final containment safety net, not a reflection/bounce mechanic.

## Dive events

Diving is independent of cruising and bursts. An exponential visible-time clock schedules each event:
`delay = -ln(1-u) * 60 / DiveRate`, where `u` is uniform in [0,1).
Events can repeat during the same fight but cannot overlap. The rate stays constant; longer fights
provide more opportunities rather than increasing the rate. Hidden time does not count toward the
next dive. Visibility changes immediately, using only the two exposed dive settings.

The hidden fish continues moving and is evaluated by the same capture/progress rules. Its RNG for
diving is separate from its movement RNG: enabling diving does not alter its trajectory. Reset,
completion and restarting restore the cursor's visibility. Pausing stops both clocks with movement.

## Reproducing movement and checking changes

`FightController.StartFight(settings, seed)` accepts an optional seed and exposes the active
`MovementSeed`. Reproduction requires the same seed, settings, panel geometry, frame steps and
box-input history; the seed alone is not enough because the fish responds to the player.

Run the dependency-free simulation and repository checks with the .NET 10 SDK:

```powershell
dotnet run --project Tests/MovementChecks/MovementChecks.csproj
```

The checks use the actual fishing JSON and repository, substituting only Godot's resource-file API.
They check profile propagation, bounds at multiple frame rates, acceleration limits, seeded replay,
repeated dives, hidden duration/movement, dive independence, avoidance and previous-frame counter-turns.
They do not substitute for a Godot build or an interactive playtest.

Open `Scenes/FishingTest.tscn` in Godot and run the current scene (F6) for direct movement testing.
The old bait/depth/bite sequence is replaced by three selectable setups: Gentle (`fight.stable`),
Active (`fight.active`) and Sprint (`fight.sprint`). The setup screen displays all 16 parameters.
Choose a seed, start the fight, then use the same-seed retry or stop and select another setup.
Seeded retries reproduce movement only when the player also supplies the same input history.

The capture box turns green when the fish centre is inside, and coral when outside. Both its fill
and outline change; containment/progress use the same check, including when the fish is invisible.
Each controller duplicates its style so feedback cannot recolour another fight's shared resource.
The test also displays tracking/visibility state in words. This feedback applies to story fights too.

Verify interactively that a hidden fish can still gain/lose catch progress, pause/resume preserves
the current dive, and a new attempt always starts visible.

Settings are copied per encounter so a future difficulty selector can adjust them without changing
the shared species definitions. This change does not train a model, collect player history or add
automatic ML adaptation.
