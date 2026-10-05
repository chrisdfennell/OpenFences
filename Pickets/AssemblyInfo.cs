using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// Lets the test project reach internal helpers (rules parsing, snapping math, repair).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Pickets.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Pickets.UiTests")]
// …and the screenshot tool, which shows system monitors with made-up readings.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Screenshots")]
