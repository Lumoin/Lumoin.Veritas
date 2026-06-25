namespace Lumoin.Veritas.Core.Xml;

/// <summary>How an <see cref="XmlByteScanner"/> reacts to a malformed token.</summary>
public enum XmlScanStrictness
{
    /// <summary>A malformed token throws <see cref="System.FormatException"/> — the well-formedness contract the RDF/XML and SPARQL-results readers present.</summary>
    Strict,

    /// <summary>A malformed token is recovered from silently and scanning continues — the value-based contract the OWL editor surfaces present (a bare <c>&amp;</c> stays literal, an undefined entity drops, an unterminated tail is abandoned), recording nothing.</summary>
    Lenient
}
