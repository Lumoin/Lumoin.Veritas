using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// The numeral conversions for the XPath <c>fn:format-integer</c>/<c>fn:parse-integer</c> picture families that
/// are not plain decimal digits: Roman numerals, spreadsheet-column letters (bijective base-26), and English
/// number words (cardinal and ordinal). This is a member of the reusable <c>Formatting</c> unit
/// <see cref="IntegerPictureFormatter"/> consumes; the conversions are the faithful counterparts of the
/// reference engine's <c>decimalToRoman</c>/<c>romanToDecimal</c>, <c>decimalToLetters</c>/<c>lettersToDecimal</c>,
/// and <c>numberToWords</c>/<c>wordsToNumber</c> helpers.
/// </summary>
/// <remarks>
/// <para>
/// Every conversion runs as an explicit loop or work-stack with no recursion: <c>decimalToRoman</c> is a
/// single forward pass over the numeral table, and the recursive <c>numberToWords.lookup</c> becomes an
/// explicit task stack of emit-literal and expand-magnitude tasks bounded by <see cref="MaximumWordTasks"/>, so a
/// pathological input throws a catchable <see cref="JsonataEvaluationLimitException"/> rather than overflowing
/// the call stack.
/// </para>
/// <para>
/// All magnitude arithmetic is carried in <see cref="double"/>, matching the reference engine's number model, so
/// the largest representable inputs (for example <c>1e46</c>) decompose identically. Words are emitted in the
/// table's title-case spelling, with American decade spelling (<c>Forty</c>); the caller lower- or upper-cases
/// the result for the <c>w</c> or <c>W</c> picture.
/// </para>
/// </remarks>
internal static partial class IntegerNumerals
{
    /// <summary>The Roman-numeral value/symbol table, largest first, with the four subtractive pairs in lower case.</summary>
    private static readonly (int Value, string Symbol)[] RomanNumerals =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
        (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
    ];

    /// <summary>The cardinal words for zero through nineteen.</summary>
    private static readonly string[] Few =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve",
        "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];

    /// <summary>The ordinal words for zeroth through nineteenth.</summary>
    private static readonly string[] Ordinals =
    [
        "Zeroth", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth",
        "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth", "Sixteenth", "Seventeenth", "Eighteenth", "Nineteenth"
    ];

    /// <summary>The decade words (Twenty through Ninety) followed by Hundred; the Hundred entry is only reached above one hundred.</summary>
    private static readonly string[] Decades =
    [
        "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety", "Hundred"
    ];

    /// <summary>The magnitude words, each three orders of magnitude apart.</summary>
    private static readonly string[] Magnitudes =
    [
        "Thousand", "Million", "Billion", "Trillion"
    ];

    /// <summary>The lower-cased word-to-value lookup the parse path consults, built once from the spelling tables.</summary>
    private static readonly Dictionary<string, double> WordValues = BuildWordValues();

    /// <summary>The work-stack bound for the number-to-words expansion; deep inputs throw a catchable limit rather than overflowing.</summary>
    private const int MaximumWordTasks = 256;

    /// <summary>Formats a non-negative integer-valued magnitude as a bijective base-26 column label in the given alphabet.</summary>
    /// <param name="value">The non-negative integer-valued magnitude.</param>
    /// <param name="aChar">The alphabet's first letter (<c>'A'</c> for upper case, <c>'a'</c> for lower case).</param>
    /// <returns>The column label, least-significant group last.</returns>
    public static string DecimalToLetters(double value, char aChar)
    {
        int aCode = aChar;
        StringBuilder builder = new();
        double remaining = value;
        while(remaining > 0)
        {
            int digit = (int)((remaining - 1) % 26);
            builder.Insert(0, (char)(digit + aCode));
            remaining = Math.Floor((remaining - 1) / 26);
        }

        return builder.ToString();
    }

