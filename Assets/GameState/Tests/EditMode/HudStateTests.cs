using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ParadiseTycoon.GameState.Tests
{
    public sealed class HudStateTests
    {
        sealed class MemoryStore : IGameStateStore
        {
            GameStateData state;
            public GameStateData Load() => state?.Copy();
            public void Save(GameStateData value) { state = value.Copy(); }
        }

        [Test]
        public void MainHudTracksNewGamePurchaseRewardLoadAndRebinding()
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var hud = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                    .Single(component => component != null && component.GetType().FullName == "ParadiseTycoon.Hud.ResortHud");
                var type = hud.GetType();
                var bind = type.GetMethod("BindState");
                Assert.That(bind, Is.Not.Null, "The existing HUD must expose state binding.");
                var store = new MemoryStore();
                var configuration = new GameConfiguration();
                var game = new GameStateService(configuration, store);
                game.TryNewGame();
                bind.Invoke(hud, new object[] { game });
                AssertLabels(hud, game);
                Assert.That(game.TryPurchaseUpgrade("hotel").Succeeded, Is.True);
                AssertLabels(hud, game);
                game.TryStartWave();
                AssertLabels(hud, game);
                game.TryCompleteWave(new WaveReward(game.CurrentWaveId, 700, 4));
                AssertLabels(hud, game);
                var other = new GameStateService(configuration, store);
                other.TryLoad();
                bind.Invoke(hud, new object[] { other });
                AssertLabels(hud, other);
                game.TryNewGame();
                AssertLabels(hud, other); // The previous state was unsubscribed.
                other.TryNewGame();
                AssertLabels(hud, other);
                other.TryPurchaseUpgrade("restaurant");
                other.TryLoad();
                AssertLabels(hud, other);
                type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
                string before = (string)type.GetProperty("CoinsText").GetValue(hud);
                other.TryPurchaseUpgrade("restaurant");
                Assert.That(type.GetProperty("CoinsText").GetValue(hud), Is.EqualTo(before));
                bind.Invoke(hud, new object[] { other });
                AssertLabels(hud, other);
                bind.Invoke(hud, new object[] { null });
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        static void AssertLabels(MonoBehaviour hud, IGameState state)
        {
            var type = hud.GetType();
            var culture = CultureInfo.GetCultureInfo("sk-SK");
            Assert.That(type.GetProperty("CoinsText").GetValue(hud), Is.EqualTo(state.Coins.ToString("N0", culture)));
            Assert.That(type.GetProperty("EnergyText").GetValue(hud), Is.EqualTo(state.Energy + " / " + state.MaxEnergy));
            Assert.That(type.GetProperty("ResortText").GetValue(hud), Is.EqualTo(state.ResortValue.ToString("N0", culture)));
        }

        [Test]
        public void ConfiguredStartingBuildingsMatchTheMergedMainLayout()
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var rules = Resources.Load<GameRules>("GameRules");
                Assert.That(rules, Is.Not.Null, "Inspector-tunable game rules must be included in the player.");
                var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
                foreach (var definition in rules.Configuration.buildings)
                {
                    string name = definition.id == "beach_bar" ? "BeachBar_L1" :
                        char.ToUpperInvariant(definition.id[0]) + definition.id.Substring(1) + "_L1";
                    var building = transforms.Single(item => item.name == name);
                    var visual = building.Find("Visual");
                    Assert.That(visual, Is.Not.Null, name);
                    Assert.That(visual.gameObject.activeSelf, Is.EqualTo(definition.initialStatus == BuildingStatus.Available), name);
                    var construction = building.Find("Construction");
                    if (construction != null)
                        Assert.That(construction.gameObject.activeSelf, Is.EqualTo(definition.initialStatus == BuildingStatus.UnderConstruction), name);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }
    }
}
