using System.Globalization;
using ParadiseTycoon.GameState;
using ParadiseTycoon.Scrum104;
using UnityEngine;
using UnityEngine.UI;

namespace ParadiseTycoon.Hud
{
    /// <summary>Event-driven presentation of the shared game state.</summary>
    public sealed class ResortHud : MonoBehaviour
    {
        [SerializeField] TMPro.TMP_Text coinsLabel, energyLabel, resortLabel, statusLabel;
        [SerializeField] Button pauseButton, buildButton, upgradeButton;
        IGameState state;
        PauseMenuController menu;
        HudLegacyPauseShortcut legacyPause;
        public Button PauseButton => pauseButton;
        public Button BuildButton => buildButton;
        public Button UpgradeButton => upgradeButton;
        public string CoinsText => coinsLabel.text;
        public string EnergyText => energyLabel.text;
        public string ResortText => resortLabel.text;
        public string StatusText => statusLabel.text;

        void OnEnable()
        {
            pauseButton.onClick.AddListener(OpenPause);
            buildButton.onClick.AddListener(PreviewBuild);
            upgradeButton.onClick.AddListener(PreviewUpgrade);
            GameSession.StateAvailable += BindState;
            BindState(GameSession.EnsureCreated().State);
        }

        public void BindState(IGameState source)
        {
            if (state != null) state.Changed -= RefreshValues;
            state = source;
            if (state != null) state.Changed += RefreshValues;
            RefreshValues();
        }

        public void RefreshValues()
        {
            if (state == null)
            {
                coinsLabel.text = energyLabel.text = resortLabel.text = "—";
                statusLabel.text = "Uložený postup sa nepodarilo načítať.";
                return;
            }
            var format = CultureInfo.GetCultureInfo("sk-SK");
            coinsLabel.text = state.Coins.ToString("N0", format);
            energyLabel.text = state.Energy + " / " + state.MaxEnergy;
            resortLabel.text = state.ResortValue.ToString("N0", format);
            statusLabel.text = state.Phase == WavePhase.Active
                ? "Prebieha vlna " + state.CurrentWave
                : "Pripravený na vlnu " + state.CurrentWave;
        }

        void LateUpdate()
        {
            if (menu != PauseMenuController.Instance)
            {
                RestoreLegacyButton();
                menu = PauseMenuController.Instance;
                if (menu != null)
                {
                    legacyPause = menu.PauseButton.GetComponent<HudLegacyPauseShortcut>();
                    if (legacyPause == null) legacyPause = menu.PauseButton.gameObject.AddComponent<HudLegacyPauseShortcut>();
                    legacyPause.Bind(this);
                }
            }
            // A standalone HUD remains usable, but cannot pause without the menu prefab.
            pauseButton.interactable = menu != null && menu.isActiveAndEnabled;
        }

        public void OpenPause()
        {
            if (PauseMenuController.Instance != null && PauseMenuController.Instance.isActiveAndEnabled) PauseMenuController.Instance.OpenPauseMenu();
        }
        void PreviewBuild() { statusLabel.text = "Stavanie • ukážka rozhrania"; PlayClick(); }
        void PreviewUpgrade() { statusLabel.text = "Vylepšenia • ukážka rozhrania"; PlayClick(); }
        void PlayClick() { if (PauseMenuController.Instance != null) PauseMenuController.Instance.PlayClick(); }

        void RestoreLegacyButton()
        {
            if (legacyPause != null)
            {
                legacyPause.Release(this);
            }
            legacyPause = null;
        }
        void OnDisable()
        {
            GameSession.StateAvailable -= BindState;
            if (state != null) state.Changed -= RefreshValues;
            state = null;
            pauseButton.onClick.RemoveListener(OpenPause);
            buildButton.onClick.RemoveListener(PreviewBuild);
            upgradeButton.onClick.RemoveListener(PreviewUpgrade);
            RestoreLegacyButton();
            menu = null;
        }
    }
}

