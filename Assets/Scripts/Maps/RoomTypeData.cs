using UnityEngine;

public enum RoomType
{
    unset,
    Start,
    Combat,
    Elite,
    Story,
    Vault,
    Shop,
    Rest,
    End
}

[CreateAssetMenu(menuName = "Map/NewNodeTypeData")]
public class RoomTypeData : DatabaseEntry
{
    [Header("Display")]
    public string displayName;
    public Sprite displayIcon;
    public bool displayRewardType = true;

    [Header("Behaviour")]
    public bool isStart;
    public bool isEnd;
    public bool isCombat;
    public bool isShop;
    [Tooltip("Always play the intense run track in sectors of this type (elites, final sector).")]
    public bool intenseMusic;

    [Header("Placement")]
    public bool canRandomSpawn = true;
    public float baseWeight = 1f;
    public AnimationCurve weightOverProgress = AnimationCurve.Constant(0f, 1f, 1f);

    [Range(0, 1)] public float minProgress = 0f;
    [Range(0, 1)] public float maxProgress = 0f;

    public int maxPerMap = -1; // unlimited at base
    public int minRowGap = 0;

    public int guaranteedCount;
    [Range(0, 1)] public float guarenteedMinProgress;

    [Header("Encounter")]
    public float encounterBudgetModifier = 1f;
    public int minWaves;
    public int maxWaves;


    [Header("Rewards")]
    public RewardTable rewards;

    public float GetWeight(float progress)
    {
        if (progress < minProgress || progress > maxProgress) return 0f;
        return baseWeight * weightOverProgress.Evaluate(progress);
    }
}
