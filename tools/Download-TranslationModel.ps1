# Downloads the pinned Mozilla Spanish-to-English release, not whichever model
# happens to be the latest. Run from any working directory with PowerShell 7.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modelDirectory = Join-Path $PSScriptRoot '..\SpanishReader\TranslationModels\esen'
$baseUrl = 'https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/models/es-en/retrain_hr_HbNjJ60BTwmVTbhfFxuduA/exported'
$files = @(
    @{ Name = 'model.esen.intgemm.alphas.bin'; Hash = '4aed7734152ae0045d1a69ae49c86cfda18f53c61f90e95e1d1de1c7c7c3b033' },
    @{ Name = 'vocab.esen.spm'; Hash = '5ae254fa9b15aa182e70fd2a6186b1333c63a29a48043a9224c6aa4fcac058ad' },
    @{ Name = 'lex.50.50.esen.s2t.bin'; Hash = 'e2610211d3b9577d012638fe7e7e74ed7b4b708ce96b9e792e67c282a6492daa' }
)

New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
foreach ($file in $files) {
    $destination = Join-Path $modelDirectory $file.Name
    if ($file.Hash -and (Test-Path -LiteralPath $destination) -and
        (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $file.Hash) {
        Write-Host "Already verified: $($file.Name)"
        continue
    }

    # Publish only a fully decompressed, verified file; interrupted downloads do
    # not replace a previously working model. Temporary files stay beside it.
    $archive = "$destination.gz.tmp"
    $expanded = "$destination.tmp"
    try {
        Invoke-WebRequest "$baseUrl/$($file.Name).gz" -OutFile $archive
        $inputStream = [IO.File]::OpenRead($archive)
        try {
            $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
            try {
                $outputStream = [IO.File]::Create($expanded)
                try { $gzip.CopyTo($outputStream) }
                finally { $outputStream.Dispose() }
            }
            finally { $gzip.Dispose() }
        }
        finally { $inputStream.Dispose() }

        $actualHash = (Get-FileHash -LiteralPath $expanded -Algorithm SHA256).Hash
        if ($file.Hash -and $actualHash -ne $file.Hash) {
            throw "SHA256 mismatch for $($file.Name)."
        }
        Move-Item -LiteralPath $expanded -Destination $destination -Force
        Write-Host "$($file.Name): $actualHash"
    }
    finally {
        Remove-Item -LiteralPath $archive, $expanded -Force -ErrorAction SilentlyContinue
    }
}
