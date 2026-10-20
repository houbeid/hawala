# Architecture — App Hawala Mauritanie–Sénégal

Mis à jour le 18 octobre 2026 — copie exportée depuis Claude Docs (source : https://claude.ai/artifact/Qzcyjj7dCmFiDsQJqKZVi5)

Un monolithe modulaire en .NET 10 avec PostgreSQL, et une PWA React bilingue (français / arabe) qui fonctionne hors ligne. Toute opération d'argent passe par un grand livre en partie double, ce qui rend le bénéfice, la caisse et le bilan exacts par construction.

## Vue d'ensemble

```text
PWA React (agents Nouakchott + Dakar, Superviseur)
        │  HTTPS /api/v1  +  SignalR
        ▼
Caddy (TLS) → Hawala.Api (.NET 10, monolithe modulaire)
        │   modules : Identity, Organization, Parties, Treasury, Ledger, Rates,
        │             Transfers, Cash, Profit, Balance, Notifications, Compliance
        ▼
PostgreSQL 17 (un schéma par module)   ·   Hangfire (tâches planifiées)
```

Les agents des deux agences utilisent la même PWA, qui parle à une seule API. Dans l'API, chaque module garde ses données, mais tout montant passe par le grand livre.

## Choix technologiques

Le backend reste dans votre zone de force (.NET) ; le frontend vise une PWA légère qui tient hors ligne sur un téléphone d'agent.

| Couche | Choix | Pourquoi |
| --- | --- | --- |
| API | ASP.NET Core (.NET 10 LTS), Minimal APIs | Support long terme, performant, votre stack |
| Architecture | Monolithe modulaire + Clean Architecture, CQRS léger | Une seule app à déployer, modules séparables plus tard |
| Commandes / requêtes | Handlers maison ou Mediator (source-generated) | MediatR est devenu payant ; pas besoin de lui |
| Validation | FluentValidation | Règles métier lisibles et testables |
| Base de données | PostgreSQL 17+ | Transactions ACID, `numeric` exact, verrous de ligne, JSONB pour l'audit |
| ORM | EF Core 10 (+ Dapper pour les rapports lourds) | Productivité ; SQL brut pour bilan et relevés |
| Auth | ASP.NET Core Identity, JWT court + refresh token rotatif, TOTP 2FA | Standard, sans dépendance externe |
| Temps réel | SignalR | Un transfert créé à Dakar apparaît tout de suite à Nouakchott |
| Tâches planifiées | Hangfire (stockage PostgreSQL) | Clôture du jour, snapshots du bilan |
| PDF | QuestPDF | Reçus et relevés de tiers |
| Messagerie | Plus tard : WhatsApp Cloud API (Meta) + SMS | Hors périmètre de la V1 |
| Logs / traces | Serilog + OpenTelemetry → Seq ou Grafana | Retrouver une opération de bout en bout |
| Frontend | React 19 + TypeScript + Vite | Écosystème PWA mature, bundle léger |
| UI | Tailwind CSS + shadcn/ui | Composants accessibles, RTL géré avec les propriétés logiques CSS |
| Données côté client | TanStack Query + TanStack Router | Cache, reprise, pagination |
| Formulaires | React Hook Form + Zod | Formulaire de transfert en 3 étapes, validation partagée |
| Hors ligne | vite-plugin-pwa (Workbox) + IndexedDB (Dexie) | File d'attente des opérations sans réseau |
| Langues | i18next (fr, ar) + `Intl.NumberFormat` | Français / arabe RTL, formats de montants |
| Graphiques | Recharts | Tableau de bord bénéfice et bilan |
| Tests | xUnit + Testcontainers (PostgreSQL réel), Playwright | Le moteur comptable doit être testé sur une vraie base |
| CI/CD | GitHub Actions → images Docker | Déploiement reproductible |

Alternatives écartées : Blazor WebAssembly (lourd au premier chargement sur mobile, hors ligne plus difficile) et microservices (trop de complexité pour deux agences).

## Modules métier

Douze modules, chacun avec son schéma SQL et son API. Ils ne se parlent que par des interfaces publiques ou des événements internes, jamais en lisant les tables d'un autre module.

| Module | Responsabilité | Dépend de |
| --- | --- | --- |
| Identité & accès | Comptes, connexion, 2FA, modèles de rôles, permissions, périmètre (agence, tiers autorisés), plafonds | — |
| Organisation | Pays, agences (Nouakchott, Dakar), couleurs, devise locale, paramètres | — |
| Tiers | Clients, fournisseurs, personnel ; fiche, solde, relevé, plafond de crédit, dédoublonnage téléphone | Organisation |
| Trésorerie | Caisses espèces, banques, wallets (Bankily, Sedad…) ; séparés des tiers | Organisation |
| Grand livre | Écritures en partie double, soldes, contre-passation ; seul module qui écrit les montants | — |
| Taux | Taux du marché du jour par agence, historique, verrouillage | Organisation |
| Transferts | Création, répartition des paiements, crédit, livraison (en espèces, wallet ou banque, éventuellement répartie), annulation, reçu, code de retrait | Tiers, Trésorerie, Grand livre, Taux |
| Caisse | Encaissements / décaissements manuels, virements internes, clôture journalière (compté vs théorique) | Trésorerie, Grand livre |
| Bénéfice | Calcul de la marge par transfert et par jour, enregistrement du bénéfice du jour | Transferts, Taux |
| Bilan | Trésorerie + résultat tiers − transferts non livrés vs capital initial ; snapshots datés | Grand livre, Transferts |
| Notifications | Notifications temps réel entre agences (WhatsApp / SMS : plus tard) | Transferts |
| Audit & conformité | Journal de toutes les actions (avant / après), alertes AML (seuils, fractionnement), point d'intégration HarisAI | Tous (abonné aux événements) |

## Modèle de données

Les montants sont en `numeric(18,2)` avec leur code devise (MRO ou XOF), jamais en `float`. L'ouguiya est stockée et affichée en MRO (ancienne ouguiya, 1 MRU = 10 MRO), comme dans l'app actuelle (décision validée). Les montants lus sur un relevé bancaire ou Bankily, souvent en MRU, sont convertis à la saisie avec l'unité affichée à côté du champ. Les identifiants sont des UUID v7 (triables par date) ; les références lisibles reprennent le format de l'app actuelle (ex. TR-RS18-HG01-2026-TGNC-20261019-000043, décision du 20 octobre 2026 ; sens des codes à préciser).

**Arrondi** (validé) :

1. Le FCFA est toujours en nombres entiers ; le taux garde 2 décimales (3 604,77).
2. Le montant MRO d'un transfert (FCFA × taux / 5000) est arrondi à l'unité la plus proche, 0,5 vers le haut (`MidpointRounding.AwayFromZero`, jamais l'arrondi par défaut de .NET vers le pair).
3. On arrondit une seule fois, à la création du transfert ; le montant arrondi est enregistré et jamais recalculé.
4. Bénéfice et bilan : calcul en pleine précision sur les montants enregistrés, arrondi seulement du total affiché.
5. MRU ↔ MRO : ×10 exact.
6. Un taux saisi avec plus de 2 décimales est refusé, jamais arrondi.
7. Les montants MRO hors transferts (caisse, banque, tiers) peuvent avoir des décimales.
8. La saisie en MRU avec décimales est acceptée (12,5 MRU = 125 MRO).

Si le test de référence (536 893 MRO) montre un écart, on ajuste le point 4, jamais la règle des transferts.

| Entité | Champs clés | Remarques |
| --- | --- | --- |
| Country | code, nom, devise | MR → MRO, SN → XOF |
| Agency | nom, pays, couleur, fuseau | Nouakchott, Dakar |
| User | email, téléphone, nom, actif, 2FA, dernière connexion | Identity |
| RoleTemplate | nom, liste de permissions | Superviseur, Agent, Caissier, Consultation |
| UserAccess | user, agence(s), rôle, permissions ajoutées / retirées, plafonds | Surcharges par compte |
| UserPartyScope | user, tiers autorisé | Actif si « Limiter les tiers » |
| Party (tiers) | nom, téléphone (E.164), NNI, adresse, genre, compte comptable, dans le bilan, plafond de crédit, note | Client, fournisseur, personnel |
| TreasuryAccount | nom, type (espèces / banque / wallet), agence, devise, actif | Les « méthodes de paiement » |
| LedgerAccount | code, type, devise, propriétaire (tiers, trésorerie, système) | Un par tiers × devise, un par caisse |
| JournalEntry | date opération, type, source (transfert, caisse…), auteur, contre-passe | Immuable |
| JournalLine | entrée, compte, débit, crédit, devise | Σ débit = Σ crédit par devise |
| DailyRate | agence, date, prix d'achat et prix de vente du jour (MRO pour 5 000 FCFA), fixés par le Superviseur, verrouillé | Base du bénéfice |
| Transfer | référence, sens (envoyé / reçu), agence source / destination, montant FCFA (entier), prix de vente appliqué et prix d'achat du jour (fixés par le Superviseur), montant MRO, crédit, statut, émetteur, bénéficiaire, hash du code de retrait, essais restants, note | Version (concurrence optimiste) |
| TransferPayment | transfert, étape (paiement du client / livraison au bénéficiaire), compte de trésorerie ou tiers, montant | Répartition à la création et à la livraison |
| TransferEvent | transfert, action, auteur, horodatage, motif, diff JSON | Historique Ajouté / Modifié / Livré / Annulé |
| CashMovement | compte, sens, montant, tiers, libellé, date | Encaissement / décaissement manuel, approvisionnement de caisse, retrait du patron |
| CashClosing | compte, date, théorique, compté, écart, validé par | Clôture journalière |
| CashBudgetSnapshot | agence, date, espèces, banques, soldes débiteurs, soldes créditeurs, non livrés, budget, enregistré par | Budget de la caisse, enregistré chaque jour par l'agence |
| ProfitSnapshot | agence, date, taux du jour, bénéfice MRO, détail | Onglet Bénéfice |
| BalanceSnapshot | date, taux, trésorerie, résultat tiers, non livrés, capital, résultat net | Historique du bilan |
| AuditLog | qui, quoi, entité, avant / après (JSONB), IP, appareil | Inséré seulement, jamais modifié |
| ComplianceAlert | règle, tiers, transfert(s), score, statut | Seuils, fractionnement, listes |

## Moteur comptable

Chaque action d'argent produit une écriture équilibrée par devise ; les soldes de caisse, de tiers et le bilan se lisent directement dans le grand livre. Une écriture n'est jamais modifiée ni supprimée : on la contre-passe.

Le passage MRO ↔ FCFA se fait par un compte système « Position de change », qui garde chaque devise équilibrée. Exemple : transfert de F FCFA payé M MRO à Nouakchott, livré à Dakar.

| Événement | Débit | Crédit | Devise |
| --- | --- | --- | --- |
| Création, payé comptant | Trésorerie NKC (une ligne par méthode de paiement) | Position de change | MRO |
| Création (contrepartie) | Position de change | Transferts à livrer | XOF |
| Création avec crédit activé | Tiers client (rouge : il nous doit) | Position de change | MRO |
| Livraison à Dakar | Transferts à livrer | Trésorerie Dakar (une ligne par mode : espèces, Wave, Orange Money, banque…) | XOF |
| Annulation | Contre-passation de l'écriture de création |  |  |
| Encaissement manuel | Trésorerie | Tiers ou produit | locale |
| Décaissement manuel | Tiers ou charge | Trésorerie | locale |
| Virement interne | Trésorerie destination | Trésorerie source | locale |

Décisions validées pour le grand livre :

- Sens des soldes : débit − crédit (tiers : positif = rouge, négatif = vert).
- Comptes système par agence : Position de change, Transferts à livrer, Apports du patron, Écarts et ajustements, Soldes d'ouverture (contrepartie du solde initial d'un tiers, rattachée au capital).
- Date d'opération saisissable dans le passé, distincte de l'instant d'enregistrement (UTC).
- Contre-passation : motif obligatoire, une seule par écriture, jamais de contre-passation d'une contre-passation.
- Transaction sur plusieurs modules : une connexion PostgreSQL par requête, partagée par les DbContext des modules d'argent, et une transaction ouverte par un décorateur commun.
- En base : déclencheur refusant UPDATE / DELETE sur les écritures, contrôle d'équilibre par devise au COMMIT.

Le sens inverse (reçu de Dakar, payé à Nouakchott) suit la même logique, devises échangées. Le type d'opération fixe le signe : l'agent ne choisit jamais « + » ou « − ».

La livraison se répartit comme le paiement : le bénéficiaire peut être payé en espèces, par wallet ou par virement, ou par plusieurs moyens à la fois. L'écran « Livrer » reprend donc le sélecteur « Méthode de paiement », et le total livré doit égaler le montant du transfert.

**Code de retrait** (règle validée) : l'expéditeur le transmet lui-même au bénéficiaire.

1. À la création, l'app génère un code aléatoire de 6 chiffres, affiché une seule fois et imprimé sur le reçu de l'expéditeur.
2. L'expéditeur l'envoie au bénéficiaire (appel, message).
3. À l'agence de destination, l'agent saisit le code avant de livrer. Il ne voit jamais le code, il ne peut que le vérifier.
4. Le code est stocké haché ; après 5 essais faux, le transfert est bloqué et seul le Superviseur peut le débloquer.
5. Code perdu : le Superviseur peut livrer sans code, avec pièce d'identité et motif obligatoire, tracé dans l'audit. L'expéditeur peut aussi demander un nouveau code à l'agence d'envoi.

**Bénéfice d'un transfert** (T = prix d'achat du jour (taux du marché) fixé par le Superviseur ; M découle du prix de vente, en MRO pour 5 000 FCFA) :

