using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Studio.Host;

/// <summary>
/// Boots the Studio desktop shell: opens the native window, wires the engine bridge, and loads the web
/// app from the Vite dev server (when <c>VERITAS_STUDIO_DEV</c> names it) or the bundled build over the
/// <c>veritas://</c> scheme, then runs the native message loop.
/// </summary>
internal static class Program
{
    /// <summary>The desktop entry point; the native webview requires a single-threaded apartment on Windows.</summary>
    /// <param name="args">Unused; configuration is by environment for now.</param>
    [STAThread]
    private static void Main(string[] args)
    {
        StudioWindow window = new("Veritas Studio");

        //The in-process engine adapter: build a VeritasEngine over a store and return its SPARQL-results-JSON.
        //Stubbed to an empty result set until the engine is referenced and wired (decision record §5/§8).
        Func<string, CancellationToken, Task<string>> runSparqlJson =
            (_, _) => Task.FromResult("""{"head":{"vars":[]},"results":{"bindings":[]}}""");

        _ = new VeritasBridge(window, runSparqlJson);

        string? devServer = Environment.GetEnvironmentVariable("VERITAS_STUDIO_DEV");
        if(devServer is not null)
        {
            window.LoadDevServer(new Uri(devServer));
        }
        else
        {
            string dist = Path.Combine(AppContext.BaseDirectory, "dist");
            window.LoadBundled(new DirectoryBundledAssets(dist));
        }

        window.Run();
    }
}
