using UnityEngine;
using UnityEngine.InputSystem;

namespace BoxingGame
{
    /// <summary>
    /// Keyboard + mouse + gamepad control for the player's Fighter (new Input System).
    ///   Move      : W/S = toward/away from opponent, A/D = circle     | Left stick
    ///   Jab       : Left Mouse / J                                    | Gamepad West (X / Square)
    ///   Cross     : Right Mouse / K                                   | Gamepad East (B / Circle)
    ///   Uppercut  : E / L                                             | Gamepad North (Y / Triangle)
    ///   Dodge     : Left Ctrl / Space (+ A/D/S direction, default back)| Gamepad South (A / Cross)
    ///   Guard     : hold Left Shift                                   | Left trigger / Left bumper
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class PlayerController : MonoBehaviour
    {
        public float inputBufferTime = 0.25f;

        Fighter fighter;
        PunchType? buffered;
        float bufferedAt;
        float dodgeRequestedAt = -10f;

        void Awake() { fighter = GetComponent<Fighter>(); }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            Vector2 move = Vector2.zero;
            bool guard = false;

            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                guard |= kb.leftShiftKey.isPressed;
                if (kb.leftCtrlKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) RequestDodge();

                if (kb.jKey.wasPressedThisFrame) Buffer(PunchType.Jab);
                if (kb.kKey.wasPressedThisFrame) Buffer(PunchType.Cross);
                if (kb.eKey.wasPressedThisFrame || kb.lKey.wasPressedThisFrame) Buffer(PunchType.Uppercut);
            }

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) Buffer(PunchType.Jab);
                if (mouse.rightButton.wasPressedThisFrame) Buffer(PunchType.Cross);
            }

            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                guard |= pad.leftTrigger.isPressed || pad.leftShoulder.isPressed;
                if (pad.buttonWest.wasPressedThisFrame) Buffer(PunchType.Jab);
                if (pad.buttonEast.wasPressedThisFrame) Buffer(PunchType.Cross);
                if (pad.buttonNorth.wasPressedThisFrame) Buffer(PunchType.Uppercut);
                if (pad.buttonSouth.wasPressedThisFrame) RequestDodge();
            }

            fighter.MoveInput = Vector2.ClampMagnitude(move, 1f);
            fighter.GuardHeld = guard;

            if (Time.time - dodgeRequestedAt <= inputBufferTime)
            {
                if (fighter.TryDodge(fighter.MoveInput)) dodgeRequestedAt = -10f;
            }

            // small input buffer so punches pressed slightly early still come out
            if (buffered.HasValue)
            {
                if (Time.time - bufferedAt > inputBufferTime) buffered = null;
                else if (fighter.TryPunch(buffered.Value)) buffered = null;
            }
        }

        void RequestDodge()
        {
            if (fighter.Active) dodgeRequestedAt = Time.time;
        }

        void Buffer(PunchType t)
        {
            if (!fighter.Active) return;
            buffered = t;
            bufferedAt = Time.time;
        }
    }
}
