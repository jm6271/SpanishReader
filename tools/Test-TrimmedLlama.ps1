param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory
)

$ErrorActionPreference = 'Stop'

# Inspect metadata rather than loading the assembly: this also works for ARM64
# artifacts on an x64 runner and never initializes a native inference backend.
$assemblyPath = Join-Path $PublishDirectory 'LLamaSharp.dll'
$stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $assemblyPath))
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $typeNames = foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        '{0}.{1}' -f $metadata.GetString($type.Namespace), $metadata.GetString($type.Name)
    }

    foreach ($name in @('LLama.Abstractions.TensorSplitsCollectionConverter',
                       'LLama.Abstractions.MetadataOverrideConverter')) {
        if ($typeNames -contains $name) {
            throw "Unused reflection-based JSON converter survived trimming: $name"
        }
    }

    # Removing JSON registrations must retain the types used for inference.
    foreach ($name in @('LLama.Common.ModelParams',
                       'LLama.Abstractions.TensorSplitsCollection',
                       'LLama.Abstractions.MetadataOverride')) {
        if ($typeNames -notcontains $name) {
            throw "Required inference parameter type is missing: $name"
        }
    }
    Write-Output 'Trimmed LLamaSharp: inference parameters retained; unused JSON converters removed.'
}
finally {
    $pe.Dispose()
    $stream.Dispose()
}
