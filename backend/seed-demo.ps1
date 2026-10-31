<#
.SYNOPSIS
  Remplit une base de DÉMONSTRATION avec des données fictives, pour présenter l'app.

.DESCRIPTION
  Tout passe par l'API, avec les mêmes règles et les mêmes écritures que l'app : comptes de démo, caisses, banques et
  wallets avec leur solde de départ, tiers avec soldes et plafonds, taux des 30 derniers jours, transferts dans les deux
  sens (comptant, à crédit, livrés, non livrés, annulés, livré sans code, codes faux, vendus à un autre prix que celui du
  jour, paiement réparti deux fois sur la même caisse), mouvements de caisse, ajustement, capitaux (par caisse et général),
  puis les photos du jour (budget, bénéfice, bilan par caisse, bilan général) et quelques opérations faites APRÈS ces photos,
  pour que « Expliquer la variation » ait quelque chose à montrer.

  Les courbes demandent au moins deux jours de photos : le serveur date toujours une photo du jour même. Pour les voir,
  lancer ensuite seed-demo-history.ps1 (historique fictif, écrit directement dans la base de démo).

  À LANCER SUR UNE BASE DE DÉMONSTRATION SEULEMENT (ex. hawala_demo), jamais sur la base de production : le grand livre
  est immuable, ces données ne s'effacent pas. Toutes les données sont fictives (emails en @example.com).

  Utilisation (PowerShell, API démarrée sur la base de démo) :
    .\seed-demo.ps1                      # API sur http://localhost:5215
    .\seed-demo.ps1 -ApiUrl http://localhost:5215 -Days 30

  Le script demande l'email et le mot de passe du Superviseur (celui de SeedSupervisor), puis un mot de passe commun
  pour les comptes de démo. Aucun mot de passe n'est affiché ni écrit sur le disque.
#>
param(
    [string]$ApiUrl = 'http://localhost:5215',
    [ValidateRange(7, 60)][int]$Days = 30,
    [int]$Seed = 2026
)

$ErrorActionPreference = 'Stop'
$api = "$ApiUrl/api/v1"
$inv = [Globalization.CultureInfo]::InvariantCulture
$rng = New-Object System.Random $Seed
$now = [DateTime]::UtcNow
$today = $now.Date

