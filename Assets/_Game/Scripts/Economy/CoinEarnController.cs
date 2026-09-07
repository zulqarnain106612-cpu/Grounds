using UnityEngine;
using JetFighter.Enemy;

namespace JetFighter.Economy
{
    /// <summary>
    /// Earns coins from survival time and from kills.
    ///
    /// Both sources go through Wallet.Add, so a third source -- ADR-004's
    /// deferred achievement trickle, or anything Phase 5 adds -- needs no
    /// change here beyond a call.
    ///
    /// Saving is debounced rather than done on every coin. A write per kill is
    /// a file write several times a second on a phone, which is both a stall
    /// and a way to be mid-write when the OS kills the process.
    /// </summary>
    public class CoinEarnController : MonoBehaviour
    {
        [Header("Earn rates (data, not code)")]
        [Tooltip("Coins granted per whole second survived.")]
        [Min(0)]
        [SerializeField] private int coinsPerSecondSurvived = 1;

        [Tooltip("Coins granted per enemy killed.")]
        [Min(0)]
        [SerializeField] private int coinsPerKill = 5;

        [Tooltip("Seconds between saves. Zero saves on every change.")]
        [Min(0f)]
        [SerializeField] private float saveIntervalSeconds = 10f;

        private float survivalRemainder;
        private float sinceLastSave;
        private bool dirty;

        /// <summary>The wallet being credited. Created on first use if unset.</summary>
        public Wallet Wallet { get; private set; } = new Wallet();

        /// <summary>Coins earned this run from survival alone. For a run summary.</summary>
        public int EarnedFromSurvival { get; private set; }

        /// <summary>Coins earned this run from kills alone.</summary>
        public int EarnedFromKills { get; private set; }

        public int CoinsPerSecondSurvived
        {
            get => coinsPerSecondSurvived;
            set => coinsPerSecondSurvived = Mathf.Max(0, value);
        }

        public int CoinsPerKill
        {
            get => coinsPerKill;
            set => coinsPerKill = Mathf.Max(0, value);
        }

        private void Awake()
        {
            SaveService.Load(Wallet);
        }

        /// <summary>Subscribes to an enemy's death. Called by the spawner on each spawn.</summary>
        public void Track(EnemyHealth enemy)
        {
            if (enemy == null)
            {
                return;
            }
            // Removed first: pooled enemies are tracked again on every spawn,
            // and a doubled listener pays the kill bounty twice.
            enemy.OnDied.RemoveListener(HandleKill);
            enemy.OnDied.AddListener(HandleKill);
        }

        private void HandleKill()
        {
            AwardKill();
        }

        /// <summary>Credits one kill.</summary>
        public void AwardKill()
        {
            if (coinsPerKill <= 0)
            {
                return;
            }
            Wallet.Add(CurrencyType.Coins, coinsPerKill);
            EarnedFromKills += coinsPerKill;
            dirty = true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Accrues survival earnings and saves on the interval.
        ///
        /// The remainder is carried rather than reset, so a run at 30fps earns
        /// exactly as much as one at 60 -- a fractional second dropped every
        /// frame costs a visible amount over a five-minute run.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (coinsPerSecondSurvived > 0)
            {
                survivalRemainder += deltaTime;
                int wholeSeconds = Mathf.FloorToInt(survivalRemainder);
                if (wholeSeconds > 0)
                {
                    survivalRemainder -= wholeSeconds;
                    int award = wholeSeconds * coinsPerSecondSurvived;
                    Wallet.Add(CurrencyType.Coins, award);
                    EarnedFromSurvival += award;
                    dirty = true;
                }
            }

            sinceLastSave += deltaTime;
            if (dirty && sinceLastSave >= saveIntervalSeconds)
            {
                Flush();
            }
        }

        /// <summary>
        /// Writes the wallet now. Called on the save interval, and on pause
        /// and quit -- a phone backgrounding the app is the most likely last
        /// moment before the process is killed.
        /// </summary>
        public void Flush()
        {
            sinceLastSave = 0f;
            if (!dirty)
            {
                return;
            }
            if (SaveService.Save(Wallet))
            {
                dirty = false;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Flush();
            }
        }

        private void OnApplicationQuit()
        {
            Flush();
        }
    }
}
