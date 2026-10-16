param(
    [Parameter(Mandatory)][string]$Query,
    [int]$TopK = 4,
    [string]$Index = "customer-service-kb",
    [string]$Namespace = "kb"
)

# La clave se busca en: PINECONE_API_KEY, appsettings.Local.json y appsettings.json (nunca se imprime).
$cli = Join-Path $PSScriptRoot "..\src\Ingest.Cli"
$placeholders = @("clave", "pega-aqui-tu-clave")
function Read-KeyFromFile($name) {
    $path = Join-Path $cli $name
    if (-not (Test-Path $path)) { return $null }
    try { return (Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json).Pinecone.ApiKey } catch { return $null }
}
function Is-Missing($k) { [string]::IsNullOrWhiteSpace($k) -or ($placeholders -contains $k) }

$apiKey = $env:PINECONE_API_KEY
if (Is-Missing $apiKey) { $apiKey = Read-KeyFromFile "appsettings.Local.json" }
if (Is-Missing $apiKey) { $apiKey = Read-KeyFromFile "appsettings.json" }
if (Is-Missing $apiKey) { Write-Error "No encontré la API key (PINECONE_API_KEY o appsettings.Local.json)."; exit 1 }

$headers = @{ "Api-Key" = $apiKey.Trim(); "X-Pinecone-API-Version" = "2025-04" }
$utf8 = [Text.Encoding]::UTF8

$embedJson = @{
    model      = "llama-text-embed-v2"
    parameters = @{ input_type = "query"; truncate = "END"; dimension = 1024 }
    inputs     = @(@{ text = $Query })
} | ConvertTo-Json -Depth 6
$embedding = Invoke-RestMethod -Method Post -Uri "https://api.pinecone.io/embed" -Headers $headers `
    -ContentType "application/json; charset=utf-8" -Body $utf8.GetBytes($embedJson)

$indexHost = (Invoke-RestMethod -Uri "https://api.pinecone.io/indexes/$Index" -Headers $headers).host

$queryJson = @{
    vector          = $embedding.data[0].values
    topK            = $TopK
    namespace       = $Namespace
    includeMetadata = $true
} | ConvertTo-Json -Depth 6 -Compress
$result = Invoke-RestMethod -Method Post -Uri "https://$indexHost/query" -Headers $headers `
    -ContentType "application/json; charset=utf-8" -Body $utf8.GetBytes($queryJson)

foreach ($m in $result.matches) {
    $preview = ($m.metadata.text -replace "\s+", " ")
    if ($preview.Length -gt 140) { $preview = $preview.Substring(0, 140) + "..." }
    "{0:N3}  {1,-9}  {2}" -f $m.score, $m.id, $preview
}
