using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KawaiiDesktopPets;

internal sealed class SheepManager
{
    public const int MaxSheep = 12;
    public const string RepoUrl = "https://github.com/Francisco-Tovar/DesktopSheep";

    private readonly List<MainWindow> _flock = new();
    private readonly WindowTracker _windowTracker = new();
    private readonly Random _rng = new();

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayAddItem;
    private Forms.ToolStripMenuItem? _trayAddDogItem;
    private Forms.ToolStripMenuItem? _trayAddSchnauzerItem;
    private Forms.ToolStripMenuItem? _trayRemoveItem;
    private Forms.ToolStripMenuItem? _trayGravityItem;
    private Forms.ToolStripMenuItem? _trayClimbItem;
    private Forms.ToolStripMenuItem? _traySoundItem;
    private Forms.TrackBar? _trayVolumeSlider;
    private Forms.ToolStripMenuItem? _trayTopMostItem;
    private Forms.ToolStripMenuItem? _trayClickThroughItem;

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
    public WindowTracker WindowTracker => _windowTracker;

    public void Start()
    {
        InitializeTrayIcon();
        AddPet(PetKind.Sheep, spawnNearCenter: false);
    }

    public void AddSheep(bool spawnNearCenter = true) => AddPet(PetKind.Sheep, spawnNearCenter);

    public void AddDog(bool spawnNearCenter = true) => AddPet(PetKind.Dog, spawnNearCenter);

    public void AddSchnauzer(bool spawnNearCenter = true) => AddPet(PetKind.Schnauzer, spawnNearCenter);

    public void AddPet(PetKind kind, bool spawnNearCenter = true)
    {
        if (_flock.Count >= MaxSheep)
            return;

        var pet = new MainWindow(this, kind, spawnNearCenter);
        _flock.Add(pet);
        pet.Show();
        UpdateTrayMenuState();
    }

