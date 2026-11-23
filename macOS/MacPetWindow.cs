using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace KawaiiDesktopPets;

public sealed class MacPetWindow : Window
{
    private const double Gravity = 1450.0;
    private const double WalkSpeed = 72.0;
    private const double RunSpeed = 165.0;
    private const double ClimbSpeed = 62.0;
    private const double BounceDamping = 0.35;
    private const double WindowFootOffset = 12.0;
    private const double BasePetSize = 140.0;

    private readonly MacSheepManager _manager;
    private readonly MacSheepView _sheep;
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _tickTimer;

    private double _lastTimeSeconds;
    private double _posX;
    private double _posY;

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
    private bool _isContextMenuOpen;

    private readonly bool _spawnNearCenter;
    private PetKind _petKind;

    public PetKind PetKind => _petKind;

    public bool CanCollideWithFlock =>
        !_isDragging
        && _collisionCooldown <= 0
        && _state is SheepState.Walking or SheepState.Running;

    public double PetLeft => _posX;
    public double PetTop => _posY;

    internal MacPetWindow(MacSheepManager manager, PetKind petKind, bool spawnNearCenter)
    {
        _manager = manager;
        _petKind = petKind;
        _spawnNearCenter = spawnNearCenter;
        _gravityEnabled = manager.GravityEnabled;
        _windowClimbingEnabled = manager.WindowClimbingEnabled;
        _alwaysOnTop = manager.AlwaysOnTop;
        _clickThrough = manager.ClickThrough;

        Title = "KawaiiDesktopPets";
        Width = BasePetSize;
        Height = BasePetSize;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = _alwaysOnTop;
        ShowInTaskbar = false;
        CanResize = false;

        _sheep = new MacSheepView();
        _sheep.SetPetKind(_petKind);
        Content = _sheep;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, Window_DragEnter);
        AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        PointerPressed += Window_PointerPressed;
        PointerMoved += Window_PointerMoved;
        PointerReleased += Window_PointerReleased;
        PointerCaptureLost += Window_PointerCaptureLost;
        Opened += Window_Opened;
        Closing += Window_Closing;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _tickTimer.Tick += (_, _) => OnTick();
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
        _tickTimer.Stop();
        Close();
    }

    public void SetGravity(bool enabled)
    {
        _gravityEnabled = enabled;
        if (!enabled && _state == SheepState.Falling)
        {
            _velX = 0;
            _velY = 0;
            _state = SheepState.Idle;
            _stateTimer = 1.5;
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
        var handle = TryGetPlatformHandle();
        if (handle != null)
            MacNative.SetWindowAlwaysOnTop(handle.Handle, enabled);
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        var handle = TryGetPlatformHandle();
        if (handle != null)
            MacNative.SetWindowClickThrough(handle.Handle, enabled);
    }

    public void CenterOnPrimaryScreen(double offsetX = 0)
    {
        GetPrimaryWorkArea(out Rect wa);
        _posX = Math.Clamp(wa.Left + (wa.Width - Width) / 2.0 + offsetX, wa.Left, wa.Right - Width);
        _posY = wa.Top + (wa.Height - Height) / 2.0;
        SyncWindowPosition();
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
        _posX = x;
        _posY = y;
        SyncWindowPosition();
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
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        GetPrimaryWorkArea(out Rect wa);
        if (_spawnNearCenter)
        {
            _posX = wa.Left + (wa.Width - Width) * (0.25 + _rng.NextDouble() * 0.5);
            _posY = wa.Top + 40;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        }
        else
        {
            _posX = wa.Right - Width - 80;
            _posY = wa.Bottom - Height;
            _state = SheepState.Idle;
        }

        SyncWindowPosition();
        ApplyMacWindowStyles();
        _lastTimeSeconds = _clock.Elapsed.TotalSeconds;
        _tickTimer.Start();
    }

    private void ApplyMacWindowStyles()
    {
        var handle = TryGetPlatformHandle();
        if (handle == null)
            return;

        MacNative.SetWindowAlwaysOnTop(handle.Handle, _alwaysOnTop);
        MacNative.SetWindowClickThrough(handle.Handle, _clickThrough);
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private void OnTick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTimeSeconds, 0.0, 0.05);
        _lastTimeSeconds = now;

        if (_climbCooldown > 0)
            _climbCooldown -= dt;
        if (_collisionCooldown > 0)
            _collisionCooldown -= dt;

        UpdateBlink(dt);
        UpdateBubble(dt);

        if (!_isDragging && !_isContextMenuOpen)
        {
            UpdateBehavior(dt);
            UpdatePhysics(dt);
            SyncWindowPosition();
        }

        _sheep.UpdateVisuals(_state, now, _facing, _blinkDuration > 0, _bubbleMessage);
    }

    private void SyncWindowPosition()
    {
        double scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        Position = new PixelPoint((int)Math.Round(_posX * scale), (int)Math.Round(_posY * scale));
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
            GetCurrentScreenAreas(out _, out Rect fullBounds);
            if (TryFindMonitorBelow(fullBounds, _posX + Width / 2.0) && _rng.NextDouble() < 0.16)
            {
                _posY = fullBounds.Bottom + 4;
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

    private void UpdatePhysics(double dt)
    {
        GetCurrentScreenAreas(out Rect bounds, out Rect fullBounds);
        double floorY = bounds.Bottom - Height;
        double minX = bounds.Left;
        double maxX = bounds.Right - Width;

        var screenWorkAreas = GetAllWorkAreas();
        var windows = _windowClimbingEnabled
            ? _manager.WindowTracker.GetWindows(screenWorkAreas)
            : Array.Empty<TrackedWindow>();

        if (_state == SheepState.Abducted)
        {
            _posY = Math.Max(bounds.Top + 10, _posY - 26.0 * dt);
            return;
        }

        if (_state == SheepState.Climbing)
        {
            UpdateClimbing(dt, windows, screenWorkAreas);
            return;
        }

        if (_attachedWindowHwnd != IntPtr.Zero)
        {
            var attached = _manager.WindowTracker.FindWindow(_attachedWindowHwnd, screenWorkAreas);
            double centerX = _posX + Width / 2.0;

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
                _posX += winRect.Left - _attachedWindowRect.Left;
                _posY += winRect.Top - _attachedWindowRect.Top;
                _attachedWindowRect = winRect;

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
                        _posX = Math.Clamp(_posX, winRect.Left + 10 - Width / 2.0, winRect.Right - 10 - Width / 2.0);
                        _facing = -_facing;
                    }
                }
            }
        }

        if (_state is SheepState.Walking or SheepState.Running)
        {
            double speed = _state == SheepState.Running ? RunSpeed : WalkSpeed;
            _posX += _facing * speed * dt;

            if ((_posX <= minX && _facing < 0) || (_posX >= maxX && _facing > 0))
            {
                bool isLeftEdge = _facing < 0;
                if (TryFindAdjacentScreen(fullBounds, _facing, _posY + Height - 12, out Rect adjWork, out _))
                {
                    if (adjWork.Bottom < bounds.Bottom - 36)
                    {
                        if (_climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero && (_state == SheepState.Running || _rng.NextDouble() < 0.65))
                        {
                            StartMonitorBorderClimb(isLeftEdge, adjWork, isLeftEdge ? minX : maxX);
                            return;
                        }
                        _posX = isLeftEdge ? minX : maxX;
                        _facing = isLeftEdge ? 1 : -1;
                    }
                }
                else if (_climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero
                    && TryFindMonitorAbove(fullBounds, isLeftEdge, out Rect aboveWork, out double climbX)
                    && (_state == SheepState.Running || _rng.NextDouble() < 0.65))
                {
                    StartMonitorBorderClimb(isLeftEdge, aboveWork, climbX);
                    return;
                }
                else if (_gravityEnabled && _climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero
                    && TryFindMonitorBelow(fullBounds, _posX + Width / 2.0)
                    && _rng.NextDouble() < 0.55)
                {
                    _posY = fullBounds.Bottom + 4;
                    _velX = _facing * 45.0;
                    _velY = 0;
                    _climbCooldown = 3.5;
                    _state = SheepState.Falling;
                    ShowBubble("Wheee!", 1.0);
                    return;
                }
                else
                {
                    _posX = isLeftEdge ? minX : maxX;
                    _facing = isLeftEdge ? 1 : -1;
                }
            }

            if (_windowClimbingEnabled && _climbCooldown <= 0 && _attachedWindowHwnd == IntPtr.Zero)
            {
                double sheepCenterX = _posX + Width / 2.0;
                double sheepFeetY = _posY + Height;
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

        bool aboveFloor = _posY < floorY - 1 && _attachedWindowHwnd == IntPtr.Zero;
        if (_gravityEnabled && (_state == SheepState.Falling || aboveFloor))
        {
            _state = SheepState.Falling;
            double prevFeetY = _posY + Height - WindowFootOffset;

            _velY += Gravity * dt;
            _posX += _velX * dt;
            _posY += _velY * dt;

            double newFeetY = _posY + Height - WindowFootOffset;
            double centerX = _posX + Width / 2.0;

            if (_posX <= minX && _velX <= 0
                && !TryFindAdjacentScreen(fullBounds, -1, _posY + Height / 2.0, out _, out _))
            {
                _posX = minX;
                _velX = Math.Abs(_velX) * 0.5;
                _facing = 1;
            }
            else if (_posX >= maxX && _velX >= 0
                && !TryFindAdjacentScreen(fullBounds, 1, _posY + Height / 2.0, out _, out _))
            {
                _posX = maxX;
                _velX = -Math.Abs(_velX) * 0.5;
                _facing = -1;
            }

            if (_windowClimbingEnabled && _velY >= 0)
            {
                var landedWin = _manager.WindowTracker.FindLandingWindow(
                    centerX, prevFeetY, newFeetY, bounds.Top, windows);

                if (landedWin != null)
                {
                    _posY = landedWin.Value.Bounds.Top - Height + WindowFootOffset;
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

            if (_posY >= floorY)
            {
                _posY = floorY;
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

    private void UpdateClimbing(double dt, IReadOnlyList<TrackedWindow> windows, IReadOnlyList<Rect> screenWorkAreas)
    {
        if (_climbingMonitorBorder)
        {
            _posX = _climbBorderX;
            _posY -= ClimbSpeed * dt;

            if (_posY <= _climbTargetFloorY)
            {
                _posY = _climbTargetFloorY;
                double stepIn = _climbingMonitorLeftSide ? 24 : -24;
                _posX = Math.Clamp(_climbBorderX + stepIn, _climbTargetWorkArea.Left, _climbTargetWorkArea.Right - Width);
                _climbingMonitorBorder = false;
                _climbCooldown = 3.5;
                _state = SheepState.Walking;
                _stateTimer = 2.5;
                ShowBubble("Made it!", 1.2);
            }
            return;
        }

        var target = _manager.WindowTracker.FindWindow(_climbingWindowHwnd, screenWorkAreas);
        if (target == null)
        {
            _climbingWindowHwnd = IntPtr.Zero;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            return;
        }

        var winRect = target.Value.Bounds;
        double edgeX = _climbingLeftSide ? winRect.Left : winRect.Right;
        if (_manager.WindowTracker.IsPointOccluded(edgeX + (_climbingLeftSide ? 4 : -4), _posY + Height / 2.0, target.Value.ZOrder, windows))
        {
            _climbingWindowHwnd = IntPtr.Zero;
            _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
            return;
        }

        _posX = _climbingLeftSide
            ? winRect.Left - Width / 2.0 - 8
            : winRect.Right - Width / 2.0 + 8;

        _posY -= ClimbSpeed * dt;

        double targetTop = winRect.Top - Height + WindowFootOffset;
        if (_posY <= targetTop)
        {
            _posY = targetTop;
            _posX = _climbingLeftSide
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

    private void GetPrimaryWorkArea(out Rect workArea)
    {
        var primary = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (primary != null)
        {
            double s = primary.Scaling > 0 ? primary.Scaling : 1.0;
            var wa = primary.WorkingArea;
            workArea = new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
            return;
        }
        workArea = new Rect(0, 0, 1280, 720);
    }

    private void GetCurrentScreenAreas(out Rect workArea, out Rect fullBounds)
    {
        double scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        var centerPx = new PixelPoint((int)((_posX + Width / 2.0) * scale), (int)((_posY + Height / 2.0) * scale));
        var screen = Screens.ScreenFromPoint(centerPx) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            double s = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var wa = screen.WorkingArea;
            var fb = screen.Bounds;
            workArea = new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
            fullBounds = new Rect(fb.X / s, fb.Y / s, fb.Width / s, fb.Height / s);
            return;
        }
        workArea = new Rect(0, 0, 1280, 720);
        fullBounds = workArea;
    }

    private List<(Rect WorkArea, Rect FullBounds)> GetAllScreenAreas()
    {
        var list = new List<(Rect, Rect)>();
        foreach (var screen in Screens.All)
        {
            double s = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var wa = screen.WorkingArea;
            var fb = screen.Bounds;
            list.Add((
                new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s),
                new Rect(fb.X / s, fb.Y / s, fb.Width / s, fb.Height / s)
            ));
        }
        return list;
    }

    private IReadOnlyList<Rect> GetAllWorkAreas()
    {
        var list = new List<Rect>();
        foreach (var (wa, _) in GetAllScreenAreas())
            list.Add(wa);
        return list;
    }

    private bool TryFindAdjacentScreen(
        Rect currentBounds,
        int dir,
        double probeY,
        out Rect targetWorkArea,
        out Rect targetBounds)
    {
        const double edgeTol = 48.0;
        foreach (var (wa, fb) in GetAllScreenAreas())
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
        out Rect targetWorkArea,
        out double climbX)
    {
        const double edgeTol = 48.0;
        double desiredX = leftSide ? currentBounds.Left : currentBounds.Right - Width;

        foreach (var (wa, fb) in GetAllScreenAreas())
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

    private bool TryFindMonitorBelow(Rect currentBounds, double centerX)
    {
        const double edgeTol = 48.0;
        foreach (var (_, fb) in GetAllScreenAreas())
        {
            if (fb == currentBounds)
                continue;

            if (fb.Top >= currentBounds.Bottom - edgeTol && centerX >= fb.Left && centerX <= fb.Right)
                return true;
        }
        return false;
    }

    private Point GetPointerScreenInDip(PointerEventArgs e)
    {
        var screenPx = this.PointToScreen(e.GetPosition(this));
        double scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        return new Point(screenPx.X / scale, screenPx.Y / scale);
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed)
        {
            ShowPetContextMenu();
            return;
        }

        if (!pt.Properties.IsLeftButtonPressed)
            return;

        var now = DateTime.UtcNow;
        if (e.ClickCount >= 2 || (now - _lastClickTime).TotalMilliseconds < 320)
        {
            _lastClickTime = DateTime.MinValue;
            TriggerPetReaction();
            return;
        }
        _lastClickTime = now;

        _isDragging = true;
        _climbingWindowHwnd = IntPtr.Zero;
        _climbingMonitorBorder = false;
        _attachedWindowHwnd = IntPtr.Zero;
        _state = SheepState.Dragged;
        _velX = 0;
        _velY = 0;
        _dragVelX = 0;
        _dragVelY = 0;

        _dragOffsetInWindow = e.GetPosition(this);
        _prevCursorScreen = GetPointerScreenInDip(e);
        e.Pointer.Capture(this);
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
            return;

        Point cursor = GetPointerScreenInDip(e);
        double dx = cursor.X - _prevCursorScreen.X;
        double dy = cursor.Y - _prevCursorScreen.Y;

        _posX = cursor.X - _dragOffsetInWindow.X;
        _posY = cursor.Y - _dragOffsetInWindow.Y;
        SyncWindowPosition();

        _dragVelX = _dragVelX * 0.65 + (dx * 60.0) * 0.35;
        _dragVelY = _dragVelY * 0.65 + (dy * 60.0) * 0.35;
        _prevCursorScreen = cursor;
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);

        if (_gravityEnabled)
        {
            _velX = Math.Clamp(_dragVelX, -900, 900);
            _velY = Math.Clamp(_dragVelY, -900, 900);
            if (Math.Abs(_velX) > 25)
                _facing = _velX >= 0 ? 1 : -1;

            _state = SheepState.Falling;
        }
        else
        {
            _state = SheepState.Idle;
        }
        _stateTimer = 1.5;
    }

    private void Window_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _velX = 0;
        _velY = 0;
        _state = _gravityEnabled ? SheepState.Falling : SheepState.Idle;
        _stateTimer = 1.5;
    }

    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            ShowBubble("Ooh?", 1.0);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object? sender, DragEventArgs e)
    {
        if (_bubbleMessage == "Ooh?")
            _bubbleMessage = null;
    }

    private void Window_Drop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null)
            return;

        foreach (IStorageItem item in files)
        {
            string? path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                continue;

            _state = SheepState.Eating;
            _stateTimer = 2.6;

            if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                ShowBubble("♪ Nom!", 2.2);
                SoundEffects.PlayWavFile(path);
            }
            else
            {
                ShowBubble("Nom nom!", 2.0);
                SoundEffects.PlayNom();
            }
            break;
        }
    }

    private void ShowPetContextMenu()
    {
        var menu = new ContextMenu();
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
            _facing = _rng.Next(2) == 0 ? 1 : -1;
            _stateTimer = 4.0;
        };

        var runItem = new MenuItem { Header = "Run / Zoomies!" };
        runItem.Click += (_, _) =>
        {
            _state = SheepState.Running;
            _facing = _rng.Next(2) == 0 ? 1 : -1;
            _stateTimer = 3.0;
            ShowBubble("Zoom!", 1.2);
        };

        var eatItem = new MenuItem { Header = "Eat Grass" };
        eatItem.Click += (_, _) =>
        {
            _state = SheepState.Eating;
            _stateTimer = 3.0;
            ShowBubble("Nom...", 1.5);
            SoundEffects.PlayNom();
        };

        var sleepItem = new MenuItem { Header = "Go to Sleep" };
        sleepItem.Click += (_, _) =>
        {
            _state = SheepState.Sleeping;
            _stateTimer = 5.0;
            ShowBubble("Zzz...", 2.5);
        };

        var ufoItem = new MenuItem { Header = "UFO Abduction" };
        ufoItem.Click += (_, _) => TriggerUfoAbduction();

        MenuItem MakeAddItem(string label, Action onClick)
        {
            var item = new MenuItem
            {
                Header = $"{label} ({_manager.Count}/{MacSheepManager.MaxSheep})",
                IsEnabled = _manager.Count < MacSheepManager.MaxSheep
            };
            item.Click += (_, _) => onClick();
            return item;
        }

        var addSheepItem = MakeAddItem("Add Sheep", () => _manager.AddSheep(true));
        var addDogItem = MakeAddItem("Add Dachshund", () => _manager.AddDog(true));
        var addSchnauzerItem = MakeAddItem("Add Schnauzer", () => _manager.AddSchnauzer(true));
        var addChickItem = MakeAddItem("Add Chick", () => _manager.AddChick(true));
        var addCowItem = MakeAddItem("Add Cow", () => _manager.AddCow(true));
        var addTobyItem = MakeAddItem("Add Toby (Cat)", () => _manager.AddToby(true));

        var switchMenu = new MenuItem { Header = "Switch Pet" };
        void AddSwitchOption(string label, PetKind kind)
        {
            var item = new MenuItem
            {
                Header = _petKind == kind ? $"✓ {label}" : label
            };
            item.Click += (_, _) =>
            {
                _petKind = kind;
                _sheep.SetPetKind(kind);
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

        var removeItem = new MenuItem
        {
            Header = "Remove This Pet",
            IsEnabled = _manager.Count > 1
        };
        removeItem.Click += (_, _) => _manager.RemoveSheep(this);

        MenuItem MakeToggleItem(string label, bool isChecked, Action<bool> setter)
        {
            var item = new MenuItem
            {
                Header = label,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = isChecked
            };
            item.Click += (_, _) => setter(item.IsChecked);
            return item;
        }

        var gravityItem = MakeToggleItem("Gravity", _gravityEnabled, _manager.SetGravity);
        var climbItem = MakeToggleItem("Window Climbing & Landing", _windowClimbingEnabled, _manager.SetWindowClimbing);

        var soundMenu = new MenuItem { Header = "Sound Effects" };
        var soundItem = MakeToggleItem("Mute / Unmute SFX", _manager.SoundEnabled, _manager.SetSoundEnabled);

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
        volumeSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                _manager.SetSoundVolume(volumeSlider.Value / 100.0);
                volumeLabel.Text = $"{(int)Math.Round(volumeSlider.Value)}%";
            }
        };
        var volumePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center },
                volumeSlider,
                volumeLabel
            }
        };
        soundMenu.Items.Add(soundItem);
        soundMenu.Items.Add(new Separator());
        soundMenu.Items.Add(new MenuItem { Header = volumePanel, StaysOpenOnClick = true });

        var activityMenu = new MenuItem { Header = "Activity Mix (100%)" };
        var activitySliders = new Slider[ActivityMix.Count];
        var activityLabels = new TextBlock[ActivityMix.Count];
        bool updatingSliders = false;

        void RefreshActivitySliders()
        {
            updatingSliders = true;
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
                updatingSliders = false;
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
            slider.PropertyChanged += (_, args) =>
            {
                if (updatingSliders || args.Property != Slider.ValueProperty)
                    return;

                _manager.ActivityMix.SetPercentage(idx, (int)Math.Round(slider.Value));
                RefreshActivitySliders();
            };

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = ActivityMix.Labels[i], Width = 64, VerticalAlignment = VerticalAlignment.Center },
                    slider,
                    pctText
                }
            };

            activitySliders[i] = slider;
            activityLabels[i] = pctText;
            activityMenu.Items.Add(new MenuItem { Header = rowPanel, StaysOpenOnClick = true });
        }

        var resetActivityItem = new MenuItem { Header = "Reset Defaults", StaysOpenOnClick = true };
        resetActivityItem.Click += (_, _) =>
        {
            _manager.ActivityMix.ResetDefaults();
            RefreshActivitySliders();
        };
        activityMenu.Items.Add(new Separator());
        activityMenu.Items.Add(resetActivityItem);

        var topMostItem = MakeToggleItem("Always on Top", _alwaysOnTop, _manager.SetAlwaysOnTop);
        var clickThroughItem = MakeToggleItem("Click-Through Mode (Use Tray to Disable)", _clickThrough, _manager.SetClickThrough);

        var centerItem = new MenuItem { Header = "Center All Pets" };
        centerItem.Click += (_, _) => _manager.CenterAll();

        var aboutItem = new MenuItem { Header = "About (Developed by Thovarisk)" };
        aboutItem.Click += (_, _) => MacSheepManager.ShowAboutDialog();

        var repoItem = new MenuItem { Header = "GitHub Repo (Thovarisk)" };
        repoItem.Click += (_, _) => MacSheepManager.OpenRepoUrl();

        var updateItem = new MenuItem { Header = "Check for Updates" };
        updateItem.Click += async (_, _) => await UpdateChecker.CheckForUpdatesAsync(_manager);

        var versionItem = new MenuItem
        {
            Header = $"Version v{UpdateChecker.CurrentVersionText}",
            IsEnabled = false
        };

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
        menu.Items.Add(removeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(gravityItem);
        menu.Items.Add(climbItem);
        menu.Items.Add(soundMenu);
        menu.Items.Add(activityMenu);
        menu.Items.Add(topMostItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(centerItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(repoItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(versionItem);
        menu.Items.Add(exitItem);

        menu.Open(this);
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
        _state = SheepState.Abducted;
        _stateTimer = 2.8;
        _bubbleMessage = null;
        SoundEffects.PlayUfo();
    }
}
