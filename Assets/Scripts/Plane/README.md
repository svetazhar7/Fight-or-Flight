# Plane Control System

Co-op physics-comedy plane controller for **Unity 6 + FishNet 4.5.x (Prediction V2)**.
Server-authoritative, client-predicted by the seated pilot.

**Design rule: MODEL-INDEPENDENT physics.** Every force/torque (thrust, lift, drag,
control, stability, suspension) is applied at the **centre of mass** with `AddForce`/
`AddTorque` — never at a wing/propeller/wheel transform. The COM is auto-centred on the
airframe collider, the rotational inertia is a fixed value set in code, and the hull gets
a friction-less surface — all in `PlaneController.Awake`. So you can swap the visual model
freely; the only model-specific knobs are the two local axis vectors (nose / up) on
`PlaneController`. Forces applied off-COM (e.g. thrust at a propeller offset) are what
sent the old build spinning in circles — don't reintroduce them.

## Layer map

```
Core/         PlaneController      – brain: tick loop, mode SM, reconcile (the spine)
              PlaneControlMode     – Ground | Flight
              PlaneControlState    – semantic per-tick commands
              PlaneModifierState   – aggregated lift/drag/thrust/control multipliers
              PlaneNetworkData     – Replicate/Reconcile structs
Engine/       PlaneEngine          – throttle, thrust, RPM, boost
              PlaneEngineConfig    – ScriptableObject tuning
Aerodynamics/ PlaneFlightModel     – arcade lift/drag + control torque + stability (all at COM)
              AeroSurface          – LEGACY per-wing lift; NOT used by flight anymore, kept
                                     only for damage visuals (AeroPart drives Effectiveness)
Ground/       PlaneGroundModel     – COM ground-probe + steer/brake/grip/keep-upright (no wheels)
Input/        PlaneInputReader     – New Input System → raw snapshot (owner only)
              PlaneRawInput
Damage/       IDamageable, IPlaneCondition
              PlanePart            – base: health + attachment SyncVars, detach + debris
              AeroPart             – wing part → drives AeroSurface
              PropellerPart        – prop part → flames out engine
              PlaneDamageSystem    – condition aggregation + part-broken event bus
              Conditions/IcingCondition, EngineOverheatCondition  – examples
Cockpit/      CockpitControlAnimator – stick tilt + wheel rotation (cosmetic)
              PropellerAnimator      – prop spin (cosmetic)
              PilotSeat              – sit/stand → GiveOwnership/RemoveOwnership
```

### Per-tick order (inside `PlaneController.RunSimulation`, the `[Replicate]`)
1. `DamageSystem.ComposeModifiers` → rebuild multipliers (ice/heat/cargo/damage)
2. set `centerOfMass` = auto-centred COM + flight offset + cargo offset
3. `InterpretInput` → control state for the current mode
4. `Engine.Tick` → thrust at COM along the nose axis
5. `Ground.Probe` (COM down-ray, grounded? ) (+`Ground.Tick` if grounded)
6. `Flight.Tick` → lift + drag + control torque + weather-vane/auto-level (all at COM)
7. `body.Simulate()` → one physics step
8. `UpdateMode` → Ground ⇄ Flight

> **Golden rule:** subsystems have **no Update/FixedUpdate**. Everything runs inside the
> replicate so reconcile can replay it. Don't add independent physics loops.

---

## Scene / prefab setup

### 1. Plane root (one GameObject = one NetworkObject)
Add: `Rigidbody`, `NetworkObject`, `PlaneController`, `PlaneEngine`, `PlaneFlightModel`,
`PlaneGroundModel`, `PlaneDamageSystem`, `PlaneInputReader`.

Also add **one Collider** on the root (a convex `MeshCollider` or a primitive) — it is the
airframe hull: it rests on the runway, auto-centres the COM, and is made friction-less in
code. Set the Rigidbody **Mass** (e.g. 1500–2500). Everything else on the Rigidbody
(damping, interpolation, collision mode, **inertia tensor**, **centre of mass**, physics
material) is **applied in `PlaneController.Awake`** — you don't tune it in the Inspector,
and it survives a model swap.

`PlaneController` references: drag in Input / Engine / Flight / Ground / Damage from this
same object. Set **Local Forward / Local Up** to match the model's nose/up in its own local
space (this rig: nose `(0,-1,0)`, up `(0,0,1)`). Set **Rotation Speed (Vr)** = the ground
speed below which the nose won't rise. Tune **Inertia Tensor** for how heavy it turns.

### 2. Wings — OPTIONAL (flight no longer needs them)
Lift/drag is now a single arcade force at the COM, so **you do not place `AeroSurface`s for
flight to work**. Wings are only relevant if you want damageable parts: add `AeroPart`
(+ `AeroSurface`) per wing and add them to `PlaneDamageSystem ▸ Parts` for the detach/debris
visuals. Note `AeroSurface.Effectiveness` no longer feeds the flight math — if you want wing
damage to actually degrade flying, write a condition that lowers `PlaneModifierState`'s
`LiftMultiplier` / `ControlMultiplier` (see "How to extend").

### 3. Propeller
Thrust is applied at the COM along the nose axis, so `PlaneEngine ▸ Thrust Point` is now
**cosmetic only** (leave it empty, or point it at the hub if you spawn prop FX there).
Create the engine config (below) and assign it. Add `PropellerAnimator` (link PlaneController
+ propeller transform). Optionally `PropellerPart` (link engine + animator) and add to `Parts`.

