// Utf8Strings is Veritas's pooled factory for Utf8String (Lumoin.Base.Utf8String is pool-only, with no heap
// factory). This alias resolves the pervasive unqualified Utf8Strings name without a per-file using. It targets a
// Lumoin.Veritas.Core type, so Directory.Build.props compiles it only into projects that reference Core (the lone
// non-Core project, Lumoin.Veritas.JsonPointer, is excluded there).
global using Utf8Strings = global::Lumoin.Veritas.Core.Utf8Strings;
