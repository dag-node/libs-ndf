using NDF.Interoperability.Model;
using NDF.Interoperability.Model.Bash;

namespace NDF.Interoperability.Bash.ConsoleApp;

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
