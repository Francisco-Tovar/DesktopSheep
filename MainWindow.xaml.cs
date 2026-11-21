using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace KawaiiDesktopPets;

public partial class MainWindow : Window
{
    private const double Gravity = 1450.0;
    private const double WalkSpeed = 72.0;
    private const double RunSpeed = 165.0;
    private const double ClimbSpeed = 62.0;
    private const double BounceDamping = 0.35;
    private const double WindowFootOffset = 12.0;
    private const double BasePetSize = 140.0;

    private readonly SheepManager _manager;
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTimeSeconds;

    private SheepState _state = SheepState.Idle;
    private double _stateTimer = 2.0;
    private int _facing = 1;

    private double _velX;
    private double _velY;

    private bool _isDragging;
    private Point _dragOffsetInWindow;
    private Point _prevCursorScreen;
    private double _dragVelX;
    private double _dragVelY;

    private double _blinkTimer = 3.0;
    private double _blinkDuration;

    private string? _bubbleMessage;
    private double _bubbleTimer;

    private DateTime _lastClickTime = DateTime.MinValue;
    private bool _clickThrough;
    private bool _alwaysOnTop = true;
    private bool _gravityEnabled = true;
    private bool _windowClimbingEnabled = true;
    private bool _allowClose;

    private IntPtr _attachedWindowHwnd;
    private Rect _attachedWindowRect;
    private IntPtr _climbingWindowHwnd;
    private bool _climbingLeftSide;
    private bool _climbingMonitorBorder;
    private bool _climbingMonitorLeftSide;
    private Rect _climbTargetWorkArea;
    private double _climbTargetFloorY;
    private double _climbBorderX;
    private double _climbCooldown;
    private double _collisionCooldown;
    private double _topMostRefreshTimer = 1.5;
    private bool _isContextMenuOpen;

    private readonly bool _spawnNearCenter;
    private PetKind _petKind;

    private double _visualScale = 1.0;
    private double _targetScale = 1.0;
    private bool _isBoss;
    private int _bossHp;
    private int _bossMaxHp;
    private double _bossHitFlash;
    private double _bossStompCooldown;
    private double _bossDefeatShrinkTimer;
    private MainWindow? _bossTarget;
    private double _attackCooldown;

    public PetKind PetKind => _petKind;

    public bool CanCollideWithFlock =>
        !_isDragging
        && !_isBoss
        && _bossTarget == null
        && _collisionCooldown <= 0
        && _state is SheepState.Walking or SheepState.Running;

    internal MainWindow(SheepManager manager, PetKind petKind, bool spawnNearCenter)
    {
        _manager = manager;
        _petKind = petKind;
        _spawnNearCenter = spawnNearCenter;
        _gravityEnabled = manager.GravityEnabled;
        _windowClimbingEnabled = manager.WindowClimbingEnabled;
        _alwaysOnTop = manager.AlwaysOnTop;
        _clickThrough = manager.ClickThrough;

        InitializeComponent();
        Sheep.SetPetKind(_petKind);
        Sheep.SetRibbonVariant(_rng.Next(5));
        Topmost = _alwaysOnTop;
    }

    private string VoiceText => _petKind switch
    {
        PetKind.Dog or PetKind.Schnauzer => "Woof!",
        PetKind.Chick => "Cheep!",
        PetKind.Cow => "Moo!",
        PetKind.Toby => "Meow!",
        _ => "Baa!"
    };

    private void PlayPetVoice() => SoundEffects.PlayVoice(_petKind);
    private void PlayPetHappy() => SoundEffects.PlayHappy(_petKind);

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    public void BeginBossMode(int maxHp)
    {
        _bossTarget = null;
        _climbingWindowHwnd = IntPtr.Zero;
        _isBoss = true;
        _bossHp = maxHp;
        _bossMaxHp = maxHp;
        _targetScale = 2.0;
        _bossHitFlash = 0;
        _bossStompCooldown = 2.8;
        _bossDefeatShrinkTimer = 0;
        _state = SheepState.Surprised;
        _stateTimer = 1.5;
        ShowBubble($"MEGA {VoiceText.ToUpperInvariant()} BOSS!!", 2.4);
        SoundEffects.PlayBossRoar();
    }

    public void BeginAttackBoss(MainWindow boss)
    {
        _isBoss = false;
        _targetScale = 1.0;
        _bossTarget = boss;
        _climbingWindowHwnd = IntPtr.Zero;
        _attackCooldown = 0.25 + _rng.NextDouble() * 0.55;
        ShowBubble("CHARGE!!", 1.3);
    }

    public void TakeBossDamage(int damage, int hitDir)
    {
        if (!_isBoss || _bossHp <= 0)
            return;

        _bossHp = Math.Max(0, _bossHp - damage);
        _bossHitFlash = 0.28;
        Left += hitDir * 4.0;

        if (_bossHp <= 0)
        {
            _targetScale = 1.0;
            _bossDefeatShrinkTimer = 1.4;
            _state = SheepState.Surprised;
            _stateTimer = 1.6;
            ShowBubble("DEFEATED!! x_x", 2.2);
            _manager.OnBossDefeated(this);
        }
        else if (_rng.NextDouble() < 0.35)
        {
            ShowBubble(_rng.Next(2) == 0 ? "OUCH!" : "GRRR!!", 0.9);
        }
    }

    public void CelebrateBossVictory()
    {
        _bossTarget = null;
        _state = SheepState.Happy;
        _stateTimer = 2.8;
        ShowBubble("VICTORY!! ★", 2.4);
        PlayPetHappy();
    }

