using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Collectibles;
using NeonVelocity.Effects;
using NeonVelocity.Utilities;
using NeonVelocity.World;
using UnityEngine;

namespace NeonVelocity.Player
{
    public struct PowerUpStatus
    {
        public PowerUpType Type;
        public string Label;
        public float Remaining;
        public int Charges;
        public Color Color;
        public bool Timed;
    }

    public struct RunnerReviveState
    {
        public float Distance;
        public int Lane;
        public float Height;
        public float VerticalVelocity;
        public bool Sliding;
        public float Speed;
    }

    /// <summary>
    /// Deterministic three-lane runner. Movement, jumping, sliding, collisions, pickups and
    /// power-ups are resolved against explicit swept bounds rather than Unity physics, which
    /// keeps the game frame-rate independent and lets the same rules be tested headlessly.
    /// </summary>
    public sealed class RunnerController : MonoBehaviour
    {
        public ModeSpec Mode { get; private set; }
        public GameConfig Config { get; private set; }
        public TrackManager Track { get; private set; }
        public RunnerVisuals Visuals { get; private set; }
        public float Distance { get; private set; }
        public float Speed { get; private set; }
        public float Elapsed { get; private set; }
        public int Lane { get; private set; } = 1;
        public float PlayerZ { get; private set; }
        public float LaneX { get; private set; }
        /// <summary>Lateral route displacement applied by curved junctions.</summary>
        public float RouteOffsetX { get; private set; }
        /// <summary>Full rendered X including the current junction curve.</summary>
        public float RouteX => _x + _routeOffset;
        public int Coins { get; private set; }
        public int Lives { get; private set; }
        public int Jumps { get; private set; }
        public int Slides { get; private set; }
        public int NearMisses { get; private set; }
        public int PowerUpsUsed { get; private set; }
        public bool Running { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsGrounded => _grounded;
        public bool IsSliding => _slideTimer > 0f;
        public float Height => _y;
        public int ShieldCharges => _shieldCharges;
        public int HoverboardCharges => _hoverboardCharges;
        public float PreferredTimeScale => _slowMotionTimer > 0f && !GameModes.IsOnline(Mode.Id) ? 0.64f : 1f;
        public float SpeedNormalised => Mathf.Clamp01((Speed - Config.baseSpeed) / Mathf.Max(1f, Config.maxSpeed - Config.baseSpeed));
        public string AbilityId { get; private set; } = "";
        public string AbilityName { get; private set; } = "";
        public bool HasAbility => !string.IsNullOrEmpty(AbilityId);
        public bool AbilityActive { get; private set; }
        public float AbilityCooldownRemaining { get; private set; }
        public float AbilityCooldownDuration { get; private set; } = 1f;
        public float AbilityReadyFraction => HasAbility
            ? Mathf.Clamp01(1f - AbilityCooldownRemaining / Mathf.Max(0.01f, AbilityCooldownDuration))
            : 0f;
        public JunctionPhase CurrentJunctionPhase { get; private set; }
        public JunctionKind CurrentJunctionKind { get; private set; }
        public JunctionBranch JunctionCommitment { get; private set; }
        public float JunctionLean { get; private set; }
        public int JunctionsTaken { get; private set; }
        public string JunctionLabel => _junctionState.Plan != null ? _junctionState.Plan.DisplayName : "";

        public int Score
        {
            get
            {
                float distanceScore = Distance * 10f * Mathf.Max(0.1f, Mode.ScoreMultiplier) * ScoreMultiplier;
                return Mathf.Max(0, Mathf.FloorToInt(distanceScore) + _coinScore + _bonusScore);
            }
        }

        public float ScoreMultiplier
        {
            get
            {
                float upgrade = 1f + App.Progression.Profile.GetUpgradeLevel("multiplier") * 0.03f;
                float timed = _multiplierTimer > 0f ? 2f : 1f;
                float phase = _phaseTimer > 0f ? 1.5f : 1f;
                return upgrade * timed * phase;
            }
        }

        public event Action<int> CreditsCollected;
        public event Action<PowerUpDefinition> PowerUpCollected;
        public event Action<int> NearMiss;
        public event Action Crashed;
        public event Action Died;
        public event Action WorldRebased;
        public event Action<float> Landed;
        public event Action<string> AbilityActivated;

        private GameApp App;
        private InputRouter _input;
        private VfxService _vfx;
        private SpeedModel _speedModel;
        private float _x;
        private float _xStart;
        private float _xTarget;
        private float _routeOffset;
        private int _lastLaneDirection = 1;
        private float _laneTimer;
        private float _y;
        private float _vy;
        private bool _grounded = true;
        private float _jumpBufferTimer;
        private float _coyoteTimer;
        private float _slideTimer;
        private float _invincibleTimer;
        private float _multiplierTimer;
        private float _boostTimer;
        private float _magnetTimer;
        private float _coinRushTimer;
        private float _phaseTimer;
        private float _airJumpTimer;
        private float _slowMotionTimer;
        private int _shieldCharges;
        private int _hoverboardCharges;
        private int _airCharges;
        private int _bonusScore;
        private int _coinScore;
        private int _lives;
        private float _prevZ;
        private float _worldShifted;
        private float _rebaseDistance;
        private float _stepTimer;
        private int _stepIndex;
        private int _magnetLevel;
        private int _shieldLevel;
        private int _boostLevel;
        private int _headStart;
        private CharacterDefinition _character;
        private float _speedBias = 1f;
        private float _abilityActiveTimer;
        private float _abilityDuration;
        private int _abilityLevel;
        private readonly List<Obstacle> _obstacles = new List<Obstacle>(48);
        private readonly List<EnergyPickup> _credits = new List<EnergyPickup>(64);
        private readonly List<PowerUpPickup> _powerUps = new List<PowerUpPickup>(6);
        private readonly HashSet<int> _nearMissSeen = new HashSet<int>();
        private readonly Dictionary<PowerUpType, float> _effectTimers = new Dictionary<PowerUpType, float>();
        private JunctionState _junctionState;
        private float _junctionLean;
        private int _junctionPlanSegment = -1;
        private readonly List<PowerUpStatus> _activeEffects = new List<PowerUpStatus>(5);

        public IReadOnlyList<PowerUpStatus> ActiveEffects
        {
            get
            {
                RefreshEffects();
                return _activeEffects;
            }
        }

        public void Begin(GameApp app, GameConfig config, ModeSpec mode, TrackManager track, InputRouter input,
            int seed)
        {
            App = app;
            Config = config;
            Mode = mode;
            Track = track;
            _input = input;
            _speedModel = config.CreateSpeedModel();
            _speedModel.SetBoost(1f);
            _magnetLevel = app.Progression.Profile.GetUpgradeLevel("magnet");
            _shieldLevel = app.Progression.Profile.GetUpgradeLevel("shield");
            _boostLevel = app.Progression.Profile.GetUpgradeLevel("boost");
            _headStart = Mathf.RoundToInt(ProgressionMath.UpgradeValue("head_start",
                app.Progression.Profile.GetUpgradeLevel("head_start")));
            _rebaseDistance = Mathf.Max(1000f, config.worldOriginRebaseDistance);
            _lives = Mathf.Max(1, mode.StartingLives);
            Lane = 1;
            LaneX = 0f;
            _x = 0f;
            _xStart = 0f;
            _xTarget = 0f;
            _routeOffset = 0f;
            RouteOffsetX = 0f;
            _lastLaneDirection = 1;
            Distance = Mathf.Max(0, _headStart);
            _prevZ = 8f + Distance;
            PlayerZ = _prevZ;
            Running = false;
            IsDead = false;
            _stepTimer = 0.16f;
            _stepIndex = 0;
            ResetJunctionState();
            JunctionsTaken = 0;

            var character = config.Character(app.Progression.Profile.selectedCharacter);
            var trail = config.Trail(app.Progression.Profile.selectedTrail);
            _character = character;
            _speedBias = character != null ? Mathf.Clamp(character.speedBias, 0.9f, 1.1f) : 1f;
            if (character != null && !string.IsNullOrEmpty(character.abilityId))
            {
                AbilityId = character.abilityId;
                AbilityName = string.IsNullOrEmpty(character.abilityName)
                    ? character.abilityId.Replace('_', ' ').ToUpperInvariant()
                    : character.abilityName;
                string upgradeId = AbilityId == "nova_burst" ? "nova_effect" : "phase_effect";
                _abilityLevel = Mathf.Clamp(app.Progression.Profile.PremiumUpgradeLevel(upgradeId), 0, 3);
                _abilityDuration = Mathf.Max(0.6f, character.abilityDuration +
                    (AbilityId == "nova_burst" ? _abilityLevel * 0.35f : _abilityLevel * 0.2f));
                AbilityCooldownDuration = Mathf.Max(8f, character.abilityCooldown - _abilityLevel * 1.6f);
                AbilityCooldownRemaining = 0f;
                // NOVA TITAN's defensive trade-off: one shield charge exists from the first
                // metre, but the frame is slower and its ability cooldown is the longest.
                if (character.defenseBias > 0.5f) _shieldCharges = Mathf.Max(_shieldCharges, 1);
            }
            var visualGo = new GameObject("Runner Visual");
            visualGo.transform.SetParent(transform, false);
            Visuals = visualGo.AddComponent<RunnerVisuals>();
            Visuals.Build(character, trail, false, "");
            Visuals.SetPose(RunnerPose.Idle);

            Track.Tick(Distance);
            if (Distance > 0f)
            {
                Track.ClearHazardsAround(PlayerZ, 16f);
                _invincibleTimer = 1.4f;
            }
            transform.position = new Vector3(LaneX, 0f, PlayerZ);
            Subscribe();
        }

        public void BindVfx(VfxService vfx) => _vfx = vfx;

        private void Subscribe()
        {
            if (_input == null) return;
            _input.LaneRequested += RequestLane;
            _input.JumpRequested += RequestJump;
            _input.JumpReleased += ReleaseJump;
            _input.SlideRequested += RequestSlide;
            _input.AbilityRequested += ActivateAbility;
        }

        public void StopInput()
        {
            if (_input == null) return;
            _input.LaneRequested -= RequestLane;
            _input.JumpRequested -= RequestJump;
            _input.JumpReleased -= ReleaseJump;
            _input.SlideRequested -= RequestSlide;
            _input.AbilityRequested -= ActivateAbility;
        }

        /// <summary>
        /// Fires the premium frame's signature ability. The two abilities are balanced
        /// sidegrades: PHANTOM X spends its cooldown on a short untouchable dash, while NOVA
        /// TITAN clears the hazards around it and refreshes a single shield charge. Neither
        /// grants score, credits or XP.
        /// </summary>
        public void ActivateAbility()
        {
            if (!Running || IsDead || !HasAbility || AbilityCooldownRemaining > 0.001f) return;
            AbilityActive = true;
            _abilityActiveTimer = _abilityDuration;
            AbilityCooldownRemaining = AbilityCooldownDuration;
            switch (AbilityId)
            {
                case "phase_dash":
                    _invincibleTimer = Mathf.Max(_invincibleTimer, _abilityDuration);
                    _speedModel.SetBoost(1.22f);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.95f, PlayerZ), Palette.Cyan, 30, 0.45f);
                    break;
                case "nova_burst":
                    Track?.ClearHazardsAround(PlayerZ, 13f);
                    _shieldCharges = Mathf.Max(_shieldCharges, 1);
                    _invincibleTimer = Mathf.Max(_invincibleTimer, 0.6f);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.9f, PlayerZ), Palette.Amber, 42, 0.7f);
                    break;
                case "echo_pulse":
                    _magnetTimer = Mathf.Max(_magnetTimer, 5f);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.9f, PlayerZ), Palette.Cyan, 34, 0.6f);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.9f, PlayerZ + 6f), Palette.Lime, 24, 0.8f);
                    break;
                case "night_veil":
                    _invincibleTimer = Mathf.Max(_invincibleTimer, _abilityDuration);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.95f, PlayerZ), Palette.Violet, 28, 0.5f);
                    break;
                case "solar_aegis":
                    _shieldCharges = Mathf.Max(_shieldCharges, 1);
                    _invincibleTimer = Mathf.Max(_invincibleTimer, 1.5f);
                    _vfx?.Burst(new Vector3(RouteX, _y + 0.95f, PlayerZ), Palette.Amber, 38, 0.65f);
                    break;
            }
            App?.Audio.PlaySfx("sfx_ability", 0.9f);
            AbilityActivated?.Invoke(AbilityName);
        }

        private void TickAbility(float dt)
        {
            if (AbilityCooldownRemaining > 0f)
                AbilityCooldownRemaining = Mathf.Max(0f, AbilityCooldownRemaining - dt);
            if (_abilityActiveTimer > 0f)
            {
                _abilityActiveTimer = Mathf.Max(0f, _abilityActiveTimer - dt);
                if (_abilityActiveTimer <= 0f && AbilityActive)
                {
                    AbilityActive = false;
                    if (AbilityId == "phase_dash")
                        _speedModel.SetBoost(_phaseTimer > 0f ? 1.3f
                            : (_boostTimer > 0f ? 1.2f + _boostLevel * 0.025f : 1f));
                }
            }
        }

        public void StartRunning()
        {
            Running = true;
            IsDead = false;
            Visuals?.SetPose(RunnerPose.Run);
        }

        /// <summary>
        /// Grants an extra starting distance for revival runs. Call after Begin and before
        /// StartRunning; the track regenerates ahead and the runner gets a shield window.
        /// </summary>
        public void GrantHeadStart(float distance)
        {
            float bonus = Mathf.Max(0f, distance - Distance);
            if (bonus <= 0f || Track == null) return;
            Distance += bonus;
            _prevZ = 8f + Distance;
            PlayerZ = _prevZ;
            transform.position = new Vector3(LaneX, 0f, PlayerZ);
            Track.Tick(Distance);
            Track.ClearHazardsAround(PlayerZ, 16f);
            // 3.6 s covers the launch countdown itself plus the first moment of movement,
            // so a revived runner never gets clipped by an uncleared hazard at the seam.
            _invincibleTimer = 3.6f;
        }

        public void Finish(bool victory)
        {
            Running = false;
            if (Visuals != null) Visuals.SetPose(victory ? RunnerPose.Victory : RunnerPose.Run);
        }

        public void Kill()
        {
            if (IsDead) return;
            IsDead = true;
            Running = false;
            if (_slowMotionTimer > 0f) StopSlowMotion();
            if (Visuals != null) Visuals.SetPose(RunnerPose.Dead);
            Died?.Invoke();
        }

        /// <summary>
        /// Captures the exact kinematic state at the moment of death. The revive service
        /// debits GEMS server-side, then this snapshot is restored without rebuilding the
        /// world, rerolling the track or resetting collected score.
        /// </summary>
        public RunnerReviveState CaptureReviveState() => new RunnerReviveState
        {
            Distance = Distance,
            Lane = Lane,
            Height = _y,
            VerticalVelocity = _vy,
            Sliding = _slideTimer > 0f,
            Speed = Speed
        };

        /// <summary>
        /// Restores a captured crash state at the same metre, lane and vertical pose. A short
        /// server-upgraded stabilisation window prevents the killing hazard from immediately
        /// re-triggering while the runner accelerates clear of it.
        /// </summary>
        public void ReviveAt(RunnerReviveState state, float invulnerabilitySeconds)
        {
            if (Track == null) return;
            Distance = Mathf.Max(0f, state.Distance);
            Lane = LaneMath.ClampLane(state.Lane);
            _x = LaneMath.LaneToX(Lane);
            _xStart = _x;
            _xTarget = _x;
            LaneX = _x;
            _laneTimer = 0f;
            _y = Mathf.Max(0f, state.Height);
            _vy = state.VerticalVelocity;
            _grounded = _y <= 0.01f;
            _slideTimer = state.Sliding && _grounded ? Mathf.Max(0.3f, Config.slideDuration * 0.7f) : 0f;
            _jumpBufferTimer = 0f;
            _coyoteTimer = _grounded ? 0.11f : 0f;
            _lives = Mathf.Max(1, _lives);
            Speed = Mathf.Max(Speed, Mathf.Max(Config.baseSpeed * 0.8f, state.Speed * 0.92f));
            _prevZ = 8f + Distance - _worldShifted;
            PlayerZ = _prevZ;
            Track.Tick(Distance);
            _nearMissSeen.Clear();
            _invincibleTimer = Mathf.Max(_invincibleTimer, Mathf.Max(1.5f, invulnerabilitySeconds));
            IsDead = false;
            Running = true;
            transform.position = new Vector3(RouteX, _y, PlayerZ);
            if (Visuals != null) Visuals.SetPose(RunnerPose.Run);
            _vfx?.Burst(new Vector3(RouteX, _y + 0.9f, PlayerZ), Palette.Cyan, 26, 0.5f);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            UpdateTimers(dt);
            if (Running)
            {
                TickAbility(dt);
                Elapsed += dt;
                Advance(dt);
            }
            else
            {
                UpdateIdleMotion(dt);
            }

            // Vertical and lateral motion are resolved before hazards are swept. A jump press
            // therefore raises the hitbox in the same frame it is registered, which matters at
            // top speed where the runner can cover more than half a metre per rendered frame.
            // Resolve the junction before lateral movement so a branch commitment clamps the
            // same frame's lane request. Both motion systems stay frame-rate independent.
            UpdateJunction(dt);
            UpdateLane(dt);
            UpdateRouteOffset(dt);
            UpdateVertical(dt);
            if (Running)
            {
                ResolveCollisions();
                ResolvePickups(dt);
            }

            UpdateFootsteps(dt);
            transform.position = new Vector3(RouteX, _y, PlayerZ);
            if (Visuals != null)
            {
                float lateralVelocity = _laneTimer > 0f ? (_xTarget - _xStart) / Mathf.Max(0.001f, Config.laneSwitchDuration) : 0f;
                RunnerPose pose = IsDead ? RunnerPose.Dead
                    : (!_grounded ? RunnerPose.Jump : (IsSliding ? RunnerPose.Slide : (Running ? RunnerPose.Run : RunnerPose.Idle)));
                Visuals.Tick(dt, SpeedNormalised, lateralVelocity, pose, _vy, _y);
                Visuals.SetShield(_shieldCharges > 0 || _invincibleTimer > 1.3f);
                Visuals.SetHoverboard(_hoverboardCharges > 0);
                Visuals.SetAura(_phaseTimer > 0f || _multiplierTimer > 0f || _invincibleTimer > 2.2f);
            }
        }

        private void UpdateIdleMotion(float dt)
        {
            if (!_grounded) return;
            // Idle stance has a tiny breathing bob handled by the visuals; nothing else to do.
        }

        private void UpdateFootsteps(float dt)
        {
            if (!Running || !_grounded || IsSliding || Speed < 2f) return;
            _stepTimer -= dt;
            if (_stepTimer > 0f) return;
            float cadence = Mathf.Lerp(0.3f, 0.155f, SpeedNormalised);
            _stepTimer += Mathf.Max(0.07f, cadence);
            _stepIndex++;
            bool metal = Track != null && Track.CurrentStyle == 3;
            App.Audio.PlayFootstep(metal, Mathf.Lerp(0.28f, 0.42f, SpeedNormalised), _stepIndex);
            if (_stepIndex % 2 == 0 && Speed > Config.baseSpeed * 1.25f)
                _vfx?.Dust(new Vector3(RouteX, 0.03f, PlayerZ - 0.1f), metal ? Palette.Cyan : Palette.White, 2);
        }

        private void Advance(float dt)
        {
            float target = _speedModel.SpeedAt(Distance, Elapsed) * Mode.SpeedMultiplier * _speedBias;
            Speed = Mathf.Lerp(Speed, target, 1f - Mathf.Exp(-dt * 2.8f));
            if (Speed <= 0.01f) Speed = Mathf.Max(4f, Config.baseSpeed * Mode.SpeedMultiplier);
            float advance = Speed * dt;
            Distance += advance;

            float newZ = 8f + Distance - _worldShifted;
            _prevZ = PlayerZ;
            PlayerZ = newZ;

            RebaseWorldIfNeeded();
            Track.Tick(Distance);
            if (_magnetTimer > 0f || _coinRushTimer > 0f || _phaseTimer > 0f)
            {
                Speed = Mathf.Min(Speed * 1.001f, Config.maxSpeed * 1.45f);
            }
        }

        private void RebaseWorldIfNeeded()
        {
            if (Distance - _rebaseDistance < 0f) return;
            float shift = _rebaseDistance;
            _rebaseDistance += Config.worldOriginRebaseDistance;
            _worldShifted += shift;
            PlayerZ -= shift;
            _prevZ -= shift;
            Track.ShiftWorld(-shift);
            WorldRebased?.Invoke();
        }

        private void ResetJunctionState()
        {
            _junctionState = default;
            _junctionPlanSegment = -1;
            CurrentJunctionPhase = JunctionPhase.None;
            JunctionCommitment = JunctionBranch.None;
            _junctionLean = 0f;
        }

        /// <summary>
        /// Reads the deterministic junction plan, commits the runner to the lane it occupies at
        /// the stop line and holds that branch until the merge. Signature cues fire exactly once
        /// per phase, and a new junction always clears the previous commitment.
        /// </summary>
        private void UpdateJunction(float dt)
        {
            JunctionPhase previousPhase = CurrentJunctionPhase;
            if (Track == null || !Track.GetJunctionState(Distance, out var state))
            {
                if (previousPhase != JunctionPhase.None) ResetJunctionState();
                _junctionLean = Mathf.MoveTowards(_junctionLean, 0f, dt * 5f);
                JunctionLean = _junctionLean;
                return;
            }

            if (_junctionPlanSegment != state.Plan.SegmentIndex)
            {
                _junctionPlanSegment = state.Plan.SegmentIndex;
                JunctionCommitment = JunctionBranch.None;
            }

            _junctionState = state;
            CurrentJunctionPhase = state.Phase;
            CurrentJunctionKind = state.Plan.Kind;

            if (state.Phase == JunctionPhase.Warning)
                JunctionCommitment = JunctionBranch.None;

            if (state.Phase == JunctionPhase.Commit && JunctionCommitment == JunctionBranch.None)
            {
                var branch = JunctionRules.CommitForLane(Lane, _lastLaneDirection, state.Plan);
                if (branch != JunctionBranch.None)
                {
                    JunctionCommitment = branch;
                    JunctionsTaken++;
                    App.Audio.PlaySfx("sfx_lane", 0.38f, 0.05f);
                    // The centre lane is blocked by a median at a side-choice junction. Steer
                    // the runner into the chosen branch during the commit approach instead of
                    // leaving an unavoidable collision at the divider.
                    int branchLane = JunctionRules.LaneForBranch(branch);
                    if (Lane != branchLane && state.Plan.RequiresSideChoice)
                        MoveToLane(branchLane, false);
                }
            }
            if (state.Phase == JunctionPhase.Branch && state.Plan.IsNarrowBridge)
                JunctionCommitment = JunctionBranch.Centre;

            if (state.Phase == JunctionPhase.None || state.Phase == JunctionPhase.Merge)
                JunctionCommitment = state.Phase == JunctionPhase.Merge ? JunctionCommitment : JunctionBranch.None;

            if (state.Phase != previousPhase)
            {
                if (state.Phase == JunctionPhase.Warning)
                    App.Audio.PlaySfx("sfx_location", 0.55f, 0.15f);
                else if (state.Phase == JunctionPhase.Branch && JunctionCommitment != JunctionBranch.None)
                    App.Audio.PlaySfx("sfx_ability", 0.26f, 0.1f);
            }

            float targetLean = 0f;
            if (state.Phase == JunctionPhase.Commit || state.Phase == JunctionPhase.Branch)
            {
                if (JunctionCommitment == JunctionBranch.Left) targetLean = -1f;
                else if (JunctionCommitment == JunctionBranch.Right) targetLean = 1f;
                else if (state.Plan.IsNarrowBridge) targetLean = 0f;
                else if (state.Plan.HasCurvedRoute)
                    targetLean = Mathf.Clamp(_routeOffset * 0.7f, -1f, 1f);
            }
            _junctionLean = Mathf.MoveTowards(_junctionLean, targetLean, dt * 4f);
            JunctionLean = _junctionLean;
        }

        private void UpdateLane(float dt)
        {
            if (_laneTimer <= 0f) return;
            _laneTimer -= dt;
            _x = LaneMath.Swing(_x, _xTarget, _xStart, Config.laneSwitchDuration,
                Config.laneSwitchDuration - _laneTimer);
            LaneX = _x;
            if (_laneTimer <= 0f)
            {
                _x = _xTarget;
                LaneX = _x;
            }
        }

        /// <summary>
        /// Follows the track's deterministic lateral route. The same pure curve function is
        /// used to build the asphalt and collision surfaces, so the runner cannot drift off
        /// the visible deck on a curved road, T-junction or shortcut.
        /// </summary>
        private void UpdateRouteOffset(float dt)
        {
            float target = Track != null ? Track.GetJunctionRouteOffset(Distance, Lane) : 0f;
            float rate = 6f + Speed * 0.5f;
            _routeOffset = Mathf.MoveTowards(_routeOffset, target, rate * dt);
            RouteOffsetX = _routeOffset;
        }

        private void UpdateVertical(float dt)
        {
            if (_jumpBufferTimer > 0f) _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
            if (_grounded) _coyoteTimer = 0.11f;
            else _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);

            // Consume a buffered press as soon as the runner can leave the ground. This makes
            // presses during the last few centimetres of a landing count instead of vanishing.
            if (_jumpBufferTimer > 0f && _vy <= 0f && (_grounded || _coyoteTimer > 0f))
                TryJump();

            if (_grounded && _slideTimer <= 0f && _vy <= 0f) return;
            _vy -= Config.gravity * dt;
            _y += _vy * dt;
            if (_slideTimer > 0f) _slideTimer -= dt;
            if (_y <= 0f)
            {
                bool wasAirborne = !_grounded;
                float impact = Mathf.Abs(_vy);
                _y = 0f;
                _vy = 0f;
                _grounded = true;
                _coyoteTimer = 0.11f;
                if (wasAirborne)
                {
                    float weight = Mathf.Clamp01(impact / 11f);
                    App.Audio.PlaySfx("sfx_land", Mathf.Lerp(0.45f, 0.72f, weight), 0.03f);
                    Visuals?.NotifyLanding(impact);
                    Landed?.Invoke(impact);
                    _vfx?.Dust(new Vector3(RouteX, 0.05f, PlayerZ), Palette.Cyan, 4 + Mathf.RoundToInt(weight * 8f));
                    _stepTimer = 0.2f;
                }
                if (_airJumpTimer > 0f) _airCharges = 1;
                if (_jumpBufferTimer > 0f) TryJump();
            }
            else _grounded = false;
        }

        private void RequestLane(int direction)
        {
            if (!Running) return;
            int step = Math.Sign(direction);
            if (step == 0) return;
            _lastLaneDirection = step;
            int next = LaneMath.ClampLane(Lane + step);
            next = JunctionRules.ClampLane(next, _junctionState.Plan, _junctionState.Phase, JunctionCommitment);
            if (next == Lane) return;
            MoveToLane(next, true);
            _input?.ResetTransient();
        }

        private void MoveToLane(int lane, bool sound)
        {
            lane = LaneMath.ClampLane(lane);
            if (lane == Lane) return;
            Lane = lane;
            _xStart = _x;
            _xTarget = LaneMath.LaneToX(Lane);
            _laneTimer = Mathf.Max(0.04f, Config.laneSwitchDuration);
            if (sound) App.Audio.PlaySfx("sfx_lane", 0.24f, 0.08f);
        }

        private void RequestJump()
        {
            if (!Running) return;
            _jumpBufferTimer = 0.16f;
            TryJump();
        }

        private bool TryJump()
        {
            if (_grounded || _coyoteTimer > 0f)
            {
                _vy = Config.jumpVelocity;
                _grounded = false;
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                _slideTimer = 0f;
                Jumps++;
                App.Audio.PlaySfx("sfx_jump", 0.65f);
                _vfx?.Dust(new Vector3(RouteX, 0.04f, PlayerZ), Palette.Cyan, 6);
                return true;
            }
            if (_airCharges > 0)
            {
                _airCharges--;
                _vy = Config.jumpVelocity * 0.92f;
                _jumpBufferTimer = 0f;
                Jumps++;
                App.Audio.PlaySfx("sfx_jump", 0.5f);
                _vfx?.Burst(new Vector3(RouteX, _y + 1f, PlayerZ), Palette.Cyan, 10, 0.2f);
                Visuals?.NotifyDoubleJump();
                return true;
            }
            return false;
        }

        private void ReleaseJump()
        {
            if (!Running || _grounded || _vy <= 0f) return;
            // Variable-height jumps must still clear the 0.99 m low barrier even when the key
            // is tapped for only one frame. Previously a tap cut the launch velocity by 42%,
            // producing a 0.60 m hop that visually landed on top of the barrier.
            float minimumClearVelocity = Mathf.Sqrt(2f * Mathf.Max(1f, Config.gravity) * 1.32f);
            if (_vy <= minimumClearVelocity) return;
            _vy = Mathf.Max(minimumClearVelocity, _vy * 0.7f);
        }

        private void RequestSlide()
        {
            if (!Running) return;
            if (!_grounded)
            {
                // Fast-fall into a slide; the hitbox only shrinks when grounded.
                _vy = Mathf.Min(_vy, -Config.gravity * 0.55f);
                return;
            }
            if (_slideTimer > 0f) return;
            _slideTimer = Mathf.Max(0.3f, Config.slideDuration);
            Slides++;
            App.Audio.PlaySfx("sfx_slide", 0.6f);
            _vfx?.Dust(new Vector3(RouteX, 0.03f, PlayerZ), Palette.Amber, 8);
        }

        private void ResolveCollisions()
        {
            if (_invincibleTimer > 0f) return;
            Track.CollectObstacles(PlayerZ - 4f, PlayerZ + 3f, _obstacles);
            float halfWidth = 0.31f;
            float minY = _y + 0.04f;
            float maxY = _y + (IsSliding ? 0.74f : 1.72f);
            float zMin = Mathf.Min(_prevZ, PlayerZ) - 0.32f;
            float zMax = Mathf.Max(_prevZ, PlayerZ) + 0.32f;
            for (int i = 0; i < _obstacles.Count; i++)
            {
                var obstacle = _obstacles[i];
                if (obstacle == null || !obstacle.Active) continue;
                if (obstacle.Blocks(RouteX, halfWidth, minY, maxY, zMin, zMax))
                {
                    HandleHit(obstacle);
                    break;
                }
                if (!_nearMissSeen.Contains(obstacle.RuntimeId) &&
                    obstacle.transform.position.z < PlayerZ - 0.8f &&
                    obstacle.IsNearMiss(RouteX, halfWidth, PlayerZ - 2f, PlayerZ + 2f))
                {
                    _nearMissSeen.Add(obstacle.RuntimeId);
                    NearMisses++;
                    _bonusScore += 25;
                    App.Audio.PlaySfx("sfx_nearmiss", 0.35f, 0.1f);
                    NearMiss?.Invoke(1);
                }
            }
        }

        private void HandleHit(Obstacle obstacle)
        {
            if (GameModes.IsOnline(Mode.Id))
            {
                // In online races cosmetic protection is allowed, but no revive beyond shields.
                if (_shieldCharges > 0)
                {
                    AbsorbHit(obstacle);
                    return;
                }
                Die();
                return;
            }

            if (_shieldCharges > 0)
            {
                AbsorbHit(obstacle);
                return;
            }
            if (_hoverboardCharges > 0)
            {
                _hoverboardCharges--;
                _invincibleTimer = 1.15f;
                _bonusScore += 30;
                App.Audio.PlaySfx("sfx_hit", 0.5f);
                _vfx?.Impact(new Vector3(RouteX, _y + 0.8f, PlayerZ), Palette.Magenta);
                Crashed?.Invoke();
                return;
            }
            if (!Mode.AllowsCrash)
            {
                _invincibleTimer = 1.1f;
                App.Audio.PlaySfx("sfx_hit", 0.35f);
                _vfx?.Impact(new Vector3(RouteX, _y + 0.8f, PlayerZ), Palette.Amber);
                Crashed?.Invoke();
                return;
            }

            _lives--;
            if (_lives > 0)
            {
                _invincibleTimer = 1.35f;
                App.Audio.PlaySfx("sfx_hit", 0.8f);
                _vfx?.Impact(new Vector3(RouteX, _y + 0.8f, PlayerZ), Palette.Red);
                Crashed?.Invoke();
                return;
            }
            Die();
        }

        private void AbsorbHit(Obstacle obstacle)
        {
            _shieldCharges--;
            _invincibleTimer = 1.05f;
            App.Audio.PlaySfx("sfx_hit", 0.55f);
            _vfx?.Impact(new Vector3(RouteX, _y + 0.9f, PlayerZ), Palette.Cyan);
            _bonusScore += 50;
            Crashed?.Invoke();
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Running = false;
            _invincibleTimer = 0f;
            if (_slowMotionTimer > 0f) StopSlowMotion();
            App.Audio.PlaySfx("sfx_defeat", 0.9f);
            _vfx?.Impact(new Vector3(RouteX, _y + 0.9f, PlayerZ), Palette.Red);
            if (Visuals != null) Visuals.SetPose(RunnerPose.Dead);
            Died?.Invoke();
        }

        private void ResolvePickups(float dt)
        {
            Track.CollectCredits(PlayerZ - 4f, PlayerZ + 10f, _credits);
            bool magnet = _magnetTimer > 0f || _coinRushTimer > 0f || _phaseTimer > 0f;
            float magnetRadius = 0f;
            if (magnet)
            {
                magnetRadius = Config.coinMagnetRadius * ProgressionMath.UpgradeValue("magnet", _magnetLevel);
                if (_phaseTimer > 0f) magnetRadius += 2f;
            }
            Vector3 playerCentre = new Vector3(RouteX, _y + 0.9f, PlayerZ);
            for (int i = 0; i < _credits.Count; i++)
            {
                var pickup = _credits[i];
                if (pickup == null || pickup.Collected) continue;
                var position = pickup.transform.position;
                if (magnet)
                {
                    float distance = Vector3.Distance(position, playerCentre);
                    if (distance <= magnetRadius)
                    {
                        pickup.Attract(playerCentre, dt, 12f + Speed * 0.8f);
                        if (distance < 1.05f) CollectCredit(pickup);
                        continue;
                    }
                    pickup.SetAttracting(false);
                }
                float dz = Mathf.Abs(position.z - PlayerZ);
                float dx = Mathf.Abs(position.x - RouteX);
                float dy = position.y - playerCentre.y;
                if (dz < 1.35f && dx < 0.86f && dy > -1.1f && dy < 1.5f) CollectCredit(pickup);
            }

            Track.CollectPowerUps(PlayerZ - 3f, PlayerZ + 5f, _powerUps);
            for (int i = 0; i < _powerUps.Count; i++)
            {
                var pickup = _powerUps[i];
                if (pickup == null || pickup.Collected) continue;
                var position = pickup.transform.position;
                if (Mathf.Abs(position.z - PlayerZ) < 1.5f && Mathf.Abs(position.x - RouteX) < 1.1f &&
                    Mathf.Abs(position.y - (playerCentre.y + 0.3f)) < 1.7f)
                {
                    ActivatePowerUp(pickup.Definition);
                    pickup.Collect();
                }
            }
        }

        private void CollectCredit(EnergyPickup pickup)
        {
            if (pickup == null || pickup.Collected) return;
            int value = pickup.Value;
            bool prism = pickup.Kind == EnergyKind.Prism;
            if (_coinRushTimer > 0f) value *= 3;
            pickup.Collect();
            Coins += value;
            _coinScore += Mathf.RoundToInt(value * 15f * ScoreMultiplier);
            App.Audio.PlaySfx(prism ? "sfx_shard" : "sfx_coin", prism ? 0.8f : 0.42f);
            _vfx?.Burst(pickup.transform.position, prism ? Palette.Violet : Palette.Cyan, prism ? 18 : 5, 0.28f);
            CreditsCollected?.Invoke(value);
        }

        public void ActivatePowerUp(PowerUpDefinition definition)
        {
            if (definition == null) return;
            PowerUpsUsed++;
            App.Audio.PlaySfx("sfx_powerup", 0.85f);
            _vfx?.Burst(transform.position + Vector3.up, definition.color, 22, 0.4f);
            _effectTimers[definition.type] = 0f;
            switch (definition.type)
            {
                case PowerUpType.Shield:
                    _shieldCharges = Mathf.Max(_shieldCharges, 1 + _shieldLevel / 3);
                    break;
                case PowerUpType.Hoverboard:
                    _hoverboardCharges = Mathf.Max(_hoverboardCharges, 1);
                    break;
                case PowerUpType.Magnet:
                    _magnetTimer = Mathf.Max(_magnetTimer, definition.duration);
                    break;
                case PowerUpType.Multiplier:
                    _multiplierTimer = Mathf.Max(_multiplierTimer, definition.duration);
                    break;
                case PowerUpType.SpeedBoost:
                    _boostTimer = Mathf.Max(_boostTimer, definition.duration);
                    _speedModel.SetBoost(1.2f + _boostLevel * 0.025f);
                    break;
                case PowerUpType.Invincibility:
                    _invincibleTimer = Mathf.Max(_invincibleTimer, definition.duration);
                    break;
                case PowerUpType.AirJump:
                    _airJumpTimer = Mathf.Max(_airJumpTimer, definition.duration);
                    _airCharges = Mathf.Max(_airCharges, 1);
                    break;
                case PowerUpType.SlowMotion:
                    if (!GameModes.IsOnline(Mode.Id))
                    {
                        _slowMotionTimer = Mathf.Max(_slowMotionTimer, definition.duration);
                        _bonusScore += 200;
                    }
                    else _bonusScore += 300;
                    break;
                case PowerUpType.CoinRush:
                    _coinRushTimer = Mathf.Max(_coinRushTimer, definition.duration);
                    break;
                case PowerUpType.PhaseDash:
                    _phaseTimer = Mathf.Max(_phaseTimer, definition.duration);
                    _invincibleTimer = Mathf.Max(_invincibleTimer, definition.duration);
                    _magnetTimer = Mathf.Max(_magnetTimer, definition.duration);
                    _speedModel.SetBoost(1.3f);
                    break;
            }
            PowerUpCollected?.Invoke(definition);
        }

        private void UpdateTimers(float dt)
        {
            _invincibleTimer = Mathf.Max(0f, _invincibleTimer - dt);
            _magnetTimer = Mathf.Max(0f, _magnetTimer - dt);
            _multiplierTimer = Mathf.Max(0f, _multiplierTimer - dt);
            _coinRushTimer = Mathf.Max(0f, _coinRushTimer - dt);
            _airJumpTimer = Mathf.Max(0f, _airJumpTimer - dt);
            if (_airJumpTimer <= 0f) _airCharges = 0;

            if (_boostTimer > 0f)
            {
                _boostTimer -= dt;
                if (_boostTimer <= 0f) _speedModel.SetBoost(1f);
            }
            if (_phaseTimer > 0f)
            {
                _phaseTimer -= dt;
                if (_phaseTimer <= 0f)
                {
                    _speedModel.SetBoost(_boostTimer > 0f ? 1.2f + _boostLevel * 0.025f : 1f);
                    if (_invincibleTimer > 1f) _invincibleTimer = Mathf.Max(_invincibleTimer, 0f);
                }
            }
            if (_slowMotionTimer > 0f)
            {
                _slowMotionTimer -= dt;
                if (_slowMotionTimer <= 0f) StopSlowMotion();
            }
        }

        private void StopSlowMotion()
        {
            _slowMotionTimer = 0f;
        }

        private void RefreshEffects()
        {
            _activeEffects.Clear();
            if (_shieldCharges > 0) AddEffect(PowerUpType.Shield, "AEGIS", 0f, _shieldCharges, Palette.Cyan, false);
            if (_hoverboardCharges > 0) AddEffect(PowerUpType.Hoverboard, "SPECTRE", 0f, _hoverboardCharges, Palette.Magenta, false);
            if (_magnetTimer > 0f) AddEffect(PowerUpType.Magnet, "MAGNET", _magnetTimer, 0, Palette.Violet, true);
            if (_multiplierTimer > 0f) AddEffect(PowerUpType.Multiplier, "SURGE x2", _multiplierTimer, 0, Palette.Amber, true);
            if (_boostTimer > 0f) AddEffect(PowerUpType.SpeedBoost, "VOLT RUSH", _boostTimer, 0, Palette.Orange, true);
            if (_invincibleTimer > 0.05f) AddEffect(PowerUpType.Invincibility, "PHASE", _invincibleTimer, 0, Palette.Lime, true);
            if (_airJumpTimer > 0f) AddEffect(PowerUpType.AirJump, "SKY CHARGE", _airJumpTimer, _airCharges, Palette.Cyan, true);
            if (_slowMotionTimer > 0f) AddEffect(PowerUpType.SlowMotion, "TIME DILATION", _slowMotionTimer, 0, Palette.Violet, true);
            if (_coinRushTimer > 0f) AddEffect(PowerUpType.CoinRush, "CREDIT STORM", _coinRushTimer, 0, Palette.Amber, true);
            if (_phaseTimer > 0f) AddEffect(PowerUpType.PhaseDash, "PRISM DASH", _phaseTimer, 0, Palette.White, true);
        }

        private void AddEffect(PowerUpType type, string label, float remaining, int charges, Color color, bool timed)
        {
            _activeEffects.Add(new PowerUpStatus
            {
                Type = type, Label = label, Remaining = remaining, Charges = charges, Color = color, Timed = timed
            });
        }

        public bool HasEffect(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Shield: return _shieldCharges > 0;
                case PowerUpType.Hoverboard: return _hoverboardCharges > 0;
                default: return _effectTimers.ContainsKey(type) && GetTimer(type) > 0f;
            }
        }

        private float GetTimer(PowerUpType type) => type switch
        {
            PowerUpType.Magnet => _magnetTimer,
            PowerUpType.Multiplier => _multiplierTimer,
            PowerUpType.SpeedBoost => _boostTimer,
            PowerUpType.Invincibility => _invincibleTimer,
            PowerUpType.AirJump => _airJumpTimer,
            PowerUpType.SlowMotion => _slowMotionTimer,
            PowerUpType.CoinRush => _coinRushTimer,
            PowerUpType.PhaseDash => _phaseTimer,
            _ => 0f
        };

        /// <summary>Force-finish trail effects for the results screenshot look.</summary>
        public void SetGodMode(bool enabled)
        {
            if (enabled) _invincibleTimer = 9999f;
            else _invincibleTimer = 0f;
        }

        public void AddBonusScore(int amount) => _bonusScore += Mathf.Max(0, amount);
        public void GrantCredits(int amount) => Coins += Mathf.Max(0, amount);

        public void ForceLane(int lane, bool immediate = false)
        {
            Lane = LaneMath.ClampLane(lane);
            _xTarget = LaneMath.LaneToX(Lane);
            if (immediate)
            {
                _x = _xTarget;
                LaneX = _x;
                _laneTimer = 0f;
            }
        }

        private void OnDestroy()
        {
            StopInput();
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }
    }
}
