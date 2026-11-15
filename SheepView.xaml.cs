using System.Windows;
using System.Windows.Media.Imaging;

namespace KawaiiDesktopPets;

public partial class SheepView : System.Windows.Controls.UserControl
{
    private const int CellWidth = 128;
    private const int CellHeight = 128;
    private const int Columns = 8;
    private const int Rows = 5;

    private static readonly Lazy<BitmapSource[,]> SheepFrames = new(() => LoadSpriteSheet("sheep_spritesheet_1024x640.png"));
    private static readonly Lazy<BitmapSource[,]> DogFrames = new(() => LoadSpriteSheet("Dog_spritesheet_1024x640.png"));
    private static readonly Lazy<BitmapSource[,]> SchnauzerFrames = new(() => LoadSpriteSheet("Schnauzer_spritesheet_1024x640.png"));

    private PetKind _petKind = PetKind.Sheep;

    public SheepView()
    {
        InitializeComponent();
        SpriteImage.Source = SheepFrames.Value[0, 0];
    }

    public void SetPetKind(PetKind kind)
    {
        _petKind = kind;
        SpriteImage.Source = GetFrames(kind)[0, 0];
    }

    private static BitmapSource[,] GetFrames(PetKind kind) => kind switch
    {
        PetKind.Dog => DogFrames.Value,
        PetKind.Schnauzer => SchnauzerFrames.Value,
        _ => SheepFrames.Value
    };

    public void SetRibbonVariant(int index)
    {
        // Sprite sheet uses its baked-in pastel pink bow.
    }

    public void UpdateVisuals(SheepState state, double animTime, int facingDirection, bool isBlinking, string? bubbleMessage)
    {
        FlipTransform.ScaleX = facingDirection >= 0 ? 1 : -1;

        if (!string.IsNullOrEmpty(bubbleMessage) && state != SheepState.Abducted)
        {
            BubbleText.Text = bubbleMessage;
            BubbleBorder.Visibility = Visibility.Visible;
        }
        else
        {
            BubbleBorder.Visibility = Visibility.Collapsed;
        }

        UfoGroup.Visibility = state == SheepState.Abducted ? Visibility.Visible : Visibility.Collapsed;
        ShadowEllipse.Opacity = state is SheepState.Dragged or SheepState.Falling or SheepState.Climbing or SheepState.Abducted
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

        BodyBobTransform.Y = bobY;
        SpriteImage.Source = GetFrames(_petKind)[row, col];
    }

    private static BitmapSource[,] LoadSpriteSheet(string resourceName)
    {
        var uri = new Uri($"pack://application:,,,/{resourceName}", UriKind.Absolute);
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = uri;
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.EndInit();
        sheet.Freeze();

        var frames = new BitmapSource[Rows, Columns];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                var rect = new Int32Rect(c * CellWidth, r * CellHeight, CellWidth, CellHeight);
                var cropped = new CroppedBitmap(sheet, rect);
                cropped.Freeze();
                frames[r, c] = cropped;
            }
        }
        return frames;
    }
}