```latex
\text{On reçoit des FCFA, on paie des MRO : } B = F \cdot \frac{T}{5000} - M
```

```latex
\text{On reçoit des MRO, on paie des FCFA : } B = M - F \cdot \frac{T}{5000}
```

Le bénéfice du jour est la somme sur les transferts du jour. Cette formule redonne exactement les 536 893 MRO de l'app actuelle.

**Bilan général** (tout converti en MRO au taux du bilan) :

```latex
R\acute{e}sultat\ net = Tr\acute{e}sorerie + R\acute{e}sultat\ tiers - Transferts\ non\ livr\acute{e}s - Capital\ initial
```

Sur l'exemple vu, on retrouve −261 005 MRO, soit −0,16 % d'un capital de 165 500 000.

Garanties techniques :

- Une commande = une transaction PostgreSQL (écritures + transfert + audit ensemble).
- Clé d'idempotence sur chaque création, pour qu'un double clic ou une resynchronisation hors ligne ne crée pas deux transferts.
- Verrou de ligne ou numéro de version sur le transfert : deux agents ne peuvent pas le livrer en même temps.
- Contrôle en base : une écriture déséquilibrée est refusée.

## Agences, caisses et taux

Un seul patron possède deux caisses : Nouakchott en MRO, Dakar en FCFA. Il n'y a donc pas de compte de dette entre agences.

