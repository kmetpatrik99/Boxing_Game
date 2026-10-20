using System.Collections;
using UnityEngine;

namespace BoxingGame
{
    /// <summary>
    /// Computer-controlled opponent. Approaches, circles, throws combos, blocks and steps back
    /// when it sees a punch coming, and rests when tired. 'difficulty' (0..1) scales how sharp it is.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class BoxerAI : MonoBehaviour
    {
        [Range(0f, 1f)] public float difficulty = 0.5f;

        enum Mode { Approach, Circle, Retreat, Guard }

        static readonly PunchType[][] Combos =
        {
            new[] { PunchType.Jab },
            new[] { PunchType.Cross },
            new[] { PunchType.Jab, PunchType.Jab },
            new[] { PunchType.Jab, PunchType.Cross },
            new[] { PunchType.Jab, PunchType.Jab, PunchType.Cross },
            new[] { PunchType.Jab, PunchType.Cross, PunchType.Uppercut },
            new[] { PunchType.Uppercut },
        };

        Fighter me, target;
        Mode mode = Mode.Approach;
        float modeUntil, nextThink, nextAttackTime, strafeFlipAt;
        float strafeDir = 1f;
        float guardFrom, guardUntil;
        int seenPunch;
        Coroutine combo;

        void Awake() { me = GetComponent<Fighter>(); }

        public void SetDifficulty(float d) { difficulty = Mathf.Clamp01(d); }

        public void ResetAI()
        {
            if (combo != null) StopCoroutine(combo);
            combo = null;
            mode = Mode.Approach;
            modeUntil = 0f;
            guardFrom = guardUntil = 0f;
            nextAttackTime = Time.time + Mathf.Lerp(2.0f, 0.8f, difficulty);
            seenPunch = target != null ? target.PunchCounter : 0;
        }

        void Update()
        {
            if (target == null) target = me.opponent;
            if (target == null || !me.Active || me.State == FighterState.Dead || target.State == FighterState.Dead)
            {
                me.MoveInput = Vector2.zero;
                me.GuardHeld = false;
                return;
            }

            float d = me.FlatDistanceTo(target);
            float jabRange = me.punches[0].range;

            ReactToTarget(d);

            if (Time.time >= strafeFlipAt)
            {
                strafeDir = -strafeDir;
                strafeFlipAt = Time.time + Random.Range(1.0f, 2.5f);
            }

            // don't get pinned on the ropes
            if (NearEdge() && (mode == Mode.Retreat || mode == Mode.Circle))
            {
                PickStrafeTowardCenter();
                if (mode == Mode.Retreat) mode = Mode.Circle;
            }

            if (Time.time >= nextThink && combo == null && me.State == FighterState.Idle)
            {
                nextThink = Time.time + Random.Range(0.15f, 0.35f);
                Think(d, jabRange);
            }

            Vector2 input;
            bool guard = Time.time >= guardFrom && Time.time < guardUntil;

            if (combo != null)
            {
                input = new Vector2(0f, d > jabRange * 0.9f ? 0.5f : 0f);
            }
            else
            {
                switch (mode)
                {
                    case Mode.Approach: input = new Vector2(strafeDir * 0.25f, 1f); break;
                    case Mode.Circle:
                        input = new Vector2(strafeDir, d < jabRange * 0.85f ? -0.35f : (d > jabRange * 1.05f ? 0.35f : 0f));
                        break;
                    case Mode.Retreat: input = new Vector2(strafeDir * 0.3f, -1f); break;
                    default: input = new Vector2(strafeDir * 0.2f, 0f); guard = true; break;
                }
            }

            me.MoveInput = input;
            me.GuardHeld = guard;
        }

        void ReactToTarget(float d)
        {
            if (target.PunchCounter == seenPunch) return;
            seenPunch = target.PunchCounter;
            if (combo != null) return;                         // committed to our own attack
            if (target.State != FighterState.Punching || target.CurrentPunch == null) return;
            if (d > target.CurrentPunch.range + 0.7f) return;

            float blockChance = Mathf.Lerp(0.25f, 0.70f, difficulty);
            float dodgeChance = Mathf.Lerp(0.15f, 0.25f, difficulty);
            float reaction = Mathf.Lerp(0.30f, 0.10f, difficulty) + Random.Range(0f, 0.08f);
            float r = Random.value;

            if (r < blockChance)
            {
                guardFrom = Time.time + reaction;
                guardUntil = guardFrom + 0.55f;
            }
            else if (r < blockChance + dodgeChance)
            {
                mode = Mode.Retreat;
                modeUntil = Time.time + 0.35f;
            }
        }

        void Think(float d, float jabRange)
        {
            float stam = me.Stamina / me.maxStamina;
            float hp = me.Health / me.maxHealth;

            if (Time.time < modeUntil && mode != Mode.Approach) return;   // honour timed modes

            if (stam < 0.2f) { SetMode(Mode.Guard, Random.Range(0.6f, 1.2f)); return; }
            if (d > jabRange * 1.15f) { SetMode(Mode.Approach, 0.3f); return; }

            float aggression = Mathf.Lerp(0.35f, 0.85f, difficulty) * (hp < 0.3f ? 0.7f : 1f);
            if (Time.time >= nextAttackTime && Random.value < aggression)
            {
                StartCombo();
                return;
            }

            float r = Random.value;
            if (r < 0.45f) SetMode(Mode.Circle, Random.Range(0.5f, 1.2f));
            else if (r < 0.65f) SetMode(Mode.Retreat, Random.Range(0.3f, 0.6f));
            else if (r < 0.80f) SetMode(Mode.Guard, Random.Range(0.4f, 0.9f));
            else SetMode(Mode.Approach, 0.3f);
        }

        void SetMode(Mode m, float duration)
        {
            mode = m;
            modeUntil = Time.time + duration;
        }

        void StartCombo()
        {
            int max = Mathf.RoundToInt(Mathf.Lerp(2f, Combos.Length - 1, difficulty));
            var seq = Combos[Random.Range(0, max + 1)];
            combo = StartCoroutine(ComboRoutine(seq));
        }

        IEnumerator ComboRoutine(PunchType[] seq)
        {
            yield return RunCombo(seq);
            nextAttackTime = Time.time + Mathf.Lerp(2.0f, 0.7f, difficulty) + Random.Range(0f, 0.6f);
            combo = null;
        }

        IEnumerator RunCombo(PunchType[] seq)
        {
            foreach (var punch in seq)
            {
                float wait = 0f;
                while (!me.CanAct)
                {
                    if (me.State == FighterState.Hurt || me.State == FighterState.Stunned || me.State == FighterState.Dead) yield break;
                    wait += Time.deltaTime;
                    if (wait > 1f) yield break;
                    yield return null;
                }
                if (me.FlatDistanceTo(target) > me.punches[(int)punch].range * 1.2f) yield break;
                if (!me.TryPunch(punch)) yield break;

                yield return null;
                while (me.State == FighterState.Punching) yield return null;
                if (me.State != FighterState.Idle) yield break;
                yield return new WaitForSeconds(Random.Range(0.02f, 0.15f));
            }
        }

        bool NearEdge()
        {
            Vector3 p = me.transform.position;
            return Mathf.Abs(p.x - me.ringCenter.x) > me.ringHalfSize.x - 0.5f ||
                   Mathf.Abs(p.z - me.ringCenter.z) > me.ringHalfSize.y - 0.5f;
        }

        void PickStrafeTowardCenter()
        {
            Vector3 fwd = target.transform.position - me.transform.position; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;
            Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
            Vector3 toCenter = me.ringCenter - me.transform.position; toCenter.y = 0f;
            strafeDir = Vector3.Dot(right, toCenter) >= 0f ? 1f : -1f;
            strafeFlipAt = Time.time + 1.5f;
        }
    }
}
