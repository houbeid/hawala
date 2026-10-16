# Création du squelette de la solution Hawala (backend).
# À lancer une seule fois, depuis le dossier "hawala\", dans PowerShell :
#   powershell -ExecutionPolicy Bypass -File .\init-solution.ps1

$ErrorActionPreference = "Stop"

# Lance une commande et s'arrête si elle échoue (PowerShell ne le fait pas seul pour dotnet/git).
# Fonction simple, sans bloc param : les arguments comme "-o" ou "-n" sont passés tels quels
# à dotnet au lieu d'être interprétés par PowerShell.
function Run {
    $exe  = $args[0]
    $rest = @($args | Select-Object -Skip 1)
    Write-Host "> $exe $($rest -join ' ')" -ForegroundColor Cyan
    & $exe @rest
    if ($LASTEXITCODE -ne 0) { throw "Échec : $exe $($rest -join ' ')" }
}

# --- Racine ---------------------------------------------------------------
Run git init
Run dotnet new gitignore
Run dotnet new globaljson --roll-forward latestFeature   # fige le SDK .NET 10 installé
Run dotnet new sln -n Hawala                              # crée Hawala.slnx (.NET 10)

# --- src ------------------------------------------------------------------
$ID = "src/Modules/Identity"

Run dotnet new classlib -n Hawala.SharedKernel     -o src/Hawala.SharedKernel
Run dotnet new classlib -n Identity.Contracts      -o "$ID/Identity.Contracts"
Run dotnet new classlib -n Identity.Domain         -o "$ID/Identity.Domain"
Run dotnet new classlib -n Identity.Application    -o "$ID/Identity.Application"
Run dotnet new classlib -n Identity.Infrastructure -o "$ID/Identity.Infrastructure"
Run dotnet new webapi   -n Hawala.Api              -o src/Hawala.Api

# --- tests ----------------------------------------------------------------
Run dotnet new xunit -n Hawala.SharedKernel.Tests -o tests/Hawala.SharedKernel.Tests
Run dotnet new xunit -n Identity.Domain.Tests     -o tests/Identity.Domain.Tests
Run dotnet new xunit -n Identity.IntegrationTests -o tests/Identity.IntegrationTests
Run dotnet new xunit -n Architecture.Tests        -o tests/Architecture.Tests

# --- Chemins des projets ---------------------------------------------------
$SharedKernel   = "src/Hawala.SharedKernel/Hawala.SharedKernel.csproj"
$Contracts      = "$ID/Identity.Contracts/Identity.Contracts.csproj"
$Domain         = "$ID/Identity.Domain/Identity.Domain.csproj"
$Application    = "$ID/Identity.Application/Identity.Application.csproj"
$Infrastructure = "$ID/Identity.Infrastructure/Identity.Infrastructure.csproj"
$Api            = "src/Hawala.Api/Hawala.Api.csproj"
$SkTests        = "tests/Hawala.SharedKernel.Tests/Hawala.SharedKernel.Tests.csproj"
$DomainTests    = "tests/Identity.Domain.Tests/Identity.Domain.Tests.csproj"
$IntegTests     = "tests/Identity.IntegrationTests/Identity.IntegrationTests.csproj"
$ArchTests      = "tests/Architecture.Tests/Architecture.Tests.csproj"

# --- Ajout à la solution --------------------------------------------------
Run dotnet sln Hawala.slnx add $SharedKernel $Contracts $Domain $Application $Infrastructure $Api $SkTests $DomainTests $IntegTests $ArchTests

# --- Références : de l'intérieur vers l'extérieur -------------------------
Run dotnet add $Contracts      reference $SharedKernel
Run dotnet add $Domain         reference $SharedKernel $Contracts
Run dotnet add $Application    reference $Domain
Run dotnet add $Infrastructure reference $Application
Run dotnet add $Api            reference $Infrastructure

Run dotnet add $SkTests     reference $SharedKernel
Run dotnet add $DomainTests reference $Domain
Run dotnet add $IntegTests  reference $Api
Run dotnet add $ArchTests   reference $Api

# --- Nettoyage des fichiers d'exemple des templates -----------------------
Get-ChildItem -Path src, tests -Recurse -Include Class1.cs, UnitTest1.cs | Remove-Item

# --- Vérification ---------------------------------------------------------
Run dotnet build Hawala.slnx
Write-Host "Squelette créé et compilé." -ForegroundColor Green
