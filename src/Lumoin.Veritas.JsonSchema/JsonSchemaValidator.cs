using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Json;
using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Validates a JSON instance against a JSON Schema (draft 2020-12) over the backend-agnostic
/// <see cref="JsonNode"/> model.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation is iterative: a schema and instance are walked with an explicit two-phase work stack
/// (open a frame, then close it once its child frames have reported), never by call-stack recursion.
/// Unknown keywords are treated as annotations and ignored, as the specification requires, so an
/// instance is only rejected by a keyword this validator actually asserts.
/// </para>
/// </remarks>
public static class JsonSchemaValidator
{
    /// <summary>Validates a UTF-8 JSON instance against a UTF-8 JSON schema, parsing both with the supplied parser.</summary>
    /// <param name="schema">The UTF-8 JSON schema document.</param>
    /// <param name="instance">The UTF-8 JSON instance document.</param>
    /// <param name="parse">The JSON parser (for example <c>StjJsonAdapter.Parse</c> from Lumoin.Veritas.Json.Stj).</param>
    /// <param name="loader">A loader for documents reached by <c>$ref</c> outside the root schema, or <see langword="null"/>.</param>
    /// <param name="baseUri">The root schema's retrieval base URI, or <see langword="null"/> for none.</param>
    /// <param name="regexMatch">The regular-expression matcher, or <see langword="null"/> for the default.</param>
    /// <returns>The validation result.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "The engine resolves references via the RFC 3986 string-based IriResolver; System.Uri is intentionally not used on this path.")]
    public static ValidationResult Validate(Utf8String schema, Utf8String instance, ParseJsonDelegate parse, SchemaDocumentLoader? loader = null, string? baseUri = null, RegexMatchDelegate? regexMatch = null)
    {
        ArgumentNullException.ThrowIfNull(parse);

        return Validate(parse(schema), parse(instance), loader, baseUri, regexMatch);
    }

    /// <summary>Validates an instance against a schema.</summary>
    /// <param name="schema">The schema node (an object schema, or a boolean schema <c>true</c>/<c>false</c>).</param>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="loader">A loader for documents reached by <c>$ref</c> outside the root schema, or <see langword="null"/>.</param>
    /// <param name="baseUri">The root schema's retrieval base URI, or <see langword="null"/> for none.</param>
    /// <param name="regexMatch">The regular-expression matcher for <c>pattern</c>/<c>patternProperties</c>, or <see langword="null"/> for the default <see cref="Regex"/>-backed one.</param>
    /// <returns>The validation result.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "The engine resolves references via the RFC 3986 string-based IriResolver; System.Uri is intentionally not used on this path.")]
    public static ValidationResult Validate(JsonNode schema, JsonNode instance, SchemaDocumentLoader? loader = null, string? baseUri = null, RegexMatchDelegate? regexMatch = null)
    {
        Frame root = EvaluateRoot(schema, instance, loader, baseUri, regexMatch, out List<ValidationError> errors, out _);

        return new ValidationResult(root.Result, errors);
    }

    /// <summary>Validates an instance against a schema and produces the result in a JSON Schema output format.</summary>
    /// <param name="schema">The schema node.</param>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="format">The output format (<see cref="OutputFormat.Flag"/> or <see cref="OutputFormat.Basic"/>).</param>
    /// <param name="loader">A loader for documents reached by <c>$ref</c> outside the root schema, or <see langword="null"/>.</param>
    /// <param name="baseUri">The root schema's retrieval base URI, or <see langword="null"/> for none.</param>
    /// <param name="regexMatch">The regular-expression matcher, or <see langword="null"/> for the default.</param>
    /// <returns>The root output unit.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "The engine resolves references via the RFC 3986 string-based IriResolver; System.Uri is intentionally not used on this path.")]
    public static OutputUnit Evaluate(JsonNode schema, JsonNode instance, OutputFormat format, SchemaDocumentLoader? loader = null, string? baseUri = null, RegexMatchDelegate? regexMatch = null)
    {
        Frame root = EvaluateRoot(schema, instance, loader, baseUri, regexMatch, out List<ValidationError> errors, out List<OutputUnit> annotations);

        return BuildOutput(root, errors, annotations, format);
    }

    /// <summary>Runs the evaluation loop, returning the root frame and the gathered errors and (passing) annotations.</summary>
    /// <param name="schema">The schema node.</param>
    /// <param name="instance">The instance node.</param>
    /// <param name="loader">The remote-document loader, or <see langword="null"/>.</param>
    /// <param name="baseUri">The root base URI, or <see langword="null"/>.</param>
    /// <param name="regexMatch">The regex matcher, or <see langword="null"/>.</param>
    /// <param name="errors">The accumulated assertion failures.</param>
    /// <param name="annotations">The annotation units gathered from passing frames.</param>
    /// <returns>The root frame, closed.</returns>
    private static Frame EvaluateRoot(JsonNode schema, JsonNode instance, SchemaDocumentLoader? loader, string? baseUri, RegexMatchDelegate? regexMatch, out List<ValidationError> errors, out List<OutputUnit> annotations)
    {
        string rootBase = baseUri ?? string.Empty;
        SchemaRegistry registry = new(schema, rootBase, loader);
        RegexMatchDelegate match = regexMatch ?? DefaultRegexMatch;
        bool validationEnabled = IsValidationVocabularyEnabled(schema, rootBase, loader);
        errors = [];
        annotations = [];
        Frame root = new()
        {
            Schema = schema,
            Instance = instance,
            InstanceLocation = Ptr.Root,
            KeywordLocation = Ptr.Root,
            BaseUri = rootBase,
            DynamicScope = new ScopeNode(rootBase, null)
        };

        Stack<(Frame Frame, bool Closing)> stack = new();
        stack.Push((root, false));

        while(stack.Count > 0)
        {
            (Frame frame, bool closing) = stack.Pop();
            if(closing)
            {
                CloseFrame(frame, stack, annotations);
                continue;
            }

            OpenFrame(frame, stack, errors, registry, match, validationEnabled);
        }

        return root;
    }

