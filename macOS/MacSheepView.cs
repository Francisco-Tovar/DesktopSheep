using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace KawaiiDesktopPets;

public sealed class MacSheepView : UserControl
{
    private const int Columns = 8;
    private const int Rows = 5;

    private static readonly Lazy<Bitmap[,]> SheepFrames = new(() => LoadSpriteSheet("sheep_spritesheet_1024x640.png"));
    private static readonly Lazy<Bitmap[,]> DogFrames = new(() => LoadSpriteSheet("dachshound.png"));
    private static readonly Lazy<Bitmap[,]> SchnauzerFrames = new(() => LoadSpriteSheet("schnauzer.png"));
    private static readonly Lazy<Bitmap[,]> ChickFrames = new(() => LoadSpriteSheet("chick.png"));
    private static readonly Lazy<Bitmap[,]> CowFrames = new(() => LoadSpriteSheet("cow.png"));
    private static readonly Lazy<Bitmap[,]> TobyFrames = new(() => LoadSpriteSheet("toby.png"));
    private static readonly Lazy<Bitmap?> SantaHatBitmap = new(() => LoadBitmapResource("santa_hat.png"));

    private readonly Canvas _ufoGroup;
    private readonly Border _bubbleBorder;
    private readonly TextBlock _bubbleText;
    private readonly Ellipse _shadowEllipse;
    private readonly Image _spriteImage;
    private readonly Image _santaHatImage;
    private readonly ScaleTransform _flipTransform;
    private readonly TranslateTransform _bodyBobTransform;

    private PetKind _petKind = PetKind.Sheep;

    public MacSheepView()
    {
        Background = Brushes.Transparent;

        var root = new Grid { Width = 140, Height = 140 };

        _ufoGroup = BuildUfoGroup();
        _ufoGroup.ZIndex = 20;
        _ufoGroup.IsVisible = false;
        root.Children.Add(_ufoGroup);

        _bubbleText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = SolidColorBrush.Parse("#4A403A"),
            Text = "Baa!"
        };
        _bubbleBorder = new Border
        {
            ZIndex = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(9, 3),
            CornerRadius = new CornerRadius(12),
            Background = SolidColorBrush.Parse("#FFFDF9"),
            BorderBrush = SolidColorBrush.Parse("#D8CFC4"),
            BorderThickness = new Thickness(1.5),
            Child = _bubbleText,
            IsVisible = false,
            IsHitTestVisible = false
        };
        root.Children.Add(_bubbleBorder);

