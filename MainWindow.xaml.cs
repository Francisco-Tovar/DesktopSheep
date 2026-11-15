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
    private double _climbCooldown;
    private double _collisionCooldown;
    private double _topMostRefreshTimer = 1.5;
    private bool _isContextMenuOpen;

    private readonly bool _spawnNearCenter;
    private PetKind _petKind;

    public bool CanCollideWithFlock =>
        !_isDragging
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

    private bool IsDog => _petKind is PetKind.Dog or PetKind.Schnauzer;
    private string VoiceText => IsDog ? "Woof!" : "Baa!";

    private void PlayPetVoice() => SoundEffects.PlayVoice(_petKind);
    private void PlayPetHappy() => SoundEffects.PlayHappy(_petKind);

    public void ForceClose()
    {
        _allowClose = true;
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
            ShowBubble(IsDog ? "Woof?!" : "Baa?!", 1.5);
            return;
        }

        double roll = _rng.NextDouble();
        if (roll < 0.36)
        {
            _state = SheepState.Walking;
            _facing = _rng.Next(2) == 0 ? 1 : -1;
            _stateTimer = 2.0 + _rng.NextDouble() * 4.0;
        }
        else if (roll < 0.48)
        {
            _state = SheepState.Running;
            _facing = _rng.Next(2) == 0 ? 1 : -1;
            _stateTimer = 1.4 + _rng.NextDouble() * 2.2;
            if (_rng.NextDouble() < 0.35)
                ShowBubble("Zoom!", 1.0);
        }
        else if (roll < 0.68)
        {
            _state = SheepState.Idle;
            _stateTimer = 1.5 + _rng.NextDouble() * 2.5;
            if (_rng.NextDouble() < 0.25)
            {
                ShowBubble(VoiceText, 1.4);
                PlayPetVoice();
            }
        }
        else if (roll < 0.78)
        {
            _state = SheepState.Eating;
            _stateTimer = 2.2 + _rng.NextDouble() * 2.5;
            ShowBubble("Nom...", 1.5);
            SoundEffects.PlayNom();
        }
        else if (roll < 0.86)
        {
            _state = SheepState.Sitting;
            _stateTimer = 2.5 + _rng.NextDouble() * 3.0;
        }
        else if (roll < 0.92)
        {
            _state = SheepState.Yawning;
            _stateTimer = 1.5;
            ShowBubble("*Yawn*", 1.4);
            SoundEffects.PlayYawn();
        }
        else if (roll < 0.985)
        {
            _state = SheepState.Sleeping;
            _stateTimer = 4.0 + _rng.NextDouble() * 4.0;
            ShowBubble("Zzz...", 2.5);
        }
        else
        {
            TriggerUfoAbduction();
        }
    }

    private void UpdatePhysics(double dt)
    {
        GetDpiScale(out double dpiX, out double dpiY);
        Rect bounds = GetCurrentScreenWorkArea(dpiX, dpiY);
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
            double speed = _state == SheepState.Running ? RunSpeed : WalkSpeed;
            Left += _facing * speed * dt;

            if (Left <= minX)
            {
                Left = minX;
                _facing = 1;
            }
            else if (Left >= maxX)
            {
                Left = maxX;
                _facing = -1;
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
            double prevFeetY = Top + Height - WindowFootOffset;

            _velY += Gravity * dt;
            Left += _velX * dt;
            Top += _velY * dt;

            double newFeetY = Top + Height - WindowFootOffset;
            double centerX = Left + Width / 2.0;

            if (Left <= minX)
            {
                Left = minX;
                _velX = Math.Abs(_velX) * 0.5;
                _facing = 1;
            }
            else if (Left >= maxX)
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
                    Top = landedWin.Value.Bounds.Top - Height + WindowFootOffset;
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

    private void UpdateClimbing(double dt, double dpiX, double dpiY, Rect bounds, IReadOnlyList<TrackedWindow> windows)
    {
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

        double targetTop = winRect.Top - Height + WindowFootOffset;
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
        var centerPoint = new Drawing.Point((int)((Left + Width / 2) / dpiX), (int)((Top + Height / 2) / dpiY));
        var screen = Forms.Screen.FromPoint(centerPoint);
        var wa = screen.WorkingArea;
        return new Rect(wa.Left * dpiX, wa.Top * dpiY, wa.Width * dpiX, wa.Height * dpiY);
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

        var petItem = new MenuItem { Header = IsDog ? "Pet Dog (Woof!)" : "Pet Sheep (Baa!)" };
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

        var soundItem = new MenuItem
        {
            Header = "Sound Effects",
            IsCheckable = true,
            IsChecked = _manager.SoundEnabled
        };
        soundItem.Click += (_, _) => _manager.SetSoundEnabled(soundItem.IsChecked);

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

        var aboutItem = new MenuItem { Header = "About (Developed by Thovarisk)" };
        aboutItem.Click += (_, _) => SheepManager.ShowAboutDialog();

        var repoItem = new MenuItem { Header = "GitHub Repo (Thovarisk)" };
        repoItem.Click += (_, _) => SheepManager.OpenRepoUrl();

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
        menu.Items.Add(switchMenu);
        menu.Items.Add(removeSheepItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(gravityItem);
        menu.Items.Add(climbItem);
        menu.Items.Add(soundItem);
        menu.Items.Add(topMostItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(centerItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(repoItem);
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
