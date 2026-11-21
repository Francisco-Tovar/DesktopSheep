namespace KawaiiDesktopPets;

public enum PetActivity
{
    Walking = 0,
    Idle = 1,
    Sleeping = 2,
    Dashing = 3,
    Eating = 4,
    Ufo = 5
}

internal sealed class ActivityMix
{
    public const int Count = 6;

    public static readonly string[] Labels =
    [
        "Walking",
        "Idle",
        "Sleeping",
        "Dashing",
        "Eating",
        "UFO"
    ];

    private static readonly int[] DefaultWeights = [40, 28, 14, 10, 6, 2];

    private readonly int[] _weights = (int[])DefaultWeights.Clone();

    public int GetPercentage(int index) => _weights[index];

    public void ResetDefaults()
    {
        Array.Copy(DefaultWeights, _weights, Count);
    }

    public void SetPercentage(int index, int newValue)
    {
        if (index < 0 || index >= Count)
            return;

        int clamped = Math.Clamp(newValue, 0, 100);
        if (_weights[index] == clamped)
            return;

        _weights[index] = clamped;
        int targetOtherSum = 100 - clamped;

        int currentOtherSum = 0;
        for (int i = 0; i < Count; i++)
        {
            if (i != index)
                currentOtherSum += _weights[i];
        }

        if (currentOtherSum == 0)
        {
            int perOther = targetOtherSum / (Count - 1);
            int rem = targetOtherSum % (Count - 1);
            for (int i = 0; i < Count; i++)
            {
                if (i == index)
                    continue;
                _weights[i] = perOther + (rem > 0 ? 1 : 0);
                if (rem > 0)
                    rem--;
            }
            return;
        }

        var remainders = new (int Index, double Frac)[Count - 1];
        int slot = 0;
        int allocated = 0;
        for (int i = 0; i < Count; i++)
        {
            if (i == index)
                continue;

            double exact = _weights[i] * (targetOtherSum / (double)currentOtherSum);
            int floored = (int)Math.Floor(exact);
            _weights[i] = floored;
            allocated += floored;
            remainders[slot++] = (i, exact - floored);
        }

        int leftover = targetOtherSum - allocated;
        if (leftover > 0)
        {
            Array.Sort(remainders, (a, b) => b.Frac.CompareTo(a.Frac));
            for (int k = 0; k < leftover && k < remainders.Length; k++)
                _weights[remainders[k].Index]++;
        }
    }

    public PetActivity RollActivity(Random rng)
    {
        int roll = rng.Next(100);
        int cumulative = 0;
        for (int i = 0; i < Count; i++)
        {
            cumulative += _weights[i];
            if (roll < cumulative)
                return (PetActivity)i;
        }
        return PetActivity.Walking;
    }
}
