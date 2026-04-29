# EnfySense Quick Release Push Script

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "      EnfySense Quick Release Push" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

$features = Read-Host "Enter new features (or just press Enter to use automated notes)"

if ([string]::IsNullOrWhiteSpace($features)) {
    if (Test-Path "RELEASE_NOTES.md") {
        Write-Host "Using existing notes from RELEASE_NOTES.md" -ForegroundColor Cyan
        $commitMsg = "Release: Automated improvements"
    } else {
        $features = "General improvements and bug fixes"
        "- $features" | Out-File -FilePath "RELEASE_NOTES.md" -Encoding utf8
        $commitMsg = "Release: $features"
    }
} else {
    Write-Host "`nUpdating RELEASE_NOTES.md..." -ForegroundColor Yellow
    "- $features" | Out-File -FilePath "RELEASE_NOTES.md" -Encoding utf8
    $commitMsg = "Release: $features"
}

# Git commands
Write-Host "Committing and pushing to GitHub..." -ForegroundColor Yellow
git add .
git commit -m "$commitMsg"
git push

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "   Success! GitHub is now building vNext" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
pause
