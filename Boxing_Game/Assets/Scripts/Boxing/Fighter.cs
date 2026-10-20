using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxingGame
{
    public enum PunchType { Jab = 0, Cross = 1, Uppercut = 2 }
    public enum FighterState { Idle, Punching, Hurt, Stunned, Dead }

    [Serializable]
    public class PunchData
    {
        public string label = "Jab";
        public string animState = "Punch-Left";
        public float damage = 5f;
        [Range(0.05f, 0.95f)] public float hitPoint = 0.4f;   // fraction of the animation where the punch connects
        public float recovery = 0.18f;                         // extra time after the animation before you can act again
        public float range = 1.5f;                             // max distance (centre to centre) the punch can connect
        public float staminaCost = 5f;
        public float hitStun = 0.25f;                          // how long the victim is staggered
        public float knockback = 0.5f;
        public bool heavy = false;

        public static PunchData[] Defaults()
        {
            return new[]
            {
                new PunchData { label = "Jab",      animState = "Punch-Left",     damage = 5f,  hitPoint = 0.40f, recovery = 0.18f, range = 1.50f, staminaCost = 5f,  hitStun = 0.25f, knockback = 0.4f, heavy = false },
                new PunchData { label = "Cross",    animState = "Punch-Right",    damage = 9f,  hitPoint = 0.45f, recovery = 0.25f, range = 1.60f, staminaCost = 9f,  hitStun = 0.35f, knockback = 0.7f, heavy = false },
                new PunchData { label = "Uppercut", animState = "Punch-Uppercut", damage = 13f, hitPoint = 0.50f, recovery = 0.35f, range = 1.35f, staminaCost = 14f, hitStun = 0.55f, knockback = 1.1f, heavy = true  },
            };
        }
    }

    /// <summary>
    /// Shared boxer logic (health, stamina, movement, punching, blocking, hit reactions).
    /// Driven either by PlayerController or by BoxerAI, which only set MoveInput / GuardHeld and call TryPunch.
    /// </summary>
    public class Fighter : MonoBehaviour
    {
        [Header("Identity")]
        public string displayName = "Fighter";

        [Header("Stats")]
        public float maxHealth = 100f;
        public float maxStamina = 100f;
        public float staminaRegen = 14f;
        public float moveSpeed = 1.9f;
        public float backSpeedMultiplier = 0.75f;
        public float guardSpeedMultiplier = 0.7f;
        public float turnSpeed = 540f;
        public float minSeparation = 0.85f;

        [Header("Defence")]
        public float blockDamageMultiplier = 0.15f;
        public float blockStaminaCostPerDamage = 1.5f;
        public float guardBrokenStun = 1.1f;

        [Header("Punches (Jab, Cross, Uppercut)")]
        public PunchData[] punches;

        [Header("Ring (set by GameManager)")]
        public Vector3 ringCenter = Vector3.zero;
        public Vector2 ringHalfSize = new Vector2(2.4f, 2.4f);

        public Fighter opponent;

        // ---- set by the controller (player input or AI) ----
        [NonSerialized] public Vector2 MoveInput;   // x = circle right/left, y = toward/away from opponent
        [NonSerialized] public bool GuardHeld;

        // ---- runtime state ----
        public bool Active { get; set; }
        public FighterState State { get; private set; } = FighterState.Idle;
        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public int PunchCounter { get; private set; }
        public PunchData CurrentPunch { get; private set; }

        public bool IsGuarding => GuardHeld && State == FighterState.Idle && Stamina > 0f && Active;
        public bool CanAct => Active && State == FighterState.Idle;

        // victim, damage dealt, blocked?, heavy?
        public event Action<Fighter, float, bool, bool> Damaged;
        public event Action<Fighter> Died;
        public static event Action<float> HitImpact;   // camera shake hook

        Animator animator;
        string currentAnim = "";
        Coroutine actionRoutine;
        Vector3 knockVel;
        readonly Dictionary<string, float> clipLengths = new Dictionary<string, float>();
        readonly HashSet<string> warned = new HashSet<string>();

        void Awake()
        {
            if (punches == null || punches.Length < 3) punches = PunchData.Defaults();
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError("[Fighter] No Animator found on " + name);
            }
            else
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var ctrl = animator.runtimeAnimatorController;
                if (ctrl != null)
                {
                    foreach (var c in ctrl.animationClips)
                    {
                        string key = c.name.StartsWith("Kairo_") ? c.name.Substring(6) : c.name;
                        clipLengths[key] = c.length;
                    }
                }
            }
            Health = maxHealth;
            Stamina = maxStamina;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (State == FighterState.Dead)
            {
                ApplyKnock(dt);
                ClampToRing();
                return;
            }

            if (State != FighterState.Punching)
            {
                float regen = staminaRegen * (IsGuarding ? 0.4f : 1f);
                Stamina = Mathf.Min(maxStamina, Stamina + regen * dt);
            }

            FaceOpponent(dt);

            Vector2 input = Vector2.zero;
            if (Active && State == FighterState.Idle) input = Vector2.ClampMagnitude(MoveInput, 1f);
            Move(input, dt);
            ApplyKnock(dt);
            Separate();
            ClampToRing();

            if (State == FighterState.Idle) UpdateLocomotionAnim(input);
        }

        // ------------------------------------------------------------------ movement

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public float FlatDistanceTo(Fighter other)
        {
            return other == null ? 99f : Flat(other.transform.position - transform.position).magnitude;
        }

        void FaceOpponent(float dt)
        {
            if (opponent == null) return;
            Vector3 to = Flat(opponent.transform.position - transform.position);
            if (to.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        void Move(Vector2 input, float dt)
        {
            if (input.sqrMagnitude < 0.0001f) return;
            Vector3 fwd = opponent != null ? Flat(opponent.transform.position - transform.position) : transform.forward;
            if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = right * input.x + fwd * input.y;
            float speed = moveSpeed;
            if (input.y < -0.1f) speed *= backSpeedMultiplier;
            if (IsGuarding) speed *= guardSpeedMultiplier;
            transform.position += dir * speed * dt;
        }

        void ApplyKnock(float dt)
        {
            if (knockVel.sqrMagnitude < 0.0004f) { knockVel = Vector3.zero; return; }
            transform.position += knockVel * dt;
            knockVel = Vector3.Lerp(knockVel, Vector3.zero, 10f * dt);
        }

        void Separate()
        {
            if (opponent == null || opponent.State == FighterState.Dead) return;
            Vector3 d = Flat(transform.position - opponent.transform.position);
            float dist = d.magnitude;
            if (dist < minSeparation && dist > 0.0001f)
            {
                transform.position += d / dist * ((minSeparation - dist) * 0.5f);
            }
        }

        void ClampToRing()
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, ringCenter.x - ringHalfSize.x, ringCenter.x + ringHalfSize.x);
            p.z = Mathf.Clamp(p.z, ringCenter.z - ringHalfSize.y, ringCenter.z + ringHalfSize.y);
            p.y = ringCenter.y;
            transform.position = p;
        }

        void UpdateLocomotionAnim(Vector2 input)
        {
            bool g = IsGuarding;
            string s;
            if (input.sqrMagnitude < 0.04f) s = g ? "Idle-Guard" : "Idle";
            else if (input.y < -0.1f) s = g ? "Walk-Back-Guard" : "Walk-Back";
            else s = g ? "Walk-Guard" : "Walk";
            PlayAnim(s, 0.12f, false);
        }

        // ------------------------------------------------------------------ animation

        void PlayAnim(string stateName, float fade, bool restart)
        {
            if (animator == null) return;
            if (!restart && stateName == currentAnim) return;
            int hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash))
            {
                if (warned.Add(stateName)) Debug.LogWarning("[Fighter] Animator has no state '" + stateName + "' - did you run Boxing > Build Game Scene?");
                return;
            }
            currentAnim = stateName;
            animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
        }

        float ClipLength(string stateName, float fallback)
        {
            float l;
            return clipLengths.TryGetValue(stateName, out l) && l > 0.05f ? l : fallback;
        }

        // ------------------------------------------------------------------ actions

        public bool TryPunch(PunchType type)
        {
            if (!CanAct) return false;
            PunchData p = punches[(int)type];
            if (Stamina < p.staminaCost * 0.5f) return false;
            actionRoutine = StartCoroutine(PunchRoutine(p));
            return true;
        }

        IEnumerator PunchRoutine(PunchData p)
        {
            State = FighterState.Punching;
            CurrentPunch = p;
            PunchCounter++;
            Stamina = Mathf.Max(0f, Stamina - p.staminaCost);
            PlayAnim(p.animState, 0.05f, true);

            float clip = ClipLength(p.animState, 0.5f);
            yield return new WaitForSeconds(clip * p.hitPoint);

            // moment of impact
            if (opponent != null && opponent.State != FighterState.Dead)
            {
                Vector3 to = Flat(opponent.transform.position - transform.position);
                float dist = to.magnitude;
                float angle = Vector3.Angle(transform.forward, to);
                if (dist <= p.range && angle <= 55f)
                {
                    opponent.ReceiveHit(this, p);
                }
            }

            yield return new WaitForSeconds(clip * (1f - p.hitPoint) + p.recovery);
            State = FighterState.Idle;
            actionRoutine = null;
        }

        /// <summary>Called by the attacker when a punch lands in range. Returns true if it was blocked.</summary>
        public bool ReceiveHit(Fighter attacker, PunchData p)
        {
            if (State == FighterState.Dead) return false;

            Vector3 toAtt = Flat(attacker.transform.position - transform.position);
            bool frontal = Vector3.Angle(transform.forward, toAtt) < 100f;
            bool blocked = IsGuarding && frontal && Stamina > 0f;

            if (actionRoutine != null) { StopCoroutine(actionRoutine); actionRoutine = null; }

            Vector3 away = toAtt.sqrMagnitude > 0.0001f ? -toAtt.normalized : -transform.forward;
            float dmg;

            if (blocked)
            {
                dmg = p.damage * blockDamageMultiplier;
                Stamina -= p.damage * blockStaminaCostPerDamage;
                knockVel += away * (p.knockback * 2.5f);
                if (Stamina <= 0f)
                {
                    Stamina = 0f;
                    blocked = false; // guard broken - treat as a clean hit for feedback
                    actionRoutine = StartCoroutine(StunRoutine(guardBrokenStun, "Stunned", FighterState.Stunned));
                }
                else
                {
                    actionRoutine = StartCoroutine(StunRoutine(0.18f, "Hurt-Guard", FighterState.Hurt));
                }
                HitImpact?.Invoke(0.05f);
            }
            else
            {
                dmg = p.damage;
                knockVel += away * (p.knockback * 4f);
                actionRoutine = StartCoroutine(StunRoutine(p.hitStun, "Hurt", FighterState.Hurt));
                HitImpact?.Invoke(p.heavy ? 0.22f : 0.12f);
            }

            Health = Mathf.Max(0f, Health - dmg);
            Damaged?.Invoke(this, dmg, blocked, p.heavy);

            if (Health <= 0f) Die();
            return blocked;
        }

        IEnumerator StunRoutine(float duration, string anim, FighterState state)
        {
            State = state;
            PlayAnim(anim, 0.03f, true);
            yield return new WaitForSeconds(duration);
            State = FighterState.Idle;
            actionRoutine = null;
        }

        void Die()
        {
            if (actionRoutine != null) { StopCoroutine(actionRoutine); actionRoutine = null; }
            bool wasGuarding = GuardHeld;
            State = FighterState.Dead;
            MoveInput = Vector2.zero;
            GuardHeld = false;
            PlayAnim(wasGuarding ? "Die-Guard" : "Die-LieDown", 0.05f, true);
            HitImpact?.Invoke(0.3f);
            Died?.Invoke(this);
        }

        public void ResetFighter(Vector3 position, Quaternion rotation)
        {
            StopAllCoroutines();
            actionRoutine = null;
            Health = maxHealth;
            Stamina = maxStamina;
            State = FighterState.Idle;
            knockVel = Vector3.zero;
            MoveInput = Vector2.zero;
            GuardHeld = false;
            transform.SetPositionAndRotation(position, rotation);
            currentAnim = "";
            PlayAnim("Idle", 0f, true);
        }
    }
}
