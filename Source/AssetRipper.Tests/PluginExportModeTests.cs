using AssetRipper.Export.Configuration;

namespace AssetRipper.Tests;

public class PluginExportModeTests
{
	[Test]
	public void PluginExportMode_DefaultValue_IsFromGame()
	{
		ExportSettings settings = new();
		Assert.That(settings.PluginExportMode, Is.EqualTo(PluginExportMode.FromGame));
	}

	[Test]
	public void PluginExportMode_CanBeSetToSkip()
	{
		ExportSettings settings = new() { PluginExportMode = PluginExportMode.Skip };
		Assert.That(settings.PluginExportMode, Is.EqualTo(PluginExportMode.Skip));
	}

	[Test]
	public void PluginExportMode_CanBeSetToFromGame()
	{
		ExportSettings settings = new() { PluginExportMode = PluginExportMode.FromGame };
		Assert.That(settings.PluginExportMode, Is.EqualTo(PluginExportMode.FromGame));
	}

	[Test]
	public void PluginExportMode_FromGame_HasValueZero()
	{
		Assert.That((int)PluginExportMode.FromGame, Is.EqualTo(0));
	}

	[Test]
	public void PluginExportMode_Skip_HasValueOne()
	{
		Assert.That((int)PluginExportMode.Skip, Is.EqualTo(1));
	}

	[Test]
	public void PluginExportMode_EnumHasExactlyTwoMembers()
	{
		Array values = Enum.GetValues(typeof(PluginExportMode));
		Assert.That(values.Length, Is.EqualTo(2));
		Assert.That(values, Contains.Item(PluginExportMode.FromGame));
		Assert.That(values, Contains.Item(PluginExportMode.Skip));
	}

	[Test]
	public void ExportSettings_PluginExportMode_RetainsValueAfterSet()
	{
		ExportSettings settings = new() { PluginExportMode = PluginExportMode.Skip };
		ExportSettings copy = settings with { };
		Assert.That(copy.PluginExportMode, Is.EqualTo(PluginExportMode.Skip));
	}
}