    /// <summary>Builds the requested output structure from the evaluation result.</summary>
    /// <param name="root">The closed root frame (the head of the evaluation tree).</param>
    /// <param name="errors">The assertion failures.</param>
    /// <param name="annotations">The annotation units from passing frames.</param>
    /// <param name="format">The output format.</param>
    /// <returns>The root output unit.</returns>
    private static OutputUnit BuildOutput(Frame root, List<ValidationError> errors, List<OutputUnit> annotations, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Flag => new OutputUnit { Valid = root.Result },
            OutputFormat.Basic => BuildBasic(root.Result, errors, annotations),
            OutputFormat.Detailed => BuildHierarchical(root, errors, annotations, prune: true),
            OutputFormat.Verbose => BuildHierarchical(root, errors, annotations, prune: false),
            _ => new OutputUnit { Valid = root.Result }
        };
    }

    /// <summary>Builds the flat Basic output: the annotation list on success, the error list on failure.</summary>
    /// <param name="valid">The overall validity.</param>
    /// <param name="errors">The assertion failures.</param>
    /// <param name="annotations">The annotation units from passing frames.</param>
    /// <returns>The root output unit.</returns>
    private static OutputUnit BuildBasic(bool valid, List<ValidationError> errors, List<OutputUnit> annotations)
    {
        if(valid)
        {
            return new OutputUnit { Valid = true, Annotations = annotations };
        }

        List<OutputUnit> errorUnits = [];
        foreach(ValidationError error in errors)
        {
            errorUnits.Add(ErrorUnit(error));
        }

        return new OutputUnit { Valid = false, Errors = errorUnits };
    }

    /// <summary>Builds the hierarchical (Detailed/Verbose) output by walking the evaluation tree, attaching each frame's self errors and annotations and nesting its child frames.</summary>
    /// <param name="root">The root frame.</param>
    /// <param name="errors">The flat assertion failures, distributed to their owning frames.</param>
    /// <param name="annotations">The flat annotation units, distributed to their owning frames.</param>
    /// <param name="prune">Whether to drop valid, information-free nodes (Detailed) or keep the full tree (Verbose).</param>
    /// <returns>The root output unit.</returns>
    private static OutputUnit BuildHierarchical(Frame root, List<ValidationError> errors, List<OutputUnit> annotations, bool prune)
    {
        Dictionary<string, List<OutputUnit>> selfErrors = new(StringComparer.Ordinal);
        Dictionary<string, List<OutputUnit>> selfAnnotations = new(StringComparer.Ordinal);

        HashSet<string> frameKeywordLocations = new(StringComparer.Ordinal);
        CollectFrameKeywordLocations(root, frameKeywordLocations);

        foreach(ValidationError error in errors)
        {
            string owner = OwningFrameKeywordLocation(error.KeywordLocation.ToString(), frameKeywordLocations);
            Add(selfErrors, owner, ErrorUnit(error));
        }

        foreach(OutputUnit annotation in annotations)
        {
            string owner = OwningFrameKeywordLocation(annotation.KeywordLocation ?? string.Empty, frameKeywordLocations);
            Add(selfAnnotations, owner, annotation);
        }

        OutputUnit unit = BuildFrameUnit(root, selfErrors, selfAnnotations);

        return prune ? Prune(unit, isRoot: true) : unit;
    }

    /// <summary>Builds the Verbose output unit for one frame: its validity, locations, self error/annotation units, and recursively its child frames.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="selfErrors">Self error units keyed by frame keyword location.</param>
    /// <param name="selfAnnotations">Self annotation units keyed by frame keyword location.</param>
    /// <returns>The frame's output unit.</returns>
    private static OutputUnit BuildFrameUnit(Frame frame, Dictionary<string, List<OutputUnit>> selfErrors, Dictionary<string, List<OutputUnit>> selfAnnotations)
    {
        string keywordLocation = frame.KeywordLocation.ToString();

        List<OutputUnit> children = [];
        if(selfErrors.TryGetValue(keywordLocation, out List<OutputUnit>? frameErrors))
        {
            children.AddRange(frameErrors);
        }

        if(selfAnnotations.TryGetValue(keywordLocation, out List<OutputUnit>? frameAnnotations))
        {
            children.AddRange(frameAnnotations);
        }

        foreach(Frame child in frame.Children)
        {
            children.Add(BuildFrameUnit(child, selfErrors, selfAnnotations));
        }

        OutputUnit unit = new()
        {
            Valid = frame.Result,
            KeywordLocation = keywordLocation,
            AbsoluteKeywordLocation = frame.EffectiveBase + "#" + keywordLocation,
            InstanceLocation = frame.InstanceLocation.ToString(),
            Errors = frame.Result ? null : (children.Count > 0 ? children : [InlineError(frame)]),
            Annotations = frame.Result && children.Count > 0 ? children : null
        };

        return unit;
    }

    /// <summary>Prunes a Verbose tree to a Detailed tree: a non-root node that is valid and carries no annotation, error, or surviving child is dropped.</summary>
    /// <param name="unit">The unit to prune.</param>
    /// <param name="isRoot">Whether this is the root unit (always kept).</param>
    /// <returns>The pruned unit.</returns>
    private static OutputUnit Prune(OutputUnit unit, bool isRoot)
    {
        List<OutputUnit>? prunedErrors = PruneChildren(unit.Errors);
        List<OutputUnit>? prunedAnnotations = PruneChildren(unit.Annotations);

        return unit with { Errors = prunedErrors, Annotations = prunedAnnotations };

        static List<OutputUnit>? PruneChildren(IReadOnlyList<OutputUnit>? children)
        {
            if(children is null)
            {
                return null;
            }

            List<OutputUnit> kept = [];
            foreach(OutputUnit child in children)
            {
                OutputUnit pruned = Prune(child, isRoot: false);
                if(IsInformative(pruned))
                {
                    kept.Add(pruned);
                }
            }

            return kept.Count > 0 ? kept : null;
        }
    }

    /// <summary>Whether a Detailed node carries information worth keeping: it failed, or it bears an annotation value or surviving children.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns><see langword="true"/> when the node should survive pruning.</returns>
    private static bool IsInformative(OutputUnit unit)
    {
        return !unit.Valid
            || unit.Annotation is not null
            || unit.Error is not null
            || unit.Errors is { Count: > 0 }
            || unit.Annotations is { Count: > 0 };
    }

    /// <summary>Builds a leaf error output unit from an assertion failure.</summary>
    /// <param name="error">The assertion failure.</param>
    /// <returns>The error unit.</returns>
    private static OutputUnit ErrorUnit(ValidationError error)
    {
        return new OutputUnit
        {
            Valid = false,
            KeywordLocation = error.KeywordLocation.ToString(),
            AbsoluteKeywordLocation = error.AbsoluteKeywordLocation,
            InstanceLocation = error.InstanceLocation.ToString(),
            Error = error.Message
        };
    }

    /// <summary>Builds a fallback inline error for an invalid frame that gathered no self errors or failing children (so the output unit still carries the required failure detail).</summary>
    /// <param name="frame">The invalid frame.</param>
    /// <returns>The error unit.</returns>
    private static OutputUnit InlineError(Frame frame)
    {
        return new OutputUnit
        {
            Valid = false,
            KeywordLocation = frame.KeywordLocation.ToString(),
            AbsoluteKeywordLocation = frame.EffectiveBase + "#" + frame.KeywordLocation,
            InstanceLocation = frame.InstanceLocation.ToString(),
            Error = "The instance does not satisfy the schema."
        };
    }

    /// <summary>Collects the keyword locations of every frame in the tree (so a flat error/annotation can be mapped to its owning frame).</summary>
    /// <param name="frame">The current frame.</param>
    /// <param name="keywordLocations">The accumulating set of frame keyword locations.</param>
    private static void CollectFrameKeywordLocations(Frame frame, HashSet<string> keywordLocations)
    {
        keywordLocations.Add(frame.KeywordLocation.ToString());
        foreach(Frame child in frame.Children)
        {
            CollectFrameKeywordLocations(child, keywordLocations);
        }
    }

    /// <summary>Finds the frame a flat error/annotation belongs to: the deepest frame whose keyword location is a prefix of the unit's keyword location.</summary>
    /// <param name="keywordLocation">The unit's keyword location.</param>
    /// <param name="frameKeywordLocations">The set of all frame keyword locations.</param>
    /// <returns>The owning frame's keyword location (the root's empty pointer when none deeper matches).</returns>
    private static string OwningFrameKeywordLocation(string keywordLocation, HashSet<string> frameKeywordLocations)
    {
        string candidate = keywordLocation;
        while(true)
        {
            if(frameKeywordLocations.Contains(candidate))
            {
                return candidate;
            }

            int lastSlash = candidate.LastIndexOf('/');
            if(lastSlash < 0)
            {
                return string.Empty;
            }

            candidate = candidate[..lastSlash];
        }
    }

    /// <summary>Adds a value to a list keyed by <paramref name="key"/>, creating the list on first use.</summary>
    /// <param name="map">The keyed lists.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value to append.</param>
    private static void Add(Dictionary<string, List<OutputUnit>> map, string key, OutputUnit value)
    {
        if(!map.TryGetValue(key, out List<OutputUnit>? list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }

    /// <summary>Whether the Validation vocabulary is in effect for the root schema's declared dialect (<c>$schema</c>).</summary>
    /// <remarks>
    /// The standard 2020-12 dialect and any schema without a recognised metaschema enable validation. A custom
    /// metaschema disables it only by declaring a <c>$vocabulary</c> that omits (or sets to <see langword="false"/>)
    /// the Validation vocabulary. The dialect is read from the root schema; per-resource dialect switching is not modelled.
    /// </remarks>
    /// <param name="schema">The root schema.</param>
    /// <param name="baseUri">The root base URI.</param>
    /// <param name="loader">The document loader, used to fetch a custom metaschema.</param>
    /// <returns><see langword="true"/> when Validation-vocabulary keywords should assert.</returns>
    private static bool IsValidationVocabularyEnabled(JsonNode schema, string baseUri, SchemaDocumentLoader? loader)
    {
        if(schema.Kind != JsonNodeKind.Object || !schema.TryGetProperty(JsonSchemaKeywords.Schema, out JsonNode dialect) || dialect.Kind != JsonNodeKind.String)
        {
            return true;
        }

        string dialectUri = SchemaReferenceIris.Resolve(baseUri, dialect.GetString());
        int hash = dialectUri.IndexOf('#', StringComparison.Ordinal);
        if(hash >= 0)
        {
            dialectUri = dialectUri[..hash];
        }

        if(string.Equals(dialectUri, JsonSchemaKeywords.StandardDialectUri, StringComparison.Ordinal) || loader is null || !loader(dialectUri, out JsonNode metaschema))
        {
            return true;
        }

        if(metaschema.Kind != JsonNodeKind.Object || !metaschema.TryGetProperty(JsonSchemaKeywords.Vocabulary, out JsonNode vocabulary) || vocabulary.Kind != JsonNodeKind.Object)
        {
            return true;
        }

        //A $vocabulary that lists the Validation vocabulary uses its boolean; one that omits it disables validation.
        return vocabulary.TryGetProperty(JsonSchemaKeywords.ValidationVocabularyUri, out JsonNode enabled) && enabled.Kind == JsonNodeKind.True;
    }

    /// <summary>The default regular-expression matcher, backed by <see cref="Regex"/>. Matching is unanchored, as JSON Schema requires.</summary>
    private static RegexMatchDelegate DefaultRegexMatch { get; } = static (pattern, input) => Regex.IsMatch(input, TranslateUnicodePropertyNames(pattern));

    /// <summary>The ECMA-262 long Unicode general-category names mapped to the short codes the BCL <see cref="Regex"/> accepts.</summary>
    private static Dictionary<string, string> UnicodePropertyShortNames { get; } = new(StringComparer.Ordinal)
    {
        ["Letter"] = "L", ["Uppercase_Letter"] = "Lu", ["Lowercase_Letter"] = "Ll", ["Titlecase_Letter"] = "Lt",
        ["Modifier_Letter"] = "Lm", ["Other_Letter"] = "Lo",
        ["Mark"] = "M", ["Nonspacing_Mark"] = "Mn", ["Spacing_Mark"] = "Mc", ["Enclosing_Mark"] = "Me",
        ["Number"] = "N", ["Decimal_Number"] = "Nd", ["Letter_Number"] = "Nl", ["Other_Number"] = "No",
        ["Punctuation"] = "P", ["Connector_Punctuation"] = "Pc", ["Dash_Punctuation"] = "Pd",
        ["Open_Punctuation"] = "Ps", ["Close_Punctuation"] = "Pe", ["Initial_Punctuation"] = "Pi",
        ["Final_Punctuation"] = "Pf", ["Other_Punctuation"] = "Po",
        ["Symbol"] = "S", ["Math_Symbol"] = "Sm", ["Currency_Symbol"] = "Sc", ["Modifier_Symbol"] = "Sk", ["Other_Symbol"] = "So",
        ["Separator"] = "Z", ["Space_Separator"] = "Zs", ["Line_Separator"] = "Zl", ["Paragraph_Separator"] = "Zp",
        ["Other"] = "C", ["Control"] = "Cc", ["Format"] = "Cf", ["Surrogate"] = "Cs", ["Private_Use"] = "Co", ["Unassigned"] = "Cn"
    };

    /// <summary>Matches an ECMA-262 Unicode property escape (<c>\p{Name}</c> / <c>\P{Name}</c>).</summary>
    private static Regex UnicodePropertyEscape { get; } = new(@"\\([pP])\{([A-Za-z_]+)\}", RegexOptions.CultureInvariant);

    /// <summary>Rewrites ECMA-262 long Unicode general-category names (<c>\p{Letter}</c>) to the short codes (<c>\p{L}</c>) the BCL engine understands; other escapes pass through unchanged.</summary>
    /// <param name="pattern">The pattern from the schema.</param>
    /// <returns>The pattern with long property names rewritten.</returns>
    private static string TranslateUnicodePropertyNames(string pattern)
    {
        if(!pattern.Contains("\\p{", StringComparison.Ordinal) && !pattern.Contains("\\P{", StringComparison.Ordinal))
        {
            return pattern;
        }

        return UnicodePropertyEscape.Replace(pattern, static escape =>
            UnicodePropertyShortNames.TryGetValue(escape.Groups[2].Value, out string? shortName)
                ? $"\\{escape.Groups[1].Value}{{{shortName}}}"
                : escape.Value);
    }

    /// <summary>Opens a frame: evaluates its self-contained keywords and pushes child frames for its applicators.</summary>
    /// <param name="frame">The frame being opened.</param>
    /// <param name="stack">The work stack.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void OpenFrame(Frame frame, Stack<(Frame, bool)> stack, List<ValidationError> errors, SchemaRegistry registry, RegexMatchDelegate match, bool validationEnabled)
    {
        //A boolean schema asserts directly: true accepts everything, false rejects everything.
        if(frame.Schema.Kind is JsonNodeKind.True or JsonNodeKind.False)
        {
            frame.SelfValid = frame.Schema.Kind == JsonNodeKind.True;
            if(!frame.SelfValid)
            {
                errors.Add(new ValidationError(frame.InstanceLocation, frame.KeywordLocation, "The boolean schema 'false' rejects all instances."));
            }

            stack.Push((frame, true));

            return;
        }

        //A non-object, non-boolean node in schema position carries no assertions.
        if(frame.Schema.Kind != JsonNodeKind.Object)
        {
            stack.Push((frame, true));

            return;
        }

        //An $id establishes the base URI for this schema's references and subschemas. A reference target
        //reached at its canonical URI already has its $id folded into BaseUri, so it is not applied again.
        //Computed before the assertions so failures can record their absolute keyword location.
        string effectiveBase = frame.BaseUri;
        if(!frame.BaseIsCanonical && frame.Schema.TryGetProperty(JsonSchemaKeywords.Id, out JsonNode id) && id.Kind == JsonNodeKind.String)
        {
            effectiveBase = SchemaReferenceIris.Resolve(frame.BaseUri, id.GetString());
        }

        frame.EffectiveBase = effectiveBase;

        //The Validation vocabulary keywords (type/const/enum/numeric/string/array/object assertions) apply
        //only when that vocabulary is in effect for the dialect; otherwise they are annotations and ignored.
        if(validationEnabled)
        {
            EvaluateSelfKeywords(frame, errors, match);
        }

        //An $id starting a new resource extends the dynamic scope that this frame's subschemas inherit.
        ScopeNode parentScope = frame.DynamicScope ?? new ScopeNode(frame.BaseUri, null);
        ScopeNode childScope = string.Equals(parentScope.BaseUri, effectiveBase, StringComparison.Ordinal) ? parentScope : new ScopeNode(effectiveBase, parentScope);
        frame.ChildScope = childScope;

        //The frame's Close must run after every child, so it is pushed first (children pushed above it pop first).
        stack.Push((frame, true));

        List<ChildRequest> children = [];
        CollectApplicatorChildren(frame, children, match);
        foreach(ChildRequest request in children)
        {
            Frame child = new()
            {
                Schema = request.Schema,
                Instance = request.Instance,
                InstanceLocation = request.InstanceLocation,
                KeywordLocation = request.KeywordLocation,
                Parent = frame,
                SpawnKeyword = request.Keyword,
                MemberName = request.MemberName,
                ItemIndex = request.ItemIndex,
                BaseUri = effectiveBase,
                DynamicScope = childScope
            };

            stack.Push((child, false));
        }

        //$ref evaluates the referenced schema against the same instance node; in 2020-12 its
        //sibling keywords still apply (handled above as ordinary children).
        if(frame.Schema.TryGetProperty(JsonSchemaKeywords.Ref, out JsonNode reference) && reference.Kind == JsonNodeKind.String)
        {
            if(registry.TryResolve(effectiveBase, reference.GetString(), out JsonNode target, out string targetBase))
            {
                stack.Push((MakeReferenceChild(frame, target, targetBase, JsonSchemaKeywords.Ref, childScope), false));
            }
            else
            {
                Fail(frame, errors, JsonSchemaKeywords.Ref, "The reference could not be resolved.");
            }
        }

        //$dynamicRef resolves to the outermost matching $dynamicAnchor in the dynamic scope (or like $ref otherwise).
        if(frame.Schema.TryGetProperty(JsonSchemaKeywords.DynamicRef, out JsonNode dynamicReference) && dynamicReference.Kind == JsonNodeKind.String)
        {
            if(registry.TryResolveDynamic(effectiveBase, dynamicReference.GetString(), OutermostFirst(childScope), out JsonNode target, out string targetBase))
            {
                stack.Push((MakeReferenceChild(frame, target, targetBase, JsonSchemaKeywords.DynamicRef, childScope), false));
            }
            else
            {
                Fail(frame, errors, JsonSchemaKeywords.DynamicRef, "The dynamic reference could not be resolved.");
            }
        }
    }

    /// <summary>Builds a child frame for a resolved reference, extending the dynamic scope with the target resource.</summary>
    /// <param name="frame">The referencing frame.</param>
    /// <param name="target">The resolved target schema.</param>
    /// <param name="targetBase">The base URI in effect at the target.</param>
    /// <param name="keyword">The reference keyword (<c>$ref</c> or <c>$dynamicRef</c>).</param>
    /// <param name="childScope">The dynamic scope in effect at the referencing frame.</param>
    /// <returns>The reference child frame.</returns>
    private static Frame MakeReferenceChild(Frame frame, JsonNode target, string targetBase, string keyword, ScopeNode childScope)
    {
        ScopeNode referenceScope = string.Equals(childScope.BaseUri, targetBase, StringComparison.Ordinal) ? childScope : new ScopeNode(targetBase, childScope);

        return new Frame
        {
            Schema = target,
            Instance = frame.Instance,
            InstanceLocation = frame.InstanceLocation,
            KeywordLocation = frame.KeywordLocation.Append(keyword),
            Parent = frame,
            SpawnKeyword = keyword,
            BaseUri = targetBase,
            BaseIsCanonical = true,
            DynamicScope = referenceScope
        };
    }

    /// <summary>Returns the dynamic scope's resource base URIs, outermost resource first.</summary>
    /// <param name="scope">The dynamic scope (innermost first), or <see langword="null"/>.</param>
    /// <returns>The base URIs, outermost first.</returns>
    private static List<string> OutermostFirst(ScopeNode? scope)
    {
        List<string> bases = [];
        for(ScopeNode? current = scope; current is not null; current = current.Outer)
        {
            bases.Add(current.BaseUri);
        }

        bases.Reverse();

        return bases;
    }

    /// <summary>Closes a frame: combines its children, applies <c>unevaluated*</c> in a second pass, and reports to its parent.</summary>
    /// <param name="frame">The frame being closed.</param>
    /// <param name="stack">The work stack, onto which deferred <c>unevaluated*</c> children are pushed.</param>
    /// <param name="annotations">The accumulating annotation units; this frame contributes its annotation keywords when it passes.</param>
    private static void CloseFrame(Frame frame, Stack<(Frame, bool)> stack, List<OutputUnit> annotations)
    {
        //unevaluatedProperties/unevaluatedItems must see the results of every other keyword first.
        //On the first close, spawn them for the not-yet-evaluated members/items and re-close afterwards.
        if(frame.Schema.Kind == JsonNodeKind.Object && !frame.UnevaluatedSpawned && HasUnevaluatedKeyword(frame.Schema))
        {
            frame.UnevaluatedSpawned = true;
            HashSet<string> evaluatedProperties = ComputeEvaluatedProperties(frame);
            HashSet<int> evaluatedItems = ComputeEvaluatedItems(frame);

            //Push this frame's re-close first so it pops AFTER the unevaluated children pushed on top of it.
            stack.Push((frame, true));
            SpawnUnevaluated(frame, evaluatedProperties, evaluatedItems, stack);

            return;
        }

        frame.Result = frame.SelfValid && CombineChildren(frame);
        frame.EvaluatedProperties.UnionWith(ComputeEvaluatedProperties(frame));
        frame.EvaluatedItems.UnionWith(ComputeEvaluatedItems(frame));
        CollectAnnotations(frame, annotations);
        frame.Parent?.Children.Add(frame);
    }

    /// <summary>Adds annotation units for the pure-annotation keywords a passing object frame declares.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="annotations">The accumulating annotation units.</param>
    private static void CollectAnnotations(Frame frame, List<OutputUnit> annotations)
    {
        if(!frame.Result || frame.Schema.Kind != JsonNodeKind.Object)
        {
            return;
        }

        foreach(string keyword in JsonSchemaKeywords.AnnotationKeywords)
        {
            if(frame.Schema.TryGetProperty(keyword, out JsonNode value))
            {
                Ptr keywordLocation = frame.KeywordLocation.Append(keyword);
                annotations.Add(new OutputUnit
                {
                    Valid = true,
                    KeywordLocation = keywordLocation.ToString(),
                    AbsoluteKeywordLocation = frame.EffectiveBase + "#" + keywordLocation,
                    InstanceLocation = frame.InstanceLocation.ToString(),
                    Annotation = value
                });
            }
        }
    }

    /// <summary>Whether a schema declares <c>unevaluatedProperties</c> or <c>unevaluatedItems</c>.</summary>
    /// <param name="schema">The schema object.</param>
    /// <returns><see langword="true"/> when either keyword is present.</returns>
    private static bool HasUnevaluatedKeyword(JsonNode schema)
    {
        return schema.TryGetProperty(JsonSchemaKeywords.UnevaluatedProperties, out _)
            || schema.TryGetProperty(JsonSchemaKeywords.UnevaluatedItems, out _);
    }

    /// <summary>Spawns <c>unevaluated*</c> child frames for the instance members/items no other keyword evaluated.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="evaluatedProperties">The member names already evaluated.</param>
    /// <param name="evaluatedItems">The element indices already evaluated.</param>
    /// <param name="stack">The work stack.</param>
    /// <returns><see langword="true"/> when at least one child was spawned.</returns>
    private static bool SpawnUnevaluated(Frame frame, HashSet<string> evaluatedProperties, HashSet<int> evaluatedItems, Stack<(Frame, bool)> stack)
    {
        bool spawned = false;

        if(frame.Instance.Kind == JsonNodeKind.Object && frame.Schema.TryGetProperty(JsonSchemaKeywords.UnevaluatedProperties, out JsonNode unevaluatedProperties))
        {
            foreach(KeyValuePair<string, JsonNode> member in frame.Instance.EnumerateObject())
            {
                if(!evaluatedProperties.Contains(member.Key))
                {
                    stack.Push((new Frame
                    {
                        Schema = unevaluatedProperties,
                        Instance = member.Value,
                        InstanceLocation = frame.InstanceLocation.Append(member.Key),
                        KeywordLocation = frame.KeywordLocation.Append(JsonSchemaKeywords.UnevaluatedProperties),
                        Parent = frame,
                        SpawnKeyword = JsonSchemaKeywords.UnevaluatedProperties,
                        MemberName = member.Key,
                        BaseUri = frame.EffectiveBase,
                        DynamicScope = frame.ChildScope
                    }, false));
                    spawned = true;
                }
            }
        }

        if(frame.Instance.Kind == JsonNodeKind.Array && frame.Schema.TryGetProperty(JsonSchemaKeywords.UnevaluatedItems, out JsonNode unevaluatedItems))
        {
            int index = 0;
            foreach(JsonNode element in frame.Instance.EnumerateArray())
            {
                if(!evaluatedItems.Contains(index))
                {
                    stack.Push((new Frame
                    {
                        Schema = unevaluatedItems,
                        Instance = element,
                        InstanceLocation = frame.InstanceLocation.Append(index),
                        KeywordLocation = frame.KeywordLocation.Append(JsonSchemaKeywords.UnevaluatedItems),
                        Parent = frame,
                        SpawnKeyword = JsonSchemaKeywords.UnevaluatedItems,
                        ItemIndex = index,
                        BaseUri = frame.EffectiveBase,
                        DynamicScope = frame.ChildScope
                    }, false));
                    spawned = true;
                }

                index++;
            }
        }

        return spawned;
    }

    /// <summary>Computes the set of this object instance's member names that its passing keywords evaluated.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The evaluated member names (empty when the instance is not an object).</returns>
    private static HashSet<string> ComputeEvaluatedProperties(Frame frame)
    {
        HashSet<string> evaluated = new(StringComparer.Ordinal);
        if(frame.Instance.Kind != JsonNodeKind.Object)
        {
            return evaluated;
        }

        foreach(Frame child in frame.Children)
        {
            if(!child.Result || IsBranchKeyword(child.SpawnKeyword))
            {
                continue;
            }

            if(child.MemberName is not null && IsMemberKeyword(child.SpawnKeyword))
            {
                evaluated.Add(child.MemberName);
            }
            else if(IsInPlaceApplicator(child.SpawnKeyword))
            {
                evaluated.UnionWith(child.EvaluatedProperties);
            }
        }

        UnionBranchEvaluated(frame, evaluated, static child => child.EvaluatedProperties);

        return evaluated;
    }

    /// <summary>Computes the set of this array instance's element indices that its passing keywords evaluated.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The evaluated element indices (empty when the instance is not an array).</returns>
    private static HashSet<int> ComputeEvaluatedItems(Frame frame)
    {
        HashSet<int> evaluated = [];
        if(frame.Instance.Kind != JsonNodeKind.Array)
        {
            return evaluated;
        }

        foreach(Frame child in frame.Children)
        {
            if(!child.Result || IsBranchKeyword(child.SpawnKeyword))
            {
                continue;
            }

            if(child.ItemIndex >= 0 && IsItemKeyword(child.SpawnKeyword))
            {
                evaluated.Add(child.ItemIndex);
            }
            else if(IsInPlaceApplicator(child.SpawnKeyword))
            {
                evaluated.UnionWith(child.EvaluatedItems);
            }
        }

        UnionBranchEvaluated(frame, evaluated, static child => child.EvaluatedItems);

        return evaluated;
    }

    /// <summary>Selects a child frame's evaluated set of the relevant kind (properties or items).</summary>
    /// <typeparam name="T">The set element type.</typeparam>
    /// <param name="frame">The frame to read the evaluated set from.</param>
    /// <returns>The frame's evaluated set.</returns>
    private delegate HashSet<T> EvaluatedSetSelector<T>(Frame frame);

    /// <summary>Unions the evaluated sets contributed by the selected <c>if</c>/<c>then</c>/<c>else</c> branches.</summary>
    /// <typeparam name="T">The set element type.</typeparam>
    /// <param name="frame">The frame.</param>
    /// <param name="evaluated">The accumulating evaluated set.</param>
    /// <param name="selector">Selects a child's evaluated set of the relevant kind.</param>
    private static void UnionBranchEvaluated<T>(Frame frame, HashSet<T> evaluated, EvaluatedSetSelector<T> selector)
    {
        Frame? conditional = FindChild(frame, JsonSchemaKeywords.If);
        if(conditional is null)
        {
            return;
        }

        if(conditional.Result)
        {
            evaluated.UnionWith(selector(conditional));
        }

        Frame? branch = FindChild(frame, conditional.Result ? JsonSchemaKeywords.Then : JsonSchemaKeywords.Else);
        if(branch is { Result: true })
        {
            evaluated.UnionWith(selector(branch));
        }
    }

    /// <summary>Finds this frame's child produced by a given keyword, or <see langword="null"/>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="keyword">The spawning keyword.</param>
    /// <returns>The child frame, or <see langword="null"/>.</returns>
    private static Frame? FindChild(Frame frame, string keyword)
    {
        foreach(Frame child in frame.Children)
        {
            if(ReferenceEquals(child.SpawnKeyword, keyword))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Whether a keyword applies a subschema to an object member (so the member counts as evaluated).</summary>
    /// <param name="keyword">The spawning keyword.</param>
    /// <returns><see langword="true"/> for properties / patternProperties / additionalProperties / unevaluatedProperties.</returns>
    private static bool IsMemberKeyword(string? keyword)
    {
        return ReferenceEquals(keyword, JsonSchemaKeywords.Properties)
            || ReferenceEquals(keyword, JsonSchemaKeywords.PatternProperties)
            || ReferenceEquals(keyword, JsonSchemaKeywords.AdditionalProperties)
            || ReferenceEquals(keyword, JsonSchemaKeywords.UnevaluatedProperties);
    }

    /// <summary>Whether a keyword applies a subschema to an array element (so the element counts as evaluated).</summary>
    /// <param name="keyword">The spawning keyword.</param>
    /// <returns><see langword="true"/> for prefixItems / items / contains / unevaluatedItems.</returns>
    private static bool IsItemKeyword(string? keyword)
    {
        return ReferenceEquals(keyword, JsonSchemaKeywords.PrefixItems)
            || ReferenceEquals(keyword, JsonSchemaKeywords.Items)
            || ReferenceEquals(keyword, JsonSchemaKeywords.Contains)
            || ReferenceEquals(keyword, JsonSchemaKeywords.UnevaluatedItems);
    }

    /// <summary>Whether a keyword is an in-place applicator that shares the instance node and propagates evaluated sets.</summary>
    /// <param name="keyword">The spawning keyword.</param>
    /// <returns><see langword="true"/> for allOf / anyOf / oneOf / $ref / dependentSchemas.</returns>
    private static bool IsInPlaceApplicator(string? keyword)
    {
        return ReferenceEquals(keyword, JsonSchemaKeywords.AllOf)
            || ReferenceEquals(keyword, JsonSchemaKeywords.AnyOf)
            || ReferenceEquals(keyword, JsonSchemaKeywords.OneOf)
            || ReferenceEquals(keyword, JsonSchemaKeywords.Ref)
            || ReferenceEquals(keyword, JsonSchemaKeywords.DynamicRef)
            || ReferenceEquals(keyword, JsonSchemaKeywords.DependentSchemas);
    }

    /// <summary>Whether a keyword is part of the <c>if</c>/<c>then</c>/<c>else</c> conditional (combined by branch selection).</summary>
    /// <param name="keyword">The spawning keyword.</param>
    /// <returns><see langword="true"/> for if / then / else.</returns>
    private static bool IsBranchKeyword(string? keyword)
    {
        return ReferenceEquals(keyword, JsonSchemaKeywords.If)
            || ReferenceEquals(keyword, JsonSchemaKeywords.Then)
            || ReferenceEquals(keyword, JsonSchemaKeywords.Else);
    }

    /// <summary>Combines a frame's applicator-child results, grouped by the keyword that produced them.</summary>
    /// <param name="frame">The frame whose children are combined.</param>
    /// <returns><see langword="true"/> when every applicator group is satisfied.</returns>
    private static bool CombineChildren(Frame frame)
    {
        Dictionary<string, (int Total, int Valid)> groups = new(StringComparer.Ordinal);
        foreach(Frame child in frame.Children)
        {
            (int total, int validCount) = groups.TryGetValue(child.SpawnKeyword!, out (int Total, int Valid) existing) ? existing : (0, 0);
            groups[child.SpawnKeyword!] = (total + 1, validCount + (child.Result ? 1 : 0));
        }

        //if/then/else and contains read sibling keywords, which only exist on an object schema
        //(a boolean schema carries none, and JsonNode.TryGetProperty is defined only for objects).
        if(frame.Schema.Kind == JsonNodeKind.Object && (!CombineIfThenElse(frame, groups) || !CombineContains(frame, groups)))
        {
            return false;
        }

        foreach(KeyValuePair<string, (int Total, int Valid)> group in groups)
        {
            string keyword = group.Key;

            //if / then / else and contains are combined above with their sibling keywords.
            if(ReferenceEquals(keyword, JsonSchemaKeywords.If)
                || ReferenceEquals(keyword, JsonSchemaKeywords.Then)
                || ReferenceEquals(keyword, JsonSchemaKeywords.Else)
                || ReferenceEquals(keyword, JsonSchemaKeywords.Contains))
            {
                continue;
            }

            (int total, int valid) = group.Value;
            bool satisfied =
                ReferenceEquals(keyword, JsonSchemaKeywords.AnyOf) ? valid >= 1 :
                ReferenceEquals(keyword, JsonSchemaKeywords.OneOf) ? valid == 1 :
                ReferenceEquals(keyword, JsonSchemaKeywords.Not) ? valid == 0 :
                valid == total;

            if(!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Combines the <c>if</c>/<c>then</c>/<c>else</c> conditional: <c>if</c> selects which branch must hold.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="groups">The child-result groups.</param>
    /// <returns><see langword="true"/> when the selected branch holds (or the conditional is absent).</returns>
    private static bool CombineIfThenElse(Frame frame, Dictionary<string, (int Total, int Valid)> groups)
    {
        if(!frame.Schema.TryGetProperty(JsonSchemaKeywords.If, out _))
        {
            return true;
        }

        bool conditionHeld = groups.TryGetValue(JsonSchemaKeywords.If, out (int Total, int Valid) condition) && condition.Total > 0 && condition.Valid == condition.Total;
        string branch = conditionHeld ? JsonSchemaKeywords.Then : JsonSchemaKeywords.Else;

        return !groups.TryGetValue(branch, out (int Total, int Valid) result) || result.Valid == result.Total;
    }

    /// <summary>Combines <c>contains</c> with its <c>minContains</c>/<c>maxContains</c> bounds.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="groups">The child-result groups.</param>
    /// <returns><see langword="true"/> when the match count satisfies the bounds (or <c>contains</c> does not apply).</returns>
    private static bool CombineContains(Frame frame, Dictionary<string, (int Total, int Valid)> groups)
    {
        if(frame.Instance.Kind != JsonNodeKind.Array || !frame.Schema.TryGetProperty(JsonSchemaKeywords.Contains, out _))
        {
            return true;
        }

        int matched = groups.TryGetValue(JsonSchemaKeywords.Contains, out (int Total, int Valid) contains) ? contains.Valid : 0;
        int minimum = ReadIntKeyword(frame.Schema, JsonSchemaKeywords.MinContains, 1);
        if(matched < minimum)
        {
            return false;
        }

        return !frame.Schema.TryGetProperty(JsonSchemaKeywords.MaxContains, out JsonNode maximum)
            || maximum.Kind != JsonNodeKind.Number
            || matched <= ToInt(maximum);
    }

    /// <summary>Reads an integer keyword value, or a default when the keyword is absent or not a number.</summary>
    /// <param name="schema">The schema object.</param>
    /// <param name="keyword">The keyword name.</param>
    /// <param name="fallback">The value to use when the keyword is absent or non-numeric.</param>
    /// <returns>The integer value or the fallback.</returns>
    private static int ReadIntKeyword(JsonNode schema, string keyword, int fallback)
    {
        return schema.TryGetProperty(keyword, out JsonNode value) && value.Kind == JsonNodeKind.Number ? ToInt(value) : fallback;
    }

    /// <summary>Evaluates the keywords whose assertion depends only on this instance node (not on subschemas).</summary>
    /// <param name="frame">The frame whose schema and instance are evaluated.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateSelfKeywords(Frame frame, List<ValidationError> errors, RegexMatchDelegate match)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;

        if(schema.TryGetProperty(JsonSchemaKeywords.Type, out JsonNode typeNode) && !MatchesType(instance, typeNode))
        {
            Fail(frame, errors, JsonSchemaKeywords.Type, "The instance is not of the expected type.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Const, out JsonNode constNode) && !JsonValueComparer.Equal(instance, constNode))
        {
            Fail(frame, errors, JsonSchemaKeywords.Const, "The instance does not equal the constant value.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Enum, out JsonNode enumNode) && enumNode.Kind == JsonNodeKind.Array && !EnumContains(enumNode, instance))
        {
            Fail(frame, errors, JsonSchemaKeywords.Enum, "The instance is not one of the enumerated values.");
        }

        EvaluateNumericKeywords(frame, errors);
        EvaluateStringKeywords(frame, errors, match);
        EvaluateArraySelfKeywords(frame, errors);
        EvaluateObjectSelfKeywords(frame, errors);
    }

    /// <summary>Evaluates the numeric assertions (<c>multipleOf</c>, the range bounds) when the instance is a number.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateNumericKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Number)
        {
            return;
        }

        string value = instance.GetRawNumber();

        if(schema.TryGetProperty(JsonSchemaKeywords.MultipleOf, out JsonNode multiple) && multiple.Kind == JsonNodeKind.Number && !IsMultipleOf(value, multiple.GetRawNumber()))
        {
            Fail(frame, errors, JsonSchemaKeywords.MultipleOf, "The number is not a multiple of the required value.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Maximum, out JsonNode maximum) && maximum.Kind == JsonNodeKind.Number && CompareNumbers(value, maximum.GetRawNumber()) > 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.Maximum, "The number exceeds the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.ExclusiveMaximum, out JsonNode exclusiveMaximum) && exclusiveMaximum.Kind == JsonNodeKind.Number && CompareNumbers(value, exclusiveMaximum.GetRawNumber()) >= 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.ExclusiveMaximum, "The number is not below the exclusive maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Minimum, out JsonNode minimum) && minimum.Kind == JsonNodeKind.Number && CompareNumbers(value, minimum.GetRawNumber()) < 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.Minimum, "The number is below the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.ExclusiveMinimum, out JsonNode exclusiveMinimum) && exclusiveMinimum.Kind == JsonNodeKind.Number && CompareNumbers(value, exclusiveMinimum.GetRawNumber()) <= 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.ExclusiveMinimum, "The number is not above the exclusive minimum.");
        }
    }

    /// <summary>Evaluates the string assertions (length bounds, <c>pattern</c>) when the instance is a string.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateStringKeywords(Frame frame, List<ValidationError> errors, RegexMatchDelegate match)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.String)
        {
            return;
        }

        string text = instance.GetString();

        if(schema.TryGetProperty(JsonSchemaKeywords.MinLength, out JsonNode minLength) && minLength.Kind == JsonNodeKind.Number && CodePointCount(text) < ToInt(minLength))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinLength, "The string is shorter than the minimum length.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxLength, out JsonNode maxLength) && maxLength.Kind == JsonNodeKind.Number && CodePointCount(text) > ToInt(maxLength))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxLength, "The string is longer than the maximum length.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Pattern, out JsonNode pattern) && pattern.Kind == JsonNodeKind.String && !match(pattern.GetString(), text))
        {
            Fail(frame, errors, JsonSchemaKeywords.Pattern, "The string does not match the required pattern.");
        }
    }

    /// <summary>Evaluates the array assertions that do not apply subschemas (length bounds, <c>uniqueItems</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateArraySelfKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Array)
        {
            return;
        }

        List<JsonNode> items = [.. instance.EnumerateArray()];

        if(schema.TryGetProperty(JsonSchemaKeywords.MinItems, out JsonNode minItems) && minItems.Kind == JsonNodeKind.Number && items.Count < ToInt(minItems))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinItems, "The array has fewer items than the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxItems, out JsonNode maxItems) && maxItems.Kind == JsonNodeKind.Number && items.Count > ToInt(maxItems))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxItems, "The array has more items than the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.UniqueItems, out JsonNode unique) && unique.Kind == JsonNodeKind.True && !AllUnique(items))
        {
            Fail(frame, errors, JsonSchemaKeywords.UniqueItems, "The array items are not unique.");
        }
    }

    /// <summary>Evaluates the object assertions that do not apply subschemas (member-count bounds, <c>required</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateObjectSelfKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Object)
        {
            return;
        }

        Dictionary<string, JsonNode> members = ReadObject(instance);

        if(schema.TryGetProperty(JsonSchemaKeywords.MinProperties, out JsonNode minProperties) && minProperties.Kind == JsonNodeKind.Number && members.Count < ToInt(minProperties))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinProperties, "The object has fewer members than the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxProperties, out JsonNode maxProperties) && maxProperties.Kind == JsonNodeKind.Number && members.Count > ToInt(maxProperties))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxProperties, "The object has more members than the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Required, out JsonNode required) && required.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode name in required.EnumerateArray())
            {
                if(name.Kind == JsonNodeKind.String && !members.ContainsKey(name.GetString()))
                {
                    Fail(frame, errors, JsonSchemaKeywords.Required, "A required member is missing.");
                }
            }
        }

        //dependentRequired: when a triggering member is present, its dependent members must also be present.
        if(schema.TryGetProperty(JsonSchemaKeywords.DependentRequired, out JsonNode dependentRequired) && dependentRequired.Kind == JsonNodeKind.Object)
        {
            foreach(KeyValuePair<string, JsonNode> dependency in dependentRequired.EnumerateObject())
            {
                if(!members.ContainsKey(dependency.Key) || dependency.Value.Kind != JsonNodeKind.Array)
                {
                    continue;
                }

                foreach(JsonNode dependent in dependency.Value.EnumerateArray())
                {
                    if(dependent.Kind == JsonNodeKind.String && !members.ContainsKey(dependent.GetString()))
                    {
                        Fail(frame, errors, JsonSchemaKeywords.DependentRequired, "A dependent required member is missing.");
                    }
                }
            }
        }
    }

    /// <summary>Collects the child frame requests for this frame's applicator keywords.</summary>
    /// <param name="frame">The frame whose applicators are expanded.</param>
    /// <param name="requests">The list to populate with child requests.</param>
    private static void CollectApplicatorChildren(Frame frame, List<ChildRequest> requests, RegexMatchDelegate match)
    {
        JsonNode schema = frame.Schema;

        //allOf / anyOf / oneOf: each array element is a subschema applied to the same instance node.
        CollectSchemaArray(frame, JsonSchemaKeywords.AllOf, requests);
        CollectSchemaArray(frame, JsonSchemaKeywords.AnyOf, requests);
        CollectSchemaArray(frame, JsonSchemaKeywords.OneOf, requests);

        //not: a single subschema applied to the same instance node.
        if(schema.TryGetProperty(JsonSchemaKeywords.Not, out JsonNode not))
        {
            requests.Add(new ChildRequest(JsonSchemaKeywords.Not, not, frame.Instance, frame.InstanceLocation, frame.KeywordLocation.Append(JsonSchemaKeywords.Not), null, -1));
        }

        //if / then / else: each is a subschema applied to the same instance node; the combiner selects the branch.
        CollectSingleSubschema(frame, JsonSchemaKeywords.If, requests);
        CollectSingleSubschema(frame, JsonSchemaKeywords.Then, requests);
        CollectSingleSubschema(frame, JsonSchemaKeywords.Else, requests);

        if(frame.Instance.Kind == JsonNodeKind.Object)
        {
            CollectObjectApplicators(frame, requests, match);
        }

        if(frame.Instance.Kind == JsonNodeKind.Array)
        {
            CollectArrayApplicators(frame, requests);
        }
    }

    /// <summary>Collects child requests for a keyword whose value is an array of subschemas (<c>allOf</c>/<c>anyOf</c>/<c>oneOf</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="keyword">The keyword name.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectSchemaArray(Frame frame, string keyword, List<ChildRequest> requests)
    {
        if(!frame.Schema.TryGetProperty(keyword, out JsonNode array) || array.Kind != JsonNodeKind.Array)
        {
            return;
        }

        int index = 0;
        foreach(JsonNode subschema in array.EnumerateArray())
        {
            requests.Add(new ChildRequest(keyword, subschema, frame.Instance, frame.InstanceLocation, frame.KeywordLocation.Append(keyword).Append(index), null, -1));
            index++;
        }
    }

    /// <summary>Collects a child request for a keyword whose value is a single subschema applied to the same instance node.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="keyword">The keyword name.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectSingleSubschema(Frame frame, string keyword, List<ChildRequest> requests)
    {
        if(frame.Schema.TryGetProperty(keyword, out JsonNode subschema))
        {
            requests.Add(new ChildRequest(keyword, subschema, frame.Instance, frame.InstanceLocation, frame.KeywordLocation.Append(keyword), null, -1));
        }
    }

    /// <summary>Collects <c>properties</c> and <c>additionalProperties</c> child requests for an object instance.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectObjectApplicators(Frame frame, List<ChildRequest> requests, RegexMatchDelegate match)
    {
        JsonNode schema = frame.Schema;
        Dictionary<string, JsonNode> members = ReadObject(frame.Instance);

        HashSet<string> covered = new(StringComparer.Ordinal);
        if(schema.TryGetProperty(JsonSchemaKeywords.Properties, out JsonNode properties) && properties.Kind == JsonNodeKind.Object)
        {
            foreach(KeyValuePair<string, JsonNode> definition in properties.EnumerateObject())
            {
                covered.Add(definition.Key);
                if(members.TryGetValue(definition.Key, out JsonNode value))
                {
                    requests.Add(new ChildRequest(
                        JsonSchemaKeywords.Properties,
                        definition.Value,
                        value,
                        frame.InstanceLocation.Append(definition.Key),
                        frame.KeywordLocation.Append(JsonSchemaKeywords.Properties).Append(definition.Key),
                        definition.Key,
                        -1));
                }
            }
        }

        //patternProperties applies to every member whose name matches the pattern, and those names also
        //count as covered so additionalProperties does not see them.
        if(schema.TryGetProperty(JsonSchemaKeywords.PatternProperties, out JsonNode patternProperties) && patternProperties.Kind == JsonNodeKind.Object)
        {
            foreach(KeyValuePair<string, JsonNode> definition in patternProperties.EnumerateObject())
            {
                foreach(KeyValuePair<string, JsonNode> member in members)
                {
                    if(match(definition.Key, member.Key))
                    {
                        covered.Add(member.Key);
                        requests.Add(new ChildRequest(
                            JsonSchemaKeywords.PatternProperties,
                            definition.Value,
                            member.Value,
                            frame.InstanceLocation.Append(member.Key),
                            frame.KeywordLocation.Append(JsonSchemaKeywords.PatternProperties).Append(definition.Key),
                            member.Key,
                            -1));
                    }
                }
            }
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.AdditionalProperties, out JsonNode additional))
        {
            foreach(KeyValuePair<string, JsonNode> member in members)
            {
                if(!covered.Contains(member.Key))
                {
                    requests.Add(new ChildRequest(
                        JsonSchemaKeywords.AdditionalProperties,
                        additional,
                        member.Value,
                        frame.InstanceLocation.Append(member.Key),
                        frame.KeywordLocation.Append(JsonSchemaKeywords.AdditionalProperties),
                        member.Key,
                        -1));
                }
            }
        }

        //propertyNames validates each member name as a synthesized string instance.
        if(schema.TryGetProperty(JsonSchemaKeywords.PropertyNames, out JsonNode propertyNames))
        {
            foreach(KeyValuePair<string, JsonNode> member in members)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.PropertyNames,
                    propertyNames,
                    LiteralJsonNode.String(member.Key),
                    frame.InstanceLocation.Append(member.Key),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.PropertyNames),
                    null,
                    -1));
            }
        }

        //dependentSchemas applies a subschema to the whole object when a triggering member is present.
        if(schema.TryGetProperty(JsonSchemaKeywords.DependentSchemas, out JsonNode dependentSchemas) && dependentSchemas.Kind == JsonNodeKind.Object)
        {
            foreach(KeyValuePair<string, JsonNode> definition in dependentSchemas.EnumerateObject())
            {
                if(members.ContainsKey(definition.Key))
                {
                    requests.Add(new ChildRequest(
                        JsonSchemaKeywords.DependentSchemas,
                        definition.Value,
                        frame.Instance,
                        frame.InstanceLocation,
                        frame.KeywordLocation.Append(JsonSchemaKeywords.DependentSchemas).Append(definition.Key),
                        null,
                        -1));
                }
            }
        }
    }

    /// <summary>Collects <c>prefixItems</c> and <c>items</c> child requests for an array instance.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectArrayApplicators(Frame frame, List<ChildRequest> requests)
    {
        JsonNode schema = frame.Schema;
        List<JsonNode> elements = [.. frame.Instance.EnumerateArray()];

        int prefixCount = 0;
        if(schema.TryGetProperty(JsonSchemaKeywords.PrefixItems, out JsonNode prefixItems) && prefixItems.Kind == JsonNodeKind.Array)
        {
            List<JsonNode> prefixSchemas = [.. prefixItems.EnumerateArray()];
            prefixCount = prefixSchemas.Count;
            for(int i = 0; i < prefixSchemas.Count && i < elements.Count; i++)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.PrefixItems,
                    prefixSchemas[i],
                    elements[i],
                    frame.InstanceLocation.Append(i),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.PrefixItems).Append(i),
                    null,
                    i));
            }
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Items, out JsonNode items))
        {
            for(int i = prefixCount; i < elements.Count; i++)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.Items,
                    items,
                    elements[i],
                    frame.InstanceLocation.Append(i),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.Items),
                    null,
                    i));
            }
        }

        //contains applies to every element; the combiner counts the matches against min/maxContains.
        if(schema.TryGetProperty(JsonSchemaKeywords.Contains, out JsonNode contains))
        {
            for(int i = 0; i < elements.Count; i++)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.Contains,
                    contains,
                    elements[i],
                    frame.InstanceLocation.Append(i),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.Contains),
                    null,
                    i));
            }
        }
    }

    /// <summary>Records an assertion failure on a frame and in the error list.</summary>
    /// <param name="frame">The frame whose self validity is cleared.</param>
    /// <param name="errors">The accumulating error list.</param>
    /// <param name="keyword">The failing keyword.</param>
    /// <param name="message">The failure description.</param>
    private static void Fail(Frame frame, List<ValidationError> errors, string keyword, string message)
    {
        frame.SelfValid = false;
        Ptr keywordLocation = frame.KeywordLocation.Append(keyword);
        errors.Add(new ValidationError(frame.InstanceLocation, keywordLocation, message)
        {
            AbsoluteKeywordLocation = frame.EffectiveBase + "#" + keywordLocation
        });
    }

    /// <summary>Whether the instance matches a <c>type</c> value (a single type name or an array of names).</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="typeNode">The <c>type</c> keyword value.</param>
    /// <returns><see langword="true"/> when the instance matches any named type.</returns>
    private static bool MatchesType(JsonNode instance, JsonNode typeNode)
    {
        if(typeNode.Kind == JsonNodeKind.String)
        {
            return MatchesTypeName(instance, typeNode.GetString());
        }

        if(typeNode.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode name in typeNode.EnumerateArray())
            {
                if(name.Kind == JsonNodeKind.String && MatchesTypeName(instance, name.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the instance matches a single JSON Schema primitive type name.</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="typeName">The type name (<c>null</c>, <c>boolean</c>, <c>object</c>, <c>array</c>, <c>string</c>, <c>number</c>, <c>integer</c>).</param>
    /// <returns><see langword="true"/> when the instance is of that type.</returns>
    private static bool MatchesTypeName(JsonNode instance, string typeName)
    {
        return typeName switch
        {
            "null" => instance.Kind == JsonNodeKind.Null,
            "boolean" => instance.Kind is JsonNodeKind.True or JsonNodeKind.False,
            "object" => instance.Kind == JsonNodeKind.Object,
            "array" => instance.Kind == JsonNodeKind.Array,
            "string" => instance.Kind == JsonNodeKind.String,
            "number" => instance.Kind == JsonNodeKind.Number,
            "integer" => instance.Kind == JsonNodeKind.Number && IsIntegral(instance.GetRawNumber()),
            _ => false
        };
    }

    /// <summary>Whether an <c>enum</c> array contains a value equal to the instance.</summary>
    /// <param name="enumNode">The <c>enum</c> array.</param>
    /// <param name="instance">The instance.</param>
    /// <returns><see langword="true"/> when a member equals the instance.</returns>
    private static bool EnumContains(JsonNode enumNode, JsonNode instance)
    {
        foreach(JsonNode candidate in enumNode.EnumerateArray())
        {
            if(JsonValueComparer.Equal(instance, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether every element of a list is distinct under JSON Schema value equality.</summary>
    /// <param name="items">The array elements.</param>
    /// <returns><see langword="true"/> when no two elements are equal.</returns>
    private static bool AllUnique(List<JsonNode> items)
    {
        for(int i = 0; i < items.Count; i++)
        {
            for(int j = i + 1; j < items.Count; j++)
            {
                if(JsonValueComparer.Equal(items[i], items[j]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a number's lexical form denotes an integer (a zero fractional part).</summary>
    /// <param name="raw">The raw number.</param>
    /// <returns><see langword="true"/> when the value is integral.</returns>
    private static bool IsIntegral(string raw)
    {
        if(decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            return value == Math.Truncate(value);
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
            && double.IsFinite(real)
            && real == Math.Truncate(real);
    }

    /// <summary>Whether <paramref name="value"/> is an exact multiple of <paramref name="factor"/>.</summary>
    /// <param name="value">The instance number's raw form.</param>
    /// <param name="factor">The <c>multipleOf</c> value's raw form.</param>
    /// <returns><see langword="true"/> when the division leaves no remainder.</returns>
    private static bool IsMultipleOf(string value, string factor)
    {
        if(decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal valueDecimal)
            && decimal.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal factorDecimal))
        {
            return factorDecimal != 0 && valueDecimal % factorDecimal == 0;
        }

        if(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double valueDouble)
            && double.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out double factorDouble)
            && factorDouble != 0)
        {
            double quotient = valueDouble / factorDouble;

            return double.IsFinite(quotient) && quotient == Math.Round(quotient);
        }

        return false;
    }

    /// <summary>Compares two number lexical forms by mathematical value.</summary>
    /// <param name="left">The first raw number.</param>
    /// <param name="right">The second raw number.</param>
    /// <returns>A negative value, zero, or a positive value as <paramref name="left"/> is less than, equal to, or greater than <paramref name="right"/>.</returns>
    private static int CompareNumbers(string left, string right)
    {
        if(decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal leftDecimal)
            && decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        double leftDouble = double.Parse(left, NumberStyles.Float, CultureInfo.InvariantCulture);
        double rightDouble = double.Parse(right, NumberStyles.Float, CultureInfo.InvariantCulture);

        return leftDouble.CompareTo(rightDouble);
    }

    /// <summary>Reads a numeric keyword value as an <see cref="int"/>.</summary>
    /// <param name="node">The numeric node.</param>
    /// <returns>The integer value (truncated toward zero).</returns>
    private static int ToInt(JsonNode node)
    {
        return (int)double.Parse(node.GetRawNumber(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Counts the Unicode code points in a string (JSON Schema length is measured in code points).</summary>
    /// <param name="text">The string.</param>
    /// <returns>The code-point count.</returns>
    private static int CodePointCount(string text)
    {
        int count = 0;
        foreach(System.Text.Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    /// <summary>Materialises an object instance's members into a name-to-value map.</summary>
    /// <param name="instance">The object instance.</param>
    /// <returns>The member map.</returns>
    private static Dictionary<string, JsonNode> ReadObject(JsonNode instance)
    {
        Dictionary<string, JsonNode> members = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> member in instance.EnumerateObject())
        {
            members[member.Key] = member.Value;
        }

        return members;
    }

    /// <summary>A pending subschema evaluation produced by an applicator keyword.</summary>
    /// <param name="Keyword">The applicator keyword that produced this child.</param>
    /// <param name="Schema">The subschema node.</param>
    /// <param name="Instance">The instance node the subschema applies to.</param>
    /// <param name="InstanceLocation">The child's instance location.</param>
    /// <param name="KeywordLocation">The child's keyword location.</param>
    /// <param name="MemberName">For an object-member applicator, the member name the subschema applies to; otherwise <see langword="null"/>.</param>
    /// <param name="ItemIndex">For an array-element applicator, the element index the subschema applies to; otherwise -1.</param>
    private readonly record struct ChildRequest(
        string Keyword,
        JsonNode Schema,
        JsonNode Instance,
        Ptr InstanceLocation,
        Ptr KeywordLocation,
        string? MemberName,
        int ItemIndex);

    /// <summary>One node of the evaluation: a schema applied to an instance node, with its place in the result tree.</summary>
    private sealed class Frame
    {
        /// <summary>Gets the schema node being evaluated.</summary>
        public required JsonNode Schema { get; init; }

        /// <summary>Gets the instance node being evaluated.</summary>
        public required JsonNode Instance { get; init; }

        /// <summary>Gets the instance location of this frame.</summary>
        public required Ptr InstanceLocation { get; init; }

        /// <summary>Gets the keyword location of this frame.</summary>
        public required Ptr KeywordLocation { get; init; }

        /// <summary>Gets the base URI in effect for this frame's references and subschemas.</summary>
        public required string BaseUri { get; init; }

        /// <summary>Gets the dynamic scope in effect at this frame (innermost resource first), for <c>$dynamicRef</c> resolution.</summary>
        public ScopeNode? DynamicScope { get; init; }

        /// <summary>Gets or sets the dynamic scope this frame's children inherit (this frame's scope extended by its own <c>$id</c>).</summary>
        public ScopeNode? ChildScope { get; set; }

        /// <summary>Gets the parent frame this frame reports its result to, or <see langword="null"/> for the root.</summary>
        public Frame? Parent { get; init; }

        /// <summary>Gets the applicator keyword that produced this frame, or <see langword="null"/> for the root.</summary>
        public string? SpawnKeyword { get; init; }

        /// <summary>Gets a value indicating whether this frame's <c>$id</c> is already reflected in <see cref="BaseUri"/> (a reference target reached at its canonical URI), so it must not be applied again.</summary>
        public bool BaseIsCanonical { get; init; }

        /// <summary>Gets the object member name this frame applies to, or <see langword="null"/> when it is not a member applicator.</summary>
        public string? MemberName { get; init; }

        /// <summary>Gets the array element index this frame applies to, or -1 when it is not an element applicator.</summary>
        public int ItemIndex { get; init; } = -1;

        /// <summary>Gets or sets the base URI established by this frame's own <c>$id</c> (or its inherited base), for closing-time work.</summary>
        public string EffectiveBase { get; set; } = string.Empty;

        /// <summary>Gets or sets whether this frame's self-contained keywords held.</summary>
        public bool SelfValid { get; set; } = true;

        /// <summary>Gets or sets the combined result of this frame, set when it closes.</summary>
        public bool Result { get; set; }

        /// <summary>Gets or sets whether this frame has already spawned its <c>unevaluated*</c> children (the second close finalizes).</summary>
        public bool UnevaluatedSpawned { get; set; }

        /// <summary>Gets the closed child frames this frame's applicators produced.</summary>
        public List<Frame> Children { get; } = [];

        /// <summary>Gets the names of this object instance's members that were evaluated, computed when the frame closes.</summary>
        public HashSet<string> EvaluatedProperties { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the indices of this array instance's elements that were evaluated, computed when the frame closes.</summary>
        public HashSet<int> EvaluatedItems { get; } = [];
    }

    /// <summary>One entry of the dynamic scope: a schema-resource base URI and the enclosing scope.</summary>
    /// <param name="BaseUri">The resource base URI (innermost when this is the scope head).</param>
    /// <param name="Outer">The enclosing dynamic scope, or <see langword="null"/> at the root.</param>
    private sealed record ScopeNode(string BaseUri, ScopeNode? Outer);
}
