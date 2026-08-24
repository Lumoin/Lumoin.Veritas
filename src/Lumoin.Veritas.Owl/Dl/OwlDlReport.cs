using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Dl;

/// <summary>
/// One reason a document is not an OWL 2 DL ontology document.
/// </summary>
/// <param name="Origin">The triple the violation was detected at, when one is attributable; otherwise <c>null</c>.</param>
/// <param name="Construct">A description of the violated restriction.</param>
[DebuggerDisplay("OwlDlViolation {Construct,nq}")]
public sealed record OwlDlViolation(Quad? Origin, string Construct);

/// <summary>
/// The species verdict for a document: whether it is an OWL 2 DL ontology
/// document, with every violation found. Every RDF graph is OWL 2 Full, so
/// the only question the species asks is DL membership.
/// </summary>
/// <param name="IsInDl">Whether the document satisfies the OWL 2 DL restrictions.</param>
/// <param name="Violations">The violations found; empty exactly when <paramref name="IsInDl"/> is <c>true</c>.</param>
[DebuggerDisplay("OwlDlReport IsInDl={IsInDl} Violations={Violations.Count}")]
public sealed record OwlDlReport(bool IsInDl, IReadOnlyList<OwlDlViolation> Violations);
