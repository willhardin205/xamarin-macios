#r "_provisionator/provisionator.dll"

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Xamarin.Provisioning;
using Xamarin.Provisioning.Model;

using static Xamarin.Provisioning.ProvisioningScript;

// Provision Xcode
//
// Overrides:
// * The current commit can be overridden by setting the PROVISION_FROM_COMMIT variable.

var commit = Environment.GetEnvironmentVariable ("BUILD_SOURCEVERSION");
var provision_from_commit = Environment.GetEnvironmentVariable ("PROVISION_FROM_COMMIT") ?? commit;

// Dump all the Xcodes we have
Console.WriteLine ($"Xcodes:");
Exec ("bash", "-c", "ls -lad /Applications/Xcode*");

// These are the required Xcodes by our build.
var requiredXcodes = new List<XreItem> {
	XreItem.Xcode_10_2_0,
	XreItem.Xcode_9_4_0
};

// DevOps provides different naming conventions for Xcodes out of the box
// than the ones we have in provisionator and xamarin-macios, this ensures
// we have the right naming conventions for all.
var xcodesInfo = new Dictionary<XreItem, (string DevOpsName, string XIName, string ProvisionatorName)> {
	[XreItem.Xcode_10_2_0] = ("/Applications/Xcode_10.2.app", "/Applications/Xcode102.app", "/Applications/Xcode_10.2.0.app"),
	[XreItem.Xcode_10_1_0] = ("/Applications/Xcode_10.1.app", "/Applications/Xcode101.app", "/Applications/Xcode_10.1.0.app"),
	[XreItem.Xcode_10_0_0] = ("/Applications/Xcode_10.app", "/Applications/Xcode10.app", "/Applications/Xcode_10.0.0.app"),
	[XreItem.Xcode_9_4_1] = ("/Applications/Xcode_9.4.1.app", "/Applications/Xcode941.app", null), // Same naming convention as provisionator.
	[XreItem.Xcode_9_4_0] = ("/Applications/Xcode_9.4.app", "/Applications/Xcode94.app", "/Applications/Xcode_9.4.0.app"),
	[XreItem.Xcode_9_3_1] = ("/Applications/Xcode_9.3.1.app", "/Applications/Xcode931.app", null), // Same naming convention as provisionator.
	[XreItem.Xcode_9_3_0] = ("/Applications/Xcode_9.3.app", "/Applications/Xcode93.app", "/Applications/Xcode_9.3.0.app"),
	[XreItem.Xcode_9_2_0] = ("/Applications/Xcode_9.2.app", "/Applications/Xcode92.app", "/Applications/Xcode_9.2.0.app"),
	[XreItem.Xcode_9_1_0] = ("/Applications/Xcode_9.1.app", "/Applications/Xcode91.app", "/Applications/Xcode_9.1.0.app"),
	[XreItem.Xcode_9_0_0] = ("/Applications/Xcode_9.app", "/Applications/Xcode9.app", "/Applications/Xcode_9.0.0.app"),
	[XreItem.Xcode_9_0_0] = ("/Applications/Xcode_8.3.3.app", "/Applications/Xcode833.app", null), // Same naming convention as provisionator.
};

// We expect different naming than the provided one.
EnsureXcodeNaming (xcodesInfo);
EnsureRequiredXcodesExist (requiredXcodes, xcodesInfo);

void EnsureXcodeNaming (Dictionary<XreItem, (string DevOpsName, string XIName, string ProvisionatorName)> xcodes)
{
	foreach (var xcode in xcodes) {
		if (Directory.Exists (xcode.Value.DevOpsName)) {
			if (!Directory.Exists (xcode.Value.XIName))
				ln (xcode.Value.DevOpsName, xcode.Value.XIName);
			if (xcode.Value.ProvisionatorName != null && !Directory.Exists (xcode.Value.ProvisionatorName))
				ln (xcode.Value.DevOpsName, xcode.Value.ProvisionatorName);
		}
	}
}

