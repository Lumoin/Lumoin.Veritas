using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Test fixture that captures <see cref="ShaclTraceEvent"/> values
/// emitted by <see cref="Lumoin.Veritas.Shacl.Validation.ShaclValidator"/>
/// during a validation run, and formats them for human-readable
/// diagnostic output when a test assertion fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it works.</b> A <see cref="ValidationTrace"/> exposes a
/// <see cref="Capture(in ShaclTraceEvent)"/> method that conforms to
/// the <see cref="Lumoin.Veritas.Core.Diagnostics.TraceHandler{TEvent}"/>
/// delegate shape. The pipeline test extension passes
/// <see cref="Capture(in ShaclTraceEvent)"/> (via method-group
/// conversion) to the validator's
/// <see cref="Lumoin.Veritas.Shacl.Validation.ShaclValidatorOptions.TraceHandler"/>;
/// the validator then drives a chronological event stream into the
/// trace's internal list. After the run,
/// <see cref="FormatTrace(TermDictionary)"/> renders the stream into
/// a textual log suitable for inclusion in failure messages.
/// </para>
/// <para>
/// <b>Why method-group conversion.</b> The conversion captures only
/// the trace instance (a reference), satisfying the project's
/// no-closure-over-parameters convention.
/// </para>
/// </remarks>
internal sealed class ValidationTrace
{
    //Project convention: prefer property accessors over field access.
    //CapturedEvents is the mutable backing list; Events is the
    //read-only view exposed to consumers. Both reference the same
    //instance — the IReadOnlyList<T> view is a window over the
    //growable list, so events captured after Events is observed
    //remain visible to the consumer.
    private List<ShaclTraceEvent> CapturedEvents { get; } = [];

    /// <summary>
    /// The captured event stream in arrival order. Read-only view.
    /// </summary>
    public IReadOnlyList<ShaclTraceEvent> Events => CapturedEvents;

    /// <summary>
    /// Receives a trace event. Method-group-convertible to
    /// <see cref="Lumoin.Veritas.Core.Diagnostics.TraceHandler{TEvent}"/>.
    /// </summary>
    /// <param name="evt">The event to capture.</param>
    public void Capture(in ShaclTraceEvent evt)
    {
        CapturedEvents.Add(evt);
    }

    /// <summary>
    /// Formats the captured trace into a chronological multi-line
    /// log. Each line shows the sequence number, kind, focus and
    /// shape ids resolved through the dictionary, and any kind-specific
    /// payload (constraint IRI, status, severity, value node).
    /// </summary>
    /// <param name="dictionary">
    /// Dictionary used to resolve <see cref="TermId"/>s back to their
    /// IRI or literal lexical form. Pass the same dictionary used
    /// during the validation run.
    /// </param>
    /// <returns>
    /// A multi-line string. Empty string when no events were captured.
    /// </returns>
    public string FormatTrace(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        if(CapturedEvents.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        for(int i = 0; i < CapturedEvents.Count; i++)
        {
            ShaclTraceEvent evt = CapturedEvents[i];
            sb.Append("  #").Append(evt.SequenceNumber.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ').Append(evt.Kind);
            sb.Append(" focus=").Append(FormatTerm(evt.FocusNodeId.Encoded, dictionary));
            sb.Append(" shape=").Append(FormatTerm(evt.ShapeId.Encoded, dictionary));

            if(evt.ConstraintIri is not null)
            {
                sb.Append(" comp=").Append(evt.ConstraintIri);
            }

            if(evt.Kind == ShaclTraceEventKind.ConstraintEvaluationCompleted)
            {
                sb.Append(" status=").Append(evt.Status);
            }

            if(evt.Kind == ShaclTraceEventKind.ValidationResultProduced)
            {
                sb.Append(" sev=").Append(evt.Severity);
                if(!evt.ValueNodeId.IsNone)
                {
                    sb.Append(" value=").Append(FormatTerm(evt.ValueNodeId.Encoded, dictionary));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
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
