using System.Buffers.Binary;
using System.IO;
using System.Media;

namespace KawaiiDesktopPets;

internal static class SoundEffects
{
    public static bool Enabled { get; set; } = true;
    public static double Volume { get; set; } = 0.10;

    private static readonly Lazy<byte[]?> SheepBaa = LoadResourceWav("sheep_baa.wav");
    private static readonly Lazy<byte[]?> SheepHappy = LoadResourceWav("sheep_happy.wav");
    private static readonly Lazy<byte[]?> DachshundWoof = LoadResourceWav("dachshund_woof.wav");
    private static readonly Lazy<byte[]?> DachshundHappy = LoadResourceWav("dachshund_happy.wav");
    private static readonly Lazy<byte[]?> SchnauzerBark = LoadResourceWav("schnauzer_bark.wav");
    private static readonly Lazy<byte[]?> SchnauzerHappy = LoadResourceWav("schnauzer_happy.wav");
    private static readonly Lazy<byte[]?> ChickChirp = LoadResourceWav("chick_chirp.wav");
    private static readonly Lazy<byte[]?> ChickHappy = LoadResourceWav("chick_happy.wav");
    private static readonly Lazy<byte[]?> CowMoo = LoadResourceWav("cow_moo.wav");
    private static readonly Lazy<byte[]?> CowHappy = LoadResourceWav("cow_happy.wav");
    private static readonly Lazy<byte[]?> CatMeow = LoadResourceWav("cat_meow.wav");
    private static readonly Lazy<byte[]?> CatHappy = LoadResourceWav("cat_happy.wav");
    private static readonly Lazy<byte[]?> PetEat = LoadResourceWav("pet_eat.wav");
    private static readonly Lazy<byte[]?> PetBoing = LoadResourceWav("pet_boing.wav");
    private static readonly Lazy<byte[]?> PetYawn = LoadResourceWav("pet_yawn.wav");
    private static readonly Lazy<byte[]?> UfoBeam = LoadResourceWav("ufo_beam.wav");
    private static readonly Lazy<byte[]?> BossRoar = LoadResourceWav("boss_roar.wav");
    private static readonly Lazy<byte[]?> BossHit = LoadResourceWav("boss_hit.wav");
    private static readonly Lazy<byte[]?> BossDefeat = LoadResourceWav("boss_defeat.wav");

    public static void PlayVoice(PetKind kind) => PlayBytes(kind switch
    {
        PetKind.Dog => DachshundWoof.Value,
        PetKind.Schnauzer => SchnauzerBark.Value,
        PetKind.Chick => ChickChirp.Value,
        PetKind.Cow => CowMoo.Value,
        PetKind.Toby => CatMeow.Value,
        _ => SheepBaa.Value
    });

    public static void PlayHappy(PetKind kind) => PlayBytes(kind switch
    {
        PetKind.Dog => DachshundHappy.Value,
        PetKind.Schnauzer => SchnauzerHappy.Value,
        PetKind.Chick => ChickHappy.Value,
        PetKind.Cow => CowHappy.Value,
        PetKind.Toby => CatHappy.Value,
        _ => SheepHappy.Value
    });

    public static void PlayBoing() => PlayBytes(PetBoing.Value);
    public static void PlayNom() => PlayBytes(PetEat.Value);
    public static void PlayYawn() => PlayBytes(PetYawn.Value);
    public static void PlayUfo() => PlayBytes(UfoBeam.Value);
    public static void PlayBossRoar() => PlayBytes(BossRoar.Value);
    public static void PlayBossHit() => PlayBytes(BossHit.Value);
    public static void PlayBossDefeat() => PlayBytes(BossDefeat.Value);

    public static void PlayWavFile(string path)
    {
        if (!Enabled || Volume <= 0.001)
            return;

        try
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            PlayBytes(fileBytes);
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
        if (!Enabled || Volume <= 0.001 || wavData == null || wavData.Length == 0)
            return;

        try
        {
            byte[] scaled = Volume >= 0.999 ? wavData : ScalePcm16Volume(wavData, Math.Clamp(Volume, 0.0, 1.0));
            var stream = new MemoryStream(scaled, writable: false);
            using var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Ignore audio device failures
        }
    }

    private static byte[] ScalePcm16Volume(byte[] wavData, double volume)
    {
        if (wavData.Length < 44)
            return wavData;

        // Verify RIFF....WAVE and PCM format (audioFormat == 1, bitsPerSample == 16)
        if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F'
            || wavData[8] != 'W' || wavData[9] != 'A' || wavData[10] != 'V' || wavData[11] != 'E')
            return wavData;

        int pos = 12;
        short audioFormat = 0;
        short bitsPerSample = 0;
        int dataOffset = -1;
        int dataSize = 0;

        while (pos + 8 <= wavData.Length)
        {
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(wavData.AsSpan(pos + 4, 4));
            if (wavData[pos] == 'f' && wavData[pos + 1] == 'm' && wavData[pos + 2] == 't' && wavData[pos + 3] == ' ')
            {
                if (pos + 24 <= wavData.Length)
                {
                    audioFormat = BinaryPrimitives.ReadInt16LittleEndian(wavData.AsSpan(pos + 8, 2));
                    bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(wavData.AsSpan(pos + 22, 2));
                }
            }
            else if (wavData[pos] == 'd' && wavData[pos + 1] == 'a' && wavData[pos + 2] == 't' && wavData[pos + 3] == 'a')
            {
                dataOffset = pos + 8;
                dataSize = (int)Math.Min(chunkSize, (uint)(wavData.Length - dataOffset));
                break;
            }

            int advance = 8 + (int)chunkSize + ((chunkSize & 1) == 1 ? 1 : 0);
            if (advance <= 8)
                break;
            pos += advance;
        }

        if (audioFormat != 1 || bitsPerSample != 16 || dataOffset < 0 || dataSize < 2)
            return wavData;

        byte[] copy = (byte[])wavData.Clone();
        int end = dataOffset + (dataSize & ~1);
        for (int i = dataOffset; i < end; i += 2)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(i, 2));
            short scaled = (short)Math.Clamp((int)Math.Round(sample * volume), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(i, 2), scaled);
        }

        return copy;
    }
}
