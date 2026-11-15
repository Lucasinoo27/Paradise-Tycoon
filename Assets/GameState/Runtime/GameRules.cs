using UnityEngine;

namespace ParadiseTycoon.GameState
{
    [CreateAssetMenu(menuName = "Paradise Tycoon/Game rules", fileName = "GameRules")]
    public sealed class GameRules : ScriptableObject
    {
        [SerializeField] GameConfiguration configuration = new GameConfiguration();
        public GameConfiguration Configuration => configuration.Copy();
    }
}