void ln (string source, string destination)
{
	Console.WriteLine ($"ln -sf {source} {destination}");
	if (!Config.DryRun)
		Exec ("/bin/ln", "-sf", source, destination);
}

void EnsureRequiredXcodesExist (List<XreItem> neededXcodes, Dictionary<XreItem, (string DevOpsName, string XIName, string ProvisionatorName)> xcodeList)
{
	foreach (var xcode in neededXcodes) {
		var info = xcodeList[xcode];
		switch (xcode) {
		case XreItem.Xcode_9_4_0:
			// DevOps mojave image no longer includes Xcode 9.4.0 but does have 9.4.1
			// since we do not really care about minors and want to save time not provisioning 9.4.0
			// we symlink Xcode 9.4.1 to 9.4.0 if present.
			if (!Directory.Exists (info.XIName)) {
				var info941 = xcodeList[XreItem.Xcode_9_4_1];
				if (!Directory.Exists (info941.DevOpsName))
					goto default;
				ln (info941.DevOpsName, info.XIName);
				ln (info941.DevOpsName, info.ProvisionatorName);
				Console.WriteLine ($"\tRequired Xcode Found: {info.XIName}");
			} else
				goto default;
		break;
		default:
			if (!Directory.Exists (info.XIName)) {
				Item (xcode)
					.Action ((item) => ln (info.ProvisionatorName, info.XIName))
					.Action ((item) => ln (info.ProvisionatorName, info.DevOpsName))
					.Action ((item) => Console.WriteLine ($"\tRequired Xcode Downloaded: {info.XIName}"));
			} else
				Console.WriteLine ($"\tRequired Xcode Found: {info.XIName}");
		break;
		}
	}
}












// Looks for a variable either in the environment, or in current repo's Make.config.
// Returns null if the variable couldn't be found.
IEnumerable<string> make_config = null;
string FindConfigurationVariable (string variable, string hash = "HEAD")
{
	var value = Environment.GetEnvironmentVariable (variable);
	if (!string.IsNullOrEmpty (value))
		return value;

	if (make_config == null)
		make_config = Exec ("git", "show", $"{hash}:Make.config");
	foreach (var line in make_config) {
		if (line.StartsWith (variable + "=", StringComparison.Ordinal))
			return line.Substring (variable.Length + 1);
	}

	return null;
}

string FindVariable (string variable)
{
	var value = FindConfigurationVariable (variable, provision_from_commit);
	if (!string.IsNullOrEmpty (value))
		return value;

	throw new Exception ($"Could not find {variable} in environment nor in the commit's ({commit}) manifest.");
}

if (string.IsNullOrEmpty (provision_from_commit)) {
	Console.Error.WriteLine ($"Either BUILD_SOURCEVERSION or PROVISION_FROM_COMMIT must be set.");
	Environment.Exit (1);
	return 1;
}
Console.WriteLine ($"Provisioning Xcode from {provision_from_commit}...");

// Xcode
var xcode_path = Path.GetDirectoryName (Path.GetDirectoryName (FindVariable ("XCODE_DEVELOPER_ROOT")));
if (!Directory.Exists (xcode_path)) {
	// Provision
	var root = Path.GetDirectoryName (Path.GetDirectoryName (FindVariable ("XCODE_DEVELOPER_ROOT")));
	Console.WriteLine ($"Could not find an already installed Xcode in {root}, will download and install.");
	var xcode_provisionator_name = FindVariable ("XCODE_PROVISIONATOR_NAME");
	Xcode (xcode_provisionator_name).XcodeSelect ();
	Console.WriteLine ($"ln -Fhs /Applications/Xcode.app {root}");
	Exec ("ln", "-Fhs", "/Applications/Xcode.app", root);
	Exec ("ls", "-la", "/Applications");
} else {
	// We already have it, symlink into /Applications/Xcode.app
	Console.WriteLine ($"ln -Fhs {xcode_path} /Applications/Xcode.app");
	Exec ("ln", "-Fhs", xcode_path, "/Applications/Xcode.app");
}