    public void RemoveSheep(MainWindow sheep)
    {
        if (_flock.Count <= 1)
        {
            ExitApplication();
            return;
        }

        _flock.Remove(sheep);
        sheep.ForceClose();
        UpdateTrayMenuState();
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
            double offsetX = (i - (_flock.Count - 1) / 2.0) * 65.0;
            _flock[i].CenterOnPrimaryScreen(offsetX);
        }
    }

    public void ScatterAll()
    {
        var wa = SystemParameters.WorkArea;
        foreach (var sheep in _flock)
        {
            double x = wa.Left + _rng.NextDouble() * Math.Max(40, wa.Width - sheep.Width);
            double y = wa.Top + _rng.NextDouble() * Math.Max(40, wa.Height * 0.5);
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

    public void CheckFlockCollision(MainWindow mover)
    {
        if (!mover.CanCollideWithFlock)
            return;

        double mx = mover.Left + mover.Width / 2.0;
        double my = mover.Top + mover.Height;

        foreach (var other in _flock)
        {
            if (ReferenceEquals(other, mover) || !other.CanCollideWithFlock)
                continue;

            double ox = other.Left + other.Width / 2.0;
            double oy = other.Top + other.Height;

            if (Math.Abs(my - oy) < 28 && Math.Abs(mx - ox) < 54)
            {
                int pushDir = mx <= ox ? -1 : 1;
                mover.ReactToFlockBump(pushDir);
                other.ReactToFlockBump(-pushDir);
                break;
            }
        }
    }

    public static void OpenRepoUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch
        {
            // Ignore browser launch failures
        }
    }

    public static void ShowAboutDialog()
    {
        var window = new Window
        {
            Title = "About KawaiiDesktopPets",
            Width = 340,
            Height = 175,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "KawaiiDesktopPets",
            FontSize = 16,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Developed by Thovarisk",
            Margin = new Thickness(0, 6, 0, 8),
            FontSize = 13
        });

        var link = new Hyperlink(new Run(RepoUrl)) { NavigateUri = new Uri(RepoUrl) };
        link.RequestNavigate += (_, e) =>
        {
            OpenRepoUrl();
            e.Handled = true;
        };
        var linkBlock = new TextBlock();
        linkBlock.Inlines.Add(link);
        panel.Children.Add(linkBlock);

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 72,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) => window.Close();
        panel.Children.Add(okBtn);

        window.Content = panel;
        window.ShowDialog();
    }

    public void ExitApplication()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        foreach (var sheep in _flock.ToArray())
            sheep.ForceClose();

        _flock.Clear();
        System.Windows.Application.Current.Shutdown();
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        _trayAddItem = new Forms.ToolStripMenuItem("Add Sheep");
        _trayAddItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => AddSheep(true));

        _trayAddDogItem = new Forms.ToolStripMenuItem("Add Dachshund");
        _trayAddDogItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => AddDog(true));

        _trayAddSchnauzerItem = new Forms.ToolStripMenuItem("Add Schnauzer");
        _trayAddSchnauzerItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => AddSchnauzer(true));

        _trayRemoveItem = new Forms.ToolStripMenuItem("Remove Pet");
        _trayRemoveItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(RemoveLastSheep);

        var centerItem = new Forms.ToolStripMenuItem("Center All Pets");
        centerItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(CenterAll);

        var scatterItem = new Forms.ToolStripMenuItem("Scatter Flock");
        scatterItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(ScatterAll);

        _trayGravityItem = new Forms.ToolStripMenuItem("Gravity")
        {
            CheckOnClick = true,
            Checked = GravityEnabled
        };
        _trayGravityItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => SetGravity(_trayGravityItem.Checked));

        _trayClimbItem = new Forms.ToolStripMenuItem("Window Climbing & Landing")
        {
            CheckOnClick = true,
            Checked = WindowClimbingEnabled
        };
        _trayClimbItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => SetWindowClimbing(_trayClimbItem.Checked));

        _traySoundItem = new Forms.ToolStripMenuItem("Sound Effects")
        {
            CheckOnClick = true,
            Checked = SoundEnabled
        };
        _traySoundItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => SetSoundEnabled(_traySoundItem.Checked));

        var trayVolumeMenu = new Forms.ToolStripMenuItem("Volume");
        _trayVolumeSlider = new Forms.TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 25,
            SmallChange = 5,
            LargeChange = 25,
            Value = (int)Math.Round(SoundVolume * 100),
            Width = 130,
            AutoSize = false,
            Height = 32
        };
        _trayVolumeSlider.ValueChanged += (_, _) =>
        {
            double vol = _trayVolumeSlider.Value / 100.0;
            System.Windows.Application.Current.Dispatcher.Invoke(() => SetSoundVolume(vol));
        };
        trayVolumeMenu.DropDownItems.Add(new Forms.ToolStripControlHost(_trayVolumeSlider));

        _trayTopMostItem = new Forms.ToolStripMenuItem("Always on Top")
        {
            CheckOnClick = true,
            Checked = AlwaysOnTop
        };
        _trayTopMostItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => SetAlwaysOnTop(_trayTopMostItem.Checked));

        _trayClickThroughItem = new Forms.ToolStripMenuItem("Click-Through Mode")
        {
            CheckOnClick = true,
            Checked = ClickThrough
        };
        _trayClickThroughItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => SetClickThrough(_trayClickThroughItem.Checked));

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(ExitApplication);

        var aboutItem = new Forms.ToolStripMenuItem("About (Developed by Thovarisk)");
        aboutItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(ShowAboutDialog);

        var repoItem = new Forms.ToolStripMenuItem("GitHub Repo (Thovarisk)");
        repoItem.Click += (_, _) => OpenRepoUrl();

        menu.Items.Add(_trayAddItem);
        menu.Items.Add(_trayAddDogItem);
        menu.Items.Add(_trayAddSchnauzerItem);
        menu.Items.Add(_trayRemoveItem);
        menu.Items.Add(centerItem);
        menu.Items.Add(scatterItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_trayGravityItem);
        menu.Items.Add(_trayClimbItem);
        menu.Items.Add(_traySoundItem);
        menu.Items.Add(trayVolumeMenu);
        menu.Items.Add(_trayTopMostItem);
        menu.Items.Add(_trayClickThroughItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(repoItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateSheepTrayIcon(),
            Text = "KawaiiDesktopPets",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(CenterAll);

        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (_trayAddItem != null)
        {
            _trayAddItem.Text = $"Add Sheep ({_flock.Count}/{MaxSheep})";
            _trayAddItem.Enabled = _flock.Count < MaxSheep;
        }

        if (_trayAddDogItem != null)
        {
            _trayAddDogItem.Text = $"Add Dachshund ({_flock.Count}/{MaxSheep})";
            _trayAddDogItem.Enabled = _flock.Count < MaxSheep;
        }

        if (_trayAddSchnauzerItem != null)
        {
            _trayAddSchnauzerItem.Text = $"Add Schnauzer ({_flock.Count}/{MaxSheep})";
            _trayAddSchnauzerItem.Enabled = _flock.Count < MaxSheep;
        }

        if (_trayRemoveItem != null)
            _trayRemoveItem.Enabled = _flock.Count > 1;

        if (_trayGravityItem != null)
            _trayGravityItem.Checked = GravityEnabled;

        if (_trayClimbItem != null)
            _trayClimbItem.Checked = WindowClimbingEnabled;

        if (_traySoundItem != null)
            _traySoundItem.Checked = SoundEnabled;

        if (_trayVolumeSlider != null)
        {
            int target = (int)Math.Round(SoundVolume * 100);
            if (_trayVolumeSlider.Value != target)
                _trayVolumeSlider.Value = target;
        }

        if (_trayTopMostItem != null)
            _trayTopMostItem.Checked = AlwaysOnTop;

        if (_trayClickThroughItem != null)
            _trayClickThroughItem.Checked = ClickThrough;

        if (_trayIcon != null)
            _trayIcon.Text = _flock.Count == 1 ? "KawaiiDesktopPets (1 pet)" : $"KawaiiDesktopPets ({_flock.Count} pets)";
    }

    private static Drawing.Icon CreateSheepTrayIcon()
    {
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/sheep.ico", UriKind.Absolute));
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Drawing.Icon(stream, 32, 32);
            }
        }
        catch
        {
            // Fall back to drawn icon
        }

        using var bmp = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);

        using var woolBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 252, 248));
        using var faceBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(61, 56, 52));
        using var eyeBrush = new Drawing.SolidBrush(Drawing.Color.White);

        g.FillEllipse(woolBrush, 3, 8, 22, 18);
        g.FillEllipse(woolBrush, 7, 4, 14, 12);
        g.FillEllipse(faceBrush, 16, 10, 13, 12);
        g.FillEllipse(eyeBrush, 23, 13, 3, 3);

        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
