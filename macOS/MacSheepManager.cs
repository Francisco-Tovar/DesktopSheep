using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace KawaiiDesktopPets;

internal sealed class MacSheepManager
{
    public const int MaxSheep = 12;
    public const string RepoUrl = "https://github.com/Francisco-Tovar/DesktopSheep";

    private readonly List<MacPetWindow> _flock = new();
    private readonly MacWindowTracker _windowTracker = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _collisionTimer = new();

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayAddItem;
    private NativeMenuItem? _trayAddDogItem;
    private NativeMenuItem? _trayAddSchnauzerItem;
    private NativeMenuItem? _trayAddChickItem;
    private NativeMenuItem? _trayAddCowItem;
    private NativeMenuItem? _trayAddTobyItem;
    private NativeMenuItem? _trayRemoveItem;
    private NativeMenuItem? _trayGravityItem;
    private NativeMenuItem? _trayClimbItem;
    private NativeMenuItem? _traySoundItem;
    private NativeMenuItem? _trayTopMostItem;
    private NativeMenuItem? _trayClickThroughItem;

    public bool GravityEnabled { get; private set; } = true;
    public bool WindowClimbingEnabled { get; private set; } = true;
    public bool AlwaysOnTop { get; private set; } = true;
    public bool ClickThrough { get; private set; }

    public bool SoundEnabled
    {
        get => SoundEffects.Enabled;
        private set => SoundEffects.Enabled = value;
    }

    public double SoundVolume
    {
        get => SoundEffects.Volume;
        private set => SoundEffects.Volume = Math.Clamp(value, 0.0, 1.0);
    }

    public int Count => _flock.Count;
    public MacWindowTracker WindowTracker => _windowTracker;
    internal ActivityMix ActivityMix { get; } = new();

