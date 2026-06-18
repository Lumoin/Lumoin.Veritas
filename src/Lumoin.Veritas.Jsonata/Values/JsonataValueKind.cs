namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// The discriminator for a <see cref="JsonataValue"/>: the seven JSONata data types plus the
/// <see cref="Undefined"/> bottom value.
/// </summary>
/// <remarks>
/// <para>
/// JSONata's data model has seven types — string, number, boolean, null, object, array, and
/// function — over which <see cref="Undefined"/> is the "nothing" value: the result of a path step
/// that matched nothing, distinct from JSON <see cref="Null"/>. <see cref="Undefined"/> is the
/// default kind so that <c>default(JsonataValue)</c> is the empty/"nothing" value.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</para>
/// </remarks>
public enum JsonataValueKind
{
    /// <summary>The JSONata "nothing"/undefined value, distinct from JSON null; omitted from output.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</remarks>
    Undefined = 0,

    /// <summary>The JSON null literal.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</remarks>
    Null,

    /// <summary>A boolean.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/boolean-functions">the JSONata boolean-functions reference</see>.</remarks>
    Boolean,

    /// <summary>An IEEE-754 double (JSONata's single number type).</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/numeric-functions">the JSONata numeric-functions reference</see>.</remarks>
    Number,

    /// <summary>A string.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/string-functions">the JSONata string-functions reference</see>.</remarks>
    String,

    /// <summary>An ordered array (the keep-as-array container).</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/array-functions">the JSONata array-functions reference</see>.</remarks>
    Array,

    /// <summary>An object with insertion-ordered string keys.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/object-functions">the JSONata object-functions reference</see>.</remarks>
    Object,

    /// <summary>A first-class function value; has no JSON representation.</summary>
    /// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
    Function,

    /// <summary>
    /// An INTERNAL-ONLY tuple-stream carrier (the reference's <c>resultSequence.tupleStream</c>): the value a
    /// nested keep-tuples path produces so an enclosing tuple step can merge each inner tuple's focus and
    /// ancestor bindings (the reference's <c>if(res.tupleStream) Object.assign(tuple, res[bb])</c>). It carries
    /// no JSON / JSONata data value and MUST never reach the serializer, deep-equality, the top-level facade, or
    /// any user-visible value: the outermost path always projects its tuples to focuses before returning, and a
    /// tuple stream is only ever produced by a nested path whose enclosing tuple step immediately consumes it.
    /// An escaped tuple stream reaching a value switch is an internal-error sentinel (the <c>default</c> arm).
    /// </summary>
    /// <remarks>See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
    TupleStream
}
