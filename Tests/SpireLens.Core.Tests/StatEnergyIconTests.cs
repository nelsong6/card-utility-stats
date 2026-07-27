using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public sealed class StatEnergyIconTests
{
    [Fact]
    public void GetPathForPrefix_DefaultsToIronclad()
    {
        Assert.Equal(
            "res://images/packed/sprite_fonts/ironclad_energy_icon.png",
            StatEnergyIcon.GetPathForPrefix(null));
    }

    [Fact]
    public void GetPathForPrefix_UsesCharacterPrefix()
    {
        Assert.Equal(
            "res://images/packed/sprite_fonts/necrobinder_energy_icon.png",
            StatEnergyIcon.GetPathForPrefix("Necrobinder"));
    }
}
