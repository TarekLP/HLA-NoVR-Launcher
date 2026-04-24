# Define the project root and the output file
$projectRoot = Get-Location
$outputFile = Join-Path $projectRoot "ProjectOverview.txt"
$targetFolders = @("Models", "Services", "Styles", "ViewModels", "Views", "Converters")

Write-Host "Searching in: $projectRoot" -ForegroundColor Purple

# Initialize the file with a header
"PROJECT OVERVIEW - $(Get-Date)" | Out-File -FilePath $outputFile -Encoding utf8

foreach ($folderName in $targetFolders) {
    $folderPath = Join-Path $projectRoot $folderName
    
    if (Test-Path $folderPath) {
        Add-Content -Path $outputFile -Value "`n========================================"
        Add-Content -Path $outputFile -Value "FOLDER: $folderName"
        Add-Content -Path $outputFile -Value "========================================"
        
        # Find all .cs and .axaml files
        $files = Get-ChildItem -Path $folderPath -Recurse -Include *.cs, *.axaml
        
        if ($files.Count -eq 0) {
            Write-Host "No files found in $folderName" -ForegroundColor Yellow
        }

        foreach ($file in $files) {
            Write-Host "Processing: $($file.Name)" -ForegroundColor Gray
            Add-Content -Path $outputFile -Value "`n--- FILE: $($file.FullName) ---"
            Add-Content -Path $outputFile -Value "----------------------------------------"
            
            # Read and append the actual code content
            $content = Get-Content -Path $file.FullName -Raw
            Add-Content -Path $outputFile -Value $content
            Add-Content -Path $outputFile -Value "`n"
        }
    } else {
        Write-Host "Folder not found: $folderName" -ForegroundColor Red
    }
}

Write-Host "`nDone! Your code is ready in: $outputFile" -ForegroundColor DarkMagenta