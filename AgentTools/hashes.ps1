# Print short MD5 of every WarehouseASRS source file (to verify uploads).
Set-Location D:\UnityAIProjs\DigitalTwin\WarehouseASRS
Get-ChildItem Assets\WarehouseASRS -Recurse -Include *.cs,*.shader | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -Algorithm MD5 $_.FullName).Hash.Substring(0, 8).ToLower() + " " + $_.Name
}

