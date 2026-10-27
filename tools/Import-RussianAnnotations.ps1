param([string]$SourceDirectory = (Join-Path $PSScriptRoot '../EmojiPicker/obj/cldr-48'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sources = [ordered]@{
    'annotations' = '325BC6BB96AFF4A5A9124A9087313669B0E88DA920B84E261D61A402EA9405A8'
    'annotationsDerived' = 'CD3AA5D40A42D473C7796584D560CAC9B35F223561DD3E0E57594FEC5003C010'
}
$map = @{}
foreach ($part in $sources.Keys) {
    $path = Join-Path $SourceDirectory "$part-ru.xml"
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Force -Path $SourceDirectory | Out-Null
        Invoke-WebRequest "https://raw.githubusercontent.com/unicode-org/cldr/release-48/common/$part/ru.xml" -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sources[$part]) {
        throw "CLDR checksum mismatch: $part"
    }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Ignore
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($path, $settings)
    try { $xml = [Xml.XmlDocument]::new(); $xml.Load($reader) } finally { $reader.Dispose() }
    foreach ($annotation in $xml.SelectNodes('/ldml/annotations/annotation')) {
        $key = $annotation.GetAttribute('cp').Replace([string][char]0xFE0F, '')
        $value = $annotation.InnerText.Trim()
        if (-not $value -or $value -match '[↑∅]') { continue }
        if (-not $map.ContainsKey($key)) { $map[$key] = @{ Name = ''; Keywords = @() } }
        if ($annotation.GetAttribute('type') -eq 'tts') { $map[$key].Name = $value }
        else { $map[$key].Keywords += @($value -split '\s*\|\s*') }
    }
}
# Original project-authored synonyms, distributed under the project's MIT licence.
$supplement = Get-Content (Join-Path $PSScriptRoot 'russian-supplement.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($key in $supplement.Keys) {
    if ($map.ContainsKey($key)) { throw "Supplement collides with CLDR: $key" }
    $map[$key] = $supplement[$key]
}
$aliases = Get-Content (Join-Path $PSScriptRoot 'russian-aliases.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($key in $aliases.Keys) {
    if (-not $map.ContainsKey($key)) { throw "Alias without CLDR entry: $key" }
    $map[$key].Keywords += $aliases[$key]
}
$result = [ordered]@{}
[string[]]$keys = @($map.Keys)
[Array]::Sort($keys, [StringComparer]::Ordinal)
foreach ($key in $keys) {
    $entry = $map[$key]
    if (-not $entry.Name) { throw "Missing CLDR name: $key" }
    $terms = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($term in $entry.Keywords) { [void]$terms.Add($term) }
    [string[]]$sortedTerms = @($terms)
    [Array]::Sort($sortedTerms, [StringComparer]::Ordinal)
    $result[$key] = [ordered]@{ Name = $entry.Name; Keywords = @($sortedTerms) }
}
$output = Join-Path $projectRoot 'EmojiPicker/Resources/keywords.ru.json'
[IO.File]::WriteAllText($output, ($result | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Russian annotations: $($result.Count) entries"