function Rand([int]$min, [int]$max) { $rng.Next($min, $max + 1) }
function Chance([double]$p) { $rng.NextDouble() -lt $p }
function Day([datetime]$d) { $d.ToString('yyyy-MM-dd', $inv) }
function Iso([datetime]$t) { $t.ToString("yyyy-MM-ddTHH:mm:ss'Z'", $inv) }
function Plain([Security.SecureString]$s) {
    [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

# Un appel à l'API : JSON en UTF-8, pause courte (limite de 300 requêtes par minute et par compte), 429 = attendre.
function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null, [string]$Key = $null, [switch]$AllowError)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    if ($Key) { $headers['Idempotency-Key'] = $Key }
    $params = @{ Method = $Method; Uri = "$api$Path"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    for ($try = 1; ; $try++) {
        Start-Sleep -Milliseconds 100
        try {
            $response = Invoke-WebRequest @params
            if ($response.Content) { return ($response.Content | ConvertFrom-Json) }
            return $null
        }
        catch {
            $status = 0
            if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            if ($status -eq 429 -and $try -le 6) {
                Write-Host '  (limite de débit atteinte : pause de 15 s)'
                Start-Sleep -Seconds 15
                continue
            }
            if ($AllowError) { return [pscustomobject]@{ failed = $true; status = $status; body = $_.ErrorDetails.Message } }
            throw "$Method $Path a échoué ($status) : $($_.ErrorDetails.Message)"
        }
    }
}

function New-Key { "demo-$([guid]::NewGuid().ToString('N'))" }

# Heure d'opération dans la journée, jamais dans le futur.
function OpTime([datetime]$day, [int]$minHour, [int]$maxHour) {
    $t = [DateTime]::SpecifyKind($day.AddHours((Rand $minHour $maxHour)).AddMinutes((Rand 0 59)), 'Utc')
    $limit = $now.AddMinutes(-3)
    if ($t -gt $limit) { $t = $limit.AddMinutes(-(Rand 0 20)) }
    if ($t -lt $day) { $t = $day.AddMinutes(1) }
    return $t
}

Write-Host ''
Write-Host '=== Données de démonstration ETS EL-VETH ===' -ForegroundColor Cyan
Write-Host "API : $ApiUrl"
Write-Host 'À utiliser sur une base de DÉMONSTRATION seulement : ces données ne pourront pas être effacées.' -ForegroundColor Yellow
if ((Read-Host 'Est-ce bien une base de démonstration ? (oui / non)') -ne 'oui') { Write-Host 'Arrêt.'; exit 1 }
if ($now.Hour -lt 2) { Write-Host 'Il est moins de 2 h (UTC) : relancez plus tard, la journée en cours serait trop courte.'; exit 1 }

# --- Connexion du Superviseur ---------------------------------------------------------------------------------------
$supEmail = Read-Host 'Email du Superviseur'
$supPassword = Plain (Read-Host 'Mot de passe du Superviseur' -AsSecureString)
$auth = Invoke-Api POST '/auth/login' @{ login = $supEmail; password = $supPassword }
if ($auth.mustChangePassword) {
    Write-Host 'Mot de passe temporaire : il faut le changer (8 caractères au moins, dont un chiffre).'
    $newPassword = Plain (Read-Host 'Nouveau mot de passe du Superviseur' -AsSecureString)
    $auth = Invoke-Api POST '/auth/change-password' @{ currentPassword = $supPassword; newPassword = $newPassword } $auth.accessToken
}
$sup = $auth.accessToken

$existing = Invoke-Api GET "/parties?includeArchived=true&search=$([uri]::EscapeDataString('Démo'))" -Token $sup
if ($existing.totalCount -gt 0) { Write-Host 'Des données de démo sont déjà présentes : rien n''est ajouté.' -ForegroundColor Yellow; exit 1 }
$startDate = Invoke-Api GET '/setup/start-date' -Token $sup
$firstDay = $today.AddDays(-$Days)
$fixedStart = $startDate.startDate
if ($fixedStart -is [string]) { $fixedStart = [datetime]::ParseExact($fixedStart, 'yyyy-MM-dd', $inv) }
if ($fixedStart -and $fixedStart.Date -gt $firstDay) {
    Write-Host "Date de démarrage fixée au $(Day $fixedStart) : impossible d'écrire $Days jours d'historique." -ForegroundColor Yellow
    exit 1
}

$agencies = Invoke-Api GET '/agencies' -Token $sup
$nkc = $agencies | Where-Object { $_.code -eq 'NKC' }
$dkr = $agencies | Where-Object { $_.code -eq 'DKR' }

$demoPassword = Plain (Read-Host 'Mot de passe commun des comptes de démo (8 caractères au moins, dont un chiffre)' -AsSecureString)

# --- Comptes de démo ---------------------------------------------------------------------------------------------------
Write-Host '1/7 Comptes de démo'
function New-DemoAccount([string]$email, [string]$name, [string]$role, $agency) {
    $created = Invoke-Api POST '/users' @{ email = $email; phone = $null; fullName = $name; role = $role; agencyId = $agency.id } $sup
    $login = Invoke-Api POST '/auth/login' @{ login = $email; password = $created.temporaryPassword }
    $changed = Invoke-Api POST '/auth/change-password' @{ currentPassword = $created.temporaryPassword; newPassword = $demoPassword } $login.accessToken
    return $changed.accessToken
}
$agentNkc = New-DemoAccount 'agent.nkc.demo@example.com' 'Agent Démo Nouakchott' 'Agent' $nkc
$agentDkr = New-DemoAccount 'agent.dkr.demo@example.com' 'Agent Démo Dakar' 'Agent' $dkr
$null = New-DemoAccount 'caissier.dkr.demo@example.com' 'Caissier Démo Dakar' 'Cashier' $dkr
$tokens = @{ $nkc.id = $agentNkc; $dkr.id = $agentDkr }

# --- Comptes de trésorerie et soldes de départ -------------------------------------------------------------------------
Write-Host '2/7 Caisses, banques et wallets'
$balances = @{}
function New-Treasury($agency, [string]$name, [string]$type, [decimal]$opening) {
    $created = Invoke-Api POST '/treasury-accounts' @{ name = $name; type = $type; agencyId = $agency.id } $sup
    $null = Invoke-Api PUT "/treasury-accounts/$($created.id)/opening-balance" @{ amount = $opening; operationDate = (Day $firstDay) } $sup
    $balances[$created.id] = $opening
    return $created.id
}
$acc = @{
    nkcCash   = New-Treasury $nkc 'Caisse principale' 'Cash' 25000000
    nkcWallet = New-Treasury $nkc 'Bankily' 'Wallet' 8000000
    nkcBank   = New-Treasury $nkc 'Banque Nouakchott' 'Bank' 40000000
    dkrCash   = New-Treasury $dkr 'Caisse principale' 'Cash' 30000000
    dkrWallet = New-Treasury $dkr 'Wave' 'Wallet' 10000000
    dkrOrange = New-Treasury $dkr 'Orange Money' 'Wallet' 5000000
}
$cashOf = @{ $nkc.id = $acc.nkcCash; $dkr.id = $acc.dkrCash }
$walletOf = @{ $nkc.id = $acc.nkcWallet; $dkr.id = $acc.dkrWallet }

# --- Tiers ---------------------------------------------------------------------------------------------------------------
Write-Host '3/7 Tiers'
$partyBalance = @{}   # "<tiers>|<agence>" -> solde (positif = il nous doit)
function New-Party([string]$name, [string]$type, $openings, $limits) {
    $created = Invoke-Api POST '/parties' @{ name = $name; type = $type; phone = $null; nni = $null; address = $null; note = 'Tiers de démonstration' } $sup
    foreach ($agency in @($nkc, $dkr)) {
        $amount = [decimal]0
        if ($openings.ContainsKey($agency.code)) {
            $amount = [decimal]$openings[$agency.code]
            $null = Invoke-Api PUT "/parties/$($created.id)/agencies/$($agency.id)/opening-balance" @{ amount = $amount; operationDate = (Day $firstDay) } $sup
        }
        $partyBalance["$($created.id)|$($agency.id)"] = $amount
        if ($limits.ContainsKey($agency.code)) {
            $null = Invoke-Api PUT "/parties/$($created.id)/agencies/$($agency.id)/credit-limit" @{ creditLimit = [decimal]$limits[$agency.code] } $sup
        }
    }
    return $created.id
}
$p = @{
    atlas  = New-Party 'Client Démo Atlas' 'Client' @{ NKC = 1200000 } @{ NKC = 6000000 }
    baobab = New-Party 'Client Démo Baobab' 'Client' @{ NKC = -450000 } @{}
    cedre  = New-Party 'Client Démo Cèdre' 'Client' @{ DKR = 800000 } @{ DKR = 0 }
    dune   = New-Party 'Client Démo Dune' 'Client' @{} @{ NKC = 4000000 }
    etoile = New-Party 'Client Démo Étoile' 'Client' @{ DKR = 250000 } @{ DKR = 3000000 }
    sahel  = New-Party 'Fournisseur Démo Sahel' 'Supplier' @{ NKC = -2500000 } @{}
    ocean  = New-Party 'Fournisseur Démo Océan' 'Supplier' @{ DKR = -1500000 } @{}
    staff  = New-Party 'Personnel Démo 1' 'Staff' @{ NKC = 150000 } @{}
    other  = New-Party 'Associé Démo' 'Other' @{ NKC = -300000; DKR = 120000 } @{}
}
$creditLimit = @{ "$($p.atlas)|$($nkc.id)" = 6000000; "$($p.dune)|$($nkc.id)" = 4000000; "$($p.etoile)|$($dkr.id)" = 3000000 }

# --- Taux du jour ------------------------------------------------------------------------------------------------------
Write-Host '4/7 Taux du jour'
$rates = @{}
$purchases = @{}
for ($i = $Days; $i -ge 0; $i--) {
    $d = $today.AddDays(-$i)
    $purchase = 3600 + (Rand 0 25)
    $sale = $purchase + 40 + (Rand 0 15)
    $null = Invoke-Api PUT "/rates/$(Day $d)" @{ purchaseRate = [decimal]$purchase; saleRate = [decimal]$sale } $sup
    $rates[(Day $d)] = [decimal]$sale
    $purchases[(Day $d)] = [decimal]$purchase
}

# --- Opérations jour par jour -------------------------------------------------------------------------------------------
Write-Host '5/7 Transferts et caisse (cela prend quelques minutes)'
$transfers = New-Object System.Collections.ArrayList
$counter = 0

# Approvisionne la caisse de l'agence avant une sortie trop grande (comme le ferait le patron).
function Ensure-Cash($agency, [decimal]$needed, [datetime]$at) {
    $cash = $cashOf[$agency.id]
    if ($balances[$cash] -ge $needed + 1000000) { return }
    $missing = $needed + 1000000 - $balances[$cash]
    $amount = [decimal]([Math]::Ceiling([double]($missing / 5000000)) * 5000000)
    $null = Invoke-Api POST '/cash-movements' @{
        agencyId = $agency.id; type = 'OwnerContribution'; treasuryAccountId = $cash; amount = $amount
        note = 'Approvisionnement de caisse (démo)'; operationAt = (Iso $at.AddMinutes(-2))
    } $tokens[$agency.id] (New-Key)
    $balances[$cash] += $amount
}

function Deliver($t, [datetime]$at) {
    $dest = $t.dest
    $amount = $t.toDeliver
    $account = $cashOf[$dest.id]
    if ((Chance 0.3) -and $balances[$walletOf[$dest.id]] -ge $amount + 500000) { $account = $walletOf[$dest.id] }
    else { Ensure-Cash $dest $amount $at }
    $null = Invoke-Api POST "/transfers/$($t.id)/deliver" @{
        code = $t.code; operationAt = (Iso $at); payments = [object[]]@(@{ treasuryAccountId = $account; amount = $amount }); note = $null
    } $tokens[$dest.id]
    $balances[$account] -= $amount
    $t.delivered = $true
}

for ($i = $Days - 1; $i -ge 0; $i--) {
    $day = $today.AddDays(-$i)
    $sale = $rates[(Day $day)]
    $count = if ($i -eq 0) { Rand 2 4 } else { Rand 2 5 }

    for ($n = 0; $n -lt $count; $n++) {
        $counter++
        $fromNkc = Chance 0.8
        $source = if ($fromNkc) { $nkc } else { $dkr }
        $dest = if ($fromNkc) { $dkr } else { $nkc }
        $xof = [decimal](5000 * (Rand 20 400))
        # L'agent peut vendre à un autre prix que celui du jour (30 octobre 2026) ; un transfert à plus de 5 % d'écart
        # fait apparaître l'alerte « taux inhabituel » du contrôle de cohérence.
        $applied = $sale
        if (Chance 0.1) { $applied = $sale + (Rand 5 30) }
        if ($i -eq 2 -and $n -eq 0) { $applied = [decimal][Math]::Round([double]$sale * 1.07) }
        $mro = ($xof / 5000) * $applied
        $toPay = if ($fromNkc) { $mro } else { $xof }
        $at = OpTime $day 8 18

        # Paiement : comptant (une ou deux lignes) ou en partie à crédit sur un client.
        $payments = @()
        $creditParty = $null
        $creditCandidates = if ($fromNkc) { @($p.atlas, $p.dune) } else { @($p.etoile) }
        if (Chance 0.12) {
            $candidate = $creditCandidates[(Rand 0 ($creditCandidates.Count - 1))]
            $key = "$candidate|$($source.id)"
            $credit = [decimal]::Floor($toPay * 0.6)
            if ($partyBalance[$key] + $credit -le $creditLimit[$key]) {
                $creditParty = $candidate
                $paid = $toPay - $credit
                $payments = @(@{ treasuryAccountId = $cashOf[$source.id]; amount = $paid })
                $balances[$cashOf[$source.id]] += $paid
                $partyBalance[$key] += $credit
            }
        }
        if (-not $creditParty) {
            if (Chance 0.25) {
                $cashPart = [decimal]::Floor($toPay * 0.6)
                $walletPart = $toPay - $cashPart
                $payments = @(
                    @{ treasuryAccountId = $cashOf[$source.id]; amount = $cashPart },
                    @{ treasuryAccountId = $walletOf[$source.id]; amount = $walletPart })
                $balances[$cashOf[$source.id]] += $cashPart
                $balances[$walletOf[$source.id]] += $walletPart
            }
            elseif (Chance 0.1) {
                # La même caisse deux fois dans la répartition (27 octobre 2026).
                $first = [decimal]::Floor($toPay / 2)
                $payments = @(
                    @{ treasuryAccountId = $cashOf[$source.id]; amount = $first },
                    @{ treasuryAccountId = $cashOf[$source.id]; amount = $toPay - $first })
                $balances[$cashOf[$source.id]] += $toPay
            }
            else {
                $payments = @(@{ treasuryAccountId = $cashOf[$source.id]; amount = $toPay })
                $balances[$cashOf[$source.id]] += $toPay
            }
        }

        $label = '{0:000}' -f $counter
        $note = if (Chance 0.2) { 'Urgent, client régulier (démo)' } else { $null }
        $created = Invoke-Api POST '/transfers' @{
            destinationAgencyId = $dest.id; amountXof = $xof
            senderInfo = "Expéditeur démo $label"; beneficiaryInfo = "Bénéficiaire démo $label"; note = $note
            operationAt = (Iso $at); payments = [object[]]$payments; creditPartyId = $creditParty
            saleRate = $(if ($applied -ne $sale) { $applied } else { $null })
        } $tokens[$source.id] (New-Key)

        $t = [pscustomobject]@{
            id = $created.id; reference = $created.reference; code = $created.withdrawalCode; at = $at; day = $i
            source = $source; dest = $dest; toDeliver = $(if ($fromNkc) { $xof } else { $mro }); delivered = $false; special = $null
        }
        $null = $transfers.Add($t)

        # Transferts gardés pour les actions particulières (annulation, sans code, codes faux, nouveau code).
        if ($i -eq 10 -and $n -eq 0) { $t.special = 'cancel' }
        elseif ($i -eq 0 -and $n -eq 0) { $t.special = 'cancel' }
        elseif ($i -eq 5 -and $n -eq 0) { $t.special = 'withoutCode' }
        elseif ($i -eq 3 -and $n -eq 0) { $t.special = 'wrongCodes' }
        elseif ($i -eq 1 -and $n -eq 0) { $t.special = 'newCode' }

        if ($t.special) { continue }
        $deliverChance = if ($i -ge 2) { 0.96 } elseif ($i -eq 1) { 0.7 } else { 0.35 }
        if (Chance $deliverChance) {
            $deliverAt = $at.AddMinutes((Rand 30 600))
            if ($deliverAt -gt $now.AddMinutes(-1)) { $deliverAt = $now.AddMinutes(-1) }
            Deliver $t $deliverAt
        }
    }

    # Caisse du jour : encaissement d'un client, paiement d'un fournisseur, virement interne, retrait du patron.
    foreach ($agency in @($nkc, $dkr)) {
        $token = $tokens[$agency.id]
        $cash = $cashOf[$agency.id]
        $isNkc = $agency.code -eq 'NKC'
        $clients = if ($isNkc) { @($p.atlas, $p.dune, $p.staff) } else { @($p.cedre, $p.etoile, $p.other) }
        $supplier = if ($isNkc) { $p.sahel } else { $p.ocean }

        if (Chance 0.45) {
            $client = $clients[(Rand 0 ($clients.Count - 1))]
            $key = "$client|$($agency.id)"
            if ($partyBalance[$key] -ge 100000) {
                $amount = [decimal]([Math]::Floor([double]($partyBalance[$key] * 0.4) / 1000) * 1000)
                $body = @{ agencyId = $agency.id; type = 'Receipt'; treasuryAccountId = $cash; partyId = $client; amount = $amount
                    note = 'Remboursement (démo)'; operationAt = (Iso (OpTime $day 9 17)) }
                if ($isNkc -and (Chance 0.4)) { $body.amount = $amount / 10; $body.unit = 'MRU'; $body.note = 'Remboursement saisi en MRU (démo)' }
                $null = Invoke-Api POST '/cash-movements' $body $token (New-Key)
                $balances[$cash] += $amount
                $partyBalance[$key] -= $amount
            }
        }
        if (Chance 0.3) {
            $key = "$supplier|$($agency.id)"
            $amount = [decimal](1000 * (Rand 100 600))
            if (-not $isNkc) { $amount = [decimal](5000 * (Rand 20 120)) }
            if (-$partyBalance[$key] -ge $amount -and $balances[$cash] -ge $amount + 2000000) {
                $null = Invoke-Api POST '/cash-movements' @{ agencyId = $agency.id; type = 'Payment'; treasuryAccountId = $cash; partyId = $supplier
                    amount = $amount; note = 'Règlement fournisseur (démo)'; operationAt = (Iso (OpTime $day 9 17)) } $token (New-Key)
                $balances[$cash] -= $amount
                $partyBalance[$key] += $amount
            }
        }
    }
    if ($i % 7 -eq 3 -and $balances[$acc.nkcCash] -gt 20000000) {
        $null = Invoke-Api POST '/cash-movements' @{ agencyId = $nkc.id; type = 'InternalTransfer'; treasuryAccountId = $acc.nkcCash
            destinationTreasuryAccountId = $acc.nkcBank; amount = [decimal]10000000; note = 'Dépôt en banque (démo)'; operationAt = (Iso (OpTime $day 16 17)) } $agentNkc (New-Key)
        $balances[$acc.nkcCash] -= 10000000
        $balances[$acc.nkcBank] += 10000000
    }
    if ($i % 10 -eq 5 -and $balances[$acc.nkcCash] -gt 40000000) {
        $null = Invoke-Api POST '/cash-movements' @{ agencyId = $nkc.id; type = 'OwnerWithdrawal'; treasuryAccountId = $acc.nkcCash
            amount = [decimal]15000000; note = 'Retrait du patron (démo)'; operationAt = (Iso (OpTime $day 17 18)) } $sup (New-Key)
        $balances[$acc.nkcCash] -= 15000000
    }
    Write-Host ("  {0} : {1} transferts" -f (Day $day), $count)
}

# --- Actions particulières -----------------------------------------------------------------------------------------------
Write-Host '6/7 Annulations, livraison sans code, codes faux, nouveau code, corrections'
foreach ($t in $transfers | Where-Object { $_.special }) {
    switch ($t.special) {
        'cancel' {
            $null = Invoke-Api POST "/transfers/$($t.id)/cancel" @{ reason = 'Le client a renoncé au transfert (démo)' } $sup
        }
        'withoutCode' {
            $at = $t.at.AddHours(3)
            Ensure-Cash $t.dest $t.toDeliver $at
            $null = Invoke-Api POST "/transfers/$($t.id)/deliver-without-code" @{
                idDocument = 'CNI DEMO 0001'; reason = 'Code perdu par le bénéficiaire (démo)'; operationAt = (Iso $at)
                payments = [object[]]@(@{ treasuryAccountId = $cashOf[$t.dest.id]; amount = $t.toDeliver }); note = $null
            } $sup
            $balances[$cashOf[$t.dest.id]] -= $t.toDeliver
            $t.delivered = $true
        }
        'wrongCodes' {
            $wrong = if ($t.code -eq '000000') { '111111' } else { '000000' }
            foreach ($attempt in 1..2) {
                $null = Invoke-Api POST "/transfers/$($t.id)/deliver" @{
                    code = $wrong; operationAt = (Iso $t.at.AddHours(2))
                    payments = [object[]]@(@{ treasuryAccountId = $cashOf[$t.dest.id]; amount = $t.toDeliver }); note = $null
                } $tokens[$t.dest.id] -AllowError
            }
            Deliver $t $t.at.AddHours(2).AddMinutes(10)
        }
        'newCode' {
            $null = Invoke-Api POST "/transfers/$($t.id)/new-code" -Token $tokens[$t.source.id]
        }
    }
}

# Un encaissement saisi deux fois, puis annulé par le Superviseur ; un petit écart de caisse ajusté.
$dupKey = "$($p.atlas)|$($nkc.id)"
if ($partyBalance[$dupKey] -ge 50000) {
    $dup = Invoke-Api POST '/cash-movements' @{ agencyId = $nkc.id; type = 'Receipt'; treasuryAccountId = $acc.nkcCash; partyId = $p.atlas
        amount = [decimal]50000; note = 'Saisi en double (démo)' } $agentNkc (New-Key)
    $null = Invoke-Api POST "/cash-movements/$($dup.id)/cancel" @{ reason = 'Saisie en double (démo)' } $sup
}
$null = Invoke-Api POST '/balance-adjustments' @{ agencyId = $nkc.id; target = 'TreasuryAccount'; targetId = $acc.nkcCash
    amount = [decimal]-2500; reason = 'Écart constaté au comptage du soir (démo)' } $sup

# --- Capital et photos du jour ------------------------------------------------------------------------------------------
Write-Host '7/7 Capitaux, photos du jour, puis quelques opérations après les photos'
$null = Invoke-Api PUT '/balance/capital' @{ agencyId = $nkc.id; amount = [decimal]70000000; reason = 'Capital de démonstration' } $sup
$null = Invoke-Api PUT '/balance/capital' @{ agencyId = $dkr.id; amount = [decimal]45000000; reason = 'Capital de démonstration' } $sup
$null = Invoke-Api PUT '/balance/general/capital' @{ amount = [decimal]100000000; reason = 'Capital général de démonstration' } $sup
foreach ($agency in @($nkc, $dkr)) {
    $null = Invoke-Api POST '/cash-budget/snapshots' @{ agencyId = $agency.id } $sup
    $null = Invoke-Api POST '/balance/snapshots' @{ agencyId = $agency.id } $sup
    $null = Invoke-Api POST '/profit/snapshots' @{ agencyId = $agency.id } $sup
}
$null = Invoke-Api POST '/profit/snapshots' @{ agencyId = $null } $sup
$balanceRate = $purchases[(Day $today)]
$null = Invoke-Api POST '/balance/general/snapshots' @{ rate = $balanceRate } $sup

# Après les photos : un remboursement à Nouakchott, un approvisionnement à Dakar et un transfert pas encore livré.
# « Expliquer la variation » les retrouve poste par poste (espèces, créances, non livrés).
$late = $now.AddMinutes(-2)
$repayKey = "$($p.atlas)|$($nkc.id)"
if ($partyBalance[$repayKey] -ge 200000) {
    $null = Invoke-Api POST '/cash-movements' @{ agencyId = $nkc.id; type = 'Receipt'; treasuryAccountId = $acc.nkcCash; partyId = $p.atlas
        amount = [decimal]200000; note = 'Remboursement du soir (démo)'; operationAt = (Iso $late) } $agentNkc (New-Key)
}
$null = Invoke-Api POST '/cash-movements' @{ agencyId = $dkr.id; type = 'OwnerContribution'; treasuryAccountId = $acc.dkrCash
    amount = [decimal]5000000; note = 'Approvisionnement du soir (démo)'; operationAt = (Iso $late) } $agentDkr (New-Key)
$lateXof = [decimal]1000000
$lateMro = ($lateXof / 5000) * $rates[(Day $today)]
$null = Invoke-Api POST '/transfers' @{
    destinationAgencyId = $dkr.id; amountXof = $lateXof; senderInfo = 'Expéditeur démo du soir'; beneficiaryInfo = 'Bénéficiaire démo du soir'
    note = $null; operationAt = (Iso $late); payments = [object[]]@(@{ treasuryAccountId = $acc.nkcCash; amount = $lateMro }); creditPartyId = $null
} $agentNkc (New-Key)

$delivered = @($transfers | Where-Object { $_.delivered }).Count
Write-Host ''
Write-Host 'Terminé.' -ForegroundColor Green
Write-Host ("  {0} transferts ({1} livrés), 6 comptes de trésorerie, 9 tiers, {2} jours de taux." -f ($transfers.Count + 1), $delivered, ($Days + 1))
Write-Host ("  Bilan général sauvegardé au taux de {0} ; capital général 100 000 000 MRO." -f $balanceRate.ToString($inv))
Write-Host '  Pour les courbes (30 jours de photos fictives) : .\seed-demo-history.ps1'
Write-Host '  Comptes de démo (mot de passe commun choisi plus haut) :'
Write-Host '    agent.nkc.demo@example.com     Agent, Nouakchott'
Write-Host '    agent.dkr.demo@example.com     Agent, Dakar'
Write-Host '    caissier.dkr.demo@example.com  Caissier, Dakar'
