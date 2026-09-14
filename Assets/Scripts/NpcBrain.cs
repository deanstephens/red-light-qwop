using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// Drives a ragdoll as a computer-controlled runner. It walks the lift-and-plant stride
    /// (the gait that measured best), stops when it notices a red light, and is eliminated if
    /// it is still moving once the light is judged. Eliminated runners go limp and collapse.
    /// </summary>
    public class NpcBrain : MonoBehaviour
    {
        public enum NpcState { Active, Fallen, Eliminated, Finished }

        public GameManager Game;
        public Ragdoll Ragdoll;
        public LimbController Controller;

        [Header("Gait")]
        [Tooltip("Seconds per stride beat. Around 0.2 walks well; slower tends to fall.")]
        public float StepTime = 0.2f;
        [Tooltip("Random per-beat variation added to StepTime.")]
        public float TempoJitter = 0.02f;
        [Tooltip("Seconds before the first step after a green light.")]
        public float StartDelay = 0.3f;

        [Header("Reaction to red")]
        [Tooltip("Reaction time is rolled between these each red light.")]
        public float ReactionMin = 0.1f;
        public float ReactionMax = 0.5f;
        [Tooltip("Chance per red light of a lapse that adds LapseExtra seconds. Lapsed runners usually get caught.")]
        [Range(0f, 1f)] public float LapseChance = 0.3f;
        public Vector2 LapseExtra = new Vector2(0.4f, 1.0f);

        [Header("Elimination")]
        public Color EliminatedTint = new Color(0.45f, 0.4f, 0.4f);
        public float TopplePush = 50f;

        public NpcState State { get; private set; } = NpcState.Active;
        public float CurrentReaction { get; private set; }

        static readonly LimbInput[] k_Stride =
        {
            new LimbInput { LeftHip = 1f, RightHip = -1f, LeftKnee = 1f, RightKnee = -1f },
            new LimbInput { LeftHip = 1f, RightHip = -1f, LeftKnee = -1f, RightKnee = 1f },
            new LimbInput { LeftHip = -1f, RightHip = 1f, LeftKnee = -1f, RightKnee = 1f },
            new LimbInput { LeftHip = -1f, RightHip = 1f, LeftKnee = 1f, RightKnee = -1f },
        };

        System.Random m_Rng;
        float m_BeatClock;
        float m_BeatLength;
        int m_BeatIndex;
        float m_GreenElapsed;
        float m_RedElapsed;
        bool m_SawRed;
        float m_MovingTime;
        float m_DownTime;

        public void Seed(int seed) => m_Rng = new System.Random(seed);

        void Awake()
        {
            if (Ragdoll == null) Ragdoll = GetComponent<Ragdoll>();
            if (Controller == null) Controller = GetComponent<LimbController>();
            if (m_Rng == null) m_Rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            Controller.UseKeyboard = false;
            m_BeatLength = StepTime;
            m_BeatIndex = m_Rng.Next(k_Stride.Length);
        }

        void Start()
        {
            if (Game == null) Game = FindAnyObjectByType<GameManager>();
        }

        void Update()
        {
            if (Game == null || State != NpcState.Active) return;

            if (Game.State != GameState.Playing)
            {
                Controller.Current = default;
                return;
            }

            float dt = Time.deltaTime;
            bool red = Game.Phase == LightPhase.Red;

            if (red)
            {
                if (!m_SawRed)
                {
                    m_SawRed = true;
                    m_RedElapsed = 0f;
                    m_GreenElapsed = 0f;
                    CurrentReaction = RollReaction();
                }
                m_RedElapsed += dt;
            }
            else
            {
                m_SawRed = false;
                m_GreenElapsed += dt;
            }

            // Judged exactly like the player.
            if (Game.IsRedJudging && Ragdoll.Speed > Game.MoveThreshold)
            {
                m_MovingTime += dt;
                if (m_MovingTime >= Game.MoveTolerance)
                {
                    Eliminate();
                    return;
                }
            }
            else
            {
                m_MovingTime = 0f;
            }

            if (Game.FinishLine != null && Ragdoll.Position.z >= Game.FinishLine.position.z)
            {
                State = NpcState.Finished;
                Controller.Current = default;
                return;
            }

            if (Ragdoll.Pelvis.position.y < 0.45f)
            {
                m_DownTime += dt;
                if (m_DownTime > 1f)
                {
                    State = NpcState.Fallen;
                    Controller.Current = default;
                    Ragdoll.GoLimp();
                    return;
                }
            }
            else
            {
                m_DownTime = 0f;
            }

            bool wantsToMove = red ? m_RedElapsed < CurrentReaction : m_GreenElapsed >= StartDelay;
            if (wantsToMove)
            {
                m_BeatClock += dt;
                if (m_BeatClock >= m_BeatLength)
                {
                    m_BeatClock = 0f;
                    m_BeatIndex = (m_BeatIndex + 1) % k_Stride.Length;
                    m_BeatLength = StepTime + ((float)m_Rng.NextDouble() * 2f - 1f) * TempoJitter;
                }
                Controller.Current = k_Stride[m_BeatIndex];
            }
            else
            {
                Controller.Current = default;
                m_BeatClock = 0f;
            }
        }

        float RollReaction()
        {
            float r = Mathf.Lerp(ReactionMin, ReactionMax, (float)m_Rng.NextDouble());
            if (m_Rng.NextDouble() < LapseChance)
            {
                r += Mathf.Lerp(LapseExtra.x, LapseExtra.y, (float)m_Rng.NextDouble());
            }
            return r;
        }

        public void Eliminate()
        {
            if (State == NpcState.Eliminated) return;
            State = NpcState.Eliminated;
            Controller.Current = default;
            Controller.InputEnabled = false;
            Ragdoll.GoLimp();
            Ragdoll.Tint(EliminatedTint);

            // A small sideways shove so it visibly topples rather than folding in place.
            Vector3 push = new Vector3((float)m_Rng.NextDouble() * 2f - 1f, 0f, 0.5f).normalized * TopplePush;
            Ragdoll.Torso.AddForce(push, ForceMode.Impulse);
        }
    }
}
