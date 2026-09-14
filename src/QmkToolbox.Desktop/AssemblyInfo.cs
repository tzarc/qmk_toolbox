using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("QmkToolbox.Tests")]

[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "qmk_toolbox is the shipped executable name")]
