[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Output "=== НАЧАЛО ОЧИСТКИ MSI/MSP ==="
Write-Output "Целевой путь: $targetPath"

Write-Output "1. Проверка существования папки..."
if (-not (Test-Path $targetPath)) {
    Write-Error "Директория $targetPath не существует!"
    exit 1
}

Write-Output "Папка существует"
Write-Output "2. Поиск файлов .msi и .msp..."

$allFiles = Get-ChildItem -Path $targetPath -File -ErrorAction SilentlyContinue
Write-Output "Всего файлов в папке: $($allFiles.Count)"

$filesToDelete = $allFiles | Where-Object { $_.Extension -in '.msi', '.msp' }

if ($filesToDelete.Count -eq 0) {
    Write-Output "Файлы с расширениями .msi и .msp не найдены."
    exit 0
}

$msiCount = ($filesToDelete | Where-Object { $_.Extension -eq '.msi' }).Count
$mspCount = ($filesToDelete | Where-Object { $_.Extension -eq '.msp' }).Count
$totalSize = [math]::Round(($filesToDelete | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

Write-Output "Найдено .msi файлов: $msiCount"
Write-Output "Найдено .msp файлов: $mspCount"
Write-Output "Общий размер: $totalSize MB"

Write-Output "3. Удаление файлов..."

$successCount = 0
$errorCount = 0

foreach ($file in $filesToDelete) {
    try {
        Remove-Item -Path $file.FullName -Force -ErrorAction Stop
        Write-Output "  Удален: $($file.Name)"
        $successCount++
    }
    catch {
        Write-Output "  Ошибка при удалении $($file.Name): $($_.Exception.Message)"
        $errorCount++
    }
}

Write-Output "4. Результат операции:"
Write-Output "  Успешно удалено: $successCount файлов"
Write-Output "  Ошибок при удалении: $errorCount файлов"
Write-Output "  Освобождено места: $totalSize MB"

Write-Output "=== ОЧИСТКА ЗАВЕРШЕНА ==="
Write-Output "=== ИНФОРМАЦИЯ О СВОБОДНОМ ПРОСТРАНСТВЕ НА ДИСКЕ БЫЛА ОБНОВЛЕНА ==="