    public void Start()
    {
        InitializeTrayIcon();
        AddPet(PetKind.Sheep, spawnNearCenter: false);

        _collisionTimer.Interval = TimeSpan.FromMilliseconds(40);
        _collisionTimer.Tick += (_, _) => ResolveFlockCollisions();
        _collisionTimer.Start();

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(1500);
            await UpdateChecker.CheckForUpdatesAsync(this, silentWhenUpToDate: true);
        });
    }

    public void AddSheep(bool spawnNearCenter = true) => AddPet(PetKind.Sheep, spawnNearCenter);
    public void AddDog(bool spawnNearCenter = true) => AddPet(PetKind.Dog, spawnNearCenter);
    public void AddSchnauzer(bool spawnNearCenter = true) => AddPet(PetKind.Schnauzer, spawnNearCenter);
    public void AddChick(bool spawnNearCenter = true) => AddPet(PetKind.Chick, spawnNearCenter);
    public void AddCow(bool spawnNearCenter = true) => AddPet(PetKind.Cow, spawnNearCenter);
    public void AddToby(bool spawnNearCenter = true) => AddPet(PetKind.Toby, spawnNearCenter);

    public void AddPet(PetKind kind, bool spawnNearCenter = true)
    {
        if (_flock.Count >= MaxSheep)
            return;

        var pet = new MacPetWindow(this, kind, spawnNearCenter);
        _flock.Add(pet);
        pet.Show();
        UpdateTrayMenuState();
    }

    private void ResolveFlockCollisions()
    {
        for (int i = 0; i < _flock.Count; i++)
        {
            var a = _flock[i];
            if (!a.CanCollideWithFlock)
                continue;

            double ax = a.PetLeft + a.Width / 2.0;
            double ay = a.PetTop + a.Height / 2.0;

            for (int j = i + 1; j < _flock.Count; j++)
            {
                var b = _flock[j];
                if (!b.CanCollideWithFlock)
                    continue;

                double bx = b.PetLeft + b.Width / 2.0;
                double by = b.PetTop + b.Height / 2.0;
                double dx = ax - bx;
                double dy = ay - by;

                if (Math.Abs(dy) < 42 && Math.Abs(dx) < 58)
                {
                    int pushA = dx >= 0 ? 1 : -1;
                    a.ReactToFlockBump(pushA);
                    b.ReactToFlockBump(-pushA);
                    SoundEffects.PlayBoing();
                    break;
                }
            }
        }
    }

    public void RemoveSheep(MacPetWindow sheep)
    {
        if (_flock.Count <= 1)
            return;

        if (_flock.Remove(sheep))
        {
            sheep.ForceClose();
            UpdateTrayMenuState();
        }
    }

    public void RemoveLastSheep()
    {
        if (_flock.Count <= 1)
            return;

        var last = _flock[^1];
        RemoveSheep(last);
    }

    public void CenterAll()
    {
        for (int i = 0; i < _flock.Count; i++)
        {
            double offsetX = (i - (_flock.Count - 1) / 2.0) * 55;
            _flock[i].CenterOnPrimaryScreen(offsetX);
        }
    }

    public void ScatterAll()
    {
        var primary = _flock.FirstOrDefault()?.Screens.Primary;
        double s = primary != null && primary.Scaling > 0 ? primary.Scaling : 1.0;
        var wa = primary != null
            ? new Rect(primary.WorkingArea.X / s, primary.WorkingArea.Y / s, primary.WorkingArea.Width / s, primary.WorkingArea.Height / s)
            : new Rect(0, 0, 1280, 720);

        foreach (var sheep in _flock)
        {
            double x = wa.Left + _rng.NextDouble() * Math.Max(40, wa.Width - sheep.Width);
            double y = wa.Top + _rng.NextDouble() * Math.Max(40, wa.Height * 0.45);
            sheep.WarpTo(x, y);
        }
    }

    public void SetGravity(bool enabled)
    {
        GravityEnabled = enabled;
        foreach (var sheep in _flock)
            sheep.SetGravity(enabled);
        UpdateTrayMenuState();
    }

    public void SetWindowClimbing(bool enabled)
    {
        WindowClimbingEnabled = enabled;
        foreach (var sheep in _flock)
            sheep.SetWindowClimbing(enabled);
        UpdateTrayMenuState();
    }

    public void SetAlwaysOnTop(bool enabled)
    {
        AlwaysOnTop = enabled;
        foreach (var sheep in _flock)
            sheep.SetAlwaysOnTop(enabled);
        UpdateTrayMenuState();
    }

    public void SetClickThrough(bool enabled)
    {
        ClickThrough = enabled;
        foreach (var sheep in _flock)
            sheep.SetClickThrough(enabled);
        UpdateTrayMenuState();
    }

    public void SetSoundEnabled(bool enabled)
    {
        SoundEnabled = enabled;
        UpdateTrayMenuState();
    }

    public void SetSoundVolume(double volume)
    {
        SoundVolume = volume;
        UpdateTrayMenuState();
    }

    public static void OpenRepoUrl() => UpdateChecker.OpenUrl(RepoUrl);

    public static void ShowAboutDialog()
    {
        var window = new Window
        {
            Title = "About KawaiiDesktopPets",
            Width = 360,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"KawaiiDesktopPets v{UpdateChecker.CurrentVersionText}",
            FontSize = 16,
            FontWeight = FontWeight.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Developed by Thovarisk (macOS Edition)",
            FontSize = 13
        });

        var repoBtn = new Button
        {
            Content = RepoUrl,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = SolidColorBrush.Parse("#2563EB")
        };
        repoBtn.Click += (_, _) => OpenRepoUrl();
        panel.Children.Add(repoBtn);

        var okBtn = new Button
        {
            Content = "OK",
            Width = 72,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        okBtn.Click += (_, _) => window.Close();
        panel.Children.Add(okBtn);

        window.Content = panel;
        window.Show();
    }

    public void ExitApplication()
    {
        _collisionTimer.Stop();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        foreach (var sheep in _flock)
            sheep.ForceClose();

        _flock.Clear();
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void InitializeTrayIcon()
    {
        var menu = new NativeMenu();

        _trayAddItem = new NativeMenuItem("Add Sheep");
        _trayAddItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddSheep(true));

        _trayAddDogItem = new NativeMenuItem("Add Dachshund");
        _trayAddDogItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddDog(true));

        _trayAddSchnauzerItem = new NativeMenuItem("Add Schnauzer");
        _trayAddSchnauzerItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddSchnauzer(true));

        _trayAddChickItem = new NativeMenuItem("Add Chick");
        _trayAddChickItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddChick(true));

        _trayAddCowItem = new NativeMenuItem("Add Cow");
        _trayAddCowItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddCow(true));

        _trayAddTobyItem = new NativeMenuItem("Add Toby (Cat)");
        _trayAddTobyItem.Click += (_, _) => Dispatcher.UIThread.Post(() => AddToby(true));

        _trayRemoveItem = new NativeMenuItem("Remove Pet");
        _trayRemoveItem.Click += (_, _) => Dispatcher.UIThread.Post(RemoveLastSheep);

        var centerItem = new NativeMenuItem("Center All Pets");
        centerItem.Click += (_, _) => Dispatcher.UIThread.Post(CenterAll);

        var scatterItem = new NativeMenuItem("Scatter Flock");
        scatterItem.Click += (_, _) => Dispatcher.UIThread.Post(ScatterAll);

        _trayGravityItem = new NativeMenuItem("Gravity")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = GravityEnabled
        };
        _trayGravityItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetGravity(!GravityEnabled));

        _trayClimbItem = new NativeMenuItem("Window Climbing & Landing")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = WindowClimbingEnabled
        };
        _trayClimbItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetWindowClimbing(!WindowClimbingEnabled));

        _traySoundItem = new NativeMenuItem("Sound Effects")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = SoundEnabled
        };
        _traySoundItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetSoundEnabled(!SoundEnabled));

        var volPresets = new NativeMenu();
        foreach (int pct in new[] { 0, 10, 25, 50, 75, 100 })
        {
            int target = pct;
            var vItem = new NativeMenuItem($"{target}%");
            vItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetSoundVolume(target / 100.0));
            volPresets.Items.Add(vItem);
        }
        var trayVolumeMenu = new NativeMenuItem("Volume") { Menu = volPresets };

        _trayTopMostItem = new NativeMenuItem("Always on Top")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = AlwaysOnTop
        };
        _trayTopMostItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetAlwaysOnTop(!AlwaysOnTop));

        _trayClickThroughItem = new NativeMenuItem("Click-Through Mode")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = ClickThrough
        };
        _trayClickThroughItem.Click += (_, _) => Dispatcher.UIThread.Post(() => SetClickThrough(!ClickThrough));

        var aboutItem = new NativeMenuItem("About (Developed by Thovarisk)");
        aboutItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowAboutDialog);

        var repoItem = new NativeMenuItem("GitHub Repo (Thovarisk)");
        repoItem.Click += (_, _) => OpenRepoUrl();

        var updateItem = new NativeMenuItem("Check for Updates");
        updateItem.Click += (_, _) => Dispatcher.UIThread.InvokeAsync(() => UpdateChecker.CheckForUpdatesAsync(this));

        var versionItem = new NativeMenuItem($"Version v{UpdateChecker.CurrentVersionText}")
        {
            IsEnabled = false
        };

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => Dispatcher.UIThread.Post(ExitApplication);

        menu.Items.Add(_trayAddItem);
        menu.Items.Add(_trayAddDogItem);
        menu.Items.Add(_trayAddSchnauzerItem);
        menu.Items.Add(_trayAddChickItem);
        menu.Items.Add(_trayAddCowItem);
        menu.Items.Add(_trayAddTobyItem);
        menu.Items.Add(_trayRemoveItem);
        menu.Items.Add(centerItem);
        menu.Items.Add(scatterItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_trayGravityItem);
        menu.Items.Add(_trayClimbItem);
        menu.Items.Add(_traySoundItem);
        menu.Items.Add(trayVolumeMenu);
        menu.Items.Add(_trayTopMostItem);
        menu.Items.Add(_trayClickThroughItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(repoItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(versionItem);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "KawaiiDesktopPets",
            IsVisible = true,
            Menu = menu
        };
        _trayIcon.Clicked += (_, _) => Dispatcher.UIThread.Post(CenterAll);

        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (_trayAddItem != null)
        {
            _trayAddItem.Header = $"Add Sheep ({_flock.Count}/{MaxSheep})";
            _trayAddItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayAddDogItem != null)
        {
            _trayAddDogItem.Header = $"Add Dachshund ({_flock.Count}/{MaxSheep})";
            _trayAddDogItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayAddSchnauzerItem != null)
        {
            _trayAddSchnauzerItem.Header = $"Add Schnauzer ({_flock.Count}/{MaxSheep})";
            _trayAddSchnauzerItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayAddChickItem != null)
        {
            _trayAddChickItem.Header = $"Add Chick ({_flock.Count}/{MaxSheep})";
            _trayAddChickItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayAddCowItem != null)
        {
            _trayAddCowItem.Header = $"Add Cow ({_flock.Count}/{MaxSheep})";
            _trayAddCowItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayAddTobyItem != null)
        {
            _trayAddTobyItem.Header = $"Add Toby (Cat) ({_flock.Count}/{MaxSheep})";
            _trayAddTobyItem.IsEnabled = _flock.Count < MaxSheep;
        }

        if (_trayRemoveItem != null)
            _trayRemoveItem.IsEnabled = _flock.Count > 1;

        if (_trayGravityItem != null)
            _trayGravityItem.IsChecked = GravityEnabled;

        if (_trayClimbItem != null)
            _trayClimbItem.IsChecked = WindowClimbingEnabled;

        if (_traySoundItem != null)
            _traySoundItem.IsChecked = SoundEnabled;

        if (_trayTopMostItem != null)
            _trayTopMostItem.IsChecked = AlwaysOnTop;

        if (_trayClickThroughItem != null)
            _trayClickThroughItem.IsChecked = ClickThrough;

        if (_trayIcon != null)
            _trayIcon.ToolTipText = _flock.Count == 1 ? "KawaiiDesktopPets (1 pet)" : $"KawaiiDesktopPets ({_flock.Count} pets)";
    }

    private static WindowIcon? CreateTrayIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("sheep.ico");
            return stream != null ? new WindowIcon(stream) : null;
        }
        catch
        {
            return null;
        }
    }
}
