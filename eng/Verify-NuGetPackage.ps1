param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)

try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $nuspecEntry = $archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $nuspecEntry) { throw 'Package does not contain a .nuspec file.' }

    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $license = $nuspec.package.metadata.license
    if ($null -eq $license -or $license.type -ne 'expression' -or $license.'#text' -ne 'Apache-2.0') {
        throw "Expected PackageLicenseExpression Apache-2.0; found '$($license.'#text')'."
    }

    foreach ($required in @('README.md', 'LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
        if ($entries -cnotcontains $required) { throw "Package is missing required root file '$required'." }
    }

    $forbiddenExtensions = @('.cact', '.gguf', '.bin', '.onnx', '.so', '.dylib', '.a', '.wasm', '.whl', '.exe')
    $forbidden = $entries | Where-Object {
        $extension = [IO.Path]::GetExtension($_).ToLowerInvariant()
        $leaf = [IO.Path]::GetFileName($_)
        $forbiddenExtensions -contains $extension -or
            ($extension -eq '.dll' -and ($_.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) -or $leaf.Equals('libneedle.dll', [StringComparison]::OrdinalIgnoreCase)))
    }
    if ($forbidden.Count -gt 0) {
        throw "Package unexpectedly contains upstream model/native artifacts: $($forbidden -join ', ')"
    }

    Write-Host "Verified package licensing, notices, README, and artifact boundary: $resolvedPackage"
}
finally {
    $archive.Dispose()
}
