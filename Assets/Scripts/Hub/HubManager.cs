using System;
using System.Linq;
using UnityEngine;

public class HubManager : MonoBehaviour
{
    public static HubManager Instance;

    public GameDatabase db;
    [SerializeField] private MapGenerationConfig config;

    private bool loadingToRun = false;

    // Services
    private MapGenerationService mapGeneration;

    private RunSaveData pendingSave;
    public bool HasSavedRun => pendingSave != null;

    private RunLoadout pendingLoadout;
    private SectorMap pendingMap;
    private int pendingSeed;

    public event Action<int> OnPendingSeedChanged;
    public event Action<SectorMap> OnPendingMapChanged;
    public event Action<RunLoadout> OnPendingLoadoutChanged;
    public event Action<bool> OnSavedRunChanged;

    public int PendingSeed => pendingSeed;
    public SectorMap PendingMap => pendingMap;
    public RunLoadout PendingLoadout => pendingLoadout;
    // A weapon is optional: without one the run starts melee-only until a weapon is found
    public bool CanBeginRun => pendingMap != null && pendingLoadout != null;

    [Header("Music")]
    [SerializeField] private string hubTrack = "MenuMusic01";

    [Header("Fallbacks")]
    [Tooltip("Used only if the profile has no unlocked weapon at all (mark a weapon 'Unlocked By Default' instead).")]
    [SerializeField] private PlayerWeapon defaultWeapon;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        loadingToRun = false;
    }

    public void InitializeHub()
    {
        mapGeneration = new(db.rooms, db.roomTypes, config);

        CheckSavedRun();

        pendingLoadout = BuildStartingLoadout();
        RerollSeed();

        UIManager.Instance.OpenScreen("baseScreen");

        // Same track as the menu, so it carries on seamlessly; crossfades back in after a run
        if (AudioManager.Instance != null) AudioManager.Instance.PlayMusicTrack(hubTrack);
    }

    public void CheckSavedRun()
    {
        var profile = GameManager.Instance.ActiveProfile;
        if (profile == null || !RunSaveSystem.TryLoad(profile, out pendingSave))
        {
            pendingSave = null;
            Debug.Log("No pending save found");
        }

        OnSavedRunChanged?.Invoke(HasSavedRun);
    }

    public void AbandonSavedRun()
    {
        var profile = GameManager.Instance.ActiveProfile;
        if (profile != null) RunSaveSystem.DeleteForSlot(profile.slotIndex);

        pendingSave = null;
        OnSavedRunChanged?.Invoke(false);
    }

    #region Loadout

    public void SetLoadoutWeapon(PlayerWeapon weapon)
    {
        pendingLoadout.weapon = weapon;
        RememberLoadout();
        OnPendingLoadoutChanged?.Invoke(pendingLoadout);
    }

    public void SetLoadoutTool(PlayerTool tool)
    {
        pendingLoadout.tool = tool;
        RememberLoadout();
        OnPendingLoadoutChanged?.Invoke(pendingLoadout);
    }

    public void SetLoadoutModifier(RunModifier modifier)
    {
        pendingLoadout.modifier = modifier;
        RememberLoadout();
        OnPendingLoadoutChanged?.Invoke(pendingLoadout);
    }

    // Restores the profile's last loadout if still unlocked, otherwise the first unlocked weapon
    private RunLoadout BuildStartingLoadout()
    {
        var meta = GameManager.Instance.Meta;
        var last = GameManager.Instance.ActiveProfile?.lastLoadout;

        PlayerWeapon weapon = null;
        PlayerTool tool = null;
        RunModifier modifier = null;

        if (last != null && meta != null)
        {
            if (!string.IsNullOrEmpty(last.weaponID))
            {
                var w = db.weapons.Get(last.weaponID);
                if (w != null && meta.IsUnlocked(w)) weapon = w;
            }

            if (!string.IsNullOrEmpty(last.toolID))
            {
                var t = db.tools.Get(last.toolID);
                if (t != null && meta.IsUnlocked(t)) tool = t;
            }

            if (!string.IsNullOrEmpty(last.modifierID) && db.runModifiers != null)
            {
                var m = db.runModifiers.Get(last.modifierID);
                if (m != null && meta.IsUnlocked(m)) modifier = m;
            }
        }

        if (weapon == null && meta != null)
            weapon = db.weapons.All.FirstOrDefault(w => w != null && meta.IsUnlocked(w));

        if (weapon == null && defaultWeapon != null)
            weapon = defaultWeapon;

        if (DebugManager.Instance != null && DebugManager.GodMode && DebugManager.GodWeapon != null)
            weapon = DebugManager.GodWeapon;

        return new RunLoadout { weapon = weapon, tool = tool, modifier = modifier };
    }

    private void RememberLoadout()
    {
        var profile = GameManager.Instance.ActiveProfile;
        if (profile == null) return;

        profile.lastLoadout ??= new SavedLoadoutPreset();
        profile.lastLoadout.weaponID = pendingLoadout.weapon != null ? pendingLoadout.weapon.Id : null;
        profile.lastLoadout.toolID = pendingLoadout.tool != null ? pendingLoadout.tool.Id : null;
        profile.lastLoadout.modifierID = pendingLoadout.modifier != null ? pendingLoadout.modifier.Id : null;
    }

    #endregion

    #region Run Generation

    public void RerollSeed() => GenerateMap(UnityEngine.Random.Range(1, 100000));

    public void SetCustomSeed(string input) => GenerateMap(ParseSeed(input));

    // Kept for existing button bindings
    public void GenerateNewMap() => RerollSeed();
    public void InitializeSetSeed(string input) => SetCustomSeed(input);

    public void GenerateMap(int seed)
    {
        // Map generation still pulls some values from the global RNG, so seed it first to keep seeds reproducible
        RNGManager.Instance.InitRNG(seed);

        pendingMap = mapGeneration.GenerateWithSeed(seed);
        pendingSeed = pendingMap.seed; // generation may bump the seed on retry

        OnPendingSeedChanged?.Invoke(pendingSeed);
        OnPendingMapChanged?.Invoke(pendingMap);
    }

    // Numbers are used as-is; any other text is hashed (stable across sessions, unlike string.GetHashCode)
    public static int ParseSeed(string input)
    {
        input = input?.Trim();
        if (string.IsNullOrEmpty(input)) return UnityEngine.Random.Range(1, 100000);

        if (int.TryParse(input, out int number))
            return number == int.MinValue ? int.MaxValue : Mathf.Abs(number);

        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in input.ToUpperInvariant())
                hash = (hash ^ c) * 16777619;

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    public void BeginNewRunFromHub()
    {
        if (loadingToRun || !CanBeginRun) return;

        RememberLoadout();
        GameManager.Instance.SaveActiveProfile();

        loadingToRun = true;
        RunDataCarrier.BuildNewRunData(pendingSeed, pendingLoadout, pendingMap);
        SceneLoader.Instance.LoadRun();
    }

    public void BeginSavedRunFromHub()
    {
        Debug.Log("Attempting to load saved run");

        if (loadingToRun) return;
        if (!HasSavedRun) return;

        GameManager.Instance.SaveActiveProfile();
        loadingToRun = true;
        RunDataCarrier.BuildSavedRun(pendingSave);
        SceneLoader.Instance.LoadRun();
    }

    #endregion
}
