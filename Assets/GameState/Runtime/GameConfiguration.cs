using System;
using System.Collections.Generic;

namespace ParadiseTycoon.GameState
{
    [Serializable]
    public sealed class BuildingDefinition
    {
        public string id;
        public BuildingStatus initialStatus;
        public int initialLevel;
        // Index 0 is owned level 1; upgradeCosts[0] is the price of level 1 -> 2.
        public long[] levelValues;
        public long[] upgradeCosts;
        public BuildingDefinition Copy()
        {
            var copy = (BuildingDefinition)MemberwiseClone();
            copy.levelValues = (long[])levelValues.Clone();
            copy.upgradeCosts = (long[])upgradeCosts.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class GameConfiguration
    {
        public long startingCoins = 1250;
        public int startingEnergy = 80;
        public int maxEnergy = 100;
        public int waveEnergyCost = 10;
        public int managerLevel = 1;
        public string[] unlockedAreas = { "island" };
        public BuildingDefinition[] buildings =
        {
            Definition("hotel", BuildingStatus.Available, 1, 10000, 15000, 22000, 30000),
            Definition("restaurant", BuildingStatus.Available, 1, 8000, 12000, 17000, 23000),
            Definition("beach_bar", BuildingStatus.Available, 1, 6500, 9500, 14000, 20000),
            Definition("bungalow", BuildingStatus.UnderConstruction, 0, 5000, 8000, 12000, 18000),
            Definition("pool", BuildingStatus.UnderConstruction, 0, 6000, 9000, 14000, 20000),
            Definition("spa", BuildingStatus.UnderConstruction, 0, 7000, 11000, 16000, 22000)
        };

        static BuildingDefinition Definition(string id, BuildingStatus status, int level, params long[] values)
            => new BuildingDefinition { id = id, initialStatus = status, initialLevel = level,
                levelValues = values, upgradeCosts = new long[] { 250, 500, 1000 } };

        public GameConfiguration Copy()
        {
            Validate();
            var copy = (GameConfiguration)MemberwiseClone();
            copy.unlockedAreas = (string[])unlockedAreas.Clone();
            copy.buildings = Array.ConvertAll(buildings, item => item.Copy());
            return copy;
        }

        public void Validate()
        {
            if (startingCoins < 0 || maxEnergy < 1 || startingEnergy < 0 || startingEnergy > maxEnergy ||
                waveEnergyCost < 0 || waveEnergyCost > maxEnergy || managerLevel < 1 ||
                buildings == null || buildings.Length == 0 || unlockedAreas == null || unlockedAreas.Length == 0)
                throw new ArgumentException("Invalid initial game configuration.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long maximumValue = 0;
            foreach (var building in buildings)
            {
                if (building == null || string.IsNullOrWhiteSpace(building.id) || !ids.Add(building.id) ||
                    !Enum.IsDefined(typeof(BuildingStatus), building.initialStatus) || building.levelValues == null ||
                    building.levelValues.Length == 0 || building.upgradeCosts == null ||
                    building.upgradeCosts.Length != building.levelValues.Length - 1 ||
                    building.initialLevel < 0 || building.initialLevel > building.levelValues.Length ||
                    (building.initialStatus == BuildingStatus.Available) != (building.initialLevel > 0))
                    throw new ArgumentException("Invalid or duplicate building definition.");
                long previous = 0;
                foreach (long value in building.levelValues)
                {
                    if (value < previous) throw new ArgumentException("Resort values must be non-negative and monotonic.");
                    previous = value;
                }
                foreach (long price in building.upgradeCosts)
                    if (price < 0) throw new ArgumentException("Upgrade prices must be non-negative.");
                maximumValue = checked(maximumValue + previous);
            }
            ids.Clear();
            foreach (var area in unlockedAreas)
                if (string.IsNullOrWhiteSpace(area) || !ids.Add(area)) throw new ArgumentException("Invalid unlocked areas.");
        }
    }
}
