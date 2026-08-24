using System;
using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 Framing Algorithm
/// (<see href="https://www.w3.org/TR/json-ld11-framing/#framing-algorithm"/>):
/// matches an expanded frame against the node map of expanded input and builds
/// an embedded output tree, honouring the framing flags (<c>@embed</c>,
/// <c>@explicit</c>, <c>@requireAll</c>, <c>@default</c>, <c>@omitDefault</c>),
/// frame-vs-value matching, and circular-reference protection.
/// </summary>
/// <remarks>
/// The framed output carries <c>@preserve</c> wrappers (around defaulted
/// values) and <c>@null</c> sentinels; the surrounding pipeline compacts it
/// against the frame's context and then strips those via <see cref="CleanupNull"/>.
/// </remarks>
public static class JsonLdFramer
{
    /// <summary>
    /// Frames <paramref name="expanded"/> with <paramref name="expandedFrame"/>,
    /// producing the framed expanded output (still carrying <c>@preserve</c>).
    /// </summary>
    /// <param name="expanded">The expanded input document.</param>
    /// <param name="expandedFrame">The expanded frame document.</param>
    /// <param name="options">The framing options.</param>
    /// <returns>The framed output array.</returns>
    public static List<object?> Frame(IReadOnlyList<object?> expanded, IReadOnlyList<object?> expandedFrame, JsonLdFrameOptions options)
    {
        ArgumentNullException.ThrowIfNull(expanded);
        ArgumentNullException.ThrowIfNull(expandedFrame);
        ArgumentNullException.ThrowIfNull(options);

        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphMap = JsonLdNodeMap.Generate(expanded);
        string graph = JsonLdNodeMap.DefaultGraph;
        if(options.Merged)
        {
            graphMap[JsonLdNodeMap.MergedGraph] = JsonLdNodeMap.MergeAllGraphs(graphMap);
            graph = JsonLdNodeMap.MergedGraph;
        }

        FramingState state = new(options, graphMap, graph);

        List<object?> framed = new();
        List<string> subjects = new(state.Subjects.Keys);
        subjects.Sort(StringComparer.Ordinal);
        FrameSubjects(state, subjects, expandedFrame, framed, property: null);

        List<object?> result = CleanupPreserve(framed);

        if(options.PruneBlankNodeIdentifiers)
        {
            //A blank node named exactly once carries no shared identity, so its @id is dropped.
            HashSet<string> prune = new(StringComparer.Ordinal);
            foreach(KeyValuePair<string, List<Dictionary<string, object?>>> entry in state.BlankNodeMap)
            {
                if(entry.Value.Count == 1)
                {
                    prune.Add(entry.Key);
                }
            }

            options.BlankNodesToClear.Clear();
            options.BlankNodesToClear.UnionWith(prune);
            foreach(object? node in result)
            {
                PruneBlankNodeIds(node, prune);
            }
        }

        return result;
    }

    /// <summary>Recursively removes <c>@id</c> entries whose value is a single-use blank node (in <paramref name="prune"/>).</summary>
    /// <param name="value">The framed value.</param>
    /// <param name="prune">The single-use blank-node identifiers to drop.</param>
    private static void PruneBlankNodeIds(object? value, HashSet<string> prune)
    {
        if(value is List<object?> list)
        {
            foreach(object? item in list)
            {
                PruneBlankNodeIds(item, prune);
            }

            return;
        }

        if(value is Dictionary<string, object?> map)
        {
            if(map.TryGetValue(JsonLdKeywords.Id, out object? id) && id is string blankId && prune.Contains(blankId))
            {
                map.Remove(JsonLdKeywords.Id);
            }

            foreach(object? child in map.Values)
            {
                PruneBlankNodeIds(child, prune);
            }
        }
    }

