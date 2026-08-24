namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// One namespace declaration: a prefix and the namespace IRI it is bound to. The empty prefix is
/// the default-namespace binding, and an empty IRI on it undeclares the default namespace.
/// </summary>
/// <param name="Prefix">The prefix, or an empty value for the default-namespace binding.</param>
/// <param name="NamespaceIri">The namespace IRI the prefix is bound to.</param>
public readonly record struct XmlNamespaceBinding(Utf8String Prefix, Utf8String NamespaceIri);