        _flipTransform = new ScaleTransform(1, 1);
        _bodyBobTransform = new TranslateTransform(0, 0);
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_flipTransform);
        transformGroup.Children.Add(_bodyBobTransform);

        var bodyGrid = new Grid
        {
            Width = 116,
            Height = 116,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = transformGroup
        };

        _shadowEllipse = new Ellipse
        {
            Width = 72,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            Fill = SolidColorBrush.Parse("#26000000"),
            IsHitTestVisible = false
        };
        bodyGrid.Children.Add(_shadowEllipse);

        var hitTarget = new Ellipse
        {
            Width = 96,
            Height = 92,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
            Fill = SolidColorBrush.Parse("#01000000")
        };
        bodyGrid.Children.Add(hitTarget);

        _spriteImage = new Image
        {
            Width = 116,
            Height = 116,
            Stretch = Stretch.Uniform,
            Source = SheepFrames.Value[0, 0]
        };
        bodyGrid.Children.Add(_spriteImage);

        var hatTransform = new TransformGroup();
        hatTransform.Children.Add(new ScaleTransform(-1, 1));
        hatTransform.Children.Add(new RotateTransform(-6));
        _santaHatImage = new Image
        {
            Source = SantaHatBitmap.Value,
            Width = 44,
            Height = 41,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = hatTransform,
            Stretch = Stretch.Uniform,
            IsVisible = false,
            IsHitTestVisible = false
        };
        bodyGrid.Children.Add(_santaHatImage);

        root.Children.Add(bodyGrid);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = root };
    }

    public void SetPetKind(PetKind kind)
    {
        _petKind = kind;
        _spriteImage.Source = GetFrames(kind)[0, 0];
    }

    private static Bitmap[,] GetFrames(PetKind kind) => kind switch
    {
        PetKind.Dog => DogFrames.Value,
        PetKind.Schnauzer => SchnauzerFrames.Value,
        PetKind.Chick => ChickFrames.Value,
        PetKind.Cow => CowFrames.Value,
        PetKind.Toby => TobyFrames.Value,
        _ => SheepFrames.Value
    };

    public void UpdateVisuals(SheepState state, double animTime, int facingDirection, bool isBlinking, string? bubbleMessage)
    {
        _flipTransform.ScaleX = facingDirection >= 0 ? 1 : -1;

        if (!string.IsNullOrEmpty(bubbleMessage) && state != SheepState.Abducted)
        {
            _bubbleText.Text = bubbleMessage;
            _bubbleBorder.IsVisible = true;
        }
        else
        {
            _bubbleBorder.IsVisible = false;
        }

        _ufoGroup.IsVisible = state == SheepState.Abducted;
        _shadowEllipse.Opacity = state is SheepState.Dragged or SheepState.Falling or SheepState.Climbing or SheepState.Abducted
            ? 0.0
            : 0.28;

        int row;
        int col;
        double bobY = 0;

        switch (state)
        {
            case SheepState.Walking:
                row = 1;
                col = (int)(animTime * 8.0) % 4;
                break;

            case SheepState.Running:
                row = 1;
                col = 4 + ((int)(animTime * 12.0) % 4);
                break;

            case SheepState.Sitting:
                row = 2;
                col = (int)(animTime * 2.5) % 2;
                break;

            case SheepState.Sleeping:
                row = 2;
                col = 2 + ((int)(animTime * 1.8) % 2);
                break;

            case SheepState.Yawning:
                row = 2;
                col = 4 + ((int)(animTime * 2.5) % 2);
                break;

            case SheepState.Eating:
                row = 2;
                col = 6 + ((int)(animTime * 5.0) % 2);
                break;

            case SheepState.Climbing:
                row = 3;
                col = (int)(animTime * 8.0) % 4;
                break;

            case SheepState.Dragged:
                row = 3;
                col = 4 + ((int)(animTime * 10.0) % 2);
                break;

            case SheepState.Falling:
                row = 3;
                col = 6 + ((int)(animTime * 8.0) % 2);
                break;

            case SheepState.Happy:
                row = 4;
                col = (int)(animTime * 8.0) % 4;
                break;

            case SheepState.Surprised:
                row = 4;
                col = 4 + ((int)(animTime * 8.0) % 2);
                break;

            case SheepState.Abducted:
                row = 4;
                col = 6 + ((int)(animTime * 6.0) % 2);
                bobY = -8 + Math.Sin(animTime * 4.5) * 4;
                break;

            default:
                row = 0;
                if (isBlinking)
                {
                    col = 4 + ((int)(animTime * 12.0) % 2);
                }
                else
                {
                    int cycle = (int)(animTime * 4.0) % 10;
                    col = cycle >= 8 ? 6 + (cycle - 8) : cycle % 4;
                }
                break;
        }

        _bodyBobTransform.Y = bobY;
        _spriteImage.Source = GetFrames(_petKind)[row, col];
        UpdateSantaHat(state);
    }

    private static bool IsSantaHatSeason()
    {
        var now = DateTime.Now;
        return now.Month == 12 || (now.Month == 11 && now.Day >= 20);
    }

    private void UpdateSantaHat(SheepState state)
    {
        if (!IsSantaHatSeason() || _santaHatImage.Source == null)
        {
            _santaHatImage.IsVisible = false;
            return;
        }

        (double cx, double topY) = state switch
        {
            SheepState.Sitting => _petKind switch
            {
                PetKind.Dog => (60.2, 15.4),
                PetKind.Schnauzer => (61.3, 16.3),
                PetKind.Chick => (63.5, 13.6),
                PetKind.Cow => (63.3, 19.9),
                PetKind.Toby => (64.1, 21.8),
                _ => (59.2, 9.1)
            },
            SheepState.Sleeping => _petKind switch
            {
                PetKind.Dog => (68.0, 46.0),
                PetKind.Schnauzer => (68.0, 46.0),
                PetKind.Chick => (70.0, 46.0),
                PetKind.Cow => (66.0, 42.0),
                PetKind.Toby => (68.0, 49.0),
                _ => (68.0, 44.0)
            },
            SheepState.Eating => _petKind switch
            {
                PetKind.Dog => (66.7, 40.8),
                PetKind.Schnauzer => (67.1, 37.2),
                PetKind.Chick => (66.6, 34.4),
                PetKind.Cow => (66.0, 32.0),
                PetKind.Toby => (64.8, 30.8),
                _ => (62.0, 37.2)
            },
            SheepState.Climbing => _petKind switch
            {
                PetKind.Dog => (56.2, 9.1),
                PetKind.Schnauzer => (55.4, 9.1),
                PetKind.Chick => (52.6, 10.0),
                PetKind.Cow => (57.5, 13.6),
                PetKind.Toby => (54.6, 15.4),
                _ => (54.8, 9.1)
            },
            _ => _petKind switch
            {
                PetKind.Dog => (66.5, 13.6),
                PetKind.Schnauzer => (69.3, 15.4),
                PetKind.Chick => (65.6, 9.1),
                PetKind.Cow => (74.6, 22.7),
                PetKind.Toby => (63.2, 24.5),
                _ => (63.1, 9.1)
            }
        };

        _santaHatImage.Margin = new Thickness(cx - 24.0, topY - 24.0, 0, 0);
        _santaHatImage.IsVisible = true;
    }

    private static Canvas BuildUfoGroup()
    {
        var canvas = new Canvas
        {
            Width = 120,
            Height = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };

        var beam = new Polygon
        {
            Points = [new Point(42, 26), new Point(78, 26), new Point(102, 108), new Point(18, 108)],
            Opacity = 0.32,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.Parse("#7BF5FF"), 0),
                    new GradientStop(Color.Parse("#007BF5FF"), 1)
                ]
            }
        };
        canvas.Children.Add(beam);

        var dome = new Ellipse
        {
            Width = 34,
            Height = 16,
            Fill = SolidColorBrush.Parse("#9AE8FF"),
            Stroke = SolidColorBrush.Parse("#4C8BA8"),
            StrokeThickness = 1.2,
            Opacity = 0.9
        };
        Canvas.SetLeft(dome, 43);
        Canvas.SetTop(dome, 8);
        canvas.Children.Add(dome);

        var saucer = new Ellipse
        {
            Width = 64,
            Height = 16,
            Fill = SolidColorBrush.Parse("#B8C6D4"),
            Stroke = SolidColorBrush.Parse("#586776"),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(saucer, 28);
        Canvas.SetTop(saucer, 16);
        canvas.Children.Add(saucer);

        void AddLight(double left, double top, string color)
        {
            var light = new Ellipse { Width = 6, Height = 4, Fill = SolidColorBrush.Parse(color) };
            Canvas.SetLeft(light, left);
            Canvas.SetTop(light, top);
            canvas.Children.Add(light);
        }

        AddLight(36, 22, "#FFE66D");
        AddLight(57, 23, "#FF6B8B");
        AddLight(78, 22, "#7BF5A3");
        return canvas;
    }

    private static Bitmap[,] LoadSpriteSheet(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Missing embedded sprite sheet: {resourceName}");

        using var sheet = SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException($"Failed to decode sprite sheet: {resourceName}");

        int cellW = sheet.Width / Columns;
        int cellH = sheet.Height / Rows;
        var result = new Bitmap[Rows, Columns];

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                using var cell = new SKBitmap(cellW, cellH, sheet.ColorType, sheet.AlphaType);
                sheet.ExtractSubset(cell, new SKRectI(c * cellW, r * cellH, (c + 1) * cellW, (r + 1) * cellH));
                using var image = SKImage.FromBitmap(cell);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var ms = new MemoryStream(data.ToArray());
                result[r, c] = new Bitmap(ms);
            }
        }

        return result;
    }

    private static Bitmap? LoadBitmapResource(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream != null ? new Bitmap(stream) : null;
    }
}
