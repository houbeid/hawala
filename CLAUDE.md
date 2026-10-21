# Hawala — backend .NET 10 (Nouakchott ↔ Dakar)

PWA de transfert d'argent entre l'agence de Nouakchott (MRO) et celle de Dakar (FCFA / XOF).
Développeur : Hamade (Windows, PowerShell, Docker Desktop, VS Code). **Toujours répondre en français.**

## À lire avant de coder

- Règles métier validées : skill `hawala-mr-sn-domaine` (`.claude/skills/hawala-mr-sn-domaine/SKILL.md`). C'est la source de vérité.
- Méthode de travail : skill `layered-backend-builder` (`.claude/skills/layered-backend-builder/SKILL.md`).
- Architecture complète : @docs/architecture.md

## Méthode de travail (imposée par Hamade)

- Avancer **une fonctionnalité complète par tour** (option A, choisie le 19 octobre 2026) : Domain → Application → Infrastructure → Api → tests d'une même fonctionnalité, puis build + tests, et on s'arrête en attendant son retour. Chaque tour se termine au vert. S'arrêter quand même en cours de tour pour : une question métier, une modification risquée d'un fichier déjà testé, une migration à générer (c'est Hamade qui la lance), un problème de conception. Les simples ajouts à des fichiers testés sont signalés dans le compte rendu.
- Ordre strict : Domain → Application → Infrastructure → Api, puis les tests.
- **Tests à la fin de chaque fonctionnalité** (`dotnet build` puis `dotnet test`). Ne jamais laisser une suite rouge.
- **Prévenir avant de modifier un fichier déjà testé** : quel fichier, pourquoi, quel risque.
- **Ne jamais inventer une règle métier** : poser la question, et noter les cas non tranchés en `// POINT OUVERT : …` dans le code.
- Hamade lance lui-même build, tests et migrations, sauf s'il demande le contraire. Arrêter l'API (Ctrl+C) avant `dotnet test`, sinon les fichiers sont verrouillés.
- Quand une règle est tranchée : mettre à jour le skill `hawala-mr-sn-domaine` et `docs/architecture.md`.

## Sécurité et données

- Données fictives uniquement dans le code, les tests et les seeds (jamais de vrais noms, téléphones, emails ou NNI). Emails de test en `@example.com`.
- Secrets (`Jwt:SigningKey` ≥ 32 octets, `SeedSupervisor:*`) : `dotnet user-secrets` en dev, variables d'environnement en prod. Jamais dans un `appsettings*.json` versionné.
- Le mot de passe temporaire est affiché une fois et n'est jamais journalisé ni stocké en clair.
- `docker-compose.yml` (postgres:17, base/utilisateur `hawala`, mot de passe `hawala_dev`) : dev local seulement.

## Conventions du code

- `Hawala.slnx`, `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, analyseurs), versions NuGet centralisées dans `Directory.Packages.props` (un nouveau csproj ne met **pas** de `Version` sur ses `PackageReference`, sinon NU1008).
- Monolithe modulaire. Chaque module : `X.Contracts` / `X.Domain` / `X.Application` / `X.Infrastructure`. Un module ne voit un autre que par ses `Contracts` (vérifié par `tests/Architecture.Tests`). Domain et Application ne référencent ni EF Core, ni ASP.NET Core, ni Npgsql.
- Un schéma PostgreSQL par module (`identity`, `organization`…), chacun avec sa table `__ef_migrations_history`. Pas de clé étrangère entre schémas.
- Handlers maison (pas de MediatR) : `ICommand`, `ICommand<T>`, `ICommandHandler`, `IQuery<T>`, `IQueryHandler` (`Hawala.SharedKernel.Messaging`).
- `Result` / `Result<T>` / `Error` avec `ErrorType` ; codes stables `Module.Groupe.Raison` (ex. `Identity.Account.EmailTaken`). L'API les traduit en ProblemDetails avec un champ `code` via `ErrorResults.ToProblem()`.
- `IClock` injecté (jamais `DateTime.Now`), IDs UUID v7 via `IdGenerator`. Montants en `decimal` / `numeric(18,2)`.
- Minimal APIs sous `/api/v1`, une route par cas d'usage. Tout endpoint est protégé par défaut ; permission métier via `.RequirePermission(Permissions.X)`. Énumérations renvoyées en texte (`JsonStringEnumConverter`).
- Commande qui touche à la fois les tables ASP.NET Identity et les nôtres, ou qui pose un verrou (`CountActiveSupervisorsForUpdateAsync`) : l'enregistrer avec `AddTransactional<TCommand, TResponse, THandler>`, ou `AddTransactional<TCommand, THandler>` pour une commande sans valeur de retour (décorateurs `TransactionalCommandHandler`).
- L'auteur d'une action (contrôle « soi-même ») est lu dans le JWT par `ClaimsPrincipal.TryGetAccountId` (`Hawala.Api/Configuration`) et passé à la commande en `ActorId`, jamais lu dans le corps de la requête.
- Valeurs d'énumération reçues en texte (rôle, statut) : `EnumNameParser.TryParse<T>` (insensible à la casse, refuse les nombres).
- Migrations : `dotnet ef migrations add <Nom> --project src/Modules/X/X.Infrastructure --startup-project src/Hawala.Api --context <XDbContext> --output-dir Persistence\Migrations`. Elles s'appliquent au démarrage (Organisation d'abord, puis Identité).
- Tests : xUnit, intégration via `WebApplicationFactory` (environnement `Testing`) + Testcontainers `new PostgreSqlBuilder("postgres:17")`, fixture de collection partagée (`ApiCollection`). Helpers : `TestAccounts`, `ApiClient` (lire les corps avec `ReadJsonAsync`, qui relit le texte).

## État actuel (18 octobre 2026)

Fait et testé sur PostgreSQL (dernier résultat confirmé : **575 réussis, 0 échec**) :

- SharedKernel (Result, Error, IClock, IdGenerator, Messaging, Currency MRO/XOF) ; `Money` (FCFA entier, MRO ≤ 2 décimales, saisie MRU ≤ 3 décimales × 10, montant trop précis refusé, MRO + XOF = exception) et `ExchangeRate` (MRO pour 5 000 FCFA, ≤ 2 décimales, `ToTransferMro` arrondi une fois en AwayFromZero).
- Identité : connexion email ou téléphone, verrouillage 5 échecs / 15 min, JWT 15 min (`sub`, `sv`, `pwd_change`, `jti`), refresh token rotatif 12 h en cookie `hawala_refresh` (haché, rejeu = révocation de la famille), logout, changement de mot de passe (ferme les autres sessions), `GET /api/v1/auth/me`, contrôle de la SecurityVersion à chaque requête (cache 30 s vidé par intercepteur EF), permissions côté serveur (option B), premier Superviseur créé depuis la configuration.
- Organisation : agences NKC et DKR créées au démarrage (jamais écrasées), `GET /api/v1/agencies`, `IAgencyDirectory` dans les Contracts, `POST /api/v1/agencies/{id}/change-color` (permission `agencies.manage`, couleur unique par agence, tests dans `tests/Identity.IntegrationTests/Organization/`).
- Comptes rattachés à une agence (`AgencyId`, interdit pour un Superviseur), migration `AddAccountAgency`.
- Gestion des comptes sous `/api/v1/users` (permission `users.manage`), tests dans `tests/Identity.IntegrationTests/Users/` :
  - `POST /users` : création, mot de passe temporaire généré, 201 + `{ id, temporaryPassword }` ;
  - `GET /users` (filtres `agencyId`, `role`, `status`, `search` ; tri par nom ; « verrouillé jusqu'à ») et `GET /users/{id}` ;
  - `POST /users/{id}/block` (coupe les sessions, verrou FOR UPDATE, pas soi-même) et `/unblock` (nouveau mot de passe temporaire, verrouillage levé) ;
  - `POST /users/{id}/change-role` (avec l'agence ; pas soi-même ; dernier Superviseur actif protégé) et `/change-agency` ;
  - `POST /users/{id}/reset-password` (nouveau mot de passe, verrouillage levé, sessions coupées ; pas soi-même ; un compte bloqué reste bloqué) ;
  - `PUT /users/{id}` : nom, email, téléphone (vide = retiré), sessions conservées.
- Exemples de requêtes pour les essais manuels : `src/Hawala.Api/Hawala.Api.http`.
- **Grand livre** (module `Ledger`, schéma `ledger`, migration `AddLedger`) : comptes (caisse, tiers, système par agence), écritures en partie double équilibrées par devise, contre-passation (motif obligatoire, une seule, jamais d'une contre-passation), soldes débit − crédit avec date `asOf`. Porte publique : `ILedger` (Ledger.Contracts), pas de route HTTP. En base : déclencheurs d'immuabilité (UPDATE / DELETE / TRUNCATE refusés, SQLSTATE 23001), équilibre par devise et deux lignes minimum au COMMIT (23514). Tests : `tests/Ledger.Domain.Tests`, `tests/Identity.IntegrationTests/Ledger/`.
- **`Hawala.SharedInfrastructure`** : `SharedConnection` (une connexion PostgreSQL par requête pour les modules d'argent, option A) et le décorateur de transaction commun : une commande qui écrit dans plusieurs schémas s'enregistre avec `AddSharedTransactional<…>`, et chaque `DbContext` d'un module d'argent implémente `ISharedTransactionParticipant`.
- **Périmètre par agence** : `IAgencyScope` (Identity.Contracts), rempli par `SessionValidator` à chaque requête ; filtre global EF Core dans les `DbContext` concernés (`IgnoreQueryFilters` seulement pour une règle qui vaut pour toute l'agence, comme l'unicité d'un nom). Sans session validée : aucune agence visible.
- **Trésorerie** (module `Treasury`, schéma `treasury`, migration `AddTreasury`) : `POST /api/v1/treasury-accounts` (permission `treasury.manage`) crée le compte et son compte au grand livre dans une même transaction ; nom unique par agence (sans tenir compte de la casse) ; devise = celle de l'agence. Tests : `tests/Treasury.Domain.Tests`, `tests/Identity.IntegrationTests/Treasury/`. `GET /api/v1/treasury-accounts` et `GET /api/v1/treasury-accounts/{id}` : périmètre par agence (404 hors agence), solde seulement avec `cash.budget.read`, comptes désactivés masqués sauf `includeInactive=true`. `PUT /api/v1/treasury-accounts/{id}` (renommer), `POST …/{id}/deactivate` (solde nul seulement, ferme aussi le compte au grand livre) et `POST …/{id}/reactivate` (rouvre les deux). Trésorerie complète pour la phase 2.
- **Tiers** (module `Parties`, schéma `parties`, migration `AddParties`) : `POST /api/v1/parties` (permission `parties.create`, donnée aussi au rôle Agent) crée le tiers, commun aux deux agences, et ouvre un compte au grand livre par agence (MRO à NKC, XOF à DKR) dans une même transaction ; téléphone (E.164) et NNI uniques ; nom non unique ; type = compte comptable (Client 41, Supplier 40, Staff 42, Other). Tests : `tests/Parties.Domain.Tests`, `tests/Identity.IntegrationTests/Parties/`. `GET /api/v1/parties` (recherche, type, `balanceSheet` All/In/Out, `balance` All/Red (عليه)/Green (له) sur une agence, archivés ; `count` / `totalCount` ; totaux rouge / vert / net par devise) et `GET /api/v1/parties/{id}` : soldes par agence seulement avec `parties.view_balance` et dans le périmètre. `PUT /api/v1/parties/{id}` (`parties.manage`, Superviseur) : état complet de la fiche, `inBalanceSheet` obligatoire. `PUT /api/v1/parties/{id}/agencies/{agencyId}/opening-balance` (`parties.manage`) : écriture d'ouverture de la différence contre « Soldes d'ouverture » de l'agence, à la date choisie (aujourd'hui par défaut), migration `AddPartyOpeningBalance`. `PUT /api/v1/parties/{id}/agencies/{agencyId}/credit-limit` (`parties.manage`) : positif ou nul, 0 = aucun crédit, null = pas de plafond ; affiché à côté du solde de chaque agence ; appliqué en phase 3. `POST /api/v1/parties/{id}/archive` et `/unarchive` (`parties.manage`) : archivage à solde nul dans les deux agences, ferme ses comptes au grand livre (rouverts au désarchivage), masqué de la liste sauf `includeArchived=true`. `GET /api/v1/parties/{id}/statement?agencyId=&from=&to=` (relevé ; `parties.view_balance` ; mois en cours par défaut), appuyé sur `ILedger.GetStatementAsync`. **Tiers complets pour la phase 2.**
- Règle de format des téléphones partagée : `PhoneNumbers.TryNormalizeE164` (SharedKernel) ; chaque module garde son type et ses codes d'erreur (`Identity.Phone.*`, `Parties.Phone.*`).
- **Taux du jour** (module `Rates`, schéma `rates`, migration `AddRates`) : un seul taux par date pour les deux agences, prix d'achat et prix de vente au format « 5 000 FCFA = ? MRO » ; `PUT /api/v1/rates/{date}` (`rates.set_daily`, Superviseur) ; `GET /api/v1/rates/today`, `/{date}`, `?from=&to=` (prix d'achat seulement avec `profit.view`). Tests : `tests/Rates.Domain.Tests`, `tests/Identity.IntegrationTests/Rates/`.
- **Transferts** (module `Transfers`, schéma `transfers`, migration `AddTransfers`) : `POST /api/v1/transfers` (permission `transfers.create`, Superviseur et Agent), en-tête `Idempotency-Key` obligatoire (unique par compte : la même clé renvoie 200 et le transfert existant, sans code). Paiement comptant réparti sur des comptes de trésorerie actifs de l'agence d'envoi, total = montant payé. Agence d'envoi = celle de l'agent (le Superviseur la choisit). Taux du jour de la date d'opération recopiés sur le transfert (refus `RateNotSet` sans taux). Montant MRO arrondi une fois. Code de retrait de 6 chiffres renvoyé une seule fois, stocké haché (PBKDF2). Référence provisoire `TR-{NKC|DKR}-{aaaammjj}-{000001}` (compteur par agence et par jour, POINT OUVERT sur le format de l'app actuelle). Écriture `Transfers.Created` : caisses / Position de change (devise d'envoi), Position de change / Transferts à livrer (devise de destination), dans la même transaction. Filtre de périmètre : transferts envoyés ou reçus par l'agence. Contracts ajoutés : `Rates.Contracts/IDailyRates`, `Treasury.Contracts/ITreasuryDirectory`. Tests : `tests/Transfers.Domain.Tests`, `tests/Identity.IntegrationTests/Transfers/`.
  **Crédit** (migration `AddTransferCredit`) : champ `creditPartyId` (permission `transfers.credit`, Superviseur et Agent) ; part à crédit = montant payé − total réparti (partielle ou totale, répartition vide permise ; refusée si nulle) ; débitée du tiers dans l'agence d'envoi (rouge) ; tiers archivé refusé ; plafond bloquant pour tous : solde après transfert ≤ plafond (`Transfers.Credit.LimitExceeded`, 409) ; compte du tiers verrouillé (FOR UPDATE) pendant la transaction. Porte publique `Parties.Contracts/IPartyCreditAccounts`.
  **Lecture** (pas de migration) : `GET /api/v1/transfers` (période sur la date d'opération, aujourd'hui par défaut ; `status`, `direction` Sent/Received, `agencyId` pour le Superviseur, `search` ILIKE échappé ; 50 par page, `page`, `totalCount`) et `GET /api/v1/transfers/{id}` (404 hors périmètre) ; tout compte connecté. `TransferViews` masque : prix d'achat et bénéfice (`Transfer.ProfitMro`, pleine précision) sans `profit.view` ; répartition et crédit pour l'agence de destination. Noms des tiers via `Parties.Contracts/IPartyDirectory`.
  **Livraison** (migration `AddTransferDelivery`) : `POST /api/v1/transfers/{id}/deliver` (permission `transfers.deliver` : Superviseur, Agent, Caissier) ; agence de destination seulement (`Transfers.Delivery.NotDestinationAgency`) ; transfert verrouillé (`GetForUpdateAsync`, FOR UPDATE) ; saisie vérifiée avant le code ; code faux compté et enregistré malgré le refus : le handler renvoie un `DeliveryOutcome` (succès technique qui porte le refus) pour que le décorateur valide le compteur, et l'API répond 400 `WrongCode` + `remainingAttempts`, puis 409 `CodeLocked` au 5e ; écriture `Transfers.Delivered` (Transferts à livrer / caisses de destination) ; caisses passées en négatif listées dans la réponse. `ErrorResults.ToProblem(error, extra)` ajoute des champs au ProblemDetails.
  **Cartes** (pas de migration) : `GET /api/v1/transfers/summary?agencyId=&from=&to=&search=` (`profit.view`) : `incoming`, `outgoing`, `sentByOther`, `deliveredByOther`, `notDelivered` (montant + nombre), `profitMro`, `saleRate` / `purchaseRate` du dernier jour ; calcul en mémoire (POINT OUVERT). La liste renvoie aussi `allCount` (« 24 / 392 »).
  **Annulation** (migration `AddTransferCancellation`) : `POST /api/v1/transfers/{id}/cancel` (`transfers.cancel`, Superviseur seulement) : motif obligatoire, non livré seulement, transfert verrouillé, `ILedger.ReverseAsync` de l'écriture de création à la date de l'annulation ; 204. Fiche : `cancellationOperationAt`, `cancellationReason`.
- Grand livre : **une date d'opération future est refusée** (`Ledger.Entry.FutureDate`, règle du 20 octobre 2026).
- `EnumNames.TryParse<T>` (SharedKernel) : lecture d'une énumération reçue en texte, pour tous les modules.
- La migration `AddLedger` contient du SQL ajouté à la main (déclencheurs) : si elle est régénérée, le remettre. `dotnet ef migrations remove` a besoin de la base locale (`docker compose up -d`).

## Prochaine étape

1. **Phase 2 terminée** (Money et arrondi, Grand livre, Trésorerie, Tiers). Prochaine : **phase 3, Taux et Transferts** — plan à présenter avant tout code. Droits par défaut des rôles validés le 20 octobre 2026 (tableau dans le skill ; `RolePermissions` à jour pour les permissions existantes). Reste à préciser : le sens des codes de la référence de l'app actuelle (`TR-RS18-HG01-2026-TGNC-…`).
   **Phase 3 en cours** : tours faits : taux du jour, créer (comptant), crédit, lister et lire, livrer (la livraison est passée avant les cartes, décision du 20 octobre 2026) ; cartes du Superviseur et annulation faites (21 octobre 2026) ; prochain : actions du Superviseur (débloquer le code, livrer sans code, nouveau code, modifier un non livré, historique), puis temps réel.

La CI (GitHub Actions) se fait **en dernier** (décision de Hamade). Reporté aussi : changer le nom d'une agence (le code NKC / DKR ne change jamais).

**Reporté, à discuter avec Hamade avant tout code** : 2FA TOTP du Superviseur (notamment : que peut faire un Superviseur qui ne l'a pas encore activée ?). Ne pas la proposer comme prochaine étape.

Ensuite : Money / arrondi / taux, Grand livre, Trésorerie, Tiers, Taux, Transferts, Caisse (budget journalier, contrôle de cohérence 5 %), Bénéfice, Bilan, Notifications, Audit, CI, déploiement. Renommer un jour `Identity.IntegrationTests` en `Hawala.IntegrationTests`.

## Limites connues acceptées

- Fenêtre rare de ≤ 30 s due au cache de session.
- Deux rafraîchissements simultanés peuvent réussir tous les deux : le frontend devra sérialiser les refresh.
- Course sur un index unique (email / téléphone) = 500 au lieu de 409.
- Audit de « qui a créé / bloqué / modifié le compte » : POINT OUVERT, à brancher avec le module Audit.
- Couleur d'agence unique vérifiée dans le handler, sans index unique en base (course très improbable entre deux Superviseurs) : POINT OUVERT dans `ChangeAgencyColorCommandHandler`.
- Grand livre : soldes recalculés à chaque lecture (pas de cache) ; date d'opération future non refusée (question ouverte) ; création simultanée d'un même compte système = 500 (index unique). POINT OUVERT dans le code.
- Liste des comptes sans pagination, recherche texte faite en mémoire (quelques dizaines de comptes) : POINT OUVERT dans `UserAccountRepository`.
- Le test de concurrence du blocage croisé appelle le handler directement (pas par HTTP) ; il éprouve le verrou de façon probable, pas certaine.

## Questions ouvertes pour le patron

- Autres alertes du contrôle de cohérence, en plus du « taux inhabituel ».