    public void KnockbackFromBoss(int dir)
    {
        if (_isDragging)
            return;
        _velX = dir * 210.0;
        _velY = -260.0;
        _attachedWindowHwnd = IntPtr.Zero;
        _state = SheepState.Falling;
        ShowBubble("WHOA!!", 0.9);
    }

    public void ClearBattleState()
    {
        _isBoss = false;
        _bossHp = 0;
        _bossMaxHp = 0;
        _targetScale = 1.0;
        _bossTarget = null;
    }

    public void SetGravity(bool enabled)
    {
        _gravityEnabled = enabled;
        if (!enabled && _state == SheepState.Falling)
        {
            _velX = 0;
            _velY = 0;
            _state = SheepState.Idle;
            _stateTimer = 2.0;
        }
    }

    public void SetWindowClimbing(bool enabled)
    {
        _windowClimbingEnabled = enabled;
        if (!enabled)
        {
            if (_state == SheepState.Climbing)
            {
                _climbingWindowHwnd = IntPtr.Zero;
                _climbingMonitorBorder = false;
                _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            }
            if (_attachedWindowHwnd != IntPtr.Zero)
            {
                _attachedWindowHwnd = IntPtr.Zero;
                if (_gravityEnabled)
                    _state = SheepState.Falling;
            }
        }
    }

    public void SetAlwaysOnTop(bool enabled)
    {
        _alwaysOnTop = enabled;
        Topmost = enabled;
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if (enabled)
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        else
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    public void CenterOnPrimaryScreen(double offsetX = 0)
    {
        var wa = SystemParameters.WorkArea;
        Left = Math.Clamp(wa.Left + (wa.Width - Width) / 2 + offsetX, wa.Left, wa.Right - Width);
        Top = wa.Top + (wa.Height - Height) / 2;
        _attachedWindowHwnd = IntPtr.Zero;
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = false;
        _velX = 0;
        _velY = 0;
        _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        _stateTimer = 1.5;
    }

    public void WarpTo(double x, double y)
    {
        Left = x;
        Top = y;
        _attachedWindowHwnd = IntPtr.Zero;
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = false;
        _velX = 0;
        _velY = 0;
        _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        _stateTimer = 1.5;
    }

    public void ReactToFlockBump(int pushDirection)
    {
        _collisionCooldown = 2.2;
        _facing = pushDirection;
        _state = SheepState.Surprised;
        _stateTimer = 0.65;
        ShowBubble(_rng.Next(2) == 0 ? VoiceText : "!", 1.0);
        PlayPetVoice();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyToolWindowStyle();
        if (_clickThrough)
            SetClickThrough(true);

        var workArea = SystemParameters.WorkArea;
        if (_spawnNearCenter)
        {
            Left = workArea.Left + (workArea.Width - Width) * (0.25 + _rng.NextDouble() * 0.5);
            Top = workArea.Top + 40;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        }
        else
        {
            Left = workArea.Right - Width - 80;
            Top = workArea.Bottom - Height;
        }

        _facing = _rng.Next(2) == 0 ? 1 : -1;
        _lastTimeSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRenderFrame;
    }

    private void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    private void ReassertTopMost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTimeSeconds, 0.0, 0.05);
        _lastTimeSeconds = now;

        if (_climbCooldown > 0) _climbCooldown -= dt;
        if (_collisionCooldown > 0) _collisionCooldown -= dt;
        if (_bossHitFlash > 0) _bossHitFlash -= dt;
        if (_attackCooldown > 0) _attackCooldown -= dt;
        if (_bossStompCooldown > 0) _bossStompCooldown -= dt;
        if (_bossDefeatShrinkTimer > 0)
        {
            _bossDefeatShrinkTimer -= dt;
            if (_bossDefeatShrinkTimer <= 0 && _bossHp <= 0)
                _isBoss = false;
        }

        UpdateScale(dt);

        if (_alwaysOnTop && !_isContextMenuOpen)
        {
            _topMostRefreshTimer -= dt;
            if (_topMostRefreshTimer <= 0)
            {
                _topMostRefreshTimer = 1.5;
                ReassertTopMost();
            }
        }

        UpdateBlink(dt);
        UpdateBubble(dt);

        if (!_isDragging)
        {
            UpdateBehavior(dt);
            UpdatePhysics(dt);
            _manager.CheckFlockCollision(this);
        }

