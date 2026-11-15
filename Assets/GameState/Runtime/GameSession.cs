using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParadiseTycoon.GameState
{
    /// <summary>One persistent owner. Scene/HUD reloads reuse the same state and subscriptions.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public static event Action<IGameState> StateAvailable;
        [SerializeField] GameRules rules;
        public IGameState State { get; private set; }
        public string LastError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; StateAvailable = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSceneListener()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == "Assets/Scenes/Main.unity") EnsureCreated();
        }

        public static GameSession EnsureCreated()
        {
            if (Instance == null) new GameObject("GameSession").AddComponent<GameSession>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (rules == null) rules = Resources.Load<GameRules>("GameRules");
            var configuration = rules != null ? rules.Configuration : new GameConfiguration();
            var store = new JsonGameStateStore(Path.Combine(Application.persistentDataPath, "game-state.json"), configuration);
            var service = new GameStateService(configuration, store);
            var result = service.TryLoad();
            if (!result.Succeeded)
            {
                LastError = result.Message;
                Debug.LogError("Game checkpoint could not be loaded. Existing data was preserved: " + LastError);
                return;
            }
            State = service;
            if (!string.IsNullOrEmpty(store.LastLoadWarning)) Debug.LogWarning(store.LastLoadWarning);
            StateAvailable?.Invoke(State);
        }

        void OnApplicationPause(bool paused) { if (paused) SaveCheckpoint(); }
        void OnApplicationQuit() => SaveCheckpoint();
        void SaveCheckpoint()
        {
            if (State == null || State.Phase != WavePhase.BetweenWaves) return;
            var result = State.TrySave();
            if (!result.Succeeded) Debug.LogWarning("Game checkpoint was not saved: " + result.Message);
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
