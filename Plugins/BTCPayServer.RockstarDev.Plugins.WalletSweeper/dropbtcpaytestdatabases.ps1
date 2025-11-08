$databases = @(
"btcpayserver2047478462",
"btcpayserver2114202355",
"btcpayserver2122862133",
"btcpayserver2182119047",
"btcpayserver2219574794",
"btcpayserver2287645886",
"btcpayserver240081690"
)

$pgUser = "postgres"
$pgHost = "localhost"
$pgPort = "39372"

Write-Host "Starting to drop $($databases.Count) databases..." -ForegroundColor Yellow

foreach ($db in $databases) {
    Write-Host "Dropping $db..." -NoNewline
    $query = "DROP DATABASE IF EXISTS ""$db"";"
    $output = & psql -U $pgUser -h $pgHost -p $pgPort -c $query 2>&1
    
    if ($output -match "does not exist") {
        Write-Host " NOT FOUND" -ForegroundColor Yellow
    }
    elseif ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    }
    else {
        Write-Host " FAILED: $output" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done! All databases processed." -ForegroundColor Green