    /// <summary>Parses a bijective base-26 column label in the given alphabet back to its integer value.</summary>
    /// <param name="letters">The column label.</param>
    /// <param name="aChar">The alphabet's first letter (<c>'A'</c> for upper case, <c>'a'</c> for lower case).</param>
    /// <returns>The decoded value.</returns>
    public static double LettersToDecimal(string letters, char aChar)
    {
        int aCode = aChar;
        double decimalValue = 0;
        for(int i = 0; i < letters.Length; i++)
        {
            int digit = letters[letters.Length - i - 1] - aCode + 1;
            decimalValue += digit * Math.Pow(26, i);
        }

        return decimalValue;
    }

    /// <summary>
    /// Formats a non-negative integer-valued magnitude as a lower-case Roman numeral over a single forward pass
    /// of the numeral table, repeatedly subtracting each numeral while it still fits. Zero yields the empty
    /// string, matching the reference engine.
    /// </summary>
    /// <param name="value">The non-negative integer-valued magnitude.</param>
    /// <returns>The lower-case Roman numeral, empty for zero.</returns>
    public static string DecimalToRoman(double value)
    {
        StringBuilder builder = new();
        double remaining = value;
        for(int index = 0; index < RomanNumerals.Length && remaining > 0; index++)
        {
            (int numeralValue, string symbol) = RomanNumerals[index];
            while(remaining >= numeralValue)
            {
                builder.Append(symbol);
                remaining -= numeralValue;
            }
        }

        return builder.ToString();
    }

    /// <summary>Parses an upper-case Roman numeral back to its integer value, subtracting any numeral smaller than the running maximum.</summary>
    /// <param name="roman">The upper-case Roman numeral.</param>
    /// <returns>The decoded value.</returns>
    public static double RomanToDecimal(string roman)
    {
        double decimalValue = 0;
        int max = 1;
        for(int i = roman.Length - 1; i >= 0; i--)
        {
            int value = RomanValue(roman[i]);
            if(value < max)
            {
                decimalValue -= value;
            }
            else
            {
                max = value;
                decimalValue += value;
            }
        }

        return decimalValue;
    }

