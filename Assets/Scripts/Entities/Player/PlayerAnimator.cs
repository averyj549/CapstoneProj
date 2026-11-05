using System.Collections;
using UnityEngine;

public class PlayerAnimator : MonoBehaviour
{
    private Player p;

    [SerializeField] private SpriteRenderer sr;
    private SpriteRenderer[] allRenderers;

    private Animator animator;

    private Coroutine hitRoutine = null;

    private Material baseMaterial;

    private bool heavySwingStarted = false;
    private bool deathRecieved = false;

    public Vector2 SpritePos => sr.transform.position;
    public Sprite CurrentSprite => sr.sprite;
    public bool IsFacingRight { get; private set; }

    // Movement

    private readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private readonly int MovingForwardsHash = Animator.StringToHash("MovingForwards");

    private readonly int IsDashingHash = Animator.StringToHash("IsDashing");
    private readonly int DashingAnimHash = Animator.StringToHash("Dashing");

    private readonly int DeathIdleHash = Animator.StringToHash("DeathIdle");

    //Attacks

    private readonly int AttackSideLightHash_One = Animator.StringToHash("AttackSideLight1");
    private readonly int AttackSideLightHash_Two = Animator.StringToHash("AttackSideLight2");
    private readonly int AttackSideLightHash_Three = Animator.StringToHash("AttackSideLight3");

    private readonly int AttackUpLightHash_One = Animator.StringToHash("AttackUpLight1");
    private readonly int AttackUpLightHash_Two = Animator.StringToHash("AttackUpLight2");
    private readonly int AttackUpLightHash_Three = Animator.StringToHash("AttackUpLight3");

    private readonly int AttackDownLightHash_One = Animator.StringToHash("AttackDownLight1");
    private readonly int AttackDownLightHash_Two = Animator.StringToHash("AttackDownLight2");
    private readonly int AttackDownLightHash_Three = Animator.StringToHash("AttackDownLight3");

    private readonly int AttackHeavyTriggerHash = Animator.StringToHash("DoHeavySwing");
    private readonly int AttackSideHeavyWindupHash = Animator.StringToHash("AttackSideHeavyWindup");
    private readonly int AttackUpHeavyWindupHash = Animator.StringToHash("AttackUpHeavyWindup");
    private readonly int AttackDownHeavyWindupHash = Animator.StringToHash("AttackDownHeavyWindup");


    private void Awake()
    {
        p = GetComponent<Player>();
     
        animator = GetComponentInChildren<Animator>();
        allRenderers = GetComponentsInChildren<SpriteRenderer>();


        baseMaterial = sr.material;

        deathRecieved = false;
    }

    private void OnEnable()
    {
        GameEvents.OnPlayerDashStart += HandleDashStart;
        GameEvents.OnPlayerDashEnd += HandleDashEnd;

        GameEvents.OnAttackStarted += HandleAttackStart;

        GameEvents.OnPlayerTookDamage += HandleHit;

        GameEvents.OnPlayerDeath += HandleDeath;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerDashStart -= HandleDashStart;
        GameEvents.OnPlayerDashEnd -= HandleDashEnd;

        GameEvents.OnAttackStarted -= HandleAttackStart;

        GameEvents.OnPlayerTookDamage -= HandleHit;

        GameEvents.OnPlayerDeath -= HandleDeath;
    }

    private void Update()
    {
        UpdateMovementAnimation();
        UpdateAttackAnimation();
        UpdateAutoFlip();
    }

    #region Flip
    private void UpdateAutoFlip()
    {
        if (TimescaleManager.IsPaused) return;
        if (!p.CanAct || deathRecieved) return;
        if (p.Health.IsHitstunned) return;
        if (p.Combat.CurrentState == CombatState.Startup
            || p.Combat.CurrentState == CombatState.Active) return;

        if (p.Movement.IsDashing)
        {
            if (p.Movement.CurrentMoveDirection.x > 0 && IsFacingRight)
                FlipFacing();
            else if (p.Movement.CurrentMoveDirection.x < 0 && !IsFacingRight)
                FlipFacing();
        }
        else
        {
            Vector2 mouseDir = p.GetMouseDirection();

            if (mouseDir.x > 0 && IsFacingRight)
                FlipFacing();
            else if (mouseDir.x < 0 && !IsFacingRight)
                FlipFacing();
        }
    }

    private void FlipFacing()
    {
        IsFacingRight = !IsFacingRight;

        Vector3 scale = sr.transform.localScale;
        scale.x *= -1;
        sr.transform.localScale = scale;
    }