    /// <summary>Frames a set of subjects against a frame, adding the matched, embedded output to <paramref name="parent"/>.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="subjects">The candidate subject ids.</param>
    /// <param name="frameArray">The frame (a single-element array per the spec).</param>
    /// <param name="parent">The parent collection (top-level list or a node object).</param>
    /// <param name="property">The parent property, or <see langword="null"/> at the top level.</param>
    private static void FrameSubjects(FramingState state, IReadOnlyList<string> subjects, IReadOnlyList<object?> frameArray, object parent, string? property)
    {
        ValidateFrame(frameArray);
        Dictionary<string, object?> frame = (Dictionary<string, object?>)frameArray[0]!;

        FrameFlags flags = new(
            Embed: GetEmbedFlag(frame, state.Options),
            Explicit: GetBoolFlag(frame, JsonLdKeywords.Explicit, state.Options.Explicit),
            RequireAll: GetBoolFlag(frame, JsonLdKeywords.RequireAll, state.Options.RequireAll));

        if(!state.Link.TryGetValue(state.Graph, out Dictionary<string, Dictionary<string, object?>>? link))
        {
            link = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            state.Link[state.Graph] = link;
        }

        Dictionary<string, Dictionary<string, object?>> matches = FilterSubjects(state, subjects, frame, flags);
        List<string> ids = new(matches.Keys);
        ids.Sort(StringComparer.Ordinal);

        foreach(string id in ids)
        {
            Dictionary<string, object?> subject = matches[id];

            //Each top-level match is its own compartment of unique embeds.
            if(property is null)
            {
                state.UniqueEmbeds = new Dictionary<string, Dictionary<string, EmbedReference>>(StringComparer.Ordinal)
                {
                    [state.Graph] = new Dictionary<string, EmbedReference>(StringComparer.Ordinal)
                };
            }
            else if(!state.UniqueEmbeds.ContainsKey(state.Graph))
            {
                state.UniqueEmbeds[state.Graph] = new Dictionary<string, EmbedReference>(StringComparer.Ordinal);
            }

            Dictionary<string, EmbedReference> graphEmbeds = state.UniqueEmbeds[state.Graph];

            if(string.Equals(flags.Embed, "@link", StringComparison.Ordinal) && link.TryGetValue(id, out Dictionary<string, object?>? linked))
            {
                AddFrameOutput(parent, property, linked);
                continue;
            }

            Dictionary<string, object?> output = new(StringComparer.Ordinal) { [JsonLdKeywords.Id] = id };
            if(id.StartsWith("_:", StringComparison.Ordinal))
            {
                TrackBlankNode(state, id, output);
            }
            link[id] = output;

            if(!state.Embedded && graphEmbeds.ContainsKey(id))
            {
                //Already embedded elsewhere; do not also add at this level.
                continue;
            }

            if(state.Embedded
                && (string.Equals(flags.Embed, "@never", StringComparison.Ordinal) || CreatesCircularReference(id, state.Graph, state.SubjectStack)))
            {
                AddFrameOutput(parent, property, output);
                continue;
            }

            if(state.Embedded && string.Equals(flags.Embed, "@once", StringComparison.Ordinal) && graphEmbeds.ContainsKey(id))
            {
                AddFrameOutput(parent, property, output);
                continue;
            }

            graphEmbeds[id] = new EmbedReference(parent, property);
            state.SubjectStack.Add(new StackedSubject(subject, state.Graph));

            FrameNamedGraph(state, frame, id, output);
            FrameIncluded(state, frame, subjects, output);
            FrameProperties(state, frame, subject, flags, output);
            FrameDefaults(state, frame, output);
            FrameReverse(state, frame, id, output);

            AddFrameOutput(parent, property, output);
            state.SubjectStack.RemoveAt(state.SubjectStack.Count - 1);
        }
    }