| Règle | Décision |
| --- | --- |
| Transfert Nouakchott → Dakar | L'ouguiya entre dans la caisse de Nouakchott, le FCFA sort de la caisse de Dakar (et inversement), via le compte « Position de change » |
| Remplissage des caisses | Hors périmètre. L'agent enregistre seulement « Approvisionnement de caisse » ou « Retrait du patron » (montant seul), via un compte « Apports du patron » rattaché au capital |
| Caisse insuffisante | Livraison autorisée ; la caisse peut passer en négatif, avec un avertissement sans blocage |
| Taux du jour | Un seul taux par jour pour les deux agences (prix d'achat, prix de vente) ; sans taux du jour, pas de transfert ; modifiable à tout moment, les transferts existants gardent leurs taux ; prix d'achat visible avec `profit.view` seulement |
| Transferts (décisions du 20 octobre 2026) | Crédit partiel possible ; modification d'un non livré : texte par l'agence d'envoi, montant / taux / répartition par le Superviseur (contre-passation + nouvelle écriture) ; une seule livraison (répartie) ; pas d'annulation d'un livré pour l'instant ; date d'opération future refusée |
| Création d'un transfert | Toujours par l'agence d'envoi (source = agence de l'agent, verrouillée) ; l'autre agence le voit en « reçu ». Le Superviseur peut créer dans les deux sens |
| Expéditeur, bénéficiaire, crédit | Expéditeur et bénéficiaire en texte libre ; crédit activé : la dette va sur un tiers choisi dans la liste (créé s'il n'existe pas), en rouge dans l'agence d'envoi ; crédit partiel ou total ; plafond bloquant pour tous, Superviseur compris (solde après transfert ≤ plafond) ; crédit sans part à crédit refusé |
| Périmètre d'un agent | Une seule agence. Il voit les transferts envoyés et reçus par son agence, sa caisse et ses tiers ; jamais ceux de l'autre agence. Mêmes règles à Nouakchott et à Dakar |
| Périmètre du Superviseur | Les deux agences : comptes, taux, bilan total |
| Taux | Seul le Superviseur fixe les deux taux du jour : le prix d'achat (taux du marché, T) et le prix de vente (taux client, appliqué automatiquement aux nouveaux transferts, modifiable par lui pour un transfert précis) ; le serveur refuse tout autre compte |
| Taux du jour | Prix d'achat et prix de vente, « 5 000 FCFA = ? MRO », enregistrés jour par jour. Commission d'un transfert = écart entre les deux × montant (ex. 5 000 000 FCFA vendus à 3 650, achetés à 3 600 : 50 000 MRO) |
| Ce que voit une agence | Ses montants (acheté, donné au client) et le prix de vente. Prix d'achat, commission et les quatre cartes de l'écran des transferts (entrées, sorties, envoyé, livré) : Superviseur seulement |

