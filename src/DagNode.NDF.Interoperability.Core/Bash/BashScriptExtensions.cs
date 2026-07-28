using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash;

public static class BashScriptExtensions
{
	public static BashScriptSettings SetDebug(this BashScriptSettings self, bool isDebugLoggingEnabled = false)
	{
		self.IsDebug = isDebugLoggingEnabled;
		return self;
	}
}
