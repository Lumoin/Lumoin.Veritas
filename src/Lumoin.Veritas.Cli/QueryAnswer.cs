namespace Lumoin.Veritas.Cli;

/// <summary>
/// A rendered query answer: the operation result plus the media type of the rendering the result shape
/// actually selected. The HTTP endpoint stamps <see cref="ContentType"/> on its response; the
/// command-line and MCP surfaces read <see cref="Result"/> alone. On failure the content type is the
/// plain-text default and the presenting surface owns the failure rendering.
/// </summary>
/// <param name="Result">The operation result carrying the rendered document or the failure.</param>
/// <param name="ContentType">The media type of the rendered document (with charset), meaningful on success.</param>
internal readonly record struct QueryAnswer(OperationResult Result, string ContentType);