Le périmètre est appliqué par un filtre global EF Core sur l'agence ; le choix de l'agence est verrouillé dans l'interface d'un agent.

Couleur d'agence : modifiable par le Superviseur (permission `agencies.manage`) ; chaque agence a sa propre couleur, deux agences ne peuvent pas partager la même. Le changement du nom d'une agence est reporté ; le code (NKC, DKR) ne change jamais.

## Comptes de trésorerie

Décisions validées : caisses, banques et wallets par agence (types Cash, Bank, Wallet), chacun avec son compte au grand livre ouvert dans la même transaction. Seul le Superviseur les gère (`treasury.manage`). Un agent voit les comptes de son agence, et leurs soldes seulement avec `cash.budget.read`. Plusieurs caisses par agence, nom unique par agence. Désactivation seulement à solde nul, réactivation possible. Pas d'unité d'affichage par compte : tout s'affiche en MRO à Nouakchott (FCFA à Dakar) ; le MRU ne sert qu'à la saisie, convertie en MRO × 10. Solde de départ à la mise en service : écriture d'ouverture contre « Soldes d'ouverture ».

Le périmètre par agence est appliqué par un filtre global EF Core, à partir d'un `IAgencyScope` (Identity.Contracts) : l'agence du compte connecté, ou toutes pour un Superviseur.

