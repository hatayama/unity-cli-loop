using System.Runtime.CompilerServices;

// Grants internals to a name no assembly in this project carries, so the shim reference copy tests
// can tell an assembly that grants internals to the edited one from an assembly that does not.
[assembly: InternalsVisibleTo("UloopShimReferenceFriendTarget")]
