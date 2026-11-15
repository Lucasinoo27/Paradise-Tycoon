using System;
using System.Collections.Generic;
using System.IO;

namespace ParadiseTycoon.GameState
{
    public interface IGameStateStore
    {
        // Null means no checkpoint exists. Invalid/unreadable checkpoints throw; never silently overwrite them.
        GameStateData Load();
        void Save(GameStateData state);
    }

    public sealed class GameStateService : IGameState
    {
        readonly GameConfiguration configuration;
        readonly IGameStateStore store;
        readonly Dictionary<string, BuildingDefinition> definitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
        GameStateData state;
        public event Action Changed;
        public long Coins => state.coins;
        public int Energy => state.energy;
        public int MaxEnergy => state.maxEnergy;
        public int CurrentWave => state.currentWave;
        public string CurrentWaveId => state.CurrentWaveId;
        public WavePhase Phase => state.wavePhase;
        public long ResortValue
        {
            get
            {
                long total = 0;
                foreach (var building in state.buildings)
                    if (building.level > 0) total = checked(total + definitions[building.id].levelValues[building.level - 1]);
                return total;
            }
        }

        public GameStateService(GameConfiguration configuration, IGameStateStore store)
        {
            this.configuration = (configuration ?? throw new ArgumentNullException(nameof(configuration))).Copy();
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            foreach (var definition in this.configuration.buildings) definitions.Add(definition.id, definition);
            state = InitialState();
        }

        GameStateData InitialState() => new GameStateData
        {
            runId = Guid.NewGuid().ToString("N"), coins = configuration.startingCoins,
            energy = configuration.startingEnergy, maxEnergy = configuration.maxEnergy,
            buildings = Array.ConvertAll(configuration.buildings, item => new BuildingState
                { id = item.id, level = item.initialLevel, status = item.initialStatus }),
            managerLevel = configuration.managerLevel, unlockedAreas = (string[])configuration.unlockedAreas.Clone(),
            completedWaves = 0, currentWave = 1, wavePhase = WavePhase.BetweenWaves, lastRewardedWaveId = ""
        };

        public GameStateData Snapshot() => state.Copy();
        public StateResult TryNewGame() => Commit(InitialState(), true);