### 4. Cockpit
- `CockpitControlAnimator`: link PlaneController, ControlStick, ControlWheel. Check the
  tilt/spin axes match your meshes.
- `PilotSeat` (on the same NetworkObject): link the PlaneController and PilotViewPoint.
  Call `RequestSit()` / `RequestStand()` from your interaction system.

### 5. Wheels — not needed
There are no wheel transforms anymore. `PlaneGroundModel` casts a single ray straight down
from the COM (ignoring the plane's own colliders) to know it's grounded, and the hull slides
friction-lessly on the runway. Just make sure `Ground Mask` includes the runway layer
(default `Everything` works). Wheel meshes can stay as pure visuals.

---

## Create the engine config
`Assets ▸ Create ▸ Fight or Flight ▸ Plane ▸ Engine Config`, then assign it to
`PlaneEngine ▸ Config`. Without it the engine produces no thrust (it logs nothing, just
idles), so don't skip this.

## NetworkObject / Prediction settings
On the plane's `NetworkObject`, enable **Prediction** and set it up for a predicted
rigidbody. The simplest reliable reference is the bundled demo:
`Assets/FishNet/Demos/Prediction/Rigidbody/` — open it, select the predicted vehicle, and
mirror its NetworkObject prediction fields (enable prediction, graphical object for
smoothing/interpolation). **Do not** add a `NetworkTransform` to the plane — reconcile
already syncs the transform.

## Input bindings (New Input System)
On `PlaneInputReader`, edit the inline actions in the Inspector:
- **Move** (Value / Vector2): a 2D-Vector composite → W=up, S=down, A=left, D=right.
- **ActionQ** (Button): Q.
- **ActionE** (Button): E.

Mapping is contextual (handled in `PlaneController.InterpretInput`):
| Key | Ground | Flight |
|-----|--------|--------|
| W/S | throttle + / − | pitch up / down |
| A/D | nose-wheel left / right | roll left / right |
| Q   | brake | throttle down |
| E   | boost | throttle up |

---

## Tuning guide (all knobs are plain numbers — no curves, no per-wing setup)
Rule of thumb: **take-off speed ≈ sqrt(Mass × 9.81 / `Lift Per Speed Sqr`)**, and
**top speed ≈ sqrt(`MaxThrust` / `Drag Per Speed Sqr`)** (then `Max Speed` is a hard cap).

- **Won't take off / takes the whole map:** raise `Lift Per Speed Sqr` (PlaneFlightModel),
  raise engine `MaxThrust`, or lower `Drag Per Speed Sqr`. Lower `Vr` so you can rotate sooner.
- **Balloons up / hard to keep level:** lower `Max Lift` toward the weight (Mass × 9.81);
  ~1.2–1.5× weight is a sweet spot.
- **Controls feel sluggish or twitchy:** controls are torque ÷ inertia, so tune them together.
  `Control Power` (x=pitch, y=yaw, z=roll) on PlaneFlightModel vs `Inertia Tensor` on
  PlaneController. Lower inertia OR raise control power = snappier. Also `Control Ref Speed`
  = airspeed for full authority.
- **Nose won't hold up / fights you at speed:** lower `Weather Vane` (it's already capped at
  `Control Ref Speed` so it can't beat the stick, but a high value still stiffens it).
- **Won't settle / wobbles or spins after a hit:** raise `Angular Damping` (PlaneController).
  Hard spin cap is `Max Angular Velocity`.
- **Doesn't return to level / over-corrects:** tune `Auto Level` (0 = fully manual roll).
- **Slides around on the ground / tips over:** raise `Lateral Grip`, raise `Upright Torque`,
  lower `Steer Torque`. The hull is friction-less by design — grip comes from these.
- **Runs away to silly speeds:** that's what `Max Speed` is for; lower it.

## How to extend (the whole point)
- **New ongoing effect** (fire, storm, fuel starvation, control loss): make a
  `NetworkBehaviour : IPlaneCondition`, sync intensity with `SyncVar<T>`, write into
  `PlaneModifierState` in `Contribute`, add it to `PlaneDamageSystem ▸ Condition Behaviours`.
  The flight model never changes. See `IcingCondition` / `EngineOverheatCondition`.
- **New detachable part** (tail, gear, cargo door): subclass `PlanePart`, override
  `OnAttachedChanged` / `OnHealthChanged`, add to `Parts`.
- **Cargo / centre-of-mass shift:** a condition that writes `CenterOfMassOffset` / `MassDelta`.
- **Turbulence / gusts** (direct force, not just a multiplier): add an optional
  `ApplyForces(PredictionRigidbody, dt)` hook called next to `ComposeModifiers`.

## MVP gaps / next steps (intentionally out of scope)
- **Passengers riding the moving plane** — needs a predicted-platform solution; design a
  `IRidingPlatform` rather than bolting it onto the controller.
- **Seat occupancy on clients** — `PilotSeat.Occupant` is server-side; sync the pilot's
  ClientId if the UI needs it.
- **Reconciling fast effect state** (heat) — currently SyncVar; move into ReconcileData if
  you need frame-perfect replays of it.
- **Apply `MassDelta`** where you set `rb.mass` once cargo is implemented.
```