## Tiers

Décisions validées : un tiers est commun aux deux agences (une fiche), avec un solde par agence (un compte au grand livre par agence) ; un agent ne voit que le solde de son agence, et seulement avec `parties.view_balance`. Type = compte comptable : Client (41), Fournisseur (40), Personnel (42), Autre. Obligatoires : nom (30 caractères) et type ; téléphone (E.164) et NNI facultatifs mais uniques ; nom non unique ; pas de champ Homme / Femme (le « genre » de l'app actuelle désignait banque / non banque). « Considéré comme banque » supprimé (les banques sont des comptes de trésorerie). Plafond de crédit par tiers et par agence, fixé par le Superviseur, bloquant (appliqué en phase 3). Un agent peut créer un tiers (`parties.create`) ; seul le Superviseur le modifie (`parties.manage` : fiche, solde initial, plafond, « dans le bilan », archivage). Le solde initial n'est jamais saisi à la création : toujours par l'écriture d'ouverture de la différence. Téléphone et NNI en clair jusqu'à la phase 6 (chiffrement + empreinte). Pas de suppression : archivage à solde nul dans les deux agences. « Dans le bilan » vrai par défaut, modifiable par le Superviseur.

## Budget de la caisse

Chaque agence enregistre son budget une fois par jour : c'est sa clôture journalière, dans sa devise (MRO à Nouakchott, FCFA à Dakar).

```latex
\text{Budget caisse} = \text{espèces} + \text{banques} + \text{soldes débiteurs} - \text{soldes créditeurs} - \text{transferts non livrés}
```

- **Enregistrement** : une photo datée du budget. L'historique affiche ces photos sur une période, avec une courbe.
- **Contrôle de cohérence** : analyse les opérations faites depuis le dernier budget enregistré et signale les anomalies avant validation.
- **Alerte « taux de change inhabituel »** : le taux d'un transfert s'écarte de plus de 5 % du taux du jour (avec 3 600 : hors de 3 420 à 3 780). Alerte seulement, sans blocage.
- **Permissions** : `cash.budget.read` (lire le budget) et `cash.budget.save` (l'enregistrer).

Ce budget par agence donne le résultat de chaque agence sans règle de partage du bénéfice ; le bilan général reste réservé au Superviseur.

**D'où viennent les chiffres.** Tout est lu dans le grand livre, pour l'agence et dans sa devise ; rien n'est saisi à la main. Chaque ligne s'ouvre sur son détail.

| Ligne | Source |
| --- | --- |
| Espèces | Solde des caisses en espèces de l'agence |
| Banques | Solde de ses comptes banque et wallets |
| Soldes débiteurs | Tiers en rouge de l'agence, marqués « dans le bilan » |
| Soldes créditeurs | Tiers en vert de l'agence, marqués « dans le bilan » |
| Transferts non livrés | Transferts que l'agence doit encore payer, dans sa devise |

**Corriger un solde.** Personne n'écrit un solde à la place du calcul. Le Superviseur dispose de trois moyens, tous tracés :

1. **Solde initial d'un tiers** (par agence) : modifiable par le Superviseur à tout moment, comme dans l'ancienne app. Chaque modification passe par une écriture d'ouverture de la différence et apparaît dans l'audit (avant / après).
2. **Ajustement de solde** : Superviseur seulement, motif obligatoire, écriture vers un compte « Écarts et ajustements ».
3. **Contre-passation** d'une opération erronée, puis saisie de la bonne.

Les budgets déjà enregistrés ne changent pas ; le contrôle de cohérence signale les ajustements et les changements de solde initial depuis le dernier budget.

## Cycle de vie d'un transfert

```text
En attente de synchro (hors ligne) ──► Non livré ──► Livré   (verrouillé)
                │                          └────► Annulé  (verrouillé, motif obligatoire)
                └──► Rejeté (refus du serveur)
```

Seul « Non livré » est modifiable, et chaque modification garde l'ancienne valeur dans l'historique. Une erreur sur un transfert livré se corrige par une écriture inverse, jamais en effaçant.

## Sécurité et permissions

L'accès se décide en trois couches, vérifiées côté serveur à chaque requête : la permission (puis-je faire cette action ?), le périmètre (sur cette agence, ce tiers ?) et le plafond (pour ce montant ?).

| Couche | Contenu | Mise en œuvre |
| --- | --- | --- |
| Permission | `transfers.create`, `transfers.deliver`, `transfers.cancel`, `transfers.edit_rate` (Superviseur seulement), `rates.set_daily` (Superviseur seulement), `transfers.credit`, `cash.create`, `cash.budget.read`, `cash.budget.save`, `parties.view_balance`, `profit.view`, `balance.view`, `export`, `users.manage`… | Policies ASP.NET Core ; modèle de rôle + ajouts / retraits par compte |
| Droits par défaut | Validés le 20 octobre 2026 (tableau dans le skill) : l'agent crée, livre, fait crédit, crée des tiers, voit les soldes de son agence, n'annule pas ; le caissier livre, tient la caisse et enregistre son budget ; la consultation lit et exporte | `RolePermissions` (Identity.Domain) |
| Périmètre | Agence du compte (un agent = une agence ; le Superviseur voit les deux), liste de tiers autorisés | Filtre global EF Core sur chaque requête ; jamais seulement côté interface |
| Plafond | Montant max par transfert, par jour, crédit max | Validation dans le handler de commande |

Authentification et comptes :

- Connexion par email ou téléphone dans un seul champ. Email obligatoire et unique ; téléphone facultatif, unique, en E.164 (sans indicatif pays : refusé). Même erreur que le compte existe ou non.
- Mot de passe haché (Identity), 8 caractères minimum dont un chiffre, verrouillage de 15 minutes après 5 échecs, 2FA TOTP obligatoire pour le Superviseur.
- Compte créé par le Superviseur avec un mot de passe temporaire ; le changer à la première connexion est la seule action permise avant.
- Jeton d'accès de 15 minutes (identité seulement : compte et `SecurityVersion`) + refresh token rotatif de 12 heures en cookie `HttpOnly`, `Secure`, `SameSite=Strict`, haché en base. Rejouer un ancien refresh token ferme toute la session.
- Permissions lues côté serveur (option B) : à chaque requête, la `SecurityVersion` du jeton est comparée à celle du compte (cache de 30 s vidé à chaque modification). Blocage, changement de rôle ou de mot de passe : effet immédiat ; déconnexion en direct via SignalR en plus.
- Plusieurs Superviseurs possibles ; le dernier Superviseur actif ne peut être ni bloqué ni rétrogradé. Le premier est créé au démarrage depuis la configuration.
- Un Superviseur ne peut ni bloquer, ni changer le rôle, ni réinitialiser le mot de passe de son propre compte ; aucun motif n'est exigé pour bloquer.
- Déblocage : l'ancien mot de passe n'est plus valable ; un nouveau mot de passe temporaire est généré et le verrouillage est levé.
- Réinitialisation du mot de passe par le Superviseur : nouveau mot de passe temporaire généré, verrouillage levé, toutes les sessions coupées ; possible sur un compte bloqué et sur un autre Superviseur.
- Le Superviseur peut modifier le nom, l'email et le téléphone d'un compte, y compris le sien (unicité vérifiée comme à la création, téléphone retirable) ; les sessions ne sont pas coupées.
- Liste des comptes : nom, email, téléphone, rôle, agence, statut, mot de passe à changer, verrouillé jusqu'à ; filtres agence / rôle / statut et recherche texte ; tri par nom ; pas de pagination au départ. Dernière connexion : phase 6.
- Liste des appareils connectés par compte.

Audit :

- Chaque commande écrit une ligne d'audit (qui, quoi, avant / après, IP, appareil) dans la même transaction.
- Changements de permissions lisibles : « X a retiré *Annuler un transfert* à Y ».
- Table d'audit en insertion seule (droit `INSERT` uniquement pour l'utilisateur applicatif).

Protection des données : HTTPS partout, NNI et téléphones chiffrés au repos (Data Protection API), identifiants techniques jamais affichés, limitation de débit sur la connexion et l'API.

## Hors ligne, temps réel et notifications

Le réseau tombe ; l'agent doit pouvoir continuer à saisir. La PWA garde les données de référence en local et met les nouvelles opérations en file d'attente.

- **Lecture hors ligne** : tiers autorisés, comptes de trésorerie, taux du jour et transferts récents en IndexedDB.
- **Écriture hors ligne** : seules les créations (transfert, encaissement, décaissement) sont mises en file, avec une clé d'idempotence générée sur le téléphone. Elles s'affichent « En attente de synchronisation ».
- **Interdit hors ligne** : livrer, annuler, modifier un taux, clôturer la caisse. Ces actions exigent l'état serveur à jour.
- **Synchronisation** : au retour du réseau, envoi dans l'ordre ; un refus (plafond, tiers bloqué) remonte à l'agent avec le motif.
- **Temps réel** : hub SignalR par agence ; un transfert créé à Nouakchott apparaît « Non livré » à Dakar sans rafraîchir.
- **Notifications client** : reportées à une phase ultérieure (WhatsApp / SMS). En V1, le reçu PDF s'imprime ou se télécharge depuis l'app.

## Structure de la solution

Un dépôt unique : l'API .NET et la PWA côte à côte, chaque module backend dans son propre projet avec ses couches.

```text
hawala/
├─ src/
│  ├─ Hawala.Api/                  # hôte : Program.cs, auth, SignalR, Hangfire, OpenAPI
│  ├─ Hawala.SharedKernel/         # Money, Currency, Result, événements, horloge
│  └─ Modules/
│     ├─ Identity/
│     │  ├─ Identity.Domain/         # entités, règles
│     │  ├─ Identity.Application/    # commandes, requêtes, validateurs
│     │  ├─ Identity.Infrastructure/ # EF Core (schéma identity), services externes
│     │  └─ Identity.Contracts/      # ce que les autres modules ont le droit d'appeler
│     ├─ Organization/  ├─ Parties/  ├─ Treasury/  ├─ Ledger/
│     ├─ Rates/  ├─ Transfers/  ├─ Cash/  ├─ Profit/
│     └─ Balance/  ├─ Notifications/  └─ Compliance/
├─ tests/
│  ├─ Ledger.Tests/                # équilibre, contre-passation, soldes
│  ├─ Transfers.Tests/             # bénéfice 536 893, bilan −261 005
│  ├─ Architecture.Tests/          # un module ne référence que les Contracts d'un autre
│  └─ E2E/                         # Playwright
├─ web/                            # PWA React
│  ├─ src/app/                     # routes, layout, thème, RTL
│  ├─ src/features/                # transfers, cash, parties, profit, balance, security
│  ├─ src/shared/ui/               # composants shadcn, MoneyInput, RateInput
│  ├─ src/shared/api/              # client généré depuis OpenAPI
│  ├─ src/offline/                 # Dexie, file d'attente, synchronisation
│  └─ src/i18n/                    # fr.json, ar.json
└─ deploy/                         # docker-compose, Caddyfile, scripts de sauvegarde
```

Conventions d'API :

- REST sous `/api/v1`, une route par cas d'usage : `POST /transfers`, `POST /transfers/{id}/deliver`, `POST /transfers/{id}/cancel`.
- En-tête `Idempotency-Key` obligatoire sur les créations ; `If-Match` (version) sur les modifications.
- Erreurs au format Problem Details (RFC 9457), messages traduits côté client.
- Client TypeScript généré depuis le contrat OpenAPI, pour que le front et le back ne divergent pas.

## Déploiement et exploitation

Au départ, un seul serveur suffit pour deux agences ; l'architecture permet de passer à plusieurs instances sans réécriture.

| Élément | Départ | Quand ça grandit |
| --- | --- | --- |
| Serveur | 1 VPS (4 vCPU, 8 Go) avec Docker Compose | 2 instances API derrière un load balancer, Redis comme backplane SignalR |
| Base | PostgreSQL dans un conteneur, volume dédié | PostgreSQL managé avec réplica |
| Proxy / TLS | Caddy (certificats automatiques) derrière Cloudflare | Idem |
| Sauvegardes | `pg_dump` chaque nuit + WAL archivé (pgBackRest) vers un stockage objet hors serveur, rétention 30 jours | Restauration testée chaque mois |
| Supervision | Seq ou Grafana (logs, traces), alerte si l'API ou la base tombe | Tableau de bord métier (volume, écarts de caisse) |
| Environnements | dev, staging, production | Idem |
| Mises à jour | GitHub Actions : tests → image Docker → migration EF → redémarrage | Déploiement sans coupure |

La PWA affiche sa version et propose la mise à jour quand une nouvelle version est publiée, comme le « v1.0.57 » de l'app actuelle mais automatiquement.

## Plan de réalisation

Le cœur comptable vient en premier : tout le reste écrit dedans. Chaque phase se termine par une version utilisable.

1. **Fondations** : dépôt, CI, Docker, Identity (connexion, 2FA), Organisation (pays, agences), squelette PWA bilingue avec thème clair / sombre. Fait : authentification (connexion, rafraîchissement, déconnexion, mot de passe, blocage immédiat), permissions côté serveur, agences NKC et DKR, gestion des comptes par le Superviseur (création, liste, bloquer / débloquer, rôle, agence, réinitialisation du mot de passe, profil) , couleur d'agence ; 253 tests sur PostgreSQL. Restent : CI, squelette PWA ; 2FA reportée (à discuter).
2. **Grand livre + Trésorerie + Tiers** : comptes, écritures, soldes, fiche et relevé de tiers. Tests sur les équilibres et les contre-passations. **Fait (19 octobre 2026)** : grand livre (partie double par devise, contre-passation, relevé, déclencheurs d'immuabilité et d'équilibre en base), trésorerie (caisses, banques, wallets), tiers (fiche commune, solde et plafond par agence, solde initial, archivage, liste avec totaux, relevé) ; 456 tests sur PostgreSQL.
3. **Transferts** : taux du jour, formulaire en 3 étapes, répartition des paiements, crédit, livraison, annulation avec motif, historique, temps réel entre agences.
4. **Caisse** : encaissements, décaissements, virements internes, approvisionnement et retrait du patron, budget de caisse enregistré chaque jour, contrôle de cohérence.
5. **Bénéfice + Bilan** : calculs, snapshots, tableaux de bord. Validation avec les chiffres de l'app actuelle (536 893 et −261 005).
6. **Sécurité avancée** : modèles de rôles, périmètre par tiers, plafonds, audit lisible.
7. **Hors ligne + reçu** : file d'attente, synchronisation, reçu PDF.
8. **Migration + mise en service** : import des tiers et de leurs soldes à la date de démarrage, sans les anciens transferts, période en parallèle, bascule.
9. **Conformité** : alertes AML (seuils, fractionnement), branchement HarisAI. Plus tard : notifications clients par WhatsApp / SMS.

## Questions ouvertes

- [x] Stocker en MRU ou en MRO ? Réponse : MRO, comme l'app actuelle.
- [x] Un transfert à un taux inférieur au taux du jour (3 580 contre 3 629,88) est-il normal, ou faut-il bloquer / alerter ? Réponse : seul le Superviseur fixe les taux ; alerte « taux inhabituel » au-delà de 5 % d'écart avec le taux du jour, sans blocage.
- [x] Comment Nouakchott et Dakar se règlent entre eux (compensation, banque, transport d'espèces) ? Cela décide du compte de liaison entre agences. Réponse : un seul patron et deux caisses, donc pas de compte de liaison ; le remplissage des caisses est hors périmètre.
- [x] Un compte peut-il travailler sur les deux pays ? Réponse : un agent = une agence ; seul le Superviseur voit les deux.
- [x] Que contient le bouton « + » de l'écran de modification d'un transfert (livraison, annulation, autre) ? Réponse : il ouvre simplement un nouveau transfert vide (raccourci de création, comme « Ajouter » dans la liste) ; il ne livre pas, n'annule pas et ne recopie rien. Aucun effet sur le backend.
- [x] Faut-il importer l'historique de l'ancienne app, ou seulement les tiers et leurs soldes ? Réponse : seulement les tiers et leurs soldes.
- [x] Hébergement : contrainte de localisation des données (Mauritanie / Sénégal) ou serveur en Europe acceptable ? Réponse : Europe acceptable.
