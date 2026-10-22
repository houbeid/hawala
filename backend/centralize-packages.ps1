# Centralise les versions NuGet dans Directory.Packages.props.
# Lit les versions déjà présentes dans les .csproj (celles posées par les templates),
# crée Directory.Packages.props, puis retire l'attribut Version des .csproj.
# À lancer une seule fois, depuis le dossier "hawala\" :
#   powershell -ExecutionPolicy Bypass -File .\centralize-packages.ps1

$ErrorActionPreference = "Stop"

if (Test-Path "Directory.Packages.props") {
    throw "Directory.Packages.props existe déjà : script déjà lancé ?"
}

# Garde la version la plus haute quand deux projets n'utilisent pas la même.
function Get-HigherVersion($a, $b) {
    try {
        $va = [version]($a -replace '-.*$', '')
        $vb = [version]($b -replace '-.*$', '')
        if ($va -ge $vb) { return $a } else { return $b }
    } catch {
        return $a
    }
}

$versions = @{}
$projects = Get-ChildItem -Path src, tests -Recurse -Filter *.csproj

foreach ($project in $projects) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($project.FullName)

    $refs = $xml.SelectNodes("//PackageReference[@Version]")
    if ($refs.Count -eq 0) { continue }

    foreach ($ref in $refs) {
        $id = $ref.GetAttribute("Include")
        $version = $ref.GetAttribute("Version")
        if ($versions.ContainsKey($id)) {
            $versions[$id] = Get-HigherVersion $versions[$id] $version
        } else {
            $versions[$id] = $version
        }
        $ref.RemoveAttribute("Version")
    }

    $xml.Save($project.FullName)
    Write-Host "Versions retirées : $($project.Name)" -ForegroundColor Cyan
}

$lines = @(
    '<Project>'
    ''
    '  <!-- Toutes les versions NuGet de la solution sont ici, et seulement ici.'
    '       Les .csproj déclarent <PackageReference Include="..." /> sans Version. -->'
    '  <PropertyGroup>'
    '    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>'
    '  </PropertyGroup>'
    ''
    '  <ItemGroup>'
)
foreach ($id in ($versions.Keys | Sort-Object)) {
    $lines += "    <PackageVersion Include=""$id"" Version=""$($versions[$id])"" />"
}
$lines += @(
    '  </ItemGroup>'
    ''
    '</Project>'
)

Set-Content -Path "Directory.Packages.props" -Value $lines -Encoding UTF8
Write-Host "Directory.Packages.props créé avec $($versions.Count) paquets." -ForegroundColor Green

dotnet build Hawala.slnx
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }
Write-Host "Compilation OK." -ForegroundColor Green
