using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash.ConsoleApp;

public class ScriptReadyAsyncHandlerTest
{
	public async Task TestScriptReadyAsyncHandler()
	{
		var bashScript = await BashScript.CreateAsync(
			bashScriptSettings: BashScriptSettings.CreateFactoryDefault(
				AbsolutePath.Create("functions.sh"))
			).ConfigureAwait(false);
		bashScript.EventHandlerScriptSourcedAsync += async (sender, e) => {
			// e.BashProcess
			// e.StreamInputWriter
			// e.StreamOutputReader
		};
	} 
}
