namespace Tradeborn.Domain.Common;

/// <summary>
/// An exact monetary amount, stored as an integer number of <b>cent</b> (1 coin = 100 cent).
/// </summary>
/// <remarks>
/// <para>
/// Per docs/economy/ECONOMY_DESIGN.md §1, no floating-point type may participate in a
/// balance. Every economic mutation in Tradeborn is integer addition or subtraction, which
/// is what makes the economy exactly reproducible — and therefore what makes the
/// determinism test in docs/testing/TEST_STRATEGY.md §2 possible at all.
/// </para>
/// <para>
/// There is deliberately no implicit conversion from <see cref="double"/> or
/// <see cref="decimal"/>. Creating money from a floating-point value must be a visible,
/// deliberate act.
/// </para>
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    public const long CentPerCoin = 100;

    public long Cent { get; }

    private Money(long cent) => Cent = cent;

    public static Money Zero => new(0);

    public static Money FromCent(long cent) => new(cent);

    public static Money FromCoins(long coins)
    {
        // A balance is a long; coins * 100 can overflow only at absurd magnitudes, but an
        // overflowed balance is a duplicated fortune, so it is checked rather than trusted.
        checked
        {
            return new Money(coins * CentPerCoin);
        }
    }

    /// <summary>Whole coins, truncated. For display only — never for arithmetic.</summary>
    public long Coins => Cent / CentPerCoin;

    public bool IsZero => Cent == 0;
    public bool IsPositive => Cent > 0;

    public static Money operator +(Money left, Money right)
    {
        checked
        {
            return new Money(left.Cent + right.Cent);
        }
    }

    public static Money operator -(Money left, Money right)
    {
        checked
        {
            return new Money(left.Cent - right.Cent);
        }
    }

    public static Money operator *(Money value, long factor)
    {
        checked
        {
            return new Money(value.Cent * factor);
        }
    }

    public static bool operator >(Money left, Money right) => left.Cent > right.Cent;
    public static bool operator <(Money left, Money right) => left.Cent < right.Cent;
    public static bool operator >=(Money left, Money right) => left.Cent >= right.Cent;
    public static bool operator <=(Money left, Money right) => left.Cent <= right.Cent;

    /// <summary>
    /// Subtracts <paramref name="amount"/>, throwing if the result would be negative.
    /// </summary>
    /// <remarks>
    /// This is the last line of defence against a negative balance. Request validation runs
    /// earlier and gives a friendlier error, but the domain does not rely on it — a bug in
    /// a handler must fail loudly here rather than quietly mint money.
    /// </remarks>
    /// <exception cref="InsufficientFundsException">The balance would go negative.</exception>
    public Money Debit(Money amount)
    {
        if (amount.Cent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot debit a negative amount.");
        }

        var result = Cent - amount.Cent;
        if (result < 0)
        {
            throw new InsufficientFundsException(this, amount);
        }

        return new Money(result);
    }

    public bool CanAfford(Money cost) => Cent >= cost.Cent;

    public int CompareTo(Money other) => Cent.CompareTo(other.Cent);

    public override string ToString() =>
        $"{Cent / CentPerCoin}.{Math.Abs(Cent % CentPerCoin):D2}";
}

public sealed class InsufficientFundsException : Exception
{
    public InsufficientFundsException(Money balance, Money attempted)
        : base($"Insufficient funds: balance {balance}, attempted to debit {attempted}.")
    {
        Balance = balance;
        Attempted = attempted;
    }

    public Money Balance { get; }
    public Money Attempted { get; }
}
