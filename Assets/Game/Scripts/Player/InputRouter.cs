using System;
using NeonVelocity.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace NeonVelocity.Player
{
    /// <summary>
    /// Normalises keyboard, gamepad and touch into four abstract runner actions.
    /// Swipes are resolved proportional to the configurable sensitivity setting.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        public bool Enabled { get; set; } = true;
        public bool JumpHeld { get; private set; }
        public event Action<int> LaneRequested;
        public event Action JumpRequested;
        public event Action JumpReleased;
        public event Action SlideRequested;
        public event Action PauseRequested;

        private float _sensitivity = 1f;
        private bool _gamepadLaneLatched;
        private Vector2 _touchStart;
        private bool _touchActive;
        private int _lastLaneDirection;
        private float _laneCooldown;

        public void SetSensitivity(float value) => _sensitivity = Mathf.Clamp(value, 0.4f, 2.5f);

        private void Update()
        {
            if (!Enabled) { JumpHeld = false; return; }
            _laneCooldown -= Time.unscaledDeltaTime;
            ReadKeyboard();
            ReadGamepad();
            ReadTouch();
        }

        private void ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) PauseRequested?.Invoke();
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) RequestLane(-1);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) RequestLane(1);
            bool jumpPress = kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            if (jumpPress)
            {
                JumpHeld = true;
                JumpRequested?.Invoke();
            }
            bool jumpRelease = kb.spaceKey.wasReleasedThisFrame || kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame;
            if (jumpRelease)
            {
                JumpHeld = false;
                JumpReleased?.Invoke();
            }
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) SlideRequested?.Invoke();
        }

        private void ReadGamepad()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;
            float x = gamepad.leftStick.x.ReadValue();
            if (Mathf.Abs(x) > 0.55f)
            {
                if (!_gamepadLaneLatched)
                {
                    RequestLane(x < 0f ? -1 : 1);
                    _gamepadLaneLatched = true;
                }
            }
            else if (Mathf.Abs(x) < 0.25f) _gamepadLaneLatched = false;
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                JumpHeld = true;
                JumpRequested?.Invoke();
            }
            if (gamepad.buttonSouth.wasReleasedThisFrame)
            {
                JumpHeld = false;
                JumpReleased?.Invoke();
            }
            if (gamepad.buttonEast.wasPressedThisFrame) SlideRequested?.Invoke();
            if (gamepad.startButton.wasPressedThisFrame) PauseRequested?.Invoke();
        }

        private void ReadTouch()
        {
            var touch = Touchscreen.current;
            if (touch == null) return;
            var primary = touch.primaryTouch;
            if (primary.press.wasPressedThisFrame)
            {
                _touchStart = primary.position.ReadValue();
                _touchActive = true;
            }
            else if (_touchActive && primary.press.wasReleasedThisFrame)
            {
                _touchActive = false;
                Vector2 delta = primary.position.ReadValue() - _touchStart;
                float threshold = 48f / Mathf.Clamp(_sensitivity, 0.4f, 2.5f);
                if (delta.magnitude < threshold) return;
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    RequestLane(delta.x > 0f ? 1 : -1);
                else if (delta.y > 0f)
                {
                    JumpHeld = false;
                    JumpRequested?.Invoke();
                }
                else SlideRequested?.Invoke();
            }
        }

        private void RequestLane(int direction)
        {
            if (direction == 0 || _laneCooldown > 0f) return;
            _lastLaneDirection = direction;
            _laneCooldown = 0.05f;
            LaneRequested?.Invoke(direction);
        }

        public void ResetTransient()
        {
            _touchActive = false;
            _gamepadLaneLatched = false;
            JumpHeld = false;
        }
    }
}
