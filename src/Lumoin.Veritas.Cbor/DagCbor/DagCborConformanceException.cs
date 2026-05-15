using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.DagCbor;

/// <summary>
/// Thrown when a CBOR wire form or write attempt violates one of the
/// six DAG-CBOR strictness rules from the IPLD specification. The rule
/// name matches the spec's §Strictness numbering / labeling so callers
/// can branch on the specific violation.
/// </summary>
/// <seealso href="https://ipld.io/specs/codecs/dag-cbor/spec/#strictness"/>
public sealed class DagCborConformanceException: FormatException
{
    /// <summary>Initialises a new exception with default values.</summary>
    public DagCborConformanceException()
        : base("DAG-CBOR conformance violation.")
    {
        RuleName = string.Empty;
        Detail = string.Empty;
    }

    /// <summary>Initialises a new exception with the supplied message.</summary>
    /// <param name="message">The exception message.</param>
    public DagCborConformanceException(string message)
        : base(message)
    {
        RuleName = string.Empty;
        Detail = message;
    }

    /// <summary>Initialises a new exception with a message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DagCborConformanceException(string message, Exception innerException)
        : base(message, innerException)
    {
        RuleName = string.Empty;
        Detail = message;
    }

    /// <summary>Initialises a new exception naming the violated rule and the violation detail.</summary>
    /// <param name="ruleName">The IPLD DAG-CBOR §Strictness rule name (e.g. <c>TagsOnlyTag42</c>, <c>FloatsAlways64Bit</c>).</param>
    /// <param name="detail">Free-text explanation of the violation.</param>
    public DagCborConformanceException(string ruleName, string detail)
        : base(string.Create(CultureInfo.InvariantCulture, $"DAG-CBOR conformance violation in rule {ruleName}: {detail}."))
    {
        RuleName = ruleName;
        Detail = detail;
    }

    /// <summary>Gets the rule name (one of the IPLD spec's §Strictness rule labels).</summary>
    public string RuleName { get; }

    /// <summary>Gets the violation detail.</summary>
    public string Detail { get; }
}
