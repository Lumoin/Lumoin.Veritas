namespace Lumoin.Veritas.Rdf.Values;

/// <summary>The value-datatype operation a <see cref="ValueDatatypeQuestion"/> folds.</summary>
public enum ValueDatatypeOperation
{
    /// <summary>The lexical-validity question for the first carried lexical form.</summary>
    ValidateLexicalForm,

    /// <summary>The value-identity question for the two carried lexical forms.</summary>
    SameValue,
}

/// <summary>
/// The folded question an operator's value-datatype oracle answers: an operation kind and up to two
/// lexical forms. One carrier folds both <see cref="ValueDatatype"/> operations so the escape hatch is a
/// single delegate binding rather than two.
/// </summary>
/// <param name="Operation">Which operation is asked.</param>
/// <param name="First">The lexical form under the validity question, and the first form under the identity question.</param>
/// <param name="Second">The second lexical form, for the identity operation; default for the validity operation.</param>
public readonly record struct ValueDatatypeQuestion(
    ValueDatatypeOperation Operation,
    Utf8String First,
    Utf8String Second);

/// <summary>
/// The folded answer a value-datatype oracle returns: the verdict fitting the asked operation, with the
/// other verdict slot left at its abstention default. The static factories fill exactly the fitting slot.
/// </summary>
/// <param name="Validity">The lexical-validity verdict, for a <see cref="ValueDatatypeOperation.ValidateLexicalForm"/> answer.</param>
/// <param name="Identity">The value-identity verdict, for a <see cref="ValueDatatypeOperation.SameValue"/> answer.</param>
public readonly record struct ValueDatatypeAnswer(ValueLexicalValidity Validity, ValueIdentity Identity)
{
    /// <summary>A lexical-validity answer.</summary>
    /// <param name="validity">The validity verdict.</param>
    /// <returns>The answer.</returns>
    public static ValueDatatypeAnswer ForLexicalForm(ValueLexicalValidity validity)
    {
        return new ValueDatatypeAnswer(validity, ValueIdentity.Indeterminate);
    }

    /// <summary>A value-identity answer.</summary>
    /// <param name="identity">The identity verdict.</param>
    /// <returns>The answer.</returns>
    public static ValueDatatypeAnswer ForSameValue(ValueIdentity identity)
    {
        return new ValueDatatypeAnswer(ValueLexicalValidity.Indeterminate, identity);
    }
}

/// <summary>
/// The computational escape hatch: an operator-supplied oracle that answers either value-datatype
/// operation for a <see cref="DelegateBackedValueDatatype"/>. Named rather than a bare functional so the
/// binding is a discoverable type; implementors bind their state in an explicit frame and pass a method
/// group, never a capturing lambda.
/// </summary>
/// <param name="question">The folded question.</param>
/// <returns>The folded answer.</returns>
public delegate ValueDatatypeAnswer ValueDatatypeOracleDelegate(in ValueDatatypeQuestion question);