    /// <summary>
    /// Spells a non-negative integer-valued magnitude in English words, cardinal or ordinal, over an explicit
    /// task stack: a task either emits a literal or expands a <c>(num, prev, ord)</c> triple, with an expand
    /// pushing its parts in reverse so they pop in spelling order. The result is title-cased per the spelling
    /// tables; the caller lower- or upper-cases it for the picture's word case.
    /// </summary>
    /// <param name="value">The non-negative integer-valued magnitude.</param>
    /// <param name="ordinal">Whether to spell the final word as an ordinal.</param>
    /// <returns>The spelled words.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The expansion exceeded the task bound.</exception>
    public static string NumberToWords(double value, bool ordinal)
    {
        StringBuilder builder = new();
        Stack<WordTask> tasks = new();
        tasks.Push(WordTask.Expand(value, prev: false, ordinal));

        int steps = 0;
        while(tasks.Count > 0)
        {
            if(++steps > MaximumWordTasks)
            {
                throw new JsonataEvaluationLimitException("The $formatInteger word expansion exceeded its task limit.");
            }

            WordTask task = tasks.Pop();
            if(task.IsLiteral)
            {
                builder.Append(task.Literal);
                continue;
            }

            ExpandWord(task.Number, task.Prev, task.Ordinal, tasks);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses an English number phrase (lower-cased by the caller) back to its integer value, splitting on the
    /// fixed word-separator pattern, mapping each part through the spelling lookup, and folding the parts into
    /// magnitude segments.
    /// </summary>
    /// <param name="text">The lower-cased number phrase.</param>
    /// <returns>The decoded value.</returns>
    public static double WordsToNumber(string text)
    {
        string[] parts = WordSeparator().Split(text);
        List<double> segments = [0];
        foreach(string part in parts)
        {
            WordValues.TryGetValue(part, out double value);
            if(value < 100)
            {
                double top = segments[^1];
                segments.RemoveAt(segments.Count - 1);
                if(top >= 1000)
                {
                    segments.Add(top);
                    top = 0;
                }

                segments.Add(top + value);
            }
            else
            {
                double top = segments[^1];
                segments.RemoveAt(segments.Count - 1);
                segments.Add(top * value);
            }
        }

        double total = 0;
        foreach(double segment in segments)
        {
            total += segment;
        }

        return total;
    }

    /// <summary>
    /// Expands a single <c>(num, prev, ord)</c> triple into its literal parts and child expansions, pushing the
    /// produced tasks in reverse order so the task stack pops them in spelling order. The four branches mirror
    /// the reference engine's <c>lookup</c>: the teens, the decades, the hundreds, and the magnitudes.
    /// </summary>
    /// <param name="num">The magnitude to expand.</param>
    /// <param name="prev">Whether a higher-order word precedes this magnitude (governs the joining word).</param>
    /// <param name="ord">Whether this magnitude is the final, ordinal-spelled, word.</param>
    /// <param name="tasks">The task stack the produced tasks are pushed onto.</param>
    private static void ExpandWord(double num, bool prev, bool ord, Stack<WordTask> tasks)
    {
        if(num <= 19)
        {
            int index = (int)num;
            string word = (prev ? " and " : "") + (ord ? Ordinals[index] : Few[index]);
            tasks.Push(WordTask.Emit(word));

            return;
        }

        if(num < 100)
        {
            int tens = (int)Math.Floor(num / 10);
            int remainder = (int)(num % 10);
            string decade = Decades[tens - 2];
            if(remainder > 0)
            {
                //Push in reverse: the decade-and-hyphen literal then the remainder, so they pop in spelling order.
                tasks.Push(WordTask.Expand(remainder, prev: false, ord));
                tasks.Push(WordTask.Emit((prev ? " and " : "") + decade + "-"));
            }
            else if(ord)
            {
                tasks.Push(WordTask.Emit((prev ? " and " : "") + decade[..^1] + "ieth"));
            }
            else
            {
                tasks.Push(WordTask.Emit((prev ? " and " : "") + decade));
            }

            return;
        }

        if(num < 1000)
        {
            int hundreds = (int)Math.Floor(num / 100);
            int remainder = (int)(num % 100);
            string head = (prev ? ", " : "") + Few[hundreds] + " Hundred" + (remainder == 0 && ord ? "th" : "");
            if(remainder > 0)
            {
                tasks.Push(WordTask.Expand(remainder, prev: true, ord));
            }

            tasks.Push(WordTask.Emit(head));

            return;
        }

        int magnitude = (int)Math.Floor(Math.Log10(num) / 3);
        if(magnitude > Magnitudes.Length)
        {
            magnitude = Magnitudes.Length;
        }

        double factor = Math.Pow(10, magnitude * 3);
        double mantissa = Math.Floor(num / factor);
        double remainderHigh = num - (mantissa * factor);

        //Push in reverse: the remainder expansion, then the magnitude word, then the mantissa expansion, then
        //the leading separator, so the stack pops them in spelling order.
        if(remainderHigh > 0)
        {
            tasks.Push(WordTask.Expand(remainderHigh, prev: true, ord));
        }

        tasks.Push(WordTask.Emit(" " + Magnitudes[magnitude - 1] + (remainderHigh == 0 && ord ? "th" : "")));
        tasks.Push(WordTask.Expand(mantissa, prev: false, ordinal: false));
        if(prev)
        {
            tasks.Push(WordTask.Emit(", "));
        }
    }

    /// <summary>Maps a single upper-case Roman-numeral letter to its value, or zero for an unrecognised letter.</summary>
    /// <param name="letter">The Roman-numeral letter.</param>
    /// <returns>The letter's value, or zero.</returns>
    private static int RomanValue(char letter)
    {
        return letter switch
        {
            'M' => 1000,
            'D' => 500,
            'C' => 100,
            'L' => 50,
            'X' => 10,
            'V' => 5,
            'I' => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Builds the lower-cased word-to-value lookup from the spelling tables: the cardinal and ordinal teens, the
    /// decades (with their <c>ieth</c> ordinal spelling), <c>hundredth</c>, and the magnitudes (with their
    /// <c>th</c> ordinal spelling).
    /// </summary>
    /// <returns>The word-to-value lookup.</returns>
    private static Dictionary<string, double> BuildWordValues()
    {
        Dictionary<string, double> values = new(StringComparer.Ordinal);
        for(int i = 0; i < Few.Length; i++)
        {
            values[ToLower(Few[i])] = i;
        }

        for(int i = 0; i < Ordinals.Length; i++)
        {
            values[ToLower(Ordinals[i])] = i;
        }

        for(int i = 0; i < Decades.Length; i++)
        {
            string lower = ToLower(Decades[i]);
            double decadeValue = (i + 2) * 10;
            values[lower] = decadeValue;
            values[lower[..^1] + "ieth"] = decadeValue;
        }

        values["hundredth"] = 100;
        for(int i = 0; i < Magnitudes.Length; i++)
        {
            string lower = ToLower(Magnitudes[i]);
            double magnitudeValue = Math.Pow(10, (i + 1) * 3);
            values[lower] = magnitudeValue;
            values[lower + "th"] = magnitudeValue;
        }

        return values;
    }

    /// <summary>
    /// Lower-cases a string with invariant culture. The <c>fn:format-integer</c> word picture is defined in
    /// terms of lower-case spelling, so the invariant lower-casing is the contract here, not a normalisation.
    /// </summary>
    /// <param name="text">The string to lower-case.</param>
    /// <returns>The invariant lower-case string.</returns>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "The fn:format-integer word picture is defined to produce lower-case spelling; the invariant lower-casing is its contract, not a normalization.")]
    public static string ToLower(string text)
    {
        return text.ToLowerInvariant();
    }

    /// <summary>The fixed word-separator pattern: a comma-space, the word <c>and</c> with surrounding spaces, or a single whitespace or hyphen.</summary>
    /// <returns>The compiled source-generated matcher.</returns>
    [GeneratedRegex(@",\s|\sand\s|[\s\-]", RegexOptions.CultureInvariant)]
    private static partial Regex WordSeparator();

    /// <summary>
    /// One step of the number-to-words expansion: either an emit-literal task carrying its literal, or an
    /// expand task carrying a <c>(num, prev, ord)</c> triple. The work stack of these tasks replaces the
    /// reference engine's recursive <c>lookup</c>.
    /// </summary>
    /// <param name="IsLiteral">Whether this task emits a literal (otherwise it expands a magnitude triple).</param>
    /// <param name="Literal">The literal to emit, for an emit task.</param>
    /// <param name="Number">The magnitude to expand, for an expand task.</param>
    /// <param name="Prev">Whether a higher-order word precedes the magnitude, for an expand task.</param>
    /// <param name="Ordinal">Whether the magnitude is the final ordinal-spelled word, for an expand task.</param>
    private readonly record struct WordTask(bool IsLiteral, string Literal, double Number, bool Prev, bool Ordinal)
    {
        /// <summary>Creates an emit-literal task.</summary>
        /// <param name="literal">The literal to emit.</param>
        /// <returns>The emit task.</returns>
        public static WordTask Emit(string literal)
        {
            return new WordTask(IsLiteral: true, literal, Number: 0, Prev: false, Ordinal: false);
        }

        /// <summary>Creates an expand task for a magnitude triple.</summary>
        /// <param name="number">The magnitude to expand.</param>
        /// <param name="prev">Whether a higher-order word precedes the magnitude.</param>
        /// <param name="ordinal">Whether the magnitude is the final ordinal-spelled word.</param>
        /// <returns>The expand task.</returns>
        public static WordTask Expand(double number, bool prev, bool ordinal)
        {
            return new WordTask(IsLiteral: false, Literal: "", number, prev, ordinal);
        }
    }
}