    #endregion

    #region Animations
    private void UpdateMovementAnimation()
    {
        if (TimescaleManager.IsPaused) return;
        if (!p.CanAct || deathRecieved) return;
        if (p.Movement.IsDashing) return;

        animator.SetBool(IsMovingHash, p.Movement.IsMoving);

        // Facing dir
        bool movingForwards = false;

        Vector2 mouseDir = p.GetMouseDirection();

        if (mouseDir.x > 0 && p.Movement.CurrentMoveDirection.x > 0)
            movingForwards = true;
        else if (mouseDir.x < 0 && p.Movement.CurrentMoveDirection.x < 0)
            movingForwards = true;
        else
            movingForwards = false;

        animator.SetBool(MovingForwardsHash, movingForwards);
    }

    private void HandleAttackStart(AttackType type, Vector2 direction)
    {
        if(TimescaleManager.IsPaused) return;
        if (deathRecieved) return;

        if (direction.x > 0 && IsFacingRight)
            FlipFacing();
        else if (direction.x < 0 && !IsFacingRight)
            FlipFacing();

        int combo = p.Combat.ComboStep;

        AttackDirection animationDir = AttackDirection.Side;
        if (Mathf.Abs(direction.y) > Mathf.Abs(direction.x))
        {
            if (direction.y > 0)
                animationDir = AttackDirection.Up;
            else if(direction.y < 0)
                animationDir = AttackDirection.Down;
        }

        switch (type)
        {
            case AttackType.Light:
                switch (animationDir)
                {
                    case AttackDirection.Side:
                        if (combo <= 1)
                            animator.Play(AttackSideLightHash_One);
                        else if (combo == 2)
                            animator.Play(AttackSideLightHash_Two);
                        else if (combo == 3)
                            animator.Play(AttackSideLightHash_Three);
                        break;

                    case AttackDirection.Up:
                        if (combo <= 1)
                            animator.Play(AttackUpLightHash_One);
                        else if (combo == 2)
                            animator.Play(AttackUpLightHash_Two);
                        else if (combo == 3)
                            animator.Play(AttackUpLightHash_Three);
                        break;

                    case AttackDirection.Down:
                        if (combo <= 1)
                            animator.Play(AttackDownLightHash_One);
                        else if (combo == 2)
                            animator.Play(AttackDownLightHash_Two);
                        else if (combo == 3)
                            animator.Play(AttackDownLightHash_Three);
                        break;
                }
                break;

            case AttackType.Heavy:

                heavySwingStarted = false;
                animator.ResetTrigger(AttackHeavyTriggerHash);

                switch (animationDir)
                {
                    case AttackDirection.Side:
                        animator.Play(AttackSideHeavyWindupHash);
                        break;

                    case AttackDirection.Up:
                        animator.Play(AttackUpHeavyWindupHash);
                        break;

                    case AttackDirection.Down:
                        animator.Play(AttackDownHeavyWindupHash);
                        break;
                }

                break;
        }
    }

    private void UpdateAttackAnimation()
    {
        if (p.Combat.CurrentAttackType == AttackType.Heavy)
        {
            if (p.Combat.CurrentState == CombatState.Active && !heavySwingStarted)
            {
                animator.SetTrigger(AttackHeavyTriggerHash);
                heavySwingStarted = true;
                return;
            }
        }
    }

    private void HandleDashStart()
    {
        animator.SetBool(IsDashingHash, true);
        animator.Play(DashingAnimHash);
    }
    
    private void HandleDashEnd()
    {
        animator.SetBool(IsDashingHash, false);
    }

    #endregion

    #region Masks

    public void SetMaskInteraction(SpriteMaskInteraction mode)
    {
        foreach(var renderer in allRenderers)
            renderer.maskInteraction = mode;
    }

    #endregion


    private void HandleDeath()
    {
        StartCoroutine(HitFeedbackRoutine(1f));
        animator.Play(DeathIdleHash);
        deathRecieved = true;
    }

    private void HandleHit(HitData hit)
    {
        if(hitRoutine != null)
            StopCoroutine(hitRoutine);

        hitRoutine = StartCoroutine(HitFeedbackRoutine(hit.hitstunTime));
    }

    private IEnumerator HitFeedbackRoutine(float duration)
    {
        sr.material = GameManager.Materials.HitMaterial;

        yield return new WaitForSeconds(duration);

        sr.material = baseMaterial;
    }

}
