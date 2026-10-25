using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

// In-run pause terminal: resume, settings, suspend to hub, or exit. Shows a short status readout of the run.
public class PauseMenuScreen : UIScreen
{
    [Header("Readout")]
    [SerializeField] private TextMeshProUGUI readout;
    [SerializeField] private TextMeshProUGUI headerInfo;
    [Tooltip("Total characters per readout line (label + dots + value).")]
    [SerializeField] private int lineWidth = 30;

    [Header("Screens")]
    [SerializeField] private string settingsScreen = "settingsScreen";
    [SerializeField] private string confirmScreen = "confirmScreen";

    [Header("Prompts")]
    [Tooltip("Keep short: the confirm box fits about 19 characters per line, 2-3 lines.")]
    [SerializeField] private string returnPrompt = "RETURN TO CORE?\nSECTOR WILL RESET";
    [SerializeField] private string exitPrompt = "EXIT GAME?\nSECTOR WILL RESET";

    private bool leaving;

    protected override void OnBeforeOpen(object payload)
    {
        leaving = false;
        RefreshReadout();
    }

    protected override void OnFocusRegained() => RefreshReadout();

    #region Buttons

    public void OnResumeClicked()
    {
        if (leaving) return;
        UIManager.Instance.CloseTop();
    }

    public void OnSettingsClicked()
    {
        if (leaving) return;
        UIManager.Instance.OpenScreen(settingsScreen);
    }

    // The run is saved on entering each sector, so it can be resumed from the hub at the start of this one
    public void OnReturnToHubClicked()
    {
        if (leaving) return;
        Confirm(returnPrompt, "RETURN", () =>
        {
            leaving = true;
            if (RunManager.Instance != null) RunManager.Instance.ReturnToHub();
            else SceneLoader.Instance.LoadHub();
        });
    }

    public void OnQuitClicked()
    {
        if (leaving) return;
        Confirm(exitPrompt, "EXIT", () =>
        {
            leaving = true;
            GameManager.Instance.ExitGame();
        });
    }

    #endregion

    private void Confirm(string prompt, string label, System.Action onConfirm)
    {
        if (UIManager.Instance.OpenScreen(confirmScreen, new ConfirmRequest(prompt, onConfirm, label)) == null)
            onConfirm(); // no confirm screen in this scene
    }

    private void RefreshReadout()
    {
        var run = RunManager.Instance != null ? RunManager.CurrentRun : null;

        if (headerInfo != null)
            headerInfo.text = run?.CurrentNode != null && !string.IsNullOrEmpty(run.CurrentNode.sectorCode)
                ? $"SECTOR {run.CurrentNode.sectorCode.ToUpperInvariant()}"
                : "SIGNAL HELD";

        if (readout == null) return;
        if (run == null) { readout.text = "> NO ACTIVE DELVE"; return; }

        var sb = new StringBuilder();
        void Line(string label, string value) => sb.AppendLine(TerminalFormat.Leader(label, value, lineWidth));

        int maxDepth = Mathf.Max(0, run.map.rowCount - 1);
        var player = run.player;

        Line("DEPTH", $"{run.currentDepth:00}/{maxDepth:00}");
        Line("TIME ELAPSED", TerminalFormat.Duration(run.runDurationTimer));
        Line("HOSTILES DESTROYED", run.EnemiesKilled.ToString());
        Line("FUNDS", TerminalFormat.Currency(run.currency));
        Line("SHARDS BANKED", run.ShardsEarned.ToString());

        if (player != null)
        {
            sb.AppendLine();
            Line("WEAPON", player.Combat.HasWeaponEquipped ? player.Combat.EquippedWeapon.data.Name.ToUpperInvariant() : "NONE");
            Line("PROTOCOL", player.ActiveModifier != null ? player.ActiveModifier.Name.ToUpperInvariant() : "NONE");
            Line("UPGRADES", player.Upgrades.AllActiveUpgrades.Count().ToString());
        }

        readout.text = sb.ToString().TrimEnd();
    }
}
