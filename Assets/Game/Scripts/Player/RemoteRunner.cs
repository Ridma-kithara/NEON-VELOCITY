using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.Player
{
    /// <summary>
    /// Visual proxy for another racer. The server owns the truth: its validated distance and
    /// lane are interpolated and mapped relative to the local runner on the shared deterministic
    /// track. Only a small display window is rendered to keep the scene light.
    /// </summary>
    public sealed class RemoteRunner : MonoBehaviour
    {
        public string PlayerId { get; private set; }
        public WirePlayer State { get; private set; }
        public float DisplayDistance { get; private set; }
        public int Lane { get; private set; }

        private RunnerVisuals _visuals;
        private Transform _visualRoot;
        private TextMesh _tag;
        private float _targetDistance;
        private float _phase;
        private float _laneX;
        private float _targetLaneX;
        private bool _dead;
        private bool _finished;
        private bool _connected = true;
        private string _displayName;

        private bool IsLocalLeader => State != null && State.placement == 1;

        public static RemoteRunner Create(Transform parent, GameApp app, WirePlayer state)
        {
            var go = new GameObject("Remote Runner - " + (state?.name ?? "RUNNER"));
            go.transform.SetParent(parent, false);
            var remote = go.AddComponent<RemoteRunner>();
            remote.Build(app, state);
            return remote;
        }

        private void Build(GameApp app, WirePlayer state)
        {
            State = state;
            PlayerId = state?.playerId ?? "";
            DisplayDistance = state?.distance ?? 0f;
            _targetDistance = DisplayDistance;
            Lane = Mathf.Clamp(state?.lane ?? 1, 0, 2);
            _targetLaneX = LaneMath.LaneToX(Lane);
            _laneX = _targetLaneX;
            _dead = state != null && state.runnerState == "dead";
            _finished = state != null && state.finished;
            _connected = state?.connected ?? true;
            _displayName = state?.name ?? "RUNNER";

            _visualRoot = new GameObject("Remote Visual").transform;
            _visualRoot.SetParent(transform, false);
            var config = app.Config;
            int hash = DeterministicRandom.StableHash(PlayerId + _displayName);
            var characters = config.characters;
            var character = characters.Count > 0 ? characters[Mathf.Abs(hash) % characters.Count] : null;
            var trails = config.trails;
            var trail = trails.Count > 0 ? trails[Mathf.Abs(hash / 7) % trails.Count] : null;
            _visuals = _visualRoot.gameObject.AddComponent<RunnerVisuals>();
            _visuals.Build(character, trail, true, "");
            _visuals.SetPose(RunnerPose.Run);

            var tagGo = new GameObject("Tag Background");
            tagGo.transform.SetParent(transform, false);
            tagGo.transform.localPosition = new Vector3(0f, 2.42f, 0f);
            var tag = tagGo.AddComponent<TextMesh>();
            tag.text = _displayName.ToUpperInvariant();
            tag.font = FontLibrary.Body;
            tag.fontSize = 46;
            tag.characterSize = 0.05f;
            tag.anchor = TextAnchor.MiddleCenter;
            tag.alignment = TextAlignment.Center;
            tag.color = character != null ? character.primary : Palette.Cyan;
            var renderer = tagGo.GetComponent<MeshRenderer>();
            if (tag.font != null) renderer.material = tag.font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _tag = tag;

            transform.position = new Vector3(_laneX, 0f, 8f);
        }

        public void ApplyState(WirePlayer state)
        {
            if (state == null) return;
            State = state;
            _targetDistance = state.distance;
            int lane = Mathf.Clamp(state.lane, 0, 2);
            if (lane != Lane)
            {
                Lane = lane;
                _targetLaneX = LaneMath.LaneToX(lane);
            }
            _dead = state.runnerState == "dead";
            _finished = state.finished;
            _connected = state.connected;
        }

        public void Tick(float localDistance, float localPlayerZ, float dt)
        {
            if (State == null) return;
            float k = 1f - Mathf.Exp(-dt * (IsLocalLeader ? 6f : 9f));
            DisplayDistance = Mathf.Lerp(DisplayDistance, _targetDistance, k);
            _laneX = Mathf.Lerp(_laneX, _targetLaneX, 1f - Mathf.Exp(-dt * 12f));
            float relative = DisplayDistance - localDistance;
            bool visible = _connected && relative > -32f && relative < 75f;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            _phase += dt;
            string runnerState = State.runnerState ?? "run";
            float y = 0f;
            RunnerPose pose = RunnerPose.Run;
            if (runnerState == "dead" || _dead) pose = RunnerPose.Dead;
            else if (runnerState == "finished" || _finished) pose = RunnerPose.Victory;
            else if (runnerState == "jump") { pose = RunnerPose.Jump; y = 0.85f + Mathf.Sin(_phase * 5f) * 0.12f; }
            else if (runnerState == "slide") pose = RunnerPose.Slide;
            _visuals.Tick(dt, Mathf.Clamp01(State.speed / 34f), (_targetLaneX - _laneX) * 2f, pose);
            transform.position = new Vector3(_laneX, y, localPlayerZ + relative);
            transform.rotation = Quaternion.identity;
            if (_tag != null)
            {
                var camera = Camera.main;
                if (camera != null)
                    _tag.transform.rotation = Quaternion.LookRotation(_tag.transform.position - camera.transform.position, Vector3.up);
                _tag.text = _displayName.ToUpperInvariant() + (_finished ? "  [DONE]" : _dead ? "  [OUT]" : "");
            }
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
