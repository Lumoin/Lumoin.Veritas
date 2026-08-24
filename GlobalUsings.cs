// The byte-native string is the shared Lumoin.Base type. This global alias, compiled into every project via
// Directory.Build.props, lets the pervasive unqualified Utf8String resolve without a per-file using. The companion
// Utf8Strings factory alias lives in GlobalUsings.Core.cs (it targets a Lumoin.Veritas.Core type, so it is only
// compiled where Core is referenced).
global using Utf8String = global::Lumoin.Base.Utf8String;
