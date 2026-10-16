param(
    [string]$IndexName = "customer-service-kb",
    [int]$Dimension = 1024,
    [string]$Metric = "cosine",
    [string]$Cloud = "aws",
    [string]$Region = "us-east-1"
)

# Crea el índice en el proyecto de la API key. Si ya existe no hace nada.
# La clave se busca en: PINECONE_API_KEY, appsettings.Local.json y appsettings.json. Nunca se imprime.
$cli = Join-Path $PSScriptRoot "..\src\Ingest.Cli"
$placeholders = @("", "clave", "pega-aqui-tu-clave")

function Read-KeyFromFile($name) {
    $path = Join-Path $cli $name
    if (-not (Test-Path $path)) { return $null }
    try { return (Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json).Pinecone.ApiKey } catch { return $null }
}

function Is-Missing($k) { [string]::IsNullOrWhiteSpace($k) -or ($placeholders -contains $k) }

$apiKey = $env:PINECONE_API_KEY; $source = "variable PINECONE_API_KEY"
if (Is-Missing $apiKey) { $apiKey = Read-KeyFromFile "appsettings.Local.json"; $source = "appsettings.Local.json" }
if (Is-Missing $apiKey) { $apiKey = Read-KeyFromFile "appsettings.json"; $source = "appsettings.json" }
if (Is-Missing $apiKey) {
    Write-Error "No encontré una API key válida. Ponla en src/Ingest.Cli/appsettings.Local.json o en PINECONE_API_KEY."
    exit 1
}
$apiKey = $apiKey.Trim()
Write-Output "Clave tomada de: $source (longitud $($apiKey.Length) caracteres)"

$headers = @{ "Api-Key" = $apiKey; "X-Pinecone-API-Version" = "2025-04" }

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri "https://api.pinecone.io/indexes" -Headers $headers
    $existing = ($response.Content | ConvertFrom-Json).indexes
} catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "sin respuesta" }
    Write-Error "No se pudo listar los índices. Código HTTP: $status. $($_.Exception.Message)"
    exit 1
}

$found = $existing | Where-Object { $_.name -eq $IndexName }
if ($found) {
    Write-Output "El índice '$IndexName' ya existe (dimensión $($found.dimension), métrica $($found.metric)). No se hizo nada."
    exit 0
}

$body = @{
    name      = $IndexName
    dimension = $Dimension
    metric    = $Metric
    spec      = @{ serverless = @{ cloud = $Cloud; region = $Region } }
} | ConvertTo-Json -Depth 5

try {
    Invoke-RestMethod -Method Post -Uri "https://api.pinecone.io/indexes" -Headers ($headers + @{ "Content-Type" = "application/json" }) -Body $body | Out-Null
} catch {
    $detail = $_.ErrorDetails.Message
    Write-Error "Pinecone rechazó la creación: $detail"
    exit 1
}

for ($i = 0; $i -lt 30; $i++) {
    $info = Invoke-RestMethod -Uri "https://api.pinecone.io/indexes/$IndexName" -Headers $headers
    if ($info.status.ready) {
        Write-Output "Índice '$IndexName' creado y listo: dimensión $($info.dimension), métrica $($info.metric), $Cloud/$Region."
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Output "Índice '$IndexName' creado, pero aún no figura como listo. Revisa la consola en unos segundos."
