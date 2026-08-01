namespace DagNode.NDF.Interoperability.Tests;

/// <summary>
/// Marks a test that needs a real bash. Where bash is absent — a Windows run of the same
/// solution — the test reports inconclusive with the reason naming what is missing, so the run
/// stays green and the gap stays visible.
/// </summary>
public static class BashRequirement
{
	/// <summary>Ends the calling test as inconclusive unless a real bash is available.</summary>
	public static void SkipUnlessAvailable()
	{
		if (!BashHost.IsAvailable)
			Assert.Inconclusive($"Requires bash at {BashHost.BashPath} on Linux");
	}
}
