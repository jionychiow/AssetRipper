using AssetRipper.Export.UnityProjects.Scripts;

namespace AssetRipper.Tests;

internal class DecompiledCodePostProcessorTests
{
	[Test]
	public static void AccessModifierFix_FixesPublicOverrideDisposeToProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "public override void Dispose(bool disposing)\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out string fixRecord), Is.True);
		Assert.That(fixedContent, Does.Contain("protected override void Dispose(bool disposing)"));
		Assert.That(fixedContent, Does.Not.Contain("public override void Dispose(bool disposing)"));
		Assert.That(fixRecord, Is.Not.Empty);
	}

	[Test]
	public static void AccessModifierFix_DoesNotFixAlreadyProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "protected override void Dispose(bool disposing)\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void AccessModifierFix_EmptyContentReturnsFalse()
	{
		AccessModifierFixRule rule = new();
		Assert.That(rule.TryFix("", out _, out _), Is.False);
	}

	[Test]
	public static void UsingDirectiveFix_RemovesUnnecessaryUsingUnityEngineUI()
	{
		UsingDirectiveFixRule rule = new();
		string input = "using UnityEngine;\nusing UnityEngine.UI;\n\npublic class Test\n{\n\tpublic int Value;\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out string fixRecord), Is.True);
		Assert.That(fixedContent, Does.Not.Contain("using UnityEngine.UI;"));
		Assert.That(fixRecord, Does.Contain("Removed"));
	}

	[Test]
	public static void UsingDirectiveFix_KeepsNecessaryUsingUnityEngineUI()
	{
		UsingDirectiveFixRule rule = new();
		string input = "using UnityEngine;\nusing UnityEngine.UI;\n\npublic class Test\n{\n\tprivate Text label;\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void UsingDirectiveFix_DoesNotProcessWhenUsingAbsent()
	{
		UsingDirectiveFixRule rule = new();
		string input = "using UnityEngine;\n\npublic class Test\n{\n\tpublic int Value;\n}";
		Assert.That(rule.TryFix(input, out _, out _), Is.False);
	}

	[Test]
	public static void UsingDirectiveFix_EmptyContentReturnsFalse()
	{
		UsingDirectiveFixRule rule = new();
		Assert.That(rule.TryFix("", out _, out _), Is.False);
	}

	[Test]
	public static void DuplicateAttributeFix_RemovesDuplicateNonSerialized()
	{
		DuplicateAttributeFixRule rule = new();
		string input = "[NonSerialized]\n[NonSerialized]\npublic int Value;";
		Assert.That(rule.TryFix(input, out string fixedContent, out string fixRecord), Is.True);
		Assert.That(fixedContent, Does.Contain("[NonSerialized]"));
		int nonSerializedCount = fixedContent.Split("[NonSerialized]").Length - 1;
		Assert.That(nonSerializedCount, Is.EqualTo(1));
		Assert.That(fixRecord, Does.Contain("Removed 1"));
	}

	[Test]
	public static void DuplicateAttributeFix_DoesNotRemoveWhenNotDuplicate()
	{
		DuplicateAttributeFixRule rule = new();
		string input = "[NonSerialized]\npublic int Value;";
		Assert.That(rule.TryFix(input, out _, out _), Is.False);
	}

	[Test]
	public static void DuplicateAttributeFix_DoesNotRemoveRepeatableAttributes()
	{
		DuplicateAttributeFixRule rule = new();
		string input = "[Conditional(\"DEBUG\")]\n[Conditional(\"TRACE\")]\npublic void Method()";
		Assert.That(rule.TryFix(input, out _, out _), Is.False);
	}

	[Test]
	public static void DuplicateAttributeFix_EmptyContentReturnsFalse()
	{
		DuplicateAttributeFixRule rule = new();
		Assert.That(rule.TryFix("", out _, out _), Is.False);
	}

	[Test]
	public static void PostProcessStatistics_MergeAggregatesCorrectly()
	{
		PostProcessStatistics stats1 = new();
		stats1.AddProcessedFile();
		stats1.AddProcessedFile();
		stats1.AddFix("AccessModifierFix");

		PostProcessStatistics stats2 = new();
		stats2.AddProcessedFile();
		stats2.AddFix("UsingDirectiveFix");
		stats2.AddFix("AccessModifierFix");

		stats1.Merge(stats2);
		Assert.That(stats1.ProcessedFileCount, Is.EqualTo(3));
		Assert.That(stats1.FixedFileCount, Is.EqualTo(3));
		Assert.That(stats1.FixRecordsByRule["AccessModifierFix"], Is.EqualTo(2));
		Assert.That(stats1.FixRecordsByRule["UsingDirectiveFix"], Is.EqualTo(1));
	}

	[Test]
	public static void AccessModifierFix_DeterministicOutput()
	{
		AccessModifierFixRule rule = new();
		string input = "public override void Dispose(bool disposing)\n{\n}";
		rule.TryFix(input, out string result1, out _);
		rule.TryFix(input, out string result2, out _);
		Assert.That(result2, Is.EqualTo(result1));
	}

	[Test]
	public static void AccessModifierFix_DoesNotChangePublicReadToProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "public override int Read(byte[] buffer, int offset, int count)\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void AccessModifierFix_DoesNotChangePublicWriteToProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "public override void Write(byte[] buffer, int offset, int count)\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void AccessModifierFix_DoesNotChangePublicSeekToProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "public override long Seek(long offset, SeekOrigin origin)\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void AccessModifierFix_DoesNotChangePublicFlushToProtected()
	{
		AccessModifierFixRule rule = new();
		string input = "public override void Flush()\n{\n}";
		Assert.That(rule.TryFix(input, out string fixedContent, out _), Is.False);
		Assert.That(fixedContent, Is.EqualTo(input));
	}

	[Test]
	public static void UsingDirectiveFix_DeterministicOutput()
	{
		UsingDirectiveFixRule rule = new();
		string input = "using UnityEngine;\nusing UnityEngine.UI;\n\npublic class Test\n{\n\tpublic int Value;\n}";
		rule.TryFix(input, out string result1, out _);
		rule.TryFix(input, out string result2, out _);
		Assert.That(result2, Is.EqualTo(result1));
	}

	[Test]
	public static void DuplicateAttributeFix_DeterministicOutput()
	{
		DuplicateAttributeFixRule rule = new();
		string input = "[NonSerialized]\n[NonSerialized]\npublic int Value;";
		rule.TryFix(input, out string result1, out _);
		rule.TryFix(input, out string result2, out _);
		Assert.That(result2, Is.EqualTo(result1));
	}
}
