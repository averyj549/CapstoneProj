using DG.Tweening;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RunServices
{
    public EnemyService enemies;
    public EncounterService encounters;

    public LootService loot;
    public CurrencyDropService currency;
}

public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    public GameDatabase db;
    [SerializeField] private EncounterGenerationConfig encounterConfig;

    [Header("Refs")]

    [SerializeField] private Player playerPrefab;
    [SerializeField] private PlayerUI playerUIPrefab;

    //[SerializeField] private DroneWeaponCarrier dronePrefab;

    [SerializeField] private Transform roomContainer;
    [SerializeField] private Transform enemyContainer;
    [SerializeField] private Transform playerContainer;
    [SerializeField] private Transform playerUIContainer;

    [SerializeField] private Transform currencyContainer;
    [SerializeField] private CurrencyPickup currencyPickupPrefab;

    [SerializeField] private float fadeTime;
    [SerializeField] private CanvasGroup fadeOverlay;

    [SerializeField] private RunEndOverlay runEndOverlay;

    [Header("Run Config")]

    [Header("Music")]
    [SerializeField] private string runTrack = "ActionMusicMedium";
    [SerializeField] private string intenseTrack = "ActionMusicHeavy";
    [Tooltip("Combat sectors at or beyond this fraction of the map's depth use the intense track.")]
    [SerializeField, Range(0f, 1f)] private float intenseFromProgress = 0.6f;
    [SerializeField] private float runEndMusicFade = 2.5f;

    [Header("Shards (meta currency)")]
    [Tooltip("Banked for clearing a combat sector flagged with a shard cache on the map.")]
    [SerializeField] private int shardsPerCache = 10;
    [Tooltip("Paid at the end of the run per depth reached, win or lose.")]
    [SerializeField] private int shardsPerDepth = 5;
    [SerializeField] private int victoryShardBonus = 40;

    private RunServices runServices;

    public RoomPoolService roomService; // Pools rooms, holds useful values etc


    public static RunState CurrentRun => Instance.currentRun;
    public RunState currentRun;

    public Player activePlayer;
    public PlayerUI activePlayerUI;
    public RoomManager activeRoom;

    private bool runEnded;

    private bool transitioning;

    public event Action<Vector2Int> OnPlayerRoomChanged;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        roomService = new RoomPoolService(roomContainer);

        runServices = new RunServices()
        {
            enemies = new EnemyService(db.enemies, enemyContainer),
            encounters = new EncounterService(db.enemies, encounterConfig),

            loot = new LootService(db),
            currency = new CurrencyDropService(currencyPickupPrefab, currencyContainer)
        };

        GameEvents.OnEnemyKilled += runServices.currency.HandleEnemyKilled;
    }

    private void OnEnable()
    {
        GameEvents.OnRunEnded += HandleRunEnd;
        GameEvents.OnEnemyKilled += HandleEnemyKilled;
        GameEvents.OnPlayerTookDamage += HandlePlayerTookDamage;
    }

    private void OnDestroy()
    {
        GameEvents.OnRunEnded -= HandleRunEnd;
        GameEvents.OnEnemyKilled -= HandleEnemyKilled;
        GameEvents.OnPlayerTookDamage -= HandlePlayerTookDamage;
        GameEvents.OnEnemyKilled -= runServices.currency.HandleEnemyKilled;
    }

    private void Update()
    {
        UpdateRunTick();
    }

    #region Initializing Runs

    public void InitializeRun()
    {
        if (RunDataCarrier.IsNewRun)
        {
            BeginNewRun();
            return;
        }
        else
        {
            ResumeSavedRun();
            return;
        }
    }

    public void BeginNewRun()
    {
        RunConfig config = RunDataCarrier.ConsumeNewRunData();
        if (config == null)
        {
            Debug.LogError("[RunManager] No RunConfig exists");
            return;
        }

        SetupPlayer();
        activePlayer.SetupNew(config.loadout);
        activePlayer.SetPlayerCanAct(true);

        currentRun = new(config.map, activePlayer);

        var modifier = config.loadout?.modifier;
        if (modifier != null && modifier.startingCurrency > 0)
            currentRun.GrantCurrency(modifier.startingCurrency);

        EnterNode(currentRun.map.entryNode);
    }

    public void ResumeSavedRun()
    {
        RunSaveData save = RunDataCarrier.ConsumeSavedRunData();
        if (save == null)
        {
            Debug.LogError("[RunManager] No RunSaveData exists");
            return;
        }

        SectorMap map = save?.map.ToMap(db.rooms, db.roomTypes, save.seed, save.chapter);
        MapNode node = (map != null && !string.IsNullOrEmpty(save.currentNodeId))
            ? map.GetNode(save.currentNodeId)
            : null;

        if (node == null)
        {

            Debug.LogError("[RunManager] Saved run is missing or incompatible with current content");
            var p = GameManager.Instance.ActiveProfile;
            if (p != null) RunSaveSystem.DeleteForSlot(p.slotIndex);
            SceneLoader.Instance.LoadHub();
            return;
        }

        SetupPlayer();
        activePlayer.SetupFromSave(save.player);
        activePlayer.SetPlayerCanAct(true);

        currentRun = RunState.FromSave(save, map, activePlayer);

        EnterNode(node, resuming: true);
    }

    private void SetupPlayer()
    {
        activePlayer = Instantiate(playerPrefab, playerContainer);
        AIManager.Instance.Initialize(activePlayer);

        activePlayerUI = Instantiate(playerUIPrefab, playerUIContainer);
        activePlayerUI.Initialize(activePlayer);
    }
    #endregion

    #region Ending Runs

    // Picked once per sector on entry, so the music never flips mid-fight.
    // Intense: sector types flagged intenseMusic (elite, final), or any combat sector deep into the map.
    private void UpdateMusic(MapNode node)
    {
        if (AudioManager.Instance == null || node == null) return;

        int maxDepth = Mathf.Max(1, currentRun.map.rowCount - 1);
        bool deep = node.depth >= Mathf.CeilToInt(maxDepth * intenseFromProgress);
        bool intense = node.type != null && (node.type.intenseMusic || (node.type.isCombat && deep));

        AudioManager.Instance.PlayMusicTrack(intense ? intenseTrack : runTrack);
    }

    private void HandleRunEnd(bool victory)
    {
        if (runEnded) return;
        runEnded = true;
        transitioning = true;

        if (AudioManager.Instance != null) AudioManager.Instance.StopMusic(runEndMusicFade);

        // Update profile based on run results
        var profile = GameManager.Instance.ActiveProfile;
        if (profile != null)
        {
            RunSaveSystem.DeleteForSlot(profile.slotIndex);
            profile.RegisterRunResult(victory, currentRun.runDurationTimer, currentRun.EnemiesKilled, currentRun.currentDepth);

            currentRun.shardsFromPayout = currentRun.currentDepth * shardsPerDepth + (victory ? victoryShardBonus : 0);
            GameManager.Instance.Meta?.GrantCurrency(currentRun.ShardsEarned);

            GameManager.Instance.SaveActiveProfile();
        }

        StartCoroutine(RunEndRoutine(victory));
    }

    private IEnumerator RunEndRoutine(bool victory)
    {
        runServices.currency.ForceCollectAll();

        activePlayerUI.SetUIAlpha(0f);
        activePlayer.SetPlayerCanAct(false);
        activePlayer.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

        if (!victory)
            ScreenGlitch.Instance.SetBaseline(0.1f);

        yield return new WaitForSeconds(1.0f);

        string screenTag = victory
            ? "victoryScreen"
            : "failureScreen";

        UIManager.Instance.OpenScreen(screenTag, currentRun);
    }

    public void ReturnToHub()
    {
        ScreenGlitch.Instance.SetBaseline(0f);
        SceneLoader.Instance.LoadHub();
    }

    #endregion

    #region Room Transitions
    private void EnterNode(MapNode newNode, bool arriving = false, bool resuming = false)
    {
        // Remove Current Room
        runServices.currency.ForceCollectAll();
        ReleaseActiveRoom();

        if (!resuming) currentRun.EnterNode(newNode);

        activeRoom = roomService.Spawn(newNode.room);
        activeRoom.Initialize(newNode, currentRun, runServices);
        activeRoom.OnCleared += HandleRoomCleared;
        activeRoom.OnFailure += HandleRoomFailed;
        activeRoom.OnExitChosen += HandleExitChosen;

        // Setup Player
        //var entryDoor = arriving ? activeRoom.EntryDoor : null;
        Vector2 startPos = activeRoom.EntryPoint;

        GameEvents.PlayerTransitionTeleport(activeRoom.EntryPoint);
        activePlayer.PrepareForRoomChange();
        activePlayer.transform.position = startPos;

        activePlayerUI.ToggleUI(true);

        OnPlayerRoomChanged?.Invoke(newNode.coordinates);
        UpdateMusic(newNode);

        SaveCurrentRun();

        activeRoom.Activate();

        //if (entryDoor != null)
        //    StartCoroutine(ArrivalRoutine(entryDoor));
        //else
        activePlayer.Animator.SetMaskInteraction(SpriteMaskInteraction.None);
    }

    private void ReleaseActiveRoom()
    {
        if(activeRoom == null) return;

        activeRoom.OnCleared -= HandleRoomCleared;
        activeRoom.OnFailure -= HandleRoomFailed;

        activeRoom.OnExitChosen -= HandleExitChosen;

        HUDIndicatorService.Instance.ClearAll();

        roomService.Release(activeRoom);
        activeRoom = null;
    }



    private void HandleRoomCleared(RoomManager room)
    {
        activePlayerUI.ToggleUI(false);

        // Claimed once; the flag is saved with the map, so resuming before leaving re-offers it but the bank resets with it
        var node = currentRun.CurrentNode;
        if (node != null && node.grantShard)
        {
            node.grantShard = false;
            currentRun.EarnSectorShards(shardsPerCache);
        }

        if (currentRun.CurrentNode.type.isEnd) HandleRunEnd(true);
    }

    private void HandleRoomFailed(RoomManager room)
    {
        // Fire Run End Event
    }

    private void HandleExitChosen(Doorway door)
    {
        if (transitioning) return;
        StartCoroutine(TransitionRoutine(door));
    }

    private IEnumerator TransitionRoutine(Doorway door)
    {
        transitioning = true;
        var nextNode = door.Destination;

        try
        {
            DoTransitionFade(true);
            activePlayer.Movement.StartDoorwayMovement(door.exitDirection);
            activePlayer.Animator.SetMaskInteraction(SpriteMaskInteraction.VisibleOutsideMask);


            yield return new WaitForSeconds(fadeTime);

            TimescaleManager.Instance.PauseGame(this);

            activePlayer.Movement.EndDoorwayMovement();
            EnterNode(nextNode);

            TimescaleManager.Instance.UnpauseGame(this);
            DoTransitionFade(false);
        }
        finally
        {
            transitioning = false;
        }
    }

    private IEnumerator ArrivalRoutine(Doorway door)
    {
        door.SetMask(true);
        activePlayer.Animator.SetMaskInteraction(SpriteMaskInteraction.VisibleOutsideMask);

        yield return new WaitForSeconds(0.3f);

        activePlayer.Animator.SetMaskInteraction(SpriteMaskInteraction.None);
    }

    private void DoTransitionFade(bool enable)
    {
        if (fadeOverlay == null) return;

        float fadeValue = enable ? 1f : 0f;

        fadeOverlay?.DOKill();
        fadeOverlay.DOFade(fadeValue, fadeTime).SetUpdate(true);
    }

    #endregion

    private void UpdateRunTick()
    {
        if (TimescaleManager.IsPaused) return;
        if (runEnded) return;
        if (currentRun == null) return;
        if (activeRoom == null) return;

        currentRun.Tick(Time.deltaTime);
    }

    #region Run Counters

    private void HandleEnemyKilled(EnemyController enemy)
    {
        if (currentRun == null || runEnded) return;
        currentRun.EnemiesKilled++;
    }

    private void HandlePlayerTookDamage(HitData hit)
    {
        if (currentRun == null || runEnded) return;
        currentRun.DamageTaken += Mathf.RoundToInt(hit.FinalDamage);
    }

    #endregion

    #region Save System

    private void SaveCurrentRun()
    {
        var profile = GameManager.Instance.ActiveProfile;
        if (profile == null || currentRun == null || runEnded)
        {
            Debug.Log("[RunManager] No Profile or No Run found");
            return;
        }

        if (!RunSaveSystem.Save(currentRun.ToSave(), profile))
            Debug.LogWarning("[RunManager] Run Save Failed");
        else
            Debug.Log("[RunManager] saved run");
    }

    #endregion
}