    /// <summary>When the matched subject is also a graph name, recurses into that graph (or a sub-frame's <c>@graph</c>) under <c>@graph</c>.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="id">The subject id.</param>
    /// <param name="output">The subject output under construction.</param>
    private static void FrameNamedGraph(FramingState state, Dictionary<string, object?> frame, string id, Dictionary<string, object?> output)
    {
        if(!state.GraphMap.TryGetValue(id, out Dictionary<string, Dictionary<string, object?>>? namedGraph))
        {
            return;
        }

        bool recurse;
        Dictionary<string, object?> subframe;
        if(!frame.ContainsKey(JsonLdKeywords.Graph))
        {
            recurse = !string.Equals(state.Graph, JsonLdNodeMap.MergedGraph, StringComparison.Ordinal);
            subframe = new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        else
        {
            subframe = frame[JsonLdKeywords.Graph] is IReadOnlyList<object?> { Count: > 0 } graphFrame && graphFrame[0] is Dictionary<string, object?> sf
                ? sf
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            recurse = !(string.Equals(id, JsonLdNodeMap.MergedGraph, StringComparison.Ordinal) || string.Equals(id, JsonLdNodeMap.DefaultGraph, StringComparison.Ordinal));
        }

        if(recurse)
        {
            List<string> graphSubjects = new(namedGraph.Keys);
            graphSubjects.Sort(StringComparer.Ordinal);
            FrameSubjects(state.WithGraph(id, embedded: false), graphSubjects, new List<object?> { subframe }, output, JsonLdKeywords.Graph);
        }
    }

    /// <summary>Recurses over a frame's <c>@included</c> sub-frame.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="subjects">The candidate subject ids.</param>
    /// <param name="output">The subject output.</param>
    private static void FrameIncluded(FramingState state, Dictionary<string, object?> frame, IReadOnlyList<string> subjects, Dictionary<string, object?> output)
    {
        if(frame.TryGetValue(JsonLdKeywords.Included, out object? included) && included is IReadOnlyList<object?> includedFrame)
        {
            FrameSubjects(state.WithEmbedded(false), subjects, includedFrame, output, JsonLdKeywords.Included);
        }
    }

    /// <summary>Frames each of a matched subject's properties against the frame (or an implicit wildcard sub-frame), embedding referenced nodes and matching values.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="subject">The matched subject.</param>
    /// <param name="flags">The frame flags.</param>
    /// <param name="output">The subject output.</param>
    private static void FrameProperties(FramingState state, Dictionary<string, object?> frame, Dictionary<string, object?> subject, FrameFlags flags, Dictionary<string, object?> output)
    {
        List<string> properties = new(subject.Keys);
        properties.Sort(StringComparer.Ordinal);
        foreach(string prop in properties)
        {
            if(IriUtils.IsKeyword(prop))
            {
                output[prop] = JsonLdNodeMap.Clone(subject[prop]);
                if(JsonLdKeywords.IsType(prop) && subject[prop] is IReadOnlyList<object?> types)
                {
                    foreach(object? type in types)
                    {
                        if(type is string typeId && typeId.StartsWith("_:", StringComparison.Ordinal))
                        {
                            TrackBlankNode(state, typeId, output);
                        }
                    }
                }

                continue;
            }

            if(flags.Explicit && !frame.ContainsKey(prop))
            {
                continue;
            }

            if(subject[prop] is not IReadOnlyList<object?> objects)
            {
                continue;
            }

            foreach(object? o in objects)
            {
                IReadOnlyList<object?> subframe = frame.TryGetValue(prop, out object? propFrame) && propFrame is IReadOnlyList<object?> pf
                    ? pf
                    : CreateImplicitFrame(flags);

                if(o is IReadOnlyDictionary<string, object?> listObject && JsonLdNodeMap.IsList(listObject))
                {
                    IReadOnlyList<object?> listSubframe = propFrame is IReadOnlyList<object?> { Count: > 0 } outer
                        && outer[0] is IReadOnlyDictionary<string, object?> outerFrame
                        && outerFrame.TryGetValue(JsonLdKeywords.List, out object? listFrame) && listFrame is IReadOnlyList<object?> lf
                        ? lf
                        : CreateImplicitFrame(flags);

                    Dictionary<string, object?> list = new(StringComparer.Ordinal) { [JsonLdKeywords.List] = new List<object?>() };
                    AddFrameOutput(output, prop, list);
                    List<object?> listArray = (List<object?>)list[JsonLdKeywords.List]!;

                    foreach(object? oo in (IReadOnlyList<object?>)listObject[JsonLdKeywords.List]!)
                    {
                        if(oo is IReadOnlyDictionary<string, object?> listRef && JsonLdNodeMap.IsSubjectReference(listRef))
                        {
                            FrameSubjects(state.WithEmbedded(true), new List<string> { JsonLdNodeMap.GetId(listRef)! }, listSubframe, list, JsonLdKeywords.List);
                        }
                        else
                        {
                            AddFrameOutput(list, JsonLdKeywords.List, JsonLdNodeMap.Clone(oo));
                        }
                    }

                    _ = listArray;
                }
                else if(o is IReadOnlyDictionary<string, object?> reference && JsonLdNodeMap.IsSubjectReference(reference))
                {
                    FrameSubjects(state.WithEmbedded(true), new List<string> { JsonLdNodeMap.GetId(reference)! }, subframe, output, prop);
                }
                else if(subframe.Count > 0 && subframe[0] is Dictionary<string, object?> valuePattern && ValueMatch(valuePattern, o))
                {
                    AddFrameOutput(output, prop, JsonLdNodeMap.Clone(o));
                }
            }
        }
    }

    /// <summary>Adds <c>@default</c> values (wrapped in <c>@preserve</c>) for frame properties absent from the matched subject, unless <c>@omitDefault</c> is on.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="output">The subject output.</param>
    private static void FrameDefaults(FramingState state, Dictionary<string, object?> frame, Dictionary<string, object?> output)
    {
        List<string> properties = new(frame.Keys);
        properties.Sort(StringComparer.Ordinal);
        foreach(string prop in properties)
        {
            if(JsonLdKeywords.IsType(prop))
            {
                if(frame[prop] is not IReadOnlyList<object?> { Count: > 0 } typeFrame
                    || typeFrame[0] is not IReadOnlyDictionary<string, object?> typeObject || !typeObject.ContainsKey(JsonLdKeywords.Default))
                {
                    continue;
                }
            }
            else if(IriUtils.IsKeyword(prop))
            {
                continue;
            }

            Dictionary<string, object?> next = frame[prop] is IReadOnlyList<object?> { Count: > 0 } list && list[0] is Dictionary<string, object?> first
                ? first
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            if(GetBoolFlag(next, JsonLdKeywords.OmitDefault, state.Options.OmitDefault) || output.ContainsKey(prop))
            {
                continue;
            }

            object? preserve = next.TryGetValue(JsonLdKeywords.Default, out object? defaultValue) ? JsonLdNodeMap.Clone(defaultValue) : "@null";
            List<object?> preserveList = preserve as List<object?> ?? new List<object?> { preserve };
            output[prop] = new List<object?>
            {
                new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Preserve] = preserveList }
            };
        }
    }

    /// <summary>Frames <c>@reverse</c> sub-frames: embeds nodes that reference this subject via the named reverse property.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="id">The subject id.</param>
    /// <param name="output">The subject output.</param>
    private static void FrameReverse(FramingState state, Dictionary<string, object?> frame, string id, Dictionary<string, object?> output)
    {
        if(!frame.TryGetValue(JsonLdKeywords.Reverse, out object? reverseValue) || reverseValue is not IReadOnlyDictionary<string, object?> reverseMap)
        {
            return;
        }

        List<string> reverseProps = new(reverseMap.Keys);
        reverseProps.Sort(StringComparer.Ordinal);
        foreach(string reverseProp in reverseProps)
        {
            if(reverseMap[reverseProp] is not IReadOnlyList<object?> subframe)
            {
                continue;
            }

            foreach(string subjectId in state.Subjects.Keys)
            {
                List<object?> nodeValues = GetValues(state.Subjects[subjectId], reverseProp);
                bool references = false;
                foreach(object? value in nodeValues)
                {
                    if(value is IReadOnlyDictionary<string, object?> reference && string.Equals(JsonLdNodeMap.GetId(reference), id, StringComparison.Ordinal))
                    {
                        references = true;
                        break;
                    }
                }

                if(!references)
                {
                    continue;
                }

                if(!output.TryGetValue(JsonLdKeywords.Reverse, out object? outputReverseValue) || outputReverseValue is not Dictionary<string, object?> outputReverse)
                {
                    outputReverse = new Dictionary<string, object?>(StringComparer.Ordinal);
                    output[JsonLdKeywords.Reverse] = outputReverse;
                }

                if(!outputReverse.TryGetValue(reverseProp, out object? bucket) || bucket is not List<object?> bucketList)
                {
                    bucketList = new List<object?>();
                    outputReverse[reverseProp] = bucketList;
                }

                FrameSubjects(state.WithEmbedded(true), new List<string> { subjectId }, subframe, bucketList, reverseProp);
            }
        }
    }

    /// <summary>Returns the subjects (id → node) of <paramref name="subjects"/> that match <paramref name="frame"/>.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="subjects">The candidate ids.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="flags">The frame flags.</param>
    /// <returns>The matching subjects.</returns>
    private static Dictionary<string, Dictionary<string, object?>> FilterSubjects(FramingState state, IReadOnlyList<string> subjects, Dictionary<string, object?> frame, FrameFlags flags)
    {
        Dictionary<string, Dictionary<string, object?>> result = new(StringComparer.Ordinal);
        foreach(string id in subjects)
        {
            Dictionary<string, object?> subject = state.GraphMap[state.Graph][id];
            if(FilterSubject(state, subject, frame, flags))
            {
                result[id] = subject;
            }
        }

        return result;
    }

    /// <summary>Whether a subject matches a frame: <c>@id</c>/<c>@type</c> matching, then duck-typing over the frame's properties (honouring <c>@requireAll</c> and <c>@default</c>).</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="flags">The frame flags.</param>
    /// <returns><see langword="true"/> when the subject matches.</returns>
    private static bool FilterSubject(FramingState state, IReadOnlyDictionary<string, object?> subject, Dictionary<string, object?> frame, FrameFlags flags)
    {
        bool wildcard = true;
        bool matchesSome = false;

        foreach(KeyValuePair<string, object?> entry in frame)
        {
            string key = entry.Key;
            bool matchThis = false;
            List<object?> nodeValues = GetValues(subject, key);
            bool isEmpty = GetValues(frame, key).Count == 0;

            if(JsonLdKeywords.IsId(key))
            {
                List<object?> frameIds = GetValues(frame, key);
                if(frameIds.Count > 0 && frameIds[0] is IReadOnlyDictionary<string, object?> { Count: 0 })
                {
                    matchThis = true;
                }
                else
                {
                    matchThis = nodeValues.Count > 0 && ContainsValue(frameIds, nodeValues[0]);
                }

                if(!flags.RequireAll)
                {
                    return matchThis;
                }
            }
            else if(JsonLdKeywords.IsType(key))
            {
                wildcard = false;
                List<object?> frameTypes = GetValues(frame, key);
                if(isEmpty)
                {
                    if(nodeValues.Count > 0)
                    {
                        return false;
                    }

                    matchThis = true;
                }
                else if(frameTypes.Count == 1 && frameTypes[0] is IReadOnlyDictionary<string, object?> { Count: 0 })
                {
                    matchThis = nodeValues.Count > 0;
                }
                else
                {
                    foreach(object? type in frameTypes)
                    {
                        if(type is IReadOnlyDictionary<string, object?> typeObject && typeObject.ContainsKey(JsonLdKeywords.Default))
                        {
                            matchThis = true;
                        }
                        else
                        {
                            matchThis = matchThis || ContainsValue(nodeValues, type);
                        }
                    }
                }

                if(!flags.RequireAll)
                {
                    return matchThis;
                }
            }
            else if(IriUtils.IsKeyword(key))
            {
                continue;
            }
            else
            {
                object? thisFrame = GetValues(frame, key).Count > 0 ? GetValues(frame, key)[0] : null;
                bool hasDefault = false;
                if(thisFrame is not null)
                {
                    ValidateFrame(new List<object?> { thisFrame });
                    hasDefault = thisFrame is IReadOnlyDictionary<string, object?> tf && tf.ContainsKey(JsonLdKeywords.Default);
                }

                wildcard = false;

                if(nodeValues.Count == 0 && hasDefault)
                {
                    continue;
                }

                if(nodeValues.Count > 0 && isEmpty)
                {
                    return false;
                }

                if(thisFrame is null)
                {
                    if(nodeValues.Count > 0)
                    {
                        return false;
                    }

                    matchThis = true;
                }
                else
                {
                    matchThis = MatchPropertyFrame(state, thisFrame, nodeValues, flags);
                }
            }

            if(!matchThis && flags.RequireAll)
            {
                return false;
            }

            matchesSome = matchesSome || matchThis;
        }

        return wildcard || matchesSome;
    }

    /// <summary>Matches a property's node values against the property's sub-frame (list, value, node reference, or wildcard object).</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="thisFrame">The property sub-frame.</param>
    /// <param name="nodeValues">The subject's values for the property.</param>
    /// <param name="flags">The frame flags.</param>
    /// <returns><see langword="true"/> when at least one value matches.</returns>
    private static bool MatchPropertyFrame(FramingState state, object? thisFrame, List<object?> nodeValues, FrameFlags flags)
    {
        if(thisFrame is IReadOnlyDictionary<string, object?> listFrame && JsonLdNodeMap.IsList(listFrame))
        {
            if(((IReadOnlyList<object?>)listFrame[JsonLdKeywords.List]!) is not { Count: > 0 } listFrameItems
                || nodeValues.Count == 0 || nodeValues[0] is not IReadOnlyDictionary<string, object?> nodeList || !JsonLdNodeMap.IsList(nodeList))
            {
                return false;
            }

            object? listValue = listFrameItems[0];
            IReadOnlyList<object?> nodeListValues = (IReadOnlyList<object?>)nodeList[JsonLdKeywords.List]!;
            if(listValue is IReadOnlyDictionary<string, object?> listValueObject && JsonLdNodeMap.IsValue(listValueObject))
            {
                foreach(object? lv in nodeListValues)
                {
                    if(ValueMatch(listValueObject, lv))
                    {
                        return true;
                    }
                }

                return false;
            }

            if(listValue is IReadOnlyDictionary<string, object?> listValueNode && (JsonLdNodeMap.IsSubject(listValueNode) || JsonLdNodeMap.IsSubjectReference(listValueNode)))
            {
                foreach(object? lv in nodeListValues)
                {
                    if(NodeMatch(state, listValueNode, lv, flags))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        if(thisFrame is IReadOnlyDictionary<string, object?> valueFrame && JsonLdNodeMap.IsValue(valueFrame))
        {
            foreach(object? nv in nodeValues)
            {
                if(ValueMatch(valueFrame, nv))
                {
                    return true;
                }
            }

            return false;
        }

        if(thisFrame is IReadOnlyDictionary<string, object?> nodeFrame && JsonLdNodeMap.IsSubjectReference(nodeFrame))
        {
            foreach(object? nv in nodeValues)
            {
                if(NodeMatch(state, nodeFrame, nv, flags))
                {
                    return true;
                }
            }

            return false;
        }

        if(thisFrame is IReadOnlyDictionary<string, object?>)
        {
            return nodeValues.Count > 0;
        }

        return false;
    }

    /// <summary>Validates that a frame is a single-object array, with valid <c>@id</c>/<c>@type</c> entries.</summary>
    /// <param name="frame">The frame to validate.</param>
    private static void ValidateFrame(IReadOnlyList<object?> frameArray)
    {
        if(frameArray.Count != 1 || frameArray[0] is not IReadOnlyDictionary<string, object?> frame)
        {
            throw new JsonLdProcessingException("A JSON-LD frame must be a single object.");
        }

        //@id values must each be a wildcard ({}) or an absolute IRI — never a blank node.
        if(frame.TryGetValue(JsonLdKeywords.Id, out object? idValue))
        {
            foreach(object? id in AsFrameList(idValue))
            {
                if(id is not IReadOnlyDictionary<string, object?> && !(id is string idText && IriUtils.IsAbsoluteIri(idText)))
                {
                    throw new JsonLdProcessingException("Invalid @id in JSON-LD frame.");
                }
            }
        }

        //@type values must each be a wildcard, an absolute IRI, or @json — never a blank node.
        if(frame.TryGetValue(JsonLdKeywords.Type, out object? typeValue))
        {
            foreach(object? type in AsFrameList(typeValue))
            {
                if(type is not IReadOnlyDictionary<string, object?>
                    && !(type is string typeText && (string.Equals(typeText, JsonLdKeywords.Json, StringComparison.Ordinal) || IriUtils.IsAbsoluteIri(typeText))))
                {
                    throw new JsonLdProcessingException("Invalid @type in JSON-LD frame.");
                }
            }
        }
    }

    /// <summary>Resolves the <c>@embed</c> flag (frame override or option default), normalising legacy booleans and rejecting invalid values.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The framing options.</param>
    /// <returns>The embed flag (<c>@once</c>/<c>@always</c>/<c>@never</c>/<c>@link</c>).</returns>
    private static string GetEmbedFlag(Dictionary<string, object?> frame, JsonLdFrameOptions options)
    {
        object? raw = frame.TryGetValue(JsonLdKeywords.Embed, out object? value) && value is not null
            ? UnwrapFlagValue(FirstOrSelf(value))
            : options.Embed;

        return raw switch
        {
            true => "@once",
            false => "@never",
            "@always" or "@never" or "@link" or "@once" => (string)raw,
            _ => throw new JsonLdProcessingException($"Invalid @embed value '{raw}'.")
        };
    }

    /// <summary>Resolves a boolean framing flag (<c>@explicit</c>/<c>@requireAll</c>/<c>@omitDefault</c>) from the frame or the option default.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="keyword">The flag keyword.</param>
    /// <param name="defaultValue">The option default.</param>
    /// <returns>The flag value.</returns>
    private static bool GetBoolFlag(Dictionary<string, object?> frame, string keyword, bool defaultValue)
    {
        if(frame.TryGetValue(keyword, out object? value) && value is not null)
        {
            return UnwrapFlagValue(FirstOrSelf(value)) switch
            {
                bool flag => flag,
                string text => string.Equals(text, "true", StringComparison.Ordinal),
                _ => defaultValue
            };
        }

        return defaultValue;
    }

    /// <summary>Returns a value's first element when it is a non-empty array, otherwise the value itself (a framing flag may expand to either form).</summary>
    /// <param name="value">The expanded flag value.</param>
    /// <returns>The flag scalar carrier.</returns>
    private static object? FirstOrSelf(object? value)
    {
        return value is IReadOnlyList<object?> { Count: > 0 } list ? list[0] : value;
    }

    /// <summary>Unwraps a framing-flag value, which expansion may carry as a bare scalar or a <c>{"@value": …}</c> object.</summary>
    /// <param name="value">The expanded flag value.</param>
    /// <returns>The underlying scalar.</returns>
    private static object? UnwrapFlagValue(object? value)
    {
        return value is IReadOnlyDictionary<string, object?> valueObject && valueObject.TryGetValue(JsonLdKeywords.Value, out object? inner)
            ? inner
            : value;
    }

    /// <summary>Builds an implicit wildcard sub-frame carrying the current flags, used when a property has no explicit frame.</summary>
    /// <param name="flags">The current flags.</param>
    /// <returns>A single-element frame array.</returns>
    private static List<object?> CreateImplicitFrame(FrameFlags flags)
    {
        Dictionary<string, object?> frame = new(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Embed] = new List<object?> { flags.Embed },
            [JsonLdKeywords.Explicit] = new List<object?> { flags.Explicit },
            [JsonLdKeywords.RequireAll] = new List<object?> { flags.RequireAll }
        };
        return new List<object?> { frame };
    }

    /// <summary>Whether embedding <paramref name="id"/> in the current graph would close a cycle already on the subject stack.</summary>
    /// <param name="id">The subject id to embed.</param>
    /// <param name="graph">The current graph.</param>
    /// <param name="stack">The subject stack.</param>
    /// <returns><see langword="true"/> when a cycle would form.</returns>
    private static bool CreatesCircularReference(string id, string graph, List<StackedSubject> stack)
    {
        for(int i = stack.Count - 1; i >= 0; i--)
        {
            if(string.Equals(stack[i].Graph, graph, StringComparison.Ordinal)
                && string.Equals(JsonLdNodeMap.GetId(stack[i].Subject), id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Recursively removes <c>@preserve</c> wrappers from the framed output, lifting the preserved value.</summary>
    /// <param name="input">The framed output.</param>
    /// <returns>The cleaned output.</returns>
    private static List<object?> CleanupPreserve(List<object?> input)
    {
        List<object?> result = new(input.Count);
        foreach(object? item in input)
        {
            result.Add(CleanupPreserveValue(item));
        }

        return result;
    }

    /// <summary>Removes <c>@preserve</c> from a single framed value.</summary>
    /// <param name="input">The value.</param>
    /// <returns>The cleaned value.</returns>
    private static object? CleanupPreserveValue(object? input)
    {
        if(input is List<object?> list)
        {
            return CleanupPreserve(list);
        }

        if(input is Dictionary<string, object?> map)
        {
            if(map.TryGetValue(JsonLdKeywords.Preserve, out object? preserved) && preserved is IReadOnlyList<object?> { Count: > 0 } preserveList)
            {
                return CleanupPreserveValue(preserveList[0]);
            }

            if(JsonLdNodeMap.IsValue(map))
            {
                return map;
            }

            List<string> keys = new(map.Keys);
            foreach(string key in keys)
            {
                map[key] = CleanupPreserveValue(map[key]);
            }
        }

        return input;
    }

    /// <summary>Adds framed output to a parent: pushed onto a list parent, or appended (array-valued) to a node-object parent's property.</summary>
    /// <param name="parent">The parent (list or node object).</param>
    /// <param name="property">The property (for an object parent).</param>
    /// <param name="output">The framed output to add.</param>
    private static void AddFrameOutput(object parent, string? property, object? output)
    {
        if(parent is Dictionary<string, object?> node && property is not null)
        {
            JsonLdNodeMap.AddValue(node, property, output, propertyIsArray: true, allowDuplicate: true);
        }
        else if(parent is List<object?> list)
        {
            list.Add(output);
        }
    }

    /// <summary>Whether a value is a node that matches a node pattern (the pattern applied as a frame to the referenced subject).</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="pattern">The node pattern.</param>
    /// <param name="value">The value to test.</param>
    /// <param name="flags">The frame flags.</param>
    /// <returns><see langword="true"/> when the value matches.</returns>
    private static bool NodeMatch(FramingState state, IReadOnlyDictionary<string, object?> pattern, object? value, FrameFlags flags)
    {
        if(value is not IReadOnlyDictionary<string, object?> reference || JsonLdNodeMap.GetId(reference) is not { } id || !state.Subjects.TryGetValue(id, out Dictionary<string, object?>? node))
        {
            return false;
        }

        return FilterSubject(state, node, ToFrameDictionary(pattern), flags);
    }

    /// <summary>Whether a value object matches a value pattern (empty pattern is a wildcard; otherwise @value/@type/@language must match or be wildcards).</summary>
    /// <param name="pattern">The value pattern.</param>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value matches.</returns>
    private static bool ValueMatch(IReadOnlyDictionary<string, object?> pattern, object? value)
    {
        if(value is not IReadOnlyDictionary<string, object?> valueObject)
        {
            return false;
        }

        object? v1 = valueObject.GetValueOrDefault(JsonLdKeywords.Value);
        object? t1 = valueObject.GetValueOrDefault(JsonLdKeywords.Type);
        object? l1 = valueObject.GetValueOrDefault(JsonLdKeywords.Language);
        List<object?> v2 = AsFrameList(pattern.GetValueOrDefault(JsonLdKeywords.Value));
        List<object?> t2 = AsFrameList(pattern.GetValueOrDefault(JsonLdKeywords.Type));
        List<object?> l2 = AsFrameList(pattern.GetValueOrDefault(JsonLdKeywords.Language));

        if(v2.Count == 0 && t2.Count == 0 && l2.Count == 0)
        {
            return true;
        }

        if(!(ContainsValue(v2, v1) || IsWildcard(v2)))
        {
            return false;
        }

        if(!((t1 is null && t2.Count == 0) || ContainsValue(t2, t1) || (t1 is not null && IsWildcard(t2))))
        {
            return false;
        }

        if(!((l1 is null && l2.Count == 0) || ContainsValue(l2, l1) || (l1 is not null && IsWildcard(l2))))
        {
            return false;
        }

        return true;
    }

    /// <summary>Tracks a blank-node output for the single-reference pruning pass.</summary>
    /// <param name="state">The framing state.</param>
    /// <param name="id">The blank-node id.</param>
    /// <param name="output">The output that references it.</param>
    private static void TrackBlankNode(FramingState state, string id, Dictionary<string, object?> output)
    {
        if(!state.BlankNodeMap.TryGetValue(id, out List<Dictionary<string, object?>>? uses))
        {
            uses = new List<Dictionary<string, object?>>();
            state.BlankNodeMap[id] = uses;
        }

        uses.Add(output);
    }

    /// <summary>Gets the array-valued entries of a node's property as a list (empty when absent or not an array).</summary>
    /// <param name="node">The node.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The values.</returns>
    private static List<object?> GetValues(IReadOnlyDictionary<string, object?> node, string key)
    {
        return (node.TryGetValue(key, out object? value), value) switch
        {
            (false, _) => new List<object?>(),
            (true, IReadOnlyList<object?> list) => new List<object?>(list),
            (true, null) => new List<object?>(),
            _ => new List<object?> { value }
        };
    }

    /// <summary>Whether a list contains a value equal (by <c>compareValues</c>) to the target, or the target is a string present in the list.</summary>
    /// <param name="list">The list.</param>
    /// <param name="target">The target value.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsValue(IReadOnlyList<object?> list, object? target)
    {
        foreach(object? item in list)
        {
            if(JsonLdNodeMap.ValueEquals(item, target) || (item is string a && target is string b && string.Equals(a, b, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a pattern list is a single empty-object wildcard.</summary>
    /// <param name="list">The pattern list.</param>
    /// <returns><see langword="true"/> when a wildcard.</returns>
    private static bool IsWildcard(List<object?> list)
    {
        return list.Count > 0 && list[0] is IReadOnlyDictionary<string, object?> { Count: 0 };
    }

    /// <summary>Normalises a pattern entry into a list (wrapping a single value).</summary>
    /// <param name="value">The pattern entry.</param>
    /// <returns>The list form.</returns>
    private static List<object?> AsFrameList(object? value)
    {
        return value switch
        {
            null => new List<object?>(),
            IReadOnlyList<object?> list => new List<object?>(list),
            _ => new List<object?> { value }
        };
    }

    /// <summary>Wraps a node pattern as the single-object frame dictionary that <see cref="FilterSubject"/> expects.</summary>
    /// <param name="pattern">The node pattern.</param>
    /// <returns>The frame dictionary.</returns>
    private static Dictionary<string, object?> ToFrameDictionary(IReadOnlyDictionary<string, object?> pattern)
    {
        return pattern as Dictionary<string, object?> ?? new Dictionary<string, object?>(pattern, StringComparer.Ordinal);
    }

    /// <summary>
    /// Replaces <c>@null</c> sentinels with JSON null (dropping them from arrays)
    /// after the framed output has been compacted.
    /// </summary>
    /// <param name="input">The framed, compacted output.</param>
    /// <returns>The cleaned output, or <see langword="null"/> when the value was <c>@null</c>.</returns>
    public static object? CleanupNull(object? input)
    {
        if(input is IReadOnlyList<object?> list)
        {
            List<object?> cleaned = new();
            foreach(object? item in list)
            {
                object? value = CleanupNull(item);
                if(value is not null)
                {
                    cleaned.Add(value);
                }
            }

            return cleaned;
        }

        if(input is string s && string.Equals(s, "@null", StringComparison.Ordinal))
        {
            return null;
        }

        if(input is Dictionary<string, object?> map)
        {
            List<string> keys = new(map.Keys);
            foreach(string key in keys)
            {
                map[key] = CleanupNull(map[key]);
            }
        }

        return input;
    }

    /// <summary>The resolved framing flags for one frame level.</summary>
    /// <param name="Embed">The <c>@embed</c> mode.</param>
    /// <param name="Explicit">Whether only framed properties are kept.</param>
    /// <param name="RequireAll">Whether all frame properties must match.</param>
    private sealed record FrameFlags(string Embed, bool Explicit, bool RequireAll);

    /// <summary>A reference to where an embedded subject was placed, for <c>@last</c>/embed removal.</summary>
    /// <param name="Parent">The parent collection.</param>
    /// <param name="Property">The parent property.</param>
    private sealed record EmbedReference(object Parent, string? Property);

    /// <summary>A subject on the embedding stack, for circular-reference detection.</summary>
    /// <param name="Subject">The subject node.</param>
    /// <param name="Graph">The graph it belongs to.</param>
    private sealed record StackedSubject(Dictionary<string, object?> Subject, string Graph);

    /// <summary>The mutable framing state threaded through the recursion (graph maps, embed/link tracking, the subject stack).</summary>
    private sealed class FramingState
    {
        /// <summary>Initialises the state for a framing run.</summary>
        /// <param name="options">The framing options.</param>
        /// <param name="graphMap">The per-graph node maps.</param>
        /// <param name="graph">The starting graph (default or merged).</param>
        public FramingState(JsonLdFrameOptions options, Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphMap, string graph)
        {
            Options = options;
            GraphMap = graphMap;
            Graph = graph;
            Subjects = graphMap[graph];
            Link = new Dictionary<string, Dictionary<string, Dictionary<string, object?>>>(StringComparer.Ordinal);
            BlankNodeMap = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
            SubjectStack = new List<StackedSubject>();
            UniqueEmbeds = new Dictionary<string, Dictionary<string, EmbedReference>>(StringComparer.Ordinal);
            Embedded = false;
        }

        private FramingState(FramingState source, string graph, bool embedded)
        {
            Options = source.Options;
            GraphMap = source.GraphMap;
            Graph = graph;
            Subjects = source.Subjects;
            Link = source.Link;
            BlankNodeMap = source.BlankNodeMap;
            SubjectStack = source.SubjectStack;
            UniqueEmbeds = source.UniqueEmbeds;
            Embedded = embedded;
        }

        /// <summary>Gets the framing options.</summary>
        public JsonLdFrameOptions Options { get; }

        /// <summary>Gets the per-graph node maps.</summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, object?>>> GraphMap { get; }

        /// <summary>Gets the current graph name.</summary>
        public string Graph { get; }

        /// <summary>Gets the top-level frame graph's subjects (used by node/reverse matching).</summary>
        public Dictionary<string, Dictionary<string, object?>> Subjects { get; }

        /// <summary>Gets the per-graph linked-output map (for <c>@embed: @link</c>).</summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, object?>>> Link { get; }

        /// <summary>Gets the blank-node usage map (for single-reference pruning).</summary>
        public Dictionary<string, List<Dictionary<string, object?>>> BlankNodeMap { get; }

        /// <summary>Gets the subject stack (for circular-reference detection).</summary>
        public List<StackedSubject> SubjectStack { get; }

        /// <summary>Gets or sets the per-graph unique-embed tracking.</summary>
        public Dictionary<string, Dictionary<string, EmbedReference>> UniqueEmbeds { get; set; }

        /// <summary>Gets a value indicating whether the current frame level is embedded (not top-level).</summary>
        public bool Embedded { get; }

        /// <summary>Returns a state sharing all tracking maps but framing a different graph at the given embed level.</summary>
        /// <param name="graph">The graph to frame.</param>
        /// <param name="embedded">Whether the level is embedded.</param>
        /// <returns>The derived state.</returns>
        public FramingState WithGraph(string graph, bool embedded)
        {
            return new FramingState(this, graph, embedded);
        }

        /// <summary>Returns a state sharing everything but with a different embed level.</summary>
        /// <param name="embedded">Whether the level is embedded.</param>
        /// <returns>The derived state.</returns>
        public FramingState WithEmbedded(bool embedded)
        {
            return new FramingState(this, Graph, embedded);
        }
    }

}

/// <summary>The options controlling a JSON-LD framing run.</summary>
public sealed class JsonLdFrameOptions
{
    /// <summary>Gets or sets the default <c>@embed</c> mode (<c>@once</c> by default).</summary>
    public string Embed { get; set; } = "@once";

    /// <summary>Gets or sets the default <c>@explicit</c> flag.</summary>
    public bool Explicit { get; set; }

    /// <summary>Gets or sets the default <c>@requireAll</c> flag.</summary>
    public bool RequireAll { get; set; }

    /// <summary>Gets or sets the default <c>@omitDefault</c> flag.</summary>
    public bool OmitDefault { get; set; }

    /// <summary>Gets or sets a value indicating whether the merged graph is framed (the frame has no top-level <c>@graph</c>).</summary>
    public bool Merged { get; set; }

    /// <summary>Gets or sets a value indicating whether single-use blank-node identifiers are pruned during compaction.</summary>
    public bool PruneBlankNodeIdentifiers { get; set; } = true;

    /// <summary>Gets the set of blank-node identifiers to prune, populated by framing for the compaction step.</summary>
    public HashSet<string> BlankNodesToClear { get; } = new(StringComparer.Ordinal);
}
