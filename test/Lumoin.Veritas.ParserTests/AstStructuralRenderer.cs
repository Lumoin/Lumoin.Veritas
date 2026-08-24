using System;
using System.Reflection;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.ParserTests;

/// <summary>
/// Renders an AST node deeply and deterministically — expanding lists by content and rendering each
/// <see cref="Utf8String"/> as its text — so two structurally-equal trees compare equal regardless of list-instance
/// identity or interning, while still comparing every source span. The incremental-reader tests use it to assert a
/// chunk-fed (byte-by-byte) parse renders identically to the whole-buffer parse, which record equality cannot do
/// (record <c>Equals</c> compares <c>List&lt;T&gt;</c> fields by reference, so two separately-parsed trees never match).
/// </summary>
internal static class AstStructuralRenderer
{
    /// <summary>Renders a node, value, or list to a deterministic deep string.</summary>
    /// <param name="node">The node to render.</param>
    /// <returns>The deterministic deep rendering.</returns>
    public static string Render(object? node)
    {
        return node switch
        {
            null => "∅",
            Utf8String text => $"\"{text}\"",
            string text => $"\"{text}\"",
            bool or int or long or double or Enum => node.ToString()!,
            System.Collections.IEnumerable items => RenderList(items),
            _ => RenderObject(node)
        };

        static string RenderList(System.Collections.IEnumerable items)
        {
            StringBuilder list = new("[");
            foreach(object? item in items)
            {
                list.Append(Render(item)).Append(',');
            }

            return list.Append(']').ToString();
        }

        static string RenderObject(object node)
        {
            Type type = node.GetType();
            StringBuilder builder = new(type.Name);
            builder.Append('{');
            foreach(PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if(property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                builder.Append(property.Name).Append('=').Append(Render(property.GetValue(node))).Append(';');
            }

            return builder.Append('}').ToString();
        }
    }
}
