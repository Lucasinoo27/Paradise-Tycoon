using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ParadiseTycoon.GameState.Tests
{
    public sealed class SaveGameTests
    {
        string directory, path;
        GameConfiguration configuration;
        JsonGameStateStore store;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ParadiseTycoonTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "game-state.json");
            configuration = new GameConfiguration();
            store = new JsonGameStateStore(path, configuration);
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test]
        public void TwoWavesWithUpgradeSurviveSeparateServiceAndStoreInstances()
        {
            var first = new GameStateService(configuration, store);
            var initialized = first.TryLoad();
            Assert.That(initialized.Succeeded, Is.True, initialized.Message);
            first.TryStartWave();
            var firstReward = new WaveReward(first.CurrentWaveId, 500, 3);
            Assert.That(first.TryCompleteWave(firstReward).Succeeded, Is.True);
            Assert.That(first.TryPurchaseUpgrade("hotel").Succeeded, Is.True);
            string checkpoint = JsonUtility.ToJson(first.Snapshot());
            var restarted = new GameStateService(configuration, new JsonGameStateStore(path, configuration));
            Assert.That(restarted.TryLoad().Succeeded, Is.True);
            Assert.That(JsonUtility.ToJson(restarted.Snapshot()), Is.EqualTo(checkpoint));
            Assert.That(restarted.ResortValue, Is.EqualTo(first.ResortValue));
            Assert.That(restarted.TryCompleteWave(firstReward).Error, Is.EqualTo(StateError.DuplicateReward));
            restarted.TryStartWave();
            Assert.That(restarted.TryCompleteWave(new WaveReward(restarted.CurrentWaveId, 100)).Succeeded, Is.True);
            Assert.That(restarted.TrySave().Succeeded, Is.True);
            var third = new GameStateService(configuration, new JsonGameStateStore(path, configuration));
            Assert.That(third.TryLoad().Succeeded, Is.True);
            Assert.That(third.CurrentWave, Is.EqualTo(3));
            Assert.That(third.Coins, Is.EqualTo(1600));
            Assert.That(third.Energy, Is.EqualTo(63));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            StringAssert.Contains("\"formatVersion\": 1", File.ReadAllText(path));
        }

        [Test]
        public void TruncatedPrimaryRecoversPreviousWholeCheckpoint()
        {
            var game = new GameStateService(configuration, store);
            var initialized = game.TryLoad();
            Assert.That(initialized.Succeeded, Is.True, initialized.Message);
            game.TryPurchaseUpgrade("hotel");
            File.WriteAllText(path, "{truncated");
            var restarted = new GameStateService(configuration, store);
            Assert.That(restarted.TryLoad().Succeeded, Is.True);
            Assert.That(restarted.Coins, Is.EqualTo(1250));
            Assert.That(restarted.ResortValue, Is.EqualTo(24500));
            Assert.That(store.LastLoadWarning, Is.Not.Empty);
        }

        [Test]
        public void FutureFormatIsPreservedInsteadOfSilentlyStartingOver()
        {
            var game = new GameStateService(configuration, store);
            var initialized = game.TryLoad();
            Assert.That(initialized.Succeeded, Is.True, initialized.Message);
            game.TryPurchaseUpgrade("hotel");
            string future = File.ReadAllText(path).Replace("\"formatVersion\": 1", "\"formatVersion\": 99");
            File.WriteAllText(path, future);
            Assert.That(game.TryLoad().Error, Is.EqualTo(StateError.PersistenceFailed));
            Assert.That(File.ReadAllText(path), Is.EqualTo(future));
            Assert.That(game.Coins, Is.EqualTo(1000));
        }

        [Test]
        public void CorruptSaveWithoutBackupIsNotOverwritten()
        {
            File.WriteAllText(path, "{}");
            var game = new GameStateService(configuration, store);
            Assert.That(game.TryLoad().Error, Is.EqualTo(StateError.PersistenceFailed));
            Assert.That(File.ReadAllText(path), Is.EqualTo("{}"));
        }

        [Test]
        public void StoreRejectsActiveWaveSave()
        {
            var game = new GameStateService(configuration, store);
            var initialized = game.TryLoad();
            Assert.That(initialized.Succeeded, Is.True, initialized.Message);
            string previous = File.ReadAllText(path);
            game.TryStartWave();
            Assert.Throws<InvalidDataException>(() => store.Save(game.Snapshot()));
            Assert.That(File.ReadAllText(path), Is.EqualTo(previous));
        }
    }
}
