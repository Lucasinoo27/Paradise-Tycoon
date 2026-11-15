using System;
using System.IO;
using NUnit.Framework;

namespace ParadiseTycoon.GameState.Tests
{
    public sealed class GameStateTests
    {
        sealed class MemoryStore : IGameStateStore
        {
            public GameStateData Checkpoint;
            public bool FailWrites;
            public GameStateData Load() => Checkpoint?.Copy();
            public void Save(GameStateData state)
            {
                if (FailWrites) throw new IOException("Simulated full disk.");
                Checkpoint = state.Copy();
            }
        }

        MemoryStore store;
        GameConfiguration configuration;
        GameStateService game;

        [SetUp]
        public void SetUp()
        {
            store = new MemoryStore();
            configuration = new GameConfiguration();
            game = new GameStateService(configuration, store);
            Assert.That(game.TryLoad().Succeeded, Is.True);
        }

        [Test]
        public void NewGameResetsAllProgressAndHasExactlyThreeAvailableBuildings()
        {
            var initial = game.Snapshot();
            Assert.That(game.TryPurchaseUpgrade("hotel").Succeeded, Is.True);
            Assert.That(game.TryStartWave().Succeeded, Is.True);
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, 500)).Succeeded, Is.True);
            Assert.That(game.TryNewGame().Succeeded, Is.True);
            var reset = game.Snapshot();
            Assert.That(reset.runId, Is.Not.EqualTo(initial.runId));
            Assert.That(reset.coins, Is.EqualTo(initial.coins));
            Assert.That(reset.energy, Is.EqualTo(80));
            Assert.That(reset.maxEnergy, Is.EqualTo(100));
            Assert.That(reset.completedWaves, Is.Zero);
            Assert.That(reset.currentWave, Is.EqualTo(1));
            Assert.That(reset.managerLevel, Is.EqualTo(1));
            Assert.That(reset.unlockedAreas, Is.EqualTo(new[] { "island" }));
            Assert.That(reset.buildings.Length, Is.EqualTo(6));
            Assert.That(Array.FindAll(reset.buildings, b => b.status == BuildingStatus.Available).Length, Is.EqualTo(3));
            foreach (string id in new[] { "hotel", "restaurant", "beach_bar" })
                Assert.That(Array.Find(reset.buildings, b => b.id == id).level, Is.EqualTo(1));
            foreach (string id in new[] { "bungalow", "pool", "spa" })
            {
                var building = Array.Find(reset.buildings, b => b.id == id);
                Assert.That(building.level, Is.Zero);
                Assert.That(building.status, Is.EqualTo(BuildingStatus.UnderConstruction));
            }
            Assert.That(game.ResortValue, Is.EqualTo(24500));
        }

        [Test]
        public void UpgradeDebitsPriceAndRaisesDerivedValueInOneCheckpoint()
        {
            Assert.That(game.TryPurchaseUpgrade("hotel").Succeeded, Is.True);
            Assert.That(game.Coins, Is.EqualTo(1000));
            Assert.That(game.ResortValue, Is.EqualTo(29500));
            var restarted = new GameStateService(configuration, store);
            Assert.That(restarted.TryLoad().Succeeded, Is.True);
            Assert.That(restarted.Coins, Is.EqualTo(game.Coins));
            Assert.That(restarted.ResortValue, Is.EqualTo(game.ResortValue));
        }

        [Test]
        public void InsufficientCoinsRejectsWholePurchaseWithoutAnEvent()
        {
            configuration.startingCoins = 249;
            game = new GameStateService(configuration, store);
            game.TryNewGame();
            int changes = 0;
            game.Changed += () => changes++;
            Assert.That(game.TryPurchaseUpgrade("hotel").Error, Is.EqualTo(StateError.InsufficientCoins));
            Assert.That(game.Coins, Is.EqualTo(249));
            Assert.That(game.ResortValue, Is.EqualTo(24500));
            Assert.That(store.Checkpoint.coins, Is.EqualTo(249));
            Assert.That(changes, Is.Zero);
        }

        [TestCase(null, StateError.InvalidOperation)]
        [TestCase("unknown", StateError.InvalidOperation)]
        [TestCase("spa", StateError.BuildingUnavailable)]
        public void InvalidBuildingPurchaseIsRejected(string id, StateError error)
        {
            Assert.That(game.TryPurchaseUpgrade(id).Error, Is.EqualTo(error));
            Assert.That(game.Coins, Is.EqualTo(1250));
        }

        [Test]
        public void MaximumLevelCannotBePurchasedAgain()
        {
            configuration.startingCoins = 10000;
            game = new GameStateService(configuration, store);
            game.TryNewGame();
            for (int i = 0; i < 3; i++) Assert.That(game.TryPurchaseUpgrade("hotel").Succeeded, Is.True);
            long coins = game.Coins;
            Assert.That(game.TryPurchaseUpgrade("hotel").Error, Is.EqualTo(StateError.MaximumLevel));
            Assert.That(game.Coins, Is.EqualTo(coins));
        }

        [Test]
        public void CompletedWaveRewardIsIdempotentAcrossRestartAndLaterWaves()
        {
            game.TryStartWave();
            var reward = new WaveReward(game.CurrentWaveId, 300, 5);
            Assert.That(game.TryCompleteWave(reward).Succeeded, Is.True);
            Assert.That(game.Coins, Is.EqualTo(1550));
            Assert.That(game.Energy, Is.EqualTo(75));
            game = new GameStateService(configuration, store);
            Assert.That(game.TryLoad().Succeeded, Is.True);
            Assert.That(game.TryCompleteWave(reward).Error, Is.EqualTo(StateError.DuplicateReward));
            game.TryStartWave();
            Assert.That(game.TryCompleteWave(reward).Error, Is.EqualTo(StateError.DuplicateReward));
            Assert.That(game.CurrentWave, Is.EqualTo(2));
            Assert.That(game.Coins, Is.EqualTo(1550));
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, 200)).Succeeded, Is.True);
            Assert.That(game.CurrentWave, Is.EqualTo(3));
            Assert.That(game.TryCompleteWave(reward).Error, Is.EqualTo(StateError.DuplicateReward));
        }

        [Test]
        public void WrongWaveAndNegativeOrOverflowingRewardsAreRejected()
        {
            game.TryStartWave();
            Assert.That(game.TryCompleteWave(new WaveReward("other:1", 1)).Error, Is.EqualTo(StateError.WrongWave));
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, -1)).Error, Is.EqualTo(StateError.InvalidOperation));
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, 1, -1)).Error, Is.EqualTo(StateError.InvalidOperation));
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, long.MaxValue)).Error, Is.EqualTo(StateError.InvalidOperation));
            Assert.That(game.Coins, Is.EqualTo(1250));
            Assert.That(game.CurrentWave, Is.EqualTo(1));
            Assert.That(game.Phase, Is.EqualTo(WavePhase.Active));
        }

        [Test]
        public void RewardEnergyIsCappedWithoutIntegerOverflow()
        {
            game.TryStartWave();
            Assert.That(game.TryCompleteWave(new WaveReward(game.CurrentWaveId, 0, int.MaxValue)).Succeeded, Is.True);
            Assert.That(game.Energy, Is.EqualTo(100));
        }

        [Test]
        public void FailedPurchaseSaveChangesNeitherMemoryNorCheckpoint()
        {
            store.FailWrites = true;
            int changes = 0;
            game.Changed += () => changes++;
            Assert.That(game.TryPurchaseUpgrade("hotel").Error, Is.EqualTo(StateError.PersistenceFailed));
            Assert.That(game.Coins, Is.EqualTo(1250));
            Assert.That(game.ResortValue, Is.EqualTo(24500));
            Assert.That(store.Checkpoint.coins, Is.EqualTo(1250));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void FailedRewardSaveCanBeRetriedExactlyOnce()
        {
            game.TryStartWave();
            var reward = new WaveReward(game.CurrentWaveId, 500);
            store.FailWrites = true;
            Assert.That(game.TryCompleteWave(reward).Error, Is.EqualTo(StateError.PersistenceFailed));
            Assert.That(game.Phase, Is.EqualTo(WavePhase.Active));
            Assert.That(game.Coins, Is.EqualTo(1250));
            Assert.That(store.Checkpoint.currentWave, Is.EqualTo(1));
            store.FailWrites = false;
            Assert.That(game.TryCompleteWave(reward).Succeeded, Is.True);
            Assert.That(game.TryCompleteWave(reward).Error, Is.EqualTo(StateError.DuplicateReward));
            Assert.That(game.Coins, Is.EqualTo(1750));
        }

        [Test]
        public void ActiveWaveDoesNotOverwriteBetweenWaveCheckpoint()
        {
            game.TryStartWave();
            Assert.That(game.TryStartWave().Error, Is.EqualTo(StateError.WaveActive));
            Assert.That(game.TrySave().Error, Is.EqualTo(StateError.WaveActive));
            Assert.That(game.TryLoad().Error, Is.EqualTo(StateError.WaveActive));
            Assert.That(game.TryPurchaseUpgrade("hotel").Error, Is.EqualTo(StateError.WaveActive));
            var restart = new GameStateService(configuration, store);
            restart.TryLoad();
            Assert.That(restart.Phase, Is.EqualTo(WavePhase.BetweenWaves));
            Assert.That(restart.Energy, Is.EqualTo(80));
        }

        [Test]
        public void InsufficientEnergyRejectsWaveWithoutChangingState()
        {
            configuration.startingEnergy = 0;
            game = new GameStateService(configuration, store);
            game.TryNewGame();
            Assert.That(game.TryStartWave().Error, Is.EqualTo(StateError.InsufficientEnergy));
            Assert.That(game.Phase, Is.EqualTo(WavePhase.BetweenWaves));
        }

        [Test]
        public void DetachedSnapshotsAndCallerConfigurationCannotMutateLiveState()
        {
            var snapshot = game.Snapshot();
            snapshot.coins = 0;
            snapshot.buildings[0].level = 4;
            snapshot.unlockedAreas[0] = "changed";
            configuration.buildings[0].upgradeCosts[0] = 0;
            Assert.That(game.Coins, Is.EqualTo(1250));
            Assert.That(game.ResortValue, Is.EqualTo(24500));
            Assert.That(game.Snapshot().unlockedAreas[0], Is.EqualTo("island"));
            game.TryPurchaseUpgrade("hotel");
            Assert.That(game.Coins, Is.EqualTo(1000));
        }

        [Test]
        public void InvalidCheckpointCannotReplaceExistingLiveState()
        {
            store.Checkpoint.buildings[0].level = 99;
            Assert.That(game.TryLoad().Error, Is.EqualTo(StateError.PersistenceFailed));
            Assert.That(game.ResortValue, Is.EqualTo(24500));
            Assert.That(game.Coins, Is.EqualTo(1250));
        }

        [Test]
        public void ConfigurationRejectsDecreasingValuesAndDuplicateIds()
        {
            configuration.buildings[0].levelValues[1] = 1;
            Assert.Throws<ArgumentException>(() => new GameStateService(configuration, store));
            configuration = new GameConfiguration();
            configuration.buildings[1].id = "hotel";
            Assert.Throws<ArgumentException>(() => new GameStateService(configuration, store));
        }
    }
}
