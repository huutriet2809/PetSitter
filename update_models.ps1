$models = Get-ChildItem "c:\PetSitter\PetSitter\Models\*.cs"
foreach ($model in $models) {
    if ($model.Name -eq "PetSitterContext.cs") { continue }
    $content = Get-Content $model.FullName -Raw

    $content = [System.Text.RegularExpressions.Regex]::Replace($content,
        "\[Key\]\s*\[DatabaseGenerated\(DatabaseGeneratedOption\.Identity\)\]\s*public int (\w+Id)",
        "[BsonId]`r`n        [BsonRepresentation(BsonType.ObjectId)]`r`n        public string `$1")

    $content = [System.Text.RegularExpressions.Regex]::Replace($content,
        "public int (\w+Id)",
        "[BsonRepresentation(BsonType.ObjectId)]`r`n        public string `$1")

    if ($content -notmatch "using MongoDB.Bson") {
        $content = "using MongoDB.Bson;`r`nusing MongoDB.Bson.Serialization.Attributes;`r`n" + $content
    }

    Set-Content -Path $model.FullName -Value $content
}
