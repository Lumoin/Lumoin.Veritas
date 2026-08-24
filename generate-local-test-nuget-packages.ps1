#Library packages only. Lumoin.Veritas.Cli is excluded: it ships as a PackAsTool whose native
#packages are produced per runtime identifier on matching CI legs (ToolPackageRuntimeIdentifiers
#+ PublishAot), which a single plain `dotnet pack` cannot build; the local feed exists for the
#library packages siblings consume, so the tool is left to CI.
$projects = @(
    'Lumoin.Veritas.Canonicalization',
    'Lumoin.Veritas.Cbor',
    'Lumoin.Veritas.CborLd',
    'Lumoin.Veritas.Cid',
    'Lumoin.Veritas.Core',
    'Lumoin.Veritas.Database',
    'Lumoin.Veritas.Json',
    'Lumoin.Veritas.Json.Stj',
    'Lumoin.Veritas.Jsonata',
    'Lumoin.Veritas.JsonLd',
    'Lumoin.Veritas.JsonPointer',
    'Lumoin.Veritas.JsonSchema',
    'Lumoin.Veritas.LinkedData',
    'Lumoin.Veritas.NQuads',
    'Lumoin.Veritas.Owl',
    'Lumoin.Veritas.Rdf',
    'Lumoin.Veritas.Rdf.Json',
    'Lumoin.Veritas.Replication',
    'Lumoin.Veritas.Shacl',
    'Lumoin.Veritas.Skos',
    'Lumoin.Veritas.Sparql',
    'Lumoin.Veritas.Turtle',
    'Lumoin.Veritas.Xml'
)

$outputDir = './generated-nugets'
$baseVersion = '0.0.1'
$sha = git rev-parse --short HEAD 2>$null
$buildMetadata = ($sha) ? $sha : (Get-Date -Format 'yyyyMMddHHmmss')
$packageVersion = "$baseVersion-local"
$informationalVersion = "$baseVersion-local+$buildMetadata"

#Remove all existing packages before generating new ones so stale or malformed
#packages from previous runs do not accumulate in the output directory.
if(Test-Path $outputDir)
{
    Get-ChildItem -Path $outputDir -Filter '*.nupkg' | Remove-Item -Force
    Get-ChildItem -Path $outputDir -Filter '*.snupkg' | Remove-Item -Force
}
else
{
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

foreach($project in $projects)
{
    #IgnoreSetReleaseNotesProp skips SIL.ReleaseTasks' changelog/release-notes stamping: local
    #throwaway 0.0.1-local packages should not depend on the repo's release machinery (no CHANGELOG.md needed).
    dotnet pack --verbosity normal `
        --configuration Release `
        --output $outputDir `
        --include-symbols `
        --include-source `
        --property:PackageVersion=$packageVersion `
        --property:InformationalVersion=$informationalVersion `
        --property:IgnoreSetReleaseNotesProp=true `
        "./src/$project/$project.csproj"

    if($LASTEXITCODE -ne 0)
    {
        Write-Error "Pack failed for $project."
        exit $LASTEXITCODE
    }
}

Write-Host "Generated packages in $outputDir with version $packageVersion."