        bool isBlinking = _blinkDuration > 0;
        Sheep.UpdateVisuals(_state, now, _facing, isBlinking, _bubbleMessage);
        Sheep.SetBossVisual(_isBoss, _bossHp, _bossMaxHp, _bossHitFlash);
    }

    private void UpdateScale(double dt)
    {
        if (Math.Abs(_visualScale - _targetScale) < 0.002)
        {
            _visualScale = _targetScale;
        }
        else
        {
            double step = 1.4 * dt;
            _visualScale = _visualScale < _targetScale
                ? Math.Min(_targetScale, _visualScale + step)
                : Math.Max(_targetScale, _visualScale - step);
        }

        double targetSize = BasePetSize * _visualScale;
        if (Math.Abs(targetSize - Width) > 0.25)
        {
            double oldW = Width;
            double oldH = Height;
            Width = targetSize;
            Height = targetSize;
            Left -= (targetSize - oldW) / 2.0;
            Top -= (targetSize - oldH);
        }
    }

    private void UpdateBlink(double dt)
    {
        if (_blinkDuration > 0)
        {
            _blinkDuration -= dt;
            return;
        }

        _blinkTimer -= dt;
        if (_blinkTimer <= 0)
        {
            _blinkDuration = 0.14;
            _blinkTimer = 2.5 + _rng.NextDouble() * 4.0;
        }
    }

    private void UpdateBubble(double dt)
    {
        if (_bubbleTimer > 0)
        {
            _bubbleTimer -= dt;
            if (_bubbleTimer <= 0)
                _bubbleMessage = null;
        }
    }

    private void ShowBubble(string message, double durationSeconds)
    {
        _bubbleMessage = message;
        _bubbleTimer = durationSeconds;
    }

    private void UpdateBehavior(double dt)
    {
        if (_state is SheepState.Falling or SheepState.Climbing)
            return;

        if (_isBoss && _bossHp > 0)
        {
            UpdateBossBehavior(dt);
            return;
        }

        if (_bossTarget != null)
        {
            if (!_bossTarget._isBoss || _bossTarget._bossHp <= 0)
            {
                _bossTarget = null;
            }
            else
            {
                UpdateAttackerBehavior(dt);
                return;
            }
        }

        _stateTimer -= dt;
        if (_stateTimer > 0)
            return;

        if (_state == SheepState.Abducted)
        {
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            _stateTimer = 1.5;
            ShowBubble($"{VoiceText.TrimEnd('!')}?!", 1.5);
            return;
        }

        if (_gravityEnabled && _attachedWindowHwnd == IntPtr.Zero && _climbCooldown <= 0)
        {
            GetDpiScale(out double dpiX, out double dpiY);
            GetCurrentScreenAreas(dpiX, dpiY, out _, out Rect fullBounds);
            if (TryFindMonitorBelow(fullBounds, Left + Width / 2.0, dpiX, dpiY) && _rng.NextDouble() < 0.16)
            {
                Top = fullBounds.Bottom + 4;
                _velX = _facing * 40.0;
                _velY = 0;
                _climbCooldown = 3.5;
                _state = SheepState.Falling;
                ShowBubble("Wheee!", 1.0);
                return;
            }
        }

        switch (_manager.ActivityMix.RollActivity(_rng))
        {
            case PetActivity.Walking:
                _state = SheepState.Walking;
                _facing = _rng.Next(2) == 0 ? 1 : -1;
                _stateTimer = 2.0 + _rng.NextDouble() * 4.0;
                break;

            case PetActivity.Dashing:
                _state = SheepState.Running;
                _facing = _rng.Next(2) == 0 ? 1 : -1;
                _stateTimer = 1.4 + _rng.NextDouble() * 2.2;
                if (_rng.NextDouble() < 0.35)
                    ShowBubble("Zoom!", 1.0);
                break;

            case PetActivity.Idle:
                if (_rng.NextDouble() < 0.70)
                {
                    _state = SheepState.Idle;
                    _stateTimer = 1.5 + _rng.NextDouble() * 2.5;
                    if (_rng.NextDouble() < 0.25)
                    {
                        ShowBubble(VoiceText, 1.4);
                        PlayPetVoice();
                    }
                }
                else
                {
                    _state = SheepState.Sitting;
                    _stateTimer = 2.5 + _rng.NextDouble() * 3.0;
                }
                break;

            case PetActivity.Eating:
                _state = SheepState.Eating;
                _stateTimer = 2.2 + _rng.NextDouble() * 2.5;
                ShowBubble("Nom...", 1.5);
                SoundEffects.PlayNom();
                break;

            case PetActivity.Sleeping:
                if (_rng.NextDouble() < 0.75)
                {
                    _state = SheepState.Sleeping;
                    _stateTimer = 4.0 + _rng.NextDouble() * 4.0;
                    ShowBubble("Zzz...", 2.5);
                }
                else
                {
                    _state = SheepState.Yawning;
                    _stateTimer = 1.5;
                    ShowBubble("*Yawn*", 1.4);
                    SoundEffects.PlayYawn();
                }
                break;

            case PetActivity.Ufo:
                TriggerUfoAbduction();
                break;
        }
    }

    private void UpdateBossBehavior(double dt)
    {
        _stateTimer -= dt;
        if (_bossStompCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero)
        {
            _bossStompCooldown = 3.4 + _rng.NextDouble() * 1.6;
            _velY = -390.0;
            _state = SheepState.Falling;
            ShowBubble("STOMP!!", 1.1);
            SoundEffects.PlayBossRoar();
            _manager.ApplyBossStomp(this);
            return;
        }

        if (_stateTimer <= 0)
        {
            double? targetX = _manager.GetNearestAttackerX(this);
            if (targetX.HasValue)
                _facing = targetX.Value >= Left + Width / 2.0 ? 1 : -1;
            else
                _facing = _rng.Next(2) == 0 ? 1 : -1;

            _state = _rng.NextDouble() < 0.45 ? SheepState.Running : SheepState.Walking;
            _stateTimer = 1.0 + _rng.NextDouble() * 1.4;
        }
    }

    private void UpdateAttackerBehavior(double dt)
    {
        var boss = _bossTarget!;
        double myCenterX = Left + Width / 2.0;
        double myBottom = Top + Height;
        double bossCenterX = boss.Left + boss.Width / 2.0;
        double bossBottom = boss.Top + boss.Height;

        double dx = bossCenterX - myCenterX;
        double dy = bossBottom - myBottom;

        if (Math.Abs(dy) > 45 && _attachedWindowHwnd != IntPtr.Zero)
        {
            _attachedWindowHwnd = IntPtr.Zero;
            if (_gravityEnabled)
                _state = SheepState.Falling;
            return;
        }

        _facing = dx >= 0 ? 1 : -1;
        if (Math.Abs(dx) > 95)
        {
            _state = SheepState.Running;
            _stateTimer = 0.3;
            return;
        }

        _state = SheepState.Idle;
        _stateTimer = 0.2;
        if (_attackCooldown <= 0 && Math.Abs(dy) < 60)
        {
            _attackCooldown = 0.65 + _rng.NextDouble() * 0.55;
            _velY = -330.0;
            _velX = _facing * 95.0;
            _state = SheepState.Falling;

            int dmg = _rng.NextDouble() < 0.22 ? 2 : 1;
            boss.TakeBossDamage(dmg, _facing);
            SoundEffects.PlayBossHit();
            ShowBubble(dmg > 1 ? "CRIT!!" : (_rng.Next(2) == 0 ? "BONK!" : "POW!"), 0.85);
        }
    }

    private void UpdatePhysics(double dt)
    {
        GetDpiScale(out double dpiX, out double dpiY);
        GetCurrentScreenAreas(dpiX, dpiY, out Rect bounds, out Rect fullBounds);
        double floorY = bounds.Bottom - Height;
        double minX = bounds.Left;
        double maxX = bounds.Right - Width;

        var windows = _windowClimbingEnabled
            ? _manager.WindowTracker.GetWindows(dpiX, dpiY)
            : Array.Empty<TrackedWindow>();

        if (_state == SheepState.Abducted)
        {
            Top = Math.Max(bounds.Top + 10, Top - 26.0 * dt);
            return;
        }

        if (_state == SheepState.Climbing)
        {
            UpdateClimbing(dt, dpiX, dpiY, bounds, windows);
            return;
        }

        // Keep riding the attached window if we're standing on one
        if (_attachedWindowHwnd != IntPtr.Zero)
        {
            var attached = _manager.WindowTracker.FindWindow(_attachedWindowHwnd, dpiX, dpiY);
            double centerX = Left + Width / 2.0;

            if (attached == null
                || attached.Value.Bounds.Top < bounds.Top + 18
                || _manager.WindowTracker.IsPointOccluded(centerX, attached.Value.Bounds.Top + 4, attached.Value.ZOrder, windows))
            {
                _attachedWindowHwnd = IntPtr.Zero;
                if (_gravityEnabled)
                    _state = SheepState.Falling;
            }
            else
            {
                var winRect = attached.Value.Bounds;
                double dx = winRect.Left - _attachedWindowRect.Left;
                double dy = winRect.Top - _attachedWindowRect.Top;
                Left += dx;
                Top += dy;
                _attachedWindowRect = winRect;

                // Check if sheep walked off the left or right edge of the window
                if (centerX < winRect.Left + 8 || centerX > winRect.Right - 8)
                {
                    if (_rng.NextDouble() < 0.65)
                    {
                        _attachedWindowHwnd = IntPtr.Zero;
                        if (_gravityEnabled)
                        {
                            _state = SheepState.Falling;
                            _velX = _facing * 45.0;
                            ShowBubble("Whoops!", 1.0);
                        }
                    }
                    else
                    {
                        Left = Math.Clamp(Left, winRect.Left + 10 - Width / 2.0, winRect.Right - 10 - Width / 2.0);
                        _facing = -_facing;
                    }
                }
            }
        }

        if (_state is SheepState.Walking or SheepState.Running)
        {
            double speed = _state == SheepState.Running
                ? (_bossTarget != null ? 215.0 : RunSpeed)
                : WalkSpeed;
            Left += _facing * speed * dt;

            if ((Left <= minX && _facing < 0) || (Left >= maxX && _facing > 0))
            {
                bool isLeftEdge = _facing < 0;
                if (TryFindAdjacentScreen(fullBounds, _facing, Top + Height - 12, dpiX, dpiY, out Rect adjWork, out _))
                {
                    if (adjWork.Bottom < bounds.Bottom - 36)
                    {
                        if (_climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero && (_state == SheepState.Running || _rng.NextDouble() < 0.65))
                        {
                            StartMonitorBorderClimb(isLeftEdge, adjWork, isLeftEdge ? minX : maxX);
                            return;
                        }
                        Left = isLeftEdge ? minX : maxX;
                        _facing = isLeftEdge ? 1 : -1;
                    }
                    // Otherwise allow walking straight across onto the adjacent monitor.
                }
                else if (_climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero
                    && TryFindMonitorAbove(fullBounds, isLeftEdge, dpiX, dpiY, out Rect aboveWork, out double climbX)
                    && (_state == SheepState.Running || _rng.NextDouble() < 0.65))
                {
                    StartMonitorBorderClimb(isLeftEdge, aboveWork, climbX);
                    return;
                }
                else if (_gravityEnabled && _climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero
                    && TryFindMonitorBelow(fullBounds, Left + Width / 2.0, dpiX, dpiY)
                    && _rng.NextDouble() < 0.55)
                {
                    Top = fullBounds.Bottom + 4;
                    _velX = _facing * 45.0;
                    _velY = 0;
                    _climbCooldown = 3.5;
                    _state = SheepState.Falling;
                    ShowBubble("Wheee!", 1.0);
                    return;
                }
                else
                {
                    Left = isLeftEdge ? minX : maxX;
                    _facing = isLeftEdge ? 1 : -1;
                }
            }

            // Check if we bumped into a climbable window edge
            if (_windowClimbingEnabled && _climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero)
            {
                double sheepCenterX = Left + Width / 2.0;
                double sheepFeetY = Top + Height;
                var climbTarget = _manager.WindowTracker.FindClimbableWindow(
                    sheepCenterX, sheepFeetY, _facing, bounds.Top, windows, out bool climbLeftSide);

                if (climbTarget != null && (_state == SheepState.Running || _rng.NextDouble() < 0.55))
                {
                    _climbingWindowHwnd = climbTarget.Value.Hwnd;
                    _climbingLeftSide = climbLeftSide;
                    _facing = climbLeftSide ? 1 : -1;
                    _state = SheepState.Climbing;
                    _velX = 0;
                    _velY = 0;
                    return;
                }
                else if (climbTarget != null)
                {
                    _climbCooldown = 2.0;
                }
            }
        }

        bool aboveFloor = Top < floorY - 1 && _attachedWindowHwnd == IntPtr.Zero;
        if (_gravityEnabled && (_state == SheepState.Falling || aboveFloor))
        {
            _state = SheepState.Falling;
            double footOffset = WindowFootOffset * _visualScale;
            double prevFeetY = Top + Height - footOffset;

            _velY += Gravity * dt;
            Left += _velX * dt;
            Top += _velY * dt;

            double newFeetY = Top + Height - footOffset;
            double centerX = Left + Width / 2.0;

            if (Left <= minX && _velX <= 0
                && !TryFindAdjacentScreen(fullBounds, -1, Top + Height / 2.0, dpiX, dpiY, out _, out _))
            {
                Left = minX;
                _velX = Math.Abs(_velX) * 0.5;
                _facing = 1;
            }
            else if (Left >= maxX && _velX >= 0
                && !TryFindAdjacentScreen(fullBounds, 1, Top + Height / 2.0, dpiX, dpiY, out _, out _))
            {
                Left = maxX;
                _velX = -Math.Abs(_velX) * 0.5;
                _facing = -1;
            }

            // Try landing on top of a visible window
            if (_windowClimbingEnabled && _velY >= 0)
            {
                var landedWin = _manager.WindowTracker.FindLandingWindow(
                    centerX, prevFeetY, newFeetY, bounds.Top, windows);

                if (landedWin != null)
                {
                    Top = landedWin.Value.Bounds.Top - Height + WindowFootOffset * _visualScale;
                    _attachedWindowHwnd = landedWin.Value.Hwnd;
                    _attachedWindowRect = landedWin.Value.Bounds;

                    if (Math.Abs(_velY) > 320)
                        SoundEffects.PlayBoing();

                    _velX = 0;
                    _velY = 0;
                    _state = SheepState.Idle;
                    _stateTimer = 1.4;
                    return;
                }
            }

            if (Top >= floorY)
            {
                Top = floorY;
                _attachedWindowHwnd = IntPtr.Zero;
                if (Math.Abs(_velY) > 180)
                {
                    _velY = -_velY * BounceDamping;
                    _velX *= 0.6;
                    SoundEffects.PlayBoing();
                }
                else
                {
                    _velX = 0;
                    _velY = 0;
                    _state = SheepState.Idle;
                    _stateTimer = 1.5;
                }
            }
        }
    }

    private void StartMonitorBorderClimb(bool leftSide, Rect targetWorkArea, double climbX)
    {
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = true;
        _climbingMonitorLeftSide = leftSide;
        _climbTargetWorkArea = targetWorkArea;
        _climbTargetFloorY = targetWorkArea.Bottom - Height;
        _climbBorderX = climbX;
        _facing = leftSide ? 1 : -1;
        _state = SheepState.Climbing;
        _velX = 0;
        _velY = 0;
    }

    private void UpdateClimbing(double dt, double dpiX, double dpiY, Rect bounds, IReadOnlyList<TrackedWindow> windows)
    {
        if (_climbingMonitorBorder)
        {
            Left = _climbBorderX;
            Top -= ClimbSpeed * dt;

            if (Top <= _climbTargetFloorY)
            {
                Top = _climbTargetFloorY;
                double stepIn = _climbingMonitorLeftSide ? 24 : -24;
                Left = Math.Clamp(_climbBorderX + stepIn, _climbTargetWorkArea.Left, _climbTargetWorkArea.Right - Width);
                _climbingMonitorBorder = false;
                _climbCooldown = 3.5;
                _state = SheepState.Walking;
                _stateTimer = 2.5;
                ShowBubble("Made it!", 1.2);
            }
            return;
        }

        var target = _manager.WindowTracker.FindWindow(_climbingWindowHwnd, dpiX, dpiY);
        if (target == null)
        {
            _climbingWindowHwnd = IntPtr.Zero;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            return;
        }

        var winRect = target.Value.Bounds;
        double edgeX = _climbingLeftSide ? winRect.Left : winRect.Right;
        if (_manager.WindowTracker.IsPointOccluded(edgeX + (_climbingLeftSide ? 4 : -4), Top + Height / 2.0, target.Value.ZOrder, windows))
        {
            _climbingWindowHwnd = IntPtr.Zero;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            return;
        }

        Left = _climbingLeftSide
            ? winRect.Left - Width / 2.0 - 8
            : winRect.Right - Width / 2.0 + 8;

        Top -= ClimbSpeed * dt;

        double targetTop = winRect.Top - Height + WindowFootOffset * _visualScale;
        if (Top <= targetTop)
        {
            Top = targetTop;
            Left = _climbingLeftSide
                ? winRect.Left + 16 - Width / 2.0
                : winRect.Right - 16 - Width / 2.0;
            _climbingWindowHwnd = IntPtr.Zero;
            _attachedWindowHwnd = target.Value.Hwnd;
            _attachedWindowRect = winRect;
            _climbCooldown = 3.0;
            _state = SheepState.Walking;
            _stateTimer = 2.5;
            ShowBubble("Made it!", 1.2);
        }
    }

    private void GetDpiScale(out double dpiX, out double dpiY)
    {
        var source = PresentationSource.FromVisual(this);
        dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
    }

    private Rect GetCurrentScreenWorkArea(double dpiX, double dpiY)
    {
        GetCurrentScreenAreas(dpiX, dpiY, out Rect workArea, out _);
        return workArea;
    }

    private void GetCurrentScreenAreas(double dpiX, double dpiY, out Rect workArea, out Rect fullBounds)
    {
        var centerPoint = new Drawing.Point((int)((Left + Width / 2) / dpiX), (int)((Top + Height / 2) / dpiY));
        var screen = Forms.Screen.FromPoint(centerPoint);
        var wa = screen.WorkingArea;
        var fb = screen.Bounds;
        workArea = new Rect(wa.Left * dpiX, wa.Top * dpiY, wa.Width * dpiX, wa.Height * dpiY);
        fullBounds = new Rect(fb.Left * dpiX, fb.Top * dpiY, fb.Width * dpiX, fb.Height * dpiY);
    }

    private static List<(Rect WorkArea, Rect FullBounds)> GetAllScreenAreas(double dpiX, double dpiY)
    {
        var list = new List<(Rect, Rect)>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var wa = screen.WorkingArea;
            var fb = screen.Bounds;
            list.Add((
                new Rect(wa.Left * dpiX, wa.Top * dpiY, wa.Width * dpiX, wa.Height * dpiY),
                new Rect(fb.Left * dpiX, fb.Top * dpiY, fb.Width * dpiX, fb.Height * dpiY)
            ));
        }
        return list;
    }

    private static bool TryFindAdjacentScreen(
        Rect currentBounds,
        int dir,
        double probeY,
        double dpiX,
        double dpiY,
        out Rect targetWorkArea,
        out Rect targetBounds)
    {
        const double edgeTol = 48.0;
        foreach (var (wa, fb) in GetAllScreenAreas(dpiX, dpiY))
        {
            if (fb == currentBounds)
                continue;

            bool touchesSide = dir < 0
                ? Math.Abs(fb.Right - currentBounds.Left) <= edgeTol
                : Math.Abs(fb.Left - currentBounds.Right) <= edgeTol;

            if (touchesSide && probeY >= fb.Top - 24 && probeY <= fb.Bottom + 24)
            {
                targetWorkArea = wa;
                targetBounds = fb;
                return true;
            }
        }

        targetWorkArea = default;
        targetBounds = default;
        return false;
    }

    private bool TryFindMonitorAbove(
        Rect currentBounds,
        bool leftSide,
        double dpiX,
        double dpiY,
        out Rect targetWorkArea,
        out double climbX)
    {
        const double edgeTol = 48.0;
        double desiredX = leftSide ? currentBounds.Left : currentBounds.Right - Width;

        foreach (var (wa, fb) in GetAllScreenAreas(dpiX, dpiY))
        {
            if (fb == currentBounds)
                continue;

            if (fb.Bottom <= currentBounds.Top + edgeTol)
            {
                double overlapLeft = Math.Max(fb.Left, currentBounds.Left);
                double overlapRight = Math.Min(fb.Right, currentBounds.Right);
                if (overlapRight - overlapLeft >= Width)
                {
                    targetWorkArea = wa;
                    climbX = Math.Clamp(desiredX, wa.Left, wa.Right - Width);
                    return true;
                }
            }
        }

        targetWorkArea = default;
        climbX = 0;
        return false;
    }

    private static bool TryFindMonitorBelow(Rect currentBounds, double centerX, double dpiX, double dpiY)
    {
        const double edgeTol = 48.0;
        foreach (var (_, fb) in GetAllScreenAreas(dpiX, dpiY))
        {
            if (fb == currentBounds)
                continue;

            if (fb.Top >= currentBounds.Bottom - edgeTol && centerX >= fb.Left && centerX <= fb.Right)
                return true;
        }
        return false;
    }

    private Point GetCursorPositionInDip()
    {
        var pos = Forms.Control.MousePosition;
        GetDpiScale(out double dpiX, out double dpiY);
        return new Point(pos.X * dpiX, pos.Y * dpiY);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastClickTime).TotalMilliseconds < 320)
        {
            _lastClickTime = DateTime.MinValue;
            TriggerPetReaction();
            return;
        }
        _lastClickTime = now;

        _isDragging = true;
        _attachedWindowHwnd = IntPtr.Zero;
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = false;
        _state = SheepState.Dragged;
        _velX = 0;
        _velY = 0;
        _dragVelX = 0;
        _dragVelY = 0;

        _dragOffsetInWindow = e.GetPosition(this);
        _prevCursorScreen = GetCursorPositionInDip();
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        Point cursor = GetCursorPositionInDip();
        double dx = cursor.X - _prevCursorScreen.X;
        double dy = cursor.Y - _prevCursorScreen.Y;

        if (Math.Abs(dx) > 1.5)
            _facing = dx >= 0 ? 1 : -1;

        _dragVelX = dx * 30.0;
        _dragVelY = dy * 30.0;
        _prevCursorScreen = cursor;

        Left = cursor.X - _dragOffsetInWindow.X;
        Top = cursor.Y - _dragOffsetInWindow.Y;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        ReleaseMouseCapture();

        _velX = Math.Clamp(_dragVelX, -900, 900);
        _velY = Math.Clamp(_dragVelY, -900, 900);

        if (_gravityEnabled)
        {
            _state = SheepState.Falling;
        }
        else
        {
            _velX = 0;
            _velY = 0;
            _state = SheepState.Idle;
            _stateTimer = 2.0;
        }
    }

    private void Window_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _velX = 0;
        _velY = 0;
        _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        _stateTimer = 1.5;
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            ShowBubble("Ooh?", 1.0);
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (_bubbleMessage == "Ooh?")
            _bubbleMessage = null;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            string file = files[0];
            _state = SheepState.Eating;
            _stateTimer = 2.6;

            if (string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                ShowBubble("♪ Nom!", 2.2);
                SoundEffects.PlayWavFile(file);
            }
            else
            {
                ShowBubble("Nom nom!", 2.0);
                SoundEffects.PlayNom();
            }
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this
        };
        _isContextMenuOpen = true;
        menu.Closed += (_, _) => _isContextMenuOpen = false;

        var petItem = new MenuItem
        {
            Header = _petKind switch
            {
                PetKind.Dog or PetKind.Schnauzer => "Pet Dog (Woof!)",
                PetKind.Chick => "Pet Chick (Cheep!)",
                PetKind.Cow => "Pet Cow (Moo!)",
                PetKind.Toby => "Pet Toby (Meow!)",
                _ => "Pet Sheep (Baa!)"
            }
        };
        petItem.Click += (_, _) => TriggerPetReaction();

        var walkItem = new MenuItem { Header = "Walk Around" };
        walkItem.Click += (_, _) =>
        {
            _state = SheepState.Walking;
            _stateTimer = 4.0;
        };

        var runItem = new MenuItem { Header = "Zoomies (Run!)" };
        runItem.Click += (_, _) =>
        {
            _state = SheepState.Running;
            _stateTimer = 2.5;
            ShowBubble("Zoom!", 1.0);
        };

        var eatItem = new MenuItem { Header = "Eat Grass" };
        eatItem.Click += (_, _) =>
        {
            _state = SheepState.Eating;
            _stateTimer = 3.0;
            ShowBubble("Nom...", 1.5);
            SoundEffects.PlayNom();
        };

        var sleepItem = new MenuItem { Header = "Take a Nap" };
        sleepItem.Click += (_, _) =>
        {
            _state = SheepState.Sleeping;
            _stateTimer = 6.0;
            ShowBubble("Zzz...", 3.0);
        };

        var ufoItem = new MenuItem { Header = "Summon UFO" };
        ufoItem.Click += (_, _) => TriggerUfoAbduction();

        var addSheepItem = new MenuItem
        {
            Header = $"Add Sheep ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addSheepItem.Click += (_, _) => _manager.AddSheep(true);

        var addDogItem = new MenuItem
        {
            Header = $"Add Dachshund ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addDogItem.Click += (_, _) => _manager.AddDog(true);

        var addSchnauzerItem = new MenuItem
        {
            Header = $"Add Schnauzer ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addSchnauzerItem.Click += (_, _) => _manager.AddSchnauzer(true);

        var addChickItem = new MenuItem
        {
            Header = $"Add Chick ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addChickItem.Click += (_, _) => _manager.AddChick(true);

        var addCowItem = new MenuItem
        {
            Header = $"Add Cow ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addCowItem.Click += (_, _) => _manager.AddCow(true);

        var addTobyItem = new MenuItem
        {
            Header = $"Add Toby (Cat) ({_manager.Count}/{SheepManager.MaxSheep})",
            IsEnabled = _manager.Count < SheepManager.MaxSheep
        };
        addTobyItem.Click += (_, _) => _manager.AddToby(true);

        var switchMenu = new MenuItem { Header = "Switch Pet" };
        void AddSwitchOption(string label, PetKind kind)
        {
            var item = new MenuItem { Header = label, IsChecked = _petKind == kind };
            item.Click += (_, _) =>
            {
                _petKind = kind;
                Sheep.SetPetKind(kind);
                ShowBubble(VoiceText, 1.4);
                PlayPetVoice();
            };
            switchMenu.Items.Add(item);
        }
        AddSwitchOption("Sheep", PetKind.Sheep);
        AddSwitchOption("Dachshund", PetKind.Dog);
        AddSwitchOption("Schnauzer", PetKind.Schnauzer);
        AddSwitchOption("Chick", PetKind.Chick);
        AddSwitchOption("Cow", PetKind.Cow);
        AddSwitchOption("Toby (Cat)", PetKind.Toby);

        var removeSheepItem = new MenuItem
        {
            Header = "Remove This Pet",
            IsEnabled = _manager.Count > 1
        };
        removeSheepItem.Click += (_, _) => _manager.RemoveSheep(this);

        var gravityItem = new MenuItem
        {
            Header = "Gravity",
            IsCheckable = true,
            IsChecked = _gravityEnabled
        };
        gravityItem.Click += (_, _) => _manager.SetGravity(gravityItem.IsChecked);

        var climbItem = new MenuItem
        {
            Header = "Window Climbing & Landing",
            IsCheckable = true,
            IsChecked = _windowClimbingEnabled
        };
        climbItem.Click += (_, _) => _manager.SetWindowClimbing(climbItem.IsChecked);

        var soundMenu = new MenuItem { Header = "Sound" };

        var soundItem = new MenuItem
        {
            Header = "Sound Effects",
            IsCheckable = true,
            IsChecked = _manager.SoundEnabled
        };
        soundItem.Click += (_, _) => _manager.SetSoundEnabled(soundItem.IsChecked);

        var volumeLabel = new TextBlock
        {
            Text = $"{(int)Math.Round(_manager.SoundVolume * 100)}%",
            Width = 36,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };

        var volumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 110,
            Value = Math.Round(_manager.SoundVolume * 100),
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true,
            TickFrequency = 5
        };
        volumeSlider.ValueChanged += (_, args) =>
        {
            double vol = args.NewValue / 100.0;
            _manager.SetSoundVolume(vol);
            volumeLabel.Text = $"{(int)Math.Round(args.NewValue)}%";
        };

        var volumePanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        volumePanel.Children.Add(new TextBlock
        {
            Text = "Volume",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        volumePanel.Children.Add(volumeSlider);
        volumePanel.Children.Add(volumeLabel);

        var volumeItem = new MenuItem
        {
            Header = volumePanel,
            StaysOpenOnClick = true
        };

        soundMenu.Items.Add(soundItem);
        soundMenu.Items.Add(new Separator());
        soundMenu.Items.Add(volumeItem);

        var activityMenu = new MenuItem { Header = "Activity Mix (100%)" };
        var activitySliders = new Slider[ActivityMix.Count];
        var activityLabels = new TextBlock[ActivityMix.Count];
        bool isUpdatingActivitySliders = false;

        void RefreshActivitySliders()
        {
            isUpdatingActivitySliders = true;
            try
            {
                for (int j = 0; j < ActivityMix.Count; j++)
                {
                    int pct = _manager.ActivityMix.GetPercentage(j);
                    activitySliders[j].Value = pct;
                    activityLabels[j].Text = $"{pct}%";
                }
            }
            finally
            {
                isUpdatingActivitySliders = false;
            }
        }

        for (int i = 0; i < ActivityMix.Count; i++)
        {
            int idx = i;
            var pctText = new TextBlock
            {
                Text = $"{_manager.ActivityMix.GetPercentage(i)}%",
                Width = 36,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 115,
                Value = _manager.ActivityMix.GetPercentage(i),
                VerticalAlignment = VerticalAlignment.Center,
                IsSnapToTickEnabled = true,
                TickFrequency = 1
            };
            slider.ValueChanged += (_, args) =>
            {
                if (isUpdatingActivitySliders)
                    return;
                _manager.ActivityMix.SetPercentage(idx, (int)Math.Round(args.NewValue));
                RefreshActivitySliders();
                _manager.RefreshTrayActivityControls();
            };

            var rowPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            rowPanel.Children.Add(new TextBlock
            {
                Text = ActivityMix.Labels[i],
                Width = 64,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            rowPanel.Children.Add(slider);
            rowPanel.Children.Add(pctText);

            activitySliders[i] = slider;
            activityLabels[i] = pctText;
            activityMenu.Items.Add(new MenuItem
            {
                Header = rowPanel,
                StaysOpenOnClick = true
            });
        }

        var resetActivityItem = new MenuItem
        {
            Header = "Reset Defaults",
            StaysOpenOnClick = true
        };
        resetActivityItem.Click += (_, _) =>
        {
            _manager.ActivityMix.ResetDefaults();
            RefreshActivitySliders();
            _manager.RefreshTrayActivityControls();
        };
        activityMenu.Items.Add(new Separator());
        activityMenu.Items.Add(resetActivityItem);

        var topMostItem = new MenuItem
        {
            Header = "Always on Top",
            IsCheckable = true,
            IsChecked = _alwaysOnTop
        };
        topMostItem.Click += (_, _) => _manager.SetAlwaysOnTop(topMostItem.IsChecked);

        var clickThroughItem = new MenuItem
        {
            Header = "Click-Through Mode (Use Tray to Disable)",
            IsCheckable = true,
            IsChecked = _clickThrough
        };
        clickThroughItem.Click += (_, _) => _manager.SetClickThrough(clickThroughItem.IsChecked);

        var centerItem = new MenuItem { Header = "Center All Sheep" };
        centerItem.Click += (_, _) => _manager.CenterAll();

        var bossBattleItem = new MenuItem
        {
            Header = _manager.IsBossBattleActive ? "Boss Battle in Progress!" : "Start Boss Battle!",
            IsEnabled = _manager.Count >= 2 && !_manager.IsBossBattleActive
        };
        bossBattleItem.Click += (_, _) => _manager.StartBossBattle();

        var aboutItem = new MenuItem { Header = "About (Developed by Thovarisk)" };
        aboutItem.Click += (_, _) => SheepManager.ShowAboutDialog();

        var repoItem = new MenuItem { Header = "GitHub Repo (Thovarisk)" };
        repoItem.Click += (_, _) => SheepManager.OpenRepoUrl();

        var updateItem = new MenuItem { Header = "Check for Updates" };
        updateItem.Click += async (_, _) => await UpdateChecker.CheckForUpdatesAsync(_manager);

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => _manager.ExitApplication();

        menu.Items.Add(petItem);
        menu.Items.Add(walkItem);
        menu.Items.Add(runItem);
        menu.Items.Add(eatItem);
        menu.Items.Add(sleepItem);
        menu.Items.Add(ufoItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(addSheepItem);
        menu.Items.Add(addDogItem);
        menu.Items.Add(addSchnauzerItem);
        menu.Items.Add(addChickItem);
        menu.Items.Add(addCowItem);
        menu.Items.Add(addTobyItem);
        menu.Items.Add(switchMenu);
        menu.Items.Add(removeSheepItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(gravityItem);
        menu.Items.Add(climbItem);
        menu.Items.Add(soundMenu);
        menu.Items.Add(activityMenu);
        menu.Items.Add(topMostItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(centerItem);
        menu.Items.Add(bossBattleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(repoItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    private void TriggerPetReaction()
    {
        _state = SheepState.Happy;
        _stateTimer = 1.6;
        ShowBubble($"♥ {VoiceText}", 1.6);
        PlayPetHappy();
    }

    private void TriggerUfoAbduction()
    {
        _attachedWindowHwnd = IntPtr.Zero;
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = false;
        _velX = 0;
        _velY = 0;
        _state = SheepState.Abducted;
        _stateTimer = 2.6;
        _bubbleMessage = null;
        SoundEffects.PlayUfo();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _manager.RemoveSheep(this);
            return;
        }

        CompositionTarget.Rendering -= OnRenderFrame;
    }
}
