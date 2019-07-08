#load "utils.csx"

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Xamarin.Provisioning;
using Xamarin.Provisioning.Model;

// Provision Xcode
//
// Overrides:
// * The current commit can be overridden by setting the PROVISION_FROM_COMMIT variable.

var commit = Environment.GetEnvironmentVariable ("BUILD_SOURCEVERSION");
var provision_from_commit = Environment.GetEnvironmentVariable ("PROVISION_FROM_COMMIT") ?? commit;

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
