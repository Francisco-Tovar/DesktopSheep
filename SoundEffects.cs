using System.IO;
using System.Media;

namespace KawaiiDesktopPets;

internal static class SoundEffects
{
    public static bool Enabled { get; set; } = true;

    private static readonly Lazy<byte[]?> SheepBaa = LoadResourceWav("sheep_baa.wav");
    private static readonly Lazy<byte[]?> SheepHappy = LoadResourceWav("sheep_happy.wav");
    private static readonly Lazy<byte[]?> DachshundWoof = LoadResourceWav("dachshund_woof.wav");
    private static readonly Lazy<byte[]?> DachshundHappy = LoadResourceWav("dachshund_happy.wav");
    private static readonly Lazy<byte[]?> SchnauzerBark = LoadResourceWav("schnauzer_bark.wav");
    private static readonly Lazy<byte[]?> SchnauzerHappy = LoadResourceWav("schnauzer_happy.wav");
    private static readonly Lazy<byte[]?> PetEat = LoadResourceWav("pet_eat.wav");
    private static readonly Lazy<byte[]?> PetBoing = LoadResourceWav("pet_boing.wav");
    private static readonly Lazy<byte[]?> PetYawn = LoadResourceWav("pet_yawn.wav");
    private static readonly Lazy<byte[]?> UfoBeam = LoadResourceWav("ufo_beam.wav");

    public static void PlayVoice(PetKind kind) => PlayBytes(kind switch
    {
        PetKind.Dog => DachshundWoof.Value,
        PetKind.Schnauzer => SchnauzerBark.Value,
        _ => SheepBaa.Value
    });

    public static void PlayHappy(PetKind kind) => PlayBytes(kind switch
    {
        PetKind.Dog => DachshundHappy.Value,
        PetKind.Schnauzer => SchnauzerHappy.Value,
        _ => SheepHappy.Value
    });

    public static void PlayBoing() => PlayBytes(PetBoing.Value);
    public static void PlayNom() => PlayBytes(PetEat.Value);
    public static void PlayYawn() => PlayBytes(PetYawn.Value);
    public static void PlayUfo() => PlayBytes(UfoBeam.Value);

    public static void PlayWavFile(string path)
    {
        if (!Enabled)
            return;

        try
        {
            using var player = new SoundPlayer(path);
            player.Play();
        }
        catch
        {
            // Ignore invalid or unsupported audio files
        }
    }

    private static Lazy<byte[]?> LoadResourceWav(string fileName) => new(() =>
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/KawaiiDesktopPets_SFX/{fileName}", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo == null)
                return null;

            using var source = streamInfo.Stream;
            using var ms = new MemoryStream();
            source.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    });

    private static void PlayBytes(byte[]? wavData)
    {
        if (!Enabled || wavData == null || wavData.Length == 0)
            return;

        try
        {
            var stream = new MemoryStream(wavData, writable: false);
            using var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Ignore audio device failures
        }
    }
}
