using System;
using System.IO;
using PhotinoNET;

namespace Lumoin.Veritas.Studio.Host;

/// <summary>
/// The thin managed shell over the native OS webview window — currently backed by Photino.Native
/// (WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux). It is the seam that keeps the native
/// layer swappable: the app and the bridge protocol are ours, and replacing the native window means
/// changing only this type. Construct it, subscribe <see cref="MessageReceived"/>, choose a load source,
/// then <see cref="Run"/> on the UI thread.
/// </summary>
public sealed class StudioWindow
{
    /// <summary>The underlying native window.</summary>
    private PhotinoWindow Window { get; }

    /// <summary>Raised with each message the page posts through <c>window.external.sendMessage</c>.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Creates the native window with the Studio chrome and wires the inbound message channel.</summary>
    /// <param name="title">The window title.</param>
    public StudioWindow(string title)
    {
        Window = new PhotinoWindow()
            .SetTitle(title)
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .Center()
            .RegisterWebMessageReceivedHandler((_, message) => MessageReceived?.Invoke(message));
    }

    /// <summary>Posts a message to the page, delivered to <c>window.external.receiveMessage</c>.</summary>
    /// <param name="message">The JSON message.</param>
    public void Send(string message)
    {
        Window.SendWebMessage(message);
    }

    /// <summary>Loads the app from the Vite dev server (development; hot-module reload). The engine stays in-process via the bridge.</summary>
    /// <param name="devServer">The Vite dev-server origin.</param>
    /// <returns>This window, for chaining.</returns>
    public StudioWindow LoadDevServer(Uri devServer)
    {
        Window.Load(devServer);

        return this;
    }

    /// <summary>Loads the bundled build over the <c>veritas://</c> scheme (production); no localhost server.</summary>
    /// <param name="assets">The bundled asset source the scheme handler serves.</param>
    /// <returns>This window, for chaining.</returns>
    public StudioWindow LoadBundled(IBundledAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        Window.RegisterCustomSchemeHandler("veritas", (object _, string _, string url, out string contentType) =>
            assets.Open(url, out contentType));
        Window.Load(new Uri("veritas://app/index.html"));

        return this;
    }

    /// <summary>Runs the native message loop until the window closes; blocking, call on the UI thread.</summary>
    public void Run()
    {
        Window.WaitForClose();
    }
}

/// <summary>The source the <c>veritas://</c> scheme serves the built web app from.</summary>
public interface IBundledAssets
{
    /// <summary>Opens the asset a <c>veritas://</c> URL names.</summary>
    /// <param name="url">The requested URL.</param>
    /// <param name="contentType">The resolved MIME type.</param>
    /// <returns>The asset bytes.</returns>
    Stream Open(string url, out string contentType);
}

/// <summary>Serves the Vite <c>dist/</c> from a directory beside the executable (the simplest bundled source).</summary>
/// <param name="root">The built-app root directory.</param>
public sealed class DirectoryBundledAssets(string root): IBundledAssets
{
    /// <summary>The built-app root directory.</summary>
    private string Root { get; } = root;

    /// <inheritdoc/>
    public Stream Open(string url, out string contentType)
    {
        string path = new Uri(url).AbsolutePath.TrimStart('/');
        string full = Path.Combine(Root, path.Length == 0 ? "index.html" : path);
        contentType = Path.GetExtension(full) switch
        {
            ".html" => "text/html",
            ".js" => "text/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream"
        };

        return File.OpenRead(full);
    }
}
