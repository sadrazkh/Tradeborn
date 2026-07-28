using Tradeborn.Domain.Common;

namespace Tradeborn.UnitTests;

public class MoneyTests
{
    [Fact]
    public void Coins_convert_to_cent_exactly()
    {
        Assert.Equal(80_000, Money.FromCoins(800).Cent);
        Assert.Equal(800, Money.FromCoins(800).Coins);
    }

    [Fact]
    public void Debit_reduces_the_balance()
    {
        var balance = Money.FromCoins(800);
        Assert.Equal(Money.FromCoins(650), balance.Debit(Money.FromCoins(150)));
    }

    [Fact]
    public void Debit_beyond_the_balance_throws_rather_than_going_negative()
    {
        var balance = Money.FromCoins(100);

        var error = Assert.Throws<InsufficientFundsException>(() => balance.Debit(Money.FromCoins(150)));

        Assert.Equal(balance, error.Balance);
        Assert.Equal(Money.FromCoins(150), error.Attempted);
    }

    [Fact]
    public void Debit_of_the_entire_balance_is_allowed()
    {
        Assert.Equal(Money.Zero, Money.FromCoins(150).Debit(Money.FromCoins(150)));
    }

    [Fact]
    public void Debiting_a_negative_amount_is_rejected()
    {
        // Otherwise "debit -1000" would be a way to mint money.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.FromCoins(10).Debit(Money.FromCent(-100_000)));
    }

    [Fact]
    public void Overflow_is_checked_rather_than_wrapping()
    {
        // A wrapped balance is a duplicated fortune; it must throw, not silently invert.
        Assert.Throws<OverflowException>(() => Money.FromCoins(long.MaxValue / 2));
    }

    [Fact]
    public void Arithmetic_is_exact_over_many_small_operations()
    {
        // The floating-point equivalent of this loop does not land on exactly 1000.00.
        var total = Money.Zero;
        for (var i = 0; i < 10_000; i++)
        {
            total += Money.FromCent(10);
        }

        Assert.Equal(100_000, total.Cent);
        Assert.Equal(1_000, total.Coins);
    }

    [Fact]
    public void CanAfford_matches_what_Debit_permits()
    {
        var balance = Money.FromCoins(150);

        Assert.True(balance.CanAfford(Money.FromCoins(150)));
        Assert.False(balance.CanAfford(Money.FromCoins(151)));
    }
}
