using UnityEngine;

namespace BoxingGame
{
    /// <summary>Over-the-shoulder fight camera that keeps both boxers in frame, with a little hit shake.</summary>
    public class BoxingCamera : MonoBehaviour
    {
        public Transform player;
        public Transform opponent;
        public float height = 1.9f;
        public float minBack = 2.8f;
        public float maxBack = 4.4f;
        public float sideOffset = 0.9f;
        public float lookHeight = 1.15f;
        public float followSmooth = 0.18f;
        public float groundY = 0f;

        Vector3 vel;
        float shake;

        void OnEnable() { Fighter.HitImpact += OnImpact; }
        void OnDisable() { Fighter.HitImpact -= OnImpact; }
        void OnImpact(float amount) { shake = Mathf.Max(shake, amount); }

        void LateUpdate()
        {
            if (player == null || opponent == null) return;

            Vector3 a = player.position;
            Vector3 b = opponent.position;
            Vector3 dir = b - a; dir.y = 0f;
            float dist = dir.magnitude;
            dir = dist > 0.01f ? dir / dist : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir);

            float back = Mathf.Lerp(minBack, maxBack, Mathf.Clamp01(dist / 3f));
            Vector3 desired = a - dir * back + right * sideOffset;
            desired.y = groundY + height;

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, followSmooth);

            Vector3 mid = (a + b) * 0.5f;
            Vector3 lookAt = new Vector3(mid.x, groundY + lookHeight, mid.z);
            Quaternion rot = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-10f * Time.deltaTime));

            if (shake > 0.001f)
            {
                transform.position += Random.insideUnitSphere * shake * 0.25f;
                shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.2f);
            }
        }
    }
}
