using System;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The well-known strings of the XML transport subset: markup delimiters,
/// declaration pseudo-attribute names and values, the reserved prefixes and
/// their namespace names, and the five predefined entities with their
/// replacement bytes. Every scanner comparison routes through these members —
/// no file in this assembly spells a well-known string inline. Single
/// grammar bytes (angle brackets, quotes, the equals sign, the ampersand,
/// the semicolon) are scanner grammar and lexicon territory, not vocabulary
/// members.
/// </summary>
internal static class XmlVocabulary
{
    /// <summary>The opening of the one recognized XML declaration, exact case.</summary>
    public static ReadOnlySpan<byte> DeclarationOpening => "<?xml"u8;

    /// <summary>The two bytes closing the XML declaration.</summary>
    public static ReadOnlySpan<byte> DeclarationClose => "?>"u8;

    /// <summary>The four bytes opening a comment.</summary>
    public static ReadOnlySpan<byte> CommentOpening => "<!--"u8;

    /// <summary>The three bytes closing a comment.</summary>
    public static ReadOnlySpan<byte> CommentClose => "-->"u8;

    /// <summary>The nine bytes opening a CDATA section.</summary>
    public static ReadOnlySpan<byte> CdataOpening => "<![CDATA["u8;

    /// <summary>
    /// The three bytes closing a CDATA section — the same sequence whose
    /// appearance in plain character data refuses.
    /// </summary>
    public static ReadOnlySpan<byte> CdataClose => "]]>"u8;

    /// <summary>The keyword opening a document type declaration, refused by the security floor.</summary>
    public static ReadOnlySpan<byte> DoctypeOpening => "<!DOCTYPE"u8;

    /// <summary>The keyword opening an entity declaration, refused by the security floor.</summary>
    public static ReadOnlySpan<byte> EntityDeclarationOpening => "<!ENTITY"u8;

    /// <summary>The keyword opening an element type declaration, refused by the security floor.</summary>
    public static ReadOnlySpan<byte> ElementDeclarationOpening => "<!ELEMENT"u8;

    /// <summary>The keyword opening an attribute-list declaration, refused by the security floor.</summary>
    public static ReadOnlySpan<byte> AttributeListDeclarationOpening => "<!ATTLIST"u8;

    /// <summary>The keyword opening a notation declaration, refused by the security floor.</summary>
    public static ReadOnlySpan<byte> NotationDeclarationOpening => "<!NOTATION"u8;

    /// <summary>The required first pseudo-attribute name of the XML declaration.</summary>
    public static ReadOnlySpan<byte> VersionName => "version"u8;

    /// <summary>The optional second pseudo-attribute name of the XML declaration.</summary>
    public static ReadOnlySpan<byte> EncodingName => "encoding"u8;

    /// <summary>The optional third pseudo-attribute name of the XML declaration.</summary>
    public static ReadOnlySpan<byte> StandaloneName => "standalone"u8;

    /// <summary>The one version value this scanner accepts.</summary>
    public static ReadOnlySpan<byte> VersionValue => "1.0"u8;

    /// <summary>The uppercase spelling of the one encoding the declaration may name.</summary>
    public static ReadOnlySpan<byte> Utf8EncodingUppercase => "UTF-8"u8;

    /// <summary>The lowercase spelling of the one encoding the declaration may name.</summary>
    public static ReadOnlySpan<byte> Utf8EncodingLowercase => "utf-8"u8;

    /// <summary>The affirmative standalone pseudo-attribute value.</summary>
    public static ReadOnlySpan<byte> YesValue => "yes"u8;

    /// <summary>The negative standalone pseudo-attribute value.</summary>
    public static ReadOnlySpan<byte> NoValue => "no"u8;

    /// <summary>The reserved prefix pre-bound to the XML namespace.</summary>
    public static ReadOnlySpan<byte> XmlPrefix => "xml"u8;

    /// <summary>
    /// The reserved declaration name: as a whole attribute name it declares
    /// the default namespace, as a prefix it declares a named one, and
    /// nothing but declarations may wear it.
    /// </summary>
    public static ReadOnlySpan<byte> XmlnsName => "xmlns"u8;

    /// <summary>
    /// The namespace name the xml prefix is permanently bound to. It may be
    /// redeclared only by the xml prefix itself, to exactly this value, and
    /// never as the default namespace.
    /// </summary>
    public static ReadOnlySpan<byte> XmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>
    /// The namespace name of declarations themselves. Nothing may bind to
    /// it, and it may not be declared as the default namespace.
    /// </summary>
    public static ReadOnlySpan<byte> XmlnsNamespace => "http://www.w3.org/2000/xmlns/"u8;

    /// <summary>The entity name decoding to the ampersand.</summary>
    public static ReadOnlySpan<byte> AmpersandEntityName => "amp"u8;

    /// <summary>The ampersand entity's replacement byte, inert on arrival.</summary>
    public static ReadOnlySpan<byte> AmpersandReplacement => "&"u8;

    /// <summary>The entity name decoding to the less-than sign.</summary>
    public static ReadOnlySpan<byte> LessThanEntityName => "lt"u8;

    /// <summary>The less-than entity's replacement byte, inert on arrival.</summary>
    public static ReadOnlySpan<byte> LessThanReplacement => "<"u8;

    /// <summary>The entity name decoding to the greater-than sign.</summary>
    public static ReadOnlySpan<byte> GreaterThanEntityName => "gt"u8;

    /// <summary>The greater-than entity's replacement byte, inert on arrival.</summary>
    public static ReadOnlySpan<byte> GreaterThanReplacement => ">"u8;

    /// <summary>The entity name decoding to the apostrophe.</summary>
    public static ReadOnlySpan<byte> ApostropheEntityName => "apos"u8;

    /// <summary>The apostrophe entity's replacement byte, inert on arrival.</summary>
    public static ReadOnlySpan<byte> ApostropheReplacement => "'"u8;

    /// <summary>The entity name decoding to the quotation mark.</summary>
    public static ReadOnlySpan<byte> QuotationEntityName => "quot"u8;

    /// <summary>The quotation entity's replacement byte, inert on arrival.</summary>
    public static ReadOnlySpan<byte> QuotationReplacement => "\""u8;
}
