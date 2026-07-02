using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Allow the test project to exercise internal image-preprocessing logic during the Accord removal.
[assembly: InternalsVisibleTo("InventoryKamera.Tests")]

// GenerateAssemblyInfo is disabled below (this file is the source of assembly metadata instead), so
// the SDK's usual auto-generated [assembly: SupportedOSPlatform] for a "windows"-suffixed TFM never
// gets emitted. Without it, the CA1416 platform-compatibility analyzer has no assembly-wide baseline
// to check WinForms/GDI+ call sites against, so it warns on nearly every one of them even though this
// app has always been Windows-only. Declaring it here matches what TargetFramework net8.0-windows7.0
// would have generated automatically.
[assembly: SupportedOSPlatform("windows7.0")]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Inventory Kamera")]
[assembly: AssemblyDescription("An OCR scanner for Genshin Impact")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Andrew De La Fuente")]
[assembly: AssemblyProduct("InventoryKamera")]
[assembly: AssemblyCopyright("Copyright © 2024")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("7b7f907e-11f4-4000-b711-8e532a36f8a9")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.4.3.*")]
//[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: NeutralResourcesLanguage("en")]