        public StateResult TryLoad()
        {
            if (Phase == WavePhase.Active) return new StateResult(StateError.WaveActive);
            try
            {
                var loaded = store.Load();
                if (loaded == null) return TryNewGame();
                Validate(loaded, true);
                state = loaded.Copy();
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            { return new StateResult(StateError.PersistenceFailed, exception.Message); }
            Changed?.Invoke();
            return StateResult.Success;
        }

        public StateResult TrySave()
        {
            if (Phase != WavePhase.BetweenWaves) return new StateResult(StateError.WaveActive);
            return Commit(state.Copy(), true);
        }

        public StateResult TryPurchaseUpgrade(string buildingId)
        {
            if (Phase != WavePhase.BetweenWaves) return new StateResult(StateError.WaveActive);
            if (buildingId == null || !definitions.TryGetValue(buildingId, out var definition))
                return new StateResult(StateError.InvalidOperation);
            var building = Array.Find(state.buildings, item => item.id == buildingId);
            if (building.status != BuildingStatus.Available) return new StateResult(StateError.BuildingUnavailable);
            if (building.level >= definition.levelValues.Length) return new StateResult(StateError.MaximumLevel);
            long cost = definition.upgradeCosts[building.level - 1];
            if (Coins < cost) return new StateResult(StateError.InsufficientCoins);
            var next = state.Copy();
            next.coins -= cost;
            Array.Find(next.buildings, item => item.id == buildingId).level++;
            return Commit(next, true);
        }

        public StateResult TryStartWave()
        {
            if (Phase != WavePhase.BetweenWaves) return new StateResult(StateError.WaveActive);
            if (Energy < configuration.waveEnergyCost) return new StateResult(StateError.InsufficientEnergy);
            // Keep the last between-wave checkpoint. Active-wave recovery belongs to Sprint 4.
            var next = state.Copy();
            next.energy -= configuration.waveEnergyCost;
            next.wavePhase = WavePhase.Active;
            return Commit(next, false);
        }

        public StateResult TryCompleteWave(WaveReward reward)
        {
            if (string.IsNullOrWhiteSpace(reward.WaveId) || reward.Coins < 0 || reward.Energy < 0)
                return new StateResult(StateError.InvalidOperation);
            if (reward.WaveId.StartsWith(state.runId + ":", StringComparison.Ordinal) &&
                int.TryParse(reward.WaveId.Substring(state.runId.Length + 1), out int completed) &&
                completed > 0 && completed <= state.completedWaves)
                return new StateResult(StateError.DuplicateReward);
            if (Phase != WavePhase.Active) return new StateResult(StateError.NoActiveWave);
            if (!string.Equals(reward.WaveId, CurrentWaveId, StringComparison.Ordinal)) return new StateResult(StateError.WrongWave);
            if (reward.Coins > long.MaxValue - Coins || state.currentWave == int.MaxValue)
                return new StateResult(StateError.InvalidOperation);
            var next = state.Copy();
            next.coins += reward.Coins;
            next.energy = (int)Math.Min(next.maxEnergy, (long)next.energy + reward.Energy);
            next.lastRewardedWaveId = CurrentWaveId;
            next.completedWaves = next.currentWave;
            next.currentWave++;
            next.wavePhase = WavePhase.BetweenWaves;
            return Commit(next, true);
        }

        StateResult Commit(GameStateData candidate, bool persist)
        {
            try
            {
                Validate(candidate, persist);
                if (persist) store.Save(candidate.Copy());
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            { return new StateResult(StateError.PersistenceFailed, exception.Message); }
            state = candidate;
            Changed?.Invoke();
            return StateResult.Success;
        }

        static bool IsPersistenceException(Exception exception) => exception is IOException || exception is InvalidDataException ||
            exception is UnauthorizedAccessException || exception is ArgumentException || exception is OverflowException;

        void Validate(GameStateData candidate, bool checkpoint)
        {
            if (candidate == null || !Guid.TryParseExact(candidate.runId, "N", out _) || candidate.coins < 0 ||
                candidate.maxEnergy < 1 || candidate.energy < 0 || candidate.energy > candidate.maxEnergy ||
                candidate.managerLevel < 1 || candidate.currentWave < 1 || candidate.completedWaves < 0 ||
                (long)candidate.completedWaves + 1 != candidate.currentWave ||
                !Enum.IsDefined(typeof(WavePhase), candidate.wavePhase) ||
                (checkpoint && candidate.wavePhase != WavePhase.BetweenWaves) ||
                candidate.buildings == null || candidate.buildings.Length != definitions.Count ||
                candidate.unlockedAreas == null || candidate.unlockedAreas.Length == 0 ||
                (candidate.completedWaves == 0 ? !string.IsNullOrEmpty(candidate.lastRewardedWaveId) :
                    candidate.lastRewardedWaveId != candidate.runId + ":" + candidate.completedWaves))
                throw new InvalidDataException("Invalid or incompatible game checkpoint.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var building in candidate.buildings)
                if (building == null || building.id == null || !ids.Add(building.id) ||
                    !definitions.TryGetValue(building.id, out var definition) || building.level < 0 ||
                    building.level > definition.levelValues.Length || !Enum.IsDefined(typeof(BuildingStatus), building.status) ||
                    (building.status == BuildingStatus.Available) != (building.level > 0))
                    throw new InvalidDataException("Invalid building state.");
            ids.Clear();
            foreach (var area in candidate.unlockedAreas)
                if (string.IsNullOrWhiteSpace(area) || !ids.Add(area)) throw new InvalidDataException("Invalid area state.");
        }
    }
}
