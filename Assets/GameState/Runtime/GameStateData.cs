using System;

namespace ParadiseTycoon.GameState
{
    public enum BuildingStatus { Available, UnderConstruction, Locked }
    public enum WavePhase { BetweenWaves, Active }
    public enum StateError
    {
        None, InvalidOperation, InsufficientCoins, InsufficientEnergy, BuildingUnavailable,
        MaximumLevel, WaveActive, NoActiveWave, WrongWave, DuplicateReward, PersistenceFailed
    }

    public readonly struct StateResult
    {
        public StateError Error { get; }
        public string Message { get; }
        public bool Succeeded => Error == StateError.None;
        public StateResult(StateError error, string message = null) { Error = error; Message = message; }
        public static StateResult Success => new StateResult(StateError.None);
    }

    [Serializable]
    public sealed class BuildingState
    {
        public string id;
        public int level;
        public BuildingStatus status;
        public BuildingState Copy() => (BuildingState)MemberwiseClone();
    }

    /// <summary>Detached, serializable snapshot. Mutating it never mutates the live service.</summary>
    [Serializable]
    public sealed class GameStateData
    {
        public string runId;
        public long coins;
        public int energy;
        public int maxEnergy;
        public BuildingState[] buildings;
        public int managerLevel;
        public string[] unlockedAreas;
        public int completedWaves;
        public int currentWave;
        public WavePhase wavePhase;
        public string lastRewardedWaveId;

        public string CurrentWaveId => runId + ":" + currentWave;

        public GameStateData Copy()
        {
            var copy = (GameStateData)MemberwiseClone();
            copy.buildings = Array.ConvertAll(buildings, item => item.Copy());
            copy.unlockedAreas = (string[])unlockedAreas.Clone();
            return copy;
        }
    }

    public readonly struct WaveReward
    {
        public string WaveId { get; }
        public long Coins { get; }
        public int Energy { get; }
        public WaveReward(string waveId, long coins, int energy = 0)
        { WaveId = waveId; Coins = coins; Energy = energy; }
    }

    /// <summary>Shared contract for wave/building systems and presentation. Commands are main-thread only.</summary>
    public interface IGameState
    {
        event Action Changed;
        long Coins { get; }
        int Energy { get; }
        int MaxEnergy { get; }
        long ResortValue { get; }
        int CurrentWave { get; }
        string CurrentWaveId { get; }
        WavePhase Phase { get; }
        GameStateData Snapshot();
        StateResult TryNewGame();
        StateResult TryLoad();
        StateResult TrySave();
        StateResult TryPurchaseUpgrade(string buildingId);
        StateResult TryStartWave();
        StateResult TryCompleteWave(WaveReward reward);
    }
}
