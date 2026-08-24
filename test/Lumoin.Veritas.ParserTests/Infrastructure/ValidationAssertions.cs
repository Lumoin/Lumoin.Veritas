using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Text;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Assertion helpers for SHACL validation runs. Operate on the
/// triple of <see cref="ValidationReport"/>,
/// <see cref="ValidationTrace"/>, and
/// <see cref="TermDictionary"/>. On expectation mismatch each helper
/// formats both the report's results and the captured trace into
/// the failure message so the build log shows the full
/// chronological story of the validator's run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Output shape.</b> A failure message has three sections:
/// </para>
/// <list type="number">
///   <item><description>One-line summary of the failed expectation.</description></item>
///   <item><description>Indented list of every <see cref="ValidationResult"/> in the report (severity, constraint component IRI, source shape id, focus and value-node renderings, result-path).</description></item>
///   <item><description>Indented chronological trace dump (sequence number, kind, focus, shape, constraint, status/severity).</description></item>
/// </list>
/// </remarks>
internal static class ValidationAssertions
{
    /// <summary>
    /// Asserts that the run conformed (no violation results). On
    /// failure, dumps results and trace.
    /// </summary>
    public static void AssertConforms(
        ValidationReport report, ValidationTrace trace, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(dictionary);
        if(report.Conforms)
        {
            return;
        }

        Assert.Fail(BuildFailureMessage(
            $"Expected report.Conforms == true, but the report has {report.Results.Length} result(s) including at least one Violation.",
            report, trace, dictionary));
    }

    /// <summary>
    /// Asserts that the report has exactly <paramref name="expected"/>
    /// results. On failure, dumps results and trace.
    /// </summary>
    public static void AssertResultCount(
        ValidationReport report, ValidationTrace trace, TermDictionary dictionary, int expected)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(dictionary);
        if(report.Results.Length == expected)
        {
            return;
        }

        Assert.Fail(BuildFailureMessage(
            $"Expected {expected} result(s), got {report.Results.Length}.",
            report, trace, dictionary));
    }

    /// <summary>
    /// Asserts that the report has exactly one violation result whose
    /// constraint-component IRI equals <paramref name="expectedComponentIri"/>.
    /// On failure, dumps results and trace.
    /// </summary>
    public static void AssertSingleViolationFromComponent(
        ValidationReport report, ValidationTrace trace, TermDictionary dictionary,
        Utf8String expectedComponentIri)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(dictionary);
        if(report.Results.Length == 1
            && report.Results[0].Severity == Severity.Violation
            && report.Results[0].SourceConstraintComponent.Equals(expectedComponentIri))
        {
            return;
        }

        Assert.Fail(BuildFailureMessage(
            $"Expected exactly 1 Violation result from component {expectedComponentIri}.",
            report, trace, dictionary));
    }

    private static string BuildFailureMessage(
        string headline, ValidationReport report, ValidationTrace trace, TermDictionary dictionary)
    {
        StringBuilder sb = new();
        sb.AppendLine(headline);

        sb.AppendLine();
        sb.AppendLine("Results:");
        if(report.Results.Length == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            for(int i = 0; i < report.Results.Length; i++)
            {
                AppendResultLine(sb, i, report.Results[i], dictionary);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Trace:");
        string formatted = trace.FormatTrace(dictionary);
        if(formatted.Length == 0)
        {
            sb.AppendLine("  (no events captured)");
        }
        else
        {
            sb.Append(formatted);
        }

        return sb.ToString();
    }

    private static void AppendResultLine(
        StringBuilder sb, int index, ValidationResult result, TermDictionary dictionary)
    {
        sb.Append("  [").Append(index.ToString(CultureInfo.InvariantCulture)).Append("] ");
        sb.Append("Sev=").Append(result.Severity);
        sb.Append(" Comp=").Append(result.SourceConstraintComponent.ToString());
        sb.Append(" Shape=").Append(FormatTerm(result.SourceShape.Encoded, dictionary));
        sb.Append(" Focus=").Append(FormatTerm(result.FocusNode.Encoded, dictionary));
        sb.Append(" Value=");
        if(result.ValueNode is { } vn)
        {
            sb.Append(FormatTerm(vn.Encoded, dictionary));
        }
        else
        {
            sb.Append("<null>");
        }
        sb.Append(" Path=").Append(result.ResultPath?.ToString() ?? "<null>");
        sb.AppendLine();
    }

    private static string FormatTerm(uint encoded, TermDictionary dictionary)
    {
        if(encoded == 0)
        {
            return "<unset>";
        }

        if(encoded > dictionary.Count)
        {
            return encoded.ToString(CultureInfo.InvariantCulture);
        }

        RdfTerm term = dictionary.Resolve(new TermId(encoded));
        return term.ToString() ?? encoded.ToString(CultureInfo.InvariantCulture);
    }
}
